namespace TypeScript.Compiler;

// ported from: src/compiler/factory/utilities.ts:183-1757
/// <summary>
/// The helpers that sit beside the <see cref="NodeFactory"/>'s <c>create*</c>/<c>update*</c> members: the compound-node
/// builders for JSX and object-literal lowering, the emission-name predicates, the <c>use strict</c> prologue and
/// outer-expression helpers, binding/assignment element analysis, the grammar and operator predicates the factory's
/// callers ask about, and the generated-name formatters.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS exports these as module-level functions of <c>factory/utilities.ts</c>; C# has no module-level
/// functions, so they are static members of a class named after the module. Where TS takes <c>factory: NodeFactory</c>
/// as its first parameter and calls <c>factory.createX(...)</c>, the port takes the same <see cref="NodeFactory"/> and
/// calls <c>factory.CreateX(...)</c>.
/// <para>
/// PORT-NOTE: the parts of the module whose dependencies are not in the port yet are left out rather than stubbed:
/// <c>createExpressionForAccessorDeclaration</c> and <c>createExpressionForObjectLiteralElementLike</c> (they need
/// <c>getAllAccessorDeclarations</c> from <c>utilities.ts</c>), <c>createExternalHelpersImportDeclarationIfNeeded</c>
/// and its helpers (they need <c>CompilerOptions</c>/<c>EmitHost</c>/<c>EmitResolver</c> and the module-kind helpers),
/// and <c>getJSDocTypeAssertionType</c> (M5). Each is named with its blocking dependency in the batch report.
/// </para>
/// <para>
/// PORT-NOTE: two TS helpers whose home is <c>utilities.ts</c> are reached through <see cref="Utilities"/> rather than
/// ported a second time here (<c>isAssignmentExpression</c>, <c>isAssignmentOperator</c>, <c>isPrologueDirective</c>,
/// <c>getOriginalNode</c>, <c>isGeneratedPrivateIdentifier</c>); the private precedence chain below is the only part of
/// this file that needed one of them (<c>isAssignmentOperator</c>).
/// </para>
/// </remarks>
internal static partial class FactoryUtilities
{
    // The parse-tree factory (TS declares it in parser.ts, factory/utilities.ts imports it).

    /// <summary>ported from: src/compiler/parser.ts:441 — <c>parseNodeFactory</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>parseNodeFactory</c> at module scope in <c>parser.ts</c> as
    /// <c>createNodeFactory(NodeFactoryFlags.NoParenthesizerRules, parseBaseNodeFactory)</c>, and
    /// <c>factory/utilities.ts</c> imports it for <see cref="CreateReactNamespace"/>, which needs an identifier the
    /// checker can resolve as if it came from the parse tree. The port exposes that same instance here so M4e's parser
    /// and this file share one factory: a second instance would hand out identifiers outside the tree the checker
    /// traverses.
    /// </remarks>
    public static NodeFactory ParseNodeFactory { get; } = new(NodeFactoryFlags.NoParenthesizerRules);

    // Compound nodes

    /// <summary>ported from: src/compiler/factory/utilities.ts:183-186 — <c>createEmptyExports</c>.</summary>
    public static ExportDeclaration CreateEmptyExports(NodeFactory factory)
        => factory.CreateExportDeclaration(null, false, factory.CreateNamedExports([]), null, null);

