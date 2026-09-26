#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classFields.ts:1088-1705 — the expression visitors: the private-name access
// and assignment helpers, the property/element access, unary, binary, call and tagged-template rewrites, and the
// `this`/`super` handling a static initializer needs.
/// <summary>ported from: src/compiler/transformers/classFields.ts:1088-1705 — the expression half of
/// <c>transformClassFields</c>.</summary>
/// <remarks>PORT-NOTE: the reference's <c>{ readExpression, initializeExpression }</c> and <c>{ thisArg, target }</c>
/// destructures are the port's tuples and <see cref="ICallBinding"/> slots; the union-typed <c>super</c> operands
/// (<c>PrefixUnaryExpression | PostfixUnaryExpression</c>, <c>SuperProperty</c>) are read through the kind tests the
/// reference's own guards perform.</remarks>
internal sealed partial class ClassFieldsTransformer
{
    /// <summary>ported from: src/compiler/transformers/classFields.ts:1088-1096 — <c>shouldForceDynamicThis</c>.</summary>
    private bool ShouldForceDynamicThis()
        => _currentClassElement is not null
            && Utilities.HasStaticModifier((Node)_currentClassElement)
            && Utilities.IsAccessor((Node)_currentClassElement)
            && Utilities.IsAutoAccessorPropertyDeclaration(Utilities.GetOriginalNode((Node)_currentClassElement)!);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1098-1107 — <c>ensureDynamicThisIfNeeded</c>.</summary>
    /// <remarks>Prevent substitution of <c>this</c> to <c>_classThis</c> in static getters and setters that wrap
    /// <c>accessor</c> fields.</remarks>
    private void EnsureDynamicThisIfNeeded(Expression node)
    {
        if (ShouldForceDynamicThis())
        {
            // do not substitute `this` with `_classThis` when `this`
            // should be bound dynamically.
            var innerExpression = FactoryUtilities.SkipOuterExpressions(node);
            if (innerExpression.Kind == SyntaxKind.ThisKeyword)
            {
                _noSubstitution.Add(innerExpression);
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1109-1113 — <c>createPrivateIdentifierAccess</c>.</summary>
    private Expression CreatePrivateIdentifierAccess(PrivateIdentifierInfoBase info, Expression receiver)
    {
        receiver = Utilities.VisitNode(receiver, Visitor, Utilities.IsExpression)!;
        EnsureDynamicThisIfNeeded(receiver);
        return CreatePrivateIdentifierAccessHelper(info, receiver);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1115-1146 — <c>createPrivateIdentifierAccessHelper</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>switch (info.kind)</c> with its <c>"untransformed"</c> and default arms is
    /// the port's type-pattern switch; the <c>Field</c> arm's <c>info.isStatic ? info.variableName : undefined</c> is the
    /// static-field variant, which is the only one that carries a storage variable.</remarks>
    private Expression CreatePrivateIdentifierAccessHelper(PrivateIdentifierInfoBase info, Expression receiver)
    {
        EmitNodes.SetCommentRange(receiver, TypeScriptTransformSupport.MoveRangePos(receiver, -1));

        switch (info)
        {
            case PrivateIdentifierAccessorInfo accessor:
                return _emitHelpers.CreateClassPrivateFieldGetHelper(
                    receiver,
                    accessor.BrandCheckIdentifier,
                    PrivateIdentifierKind.Accessor,
                    accessor.GetterName);
            case PrivateIdentifierMethodInfo method:
                return _emitHelpers.CreateClassPrivateFieldGetHelper(
                    receiver,
                    method.BrandCheckIdentifier,
                    PrivateIdentifierKind.Method,
                    method.MethodName);
            case PrivateIdentifierStaticFieldInfo staticField:
                return _emitHelpers.CreateClassPrivateFieldGetHelper(
                    receiver,
                    staticField.BrandCheckIdentifier,
                    PrivateIdentifierKind.Field,
                    staticField.VariableName);
            case PrivateIdentifierInstanceFieldInfo instanceField:
                return _emitHelpers.CreateClassPrivateFieldGetHelper(
                    receiver,
                    instanceField.BrandCheckIdentifier,
                    PrivateIdentifierKind.Field,
                    /*f*/ null);
            default:
                return Debug.Fail<Expression>("Access helpers should not be created for untransformed private elements");
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1148-1186 — <c>visitPropertyAccessExpression</c>.</summary>
    private object? VisitPropertyAccessExpression(PropertyAccessExpression node)
    {
        if (NodeTests.IsPrivateIdentifier(node.Name!))
        {
            var privateIdentifierInfo = AccessPrivateIdentifier((PrivateIdentifier)node.Name!);
            if (privateIdentifierInfo is not null)
            {
                return Utilities.SetTextRange(
                    NodeFactory.SetOriginalNode(
                        CreatePrivateIdentifierAccess(privateIdentifierInfo, node.Expression!),
                        node),
                    node);
            }
        }
        if (
            _shouldTransformSuperInStaticInitializers
            && _currentClassElement is not null
            && Utilities.IsSuperProperty(node)
            && NodeTests.IsIdentifier(node.Name!)
            && ClassFieldsTransform.IsStaticPropertyDeclarationOrClassStaticBlock((Node)_currentClassElement)
            && _lexicalEnvironment?.Data is not null)
        {
            var data = _lexicalEnvironment.Data;
            if ((data.Facts & ClassFieldsFacts.ClassWasDecorated) != 0)
            {
                return VisitInvalidSuperProperty(node);
            }
            if (data.ClassConstructor is not null && data.SuperClassReference is not null)
            {
                // converts `super.x` into `Reflect.get(_baseTemp, "x", _classTemp)`
                var superProperty = _factory.CreateReflectGetCall(
                    data.SuperClassReference,
                    _factory.CreateStringLiteralFromNode(node.Name, /*isSingleQuote*/ null),
                    data.ClassConstructor);
                NodeFactory.SetOriginalNode(superProperty, node.Expression);
                Utilities.SetTextRange(superProperty, node.Expression);
                return superProperty;
            }
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1188-1214 — <c>visitElementAccessExpression</c>.</summary>
    private object? VisitElementAccessExpression(ElementAccessExpression node)
    {
        if (
            _shouldTransformSuperInStaticInitializers
            && _currentClassElement is not null
            && Utilities.IsSuperProperty(node)
            && ClassFieldsTransform.IsStaticPropertyDeclarationOrClassStaticBlock((Node)_currentClassElement)
            && _lexicalEnvironment?.Data is not null)
        {
            var data = _lexicalEnvironment.Data;
            if ((data.Facts & ClassFieldsFacts.ClassWasDecorated) != 0)
            {
                return VisitInvalidSuperProperty(node);
            }

            if (data.ClassConstructor is not null && data.SuperClassReference is not null)
            {
                // converts `super[x]` into `Reflect.get(_baseTemp, x, _classTemp)`
                var superProperty = _factory.CreateReflectGetCall(
                    data.SuperClassReference,
                    Utilities.VisitNode(node.ArgumentExpression, Visitor, Utilities.IsExpression),
                    data.ClassConstructor);
                NodeFactory.SetOriginalNode(superProperty, node.Expression);
                Utilities.SetTextRange(superProperty, node.Expression);
                return superProperty;
            }
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1216-1306 — <c>visitPreOrPostfixUnaryExpression</c>.</summary>
    private object? VisitPreOrPostfixUnaryExpression(Node node, bool discarded)
    {
        var operatorKind = node.Kind == SyntaxKind.PrefixUnaryExpression
            ? ((PrefixUnaryExpression)node).Operator
            : ((PostfixUnaryExpression)node).Operator;
        var operandNode = node.Kind == SyntaxKind.PrefixUnaryExpression
            ? ((PrefixUnaryExpression)node).Operand!
            : ((PostfixUnaryExpression)node).Operand!;

        if (operatorKind == SyntaxKind.PlusPlusToken || operatorKind == SyntaxKind.MinusMinusToken)
        {
            var operand = Utilities.SkipParentheses(operandNode);

            if (Utilities.IsPrivateIdentifierPropertyAccessExpression(operand))
            {
                var privateAccess = (PropertyAccessExpression)operand;
                var info = AccessPrivateIdentifier((PrivateIdentifier)privateAccess.Name!);
                if (info is not null)
                {
                    var receiver = Utilities.VisitNode(privateAccess.Expression, Visitor, Utilities.IsExpression)!;
                    EnsureDynamicThisIfNeeded(receiver);
                    var (readExpression, initializeExpression) = CreateCopiableReceiverExpr(receiver);

                    Expression expression = CreatePrivateIdentifierAccess(info, readExpression);
                    var temp = node.Kind == SyntaxKind.PrefixUnaryExpression || discarded
                        ? null
                        : _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                    expression = FactoryUtilities.ExpandPreOrPostfixIncrementOrDecrementExpression(
                        _factory,
                        node,
                        expression,
                        _context.HoistVariableDeclaration,
                        temp);
                    expression = CreatePrivateIdentifierAssignment(
                        info,
                        initializeExpression ?? readExpression,
                        expression,
                        SyntaxKind.EqualsToken);
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
            else if (
                _shouldTransformSuperInStaticInitializers
                && _currentClassElement is not null
                && Utilities.IsSuperProperty(operand)
                && ClassFieldsTransform.IsStaticPropertyDeclarationOrClassStaticBlock((Node)_currentClassElement)
                && _lexicalEnvironment?.Data is not null)
            {
                // converts `++super.a` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = ++_a), _classTemp), _b)`
                // converts `++super[f()]` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = ++_b), _classTemp), _c)`
                // converts `--super.a` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = --_a), _classTemp), _b)`
                // converts `--super[f()]` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = --_b), _classTemp), _c)`
                // converts `super.a++` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = _a++), _classTemp), _b)`
                // converts `super[f()]++` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = _b++), _classTemp), _c)`
                // converts `super.a--` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = _a--), _classTemp), _b)`
                // converts `super[f()]--` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = _b--), _classTemp), _c)`
                var data = _lexicalEnvironment.Data;
                if ((data.Facts & ClassFieldsFacts.ClassWasDecorated) != 0)
                {
                    var expression = VisitInvalidSuperProperty(operand);
                    return node.Kind == SyntaxKind.PrefixUnaryExpression
                        ? _factory.UpdatePrefixUnaryExpression((PrefixUnaryExpression)node, expression)
                        : _factory.UpdatePostfixUnaryExpression((PostfixUnaryExpression)node, expression);
                }
                if (data.ClassConstructor is not null && data.SuperClassReference is not null)
                {
                    Expression? setterName = null;
                    Expression? getterName = null;
                    if (NodeTests.IsPropertyAccessExpression(operand))
                    {
                        if (NodeTests.IsIdentifier(((PropertyAccessExpression)operand).Name!))
                        {
                            getterName = setterName = _factory.CreateStringLiteralFromNode(((PropertyAccessExpression)operand).Name, /*isSingleQuote*/ null);
                        }
                    }
                    else
                    {
                        var argumentExpression = ((ElementAccessExpression)operand).ArgumentExpression!;
                        if (TransformationUtilities.IsSimpleInlineableExpression(argumentExpression))
                        {
                            getterName = setterName = argumentExpression;
                        }
                        else
                        {
                            getterName = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                            setterName = _factory.CreateAssignment(getterName, Utilities.VisitNode(argumentExpression, Visitor, Utilities.IsExpression));
                        }
                    }
                    if (setterName is not null && getterName is not null)
                    {
                        Expression expression = _factory.CreateReflectGetCall(data.SuperClassReference, getterName, data.ClassConstructor);
                        Utilities.SetTextRange(expression, operand);

                        var temp = discarded ? null : _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                        expression = FactoryUtilities.ExpandPreOrPostfixIncrementOrDecrementExpression(
                            _factory,
                            node,
                            expression,
                            _context.HoistVariableDeclaration,
                            temp);
                        expression = _factory.CreateReflectSetCall(data.SuperClassReference, setterName, expression, data.ClassConstructor);
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
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1325-1336 — <c>createCopiableReceiverExpr</c>.</summary>
    private (Expression ReadExpression, Expression? InitializeExpression) CreateCopiableReceiverExpr(Expression receiver)
    {
        var clone = Utilities.NodeIsSynthesized(receiver) ? receiver : _factory.CloneNode(receiver);
        if (receiver.Kind == SyntaxKind.ThisKeyword && _noSubstitution.Contains(receiver))
        {
            _noSubstitution.Add(clone);
        }
        if (TransformationUtilities.IsSimpleInlineableExpression(receiver))
        {
            return (clone, null);
        }
        var readExpression = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
        var initializeExpression = _factory.CreateAssignment(readExpression, clone);
        return (readExpression, initializeExpression);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1338-1386 — <c>visitCallExpression</c>.</summary>
    private object? VisitCallExpression(CallExpression node)
    {
        if (
            Utilities.IsPrivateIdentifierPropertyAccessExpression(node.Expression!)
            && AccessPrivateIdentifier((PrivateIdentifier)((PropertyAccessExpression)node.Expression!).Name!) is not null)
        {
            // obj.#x()

            // Transform call expressions of private names to properly bind the `this` parameter.
            var binding = _factory.CreateCallBinding(node.Expression!, _context.HoistVariableDeclaration, _languageVersion, /*cacheIdentifiers*/ null);
            var thisArg = binding.ThisArg!;
            var target = binding.Target!;
            var argumentsList = new List<Expression>
            {
                Utilities.VisitNode(thisArg, Visitor, Utilities.IsExpression)!,
            };
            argumentsList.AddRange(Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression)!);

            if (Utilities.IsCallChain(node))
            {
                return _factory.UpdateCallChain(
                    node,
                    _factory.CreatePropertyAccessChain(Utilities.VisitNode(target, Visitor, Utilities.IsExpression), node.QuestionDotToken, "call"),
                    /*questionDotToken*/ null,
                    /*typeArguments*/ null,
                    argumentsList);
            }
            return _factory.UpdateCallExpression(
                node,
                _factory.CreatePropertyAccessExpression(Utilities.VisitNode(target, Visitor, Utilities.IsExpression), "call"),
                /*typeArguments*/ null,
                argumentsList);
        }

        if (
            _shouldTransformSuperInStaticInitializers
            && _currentClassElement is not null
            && Utilities.IsSuperProperty(node.Expression!)
            && ClassFieldsTransform.IsStaticPropertyDeclarationOrClassStaticBlock((Node)_currentClassElement)
            && _lexicalEnvironment?.Data?.ClassConstructor is not null)
        {
            // super.x()
            // super[x]()

            // converts `super.f(...)` into `Reflect.get(_baseTemp, "f", _classTemp).call(_classTemp, ...)`
            var invocation = _factory.CreateFunctionCallCall(
                Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)!,
                _lexicalEnvironment.Data.ClassConstructor,
                Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression));
            NodeFactory.SetOriginalNode(invocation, node);
            Utilities.SetTextRange(invocation, node);
            return invocation;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1388-1429 — <c>visitTaggedTemplateExpression</c>.</summary>
    private object? VisitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        if (
            Utilities.IsPrivateIdentifierPropertyAccessExpression(node.Tag!)
            && AccessPrivateIdentifier((PrivateIdentifier)((PropertyAccessExpression)node.Tag!).Name!) is not null)
        {
            // Bind the `this` correctly for tagged template literals when the tag is a private identifier property access.
            var binding = _factory.CreateCallBinding(node.Tag!, _context.HoistVariableDeclaration, _languageVersion, /*cacheIdentifiers*/ null);
            return _factory.UpdateTaggedTemplateExpression(
                node,
                _factory.CreateCallExpression(
                    _factory.CreatePropertyAccessExpression(Utilities.VisitNode(binding.Target!, Visitor, Utilities.IsExpression), "bind"),
                    /*typeArguments*/ null,
                    [Utilities.VisitNode(binding.ThisArg!, Visitor, Utilities.IsExpression)!]),
                /*typeArguments*/ null,
                Utilities.VisitNode(node.Template, Visitor, Utilities.IsTemplateLiteral));
        }
        if (
            _shouldTransformSuperInStaticInitializers
            && _currentClassElement is not null
            && Utilities.IsSuperProperty(node.Tag!)
            && ClassFieldsTransform.IsStaticPropertyDeclarationOrClassStaticBlock((Node)_currentClassElement)
            && _lexicalEnvironment?.Data?.ClassConstructor is not null)
        {
            // converts `` super.f`x` `` into `` Reflect.get(_baseTemp, "f", _classTemp).bind(_classTemp)`x` ``
            var invocation = _factory.CreateFunctionBindCall(
                Utilities.VisitNode(node.Tag, Visitor, Utilities.IsExpression)!,
                _lexicalEnvironment.Data.ClassConstructor,
                []);
            NodeFactory.SetOriginalNode(invocation, node);
            Utilities.SetTextRange(invocation, node);
            return _factory.UpdateTaggedTemplateExpression(
                node,
                invocation,
                /*typeArguments*/ null,
                Utilities.VisitNode(node.Template, Visitor, Utilities.IsTemplateLiteral));
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1489-1636 — <c>visitBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>compact([...pendingExpressions, node])</c> drops the slots TS's <c>undefined</c>
    /// could occupy; every slot <c>getPendingExpressions()</c> pushes is an expression, so the port builds the same list
    /// without the filter.
    /// </remarks>
    private object? VisitBinaryExpression(BinaryExpression node, bool discarded)
    {
        if (Utilities.IsDestructuringAssignment(node))
        {
            // ({ x: obj.#x } = ...)
            // ({ x: super.x } = ...)
            // ({ x: super[x] } = ...)
            var savedPendingExpressions = _pendingExpressions;
            _pendingExpressions = null;
            node = _factory.UpdateBinaryExpression(
                node,
                Utilities.VisitNode(node.Left, AssignmentTargetVisitor, Utilities.IsExpression),
                node.OperatorToken,
                Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression));
            object expr = Core.Some(_pendingExpressions)
                ? _factory.InlineExpressions([.. _pendingExpressions!, node])
                : node;
            _pendingExpressions = savedPendingExpressions;
            return expr;
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

            if (Utilities.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
            {
                node = (BinaryExpression)NamedEvaluation.TransformNamedEvaluation(_context, node);
                Debug.AssertNode(node, n => Utilities.IsAssignmentExpression(n));
            }

            var left = FactoryUtilities.SkipOuterExpressions(node.Left!, OuterExpressionKinds.PartiallyEmittedExpressions | OuterExpressionKinds.Parentheses);
            if (Utilities.IsPrivateIdentifierPropertyAccessExpression(left))
            {
                // obj.#x = ...
                var info = AccessPrivateIdentifier((PrivateIdentifier)((PropertyAccessExpression)left).Name!);
                if (info is not null)
                {
                    return Utilities.SetTextRange(
                        NodeFactory.SetOriginalNode(
                            CreatePrivateIdentifierAssignment(info, ((PropertyAccessExpression)left).Expression!, node.Right!, node.OperatorToken!.Kind),
                            node),
                        node);
                }
            }
            else if (
                _shouldTransformSuperInStaticInitializers
                && _currentClassElement is not null
                && Utilities.IsSuperProperty(node.Left!)
                && ClassFieldsTransform.IsStaticPropertyDeclarationOrClassStaticBlock((Node)_currentClassElement)
                && _lexicalEnvironment?.Data is not null)
            {
                // super.x = ...
                // super[x] = ...
                // super.x += ...
                // super.x -= ...
                var data = _lexicalEnvironment.Data;
                if ((data.Facts & ClassFieldsFacts.ClassWasDecorated) != 0)
                {
                    return _factory.UpdateBinaryExpression(
                        node,
                        VisitInvalidSuperProperty(node.Left!),
                        node.OperatorToken,
                        Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression));
                }
                if (data.ClassConstructor is not null && data.SuperClassReference is not null)
                {
                    Expression? setterName = NodeTests.IsElementAccessExpression(node.Left!)
                        ? Utilities.VisitNode(((ElementAccessExpression)node.Left!).ArgumentExpression, Visitor, Utilities.IsExpression)
                        : NodeTests.IsIdentifier(((PropertyAccessExpression)node.Left!).Name!)
                            ? _factory.CreateStringLiteralFromNode(((PropertyAccessExpression)node.Left!).Name, /*isSingleQuote*/ null)
                            : null;
                    if (setterName is not null)
                    {
                        // converts `super.x = 1` into `(Reflect.set(_baseTemp, "x", _a = 1, _classTemp), _a)`
                        // converts `super[f()] = 1` into `(Reflect.set(_baseTemp, f(), _a = 1, _classTemp), _a)`
                        // converts `super.x += 1` into `(Reflect.set(_baseTemp, "x", _a = Reflect.get(_baseTemp, "x", _classtemp) + 1, _classTemp), _a)`
                        // converts `super[f()] += 1` into `(Reflect.set(_baseTemp, _a = f(), _b = Reflect.get(_baseTemp, _a, _classtemp) + 1, _classTemp), _b)`

                        Expression expression = Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression)!;
                        if (TransformationUtilities.IsCompoundAssignment(node.OperatorToken!.Kind))
                        {
                            var getterName = setterName;
                            if (!TransformationUtilities.IsSimpleInlineableExpression(setterName))
                            {
                                getterName = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                                setterName = _factory.CreateAssignment(getterName, setterName);
                            }
                            var superPropertyGet = _factory.CreateReflectGetCall(
                                data.SuperClassReference,
                                getterName,
                                data.ClassConstructor);
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
                            data.SuperClassReference,
                            setterName,
                            expression,
                            data.ClassConstructor);
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
        }
        if (ClassFieldsTransform.IsPrivateIdentifierInExpression(node))
        {
            // #x in obj
            return TransformPrivateIdentifierInInExpression(node);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1638-1643 — <c>visitCommaListExpression</c>.</summary>
    private object? VisitCommaListExpression(CommaListExpression node, bool discarded)
    {
        var elements = discarded
            ? Utilities.VisitCommaListElements(node.Elements!, DiscardedValueVisitor)
            : Utilities.VisitCommaListElements(node.Elements!, Visitor, DiscardedValueVisitor);
        return _factory.UpdateCommaListExpression(node, elements);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1645-1655 — <c>visitParenthesizedExpression</c>.</summary>
    private object? VisitParenthesizedExpression(ParenthesizedExpression node, bool discarded)
    {
        // 8.4.5 RS: NamedEvaluation
        //   ParenthesizedExpression : `(` Expression `)`
        //     ...
        //     2. Return ? NamedEvaluation of |Expression| with argument _name_.

        Func<Node, object?> visitorFunc = discarded ? DiscardedValueVisitor : Visitor;
        var expression = Utilities.VisitNode(node.Expression, visitorFunc, Utilities.IsExpression);
        return _factory.UpdateParenthesizedExpression(node, expression);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1657-1705 — <c>createPrivateIdentifierAssignment</c>.</summary>
    private Expression CreatePrivateIdentifierAssignment(PrivateIdentifierInfoBase info, Expression receiver, Expression right, SyntaxKind @operator)
    {
        receiver = Utilities.VisitNode(receiver, Visitor, Utilities.IsExpression)!;
        right = Utilities.VisitNode(right, Visitor, Utilities.IsExpression)!;
        EnsureDynamicThisIfNeeded(receiver);

        if (TransformationUtilities.IsCompoundAssignment(@operator))
        {
            var (readExpression, initializeExpression) = CreateCopiableReceiverExpr(receiver);
            receiver = initializeExpression ?? readExpression;
            right = _factory.CreateBinaryExpression(
                CreatePrivateIdentifierAccessHelper(info, readExpression),
                TransformationUtilities.GetNonAssignmentOperatorForCompoundAssignment(@operator),
                right);
        }

        EmitNodes.SetCommentRange(receiver, TypeScriptTransformSupport.MoveRangePos(receiver, -1));

        switch (info)
        {
            case PrivateIdentifierAccessorInfo accessor:
                return _emitHelpers.CreateClassPrivateFieldSetHelper(
                    receiver,
                    accessor.BrandCheckIdentifier,
                    right,
                    PrivateIdentifierKind.Accessor,
                    accessor.SetterName);
            case PrivateIdentifierMethodInfo method:
                return _emitHelpers.CreateClassPrivateFieldSetHelper(
                    receiver,
                    method.BrandCheckIdentifier,
                    right,
                    PrivateIdentifierKind.Method,
                    /*f*/ null);
            case PrivateIdentifierStaticFieldInfo staticField:
                return _emitHelpers.CreateClassPrivateFieldSetHelper(
                    receiver,
                    staticField.BrandCheckIdentifier,
                    right,
                    PrivateIdentifierKind.Field,
                    staticField.VariableName);
            case PrivateIdentifierInstanceFieldInfo instanceField:
                return _emitHelpers.CreateClassPrivateFieldSetHelper(
                    receiver,
                    instanceField.BrandCheckIdentifier,
                    right,
                    PrivateIdentifierKind.Field,
                    /*f*/ null);
            default:
                return Debug.Fail<Expression>("Access helpers should not be created for untransformed private elements");
        }
    }
}
