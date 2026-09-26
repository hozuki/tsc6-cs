#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/esDecorators.ts:1630-2274 — the expression/assignment visitors and the
// pending-expression plumbing of `transformESDecorators`'s body.
/// <summary>
/// ported from: src/compiler/transformers/esDecorators.ts:1630-2274 — <c>visitThisExpression</c> through
/// <c>transformDecorator</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the super rewrites): the four members that read the reference's <c>classThis</c>/<c>classSuper</c> closure
/// variables read this class's <see cref="_classThis"/>/<see cref="_classSuper"/> fields, which are the same values at
/// the same moment (the stack's <c>updateState</c> is what the closures read).
/// </para>
/// <para>
/// PORT-NOTE (the assignment-pattern types): TS's <c>AssignmentPattern</c>, <c>ArrayAssignmentElement</c>,
/// <c>ObjectLiteralElement</c> and <c>LeftHandSideExpression</c> are unions whose members the model carries as
/// <see cref="ArrayLiteralExpression"/>/<see cref="ObjectLiteralExpression"/>/<see cref="Expression"/>; the port takes
/// <see cref="Node"/>/<see cref="Expression"/> and tests the kind, exactly as the reference's <c>is*</c> guards do.
/// </para>
/// <para>
/// PORT-NOTE (<c>WrappedExpression&lt;T&gt;</c>): TS's wrapper union is transparent to every reader in this file (it is
/// <c>T | PartiallyEmittedExpression | ParenthesizedExpression</c> and the code only ever skips or restores those
/// wrappers), so the port spells the slots <see cref="Expression"/>.
/// </para>
/// </remarks>
internal sealed partial class ESDecoratorTransformer
{
    // ---------------------------------------------------------------------------------------------
    // esDecorators.ts:1630-1752 — this, calls, super access, parameters, statements
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1630-1632 — <c>visitThisExpression</c>.</summary>
    private object VisitThisExpression(ThisExpression node) => _classThis is not null ? _classThis : node;

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1634-1645 — <c>visitCallExpression</c>.</summary>
    private object? VisitCallExpression(CallExpression node)
    {
        if (Utilities.IsSuperProperty(node.Expression!) && _classThis is not null)
        {
            var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
            var argumentsList = Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression);
            var invocation = _factory.CreateFunctionCallCall(expression, _classThis, argumentsList);
            NodeFactory.SetOriginalNode(invocation, node);
            Utilities.SetTextRange(invocation, node);
            return invocation;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1647-1658 — <c>visitTaggedTemplateExpression</c>.</summary>
    private object? VisitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        if (Utilities.IsSuperProperty(node.Tag!) && _classThis is not null)
        {
            var tag = Utilities.VisitNode(node.Tag, Visitor, Utilities.IsExpression);
            var boundTag = _factory.CreateFunctionBindCall(tag, _classThis, []);
            NodeFactory.SetOriginalNode(boundTag, node);
            Utilities.SetTextRange(boundTag, node);
            var template = Utilities.VisitNode(node.Template, Visitor, Utilities.IsTemplateLiteral);
            return _factory.UpdateTaggedTemplateExpression(node, boundTag, /*typeArguments*/ null, template);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1660-1670 — <c>visitPropertyAccessExpression</c>.</summary>
    private object? VisitPropertyAccessExpression(PropertyAccessExpression node)
    {
        if (Utilities.IsSuperProperty(node) && NodeTests.IsIdentifier(node.Name!) && _classThis is not null && _classSuper is not null)
        {
            var propertyName = _factory.CreateStringLiteralFromNode(node.Name, /*isSingleQuote*/ null);
            var superProperty = _factory.CreateReflectGetCall(_classSuper, propertyName, _classThis);
            NodeFactory.SetOriginalNode(superProperty, node.Expression);
            Utilities.SetTextRange(superProperty, node.Expression);
            return superProperty;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1672-1682 — <c>visitElementAccessExpression</c>.</summary>
    private object? VisitElementAccessExpression(ElementAccessExpression node)
    {
        if (Utilities.IsSuperProperty(node) && _classThis is not null && _classSuper is not null)
        {
            var propertyName = Utilities.VisitNode(node.ArgumentExpression, Visitor, Utilities.IsExpression);
            var superProperty = _factory.CreateReflectGetCall(_classSuper, propertyName, _classThis);
            NodeFactory.SetOriginalNode(superProperty, node.Expression);
            Utilities.SetTextRange(superProperty, node.Expression);
            return superProperty;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1684-1724 — <c>visitParameterDeclaration</c>.</summary>
    /// <remarks>
    /// 8.6.3 RS: IteratorBindingInitialization<br/>
    ///   SingleNameBinding : BindingIdentifier Initializer?
    ///     ...
    ///     5. If |Initializer| is present and _v_ is *undefined*, then
    ///        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///     ...
    /// <para>
    /// 14.3.3.3 RS: KeyedBindingInitialization<br/>
    ///   SingleNameBinding : BindingIdentifier Initializer?
    ///     ...
    ///     4. If |Initializer| is present and _v_ is *undefined*, then
    ///        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///     ...
    /// </para>
    /// </remarks>
    private object? VisitParameterDeclaration(ParameterDeclaration node)
    {
        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (ParameterDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.Initializer));
        }

        var updated = _factory.UpdateParameterDeclaration(
            node,
            /*modifiers*/ null,
            node.DotDotDotToken,
            Utilities.VisitNode(node.Name, Visitor, Utilities.IsBindingName),
            /*questionToken*/ null,
            /*type*/ null,
            Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));

        if (!ReferenceEquals(updated, node))
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitNodes.SetCommentRange(updated, node);
            Utilities.SetTextRange(updated, TypeScriptTransformSupport.MoveRangePastModifiers(node));
            EmitNodes.SetSourceMapRange(updated, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers(node)));
            EmitNodes.SetEmitFlags(updated.Name!, EmitFlags.NoTrailingSourceMap);
        }
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1726-1728 — <c>isAnonymousClassNeedingAssignedName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>AnonymousFunctionDefinition</c> union (a class expression, a function
    /// expression or an arrow); the port takes <see cref="Node"/>, the union's common base, and keeps the reference's
    /// own <c>isClassExpression</c> test.</remarks>
    private static bool IsAnonymousClassNeedingAssignedName(Node node)
        => NodeTests.IsClassExpression(node) && ((ClassExpression)node).Name is null && IsDecoratedClassLike(node);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1730-1738 — <c>canIgnoreEmptyStringLiteralInAssignedName</c>.</summary>
    /// <remarks>
    /// The IIFE produced for `(@dec class {})` will result in an assigned name of the form
    /// `var class_1 = class { };`, and thus the empty string cannot be ignored. However, The IIFE
    /// produced for `(class { @dec x; })` will not result in an assigned name since it
    /// transforms to `return class { };`, and thus the empty string *can* be ignored.
    /// </remarks>
    private static bool CanIgnoreEmptyStringLiteralInAssignedName(Expression? node)
    {
        var innerExpression = FactoryUtilities.SkipOuterExpressions(node!);
        return NodeTests.IsClassExpression(innerExpression) && ((ClassExpression)innerExpression).Name is null && !Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ false, innerExpression);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1740-1748 — <c>visitForStatement</c>.</summary>
    private ForStatement VisitForStatement(ForStatement node)
        => _factory.UpdateForStatement(
            node,
            Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.Incrementor, DiscardedValueVisitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, Visitor, _context));

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1750-1752 — <c>visitExpressionStatement</c>.</summary>
    private ExpressionStatement VisitExpressionStatement(ExpressionStatement node)
        => Utilities.VisitEachChildWorker(node, DiscardedValueVisitor, _context);

