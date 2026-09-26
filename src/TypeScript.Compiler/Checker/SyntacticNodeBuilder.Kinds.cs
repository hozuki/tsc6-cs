namespace TypeScript.Compiler;

// ported from: src/compiler/expressionToTypeNode.ts:700-1330 — the typeFrom*/infer callers, the object-literal and
// accessor families, createReturnFromSignature, typeFromSingleReturnExpression and isContextuallyTyped
/// <summary>
/// ported from: src/compiler/expressionToTypeNode.ts:700-1330 — the <c>typeFrom*</c> family and the two JSDoc helpers.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (union parameters): TS's <c>AccessorDeclaration</c> (<c>GetAccessorDeclaration | SetAccessorDeclaration</c>),
/// <c>PropertyAssignment | ShorthandPropertyAssignment</c>, <c>PropertyAccessExpression | BinaryExpression |
/// ElementAccessExpression</c>, <c>PropertyDeclaration | PropertySignature | JSDocPropertyTag</c>,
/// <c>ParameterDeclaration | JSDocParameterTag</c> and <c>FunctionLikeDeclaration</c> are unions with no C# face, so
/// every one widens to <c>Node</c> (or <c>Expression</c> where the union is a subset of it) and the arms the guards
/// prove are read through the concrete class — §12.9's rule for a union of node types.
/// </para>
/// <para>
/// PORT-NOTE (<c>isPrimitiveLiteralValue</c> / <c>isVarConstLike</c>): the reference imports both from
/// <c>utilities.ts</c>:12030 / :2685, which the port has not landed (<c>Utilities.Checker.cs</c> carries
/// <c>isVarConst</c>:2677 and the checker's own <c>IsVarConstLike</c>, which is an instance member of <c>Checker</c>).
/// Both are pure kind/flags tests, so <see cref="IsPrimitiveLiteralValue"/> and <see cref="IsVarConstLike"/> state them
/// here, file-locally, and must be deleted when the <c>utilities.ts</c> slice lands.
/// </para>
/// </remarks>
internal sealed partial class SyntacticNodeBuilder
{
    // ---------------------------------------------------------------------------------------------
    // The accessor family (expressionToTypeNode.ts:741-767)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:741-746 — <c>getTypeAnnotationFromAccessor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>AccessorDeclaration</c> has no C# face, so the parameter is the nullable <c>Node</c> its callers pass (<c>firstAccessor</c>/<c>secondAccessor</c> are <c>Node?</c>); the <c>kind</c> test splits the two arms and licenses the <c>SetAccessorDeclaration</c> cast.</remarks>
    private static ITypeNode? GetTypeAnnotationFromAccessor(Node? accessor)
    {
        if (accessor is not null)
        {
            return accessor.Kind == SyntaxKind.GetAccessor
                ? (Utilities.IsInJSFile(accessor) ? Utilities.GetJSDocType(accessor) : null) ?? Utilities.GetEffectiveReturnTypeNode(accessor)
                : Utilities.GetEffectiveSetAccessorTypeAnnotationNode((SetAccessorDeclaration)accessor);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:748-757 — <c>getTypeAnnotationFromAllAccessorDeclarations</c>.</summary>
    private static ITypeNode? GetTypeAnnotationFromAllAccessorDeclarations(Node node, IAllAccessorDeclarations accessors)
    {
        var accessorType = GetTypeAnnotationFromAccessor(node);
        if (accessorType is null && !ReferenceEquals(node, accessors.FirstAccessor))
        {
            accessorType = GetTypeAnnotationFromAccessor(accessors.FirstAccessor);
        }
        if (accessorType is null && accessors.SecondAccessor is not null && !ReferenceEquals(node, accessors.SecondAccessor))
        {
            accessorType = GetTypeAnnotationFromAccessor(accessors.SecondAccessor);
        }
        return accessorType;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:759-767 — <c>typeFromAccessor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>AccessorDeclaration</c> widens to <c>Node</c>.</remarks>
    private ITypeNode? TypeFromAccessor(Node node, ISymbol? symbol, ISyntacticTypeNodeBuilderContext context)
    {
        var accessorDeclarations = _resolver.GetAllAccessorDeclarations(node);
        var accessorType = GetTypeAnnotationFromAllAccessorDeclarations(node, accessorDeclarations);
        if (accessorType is not null && !NodeTests.IsTypePredicateNode((Node)accessorType))
        {
            return WithNewScope(context, node, () => SerializeTypeAnnotationOfDeclaration(accessorType, context, node, symbol) ?? InferTypeOfDeclaration(node, symbol, context));
        }
        if (accessorDeclarations.GetAccessor is not null)
        {
            return WithNewScope(context, accessorDeclarations.GetAccessor, () => CreateReturnFromSignature(accessorDeclarations.GetAccessor!, symbol, context));
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // typeFromVariable / typeFromParameter / typeFromExpandoProperty / typeFromProperty (770-828)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:770-781 — <c>typeFromVariable</c>.</summary>
    private ITypeNode? TypeFromVariable(VariableDeclaration node, ISymbol symbol, ISyntacticTypeNodeBuilderContext context)
    {
        var declaredType = Utilities.GetEffectiveTypeAnnotationNode(node);
        var resultType = Failed;
        if (declaredType is not null)
        {
            resultType = SyntacticResultOf(SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol));
        }
        else if (node.Initializer is not null && (symbol.Declarations?.Count == 1 || Core.CountWhere<IDeclaration>(symbol.Declarations, (d, _) => NodeTests.IsVariableDeclaration((Node)d)) == 1))
        {
            if (!_resolver.IsExpandoFunctionDeclaration(node) && !IsContextuallyTyped(node))
            {
                resultType = TypeFromExpression(node.Initializer, context, /*isConstContext*/ false, /*requiresAddingUndefined*/ false, IsVarConstLike(node));
            }
        }
        return resultType.Type ?? InferTypeOfDeclaration(node, symbol, context, resultType.ReportFallback);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:783-800 — <c>typeFromParameter</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ParameterDeclaration | JSDocParameterTag</c> widens to <c>Node</c>; the <c>initializer</c> read is guarded by <c>isParameter</c>, which is what licenses the <c>ParameterDeclaration</c> cast.</remarks>
    private ITypeNode? TypeFromParameter(Node node, ISymbol? symbol, ISyntacticTypeNodeBuilderContext context)
    {
        var parent = node.Parent!;
        if (parent.Kind == SyntaxKind.SetAccessor)
        {
            return SerializeTypeOfAccessor(parent, /*symbol*/ null, context);
        }
        var declaredType = Utilities.GetEffectiveTypeAnnotationNode(node);
        var addUndefined = _resolver.RequiresAddingImplicitUndefined(node, symbol, context.EnclosingDeclaration);
        var resultType = Failed;
        if (declaredType is not null)
        {
            resultType = SyntacticResultOf(SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol, addUndefined));
        }
        else if (NodeTests.IsParameter(node) && ((ParameterDeclaration)node).Initializer is not null && NodeTests.IsIdentifier(((ParameterDeclaration)node).Name!) && !IsContextuallyTyped(node))
        {
            resultType = TypeFromExpression(((ParameterDeclaration)node).Initializer!, context, /*isConstContext*/ false, addUndefined);
        }
        return resultType.Type ?? InferTypeOfDeclaration(node, symbol, context, resultType.ReportFallback);
    }

    /// <summary>
    /// ported from: src/compiler/expressionToTypeNode.ts:802-812 — <c>typeFromExpandoProperty</c>.
    /// </summary>
    /// <remarks>While expando poperies are errors in TSC, in JS we try to extract the type from the binary expression.</remarks>
    /// <remarks>PORT-NOTE: TS's <c>PropertyAccessExpression | BinaryExpression | ElementAccessExpression</c> widens to <c>Expression</c>, which all three are.</remarks>
    private ITypeNode? TypeFromExpandoProperty(Expression node, ISymbol symbol, ISyntacticTypeNodeBuilderContext context)
    {
        var declaredType = Utilities.GetEffectiveTypeAnnotationNode(node);
        ITypeNode? result = null;
        if (declaredType is not null)
        {
            result = SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol);
        }
        var oldSuppressReportInferenceFallback = context.SuppressReportInferenceFallback;
        context.SuppressReportInferenceFallback = true;
        var resultType = result ?? InferTypeOfDeclaration(node, symbol, context, /*reportFallback*/ false);
        context.SuppressReportInferenceFallback = oldSuppressReportInferenceFallback;
        return resultType;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:814-828 — <c>typeFromProperty</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>PropertyDeclaration | PropertySignature | JSDocPropertyTag</c> widens to <c>Node</c>; the <c>initializer</c> read is guarded by <c>isPropertyDeclaration</c>.</remarks>
    private ITypeNode? TypeFromProperty(Node node, ISymbol symbol, ISyntacticTypeNodeBuilderContext context)
    {
        var declaredType = Utilities.GetEffectiveTypeAnnotationNode(node);
        var requiresAddingUndefined = _resolver.RequiresAddingImplicitUndefined(node, symbol, context.EnclosingDeclaration);
        var resultType = Failed;
        if (declaredType is not null)
        {
            resultType = SyntacticResultOf(SerializeTypeAnnotationOfDeclaration(declaredType, context, node, symbol, requiresAddingUndefined));
        }
        else
        {
            var initializer = NodeTests.IsPropertyDeclaration(node) ? ((PropertyDeclaration)node).Initializer : null;
            if (initializer is not null && !IsContextuallyTyped(node))
            {
                var isReadonly = Utilities.IsDeclarationReadonly((IDeclaration)node);
                resultType = TypeFromExpression(initializer, context, /*isConstContext*/ false, requiresAddingUndefined, isReadonly);
            }
        }
        return resultType.Type ?? InferTypeOfDeclaration(node, symbol, context, resultType.ReportFallback);
    }

    // ---------------------------------------------------------------------------------------------
    // typeFromExpression and the literals (886-1022)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:886-890 — <c>typeFromTypeAssertion</c>.</summary>
    private SyntacticResult TypeFromTypeAssertion(Expression expression, ITypeNode type, ISyntacticTypeNodeBuilderContext context, bool requiresAddingUndefined)
    {
        if (Utilities.IsConstTypeReference((Node)type))
        {
            return TypeFromExpression(expression, context, /*isConstContext*/ true, requiresAddingUndefined);
        }
        return SyntacticResultOf(SerializeExistingTypeNodeWithFallback(type, context, requiresAddingUndefined));
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:892-968 — <c>typeFromExpression</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>Expression | JsxAttributeValue</c> widens to <c>Expression</c> — every <c>JsxAttributeValue</c>
    /// member is a class that derives from <c>Expression</c> in the port's model.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's optional <c>addUndefined</c>/<c>preserveLiterals</c> parameters are <c>bool</c> here; the
    /// reference passes <c>undefined</c> where the port passes <c>false</c>, which every read in this file treats
    /// identically (<c>addUndefined == true</c>, <c>isConstContext || …</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>Debug.type&lt;ArrowFunction | FunctionExpression&gt;(node)</c> is typespace-only (<c>Debug.cs:37-39</c>),
    /// so the arm's <c>withNewScope</c> call carries the cast that assertion supplied.
    /// </para>
    /// </remarks>
    private SyntacticResult TypeFromExpression(Expression node, ISyntacticTypeNodeBuilderContext context, bool isConstContext = false, bool requiresAddingUndefined = false, bool preserveLiterals = false)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ParenthesizedExpression:
                if (FactoryUtilities.IsJSDocTypeAssertion(node))
                {
                    return TypeFromTypeAssertion(((ParenthesizedExpression)node).Expression!, FactoryUtilities.GetJSDocTypeAssertionType(node), context, requiresAddingUndefined);
                }
                return TypeFromExpression(((ParenthesizedExpression)node).Expression!, context, isConstContext, requiresAddingUndefined);
            case SyntaxKind.Identifier:
                if (_resolver.IsUndefinedIdentifierExpression((Identifier)node))
                {
                    return SyntacticResultOf(CreateUndefinedTypeNode());
                }
                break;
            case SyntaxKind.NullKeyword:
                if (_strictNullChecks)
                {
                    return SyntacticResultOf(AddUndefinedIfNeeded(_factory.CreateLiteralTypeNode(_factory.CreateNull()), requiresAddingUndefined, node, context));
                }
                else
                {
                    return SyntacticResultOf(_factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword));
                }
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionExpression:
                return WithNewScope(context, node, () => TypeFromFunctionLikeExpression(node, context));
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return TypeFromTypeAssertion(
                    node is AsExpression asExpression ? asExpression.Expression! : ((TypeAssertion)node).Expression!,
                    node is AsExpression asExpressionType ? asExpressionType.Type! : ((TypeAssertion)node).Type!,
                    context,
                    requiresAddingUndefined);
            case SyntaxKind.PrefixUnaryExpression:
                var unaryExpression = (PrefixUnaryExpression)node;
                if (IsPrimitiveLiteralValue(unaryExpression))
                {
                    return TypeFromPrimitiveLiteral(
                        unaryExpression.Operator == SyntaxKind.PlusToken ? unaryExpression.Operand! : unaryExpression,
                        unaryExpression.Operand!.Kind == SyntaxKind.BigIntLiteral ? SyntaxKind.BigIntKeyword : SyntaxKind.NumberKeyword,
                        context,
                        isConstContext || preserveLiterals,
                        requiresAddingUndefined);
                }
                break;
            case SyntaxKind.ArrayLiteralExpression:
                return TypeFromArrayLiteral((ArrayLiteralExpression)node, context, isConstContext, requiresAddingUndefined);
            case SyntaxKind.ObjectLiteralExpression:
                return TypeFromObjectLiteral((ObjectLiteralExpression)node, context, isConstContext, requiresAddingUndefined);
            case SyntaxKind.ClassExpression:
                return SyntacticResultOf(InferExpressionType((ClassExpression)node, context, /*reportFallback*/ true, requiresAddingUndefined));
            case SyntaxKind.TemplateExpression:
                if (!isConstContext && !preserveLiterals)
                {
                    return SyntacticResultOf(_factory.CreateKeywordTypeNode(SyntaxKind.StringKeyword));
                }
                break;
            default:
                SyntaxKind? typeKind = null;
                Expression primitiveNode = node;
                switch (node.Kind)
                {
                    case SyntaxKind.NumericLiteral:
                        typeKind = SyntaxKind.NumberKeyword;
                        break;
                    case SyntaxKind.NoSubstitutionTemplateLiteral:
                        primitiveNode = _factory.CreateStringLiteral(((NoSubstitutionTemplateLiteral)node).Text);
                        typeKind = SyntaxKind.StringKeyword;
                        break;
                    case SyntaxKind.StringLiteral:
                        typeKind = SyntaxKind.StringKeyword;
                        break;
                    case SyntaxKind.BigIntLiteral:
                        typeKind = SyntaxKind.BigIntKeyword;
                        break;
                    case SyntaxKind.TrueKeyword:
                    case SyntaxKind.FalseKeyword:
                        typeKind = SyntaxKind.BooleanKeyword;
                        break;
                }
                if (typeKind is not null)
                {
                    return TypeFromPrimitiveLiteral(primitiveNode, typeKind.Value, context, isConstContext || preserveLiterals, requiresAddingUndefined);
                }
                break;
        }
        return Failed;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:970-980 — <c>typeFromFunctionLikeExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>FunctionExpression | ArrowFunction</c> widens to <c>Node</c>; both implement <see cref="IFunctionLikeDeclarationBase"/>, which is where the <c>typeParameters</c>/<c>parameters</c> reads land.</remarks>
    private SyntacticResult TypeFromFunctionLikeExpression(Node fnNode, ISyntacticTypeNodeBuilderContext context)
    {
        var functionLike = (IFunctionLikeDeclarationBase)fnNode;
        var returnType = CreateReturnFromSignature(fnNode, /*symbol*/ null, context);
        var typeParameters = ReuseTypeParameters(functionLike.TypeParameters, context);
        var parameters = Core.Map(functionLike.Parameters, (p, _) => EnsureParameter(p, context)!);
        return SyntacticResultOf(
            _factory.CreateFunctionTypeNode(
                typeParameters,
                parameters,
                returnType));
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:982-993 — <c>canGetTypeFromArrayLiteral</c>.</summary>
    private bool CanGetTypeFromArrayLiteral(ArrayLiteralExpression arrayLiteral, ISyntacticTypeNodeBuilderContext context, bool isConstContext)
    {
        if (!isConstContext)
        {
            ReportInferenceFallback(context, arrayLiteral);
            return false;
        }
        foreach (var element in arrayLiteral.Elements!)
        {
            if (element.Kind == SyntaxKind.SpreadElement)
            {
                ReportInferenceFallback(context, element);
                return false;
            }
        }
        return true;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:995-1022 — <c>typeFromArrayLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference builds the tuple type and then answers <c>notImplemented</c> without using it (the
    /// scaffolding of an implementation that never landed); the port keeps that exact shape, including the discarded
    /// node and its emit flags, because the reference's answer is what every caller sees.
    /// </remarks>
    private SyntacticResult TypeFromArrayLiteral(ArrayLiteralExpression arrayLiteral, ISyntacticTypeNodeBuilderContext context, bool isConstContext, bool requiresAddingUndefined)
    {
        if (!CanGetTypeFromArrayLiteral(arrayLiteral, context, isConstContext))
        {
            if (requiresAddingUndefined || Utilities.IsDeclaration(Utilities.WalkUpParenthesizedExpressions(arrayLiteral).Parent!))
            {
                return AlreadyReported;
            }
            return SyntacticResultOf(InferExpressionType(arrayLiteral, context, /*reportFallback*/ false, requiresAddingUndefined));
        }
        // Disable any inference fallback since we won't actually use the resulting type and we don't want to generate errors
        var oldNoInferenceFallback = context.NoInferenceFallback;
        context.NoInferenceFallback = true;
        var elementTypesInfo = new List<ITypeNode>();
        foreach (var element in arrayLiteral.Elements!)
        {
            Debug.Assert(element.Kind != SyntaxKind.SpreadElement);
            if (element.Kind == SyntaxKind.OmittedExpression)
            {
                elementTypesInfo.Add(
                    CreateUndefinedTypeNode());
            }
            else
            {
                var expressionType = TypeFromExpression(element, context, isConstContext);
                var elementType = expressionType.Type ?? InferExpressionType(element, context, expressionType.ReportFallback);
                elementTypesInfo.Add(elementType);
            }
        }
        var tupleType = _factory.CreateTupleTypeNode([.. elementTypesInfo.Cast<Node>()]);
        // PORT-NOTE: TS's `{ flags: 1, autoGenerate: undefined, internalFlags: 0 }` is the emit-node literal whose
        // `flags` is `EmitFlags.SingleLine`; the port names the flag and leaves the other two at their defaults.
        tupleType.EmitNode = new EmitNode { Flags = EmitFlags.SingleLine, InternalFlags = InternalEmitFlags.None };
        context.NoInferenceFallback = oldNoInferenceFallback;
        return NotImplemented;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1024-1051 — <c>canGetTypeFromObjectLiteral</c>.</summary>
    /// <remarks>PORT-NOTE: TS reads <c>prop.name</c> on the object-literal element union; the port reads the same slot through <see cref="Utilities.GetNamedDeclarationName"/>, which is the honest C# spelling of the duck-typed read (the shorthand/spread arms are tested first, and neither has a name).</remarks>
    private bool CanGetTypeFromObjectLiteral(ObjectLiteralExpression objectLiteral, ISyntacticTypeNodeBuilderContext context)
    {
        var result = true;
        foreach (var prop in objectLiteral.Properties!)
        {
            if ((prop.Flags & NodeFlags.ThisNodeHasError) != 0)
            {
                result = false;
                break; // Bail if parse errors
            }
            if (prop.Kind is SyntaxKind.ShorthandPropertyAssignment or SyntaxKind.SpreadAssignment)
            {
                ReportInferenceFallback(context, prop);
                result = false;
            }
            else if ((Utilities.GetNamedDeclarationName(prop)!.Flags & NodeFlags.ThisNodeHasError) != 0)
            {
                result = false;
                break; // Bail if parse errors
            }
            else if (Utilities.GetNamedDeclarationName(prop)!.Kind == SyntaxKind.PrivateIdentifier)
            {
                // Not valid in object literals but the compiler will complain about this, we just ignore it here.
                result = false;
            }
            else if (Utilities.GetNamedDeclarationName(prop)!.Kind == SyntaxKind.ComputedPropertyName)
            {
                var expression = ((ComputedPropertyName)Utilities.GetNamedDeclarationName(prop)!).Expression!;
                if (!IsPrimitiveLiteralValue(expression, /*includeBigInt*/ false) && !_resolver.IsDefinitelyReferenceToGlobalSymbolObject(expression))
                {
                    ReportInferenceFallback(context, Utilities.GetNamedDeclarationName(prop)!);
                    result = false;
                }
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1053-1096 — <c>typeFromObjectLiteral</c>.</summary>
    private SyntacticResult TypeFromObjectLiteral(ObjectLiteralExpression objectLiteral, ISyntacticTypeNodeBuilderContext context, bool isConstContext, bool requiresAddingUndefined)
    {
        if (!CanGetTypeFromObjectLiteral(objectLiteral, context))
        {
            if (requiresAddingUndefined || Utilities.IsDeclaration(Utilities.WalkUpParenthesizedExpressions(objectLiteral).Parent!))
            {
                return AlreadyReported;
            }
            return SyntacticResultOf(InferExpressionType(objectLiteral, context, /*reportFallback*/ false, requiresAddingUndefined));
        }
        // Disable any inference fallback since we won't actually use the resulting type and we don't want to generate errors
        var oldNoInferenceFallback = context.NoInferenceFallback;
        context.NoInferenceFallback = true;
        var properties = new List<ITypeElement>();
        var oldFlags = context.Flags;
        context.Flags |= NodeBuilderFlags.InObjectTypeLiteral;
        foreach (var prop in objectLiteral.Properties!)
        {
            Debug.Assert(!NodeTests.IsShorthandPropertyAssignment(prop) && !NodeTests.IsSpreadAssignment(prop));

            var name = Utilities.GetNamedDeclarationName(prop)!;
            ITypeElement? newProp = null;
            switch (prop.Kind)
            {
                case SyntaxKind.MethodDeclaration:
                    newProp = WithNewScope(context, prop, () => TypeFromObjectLiteralMethod((MethodDeclaration)prop, name, context, isConstContext));
                    break;
                case SyntaxKind.PropertyAssignment:
                    newProp = TypeFromObjectLiteralPropertyAssignment((PropertyAssignment)prop, name, context, isConstContext);
                    break;
                case SyntaxKind.SetAccessor:
                case SyntaxKind.GetAccessor:
                    newProp = TypeFromObjectLiteralAccessor(prop, name, context);
                    break;
            }
            if (newProp is not null)
            {
                EmitNodes.SetCommentRange((Node)newProp, prop);
                properties.Add(newProp);
            }
        }

        context.Flags = oldFlags;
        var typeNode = _factory.CreateTypeLiteralNode(properties);
        if ((context.Flags & NodeBuilderFlags.MultilineObjectLiterals) == 0)
        {
            EmitNodes.SetEmitFlags(typeNode, EmitFlags.SingleLine);
        }
        context.NoInferenceFallback = oldNoInferenceFallback;
        return NotImplemented;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1098-1111 — <c>typeFromObjectLiteralPropertyAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>name: PropertyName</c> widens to <c>Node</c>; <c>createPropertySignature</c>'s name slot is <c>object?</c>, which is what the port's factory declares.</remarks>
    private ITypeElement TypeFromObjectLiteralPropertyAssignment(PropertyAssignment prop, Node name, ISyntacticTypeNodeBuilderContext context, bool isConstContext)
    {
        List<ModifierToken> modifiers = isConstContext ?
            [_factory.CreateModifier(SyntaxKind.ReadonlyKeyword)] :
            [];
        var expressionResult = TypeFromExpression(prop.Initializer!, context, isConstContext);
        var typeNode = expressionResult.Type ?? InferTypeOfDeclaration(prop, /*symbol*/ null, context, expressionResult.ReportFallback);

        return _factory.CreatePropertySignature(
            modifiers,
            ReuseNode(context, name),
            /*questionToken*/ null,
            typeNode);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1113-1122 — <c>ensureParameter</c>.</summary>
    private ParameterDeclaration EnsureParameter(ParameterDeclaration p, ISyntacticTypeNodeBuilderContext context)
        => _factory.UpdateParameterDeclaration(
            p,
            /*modifiers*/ null,
            ReuseNode(context, p.DotDotDotToken),
            _resolver.SerializeNameOfParameter(context, p),
            _resolver.IsOptionalParameter(p) ? (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken) : null,
            TypeFromParameter(p, /*symbol*/ null, context), // Ignore private param props, since this type is going straight back into a param
            /*initializer*/ null);

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1124-1135 — <c>reuseTypeParameters</c>.</summary>
    private List<TypeParameterDeclaration>? ReuseTypeParameters(NodeArray<TypeParameterDeclaration>? typeParameters, ISyntacticTypeNodeBuilderContext context)
        => Core.Map(typeParameters, (tp, _) =>
        {
            var (_, tpName) = TrackExistingEntityName(context, tp.Name!);
            return _factory.UpdateTypeParameterDeclaration(
                tp,
                Core.Map<ModifierToken, ModifierToken>(tp.Modifiers, (m, _) => ReuseNode(context, m)!),
                tpName,
                SerializeExistingTypeNodeWithFallback(tp.Constraint, context),
                SerializeExistingTypeNodeWithFallback(tp.Default, context));
        });

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1137-1165 — <c>typeFromObjectLiteralMethod</c>.</summary>
    private ITypeElement TypeFromObjectLiteralMethod(MethodDeclaration method, Node name, ISyntacticTypeNodeBuilderContext context, bool isConstContext)
    {
        var returnType = CreateReturnFromSignature(method, /*symbol*/ null, context);
        var typeParameters = ReuseTypeParameters(method.TypeParameters, context);
        var parameters = Core.Map(method.Parameters, (p, _) => EnsureParameter(p, context)!);
        if (isConstContext)
        {
            return _factory.CreatePropertySignature(
                [_factory.CreateModifier(SyntaxKind.ReadonlyKeyword)],
                ReuseNode(context, name),
                ReuseNode(context, method.QuestionToken),
                _factory.CreateFunctionTypeNode(
                    typeParameters,
                    parameters,
                    returnType));
        }
        else
        {
            if (NodeTests.IsIdentifier(name) && ((Identifier)name).EscapedText == "new")
            {
                name = _factory.CreateStringLiteral("new", null);
            }
            return _factory.CreateMethodSignature(
                [],
                ReuseNode(context, name),
                ReuseNode(context, method.QuestionToken),
                typeParameters,
                parameters,
                returnType);
        }
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1167-1210 — <c>typeFromObjectLiteralAccessor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>GetAccessorDeclaration | SetAccessorDeclaration</c> widens to <c>Node</c>; the <c>isGetAccessor</c> arm licenses the <c>GetAccessorDeclaration</c> cast.</remarks>
    private ITypeElement? TypeFromObjectLiteralAccessor(Node accessor, Node name, ISyntacticTypeNodeBuilderContext context)
    {
        var allAccessors = _resolver.GetAllAccessorDeclarations(accessor);
        var getAccessorType = allAccessors.GetAccessor is not null ? GetTypeAnnotationFromAccessor(allAccessors.GetAccessor) : null;
        var setAccessorType = allAccessors.SetAccessor is not null ? GetTypeAnnotationFromAccessor(allAccessors.SetAccessor) : null;
        // We have types for both accessors, we can't know if they are the same type so we keep both accessors
        if (getAccessorType is not null && setAccessorType is not null)
        {
            return WithNewScope<ITypeElement?>(context, accessor, () =>
            {
                var parameters = Core.Map(((IFunctionLikeDeclarationBase)accessor).Parameters, (p, _) => EnsureParameter(p, context)!);

                if (Utilities.IsGetAccessor(accessor))
                {
                    return _factory.UpdateGetAccessorDeclaration(
                        (GetAccessorDeclaration)accessor,
                        [],
                        ReuseNode(context, name),
                        parameters,
                        SerializeExistingTypeNodeWithFallback(getAccessorType, context),
                        /*body*/ null);
                }
                else
                {
                    return _factory.UpdateSetAccessorDeclaration(
                        (SetAccessorDeclaration)accessor,
                        [],
                        ReuseNode(context, name),
                        parameters,
                        /*body*/ null);
                }
            });
        }
        else if (ReferenceEquals(allAccessors.FirstAccessor, accessor))
        {
            var foundType = getAccessorType is not null ? WithNewScope(context, allAccessors.GetAccessor!, () => SerializeExistingTypeNodeWithFallback(getAccessorType, context)) :
                setAccessorType is not null ? WithNewScope(context, allAccessors.SetAccessor!, () => SerializeExistingTypeNodeWithFallback(setAccessorType, context)) :
                null;
            var propertyType = foundType ?? InferAccessorType(accessor, allAccessors, context, /*symbol*/ null);

            var propertySignature = _factory.CreatePropertySignature(
                allAccessors.SetAccessor is null ? [_factory.CreateModifier(SyntaxKind.ReadonlyKeyword)] : [],
                ReuseNode(context, name),
                /*questionToken*/ null,
                propertyType);
            return propertySignature;
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // typeFromPrimitiveLiteral (1220-1232)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1220-1232 — <c>typeFromPrimitiveLiteral</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>PrimitiveLiteral</c> union and <c>KeywordTypeSyntaxKind</c> union widen to <c>Expression</c> and <c>SyntaxKind</c>; the <c>PrefixUnaryExpression</c>/<c>PlusToken</c> read is guarded by the kind test.</remarks>
    private SyntacticResult TypeFromPrimitiveLiteral(Expression node, SyntaxKind baseType, ISyntacticTypeNodeBuilderContext context, bool preserveLiterals, bool requiresAddingUndefined)
    {
        ITypeNode result;
        if (preserveLiterals)
        {
            if (node.Kind == SyntaxKind.PrefixUnaryExpression && ((PrefixUnaryExpression)node).Operator == SyntaxKind.PlusToken)
            {
                result = _factory.CreateLiteralTypeNode(ReuseNode(context, (UpdateExpression)((PrefixUnaryExpression)node).Operand!));
            }
            result = _factory.CreateLiteralTypeNode(ReuseNode(context, (UpdateExpression)node));
        }
        else
        {
            result = _factory.CreateKeywordTypeNode(baseType);
        }
        return SyntacticResultOf(AddUndefinedIfNeeded(result, requiresAddingUndefined, node, context));
    }

    // ---------------------------------------------------------------------------------------------
    // createReturnFromSignature / typeFromSingleReturnExpression (1270-1322)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1270-1280 — <c>createReturnFromSignature</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>SignatureDeclaration | JSDocSignature</c> widens to <c>Node</c>; the <c>isJSDocConstructSignature</c> arm licenses the <c>JSDocFunctionType</c> cast of the parameter list.</remarks>
    private ITypeNode? CreateReturnFromSignature(Node fn, ISymbol? symbol, ISyntacticTypeNodeBuilderContext context, bool reportFallback = true)
    {
        var returnType = Failed;
        var returnTypeNode = Utilities.IsJSDocConstructSignature(fn) ? Utilities.GetEffectiveTypeAnnotationNode(((JSDocFunctionType)fn).Parameters![0]) : Utilities.GetEffectiveReturnTypeNode(fn);
        if (returnTypeNode is not null)
        {
            returnType = SyntacticResultOf(SerializeTypeAnnotationOfDeclaration(returnTypeNode, context, fn, symbol));
        }
        else if (Utilities.IsValueSignatureDeclaration(fn))
        {
            returnType = TypeFromSingleReturnExpression(fn, context);
        }
        return returnType.Type ?? InferReturnTypeOfSignatureSignature(fn, context, symbol, reportFallback && returnType.ReportFallback && returnTypeNode is null);
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1282-1322 — <c>typeFromSingleReturnExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>FunctionLikeDeclaration | undefined</c> widens to <c>Node?</c>; the caller (<c>createReturnFromSignature</c>) reaches it only under <c>isValueSignatureDeclaration</c>, which is the guard the <see cref="IFunctionLikeDeclarationBase"/> cast needs.</remarks>
    private SyntacticResult TypeFromSingleReturnExpression(Node? declaration, ISyntacticTypeNodeBuilderContext context)
    {
        Expression? candidateExpr = null;
        if (declaration is not null && !Utilities.NodeIsMissing(((IFunctionLikeDeclarationBase)declaration).Body))
        {
            var flags = Utilities.GetFunctionFlags(declaration);
            if ((flags & FunctionFlags.AsyncGenerator) != 0) return Failed;

            var body = ((IFunctionLikeDeclarationBase)declaration).Body;
            if (body is not null && NodeTests.IsBlock(body))
            {
                Utilities.ForEachReturnStatement<bool?>((Block)body, s =>
                {
                    if (!ReferenceEquals(s.Parent, body))
                    {
                        candidateExpr = null;
                        return true;
                    }
                    if (candidateExpr is null)
                    {
                        candidateExpr = s.Expression;
                    }
                    else
                    {
                        candidateExpr = null;
                        return true;
                    }
                    // PORT-NOTE: TS's callback falls off the end here, i.e. answers `undefined` ("keep walking").
                    return null;
                });
            }
            else
            {
                candidateExpr = (Expression?)body;
            }
        }
        if (candidateExpr is not null)
        {
            if (IsContextuallyTyped(candidateExpr))
            {
                var type = FactoryUtilities.IsJSDocTypeAssertion(candidateExpr) ? FactoryUtilities.GetJSDocTypeAssertionType(candidateExpr) :
                    candidateExpr is AsExpression asExpression ? asExpression.Type :
                    candidateExpr.Kind == SyntaxKind.TypeAssertionExpression ? ((TypeAssertion)candidateExpr).Type :
                    null;
                if (type is not null && !Utilities.IsConstTypeReference((Node)type))
                {
                    return SyntacticResultOf(SerializeExistingTypeNode(type, context));
                }
            }
            else
            {
                return TypeFromExpression(candidateExpr, context);
            }
        }
        return Failed;
    }

    /// <summary>ported from: src/compiler/expressionToTypeNode.ts:1324-1328 — <c>isContextuallyTyped</c>.</summary>
    private static bool IsContextuallyTyped(Node node)
    {
        return Utilities.FindAncestor(node.Parent, n =>
        {
            // Functions calls or parent type annotations (but not the return type of a function expression) may impact the inferred type and local inference is unreliable
            return NodeTests.IsCallExpression(n) || (!Utilities.IsFunctionLikeDeclaration(n) && Utilities.GetEffectiveTypeAnnotationNode(n) is not null) || NodeTests.IsJsxElement(n) || NodeTests.IsJsxExpression(n);
        }) is not null;
    }

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:12030 (isPrimitiveLiteralValue) and :2685 (isVarConstLike) — see the class remarks
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:12030-12052 — <c>isPrimitiveLiteralValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>PrimitiveLiteral</c>, which has no C# face, so the port answers <c>bool</c>
    /// and the arm that needs the literal back reads it through the kind test that established it.
    /// </remarks>
    private static bool IsPrimitiveLiteralValue(Expression node, bool includeBigInt = true)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return true;
            case SyntaxKind.BigIntLiteral:
                return includeBigInt;
            case SyntaxKind.PrefixUnaryExpression:
                var operand = ((PrefixUnaryExpression)node).Operand!;
                if (((PrefixUnaryExpression)node).Operator == SyntaxKind.MinusToken)
                {
                    return NodeTests.IsNumericLiteral(operand) || (includeBigInt && NodeTests.IsBigIntLiteral(operand));
                }
                if (((PrefixUnaryExpression)node).Operator == SyntaxKind.PlusToken)
                {
                    return NodeTests.IsNumericLiteral(operand);
                }
                return false;
            default:
                Debug.AssertNever(node, $"not a primitive literal: {Debug.FormatSyntaxKind(node.Kind)}");
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:2685-2690 — <c>isVarConstLike</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>VariableDeclaration | VariableDeclarationList</c>; the port widens to <c>Node</c>, which is what <c>getCombinedNodeFlags</c> reads. The checker's own <c>IsVarConstLike</c> (<c>Checker.Check.Sort.cs:102</c>) is the same test on the cached flags and is not reachable from here.</remarks>
    private static bool IsVarConstLike(Node node)
    {
        var blockScopeKind = Utilities.GetCombinedNodeFlags(node) & NodeFlags.BlockScoped;
        return blockScopeKind == NodeFlags.Const ||
            blockScopeKind == NodeFlags.Using ||
            blockScopeKind == NodeFlags.AwaitUsing;
    }

    /// <summary>ported from: src/compiler/checker.ts:1767-1769 — a <c>DynamicNamedDeclaration</c> view for <c>shouldRemoveDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference hands <c>resolver.shouldRemoveDeclaration</c> the node it has just proved has a computed
    /// name, viewed structurally as types.ts's <c>DynamicNamedDeclaration</c> interface. The port's model has the same
    /// name as an abstract class that no parsed node derives from, and the resolver's slot takes that class, so the view
    /// is materialized here — the same shape <c>Checker.Check.Classes.cs:86-95</c> records for the reference's other
    /// <c>as DynamicNamedDeclaration</c> view. <c>Kind</c> is never read by the consumer (it reads <c>name</c> and the
    /// context only), so the view answers <c>Unknown</c> rather than inventing a kind the node does not have. When the
    /// resolver's slot widens (see the slice report), this class and the call site's view both go away.
    /// </remarks>
    private sealed class DynamicNamedDeclarationView : DynamicNamedDeclaration
    {
        public override SyntaxKind Kind => SyntaxKind.Unknown;

        internal DynamicNamedDeclarationView(ComputedPropertyName name) => Name = name;
    }
}
