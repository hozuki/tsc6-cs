#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classFields.ts:354-3315 — the body of `transformClassFields`.
/// <summary>ported from: src/compiler/transformers/classFields.ts:354-3315 — the table of closures
/// <c>transformClassFields</c> returns its transformer from.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the state): the reference's <c>shouldTransformPrivateStaticElementsInFile</c>,
/// <c>enabledSubstitutions</c>, <c>classAliases</c>, <c>pendingExpressions</c>, <c>pendingStatements</c>,
/// <c>lexicalEnvironment</c>, <c>lexicalEnvironmentMap</c>, <c>noSubstitution</c>, <c>currentClassContainer</c>,
/// <c>currentClassElement</c>, <c>shouldSubstituteThisWithClassThis</c> and
/// <c>previousShouldSubstituteThisWithClassThis</c> are the factory function's locals that the closures mutate; they are
/// this class's fields.
/// </para>
/// <para>
/// PORT-NOTE (<c>shouldTransformAutoAccessors</c>): TS's is a <c>Ternary</c>; the port keeps <see cref="Ternary"/> and
/// the reference's two identity tests (<c>=== Ternary.True</c>, <c>=== Ternary.Maybe</c>) as <c>==</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>classAliases</c>): TS's is a sparse array indexed by node id
/// (<c>classAliases[getOriginalNodeId(node)] = alias</c>); the port keys a <c>Dictionary&lt;int, Identifier&gt;</c> by
/// the same id (<c>Utilities.GetNodeId</c>), and an absent entry reads as the reference's <c>undefined</c> hole.
/// </para>
/// <para>
/// PORT-NOTE (visitor results): TS's <c>VisitResult&lt;T&gt;</c> is <c>T | readonly Node[] | undefined</c>, which the
/// port spells <c>object?</c> and discriminates the same way <c>NodeFactory.CopyCustomPrologue</c> does (<c>null</c>,
/// then a node list, then a node).
/// </para>
/// </remarks>
internal sealed partial class ClassFieldsTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly ICompilerOptions _compilerOptions;
    private readonly ScriptTarget _languageVersion;
    private readonly bool _useDefineForClassFields;
    private readonly bool _legacyDecorators;
    private readonly bool _shouldTransformInitializersUsingSet;
    private readonly bool _shouldTransformInitializersUsingDefine;
    private readonly bool _shouldTransformInitializers;
    private readonly bool _shouldTransformPrivateElementsOrClassStaticBlocks;
    private readonly Ternary _shouldTransformAutoAccessors;
    private readonly bool _shouldTransformThisInStaticInitializers;
    private readonly bool _shouldTransformSuperInStaticInitializers;
    private readonly bool _shouldTransformAnything;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:398 — <c>previousOnSubstituteNode</c>.</summary>
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:401 — <c>previousOnEmitNode</c>.</summary>
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:433 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:404 — <c>shouldTransformPrivateStaticElementsInFile</c>.</summary>
    private bool _shouldTransformPrivateStaticElementsInFile;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:405 — <c>enabledSubstitutions</c>.</summary>
    private ClassPropertySubstitutionFlags _enabledSubstitutions = ClassPropertySubstitutionFlags.None;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:407 — <c>classAliases</c>.</summary>
    /// <remarks>PORT-NOTE: keyed by <c>Utilities.GetNodeId</c>, the index TS's sparse array uses.</remarks>
    private Dictionary<int, Identifier>? _classAliases;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:413 — <c>pendingExpressions</c>.</summary>
    private List<Expression>? _pendingExpressions;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:419 — <c>pendingStatements</c>.</summary>
    private List<IStatement>? _pendingStatements;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:422 — <c>lexicalEnvironment</c>.</summary>
    private ClassFieldsLexicalEnvironment? _lexicalEnvironment;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:423 — <c>lexicalEnvironmentMap</c>.</summary>
    private readonly Dictionary<Node, ClassFieldsLexicalEnvironment> _lexicalEnvironmentMap = [];

    /// <summary>ported from: src/compiler/transformers/classFields.ts:426 — <c>noSubstitution</c>.</summary>
    private readonly HashSet<Node> _noSubstitution = [];

    /// <summary>ported from: src/compiler/transformers/classFields.ts:428 — <c>currentClassContainer</c>.</summary>
    private IClassLikeDeclarationBase? _currentClassContainer;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:429 — <c>currentClassElement</c>.</summary>
    private IClassElement? _currentClassElement;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:430 — <c>shouldSubstituteThisWithClassThis</c>.</summary>
    private bool _shouldSubstituteThisWithClassThis;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:431 — <c>previousShouldSubstituteThisWithClassThis</c>.</summary>
    private bool _previousShouldSubstituteThisWithClassThis;

    /// <summary>ported from: src/compiler/transformers/classFields.ts:354-433 — the factory's prologue.</summary>
    public ClassFieldsTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _compilerOptions = context.GetCompilerOptions();
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
        _useDefineForClassFields = Utilities.GetUseDefineForClassFields(_compilerOptions);
        _legacyDecorators = Js.IsTruthy(_compilerOptions.ExperimentalDecorators);

        // Always transform field initializers using Set semantics when `useDefineForClassFields: false`.
        _shouldTransformInitializersUsingSet = !_useDefineForClassFields;

        // Transform field initializers using Define semantics when `useDefineForClassFields: true` and target < ES2022.
        _shouldTransformInitializersUsingDefine = _useDefineForClassFields && _languageVersion < ScriptTarget.ES2022;
        _shouldTransformInitializers = _shouldTransformInitializersUsingSet || _shouldTransformInitializersUsingDefine;

        // We need to transform private members and class static blocks when target < ES2022.
        _shouldTransformPrivateElementsOrClassStaticBlocks = _languageVersion < ScriptTarget.ES2022;

        // We need to transform `accessor` fields when target < ESNext.
        // We may need to transform `accessor` fields when `useDefineForClassFields: false`
        _shouldTransformAutoAccessors = _languageVersion < ScriptTarget.ESNext ? Ternary.True :
            !_useDefineForClassFields ? Ternary.Maybe :
            Ternary.False;

        // We need to transform `this` in a static initializer into a reference to the class
        // when target < ES2022 since the assignment will be moved outside of the class body.
        _shouldTransformThisInStaticInitializers = _languageVersion < ScriptTarget.ES2022;

        // We don't need to transform `super` property access when target <= ES5 because
        // the es2015 transformation handles those.
        _shouldTransformSuperInStaticInitializers = _shouldTransformThisInStaticInitializers && _languageVersion >= ScriptTarget.ES2015;

        _shouldTransformAnything = _shouldTransformInitializers
            || _shouldTransformPrivateElementsOrClassStaticBlocks
            || _shouldTransformAutoAccessors == Ternary.True;

        _previousOnSubstituteNode = context.OnSubstituteNode!;
        _previousOnEmitNode = context.OnEmitNode!;
        context.OnSubstituteNode = OnSubstituteNode;
        context.OnEmitNode = OnEmitNode;

        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:434-448 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        _lexicalEnvironment = null;
        _shouldTransformPrivateStaticElementsInFile = (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TransformPrivateStaticElements) != 0;
        if (!_shouldTransformAnything && !_shouldTransformPrivateStaticElementsInFile)
        {
            return node;
        }

        var visited = Utilities.VisitEachChildWorker(node, Visitor, _context);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:433 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:450-457 — <c>modifierVisitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>tryCast(node, isModifier)</c> answers the node or <c>undefined</c>; the port states
    /// that test as the conditional, since the predicate answers <c>bool</c>.</remarks>
    private object? ModifierVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.AccessorKeyword => ShouldTransformAutoAccessorsInCurrentClass() ? null : node,
            _ => Utilities.IsModifier(node) ? node : null,
        };

    /// <summary>ported from: src/compiler/transformers/classFields.ts:459-532 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Debug.fail</c> for a property or static block reached outside <c>classElementVisitor</c>
    /// returns <c>never</c>; the port states it as <see cref="Debug.Fail{T}(string?)"/> in expression position.</remarks>
    private object? Visitor(Node node)
    {
        if (
            (node.TransformFlags & TransformFlags.ContainsClassFields) == 0
            && (node.TransformFlags & TransformFlags.ContainsLexicalThisOrSuper) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case SyntaxKind.ClassDeclaration:
                return VisitClassDeclaration((ClassDeclaration)node);

            case SyntaxKind.ClassExpression:
                return VisitClassExpression((ClassExpression)node);

            case SyntaxKind.ClassStaticBlockDeclaration:
            case SyntaxKind.PropertyDeclaration:
                return Debug.Fail<object?>("Use `classElementVisitor` instead.");

            case SyntaxKind.PropertyAssignment:
                return VisitPropertyAssignment((PropertyAssignment)node);

            case SyntaxKind.VariableStatement:
                return VisitVariableStatement((VariableStatement)node);

            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration((VariableDeclaration)node);

            case SyntaxKind.Parameter:
                return VisitParameterDeclaration((ParameterDeclaration)node);

            case SyntaxKind.BindingElement:
                return VisitBindingElement((BindingElement)node);

            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment((ExportAssignment)node);

            case SyntaxKind.PrivateIdentifier:
                return VisitPrivateIdentifier((PrivateIdentifier)node);

            case SyntaxKind.PropertyAccessExpression:
                return VisitPropertyAccessExpression((PropertyAccessExpression)node);

            case SyntaxKind.ElementAccessExpression:
                return VisitElementAccessExpression((ElementAccessExpression)node);

            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, /*discarded*/ false);

            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression((BinaryExpression)node, /*discarded*/ false);

            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression((ParenthesizedExpression)node, /*discarded*/ false);

            case SyntaxKind.CallExpression:
                return VisitCallExpression((CallExpression)node);

            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement((ExpressionStatement)node);

            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression((TaggedTemplateExpression)node);

            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node);

            case SyntaxKind.ThisKeyword:
                return VisitThisExpression((ThisExpression)node);

            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
                // If we are descending into a new scope, clear the current class element
                return SetCurrentClassElementAnd<Node, object?>(
                    /*classElement*/ null,
                    FallbackVisitor,
                    node);

            case SyntaxKind.Constructor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                // If we are descending into a class element, set the class element
                return SetCurrentClassElementAnd<Node, object?>(
                    (IClassElement)node,
                    FallbackVisitor,
                    node);

            default:
                return FallbackVisitor(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:534-536 — <c>fallbackVisitor</c>.</summary>
    private object? FallbackVisitor(Node node)
        => Utilities.VisitEachChildWorker(node, Visitor, _context);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:538-557 — <c>discardedValueVisitor</c>: visits a
    /// node in an expression whose result is discarded.</summary>
    private object? DiscardedValueVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.PrefixUnaryExpression or SyntaxKind.PostfixUnaryExpression => VisitPreOrPostfixUnaryExpression(node, /*discarded*/ true),
            SyntaxKind.BinaryExpression => VisitBinaryExpression((BinaryExpression)node, /*discarded*/ true),
            SyntaxKind.CommaListExpression => VisitCommaListExpression((CommaListExpression)node, /*discarded*/ true),
            SyntaxKind.ParenthesizedExpression => VisitParenthesizedExpression((ParenthesizedExpression)node, /*discarded*/ true),
            _ => Visitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/classFields.ts:559-572 — <c>heritageClauseVisitor</c>: visits a node
    /// in a <c>HeritageClause</c>.</summary>
    private object? HeritageClauseVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.HeritageClause => Utilities.VisitEachChildWorker(node, HeritageClauseVisitor, _context),
            SyntaxKind.ExpressionWithTypeArguments => VisitExpressionWithTypeArgumentsInHeritageClause((ExpressionWithTypeArguments)node),
            _ => Visitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/classFields.ts:574-585 — <c>assignmentTargetVisitor</c>: visits the
    /// assignment target of a destructuring assignment.</summary>
    private object? AssignmentTargetVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.ObjectLiteralExpression or SyntaxKind.ArrayLiteralExpression => VisitAssignmentPattern((Expression)node),
            _ => Visitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/classFields.ts:587-625 — <c>classElementVisitor</c>: visits a member
    /// of a class.</summary>
    private object? ClassElementVisitor(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Constructor:
                return SetCurrentClassElementAnd(
                    (IClassElement)node,
                    VisitConstructorDeclaration,
                    (ConstructorDeclaration)node);

            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
                return SetCurrentClassElementAnd(
                    (IClassElement)node,
                    VisitMethodOrAccessorDeclaration,
                    node);

            case SyntaxKind.PropertyDeclaration:
                return SetCurrentClassElementAnd(
                    (IClassElement)node,
                    VisitPropertyDeclaration,
                    (PropertyDeclaration)node);

            case SyntaxKind.ClassStaticBlockDeclaration:
                return SetCurrentClassElementAnd(
                    (IClassElement)node,
                    VisitClassStaticBlockDeclaration,
                    (ClassStaticBlockDeclaration)node);

            case SyntaxKind.ComputedPropertyName:
                return VisitComputedPropertyName((ComputedPropertyName)node);

            case SyntaxKind.SemicolonClassElement:
                return node;

            default:
                return Utilities.IsModifierLike(node) ? ModifierVisitor(node) : Visitor(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:627-637 — <c>propertyNameVisitor</c>: visits a
    /// property name of a class member.</summary>
    private object? PropertyNameVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.ComputedPropertyName => VisitComputedPropertyName((ComputedPropertyName)node),
            _ => Visitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/classFields.ts:639-656 — <c>accessorFieldResultVisitor</c>: visits
    /// the results of an auto-accessor field transformation in a second pass.</summary>
    private object? AccessorFieldResultVisitor(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.PropertyDeclaration:
                return TransformFieldInitializer((PropertyDeclaration)node);
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return ClassElementVisitor(node);
            default:
                return Debug.Fail<object?>("Expected node to either be a PropertyDeclaration, GetAccessorDeclaration, or SetAccessorDeclaration");
        }
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:658-669 — <c>visitPrivateIdentifier</c>.</summary>
    /// <remarks>
    /// If we visit a private name, this means it is an undeclared private name. Replace it with an empty identifier to
    /// indicate a problem with the code, unless we are in a statement position — otherwise this will not trigger a
    /// SyntaxError.
    /// </remarks>
    private Node VisitPrivateIdentifier(PrivateIdentifier node)
    {
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            return node;
        }
        if (Utilities.IsStatement(node.Parent!))
        {
            return node;
        }
        return NodeFactory.SetOriginalNode(_factory.CreateIdentifier(""), node);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:671-683 — <c>transformPrivateIdentifierInInExpression</c>:
    /// visits <c>#id in expr</c>.</summary>
    private object? TransformPrivateIdentifierInInExpression(BinaryExpression node)
    {
        var info = AccessPrivateIdentifier((PrivateIdentifier)node.Left!);
        if (info is not null)
        {
            var receiver = Utilities.VisitNode(node.Right, Visitor, Utilities.IsExpression);
            return NodeFactory.SetOriginalNode(
                _emitHelpers.CreateClassPrivateFieldInHelper(info.BrandCheckIdentifier, receiver),
                node);
        }

        // Private name has not been declared. Subsequent transformers will handle this error
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:685-698 — <c>visitPropertyAssignment</c>.</summary>
    private object? VisitPropertyAssignment(PropertyAssignment node)
    {
        // 13.2.5.5 RS: PropertyDefinitionEvaluation
        //   PropertyAssignment : PropertyName `:` AssignmentExpression
        //     ...
        //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and _isProtoSetter_ is *false*, then
        //        a. Let _popValue_ be ? NamedEvaluation of |AssignmentExpression| with argument _propKey_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (PropertyAssignment)NamedEvaluation.TransformNamedEvaluation(_context, node);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:700-711 — <c>visitVariableStatement</c>.</summary>
    private object? VisitVariableStatement(VariableStatement node)
    {
        var savedPendingStatements = _pendingStatements;
        _pendingStatements = [];

        var visitedNode = Utilities.VisitEachChildWorker(node, Visitor, _context);
        object? statement = Core.Some(_pendingStatements)
            ? new List<IStatement> { visitedNode }.Concat(_pendingStatements!).ToList()
            : visitedNode;

        _pendingStatements = savedPendingStatements;
        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:713-733 — <c>visitVariableDeclaration</c>.</summary>
    private object? VisitVariableDeclaration(VariableDeclaration node)
    {
        // 14.3.1.2 RS: Evaluation
        //   LexicalBinding : BindingIdentifier Initializer
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...
        //
        // 14.3.2.1 RS: Evaluation
        //   VariableDeclaration : BindingIdentifier Initializer
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (VariableDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, node);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:735-757 — <c>visitParameterDeclaration</c>.</summary>
    private object? VisitParameterDeclaration(ParameterDeclaration node)
    {
        // 8.6.3 RS: IteratorBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     5. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...
        //
        // 14.3.3.3 RS: KeyedBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     4. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (ParameterDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, node);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:759-781 — <c>visitBindingElement</c>.</summary>
    private object? VisitBindingElement(BindingElement node)
    {
        // 8.6.3 RS: IteratorBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     5. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...
        //
        // 14.3.3.3 RS: KeyedBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     4. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (BindingElement)NamedEvaluation.TransformNamedEvaluation(_context, node);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:783-798 — <c>visitExportAssignment</c>.</summary>
    /// <remarks>
    /// NOTE: Since emit for <c>export =</c> translates to <c>module.exports = ...</c>, the assigned name of the class is
    /// <c>""</c>.
    /// </remarks>
    private object? VisitExportAssignment(ExportAssignment node)
    {
        // 16.2.3.7 RS: Evaluation
        //   ExportDeclaration : `export` `default` AssignmentExpression `;`
        //     1. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |AssignmentExpression| with argument `"default"`.
        //     ...

        if (NamedEvaluation.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (ExportAssignment)NamedEvaluation.TransformNamedEvaluation(
                _context,
                node,
                /*ignoreEmptyStringLiteral*/ true,
                Js.IsTruthy(node.IsExportEquals) ? "" : "default");
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:800-813 — <c>injectPendingExpressions</c>.</summary>
    private Expression InjectPendingExpressions(Expression expression)
    {
        if (Core.Some(_pendingExpressions))
        {
            if (NodeTests.IsParenthesizedExpression(expression))
            {
                _pendingExpressions!.Add(((ParenthesizedExpression)expression).Expression!);
                expression = _factory.UpdateParenthesizedExpression((ParenthesizedExpression)expression, _factory.InlineExpressions(_pendingExpressions));
            }
            else
            {
                _pendingExpressions!.Add(expression);
                expression = _factory.InlineExpressions(_pendingExpressions);
            }
            _pendingExpressions = null;
        }
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:815-818 — <c>visitComputedPropertyName</c>.</summary>
    private object? VisitComputedPropertyName(ComputedPropertyName node)
    {
        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
        return _factory.UpdateComputedPropertyName(node, InjectPendingExpressions(expression!));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:820-825 — <c>visitConstructorDeclaration</c>.</summary>
    private object? VisitConstructorDeclaration(ConstructorDeclaration node)
    {
        if (_currentClassContainer is not null)
        {
            return TransformConstructor(node, (Node)_currentClassContainer);
        }
        return FallbackVisitor(node);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:827-831 — <c>shouldTransformClassElementToWeakMap</c>.</summary>
    private bool ShouldTransformClassElementToWeakMap(Node node)
    {
        if (_shouldTransformPrivateElementsOrClassStaticBlocks) return true;
        if (Utilities.HasStaticModifier(node) && (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TransformPrivateStaticElements) != 0) return true;
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:833-867 — <c>visitMethodOrAccessorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the union): TS's parameter is <c>MethodDeclaration | AccessorDeclaration</c>, and every slot the body
    /// reads — <c>modifiers</c>, <c>asteriskToken</c>, <c>parameters</c>, <c>body</c> — is shared structurally by that
    /// union; the port's model states the three kinds as three classes (each declaring those four slots itself), so
    /// <see cref="MethodOrAccessorParts"/> is the projection that reads them, and the caller passes the node as it
    /// stands (a <c>GetAccessorDeclaration</c> is not a <c>MethodDeclaration</c> in this model).
    /// <para>
    /// PORT-NOTE (the modifier filter): the reference's <c>filter(node.modifiers, m =&gt; …)</c> answers a
    /// <c>Modifier[]</c>; the port projects the same elements into the <c>List&lt;ModifierToken&gt;?</c> the
    /// function-expression factory takes.
    /// </para>
    /// </remarks>
    private object? VisitMethodOrAccessorDeclaration(Node node)
    {
        if (!Utilities.IsPrivateIdentifierClassElementDeclaration(node) || !ShouldTransformClassElementToWeakMap(node))
        {
            return Utilities.VisitEachChildWorker(node, ClassElementVisitor, _context);
        }

        // leave invalid code untransformed
        var info = AccessPrivateIdentifier((PrivateIdentifier)((INamedDeclaration)node).Name!);
        Debug.Assert(info is not null, "Undeclared private name for property declaration.");
        if (!info!.IsValid)
        {
            return node;
        }

        var functionName = GetHoistedFunctionName(node);
        if (functionName is not null)
        {
            var (modifiers, asteriskToken, parameters, body) = MethodOrAccessorParts(node);
            GetPendingExpressions().Add(
                _factory.CreateAssignment(
                    functionName,
                    _factory.CreateFunctionExpression(
                        FilterMethodModifiers(modifiers),
                        asteriskToken,
                        functionName,
                        /*typeParameters*/ null,
                        Utilities.VisitParameterList(parameters, Visitor, _context),
                        /*type*/ null,
                        Utilities.VisitFunctionBody(body!, Visitor, _context))));
        }

        // remove method declaration from class
        return null;
    }

    /// <summary>The four slots <c>MethodDeclaration | AccessorDeclaration</c> shares in the reference.</summary>
    private static (NodeArray<Node>? Modifiers, PunctuationToken? AsteriskToken, NodeArray<ParameterDeclaration>? Parameters, Block? Body) MethodOrAccessorParts(Node node)
        => node switch
        {
            MethodDeclaration method => (method.Modifiers, method.AsteriskToken, method.Parameters, method.Body),
            GetAccessorDeclaration getter => (getter.Modifiers, getter.AsteriskToken, getter.Parameters, getter.Body),
            SetAccessorDeclaration setter => (setter.Modifiers, setter.AsteriskToken, setter.Parameters, setter.Body),
            _ => (null, null, null, null),
        };

    /// <summary>The reference's <c>filter(node.modifiers, m =&gt; isModifier(m) &amp;&amp; !isStaticModifier(m) &amp;&amp;
    /// !isAccessorModifier(m))</c>.</summary>
    private static List<ModifierToken>? FilterMethodModifiers(NodeArray<Node>? modifiers)
    {
        if (modifiers is null) return null;
        var result = new List<ModifierToken>();
        foreach (var modifier in modifiers)
        {
            if (Utilities.IsModifier(modifier)
                && !NodeTests.IsStaticModifier(modifier)
                && !NodeTests.IsAccessorModifier(modifier))
            {
                result.Add((ModifierToken)modifier);
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:869-882 — <c>setCurrentClassElementAnd</c>.</summary>
    private U SetCurrentClassElementAnd<T, U>(
        IClassElement? classElement,
        Func<T, U> visitor,
        T arg)
    {
        if (!ReferenceEquals(classElement, _currentClassElement))
        {
            var savedCurrentClassElement = _currentClassElement;
            _currentClassElement = classElement;
            var result = visitor(arg);
            _currentClassElement = savedCurrentClassElement;
            return result;
        }
        return visitor(arg);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:884-901 — <c>getHoistedFunctionName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference reads <c>info.kind</c>/<c>info.getterName</c> through the tagged union; the port
    /// discriminates with the two variant types the same switch selects.</remarks>
    private Identifier? GetHoistedFunctionName(Node node)
    {
        Debug.Assert(NodeTests.IsPrivateIdentifier(((INamedDeclaration)node).Name!));
        var info = AccessPrivateIdentifier((PrivateIdentifier)((INamedDeclaration)node).Name!);
        Debug.Assert(info is not null, "Undeclared private name for property declaration.");

        if (info is PrivateIdentifierMethodInfo method)
        {
            return method.MethodName;
        }

        if (info is PrivateIdentifierAccessorInfo accessor)
        {
            if (Utilities.IsGetAccessor(node))
            {
                return accessor.GetterName;
            }
            if (Utilities.IsSetAccessor(node))
            {
                return accessor.SetterName;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:903-906 — <c>tryGetClassThis</c>.</summary>
    private Identifier? TryGetClassThis()
    {
        var lex = GetClassLexicalEnvironment();
        return lex.ClassThis ?? lex.ClassConstructor ?? _currentClassContainer?.Name as Identifier;
    }
}
