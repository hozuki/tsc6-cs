#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classFields.ts:2755-3176 — the private-name environment bookkeeping, the
// hoisted-name factories, and the destructuring-assignment visitors.
/// <summary>ported from: src/compiler/transformers/classFields.ts:2755-3176 — the private-name declarations and the
/// destructuring half of <c>transformClassFields</c>.</summary>
/// <remarks>
/// PORT-NOTE (the <c>addDeclaration</c> callback): the reference threads a function value
/// (<c>addPrivateIdentifier{ClassElement,PropertyDeclaration}ToEnvironment</c>) into
/// <c>addPrivateIdentifierToEnvironment</c>; the port states that type as the private delegate
/// <see cref="PrivateIdentifierEnvironmentAdder{T}"/>, whose parameter list is the reference's own.
/// </remarks>
internal sealed partial class ClassFieldsTransformer
{
    /// <summary>ported from: src/compiler/transformers/classFields.ts:2755-2779 — the <c>addDeclaration</c> parameter of
    /// <c>addPrivateIdentifierToEnvironment</c>.</summary>
    private delegate void PrivateIdentifierEnvironmentAdder<in T>(
        T node,
        PrivateIdentifier name,
        ClassLexicalEnvironment lex,
        TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> privateEnv,
        bool isStatic,
        bool isValid,
        PrivateIdentifierInfo? previousInfo);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2755-2779 — <c>addPrivateIdentifierClassElementToEnvironment</c>.</summary>
    private void AddPrivateIdentifierClassElementToEnvironment(
        Node node,
        PrivateIdentifier name,
        ClassLexicalEnvironment lex,
        TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> privateEnv,
        bool isStatic,
        bool isValid,
        PrivateIdentifierInfo? previousInfo)
    {
        if (Utilities.IsAutoAccessorPropertyDeclaration(node))
        {
            AddPrivateIdentifierAutoAccessorPropertyDeclarationToEnvironment((PropertyDeclaration)node, name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
        else if (NodeTests.IsPropertyDeclaration(node))
        {
            AddPrivateIdentifierPropertyDeclarationToEnvironment((PropertyDeclaration)node, name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
        else if (NodeTests.IsMethodDeclaration(node))
        {
            AddPrivateIdentifierMethodDeclarationToEnvironment((MethodDeclaration)node, name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
        else if (NodeTests.IsGetAccessorDeclaration(node))
        {
            AddPrivateIdentifierGetAccessorDeclarationToEnvironment((GetAccessorDeclaration)node, name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
        else if (NodeTests.IsSetAccessorDeclaration(node))
        {
            AddPrivateIdentifierSetAccessorDeclarationToEnvironment((SetAccessorDeclaration)node, name, lex, privateEnv, isStatic, isValid, previousInfo);
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2781-2821 — <c>addPrivateIdentifierPropertyDeclarationToEnvironment</c>.</summary>
    private void AddPrivateIdentifierPropertyDeclarationToEnvironment(
        PropertyDeclaration node,
        PrivateIdentifier name,
        ClassLexicalEnvironment lex,
        TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> privateEnv,
        bool isStatic,
        bool isValid,
        PrivateIdentifierInfo? previousInfo)
    {
        if (isStatic)
        {
            var brandCheckIdentifier = Debug.CheckDefined(lex.ClassThis ?? lex.ClassConstructor, "classConstructor should be set in private identifier environment");

            var variableName = CreateHoistedVariableForPrivateName(name, /*suffix*/ null);
            TransformationUtilities.SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierStaticFieldInfo
            {
                BrandCheckIdentifier = brandCheckIdentifier,
                IsStatic = true,
                VariableName = variableName,
                IsValid = isValid,
            });
        }
        else
        {
            var weakMapName = CreateHoistedVariableForPrivateName(name, /*suffix*/ null);

            TransformationUtilities.SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierInstanceFieldInfo
            {
                BrandCheckIdentifier = weakMapName,
                IsStatic = false,
                IsValid = isValid,
            });

            GetPendingExpressions().Add(_factory.CreateAssignment(
                weakMapName,
                _factory.CreateNewExpression(
                    _factory.CreateIdentifier("WeakMap"),
                    /*typeArguments*/ null,
                    [])));
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2823-2844 — <c>addPrivateIdentifierMethodDeclarationToEnvironment</c>.</summary>
    private void AddPrivateIdentifierMethodDeclarationToEnvironment(
        MethodDeclaration node,
        PrivateIdentifier name,
        ClassLexicalEnvironment lex,
        TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> privateEnv,
        bool isStatic,
        bool isValid,
        PrivateIdentifierInfo? previousInfo)
    {
        var methodName = CreateHoistedVariableForPrivateName(name, /*suffix*/ null);
        var brandCheckIdentifier = isStatic
            ? Debug.CheckDefined(lex.ClassThis ?? lex.ClassConstructor, "classConstructor should be set in private identifier environment")
            : Debug.CheckDefined(privateEnv.Data.WeakSetName, "weakSetName should be set in private identifier environment");

        TransformationUtilities.SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierMethodInfo
        {
            MethodName = methodName,
            BrandCheckIdentifier = brandCheckIdentifier,
            IsStatic = isStatic,
            IsValid = isValid,
        });
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2846-2873 — <c>addPrivateIdentifierGetAccessorDeclarationToEnvironment</c>.</summary>
    private void AddPrivateIdentifierGetAccessorDeclarationToEnvironment(
        GetAccessorDeclaration node,
        PrivateIdentifier name,
        ClassLexicalEnvironment lex,
        TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> privateEnv,
        bool isStatic,
        bool isValid,
        PrivateIdentifierInfo? previousInfo)
    {
        var getterName = CreateHoistedVariableForPrivateName(name, "_get");
        var brandCheckIdentifier = isStatic
            ? Debug.CheckDefined(lex.ClassThis ?? lex.ClassConstructor, "classConstructor should be set in private identifier environment")
            : Debug.CheckDefined(privateEnv.Data.WeakSetName, "weakSetName should be set in private identifier environment");

        if (previousInfo is PrivateIdentifierAccessorInfo previousAccessor && previousAccessor.IsStatic == isStatic && previousAccessor.GetterName is null)
        {
            previousAccessor.GetterName = getterName;
        }
        else
        {
            TransformationUtilities.SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierAccessorInfo
            {
                GetterName = getterName,
                SetterName = null,
                BrandCheckIdentifier = brandCheckIdentifier,
                IsStatic = isStatic,
                IsValid = isValid,
            });
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2875-2905 — <c>addPrivateIdentifierSetAccessorDeclarationToEnvironment</c>.</summary>
    private void AddPrivateIdentifierSetAccessorDeclarationToEnvironment(
        SetAccessorDeclaration node,
        PrivateIdentifier name,
        ClassLexicalEnvironment lex,
        TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> privateEnv,
        bool isStatic,
        bool isValid,
        PrivateIdentifierInfo? previousInfo)
    {
        var setterName = CreateHoistedVariableForPrivateName(name, "_set");
        var brandCheckIdentifier = isStatic
            ? Debug.CheckDefined(lex.ClassThis ?? lex.ClassConstructor, "classConstructor should be set in private identifier environment")
            : Debug.CheckDefined(privateEnv.Data.WeakSetName, "weakSetName should be set in private identifier environment");

        if (previousInfo is PrivateIdentifierAccessorInfo previousAccessor && previousAccessor.IsStatic == isStatic && previousAccessor.SetterName is null)
        {
            previousAccessor.SetterName = setterName;
        }
        else
        {
            TransformationUtilities.SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierAccessorInfo
            {
                GetterName = null,
                SetterName = setterName,
                BrandCheckIdentifier = brandCheckIdentifier,
                IsStatic = isStatic,
                IsValid = isValid,
            });
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2907-2930 — <c>addPrivateIdentifierAutoAccessorPropertyDeclarationToEnvironment</c>.</summary>
    private void AddPrivateIdentifierAutoAccessorPropertyDeclarationToEnvironment(
        PropertyDeclaration node,
        PrivateIdentifier name,
        ClassLexicalEnvironment lex,
        TransformationUtilities.PrivateEnvironment<PrivateEnvironmentData, PrivateIdentifierInfo> privateEnv,
        bool isStatic,
        bool isValid,
        PrivateIdentifierInfo? previousInfo)
    {
        var getterName = CreateHoistedVariableForPrivateName(name, "_get");
        var setterName = CreateHoistedVariableForPrivateName(name, "_set");
        var brandCheckIdentifier = isStatic
            ? Debug.CheckDefined(lex.ClassThis ?? lex.ClassConstructor, "classConstructor should be set in private identifier environment")
            : Debug.CheckDefined(privateEnv.Data.WeakSetName, "weakSetName should be set in private identifier environment");

        TransformationUtilities.SetPrivateIdentifier(privateEnv, name, new PrivateIdentifierAccessorInfo
        {
            GetterName = getterName,
            SetterName = setterName,
            BrandCheckIdentifier = brandCheckIdentifier,
            IsStatic = isStatic,
            IsValid = isValid,
        });
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2932-2951 — <c>addPrivateIdentifierToEnvironment</c>.</summary>
    private void AddPrivateIdentifierToEnvironment<T>(
        T node,
        PrivateIdentifier name,
        PrivateIdentifierEnvironmentAdder<T> addDeclaration)
        where T : Node
    {
        var lex = GetClassLexicalEnvironment();
        var privateEnv = GetPrivateIdentifierEnvironment();
        var previousInfo = TransformationUtilities.GetPrivateIdentifier(privateEnv, name);
        var isStatic = Utilities.HasStaticModifier(node);
        var isValid = !ClassFieldsTransform.IsReservedPrivateName(name) && previousInfo is null;
        addDeclaration(node, name, lex, privateEnv, isStatic, isValid, previousInfo);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2953-2968 — <c>createHoistedVariableForClass</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>typeof name === "object"</c> arm is the <see cref="Node"/> test, which is
    /// the same discrimination a <c>PrivateIdentifier</c> or <c>ClassStaticBlockDeclaration</c> argument takes.</remarks>
    private Identifier CreateHoistedVariableForClass(object? name, Node node, string? suffix)
    {
        var className = GetPrivateIdentifierEnvironment().Data.ClassName;
        object prefix = className is not null ? new GeneratedNamePart(className, "_", "_") : "_";
        var identifier = name is Node nameNode
            ? _factory.GetGeneratedNameForNode(nameNode, GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes, prefix, suffix)
            : name is string text
                ? _factory.CreateUniqueName(text, GeneratedIdentifierFlags.Optimistic, prefix, suffix)
                : _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ true, prefix, suffix);

        if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.BlockScopedBindingInLoop))
        {
            _context.AddBlockScopedVariable(identifier);
        }
        else
        {
            _context.HoistVariableDeclaration(identifier);
        }

        return identifier;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2970-2973 — <c>createHoistedVariableForPrivateName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>tryGetTextOfPropertyName(name) as string | undefined</c> is
    /// <see cref="Utilities.TryGetTextOfPropertyName"/>, which already answers <c>string?</c>, so no assertion stands in
    /// for it.</remarks>
    private Identifier CreateHoistedVariableForPrivateName(PrivateIdentifier name, string? suffix)
    {
        var text = Utilities.TryGetTextOfPropertyName(name);
        return CreateHoistedVariableForClass((object?)text?[1..] ?? name, name, suffix);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2980-2983 — <c>accessPrivateIdentifier</c>.</summary>
    /// <remarks>Access an already defined private name in the current private identifier environment.</remarks>
    private PrivateIdentifierInfoBase? AccessPrivateIdentifier(PrivateIdentifier name)
    {
        var info = TransformationUtilities.AccessPrivateIdentifier<ClassLexicalEnvironment?, PrivateEnvironmentData, PrivateIdentifierInfo>(_lexicalEnvironment, name);
        return info is null || info is UntransformedPrivateIdentifierInfo ? null : (PrivateIdentifierInfoBase)info;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2985-3007 — <c>wrapPrivateIdentifierForDestructuringTarget</c>.</summary>
    private Expression WrapPrivateIdentifierForDestructuringTarget(PropertyAccessExpression node)
    {
        var parameter = _factory.GetGeneratedNameForNode(node, /*flags*/ null);
        var info = AccessPrivateIdentifier((PrivateIdentifier)node.Name!);
        if (info is null)
        {
            return (Expression)Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
        Expression receiver = node.Expression!;
        // We cannot copy `this` or `super` into the function because they will be bound
        // differently inside the function.
        if (Utilities.IsThisProperty(node) || Utilities.IsSuperProperty(node) || !TransformationUtilities.IsSimpleCopiableExpression(node.Expression!))
        {
            receiver = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ true);
            GetPendingExpressions().Add(_factory.CreateBinaryExpression(receiver, SyntaxKind.EqualsToken, Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)));
        }
        return _factory.CreateAssignmentTargetWrapper(
            parameter,
            CreatePrivateIdentifierAssignment(
                info,
                receiver,
                parameter,
                SyntaxKind.EqualsToken));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3009-3047 — <c>visitDestructuringAssignmentTarget</c>.</summary>
    private Expression VisitDestructuringAssignmentTarget(Node node)
    {
        if (NodeTests.IsObjectLiteralExpression(node) || NodeTests.IsArrayLiteralExpression(node))
        {
            return VisitAssignmentPattern((Expression)node);
        }

        if (Utilities.IsPrivateIdentifierPropertyAccessExpression(node))
        {
            return WrapPrivateIdentifierForDestructuringTarget((PropertyAccessExpression)node);
        }
        else if (
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
            else if (data.ClassConstructor is not null && data.SuperClassReference is not null)
            {
                var name = NodeTests.IsElementAccessExpression(node)
                    ? Utilities.VisitNode(((ElementAccessExpression)node).ArgumentExpression, Visitor, Utilities.IsExpression)
                    : NodeTests.IsIdentifier(((PropertyAccessExpression)node).Name!)
                        ? _factory.CreateStringLiteralFromNode(((PropertyAccessExpression)node).Name, /*isSingleQuote*/ null)
                        : null;
                if (name is not null)
                {
                    var temp = _factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ null);
                    return _factory.CreateAssignmentTargetWrapper(
                        temp,
                        _factory.CreateReflectSetCall(
                            data.SuperClassReference,
                            name,
                            temp,
                            data.ClassConstructor));
                }
            }
        }
        return (Expression)Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3049-3067 — <c>visitAssignmentElement</c>.</summary>
    private Expression VisitAssignmentElement(Node node)
    {
        // 13.15.5.5 RS: IteratorDestructuringAssignmentEvaluation
        //   AssignmentElement : DestructuringAssignmentTarget Initializer?
        //     ...
        //     4. If |Initializer| is present and _value_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
        //           i. Let _v_ be ? NamedEvaluation of |Initializer| with argument _lref_.[[ReferencedName]].
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = NamedEvaluation.TransformNamedEvaluation(_context, node);
        }
        if (Utilities.IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true))
        {
            var left = VisitDestructuringAssignmentTarget(((BinaryExpression)node).Left!);
            var right = Utilities.VisitNode(((BinaryExpression)node).Right, Visitor, Utilities.IsExpression);
            return _factory.UpdateBinaryExpression((BinaryExpression)node, left, ((BinaryExpression)node).OperatorToken, right);
        }
        return VisitDestructuringAssignmentTarget(node);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3069-3076 — <c>visitAssignmentRestElement</c>.</summary>
    private object? VisitAssignmentRestElement(SpreadElement node)
    {
        if (Utilities.IsLeftHandSideExpression(node.Expression!))
        {
            var expression = VisitDestructuringAssignmentTarget(node.Expression!);
            return _factory.UpdateSpreadElement(node, expression);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3078-3084 — <c>visitArrayAssignmentElement</c>.</summary>
    private object? VisitArrayAssignmentElement(Node node)
    {
        if (TransformationUtilities.IsArrayBindingOrAssignmentElement(node))
        {
            if (NodeTests.IsSpreadElement(node)) return VisitAssignmentRestElement((SpreadElement)node);
            if (!NodeTests.IsOmittedExpression(node)) return VisitAssignmentElement(node);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3086-3110 — <c>visitAssignmentProperty</c>.</summary>
    private object? VisitAssignmentProperty(PropertyAssignment node)
    {
        // AssignmentProperty : PropertyName `:` AssignmentElement
        // AssignmentElement : DestructuringAssignmentTarget Initializer?

        // 13.15.5.6 RS: KeyedDestructuringAssignmentEvaluation
        //   AssignmentElement : DestructuringAssignmentTarget Initializer?
        //     ...
        //     3. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousfunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
        //           i. Let _rhsValue_ be ? NamedEvaluation of |Initializer| with argument _lref_.[`ReferencedName`].
        //     ...

        var name = Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName);
        if (Utilities.IsAssignmentExpression(node.Initializer!, /*excludeCompoundAssignment*/ true))
        {
            var assignmentElement = VisitAssignmentElement(node.Initializer!);
            return _factory.UpdatePropertyAssignment(node, name, assignmentElement);
        }

        if (Utilities.IsLeftHandSideExpression(node.Initializer!))
        {
            var assignmentElement = VisitDestructuringAssignmentTarget(node.Initializer!);
            return _factory.UpdatePropertyAssignment(node, name, assignmentElement);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3112-3128 — <c>visitShorthandAssignmentProperty</c>.</summary>
    private object? VisitShorthandAssignmentProperty(ShorthandPropertyAssignment node)
    {
        // AssignmentProperty : IdentifierReference Initializer?

        // 13.15.5.3 RS: PropertyDestructuringAssignmentEvaluation
        //   AssignmentProperty : IdentifierReference Initializer?
        //     ...
        //     4. If |Initializer?| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _P_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (ShorthandPropertyAssignment)NamedEvaluation.TransformNamedEvaluation(_context, node);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3130-3137 — <c>visitAssignmentRestProperty</c>.</summary>
    private object? VisitAssignmentRestProperty(SpreadAssignment node)
    {
        if (Utilities.IsLeftHandSideExpression(node.Expression!))
        {
            var expression = VisitDestructuringAssignmentTarget(node.Expression!);
            return _factory.UpdateSpreadAssignment(node, expression);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3139-3145 — <c>visitObjectAssignmentElement</c>.</summary>
    private object? VisitObjectAssignmentElement(Node node)
    {
        Debug.AssertNode(node, TransformationUtilities.IsObjectBindingOrAssignmentElement);
        if (NodeTests.IsSpreadAssignment(node)) return VisitAssignmentRestProperty((SpreadAssignment)node);
        if (NodeTests.IsShorthandPropertyAssignment(node)) return VisitShorthandAssignmentProperty((ShorthandPropertyAssignment)node);
        if (NodeTests.IsPropertyAssignment(node)) return VisitAssignmentProperty((PropertyAssignment)node);
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:3147-3176 — <c>visitAssignmentPattern</c>.</summary>
    private Expression VisitAssignmentPattern(Expression node)
    {
        if (NodeTests.IsArrayLiteralExpression(node))
        {
            // Transforms private names in destructuring assignment array bindings.
            // Transforms SuperProperty assignments in destructuring assignment array bindings in static initializers.
            //
            // Source:
            // ([ this.#myProp ] = [ "hello" ]);
            //
            // Transformation:
            // [ { set value(x) { this.#myProp = x; } }.value ] = [ "hello" ];
            return _factory.UpdateArrayLiteralExpression(
                (ArrayLiteralExpression)node,
                Utilities.VisitNodes(((ArrayLiteralExpression)node).Elements, VisitArrayAssignmentElement, Utilities.IsExpression));
        }
        else
        {
            // Transforms private names in destructuring assignment object bindings.
            // Transforms SuperProperty assignments in destructuring assignment object bindings in static initializers.
            //
            // Source:
            // ({ stringProperty: this.#myProp } = { stringProperty: "hello" });
            //
            // Transformation:
            // ({ stringProperty: { set value(x) { this.#myProp = x; } }.value }) = { stringProperty: "hello" };
            return _factory.UpdateObjectLiteralExpression(
                (ObjectLiteralExpression)node,
                Utilities.VisitNodes(((ObjectLiteralExpression)node).Properties, VisitObjectAssignmentElement, Utilities.IsObjectLiteralElementLike));
        }
    }
}