    // ---------------------------------------------------------------------------------------------
    // esDecorators.ts:1754-1926 — assignments, unary operators, comma lists
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1754-1858 — <c>visitBinaryExpression</c>.</summary>
    private object? VisitBinaryExpression(BinaryExpression node, bool discarded)
    {
        if (Utilities.IsDestructuringAssignment(node))
        {
            var left = VisitAssignmentPattern(node.Left!);
            var right = Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression);
            return _factory.UpdateBinaryExpression(node, (Expression)left, node.OperatorToken!, right);
        }

        if (Utilities.IsAssignmentExpression(node))
        {
            // 13.15.2 RS: Evaluation
            //   AssignmentExpression : LeftHandSideExpression `=` AssignmentExpression
            //     1. If |LeftHandSideExpression| is neither an |ObjectLiteral| nor an |ArrayLiteral|, then
            //        a. Let _lref_ be ? Evaluation of |LeftHandSideExpression|.
            //        b. If IsAnonymousFunctionDefinition(|AssignmentExpression|) and IsIdentifierRef of |LeftHandSideExpression| are both *true*, then
            //           i. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...
            //
            //   AssignmentExpression : LeftHandSideExpression `&&=` AssignmentExpression
            //     ...
            //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and IsIdentifierRef of |LeftHandSideExpression| is *true*, then
            //        a. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...
            //
            //   AssignmentExpression : LeftHandSideExpression `||=` AssignmentExpression
            //     ...
            //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and IsIdentifierRef of |LeftHandSideExpression| is *true*, then
            //        a. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...
            //
            //   AssignmentExpression : LeftHandSideExpression `??=` AssignmentExpression
            //     ...
            //     4. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and IsIdentifierRef of |LeftHandSideExpression| is *true*, then
            //        a. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...

            if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
            {
                node = (BinaryExpression)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.Right));
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
            }

