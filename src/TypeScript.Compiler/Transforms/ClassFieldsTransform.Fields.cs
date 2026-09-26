#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classFields.ts:903-1086 (auto-accessors and the field initializers),
// :1308-1323 (the two statement visitors), :1431-1487 (the static block and the anonymous-class predicate),
// :2434-2722 (the initializer statements, the property transform, the invalid-`super` helper and the computed-name
// cache) and :2724-2753 (the lexical-environment accessors).
/// <summary>ported from: src/compiler/transformers/classFields.ts:903-2753 — the field/property half of
/// <c>transformClassFields</c>.</summary>
/// <remarks>
/// PORT-NOTE (the accessor modifiers): the reference hands <c>factory.createModifiersFromModifierFlags(&hellip;)</c>'s
/// <c>Modifier[]</c> to the two redirector factories, whose port parameters are <c>List&lt;Node&gt;?</c>
/// (§12.5 — <c>ModifierLike</c> collapses to <c>Node</c>); the port projects the elements, which is the same list of
/// the same nodes.
/// </remarks>
internal sealed partial class ClassFieldsTransformer
{
    /// <summary>ported from: src/compiler/transformers/classFields.ts:908-961 — <c>transformAutoAccessor</c>.</summary>
    /// <remarks>
    /// transforms:
    /// <code>
    /// accessor x = 1;
    /// </code>
    /// into:
    /// <code>
    /// #x = 1;
    /// get x() { return this.#x; }
    /// set x(value) { this.#x = value; }
    /// </code>
    /// </remarks>
    private object? TransformAutoAccessor(PropertyDeclaration node)
    {
        var commentRange = EmitNodes.GetCommentRange(node);
        var sourceMapRange = EmitNodes.GetSourceMapRange(node);