    /// <summary>ported from: src/compiler/factory/utilities.ts:188-202 — <c>createMemberAccessForPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types <c>memberName</c> as <c>PropertyName</c> and <c>location</c> as <c>TextRange</c>. Every
    /// member of that union is a node, so the port takes <see cref="Node"/> (the union member the emitter reads) and
    /// <see cref="ITextRange"/> (what a node's range is spelled as here).
    /// </remarks>
    public static MemberExpression CreateMemberAccessForPropertyName(
        NodeFactory factory,
        Expression target,
        Node memberName,
        ITextRange? location = null)
    {
        if (NodeTests.IsComputedPropertyName(memberName))
        {
            return Utilities.SetTextRange(
                factory.CreateElementAccessExpression(target, ((ComputedPropertyName)memberName).Expression),
                location);
        }

        // PORT-NOTE: TS passes the conditional straight into `setTextRange`; C# cannot infer the type argument of a
        // generic call from a conditional whose branches are two different node classes, so the common member type is
        // named first.
        MemberExpression access = Utilities.IsMemberName(memberName)
            ? factory.CreatePropertyAccessExpression(target, memberName)
            : factory.CreateElementAccessExpression(target, memberName);
        var expression = Utilities.SetTextRange(access, memberName);
        EmitNodes.AddEmitFlags(expression, EmitFlags.NoNestedSourceMaps);
        return expression;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:204-213 — <c>createReactNamespace</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>reactNamespace || "React"</c> is a JS truthiness test on a string, so an empty namespace
    /// falls back to <c>"React"</c>; the port asks <see cref="Js.IsTruthy(string?)"/> for the same answer.
    /// </remarks>
    private static Identifier CreateReactNamespace(string reactNamespace, Node parent)
    {
        // To ensure the emit resolver can properly resolve the namespace, we need to
        // treat this identifier as if it were a source tree node by clearing the `Synthesized`
        // flag and setting a parent node.
        var react = ParseNodeFactory.CreateIdentifier(Js.IsTruthy(reactNamespace) ? reactNamespace : "React");

        // Set the parent that is in parse tree
        // this makes sure that parent chain is intact for checker to traverse complete scope tree
        Utilities.SetParent(react, Utilities.GetParseTreeNode(parent));
        return react;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:215-226 — <c>createJsxFactoryExpressionFromEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Mutable&lt;Identifier&gt;</c> assertion exists so the second statement can restore the
    /// original <c>escapedText</c> after <c>createIdentifier</c> escapes leading underscores; the port's
    /// <see cref="Identifier.EscapedText"/> is settable, so the same two statements are written directly.
    /// </remarks>
    private static Expression CreateJsxFactoryExpressionFromEntityName(NodeFactory factory, Node jsxFactory, Node parent)
    {
        if (NodeTests.IsQualifiedName(jsxFactory))
        {
            var qualifiedName = (QualifiedName)jsxFactory;
            var left = CreateJsxFactoryExpressionFromEntityName(factory, qualifiedName.Left!, parent);
            var right = factory.CreateIdentifier(Utilities.IdText(qualifiedName.Right!));
            right.EscapedText = qualifiedName.Right!.EscapedText;
            return factory.CreatePropertyAccessExpression(left, right);
        }

        return CreateReactNamespace(Utilities.IdText(jsxFactory), parent);
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:228-235 — <c>createJsxFactoryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types <c>jsxFactoryEntity</c> as <c>EntityName | undefined</c> and <c>parent</c> as
    /// <c>JsxOpeningLikeElement | JsxOpeningFragment</c>; both unions have no C# type, so the port takes
    /// <see cref="Node"/> for each (§12.5).
    /// </remarks>
    public static Expression CreateJsxFactoryExpression(
        NodeFactory factory,
        Node? jsxFactoryEntity,
        string reactNamespace,
        Node parent)
        => jsxFactoryEntity is not null
            ? CreateJsxFactoryExpressionFromEntityName(factory, jsxFactoryEntity, parent)
            : factory.CreatePropertyAccessExpression(CreateReactNamespace(reactNamespace, parent), "createElement");

    /// <summary>ported from: src/compiler/factory/utilities.ts:237-245 — <c>createJsxFragmentFactoryExpression</c>.</summary>
    private static Expression CreateJsxFragmentFactoryExpression(
        NodeFactory factory,
        Node? jsxFragmentFactoryEntity,
        string reactNamespace,
        Node parent)
        => jsxFragmentFactoryEntity is not null
            ? CreateJsxFactoryExpressionFromEntityName(factory, jsxFragmentFactoryEntity, parent)
            : factory.CreatePropertyAccessExpression(CreateReactNamespace(reactNamespace, parent), "Fragment");

    /// <summary>ported from: src/compiler/factory/utilities.ts:247-278 — <c>createExpressionForJsxElement</c>.</summary>
    public static LeftHandSideExpression CreateExpressionForJsxElement(
        NodeFactory factory,
        Expression callee,
        Expression tagName,
        Expression? props,
        IList<Expression>? children,
        ITextRange location)
    {
        var argumentsList = new List<Expression> { tagName };
        if (props is not null)
        {
            argumentsList.Add(props);
        }

        if (children is not null && children.Count > 0)
        {
            if (props is null)
            {
                argumentsList.Add(factory.CreateNull());
            }

            if (children.Count > 1)
            {
                foreach (var child in children)
                {
                    StartOnNewLine(child);
                    argumentsList.Add(child);
                }
            }
            else
            {
                argumentsList.Add(children[0]);
            }
        }

        return Utilities.SetTextRange(
            factory.CreateCallExpression(callee, null, argumentsList),
            location);
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:280-307 — <c>createExpressionForJsxFragment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>children</c> as <c>readonly Expression[]</c> but still tests it, and callers in
    /// <c>transformers/jsx.ts</c> pass the element list straight through; the port keeps the test and takes the list as
    /// nullable.
    /// </remarks>
    public static LeftHandSideExpression CreateExpressionForJsxFragment(
        NodeFactory factory,
        Node? jsxFactoryEntity,
        Node? jsxFragmentFactoryEntity,
        string reactNamespace,
        IList<Expression>? children,
        Node parentElement,
        ITextRange location)
    {
        var tagName = CreateJsxFragmentFactoryExpression(factory, jsxFragmentFactoryEntity, reactNamespace, parentElement);
        var argumentsList = new List<Expression> { tagName, factory.CreateNull() };

        if (children is not null && children.Count > 0)
        {
            if (children.Count > 1)
            {
                foreach (var child in children)
                {
                    StartOnNewLine(child);
                    argumentsList.Add(child);
                }
            }
            else
            {
                argumentsList.Add(children[0]);
            }
        }

        return Utilities.SetTextRange(
            factory.CreateCallExpression(
                CreateJsxFactoryExpression(factory, jsxFactoryEntity, reactNamespace, parentElement),
                null,
                argumentsList),
            location);
    }

    // Utilities

    /// <summary>ported from: src/compiler/factory/utilities.ts:309-332 — <c>createForOfBindingStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ForInitializer</c> is <c>VariableDeclarationList | Expression</c>; the union has no C# type,
    /// so the port takes <see cref="Node"/> and narrows on the kind, and casts the expression branch for
    /// <c>CreateAssignment</c> (whose left side is an <see cref="Expression"/>).
    /// </remarks>
    public static IStatement CreateForOfBindingStatement(NodeFactory factory, Node node, Expression boundValue)
    {
        if (NodeTests.IsVariableDeclarationList(node))
        {
            var declarationList = (VariableDeclarationList)node;
            var firstDeclaration = Core.First(declarationList.Declarations!);
            var updatedDeclaration = factory.UpdateVariableDeclaration(
                firstDeclaration,
                firstDeclaration.Name,
                /*exclamationToken*/ null,
                /*type*/ null,
                boundValue);
            return Utilities.SetTextRange(
                factory.CreateVariableStatement(
                    /*modifiers*/ null,
                    factory.UpdateVariableDeclarationList(declarationList, [updatedDeclaration])),
                /*location*/ node);
        }
        else
        {
            var updatedExpression = Utilities.SetTextRange(
                factory.CreateAssignment((Expression)node, boundValue),
                /*location*/ node);
            return Utilities.SetTextRange(factory.CreateExpressionStatement(updatedExpression), /*location*/ node);
        }
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:334-346 — <c>createExpressionFromEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>EntityName | Expression</c> union has no C# type, so the port takes <see cref="Node"/> and
    /// answers the <see cref="Expression"/> the union's non-qualified member always is. TS's <c>as Mutable&lt;…&gt;</c>
    /// assertions on the two <c>setParent</c>/<c>setTextRange</c> nest are the same statements without the assertion.
    /// </remarks>
    public static Expression CreateExpressionFromEntityName(NodeFactory factory, Node node)
    {
        if (NodeTests.IsQualifiedName(node))
        {
            var qualifiedName = (QualifiedName)node;
            var left = CreateExpressionFromEntityName(factory, qualifiedName.Left!);
            // TODO(rbuckton): Does this need to be parented?
            var right = Utilities.SetParent(
                Utilities.SetTextRange(factory.CloneNode(qualifiedName.Right!), qualifiedName.Right),
                qualifiedName.Right!.Parent);
            return Utilities.SetTextRange(factory.CreatePropertyAccessExpression(left, right), node);
        }
        else
        {
            // TODO(rbuckton): Does this need to be parented?
            return (Expression)Utilities.SetParent(
                Utilities.SetTextRange(factory.CloneNode(node), node),
                node.Parent)!;
        }
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:348-360 — <c>createExpressionForPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types <c>memberName</c> as <c>Exclude&lt;PropertyName, PrivateIdentifier&gt;</c>, a union of node
    /// types; the port takes <see cref="Node"/> and narrows by kind, as elsewhere (§12.5).
    /// </remarks>
    public static Expression CreateExpressionForPropertyName(NodeFactory factory, Node memberName)
    {
        if (NodeTests.IsIdentifier(memberName))
        {
            return factory.CreateStringLiteralFromNode(memberName);
        }
        else if (NodeTests.IsComputedPropertyName(memberName))
        {
            // TODO(rbuckton): Does this need to be parented?
            var expression = ((ComputedPropertyName)memberName).Expression!;
            return Utilities.SetParent(
                Utilities.SetTextRange(factory.CloneNode(expression), expression),
                expression.Parent)!;
        }
        else
        {
            // TODO(rbuckton): Does this need to be parented?
            // PORT-NOTE: TS's `memberName` is a `PropertyName` minus `PrivateIdentifier`, and `cloneNode`/`setParent`
            // keep that static type; every member of that union is an expression, so the port says so once here and then
            // answers the `Expression` the method returns.
            var expression = (Expression)memberName;
            return Utilities.SetParent(
                Utilities.SetTextRange(factory.CloneNode(expression), expression),
                expression.Parent)!;
        }
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:411-422 — <c>createExpressionForPropertyAssignment</c>.</summary>
    private static Expression CreateExpressionForPropertyAssignment(
        NodeFactory factory,
        PropertyAssignment property,
        Expression receiver)
        => NodeFactory.SetOriginalNode(
            Utilities.SetTextRange(
                factory.CreateAssignment(
                    CreateMemberAccessForPropertyName(factory, receiver, property.Name!, /*location*/ property.Name),
                    property.Initializer),
                property),
            property);

    /// <summary>ported from: src/compiler/factory/utilities.ts:424-435 — <c>createExpressionForShorthandPropertyAssignment</c>.</summary>
    private static Expression CreateExpressionForShorthandPropertyAssignment(
        NodeFactory factory,
        ShorthandPropertyAssignment property,
        Expression receiver)
        => NodeFactory.SetOriginalNode(
            Utilities.SetTextRange(
                factory.CreateAssignment(
                    CreateMemberAccessForPropertyName(factory, receiver, property.Name!, /*location*/ property.Name),
                    factory.CloneNode(property.Name!)),
                /*location*/ property),
            /*original*/ property);

    /// <summary>ported from: src/compiler/factory/utilities.ts:437-461 — <c>createExpressionForMethodDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes <c>getModifiers(method)</c>, which for a method declaration is simply
    /// <c>method.modifiers</c> (<c>canHaveModifiers</c> is true for every method). The factory's
    /// <c>CreateFunctionExpression</c> takes <c>List&lt;ModifierToken&gt;</c> while a parsed node stores its modifiers
    /// as <c>NodeArray&lt;Node&gt;</c> — <c>ModifierLike</c> collapses to <c>Node</c> here (§12.5) — so
    /// <see cref="AsModifierTokens"/> performs the narrowing.
    /// </remarks>
    private static Expression CreateExpressionForMethodDeclaration(
        NodeFactory factory,
        MethodDeclaration method,
        Expression receiver)
        => NodeFactory.SetOriginalNode(
            Utilities.SetTextRange(
                factory.CreateAssignment(
                    CreateMemberAccessForPropertyName(factory, receiver, method.Name!, /*location*/ method.Name),
                    NodeFactory.SetOriginalNode(
                        Utilities.SetTextRange(
                            factory.CreateFunctionExpression(
                                AsModifierTokens(method.Modifiers),
                                method.AsteriskToken,
                                /*name*/ null,
                                /*typeParameters*/ null,
                                method.Parameters,
                                /*type*/ null,
                                method.Body!),
                            /*location*/ method),
                        /*original*/ method)),
                /*location*/ method),
            /*original*/ method);

    /// <summary>ported from: src/compiler/factory/utilities.ts:516-554 — <c>expandPreOrPostfixIncrementOrDecrementExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>PrefixUnaryExpression | PostfixUnaryExpression</c> union has no C# type, so the port takes
    /// <see cref="Node"/> and reads <c>operator</c>/<c>operand</c> through the kind switch. TS's
    /// <c>Debug.assert(operator === PlusPlusToken || operator === MinusMinusToken, …)</c> catches a node of any other
    /// kind, which is why the switch's fallback value is <c>Unknown</c> rather than a silent guess.
    /// </remarks>
    public static Expression ExpandPreOrPostfixIncrementOrDecrementExpression(
        NodeFactory factory,
        Node node,
        Expression expression,
        Action<Identifier> recordTempVariable,
        Identifier? resultVariable)
    {
        var @operator = node switch
        {
            PrefixUnaryExpression prefix => prefix.Operator,
            PostfixUnaryExpression postfix => postfix.Operator,
            _ => SyntaxKind.Unknown,
        };
        Debug.Assert(
            @operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken,
            "Expected 'node' to be a pre- or post-increment or pre- or post-decrement expression");

        var temp = factory.CreateTempVariable(recordTempVariable, /*reservedInNestedScopes*/ null);
        var assignment = factory.CreateAssignment(temp, expression);
        Utilities.SetTextRange(assignment, OperandOf(node));

        Expression operation = node is PrefixUnaryExpression
            ? factory.CreatePrefixUnaryExpression(@operator, temp)
            : factory.CreatePostfixUnaryExpression(temp, @operator);
        Utilities.SetTextRange(operation, node);

        if (resultVariable is not null)
        {
            operation = factory.CreateAssignment(resultVariable, operation);
            Utilities.SetTextRange(operation, node);
        }

        Expression result = factory.CreateComma(assignment, operation);
        Utilities.SetTextRange(result, node);

        if (node is PostfixUnaryExpression)
        {
            result = factory.CreateComma(result, temp);
            Utilities.SetTextRange(result, node);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:556-558 — <c>isInternalName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the parameter as <c>Identifier</c>; the port's callers hold a <see cref="Node"/> (a
    /// postfix operand, for instance) and the question only reads the emit flags, so the parameter is the wider type
    /// (the same shape the neighbouring <c>Utilities.IsGeneratedIdentifier</c> uses).
    /// </remarks>
    public static bool IsInternalName(Node node) => (Utilities.GetEmitFlags(node) & EmitFlags.InternalName) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:565-567 — <c>isLocalName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isLocalName</c> is <c>(getEmitFlags(node) &amp; EmitFlags.LocalName) !== 0</c>, with no
    /// relation to the identifiers <c>arguments</c> and <c>this</c>. The already-committed
    /// <c>Utilities.IsLocalName</c> answers a different question and carries the wrong provenance comment; this is the
    /// correct body, and the two call sites in <c>NodeFactory.cs</c> are being repointed at it. As with
    /// <see cref="IsInternalName"/>, the parameter is <see cref="Node"/> rather than <c>Identifier</c>.
    /// </remarks>
    public static bool IsLocalName(Node node) => (Utilities.GetEmitFlags(node) & EmitFlags.LocalName) != 0;

    /// <summary>ported from: src/compiler/factory/utilities.ts:575-577 — <c>isExportName</c>.</summary>
    public static bool IsExportName(Node node) => (Utilities.GetEmitFlags(node) & EmitFlags.ExportName) != 0;

    /// <summary>ported from: src/compiler/factory/utilities.ts:579-581 — <c>isUseStrictPrologue</c>.</summary>
    private static bool IsUseStrictPrologue(ExpressionStatement node)
        => NodeTests.IsStringLiteral(node.Expression!) && ((StringLiteral)node.Expression!).Text == "use strict";

    /// <summary>ported from: src/compiler/factory/utilities.ts:584-596 — <c>findUseStrictPrologue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>Statement</c> is an interface and <see cref="Node"/> a class in the port (§12.9), so passing a
    /// statement to <c>isPrologueDirective</c> needs the widening cast TS does not write.
    /// </remarks>
    public static IStatement? FindUseStrictPrologue(IList<IStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (Utilities.IsPrologueDirective((Node)statement))
            {
                if (IsUseStrictPrologue((ExpressionStatement)statement))
                {
                    return statement;
                }
            }
            else
            {
                break;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:599-604 — <c>startsWithUseStrict</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isPrologueDirective</c> narrows to <c>PrologueDirective</c> (an expression statement whose
    /// expression is a string literal) in TS; the port's predicate answers with a <c>bool</c>, so the statement is
    /// narrowed at the call.
    /// </remarks>
    public static bool StartsWithUseStrict(IList<IStatement> statements)
    {
        var firstStatement = Core.FirstOrUndefined(statements);
        return firstStatement is not null
            && Utilities.IsPrologueDirective((Node)firstStatement)
            && IsUseStrictPrologue((ExpressionStatement)firstStatement);
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:607-609 — <c>isCommaExpression</c>.</summary>
    public static bool IsCommaExpression(Expression node)
        => node.Kind == SyntaxKind.BinaryExpression
            && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.CommaToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:612-614 — <c>isCommaSequence</c>.</summary>
    public static bool IsCommaSequence(Expression node)
        => IsCommaExpression(node) || NodeTests.IsCommaListExpression(node);

    /// <summary>ported from: src/compiler/factory/utilities.ts:617-621 — <c>isJSDocTypeAssertion</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this member was a loud stub while <c>getJSDocTypeTag</c> was unported (§15.8's choice of a throw over a
    /// placeholder answer). That dependency has since landed (Utilities/Utilities.JSDoc.cs), and M5-2's
    /// <c>isNarrowingExpression</c> is the binder's own call site of this predicate (binder.ts:1252), so the reference
    /// body is stated now. The <c>isInJSFile</c> test is the reference's second conjunct and is what keeps the predicate
    /// from looking at JSDoc on a TS file's parentheses.
    /// </remarks>
    public static bool IsJSDocTypeAssertion(Node node)
        => NodeTests.IsParenthesizedExpression(node)
            && Utilities.IsInJSFile(node)
            && Utilities.GetJSDocTypeTag(node) is not null;

    /// <summary>ported from: src/compiler/factory/utilities.ts:631-651 — <c>isOuterExpression</c>.</summary>
    public static bool IsOuterExpression(Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All) => node.Kind switch
    {
        SyntaxKind.ParenthesizedExpression =>
            (kinds & OuterExpressionKinds.ExcludeJSDocTypeAssertion) != 0 && IsJSDocTypeAssertion(node)
                ? false
                : (kinds & OuterExpressionKinds.Parentheses) != 0,
        SyntaxKind.TypeAssertionExpression or SyntaxKind.AsExpression =>
            (kinds & OuterExpressionKinds.TypeAssertions) != 0,
        SyntaxKind.SatisfiesExpression =>
            (kinds & (OuterExpressionKinds.TypeAssertions | OuterExpressionKinds.Satisfies)) != 0,
        SyntaxKind.ExpressionWithTypeArguments =>
            (kinds & OuterExpressionKinds.ExpressionsWithTypeArguments) != 0,
        SyntaxKind.NonNullExpression =>
            (kinds & OuterExpressionKinds.NonNullAssertions) != 0,
        SyntaxKind.PartiallyEmittedExpression =>
            (kinds & OuterExpressionKinds.PartiallyEmittedExpressions) != 0,
        _ => false,
    };

    /// <summary>ported from: src/compiler/factory/utilities.ts:660-666 — <c>skipOuterExpressions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares four overloads — a generic one that keeps the caller's <c>WrappedExpression&lt;T&gt;</c>
    /// type, one for <c>Expression</c>, and the <c>Node</c> one — because TS's structural typing lets the result be
    /// widened at the call. The port declares the <c>Node</c> one; callers that need the narrower type cast, as
    /// <c>Utilities.SkipParentheses</c> and <c>Utilities.SkipPartiallyEmittedExpressions</c> already do.
    /// </remarks>
    public static Node SkipOuterExpressions(Node node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        while (IsOuterExpression(node, kinds))
        {
            node = OuterExpressionOf(node)!;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:668-675 — <c>walkUpOuterExpressions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's declared result is <c>Node</c>, but the parent chain it walks can end in <c>undefined</c> (TS's
    /// <c>node.parent</c> is typed as non-optional and is not). The port answers <c>Node?</c> and asserts the same
    /// invariant TS asserts inside the loop.
    /// </remarks>
    public static Node? WalkUpOuterExpressions(Expression node, OuterExpressionKinds kinds = OuterExpressionKinds.All)
    {
        var parent = node.Parent;
        while (IsOuterExpression(parent!, kinds))
        {
            parent = parent!.Parent;
            Debug.Assert(parent is not null);
        }

        return parent;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:678-680 — <c>startOnNewLine</c>.</summary>
    public static T StartOnNewLine<T>(T node)
        where T : Node => EmitNodes.SetStartsOnNewLine(node, /*newLine*/ true);

    /// <summary>ported from: src/compiler/factory/utilities.ts:683-687 — <c>getExternalHelpersModuleName</c>.</summary>
    public static Identifier? GetExternalHelpersModuleName(SourceFile node)
    {
        var parseNode = Utilities.GetOriginalNode<SourceFile>(node, NodeTests.IsSourceFile);
        var emitNode = parseNode?.EmitNode;
        return emitNode?.ExternalHelpersModuleName;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:690-694 — <c>hasRecordedExternalHelpers</c>.</summary>
    public static bool HasRecordedExternalHelpers(SourceFile sourceFile)
    {
        var parseNode = Utilities.GetOriginalNode<SourceFile>(sourceFile, NodeTests.IsSourceFile);
        var emitNode = parseNode?.EmitNode;
        return emitNode is not null
            && (emitNode.ExternalHelpersModuleName is not null || emitNode.ExternalHelpers == true);
    }

    //
    // Binding and assignment element analysis
    //

    /// <summary>ported from: src/compiler/factory/utilities.ts:865-903 — <c>getInitializerOfBindingOrAssignmentElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BindingOrAssignmentElement</c> is a large union of node types; the port takes
    /// <see cref="Node"/> and narrows by kind, since a union has no C# type (§12.5). The declaration-element branch
    /// reads <c>initializer</c> from the three kinds <c>isDeclarationBindingElement</c> admits.
    /// </remarks>
    public static Expression? GetInitializerOfBindingOrAssignmentElement(Node bindingElement)
    {
        if (Utilities.IsDeclarationBindingElement(bindingElement))
        {
            // `1` in `let { a = 1 } = ...`
            // `1` in `let { a: b = 1 } = ...`
            // `1` in `let { a: {b} = 1 } = ...`
            // `1` in `let { a: [b] = 1 } = ...`
            // `1` in `let [a = 1] = ...`
            // `1` in `let [{a} = 1] = ...`
            // `1` in `let [[a] = 1] = ...`
            return bindingElement switch
            {
                VariableDeclaration declaration => declaration.Initializer,
                ParameterDeclaration parameter => parameter.Initializer,
                _ => ((BindingElement)bindingElement).Initializer,
            };
        }

        if (NodeTests.IsPropertyAssignment(bindingElement))
        {
            // `1` in `({ a: b = 1 } = ...)`
            // `1` in `({ a: {b} = 1 } = ...)`
            // `1` in `({ a: [b] = 1 } = ...)`
            var initializer = ((PropertyAssignment)bindingElement).Initializer;
            return Utilities.IsAssignmentExpression(initializer!, /*excludeCompoundAssignment*/ true)
                ? ((BinaryExpression)initializer!).Right
                : null;
        }

        if (NodeTests.IsShorthandPropertyAssignment(bindingElement))
        {
            // `1` in `({ a = 1 } = ...)`
            return ((ShorthandPropertyAssignment)bindingElement).ObjectAssignmentInitializer;
        }

        if (Utilities.IsAssignmentExpression(bindingElement, /*excludeCompoundAssignment*/ true))
        {
            // `1` in `[a = 1] = ...`
            // `1` in `[{a} = 1] = ...`
            // `1` in `[[a] = 1] = ...`
            return ((BinaryExpression)bindingElement).Right;
        }

        if (NodeTests.IsSpreadElement(bindingElement))
        {
            // Recovery consistent with existing emit.
            return GetInitializerOfBindingOrAssignmentElement(((SpreadElement)bindingElement).Expression!);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:910-984 — <c>getTargetOfBindingOrAssignmentElement</c>.</summary>
    public static Node? GetTargetOfBindingOrAssignmentElement(Node bindingElement)
    {
        if (Utilities.IsDeclarationBindingElement(bindingElement))
        {
            // `a` in `let { a } = ...`
            // `a` in `let { a = 1 } = ...`
            // `b` in `let { a: b } = ...`
            // `b` in `let { a: b = 1 } = ...`
            // `a` in `let { ...a } = ...`
            // `{b}` in `let { a: {b} } = ...`
            // `{b}` in `let { a: {b} = 1 } = ...`
            // `[b]` in `let { a: [b] } = ...`
            // `[b]` in `let { a: [b] = 1 } = ...`
            // `a` in `let [a] = ...`
            // `a` in `let [a = 1] = ...`
            // `a` in `let [...a] = ...`
            // `{a}` in `let [{a}] = ...`
            // `{a}` in `let [{a} = 1] = ...`
            // `[a]` in `let [[a]] = ...`
            // `[a]` in `let [[a] = 1] = ...`
            return bindingElement switch
            {
                VariableDeclaration declaration => declaration.Name,
                ParameterDeclaration parameter => parameter.Name,
                _ => ((BindingElement)bindingElement).Name,
            };
        }

        if (Utilities.IsObjectLiteralElementLike(bindingElement))
        {
            switch (bindingElement.Kind)
            {
                case SyntaxKind.PropertyAssignment:
                    // `b` in `({ a: b } = ...)`
                    // `b` in `({ a: b = 1 } = ...)`
                    // `{b}` in `({ a: {b} } = ...)`
                    // `{b}` in `({ a: {b} = 1 } = ...)`
                    // `[b]` in `({ a: [b] } = ...)`
                    // `[b]` in `({ a: [b] = 1 } = ...)`
                    // `b.c` in `({ a: b.c } = ...)`
                    // `b.c` in `({ a: b.c = 1 } = ...)`
                    // `b[0]` in `({ a: b[0] } = ...)`
                    // `b[0]` in `({ a: b[0] = 1 } = ...)`
                    return GetTargetOfBindingOrAssignmentElement(((PropertyAssignment)bindingElement).Initializer!);

                case SyntaxKind.ShorthandPropertyAssignment:
                    // `a` in `({ a } = ...)`
                    // `a` in `({ a = 1 } = ...)`
                    return ((ShorthandPropertyAssignment)bindingElement).Name;

                case SyntaxKind.SpreadAssignment:
                    // `a` in `({ ...a } = ...)`
                    return GetTargetOfBindingOrAssignmentElement(((SpreadAssignment)bindingElement).Expression!);
            }

            // no target
            return null;
        }

        if (Utilities.IsAssignmentExpression(bindingElement, /*excludeCompoundAssignment*/ true))
        {
            // `a` in `[a = 1] = ...`
            // `{a}` in `[{a} = 1] = ...`
            // `[a]` in `[[a] = 1] = ...`
            // `a.b` in `[a.b = 1] = ...`
            // `a[0]` in `[a[0] = 1] = ...`
            return GetTargetOfBindingOrAssignmentElement(((BinaryExpression)bindingElement).Left!);
        }

        if (NodeTests.IsSpreadElement(bindingElement))
        {
            // `a` in `[...a] = ...`
            return GetTargetOfBindingOrAssignmentElement(((SpreadElement)bindingElement).Expression!);
        }

        // `a` in `[a] = ...`
        // `{a}` in `[{a}] = ...`
        // `[a]` in `[[a]] = ...`
        // `a.b` in `[a.b] = ...`
        // `a[0]` in `[a[0]] = ...`
        return bindingElement;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:987-1001 — <c>getRestIndicatorOfBindingOrAssignmentElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers a union of <c>PunctuationToken</c>, <c>SpreadElement</c> and <c>SpreadAssignment</c>;
    /// the port answers the <see cref="Node"/> all three are.
    /// </remarks>
    public static Node? GetRestIndicatorOfBindingOrAssignmentElement(Node bindingElement) => bindingElement.Kind switch
    {
        // `...` in `let [...a] = ...`
        SyntaxKind.Parameter => ((ParameterDeclaration)bindingElement).DotDotDotToken,
        SyntaxKind.BindingElement => ((BindingElement)bindingElement).DotDotDotToken,

        // `...` in `[...a] = ...`
        SyntaxKind.SpreadElement or SyntaxKind.SpreadAssignment => bindingElement,

        _ => null,
    };

    /// <summary>ported from: src/compiler/factory/utilities.ts:1008-1012 — <c>getPropertyNameOfBindingOrAssignmentElement</c>.</summary>
    public static Node? GetPropertyNameOfBindingOrAssignmentElement(Node bindingElement)
    {
        var propertyName = TryGetPropertyNameOfBindingOrAssignmentElement(bindingElement);
        Debug.Assert(
            propertyName is not null || NodeTests.IsSpreadAssignment(bindingElement),
            "Invalid property name for binding element.");
        return propertyName;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1015-1063 — <c>tryGetPropertyNameOfBindingOrAssignmentElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reaches <c>Debug.failBadSyntaxKind(propertyName)</c> in two branches, and that helper is listed
    /// as unported (§10.4: it needs <c>formatSyntaxKind</c>). Rather than inventing a <c>Debug</c> member or printing a
    /// different message, <see cref="FailBadSyntaxKind"/> reproduces the helper's observable behaviour for these call
    /// sites: it fails with the same <c>"Unexpected node.\r\nNode &lt;kind&gt; was unexpected."</c> text that
    /// <c>debug.ts:206-211</c> builds (the port's <c>Debug.Fail</c> already throws
    /// <see cref="InvalidOperationException"/> where TS throws its own failure).
    /// </remarks>
    public static Node? TryGetPropertyNameOfBindingOrAssignmentElement(Node bindingElement)
    {
        switch (bindingElement.Kind)
        {
            case SyntaxKind.BindingElement:
                {
                    // `a` in `let { a: b } = ...`
                    // `[a]` in `let { [a]: b } = ...`
                    // `"a"` in `let { "a": b } = ...`
                    // `1` in `let { 1: b } = ...`
                    if (((BindingElement)bindingElement).PropertyName is { } elementPropertyName)
                    {
                        if (NodeTests.IsPrivateIdentifier(elementPropertyName)) return FailBadSyntaxKind(elementPropertyName);
                        return NodeTests.IsComputedPropertyName(elementPropertyName)
                            && IsStringOrNumericLiteral(((ComputedPropertyName)elementPropertyName).Expression!)
                            ? ((ComputedPropertyName)elementPropertyName).Expression
                            : elementPropertyName;
                    }

                    break;
                }

            case SyntaxKind.PropertyAssignment:
                {
                    // `a` in `({ a: b } = ...)`
                    // `[a]` in `({ [a]: b } = ...)`
                    // `"a"` in `({ "a": b } = ...)`
                    // `1` in `({ 1: b } = ...)`
                    if (((PropertyAssignment)bindingElement).Name is { } assignmentName)
                    {
                        if (NodeTests.IsPrivateIdentifier(assignmentName)) return FailBadSyntaxKind(assignmentName);
                        return NodeTests.IsComputedPropertyName(assignmentName)
                            && IsStringOrNumericLiteral(((ComputedPropertyName)assignmentName).Expression!)
                            ? ((ComputedPropertyName)assignmentName).Expression
                            : assignmentName;
                    }

                    break;
                }

            case SyntaxKind.SpreadAssignment:
                {
                    // `a` in `({ ...a } = ...)`
                    var spreadName = ((SpreadAssignment)bindingElement).Name;
                    if (spreadName is not null && NodeTests.IsPrivateIdentifier(spreadName)) return FailBadSyntaxKind(spreadName);
                    return spreadName;
                }
        }

        var target = GetTargetOfBindingOrAssignmentElement(bindingElement);
        if (target is not null && Utilities.IsPropertyName(target))
        {
            return target;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1065-1069 — <c>isStringOrNumericLiteral</c>.</summary>
    private static bool IsStringOrNumericLiteral(Node node)
        => node.Kind is SyntaxKind.StringLiteral or SyntaxKind.NumericLiteral;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1076-1090 — <c>getElementsOfBindingOrAssignmentPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>readonly BindingOrAssignmentElement[]</c> and reaches it through three different
    /// source arrays — <c>NodeArray&lt;Node&gt;</c>, <c>NodeArray&lt;BindingElement&gt;</c> and
    /// <c>NodeArray&lt;Expression&gt;</c> — relying on TS's array covariance. C# has no array covariance, so the port
    /// answers the covariant <c>IReadOnlyList&lt;Node&gt;</c>: every TS call site either iterates it or indexes it.
    /// <para>
    /// PORT-NOTE: TS's switch has no default, so a node outside the four kinds makes it answer <c>undefined</c> behind a
    /// non-optional return type. The port fails loudly instead.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Node> GetElementsOfBindingOrAssignmentPattern(Node name) => name switch
    {
        // `a` in `{a}`
        // `a` in `[a]`
        ObjectBindingPattern objectBindingPattern => objectBindingPattern.Elements!,
        ArrayBindingPattern arrayBindingPattern => arrayBindingPattern.Elements!,
        ArrayLiteralExpression arrayLiteral => arrayLiteral.Elements!,

        // `a` in `{a}`
        ObjectLiteralExpression objectLiteral => objectLiteral.Properties!,

        _ => throw new InvalidOperationException($"Unexpected syntax kind: {name.Kind}"),
    };

    /// <summary>ported from: src/compiler/factory/utilities.ts:1092-1103 — <c>getJSDocTypeAliasName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>JSDocNamespaceBody</c> is <c>Identifier | JSDocNamespaceDeclaration</c>;
    /// <c>JSDocNamespaceDeclaration</c> is an empty interface in the generated model that nothing implements, so the
    /// port narrows to the concrete <see cref="ModuleDeclaration"/> the parser builds for it (the same choice the
    /// hand-written half of <see cref="NodeFactory"/> makes for its private copy of this helper).
    /// </remarks>
    public static Identifier? GetJSDocTypeAliasName(Node? fullName)
    {
        if (fullName is null) return null;

        var rightNode = fullName;
        while (true)
        {
            if (rightNode is Identifier identifier) return identifier;

            var declaration = (ModuleDeclaration)rightNode;
            if (declaration.Body is null) return declaration.Name as Identifier;

            rightNode = declaration.Body;
        }
    }

    //
    // Grammar-error predicates and operator classification
    //

    /// <summary>ported from: src/compiler/factory/utilities.ts:1105-1110 — <c>canHaveIllegalType</c>.</summary>
    public static bool CanHaveIllegalType(Node node)
        => node.Kind is SyntaxKind.Constructor or SyntaxKind.SetAccessor;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1112-1118 — <c>canHaveIllegalTypeParameters</c>.</summary>
    public static bool CanHaveIllegalTypeParameters(Node node)
        => node.Kind is SyntaxKind.Constructor or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1120-1140 — <c>canHaveIllegalDecorators</c>.</summary>
    public static bool CanHaveIllegalDecorators(Node node) => node.Kind switch
    {
        SyntaxKind.PropertyAssignment
            or SyntaxKind.ShorthandPropertyAssignment
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.Constructor
            or SyntaxKind.IndexSignature
            or SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.MissingDeclaration
            or SyntaxKind.VariableStatement
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.ImportDeclaration
            or SyntaxKind.NamespaceExportDeclaration
            or SyntaxKind.ExportDeclaration
            or SyntaxKind.ExportAssignment => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/factory/utilities.ts:1142-1149 — <c>canHaveIllegalModifiers</c>.</summary>
    public static bool CanHaveIllegalModifiers(Node node) => node.Kind switch
    {
        SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.PropertyAssignment
            or SyntaxKind.ShorthandPropertyAssignment
            or SyntaxKind.MissingDeclaration
            or SyntaxKind.NamespaceExportDeclaration => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/factory/utilities.ts:1151-1153 — <c>isQuestionOrExclamationToken</c>.</summary>
    public static bool IsQuestionOrExclamationToken(Node node)
        => NodeTests.IsQuestionToken(node) || NodeTests.IsExclamationToken(node);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1155-1157 — <c>isIdentifierOrThisTypeNode</c>.</summary>
    public static bool IsIdentifierOrThisTypeNode(Node node)
        => NodeTests.IsIdentifier(node) || NodeTests.IsThisTypeNode(node);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1159-1161 — <c>isReadonlyKeywordOrPlusOrMinusToken</c>.</summary>
    public static bool IsReadonlyKeywordOrPlusOrMinusToken(Node node)
        => NodeTests.IsReadonlyKeyword(node) || NodeTests.IsPlusToken(node) || NodeTests.IsMinusToken(node);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1163-1165 — <c>isQuestionOrPlusOrMinusToken</c>.</summary>
    public static bool IsQuestionOrPlusOrMinusToken(Node node)
        => NodeTests.IsQuestionToken(node) || NodeTests.IsPlusToken(node) || NodeTests.IsMinusToken(node);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1167-1169 — <c>isModuleName</c>.</summary>
    public static bool IsModuleName(Node node) => NodeTests.IsIdentifier(node) || NodeTests.IsStringLiteral(node);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1171-1173 — <c>isExponentiationOperator</c>.</summary>
    private static bool IsExponentiationOperator(SyntaxKind kind)
        => kind == SyntaxKind.AsteriskAsteriskToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1175-1179 — <c>isMultiplicativeOperator</c>.</summary>
    private static bool IsMultiplicativeOperator(SyntaxKind kind)
        => kind is SyntaxKind.AsteriskToken or SyntaxKind.SlashToken or SyntaxKind.PercentToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1181-1184 — <c>isMultiplicativeOperatorOrHigher</c>.</summary>
    private static bool IsMultiplicativeOperatorOrHigher(SyntaxKind kind)
        => IsExponentiationOperator(kind) || IsMultiplicativeOperator(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1186-1189 — <c>isAdditiveOperator</c>.</summary>
    private static bool IsAdditiveOperator(SyntaxKind kind)
        => kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1191-1194 — <c>isAdditiveOperatorOrHigher</c>.</summary>
    private static bool IsAdditiveOperatorOrHigher(SyntaxKind kind)
        => IsAdditiveOperator(kind) || IsMultiplicativeOperatorOrHigher(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1196-1201 — <c>isShiftOperator</c>.</summary>
    private static bool IsShiftOperator(SyntaxKind kind)
        => kind is SyntaxKind.LessThanLessThanToken
            or SyntaxKind.GreaterThanGreaterThanToken
            or SyntaxKind.GreaterThanGreaterThanGreaterThanToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1203-1206 — <c>isShiftOperatorOrHigher</c>.</summary>
    public static bool IsShiftOperatorOrHigher(SyntaxKind kind)
        => IsShiftOperator(kind) || IsAdditiveOperatorOrHigher(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1208-1215 — <c>isRelationalOperator</c>.</summary>
    private static bool IsRelationalOperator(SyntaxKind kind)
        => kind is SyntaxKind.LessThanToken
            or SyntaxKind.LessThanEqualsToken
            or SyntaxKind.GreaterThanToken
            or SyntaxKind.GreaterThanEqualsToken
            or SyntaxKind.InstanceOfKeyword
            or SyntaxKind.InKeyword;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1217-1220 — <c>isRelationalOperatorOrHigher</c>.</summary>
    private static bool IsRelationalOperatorOrHigher(SyntaxKind kind)
        => IsRelationalOperator(kind) || IsShiftOperatorOrHigher(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1222-1227 — <c>isEqualityOperator</c>.</summary>
    private static bool IsEqualityOperator(SyntaxKind kind)
        => kind is SyntaxKind.EqualsEqualsToken
            or SyntaxKind.EqualsEqualsEqualsToken
            or SyntaxKind.ExclamationEqualsToken
            or SyntaxKind.ExclamationEqualsEqualsToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1229-1232 — <c>isEqualityOperatorOrHigher</c>.</summary>
    private static bool IsEqualityOperatorOrHigher(SyntaxKind kind)
        => IsEqualityOperator(kind) || IsRelationalOperatorOrHigher(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1234-1238 — <c>isBitwiseOperator</c>.</summary>
    private static bool IsBitwiseOperator(SyntaxKind kind)
        => kind is SyntaxKind.AmpersandToken or SyntaxKind.BarToken or SyntaxKind.CaretToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1240-1244 — <c>isBitwiseOperatorOrHigher</c>.</summary>
    private static bool IsBitwiseOperatorOrHigher(SyntaxKind kind)
        => IsBitwiseOperator(kind) || IsEqualityOperatorOrHigher(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1246-1249 — <c>isLogicalOperator</c>.</summary>
    /// <remarks>
    /// NOTE (from the TS source): the version in <c>utilities.ts</c> includes <c>ExclamationToken</c>, which is not a
    /// binary operator.
    /// </remarks>
    private static bool IsLogicalOperator(SyntaxKind kind)
        => kind is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1251-1254 — <c>isLogicalOperatorOrHigher</c>.</summary>
    private static bool IsLogicalOperatorOrHigher(SyntaxKind kind)
        => IsLogicalOperator(kind) || IsBitwiseOperatorOrHigher(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1256-1260 — <c>isAssignmentOperatorOrHigher</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>isAssignmentOperator</c> this chain calls is <c>utilities.ts:7435</c>'s, which the port
    /// already provides as <see cref="Utilities.IsAssignmentOperator"/>; it is not duplicated here.
    /// </remarks>
    private static bool IsAssignmentOperatorOrHigher(SyntaxKind kind)
        => kind == SyntaxKind.QuestionQuestionToken
            || IsLogicalOperatorOrHigher(kind)
            || Utilities.IsAssignmentOperator(kind);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1262-1265 — <c>isBinaryOperator</c>.</summary>
    private static bool IsBinaryOperator(SyntaxKind kind)
        => IsAssignmentOperatorOrHigher(kind) || kind == SyntaxKind.CommaToken;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1267-1269 — <c>isBinaryOperatorToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BinaryOperatorToken</c> is <c>Token&lt;BinaryOperator&gt;</c>; the port's token model has one
    /// <see cref="Token"/> class per kind family, so the parameter here and the trampoline's <c>onOperator</c> callback
    /// both take <see cref="Token"/> — which is what <c>UpdateBinaryExpression</c> takes too.
    /// </remarks>
    public static bool IsBinaryOperatorToken(Node node) => IsBinaryOperator(node.Kind);

    //
    // The binary-expression trampoline
    //

    /// <summary>Which side of a <c>BinaryExpression</c> a folded result came from.</summary>
    /// <remarks>
    /// PORT-NOTE: TS spells this as the string literal union <c>"left" | "right"</c> on the <c>foldState</c> callback;
    /// C# has no string literal union, so the port uses an enum (naming a member <c>Left</c>/<c>Right</c> would have
    /// collided with the traversal states, hence <see cref="BinaryExpressionSide"/>).
    /// </remarks>
    public enum BinaryExpressionSide
    {
        Left,
        Right,
    }

    /// <summary>
    /// The heap-based traversal state of <c>createBinaryExpressionTrampoline</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the six state functions in the <c>BinaryExpressionState</c> namespace and passes the
    /// machine to each, because it has no types to nest them in. The port puts the states — and the enum TS names
    /// through a type alias — inside the machine class they belong to, so the six callbacks are read from a field
    /// instead of being threaded through every call. The traversal itself (the three parallel stacks, the state
    /// transitions, the fold side, the circularity check) is the same, including keeping every intermediate state on the
    /// heap rather than recursing.
    /// </remarks>
    private sealed class BinaryExpressionStateMachine<TOuterState, TState, TResult>
    {
        /// <summary>ported from: src/compiler/factory/utilities.ts:1271 — <c>BinaryExpressionState</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS compares the state functions by identity (they are the values in the state stack). The port
        /// compares this enum value, which is what the identity comparison distinguishes.
        /// </remarks>
        private enum State
        {
            Enter,
            Left,
            Operator,
            Right,
            Exit,
            Done,
        }

        private readonly Func<BinaryExpression, TState?, TOuterState, TState> _onEnter;
        private readonly Func<Expression, TState, BinaryExpression, BinaryExpression?>? _onLeft;
        private readonly Action<Token, TState, BinaryExpression>? _onOperator;
        private readonly Func<Expression, TState, BinaryExpression, BinaryExpression?>? _onRight;
        private readonly Func<BinaryExpression, TState, TResult> _onExit;
        private readonly Func<TState, TResult, BinaryExpressionSide, TState>? _foldState;

        public BinaryExpressionStateMachine(
            Func<BinaryExpression, TState?, TOuterState, TState> onEnter,
            Func<Expression, TState, BinaryExpression, BinaryExpression?>? onLeft,
            Action<Token, TState, BinaryExpression>? onOperator,
            Func<Expression, TState, BinaryExpression, BinaryExpression?>? onRight,
            Func<BinaryExpression, TState, TResult> onExit,
            Func<TState, TResult, BinaryExpressionSide, TState>? foldState)
        {
            _onEnter = onEnter;
            _onLeft = onLeft;
            _onOperator = onOperator;
            _onRight = onRight;
            _onExit = onExit;
            _foldState = foldState;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1475-1487 — <c>trampoline</c>.</summary>
        public TResult Run(BinaryExpression node, TOuterState outerState)
        {
            var resultHolder = new ResultHolder();
            var stateStack = new List<State> { State.Enter };
            var nodeStack = new List<BinaryExpression> { node };
            var userStateStack = new List<TState> { default! };
            var stackIndex = 0;
            while (stateStack[stackIndex] != State.Done)
            {
                stackIndex = stateStack[stackIndex] switch
                {
                    State.Enter => Enter(stackIndex, stateStack, nodeStack, userStateStack, outerState),
                    State.Left => Left(stackIndex, stateStack, nodeStack, userStateStack),
                    State.Operator => Operator(stackIndex, stateStack, nodeStack, userStateStack),
                    State.Right => Right(stackIndex, stateStack, nodeStack, userStateStack),
                    State.Exit => Exit(stackIndex, stateStack, nodeStack, userStateStack, resultHolder),
                    _ => Debug.Fail<int>("Invalid state"),
                };
            }

            Debug.Assert(stackIndex == 0);
            return resultHolder.Value;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1280-1292 — <c>BinaryExpressionState.enter</c>.</summary>
        private int Enter(
            int stackIndex,
            List<State> stateStack,
            List<BinaryExpression> nodeStack,
            List<TState> userStateStack,
            TOuterState outerState)
        {
            var prevUserState = stackIndex > 0 ? userStateStack[stackIndex - 1] : default;
            Debug.Assert(stateStack[stackIndex] == State.Enter);
            userStateStack[stackIndex] = _onEnter(nodeStack[stackIndex], prevUserState, outerState);
            stateStack[stackIndex] = NextState(State.Enter);
            return stackIndex;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1294-1310 — <c>BinaryExpressionState.left</c>.</summary>
        private int Left(
            int stackIndex,
            List<State> stateStack,
            List<BinaryExpression> nodeStack,
            List<TState> userStateStack)
        {
            Debug.Assert(stateStack[stackIndex] == State.Left);
            Debug.AssertIsDefined(_onLeft);
            stateStack[stackIndex] = NextState(State.Left);
            var nextNode = _onLeft!(nodeStack[stackIndex].Left!, userStateStack[stackIndex], nodeStack[stackIndex]);
            if (nextNode is not null)
            {
                CheckCircularity(stackIndex, nodeStack, nextNode);
                return PushStack(stackIndex, stateStack, nodeStack, userStateStack, nextNode);
            }

            return stackIndex;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1312-1324 — <c>BinaryExpressionState.operator</c>.</summary>
        private int Operator(
            int stackIndex,
            List<State> stateStack,
            List<BinaryExpression> nodeStack,
            List<TState> userStateStack)
        {
            Debug.Assert(stateStack[stackIndex] == State.Operator);
            Debug.AssertIsDefined(_onOperator);
            stateStack[stackIndex] = NextState(State.Operator);
            _onOperator!(nodeStack[stackIndex].OperatorToken!, userStateStack[stackIndex], nodeStack[stackIndex]);
            return stackIndex;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1326-1342 — <c>BinaryExpressionState.right</c>.</summary>
        private int Right(
            int stackIndex,
            List<State> stateStack,
            List<BinaryExpression> nodeStack,
            List<TState> userStateStack)
        {
            Debug.Assert(stateStack[stackIndex] == State.Right);
            Debug.AssertIsDefined(_onRight);
            stateStack[stackIndex] = NextState(State.Right);
            var nextNode = _onRight!(nodeStack[stackIndex].Right!, userStateStack[stackIndex], nodeStack[stackIndex]);
            if (nextNode is not null)
            {
                CheckCircularity(stackIndex, nodeStack, nextNode);
                return PushStack(stackIndex, stateStack, nodeStack, userStateStack, nextNode);
            }

            return stackIndex;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1344-1363 — <c>BinaryExpressionState.exit</c>.</summary>
        private int Exit(
            int stackIndex,
            List<State> stateStack,
            List<BinaryExpression> nodeStack,
            List<TState> userStateStack,
            ResultHolder resultHolder)
        {
            Debug.Assert(stateStack[stackIndex] == State.Exit);
            stateStack[stackIndex] = NextState(State.Exit);
            var result = _onExit(nodeStack[stackIndex], userStateStack[stackIndex]);
            if (stackIndex > 0)
            {
                stackIndex--;
                if (_foldState is not null)
                {
                    var side = stateStack[stackIndex] == State.Exit ? BinaryExpressionSide.Right : BinaryExpressionSide.Left;
                    userStateStack[stackIndex] = _foldState(userStateStack[stackIndex], result, side);
                }
            }
            else
            {
                resultHolder.Value = result;
            }

            return stackIndex;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1365-1368 — <c>BinaryExpressionState.done</c>.</summary>
        private int Done(int stackIndex, List<State> stateStack)
        {
            Debug.Assert(stateStack[stackIndex] == State.Done);
            return stackIndex;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1370-1384 — <c>BinaryExpressionState.nextState</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS falls through from <c>enter</c> to <c>left</c> and so on; C# has no fallthrough, so the chain
        /// is written as the guarded ifs that produce the same answer.
        /// </remarks>
        private State NextState(State currentState)
        {
            if (currentState == State.Enter && _onLeft is not null) return State.Left;
            if ((currentState == State.Enter || currentState == State.Left) && _onOperator is not null) return State.Operator;
            if (currentState is State.Enter or State.Left or State.Operator && _onRight is not null) return State.Right;
            if (currentState is State.Enter or State.Left or State.Operator or State.Right) return State.Exit;
            if (currentState == State.Exit) return State.Done;
            if (currentState == State.Done) return State.Done;

            Debug.Fail("Invalid state");
            return State.Done;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1395-1400 — <c>BinaryExpressionState.pushStack</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS assigns into the frame slot (<c>stateStack[stackIndex] = enter</c>, and the same for the two
        /// other stacks), which *reuses* the slot a previously completed deeper frame left behind — a JavaScript array
        /// assignment past the end appends, at the end it assigns. The port's lists therefore assign when the slot
        /// exists and append only when the frame is past the end. Appending unconditionally is wrong: a walk that pops a
        /// frame and then pushes a new one at the same depth — any binary expression with a binary expression on *both*
        /// sides, e.g. <c>(a + b) + (c + d)</c> or the comma expression in
        /// <c>if (y = "", typeof x === "string")</c> — would read the popped frame's <c>done</c> state, end the walk one
        /// frame early, and fail the <c>Debug.Assert(stackIndex == 0)</c> in <c>Run</c> (M5-2 found this through the
        /// binder's use of the trampoline; the factory's own use of it has the same shape).
        /// </remarks>
        private static int PushStack(
            int stackIndex,
            List<State> stateStack,
            List<BinaryExpression> nodeStack,
            List<TState> userStateStack,
            BinaryExpression node)
        {
            stackIndex++;
            if (stackIndex < stateStack.Count)
            {
                stateStack[stackIndex] = State.Enter;
                nodeStack[stackIndex] = node;
                userStateStack[stackIndex] = default!;
            }
            else
            {
                stateStack.Add(State.Enter);
                nodeStack.Add(node);
                userStateStack.Add(default!);
            }

            return stackIndex;
        }

        /// <summary>ported from: src/compiler/factory/utilities.ts:1400-1408 — <c>BinaryExpressionState.checkCircularity</c>.</summary>
        private static void CheckCircularity(int stackIndex, List<BinaryExpression> nodeStack, BinaryExpression node)
        {
            if (Debug.ShouldAssert(AssertionLevel.Aggressive))
            {
                while (stackIndex >= 0)
                {
                    Debug.Assert(!ReferenceEquals(nodeStack[stackIndex], node), "Circular traversal detected.");
                    stackIndex--;
                }
            }
        }

        /// <summary>The <c>{ value: TResult }</c> holder TS threads through the states.</summary>
        /// <remarks>
        /// PORT-NOTE: TS declares it inline as <c>{ value: TResult; }</c>; C# needs a named type, and the traversal
        /// keeps it as the single cell the outermost <c>exit</c> writes its result into.
        /// </remarks>
        private sealed class ResultHolder
        {
            public TResult Value = default!;
        }
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1436-1453 — <c>createBinaryExpressionTrampoline</c> (no outer state).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two overloads differ only in whether <c>onEnter</c> takes the caller's <c>outerState</c>. The
    /// port keeps both; this one adapts the two-parameter <c>onEnter</c> to the machine's three-parameter one with an
    /// outer state that is always absent, which is what TS's <c>outerState: undefined</c> is.
    /// </remarks>
    public static Func<BinaryExpression, TResult> CreateBinaryExpressionTrampoline<TState, TResult>(
        Func<BinaryExpression, TState?, TState> onEnter,
        Func<Expression, TState, BinaryExpression, BinaryExpression?>? onLeft,
        Action<Token, TState, BinaryExpression>? onOperator,
        Func<Expression, TState, BinaryExpression, BinaryExpression?>? onRight,
        Func<BinaryExpression, TState, TResult> onExit,
        Func<TState, TResult, BinaryExpressionSide, TState>? foldState)
    {
        var machine = new BinaryExpressionStateMachine<object?, TState, TResult>(
            (node, prev, _) => onEnter(node, prev),
            onLeft,
            onOperator,
            onRight,
            onExit,
            foldState);

        return node => machine.Run(node, null);
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1455-1486 — <c>createBinaryExpressionTrampoline</c>.</summary>
    public static Func<BinaryExpression, TOuterState, TResult> CreateBinaryExpressionTrampoline<TOuterState, TState, TResult>(
        Func<BinaryExpression, TState?, TOuterState, TState> onEnter,
        Func<Expression, TState, BinaryExpression, BinaryExpression?>? onLeft,
        Action<Token, TState, BinaryExpression>? onOperator,
        Func<Expression, TState, BinaryExpression, BinaryExpression?>? onRight,
        Func<BinaryExpression, TState, TResult> onExit,
        Func<TState, TResult, BinaryExpressionSide, TState>? foldState)
    {
        var machine = new BinaryExpressionStateMachine<TOuterState, TState, TResult>(
            onEnter,
            onLeft,
            onOperator,
            onRight,
            onExit,
            foldState);

        return (node, outerState) => machine.Run(node, outerState);
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1489-1491 — <c>isExportOrDefaultKeywordKind</c>.</summary>
    private static bool IsExportOrDefaultKeywordKind(SyntaxKind kind)
        => kind is SyntaxKind.ExportKeyword or SyntaxKind.DefaultKeyword;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1494-1496 — <c>isExportOrDefaultModifier</c>.</summary>
    public static bool IsExportOrDefaultModifier(Node node) => IsExportOrDefaultKeywordKind(node.Kind);

    //
    // Generated names
    //

    /// <summary>ported from: src/compiler/factory/utilities.ts:1505-1512 — <c>elideNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads (<c>NodeArray&lt;T&gt;</c> and <c>NodeArray&lt;T&gt; | undefined</c>) that a
    /// nullable parameter and result cover in C#. TS's <c>setTextRange</c> works on anything with a text range; the
    /// port's <see cref="Utilities.SetTextRange"/> requires <see cref="ITextRange"/>, which <see cref="NodeArray{T}"/>
    /// does not implement (it carries the range through <see cref="IReadonlyTextRange"/>), so the two assignments are
    /// written out.
    /// </remarks>
    public static NodeArray<T>? ElideNodes<T>(NodeFactory factory, NodeArray<T>? nodes)
        where T : INode
    {
        if (nodes is null) return null;
        if (nodes.Count == 0) return nodes;

        var array = factory.CreateNodeArray<T>([], nodes.HasTrailingComma);
        array.Pos = nodes.Pos;
        array.End = nodes.End;
        return array;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1518-1544 — <c>getNodeForGeneratedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the parameter as <c>GeneratedIdentifier | GeneratedPrivateIdentifier</c>; both are an
    /// <see cref="Identifier"/>/<see cref="PrivateIdentifier"/> wearing an auto-generate record, so the port takes the
    /// <see cref="Node"/> the union collapses to and asserts the record exists, exactly as TS dereferences
    /// <c>name.emitNode.autoGenerate</c> without checking.
    /// </remarks>
    public static Node GetNodeForGeneratedName(Node name)
    {
        var autoGenerate = EmitNodes.GetIdentifierAutoGenerate(name)!;
        if ((autoGenerate.Flags & GeneratedIdentifierFlags.Node) != 0)
        {
            var autoGenerateId = autoGenerate.Id;
            var node = name;
            var original = node.Original;
            while (original is not null)
            {
                node = original;
                var nodeAutoGenerate = EmitNodes.GetIdentifierAutoGenerate(node);

                // if "node" is a different generated name (having a different "autoGenerateId"), use it and stop traversing.
                if (Utilities.IsMemberName(node)
                    && (nodeAutoGenerate is null
                        || ((nodeAutoGenerate.Flags & GeneratedIdentifierFlags.Node) != 0
                            && nodeAutoGenerate.Id != autoGenerateId)))
                {
                    break;
                }

                original = node.Original;
            }

            // otherwise, return the original node for the source
            return node;
        }

        return name;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1559-1562 — <c>formatGeneratedNamePart</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>string | GeneratedNamePart | undefined</c> and the union's
    /// <c>typeof part === "object"</c> test distinguishes the generated-name part from the two string leaves; the port
    /// takes <c>object?</c> and matches <see cref="IGeneratedNamePart"/> (§12.5). TS's <c>generateName!</c> is the
    /// callback <c>formatIdentifier</c> below requires only when the base name is a node.
    /// </remarks>
    public static string FormatGeneratedNamePart(object? part, Func<Node, string>? generateName = null)
    {
        if (part is IGeneratedNamePart namePart)
        {
            return FormatGeneratedName(/*privateName*/ false, namePart.Prefix, namePart.Node, namePart.Suffix, generateName);
        }

        if (part is string text)
        {
            return text.Length > 0 && text[0] == CharacterCodes.Hash ? text[1..] : text;
        }

        return "";
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1565-1568 — <c>formatIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the parameter as <c>string | Identifier | PrivateIdentifier</c>; the union has no C# type,
    /// so the port takes <c>object?</c> and answers the same way for a string, narrowing the rest to
    /// <see cref="Node"/> (§12.5).
    /// </remarks>
    public static string FormatIdentifier(object? name, Func<Node, string>? generateName = null)
        => name is string text
            ? text
            : FormatIdentifierWorker((Node)name!, Debug.CheckDefined(generateName));

    /// <summary>ported from: src/compiler/factory/utilities.ts:1570-1576 — <c>formatIdentifierWorker</c>.</summary>
    private static string FormatIdentifierWorker(Node node, Func<Node, string> generateName)
    {
        if (Utilities.IsGeneratedPrivateIdentifier(node)) return generateName(node)[1..];
        if (Utilities.IsGeneratedIdentifier(node)) return generateName(node);
        if (NodeTests.IsPrivateIdentifier(node)) return (((PrivateIdentifier)node).EscapedText ?? "")[1..];
        return Utilities.IdText(node);
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1599-1606 — <c>formatGeneratedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads — the four-argument one without a <c>generateName</c> callback and the
    /// five-argument one with it. One C# method with a defaulted callback covers both, and the union parameter types
    /// arrive as <c>object?</c> as above.
    /// </remarks>
    public static string FormatGeneratedName(
        bool privateName,
        object? prefix,
        object? baseName,
        object? suffix,
        Func<Node, string>? generateName = null)
    {
        var formattedPrefix = FormatGeneratedNamePart(prefix, generateName);
        var formattedSuffix = FormatGeneratedNamePart(suffix, generateName);
        var formattedBaseName = FormatIdentifier(baseName, generateName);
        return $"{(privateName ? "#" : "")}{formattedPrefix}{formattedBaseName}{formattedSuffix}";
    }

    //
    // Accessors over a private backing field
    //

    /// <summary>ported from: src/compiler/factory/utilities.ts:1611-1619 — <c>createAccessorPropertyBackingField</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>modifiers</c> is <c>ModifiersArray | undefined</c> (<c>NodeArray&lt;Modifier&gt;</c>); the
    /// port's <c>UpdatePropertyDeclaration</c> takes the <c>List&lt;Node&gt;?</c> a parsed node actually stores
    /// (<c>ModifierLike</c> collapses to <c>Node</c>, §12.5).
    /// </remarks>
    public static PropertyDeclaration CreateAccessorPropertyBackingField(
        NodeFactory factory,
        PropertyDeclaration node,
        List<Node>? modifiers,
        Expression? initializer)
        => factory.UpdatePropertyDeclaration(
            node,
            modifiers,
            factory.GetGeneratedPrivateNameForNode(node.Name, /*prefix*/ null, "_accessor_storage"),
            /*questionOrExclamationToken*/ null,
            /*type*/ null,
            initializer);

    /// <summary>ported from: src/compiler/factory/utilities.ts:1627-1643 — <c>createAccessorPropertyGetRedirector</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's default argument is <c>factory.createThis()</c>, which C# cannot express; the port defaults
    /// <paramref name="receiver"/> to <c>null</c> and creates the <c>this</c> expression on entry, which is the same
    /// value at the same moment. TS's <c>name</c> is a <c>PropertyName</c>; every member of that union is a node, so the
    /// port takes <see cref="Node"/>.
    /// </remarks>
    public static GetAccessorDeclaration CreateAccessorPropertyGetRedirector(
        NodeFactory factory,
        PropertyDeclaration node,
        List<Node>? modifiers,
        Node name,
        Expression? receiver = null)
    {
        receiver ??= factory.CreateThis();

        return factory.CreateGetAccessorDeclaration(
            modifiers,
            name,
            [],
            /*type*/ null,
            factory.CreateBlock(
                [
                    factory.CreateReturnStatement(
                        factory.CreatePropertyAccessExpression(
                            receiver,
                            factory.GetGeneratedPrivateNameForNode(node.Name, /*prefix*/ null, "_accessor_storage"))),
                ],
                null));
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1649-1669 — <c>createAccessorPropertySetRedirector</c>.</summary>
    public static SetAccessorDeclaration CreateAccessorPropertySetRedirector(
        NodeFactory factory,
        PropertyDeclaration node,
        List<Node>? modifiers,
        Node name,
        Expression? receiver = null)
    {
        receiver ??= factory.CreateThis();

        return factory.CreateSetAccessorDeclaration(
            modifiers,
            name,
            [
                factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    "value",
                    /*questionToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null),
            ],
            factory.CreateBlock(
                [
                    factory.CreateExpressionStatement(
                        factory.CreateAssignment(
                            factory.CreatePropertyAccessExpression(
                                receiver,
                                factory.GetGeneratedPrivateNameForNode(node.Name, /*prefix*/ null, "_accessor_storage")),
                            factory.CreateIdentifier("value"))),
                ],
                null));
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1673-1693 — <c>findComputedPropertyNameCacheAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows the result to <c>AssignmentExpression&lt;EqualsToken&gt; &amp; { left: GeneratedIdentifier
    /// }</c>. The port's <c>AssignmentExpression&lt;TOperator&gt;</c> is an abstract, typing-only view of a
    /// <c>BinaryExpression</c> with no instantiable subclass — the node the parser builds is a
    /// <see cref="BinaryExpression"/> — so the port answers <see cref="BinaryExpression"/> and the caller reads
    /// <c>Left</c>.
    /// </remarks>
    public static BinaryExpression? FindComputedPropertyNameCacheAssignment(ComputedPropertyName name)
    {
        var node = name.Expression!;
        while (true)
        {
            node = (Expression)SkipOuterExpressions(node);
            if (NodeTests.IsCommaListExpression(node))
            {
                node = Core.Last(((CommaListExpression)node).Elements!);
                continue;
            }

            if (IsCommaExpression(node))
            {
                node = ((BinaryExpression)node).Right!;
                continue;
            }

            if (Utilities.IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true)
                && Utilities.IsGeneratedIdentifier(((BinaryExpression)node).Left!))
            {
                return (BinaryExpression)node;
            }

            break;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1695-1699 — <c>isSyntheticParenthesizedExpression</c>.</summary>
    private static bool IsSyntheticParenthesizedExpression(Expression node)
        => NodeTests.IsParenthesizedExpression(node)
            && Utilities.NodeIsSynthesized(node)
            && node.EmitNode is null;

    /// <summary>ported from: src/compiler/factory/utilities.ts:1701-1721 — <c>flattenCommaListWorker</c>.</summary>
    private static void FlattenCommaListWorker(Expression node, List<Expression> expressions)
    {
        if (IsSyntheticParenthesizedExpression(node))
        {
            FlattenCommaListWorker(((ParenthesizedExpression)node).Expression!, expressions);
        }
        else if (IsCommaExpression(node))
        {
            FlattenCommaListWorker(((BinaryExpression)node).Left!, expressions);
            FlattenCommaListWorker(((BinaryExpression)node).Right!, expressions);
        }
        else if (NodeTests.IsCommaListExpression(node))
        {
            foreach (var child in ((CommaListExpression)node).Elements!)
            {
                FlattenCommaListWorker(child, expressions);
            }
        }
        else
        {
            expressions.Add(node);
        }
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1725-1729 — <c>flattenCommaList</c>.</summary>
    public static List<Expression> FlattenCommaList(Expression node)
    {
        var expressions = new List<Expression>();
        FlattenCommaListWorker(node, expressions);
        return expressions;
    }

    /// <summary>ported from: src/compiler/factory/utilities.ts:1739-1757 — <c>containsObjectRestOrSpread</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>AssignmentPattern</c> is <c>ArrayLiteralExpression | ObjectLiteralExpression</c>, so the port
    /// takes the <see cref="Node"/> both are and lets <see cref="Utilities.IsAssignmentPattern"/> do the questioning, as
    /// the rest of the port does for unions (§12.5).
    /// </remarks>
    public static bool ContainsObjectRestOrSpread(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0) return true;

        if ((node.TransformFlags & TransformFlags.ContainsES2018) != 0)
        {
            // check for nested spread assignments, otherwise '{ x: { a, ...b } = foo } = c'
            // will not be correctly interpreted by the ES2018 transformer
            foreach (var element in GetElementsOfBindingOrAssignmentPattern(node))
            {
                var target = GetTargetOfBindingOrAssignmentElement(element);
                if (target is not null && Utilities.IsAssignmentPattern(target))
                {
                    if ((target.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0)
                    {
                        return true;
                    }

                    if ((target.TransformFlags & TransformFlags.ContainsES2018) != 0)
                    {
                        if (ContainsObjectRestOrSpread(target)) return true;
                    }
                }
            }
        }

        return false;
    }

    //
    // Private helpers shared by the members above
    //

    /// <summary>
    /// The <c>debug.ts</c> call the two private-property branches of
    /// <c>tryGetPropertyNameOfBindingOrAssignmentElement</c> make, which is the only thing this file needs from
    /// <c>Debug.failBadSyntaxKind</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>Debug.failBadSyntaxKind</c> is listed as unported (§10.4: it needs <c>formatSyntaxKind</c>), and
    /// inventing a member on <see cref="Debug"/> is out of scope for this file. TS's body is
    /// <c>fail(`${message || "Unexpected node."}\r\nNode ${formatSyntaxKind(node.kind)} was unexpected.`)</c>, and
    /// <c>formatSyntaxKind</c> is the enum member's name — which is what <c>node.Kind</c> prints. These call sites
    /// pass no message.
    /// </remarks>
    private static Node FailBadSyntaxKind(Node node)
        => Debug.Fail<Node>($"Unexpected node.\r\nNode {node.Kind} was unexpected.");

    /// <summary>
    /// Narrows a parsed node's modifier array to the <see cref="ModifierToken"/> list the factory's function builders
    /// take.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's node types store <c>modifiers</c> as <c>NodeArray&lt;ModifierLike&gt;</c> and its factories take
    /// <c>readonly Modifier[]</c>; the arrays are compatible there because TS arrays are covariant and the two unions
    /// overlap. In C# the two element types are different constructed generics, so the port maps the elements. The
    /// elements of a <c>Modifier</c> slot always are modifier tokens; a decorator could only appear here for input the
    /// parser rejects.
    /// </remarks>
    private static List<ModifierToken>? AsModifierTokens(NodeArray<Node>? modifiers)
        => modifiers is null ? null : Core.Map(modifiers, static (modifier, _) => (ModifierToken)modifier);

    /// <summary>The <c>operand</c> of the pre- or postfix unary expression TS's union lets a caller read directly.</summary>
    private static Expression? OperandOf(Node node) => node switch
    {
        PrefixUnaryExpression prefix => prefix.Operand,
        PostfixUnaryExpression postfix => postfix.Operand,
        _ => null,
    };

    /// <summary>The <c>expression</c> of an outer expression, which TS's <c>OuterExpression</c> union lets it read.</summary>
    private static Expression? OuterExpressionOf(Node node) => node.Kind switch
    {
        SyntaxKind.ParenthesizedExpression => ((ParenthesizedExpression)node).Expression,
        SyntaxKind.TypeAssertionExpression => ((TypeAssertion)node).Expression,
        SyntaxKind.AsExpression => ((AsExpression)node).Expression,
        SyntaxKind.SatisfiesExpression => ((SatisfiesExpression)node).Expression,
        SyntaxKind.ExpressionWithTypeArguments => ((ExpressionWithTypeArguments)node).Expression,
        SyntaxKind.NonNullExpression => ((NonNullExpression)node).Expression,
        SyntaxKind.PartiallyEmittedExpression => ((PartiallyEmittedExpression)node).Expression,
        _ => null,
    };
}