            if (Utilities.IsSuperProperty(node.Left!) && _classThis is not null && _classSuper is not null)
            {
                var setterName = NodeTests.IsElementAccessExpression(node.Left!)
                    ? Utilities.VisitNode(((ElementAccessExpression)node.Left!).ArgumentExpression, Visitor, Utilities.IsExpression)
                    : NodeTests.IsIdentifier(((PropertyAccessExpression)node.Left!).Name!)
                        ? (Expression)_factory.CreateStringLiteralFromNode(((PropertyAccessExpression)node.Left!).Name, /*isSingleQuote*/ null)
                        : null;
                if (setterName is not null)
                {
                    // super.x = ...
                    // super.x += ...
                    // super[x] = ...
                    // super[x] += ...
                    var expression = Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression);
                    if (TransformationUtilities.IsCompoundAssignment(node.OperatorToken!.Kind))
                    {
                        var getterName = setterName;
                        if (!TransformationUtilities.IsSimpleInlineableExpression(setterName))
                        {
                            getterName = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                            setterName = _factory.CreateAssignment(getterName, setterName);
                        }

                        var superPropertyGet = _factory.CreateReflectGetCall(
                            _classSuper,
                            getterName,
                            _classThis);
                        NodeFactory.SetOriginalNode(superPropertyGet, node.Left);
                        Utilities.SetTextRange(superPropertyGet, node.Left);

                        expression = _factory.CreateBinaryExpression(
                            superPropertyGet,
                            TransformationUtilities.GetNonAssignmentOperatorForCompoundAssignment(node.OperatorToken.Kind),
                            expression);
                        Utilities.SetTextRange(expression, node);
                    }

                    var temp = discarded ? null : _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                    if (temp is not null)
                    {
                        expression = _factory.CreateAssignment(temp, expression);
                        Utilities.SetTextRange(temp, node);
                    }

                    expression = _factory.CreateReflectSetCall(
                        _classSuper,
                        setterName,
                        expression,
                        _classThis);
                    NodeFactory.SetOriginalNode(expression, node);
                    Utilities.SetTextRange(expression, node);

                    if (temp is not null)
                    {
                        expression = _factory.CreateComma(expression, temp);
                        Utilities.SetTextRange(expression, node);
                    }

                    return expression;
                }
            }
        }

        if (node.OperatorToken!.Kind == SyntaxKind.CommaToken)
        {
            var left = Utilities.VisitNode(node.Left, DiscardedValueVisitor, Utilities.IsExpression);
            var right = Utilities.VisitNode(node.Right, discarded ? DiscardedValueVisitor : Visitor, Utilities.IsExpression);
            return _factory.UpdateBinaryExpression(node, left, node.OperatorToken, right);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1860-1919 — <c>visitPreOrPostfixUnaryExpression</c>.</summary>
    /// <remarks>PORT-NOTE: the parameter is the reference's <c>PrefixUnaryExpression | PostfixUnaryExpression</c> union,
    /// restated over <see cref="Node"/> with the operator read through the same two-class test the reference's
    /// <c>node.operator</c> resolves.</remarks>
    private object? VisitPreOrPostfixUnaryExpression(Node node, bool discarded)
    {
        var @operator = node is PrefixUnaryExpression prefix ? prefix.Operator : ((PostfixUnaryExpression)node).Operator;
        if (
            @operator == SyntaxKind.PlusPlusToken ||
            @operator == SyntaxKind.MinusMinusToken
        )
        {
            var operand = Utilities.SkipParentheses(node is PrefixUnaryExpression prefixNode ? prefixNode.Operand! : ((PostfixUnaryExpression)node).Operand!);
            if (Utilities.IsSuperProperty(operand) && _classThis is not null && _classSuper is not null)
            {
                var setterName = NodeTests.IsElementAccessExpression(operand)
                    ? Utilities.VisitNode(((ElementAccessExpression)operand).ArgumentExpression, Visitor, Utilities.IsExpression)
                    : NodeTests.IsIdentifier(((PropertyAccessExpression)operand).Name!)
                        ? (Expression)_factory.CreateStringLiteralFromNode(((PropertyAccessExpression)operand).Name, /*isSingleQuote*/ null)
                        : null;
                if (setterName is not null)
                {
                    Expression getterName = setterName;
                    if (!TransformationUtilities.IsSimpleInlineableExpression(setterName))
                    {
                        getterName = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                        setterName = _factory.CreateAssignment(getterName, setterName);
                    }

                    Expression expression = _factory.CreateReflectGetCall(_classSuper, getterName, _classThis);
                    NodeFactory.SetOriginalNode(expression, node);
                    Utilities.SetTextRange(expression, node);

                    // If the result of this expression is discarded (i.e., it's in a position where the result
                    // will be otherwise unused, such as in an expression statement or the left side of a comma), we
                    // don't need to create an extra temp variable to hold the result:
                    //
                    //  source (discarded):
                    //    super.x++;
                    //  generated:
                    //    _a = Reflect.get(_super, "x"), _a++, Reflect.set(_super, "x", _a);
                    //
                    // Above, the temp variable `_a` is used to perform the correct coercion (i.e., number or
                    // bigint). Since the result of the postfix unary is discarded, we don't need to capture the
                    // result of the expression.
                    //
                    //  source (not discarded):
                    //    y = super.x++;
                    //  generated:
                    //    y = (_a = Reflect.get(_super, "x"), _b = _a++, Reflect.set(_super, "x", _a), _b);
                    //
                    // When the result isn't discarded, we introduce a new temp variable (`_b`) to capture the
                    // result of the operation so that we can provide it to `y` when the assignment is complete.

                    var temp = discarded ? null : _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                    expression = FactoryUtilities.ExpandPreOrPostfixIncrementOrDecrementExpression(_factory, node, expression, _context.HoistVariableDeclaration, temp);
                    expression = _factory.CreateReflectSetCall(_classSuper, setterName, expression, _classThis);
                    NodeFactory.SetOriginalNode(expression, node);
                    Utilities.SetTextRange(expression, node);

                    if (temp is not null)
                    {
                        expression = _factory.CreateComma(expression, temp);
                        Utilities.SetTextRange(expression, node);
                    }

                    return expression;
                }
            }
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1921-1926 — <c>visitCommaListExpression</c>.</summary>
    private CommaListExpression VisitCommaListExpression(CommaListExpression node, bool discarded)
    {
        var elements = discarded
            ? Utilities.VisitCommaListElements(node.Elements!, DiscardedValueVisitor)
            : Utilities.VisitCommaListElements(node.Elements!, Visitor, DiscardedValueVisitor);
        return _factory.UpdateCommaListExpression(node, elements);
    }

    // ---------------------------------------------------------------------------------------------
    // esDecorators.ts:1928-2197 — property names, named evaluation, assignment patterns
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1928-1948 — <c>visitReferencedPropertyName</c>.</summary>
    private (Expression ReferencedName, Node? Name) VisitReferencedPropertyName(Node node)
    {
        if (Utilities.IsPropertyNameLiteral(node) || NodeTests.IsPrivateIdentifier(node))
        {
            var referencedName = _factory.CreateStringLiteralFromNode(node, /*isSingleQuote*/ null);
            var name = Utilities.VisitNode(node, Visitor, Utilities.IsPropertyName);
            return (referencedName, name);
        }

        if (Utilities.IsPropertyNameLiteral(((ComputedPropertyName)node).Expression!) && !NodeTests.IsIdentifier(((ComputedPropertyName)node).Expression!))
        {
            var referencedName = _factory.CreateStringLiteralFromNode(((ComputedPropertyName)node).Expression, /*isSingleQuote*/ null);
            var name = Utilities.VisitNode(node, Visitor, Utilities.IsPropertyName);
            return (referencedName, name);
        }

        var generatedName = _factory.GetGeneratedNameForNode(node, /*flags*/ null);
        _context.HoistVariableDeclaration(generatedName);

        var key = _emitHelpers.CreatePropKeyHelper(Utilities.VisitNode(((ComputedPropertyName)node).Expression, Visitor, Utilities.IsExpression));
        var assignment = _factory.CreateAssignment(generatedName, key);
        var updatedName = _factory.UpdateComputedPropertyName((ComputedPropertyName)node, InjectPendingExpressions(assignment));
        return (generatedName, updatedName);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1950-1955 — <c>visitPropertyName</c>.</summary>
    private Node? VisitPropertyName(Node node)
        => NodeTests.IsComputedPropertyName(node)
            ? VisitComputedPropertyName((ComputedPropertyName)node)
            : Utilities.VisitNode(node, Visitor, Utilities.IsPropertyName);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1957-1963 — <c>visitComputedPropertyName</c>.</summary>
    private ComputedPropertyName VisitComputedPropertyName(ComputedPropertyName node)
    {
        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
        if (!TransformationUtilities.IsSimpleInlineableExpression(expression!))
        {
            expression = InjectPendingExpressions(expression!);
        }
        return _factory.UpdateComputedPropertyName(node, expression);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1965-1978 — <c>visitPropertyAssignment</c>.</summary>
    /// <remarks>
    /// 13.2.5.5 RS: PropertyDefinitionEvaluation<br/>
    ///   PropertyAssignment : PropertyName `:` AssignmentExpression
    ///     ...
    ///     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and _isProtoSetter_ is *false*, then
    ///        a. Let _popValue_ be ? NamedEvaluation of |AssignmentExpression| with argument _propKey_.
    ///     ...
    /// </remarks>
    private object? VisitPropertyAssignment(PropertyAssignment node)
    {
        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (PropertyAssignment)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.Initializer));
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1980-2000 — <c>visitVariableDeclaration</c>.</summary>
    /// <remarks>
    /// 14.3.1.2 RS: Evaluation<br/>
    ///   LexicalBinding : BindingIdentifier Initializer
    ///     ...
    ///     3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///     ...
    /// <para>
    /// 14.3.2.1 RS: Evaluation<br/>
    ///   VariableDeclaration : BindingIdentifier Initializer
    ///     ...
    ///     3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///     ...
    /// </para>
    /// </remarks>
    private object? VisitVariableDeclaration(VariableDeclaration node)
    {
        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (VariableDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.Initializer));
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2002-2024 — <c>visitBindingElement</c>.</summary>
    /// <remarks>
    /// 8.6.3 RS: IteratorBindingInitialization<br/>
    ///   SingleNameBinding : BindingIdentifier Initializer?
    ///     ...
    ///     5. If |Initializer| is present and _v_ is *undefined*, then
    ///        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///     ...
    /// <para>
    /// 14.3.3.3 RS: KeyedBindingInitialization<br/>
    ///   SingleNameBinding : BindingIdentifier Initializer?
    ///     ...
    ///     4. If |Initializer| is present and _v_ is *undefined*, then
    ///        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///     ...
    /// </para>
    /// </remarks>
    private object? VisitBindingElement(BindingElement node)
    {
        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (BindingElement)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.Initializer));
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2026-2053 — <c>visitDestructuringAssignmentTarget</c>.</summary>
    private Expression VisitDestructuringAssignmentTarget(Expression node)
    {
        if (NodeTests.IsObjectLiteralExpression(node) || NodeTests.IsArrayLiteralExpression(node))
        {
            return (Expression)VisitAssignmentPattern(node)!;
        }

        if (Utilities.IsSuperProperty(node) && _classThis is not null && _classSuper is not null)
        {
            var propertyName = NodeTests.IsElementAccessExpression(node)
                ? Utilities.VisitNode(((ElementAccessExpression)node).ArgumentExpression, Visitor, Utilities.IsExpression)
                : NodeTests.IsIdentifier(((PropertyAccessExpression)node).Name!)
                    ? (Expression)_factory.CreateStringLiteralFromNode(((PropertyAccessExpression)node).Name, /*isSingleQuote*/ null)
                    : null;
            if (propertyName is not null)
            {
                var paramName = _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null);
                var expression = _factory.CreateAssignmentTargetWrapper(
                    paramName,
                    _factory.CreateReflectSetCall(
                        _classSuper,
                        propertyName,
                        paramName,
                        _classThis));
                NodeFactory.SetOriginalNode(expression, node);
                Utilities.SetTextRange(expression, node);
                return expression;
            }
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2055-2076 — <c>visitAssignmentElement</c>.</summary>
    /// <remarks>
    /// 13.15.5.5 RS: IteratorDestructuringAssignmentEvaluation<br/>
    ///   AssignmentElement : DestructuringAssignmentTarget Initializer?
    ///     ...
    ///     4. If |Initializer| is present and _value_ is *undefined*, then
    ///        a. If IsAnonymousFunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
    ///           i. Let _v_ be ? NamedEvaluation of |Initializer| with argument _lref_.[[ReferencedName]].
    ///     ...
    /// </remarks>
    private Node VisitAssignmentElement(Node node)
    {
        if (Utilities.IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true))
        {
            var binary = (BinaryExpression)node;
            if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
            {
                binary = (BinaryExpression)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(binary.Right));
            }

            var assignmentTarget = VisitDestructuringAssignmentTarget(binary.Left!);
            var initializer = Utilities.VisitNode(binary.Right, Visitor, Utilities.IsExpression);
            return _factory.UpdateBinaryExpression(binary, assignmentTarget, binary.OperatorToken!, initializer);
        }
        else
        {
            return VisitDestructuringAssignmentTarget((Expression)node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2078-2085 — <c>visitAssignmentRestElement</c>.</summary>
    private Node VisitAssignmentRestElement(SpreadElement node)
    {
        if (Utilities.IsLeftHandSideExpression(node.Expression!))
        {
            var expression = VisitDestructuringAssignmentTarget(node.Expression!);
            return _factory.UpdateSpreadElement(node, expression);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2087-2092 — <c>visitArrayAssignmentElement</c>.</summary>
    private Node VisitArrayAssignmentElement(Node node)
    {
        Debug.AssertNode(node, TransformationUtilities.IsArrayBindingOrAssignmentElement);
        if (NodeTests.IsSpreadElement(node)) return VisitAssignmentRestElement((SpreadElement)node);
        if (!NodeTests.IsOmittedExpression(node)) return VisitAssignmentElement(node);
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2094-2118 — <c>visitAssignmentProperty</c>.</summary>
    /// <remarks>
    /// AssignmentProperty : PropertyName `:` AssignmentElement<br/>
    /// AssignmentElement : DestructuringAssignmentTarget Initializer?
    /// <para>
    /// 13.15.5.6 RS: KeyedDestructuringAssignmentEvaluation<br/>
    ///   AssignmentElement : DestructuringAssignmentTarget Initializer?
    ///     ...
    ///     3. If |Initializer| is present and _v_ is *undefined*, then
    ///        a. If IsAnonymousfunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
    ///           i. Let _rhsValue_ be ? NamedEvaluation of |Initializer| with argument _lref_.[[ReferencedName]].
    ///     ...
    /// </para>
    /// </remarks>
    private Node VisitAssignmentProperty(PropertyAssignment node)
    {
        var name = Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName);
        if (Utilities.IsAssignmentExpression(node.Initializer!, /*excludeCompoundAssignment*/ true))
        {
            var assignmentElement = VisitAssignmentElement(node.Initializer!);
            return _factory.UpdatePropertyAssignment(node, name, (Expression)assignmentElement);
        }

        if (Utilities.IsLeftHandSideExpression(node.Initializer!))
        {
            var assignmentElement = VisitDestructuringAssignmentTarget(node.Initializer!);
            return _factory.UpdatePropertyAssignment(node, name, (Expression)assignmentElement);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2120-2136 — <c>visitShorthandAssignmentProperty</c>.</summary>
    /// <remarks>
    /// AssignmentProperty : IdentifierReference Initializer?
    /// <para>
    /// 13.15.5.3 RS: PropertyDestructuringAssignmentEvaluation<br/>
    ///   AssignmentProperty : IdentifierReference Initializer?
    ///     ...
    ///     4. If |Initializer?| is present and _v_ is *undefined*, then
    ///        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _P_.
    ///     ...
    /// </para>
    /// </remarks>
    private object? VisitShorthandAssignmentProperty(ShorthandPropertyAssignment node)
    {
        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (ShorthandPropertyAssignment)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.ObjectAssignmentInitializer));
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2138-2145 — <c>visitAssignmentRestProperty</c>.</summary>
    private Node VisitAssignmentRestProperty(SpreadAssignment node)
    {
        if (Utilities.IsLeftHandSideExpression(node.Expression!))
        {
            var expression = VisitDestructuringAssignmentTarget(node.Expression!);
            return _factory.UpdateSpreadAssignment(node, expression);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2147-2153 — <c>visitObjectAssignmentElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>ObjectLiteralElement</c> union; the port takes the model's
    /// <see cref="Node"/> and dispatches with the reference's own three guards.</remarks>
    private Node VisitObjectAssignmentElement(Node node)
    {
        Debug.AssertNode(node, TransformationUtilities.IsObjectBindingOrAssignmentElement);
        if (NodeTests.IsSpreadAssignment(node)) return VisitAssignmentRestProperty((SpreadAssignment)node);
        if (NodeTests.IsShorthandPropertyAssignment(node)) return (Node)VisitShorthandAssignmentProperty((ShorthandPropertyAssignment)node)!;
        if (NodeTests.IsPropertyAssignment(node)) return VisitAssignmentProperty((PropertyAssignment)node);
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2155-2164 — <c>visitAssignmentPattern</c>.</summary>
    private Node VisitAssignmentPattern(Node node)
    {
        if (NodeTests.IsArrayLiteralExpression(node))
        {
            var elements = Utilities.VisitNodes(((ArrayLiteralExpression)node).Elements, VisitArrayAssignmentElementExpression, Utilities.IsExpression);
            return _factory.UpdateArrayLiteralExpression((ArrayLiteralExpression)node, elements);
        }
        else
        {
            var properties = Utilities.VisitNodes(((ObjectLiteralExpression)node).Properties, VisitObjectAssignmentElementNode, Utilities.IsObjectLiteralElementLike);
            return _factory.UpdateObjectLiteralExpression((ObjectLiteralExpression)node, properties);
        }
    }

    /// <summary>The <c>VisitArrayAssignmentElement</c> adapter the element visitor slot needs.</summary>
    /// <remarks>PORT-NOTE: TS's <c>visitNodes(node.elements, visitArrayAssignmentElement, isExpression)</c> passes the
    /// member directly; the port's element visitor is typed over <see cref="Node"/>, so this adapter states the
    /// conversion the model's class/interface boundary needs (the same §3 conversion the visitor walker makes).</remarks>
    private Node VisitArrayAssignmentElementExpression(Node node) => VisitArrayAssignmentElement(node);

    /// <summary>The <c>VisitObjectAssignmentElement</c> adapter the element visitor slot needs.</summary>
    /// <remarks>PORT-NOTE: as <see cref="VisitArrayAssignmentElementExpression"/>.</remarks>
    private Node VisitObjectAssignmentElementNode(Node node) => VisitObjectAssignmentElement(node);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2166-2178 — <c>visitExportAssignment</c>.</summary>
    /// <remarks>
    /// 16.2.3.7 RS: Evaluation<br/>
    ///   ExportDeclaration : `export` `default` AssignmentExpression `;`
    ///     1. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
    ///        a. Let _value_ be ? NamedEvaluation of |AssignmentExpression| with argument `"default"`.
    ///     ...
    /// </remarks>
    private object? VisitExportAssignment(ExportAssignment node)
    {
        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (ExportAssignment)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.Expression));
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2180-2189 — <c>visitParenthesizedExpression</c>.</summary>
    /// <remarks>
    /// 8.4.5 RS: NamedEvaluation<br/>
    ///   ParenthesizedExpression : `(` Expression `)`
    ///     ...
    ///     2. Return ? NamedEvaluation of |Expression| with argument _name_.
    /// </remarks>
    private ParenthesizedExpression VisitParenthesizedExpression(ParenthesizedExpression node, bool discarded)
    {
        Func<Node, object?> visitorFunc = discarded ? DiscardedValueVisitor : Visitor;
        var expression = Utilities.VisitNode(node.Expression, visitorFunc, Utilities.IsExpression);
        return _factory.UpdateParenthesizedExpression(node, expression);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2191-2197 — <c>visitPartiallyEmittedExpression</c>.</summary>
    /// <remarks>Emulates 8.4.5 RS: NamedEvaluation.</remarks>
    private PartiallyEmittedExpression VisitPartiallyEmittedExpression(PartiallyEmittedExpression node, bool discarded)
    {
        Func<Node, object?> visitorFunc = discarded ? DiscardedValueVisitor : Visitor;
        var expression = Utilities.VisitNode(node.Expression, visitorFunc, Utilities.IsExpression);
        return _factory.UpdatePartiallyEmittedExpression(node, expression);
    }

    // ---------------------------------------------------------------------------------------------
    // esDecorators.ts:2199-2274 — the pending-expression plumbing and the decorator transform
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2199-2216 — <c>injectPendingExpressionsCommon</c>.</summary>
    private Expression? InjectPendingExpressionsCommon(List<Expression>? pendingExpressions, Expression? expression)
    {
        if (Core.Some(pendingExpressions))
        {
            if (expression is not null)
            {
                if (NodeTests.IsParenthesizedExpression(expression))
                {
                    pendingExpressions!.Add(((ParenthesizedExpression)expression).Expression!);
                    expression = _factory.UpdateParenthesizedExpression((ParenthesizedExpression)expression, _factory.InlineExpressions(pendingExpressions));
                }
                else
                {
                    pendingExpressions!.Add(expression);
                    expression = _factory.InlineExpressions(pendingExpressions);
                }
            }
            else
            {
                expression = _factory.InlineExpressions(pendingExpressions);
            }
        }
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2218-2225 — <c>injectPendingExpressions</c>.</summary>
    private Expression InjectPendingExpressions(Expression expression)
    {
        var result = InjectPendingExpressionsCommon(_pendingExpressions, expression);
        Debug.AssertIsDefined(result);
        if (!ReferenceEquals(result, expression))
        {
            _pendingExpressions = null;
        }
        return result!;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2227-2243 — <c>injectPendingInitializers</c>.</summary>
    private Expression? InjectPendingInitializers(ClassInfo classInfo, bool isStatic, Expression? expression)
    {
        var result = InjectPendingExpressionsCommon(isStatic ? classInfo.PendingStaticInitializers : classInfo.PendingInstanceInitializers, expression);
        if (!ReferenceEquals(result, expression))
        {
            if (isStatic)
            {
                classInfo.PendingStaticInitializers = null;
            }
            else
            {
                classInfo.PendingInstanceInitializers = null;
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2245-2258 — <c>transformAllDecoratorsOfDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>map</c> over the (possibly absent) decorator array is the loop below;
    /// as in the reference, an <see cref="IAllDecorators"/> whose decorators are absent still answers an (empty) list,
    /// which is the value its callers test for <c>undefined</c>.</remarks>
    private List<Expression>? TransformAllDecoratorsOfDeclaration(IAllDecorators? allDecorators)
    {
        if (allDecorators is null)
        {
            return null;
        }

        var decoratorExpressions = new List<Expression>();
        if (allDecorators.Decorators is not null)
        {
            foreach (var decorator in allDecorators.Decorators)
            {
                decoratorExpressions.Add(TransformDecorator(decorator));
            }
        }
        return decoratorExpressions;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2260-2274 — <c>transformDecorator</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>{ target, thisArg } = factory.createCallBinding(...)</c> destructuring is the
    /// <see cref="ICallBinding"/> pair read below (<c>NodeInterfaces.cs:133</c>).</remarks>
    private Expression TransformDecorator(Decorator decorator)
    {
        var expression = Utilities.VisitNode(decorator.Expression, Visitor, Utilities.IsExpression)!;
        EmitNodes.SetEmitFlags(expression, EmitFlags.NoComments);

        // preserve the 'this' binding for an access expression
        var innerExpression = FactoryUtilities.SkipOuterExpressions(expression);
        if (Utilities.IsAccessExpression(innerExpression))
        {
            var binding = _factory.CreateCallBinding(expression, _context.HoistVariableDeclaration, _languageVersion, /*cacheIdentifiers*/ true);
            return _factory.RestoreOuterExpressions(expression, _factory.CreateFunctionBindCall(binding.Target, binding.ThisArg, []), /*kinds*/ null);
        }
        return expression;
    }
}