        // Since we're creating two declarations where there was previously one, cache
        // the expression for any computed property names.
        var name = node.Name!;
        Node getterName = name;
        Node setterName = name;
        if (NodeTests.IsComputedPropertyName(name) && !TransformationUtilities.IsSimpleInlineableExpression(((ComputedPropertyName)name).Expression!))
        {
            var cacheAssignment = FactoryUtilities.FindComputedPropertyNameCacheAssignment((ComputedPropertyName)name);
            if (cacheAssignment is not null)
            {
                getterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)name, Utilities.VisitNode(((ComputedPropertyName)name).Expression, Visitor, Utilities.IsExpression));
                setterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)name, cacheAssignment.Left);
            }
            else
            {
                var temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                EmitNodes.SetSourceMapRange(temp, TypeScriptTransformSupport.SourceMapRangeOf(((ComputedPropertyName)name).Expression!));
                var expression = Utilities.VisitNode(((ComputedPropertyName)name).Expression, Visitor, Utilities.IsExpression);
                var assignment = _factory.CreateAssignment(temp, expression);
                EmitNodes.SetSourceMapRange(assignment, TypeScriptTransformSupport.SourceMapRangeOf(((ComputedPropertyName)name).Expression!));
                getterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)name, assignment);
                setterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)name, temp);
            }
        }

        var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
        var backingField = FactoryUtilities.CreateAccessorPropertyBackingField(_factory, node, modifiers, node.Initializer);
        NodeFactory.SetOriginalNode(backingField, node);
        EmitNodes.SetEmitFlags(backingField, EmitFlags.NoComments);
        EmitNodes.SetSourceMapRange(backingField, TypeScriptTransformSupport.SourceMapRangeOf(sourceMapRange));

        Expression receiver = Utilities.IsStatic(node) ? (Expression?)TryGetClassThis() ?? _factory.CreateThis() : _factory.CreateThis();
        var getter = FactoryUtilities.CreateAccessorPropertyGetRedirector(_factory, node, modifiers, getterName, receiver);
        NodeFactory.SetOriginalNode(getter, node);
        EmitNodes.SetCommentRange(getter, commentRange);
        EmitNodes.SetSourceMapRange(getter, TypeScriptTransformSupport.SourceMapRangeOf(sourceMapRange));

        // create a fresh copy of the modifiers so that we don't duplicate comments
        var setterModifiers = _factory.CreateModifiersFromModifierFlags(Utilities.ModifiersToFlags(modifiers));
        var setter = FactoryUtilities.CreateAccessorPropertySetRedirector(
            _factory,
            node,
            setterModifiers is null ? null : [.. setterModifiers.Cast<Node>()],
            setterName,
            receiver);
        NodeFactory.SetOriginalNode(setter, node);
        EmitNodes.SetEmitFlags(setter, EmitFlags.NoComments);
        EmitNodes.SetSourceMapRange(setter, TypeScriptTransformSupport.SourceMapRangeOf(sourceMapRange));

        return Utilities.VisitArray(
            new NodeArray<Node>([backingField, getter, setter]),
            AccessorFieldResultVisitor,
            Utilities.IsClassElement);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:963-1014 — <c>transformPrivateFieldInitializer</c>.</summary>
    private object? TransformPrivateFieldInitializer(PropertyDeclaration node)
    {
        if (ShouldTransformClassElementToWeakMap(node))
        {
            // If we are transforming private elements into WeakMap/WeakSet, we should elide the node.
            var info = AccessPrivateIdentifier((PrivateIdentifier)node.Name!);
            Debug.Assert(info is not null, "Undeclared private name for property declaration.");

            // Leave invalid code untransformed
            if (!info!.IsValid)
            {
                return node;
            }

            // If we encounter a valid private static field and we're not transforming
            // class static blocks, initialize it
            if (info.IsStatic && !_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                var statement = TransformPropertyOrClassStaticBlock(node, _factory.CreateThis());
                if (statement is not null)
                {
                    return _factory.CreateClassStaticBlockDeclaration(_factory.CreateBlock([statement], /*multiLine*/ true));
                }
            }

            return null;
        }

        if (
            _shouldTransformInitializersUsingSet
            && !Utilities.IsStatic(node)
            && _lexicalEnvironment?.Data is not null
            && (_lexicalEnvironment.Data.Facts & ClassFieldsFacts.WillHoistInitializersToConstructor) != 0)
        {
            // If we are transforming initializers using Set semantics we will elide the initializer as it will
            // be moved to the constructor to preserve evaluation order next to public instance fields. We don't
            // need to do this transformation for private static fields since public static fields can be
            // transformed into `static {}` blocks.
            return _factory.UpdatePropertyDeclaration(
                node,
                Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike),
                node.Name,
                /*questionOrExclamationToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
        }

        if (Utilities.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (PropertyDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, node);
        }

        return _factory.UpdatePropertyDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
            Utilities.VisitNode(node.Name, PropertyNameVisitor, Utilities.IsPropertyName),
            /*questionOrExclamationToken*/ null,
            /*type*/ null,
            Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1016-1060 — <c>transformPublicFieldInitializer</c>.</summary>
    private object? TransformPublicFieldInitializer(PropertyDeclaration node)
    {
        if (_shouldTransformInitializers && !Utilities.IsAutoAccessorPropertyDeclaration(node))
        {
            // Create a temporary variable to store a computed property name (if necessary).
            // If it's not inlineable, then we emit an expression after the class which assigns
            // the property name to the temporary variable.

            var expr = GetPropertyNameExpressionIfNeeded(
                node.Name!,
                /*shouldHoist*/ node.Initializer is not null || _useDefineForClassFields);
            if (expr is not null)
            {
                GetPendingExpressions().AddRange(FactoryUtilities.FlattenCommaList(expr));
            }

            if (Utilities.IsStatic(node) && !_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                var initializerStatement = TransformPropertyOrClassStaticBlock(node, _factory.CreateThis());
                if (initializerStatement is not null)
                {
                    var staticBlock = _factory.CreateClassStaticBlockDeclaration(
                        _factory.CreateBlock([initializerStatement], /*multiLine*/ null));

                    NodeFactory.SetOriginalNode(staticBlock, node);
                    EmitNodes.SetCommentRange(staticBlock, node);

                    // Set the comment range for the statement to an empty synthetic range
                    // and drop synthetic comments from the statement to avoid printing them twice.
                    EmitNodes.SetCommentRange((Node)initializerStatement, TypeScriptTransformSupport.CreateRange(-1, -1));
                    EmitNodes.SetSyntheticLeadingComments((Node)initializerStatement, null);
                    EmitNodes.SetSyntheticTrailingComments((Node)initializerStatement, null);
                    return staticBlock;
                }
            }

            return null;
        }

        return _factory.UpdatePropertyDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
            Utilities.VisitNode(node.Name, PropertyNameVisitor, Utilities.IsPropertyName),
            /*questionOrExclamationToken*/ null,
            /*type*/ null,
            Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1062-1067 — <c>transformFieldInitializer</c>.</summary>
    private object? TransformFieldInitializer(PropertyDeclaration node)
    {
        Debug.Assert(!Utilities.HasDecorators(node), "Decorators should already have been transformed and elided.");
        return Utilities.IsPrivateIdentifierClassElementDeclaration(node)
            ? TransformPrivateFieldInitializer(node)
            : TransformPublicFieldInitializer(node);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1069-1073 — <c>shouldTransformAutoAccessorsInCurrentClass</c>.</summary>
    private bool ShouldTransformAutoAccessorsInCurrentClass()
        => _shouldTransformAutoAccessors == Ternary.True
            || _shouldTransformAutoAccessors == Ternary.Maybe
                && _lexicalEnvironment?.Data is not null
                && (_lexicalEnvironment.Data.Facts & ClassFieldsFacts.WillHoistInitializersToConstructor) != 0;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1075-1086 — <c>visitPropertyDeclaration</c>.</summary>
    private object? VisitPropertyDeclaration(PropertyDeclaration node)
    {
        // If this is an auto-accessor, we defer to `transformAutoAccessor`. That function
        // will in turn call `transformFieldInitializer` as needed.
        if (
            Utilities.IsAutoAccessorPropertyDeclaration(node)
            && (ShouldTransformAutoAccessorsInCurrentClass()
                || Utilities.HasStaticModifier(node) && (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TransformPrivateStaticElements) != 0))
        {
            return TransformAutoAccessor(node);
        }

        return TransformFieldInitializer(node);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1308-1316 — <c>visitForStatement</c>.</summary>
    private object? VisitForStatement(ForStatement node)
        => _factory.UpdateForStatement(
            node,
            Utilities.VisitNode(node.Initializer, DiscardedValueVisitor, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.Incrementor, DiscardedValueVisitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, Visitor, _context));

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1318-1323 — <c>visitExpressionStatement</c>.</summary>
    private object? VisitExpressionStatement(ExpressionStatement node)
        => _factory.UpdateExpressionStatement(
            node,
            Utilities.VisitNode(node.Expression, DiscardedValueVisitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1431-1469 — <c>transformClassStaticBlockDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's inferred return type is <c>Expression | undefined</c> (its three arms answer
    /// <c>visitNode(…, isExpression)</c>'s result, an IIFE, and <c>undefined</c>), so the port declares
    /// <see cref="Expression"/>-typed and the two readers that only push a statement or an expression are unchanged.</remarks>
    private Expression? TransformClassStaticBlockDeclaration(ClassStaticBlockDeclaration node)
    {
        if (_lexicalEnvironment is not null)
        {
            _lexicalEnvironmentMap[Utilities.GetOriginalNode(node)!] = _lexicalEnvironment;
        }

        if (_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            if (ClassThis.IsClassThisAssignmentBlock(node))
            {
                var result = Utilities.VisitNode(((ExpressionStatement)node.Body!.Statements![0]).Expression, Visitor, Utilities.IsExpression);
                // If the generated `_classThis` assignment is a noop (i.e., `_classThis = _classThis`), we can
                // eliminate the expression
                if (
                    result is not null
                    && Utilities.IsAssignmentExpression(result, /*excludeCompoundAssignment*/ true)
                    && ReferenceEquals(((BinaryExpression)result).Left, ((BinaryExpression)result).Right))
                {
                    return null;
                }
                return result;
            }

            if (NamedEvaluation.IsClassNamedEvaluationHelperBlock(node))
            {
                return Utilities.VisitNode(((ExpressionStatement)node.Body!.Statements![0]).Expression, Visitor, Utilities.IsExpression);
            }

            _context.StartLexicalEnvironment();
            var statements = SetCurrentClassElementAnd<NodeArray<IStatement>, NodeArray<IStatement>>(
                (IClassElement)node,
                s => Utilities.VisitNodes(s, Visitor, Utilities.IsStatement)!,
                node.Body!.Statements!);
            statements = _factory.MergeLexicalEnvironment(statements, _context.EndLexicalEnvironment());

            var iife = _factory.CreateImmediatelyInvokedArrowFunction(statements);
            NodeFactory.SetOriginalNode(Utilities.SkipParentheses(iife.Expression!), node);
            EmitNodes.AddEmitFlags(Utilities.SkipParentheses(iife.Expression!), EmitFlags.AdviseOnEmitNode);
            NodeFactory.SetOriginalNode(iife, node);
            Utilities.SetTextRange(iife, node);
            return iife;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1471-1487 — <c>isAnonymousClassNeedingAssignedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (<c>&amp;&amp;</c>, not <c>&amp;</c>): the reference's
    /// <c>!!(getInternalEmitFlags(node) &amp;&amp; InternalEmitFlags.TransformPrivateStaticElements)</c> is the JS
    /// number-valued and, whose truthiness is that of <c>getInternalEmitFlags(node)</c> alone (the flag's value is
    /// <c>1 &lt;&lt; 5</c>, never zero) — this is <em>not</em> the bitmask test the same phrase states at
    /// classFields.ts:1521, so the port keeps the reference's own reading rather than the one it looks like.
    /// </remarks>
    private bool IsAnonymousClassNeedingAssignedName(Node node)
    {
        if (NodeTests.IsClassExpression(node) && ((ClassExpression)node).Name is null)
        {
            var staticPropertiesOrClassStaticBlocks = TransformationUtilities.GetStaticPropertiesAndClassStaticBlock(node);
            if (staticPropertiesOrClassStaticBlocks.Any(member => NamedEvaluation.IsClassNamedEvaluationHelperBlock((Node)member)))
            {
                return false;
            }

            var hasTransformableStatics = (_shouldTransformPrivateElementsOrClassStaticBlocks
                || ES2015TransformSupport.GetInternalEmitFlags(node) != 0)
                && staticPropertiesOrClassStaticBlocks.Any(member =>
                    NodeTests.IsClassStaticBlockDeclaration((Node)member)
                    || Utilities.IsPrivateIdentifierClassElementDeclaration((Node)member)
                    || _shouldTransformInitializers && TransformationUtilities.IsInitializedProperty(member));
            return hasTransformableStatics;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2434-2447 — <c>addPropertyOrClassStaticBlockStatements</c>.</summary>
    /// <remarks>
    /// Generates assignment statements for property initializers.
    /// </remarks>
    /// <param name="statements">The statement list to append each initializer's statement to.</param>
    /// <param name="properties">An array of property declarations to transform.</param>
    /// <param name="receiver">The receiver on which each property should be assigned.</param>
    private void AddPropertyOrClassStaticBlockStatements(List<IStatement> statements, IReadOnlyList<IClassElement> properties, Expression receiver)
    {
        foreach (var property in properties)
        {
            if (Utilities.IsStatic((Node)property) && !_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                continue;
            }

            var statement = TransformPropertyOrClassStaticBlock(property, receiver);
            if (statement is null)
            {
                continue;
            }

            statements.Add(statement);
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2449-2491 — <c>transformPropertyOrClassStaticBlock</c>.</summary>
    private IStatement? TransformPropertyOrClassStaticBlock(IClassElement property, Expression receiver)
    {
        var expression = NodeTests.IsClassStaticBlockDeclaration((Node)property)
            ? SetCurrentClassElementAnd(property, TransformClassStaticBlockDeclaration, (ClassStaticBlockDeclaration)property)
            : TransformProperty((PropertyDeclaration)property, receiver);
        if (expression is null)
        {
            return null;
        }
        var result = expression;

        var statement = _factory.CreateExpressionStatement(result);
        NodeFactory.SetOriginalNode(statement, (Node)property);
        EmitNodes.AddEmitFlags(statement, Utilities.GetEmitFlags((Node)property) & EmitFlags.NoComments);
        EmitNodes.SetCommentRange(statement, (Node)property);

        var propertyOriginalNode = Utilities.GetOriginalNode((Node)property)!;
        if (NodeTests.IsParameter(propertyOriginalNode))
        {
            // replicate comment and source map behavior from the ts transform for parameter properties.
            EmitNodes.SetSourceMapRange(statement, TypeScriptTransformSupport.SourceMapRangeOf(propertyOriginalNode));
            EmitNodes.RemoveAllComments(statement);
        }
        else
        {
            EmitNodes.SetSourceMapRange(statement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers((Node)property)));
        }

        // `setOriginalNode` *copies* the `emitNode` from `property`, so now both
        // `statement` and `expression` have a copy of the synthesized comments.
        // Drop the comments from expression to avoid printing them twice.
        EmitNodes.SetSyntheticLeadingComments(result, null);
        EmitNodes.SetSyntheticTrailingComments(result, null);

        // If the property was originally an auto-accessor, don't emit comments here since they will be attached to
        // the synthezized getter.
        if (Utilities.HasAccessorModifier(propertyOriginalNode))
        {
            EmitNodes.AddEmitFlags(statement, EmitFlags.NoComments);
        }

        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2493-2517 — <c>generateInitializedPropertyExpressionsOrClassStaticBlock</c>.</summary>
    /// <remarks>Generates assignment expressions for property initializers.</remarks>
    private List<Expression> GenerateInitializedPropertyExpressionsOrClassStaticBlock(IReadOnlyList<IClassElement> propertiesOrClassStaticBlocks, Expression receiver)
    {
        var expressions = new List<Expression>();
        foreach (var property in propertiesOrClassStaticBlocks)
        {
            var expression = NodeTests.IsClassStaticBlockDeclaration((Node)property)
                ? SetCurrentClassElementAnd(property, TransformClassStaticBlockDeclaration, (ClassStaticBlockDeclaration)property)
                : SetCurrentClassElementAnd<object?, Expression?>(property, _ => TransformProperty((PropertyDeclaration)property, receiver), null);
            if (expression is null)
            {
                continue;
            }
            var result = expression;
            FactoryUtilities.StartOnNewLine(result);
            NodeFactory.SetOriginalNode(result, (Node)property);
            EmitNodes.AddEmitFlags(result, Utilities.GetEmitFlags((Node)property) & EmitFlags.NoComments);
            EmitNodes.SetSourceMapRange(result, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers((Node)property)));
            EmitNodes.SetCommentRange(result, (Node)property);
            expressions.Add(result);
        }

        return expressions;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2519-2531 — <c>transformProperty</c>.</summary>
    /// <remarks>Transforms a property initializer into an assignment statement.</remarks>
    private Expression? TransformProperty(PropertyDeclaration property, Expression receiver)
    {
        var savedCurrentClassElement = _currentClassElement;
        var transformed = TransformPropertyWorker(property, receiver);
        if (transformed is not null && Utilities.HasStaticModifier(property) && _lexicalEnvironment?.Data?.Facts is not null)
        {
            // capture the lexical environment for the member
            NodeFactory.SetOriginalNode(transformed, property);
            EmitNodes.AddEmitFlags(transformed, EmitFlags.AdviseOnEmitNode);
            EmitNodes.SetSourceMapRange(transformed, TypeScriptTransformSupport.SourceMapRangeOf(EmitNodes.GetSourceMapRange(property.Name!)));
            _lexicalEnvironmentMap[Utilities.GetOriginalNode(property)!] = _lexicalEnvironment;
        }
        _currentClassElement = savedCurrentClassElement;
        return transformed;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2533-2631 — <c>transformPropertyWorker</c>.</summary>
    private Expression? TransformPropertyWorker(PropertyDeclaration property, Expression receiver)
    {
        // We generate a name here in order to reuse the value cached by the relocated computed name expression (which uses the same generated name)
        var emitAssignment = !_useDefineForClassFields;

        if (Utilities.IsNamedEvaluation(property, IsAnonymousClassNeedingAssignedName))
        {
            property = (PropertyDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, property);
        }

        var propertyName = Utilities.HasAccessorModifier(property)
            ? _factory.GetGeneratedPrivateNameForNode(property.Name, /*prefix*/ null, /*suffix*/ null)
            : NodeTests.IsComputedPropertyName(property.Name!) && !TransformationUtilities.IsSimpleInlineableExpression(((ComputedPropertyName)property.Name!).Expression!)
                ? _factory.UpdateComputedPropertyName((ComputedPropertyName)property.Name!, _factory.GetGeneratedNameForNode(property.Name!, /*flags*/ null))
                : property.Name!;

        if (Utilities.HasStaticModifier(property))
        {
            _currentClassElement = property;
        }

        if (NodeTests.IsPrivateIdentifier(propertyName) && ShouldTransformClassElementToWeakMap(property))
        {
            var privateIdentifierInfo = AccessPrivateIdentifier((PrivateIdentifier)propertyName);
            if (privateIdentifierInfo is not null)
            {
                if (privateIdentifierInfo is PrivateIdentifierStaticFieldInfo staticField)
                {
                    return ClassFieldsTransform.CreatePrivateStaticFieldInitializer(
                        _factory,
                        staticField.VariableName,
                        Utilities.VisitNode(property.Initializer, Visitor, Utilities.IsExpression));
                }
                if (privateIdentifierInfo is PrivateIdentifierInstanceFieldInfo)
                {
                    return ClassFieldsTransform.CreatePrivateInstanceFieldInitializer(
                        _factory,
                        receiver,
                        Utilities.VisitNode(property.Initializer, Visitor, Utilities.IsExpression),
                        privateIdentifierInfo.BrandCheckIdentifier);
                }
                return null;
            }
            else
            {
                Debug.Fail("Undeclared private name for property declaration.");
            }
        }
        if ((NodeTests.IsPrivateIdentifier(propertyName) || Utilities.HasStaticModifier(property)) && property.Initializer is null)
        {
            return null;
        }

        var propertyOriginalNode = Utilities.GetOriginalNode(property)!;
        if (Utilities.HasSyntacticModifier(propertyOriginalNode, ModifierFlags.Abstract))
        {
            return null;
        }

        var initializer = Utilities.VisitNode(property.Initializer, Visitor, Utilities.IsExpression);
        if (Utilities.IsParameterPropertyDeclaration(propertyOriginalNode, propertyOriginalNode.Parent!) && NodeTests.IsIdentifier(propertyName))
        {
            // A parameter-property declaration always overrides the initializer. The only time a parameter-property
            // declaration *should* have an initializer is when decorators have added initializers that need to run before
            // any other initializer
            var localName = _factory.CloneNode((Identifier)propertyName);
            if (initializer is not null)
            {
                // unwrap `(__runInitializers(this, _instanceExtraInitializers), void 0)`
                if (
                    NodeTests.IsParenthesizedExpression(initializer)
                    && FactoryUtilities.IsCommaExpression(((ParenthesizedExpression)initializer).Expression!)
                    && EmitHelpers.IsCallToHelper(((BinaryExpression)((ParenthesizedExpression)initializer).Expression!).Left!, "___runInitializers")
                    && NodeTests.IsVoidExpression(((BinaryExpression)((ParenthesizedExpression)initializer).Expression!).Right!)
                    && NodeTests.IsNumericLiteral(((VoidExpression)((BinaryExpression)((ParenthesizedExpression)initializer).Expression!).Right!).Expression!))
                {
                    initializer = ((BinaryExpression)((ParenthesizedExpression)initializer).Expression!).Left;
                }
                initializer = _factory.InlineExpressions([initializer!, localName]);
            }
            else
            {
                initializer = localName;
            }
            EmitNodes.SetEmitFlags(propertyName, EmitFlags.NoComments | EmitFlags.NoSourceMap);
            EmitNodes.SetSourceMapRange(localName, TypeScriptTransformSupport.SourceMapRangeOf(((INamedDeclaration)propertyOriginalNode).Name!));
            EmitNodes.SetEmitFlags(localName, EmitFlags.NoComments);
        }
        else
        {
            initializer ??= _factory.CreateVoidZero();
        }

        if (emitAssignment || NodeTests.IsPrivateIdentifier(propertyName))
        {
            var memberAccess = FactoryUtilities.CreateMemberAccessForPropertyName(_factory, receiver, propertyName, /*location*/ propertyName);
            EmitNodes.AddEmitFlags(memberAccess, EmitFlags.NoLeadingComments);
            var expression = _factory.CreateAssignment(memberAccess, initializer);
            return expression;
        }
        else
        {
            var name = NodeTests.IsComputedPropertyName(propertyName) ? ((ComputedPropertyName)propertyName).Expression
                : NodeTests.IsIdentifier(propertyName) ? _factory.CreateStringLiteral(Utilities.UnescapeLeadingUnderscores(((Identifier)propertyName).EscapedText!), /*isSingleQuote*/ null)
                : propertyName;
            var descriptor = _factory.CreatePropertyDescriptor(
                new PropertyDescriptorAttributes { Value = initializer, Configurable = true, Writable = true, Enumerable = true },
                /*singleLine*/ null);
            return _factory.CreateObjectDefinePropertyCall(receiver, name, descriptor);
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2670-2682 — <c>addInstanceMethodStatements</c>.</summary>
    /// <remarks>
    /// Generates brand-check initializer for private methods.
    /// </remarks>
    /// <param name="statements">Statement list that should be used to append new statements.</param>
    /// <param name="methods">An array of method declarations.</param>
    /// <param name="receiver">The receiver on which each method should be assigned.</param>
    private void AddInstanceMethodStatements(List<IStatement> statements, IReadOnlyList<IClassElement> methods, Expression receiver)
    {
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks || methods.Count == 0)
        {
            return;
        }

        var weakSetName = GetPrivateIdentifierEnvironment().Data.WeakSetName;
        Debug.Assert(weakSetName is not null, "weakSetName should be set in private identifier environment");
        statements.Add(
            _factory.CreateExpressionStatement(
                ClassFieldsTransform.CreatePrivateInstanceMethodInitializer(_factory, receiver, weakSetName!)));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2684-2701 — <c>visitInvalidSuperProperty</c>.</summary>
    private Expression VisitInvalidSuperProperty(Node node)
        => NodeTests.IsPropertyAccessExpression(node)
            ? _factory.UpdatePropertyAccessExpression(
                (PropertyAccessExpression)node,
                _factory.CreateVoidZero(),
                ((PropertyAccessExpression)node).Name)
            : _factory.UpdateElementAccessExpression(
                (ElementAccessExpression)node,
                _factory.CreateVoidZero(),
                Utilities.VisitNode(((ElementAccessExpression)node).ArgumentExpression, Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2703-2722 — <c>getPropertyNameExpressionIfNeeded</c>.</summary>
    /// <remarks>
    /// If the name is a computed property, this function transforms it, then either returns an expression which caches the
    /// value of the result or the expression itself if the value is either unused or safe to inline into multiple locations.
    /// </remarks>
    /// <param name="shouldHoist">Does the expression need to be reused? (ie, for an initializer or a decorator)</param>
    private Expression? GetPropertyNameExpressionIfNeeded(Node name, bool shouldHoist)
    {
        if (NodeTests.IsComputedPropertyName(name))
        {
            var cacheAssignment = FactoryUtilities.FindComputedPropertyNameCacheAssignment((ComputedPropertyName)name);
            var expression = Utilities.VisitNode(((ComputedPropertyName)name).Expression, Visitor, Utilities.IsExpression)!;
            var innerExpression = Utilities.SkipPartiallyEmittedExpressions(expression);
            var inlinable = TransformationUtilities.IsSimpleInlineableExpression(innerExpression);
            var alreadyTransformed = cacheAssignment is not null
                || Utilities.IsAssignmentExpression(innerExpression) && Utilities.IsGeneratedIdentifier(((BinaryExpression)innerExpression).Left!);
            if (!alreadyTransformed && !inlinable && shouldHoist)
            {
                var generatedName = _factory.GetGeneratedNameForNode(name, /*flags*/ null);
                if (_resolver.HasNodeCheckFlag(name, NodeCheckFlags.BlockScopedBindingInLoop))
                {
                    _context.AddBlockScopedVariable(generatedName);
                }
                else
                {
                    _context.HoistVariableDeclaration(generatedName);
                }
                return _factory.CreateAssignment(generatedName, expression);
            }
            return inlinable || NodeTests.IsIdentifier(innerExpression) ? null : expression;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2724-2726 — <c>startClassLexicalEnvironment</c>.</summary>
    private void StartClassLexicalEnvironment()
        => _lexicalEnvironment = new ClassFieldsLexicalEnvironment(_lexicalEnvironment);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2728-2730 — <c>endClassLexicalEnvironment</c>.</summary>
    private void EndClassLexicalEnvironment()
        => _lexicalEnvironment = (ClassFieldsLexicalEnvironment?)_lexicalEnvironment?.Previous;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2732-2741 — <c>getClassLexicalEnvironment</c>.</summary>
    private ClassLexicalEnvironment GetClassLexicalEnvironment()
    {
        Debug.Assert(_lexicalEnvironment is not null);
        return _lexicalEnvironment!.Data ??= new ClassLexicalEnvironment();
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2743-2749 — <c>getPrivateIdentifierEnvironment</c>.</summary>
    private TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> GetPrivateIdentifierEnvironment()
    {
        Debug.Assert(_lexicalEnvironment is not null);
        return _lexicalEnvironment!.PrivateEnv ??= TransformationUtilities.NewPrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo>(new PrivateEnvironmentData());
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2751-2753 — <c>getPendingExpressions</c>.</summary>
    private List<Expression> GetPendingExpressions()
        => _pendingExpressions ??= [];

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2633-2644 — <c>enableSubstitutionForClassAliases</c>.</summary>
    private void EnableSubstitutionForClassAliases()
    {
        if ((_enabledSubstitutions & ClassPropertySubstitutionFlags.ClassAliases) == 0)
        {
            _enabledSubstitutions |= ClassPropertySubstitutionFlags.ClassAliases;

            // We need to enable substitutions for identifiers. This allows us to
            // substitute class names inside of a class declaration.
            _context.EnableSubstitution(SyntaxKind.Identifier);

            // Keep track of class aliases.
            _classAliases = [];
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2646-2668 — <c>enableSubstitutionForClassStaticThisOrSuperReference</c>.</summary>
    private void EnableSubstitutionForClassStaticThisOrSuperReference()
    {
        if ((_enabledSubstitutions & ClassPropertySubstitutionFlags.ClassStaticThisOrSuperReference) == 0)
        {
            _enabledSubstitutions |= ClassPropertySubstitutionFlags.ClassStaticThisOrSuperReference;

            // substitute `this` in a static field initializer
            _context.EnableSubstitution(SyntaxKind.ThisKeyword);
            _context.EnableEmitNotification(SyntaxKind.FunctionDeclaration);
            _context.EnableEmitNotification(SyntaxKind.FunctionExpression);
            _context.EnableEmitNotification(SyntaxKind.Constructor);
            _context.EnableEmitNotification(SyntaxKind.GetAccessor);
            _context.EnableEmitNotification(SyntaxKind.SetAccessor);
            _context.EnableEmitNotification(SyntaxKind.MethodDeclaration);
            _context.EnableEmitNotification(SyntaxKind.PropertyDeclaration);
            _context.EnableEmitNotification(SyntaxKind.ComputedPropertyName);
        }
    }
}
