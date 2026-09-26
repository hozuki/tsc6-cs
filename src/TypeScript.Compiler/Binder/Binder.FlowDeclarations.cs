namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:2025-2112 (bindDeleteExpressionFlow through bindInitializer)
/// <summary>
/// The flow binders of the declaration-shaped nodes: a conditional expression's two branches, the assignment a
/// declaration's initializer performs, and the order in which a binding element or parameter evaluates its initializer
/// before its name.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>bindInitializedVariableFlow</c>'s parameter is TS's <c>VariableDeclaration | ArrayBindingElement</c>
/// union (so: a variable declaration, a binding element or an omitted expression); the three have no C# anchor below
/// <see cref="Node"/>, which is the honest upper bound, and the two named member's <c>name</c> slots are read back with the
/// class patterns the reference's <c>isOmittedExpression</c> guard implies. The binding pattern's elements are read
/// through <see cref="BindingPatternElements"/>, because the model has no <c>BindingPattern</c> class — the two pattern
/// classes share only <see cref="Node"/>.
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:2025-2030 — <c>bindDeleteExpressionFlow</c>.</summary>
    private void BindDeleteExpressionFlow(DeleteExpression node)
    {
        BindEachChild(node);
        if (node.Expression is PropertyAccessExpression propertyAccessExpression)
        {
            BindAssignmentTargetFlow(propertyAccessExpression);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2032-2056 — <c>bindConditionalExpressionFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>hasFlowEffects ||= saveHasFlowEffects</c> has no C# operator (§10.2); the same value is
    /// stated as the plain or-assignment.</remarks>
    private void BindConditionalExpressionFlow(ConditionalExpression node)
    {
        var trueLabel = CreateBranchLabel();
        var falseLabel = CreateBranchLabel();
        var postExpressionLabel = CreateBranchLabel();
        var saveCurrentFlow = _currentFlow;
        var saveHasFlowEffects = _hasFlowEffects;
        _hasFlowEffects = false;
        BindCondition(node.Condition, trueLabel, falseLabel);
        _currentFlow = FinishFlowLabel(trueLabel);
        if (_inReturnPosition)
        {
            node.FlowNodeWhenTrue = _currentFlow;
        }

        Bind(node.QuestionToken);
        Bind(node.WhenTrue);
        AddAntecedent(postExpressionLabel, _currentFlow!);
        _currentFlow = FinishFlowLabel(falseLabel);
        if (_inReturnPosition)
        {
            node.FlowNodeWhenFalse = _currentFlow;
        }

        Bind(node.ColonToken);
        Bind(node.WhenFalse);
        AddAntecedent(postExpressionLabel, _currentFlow!);
        _currentFlow = _hasFlowEffects ? FinishFlowLabel(postExpressionLabel) : saveCurrentFlow;
        _hasFlowEffects = _hasFlowEffects || saveHasFlowEffects;
    }

    /// <summary>ported from: src/compiler/binder.ts:2058-2068 — <c>bindInitializedVariableFlow</c>.</summary>
    private void BindInitializedVariableFlow(Node node)
    {
        var name = node switch
        {
            VariableDeclaration variableDeclaration => variableDeclaration.Name,
            BindingElement bindingElement => bindingElement.Name,
            _ => null, // an omitted expression is not a named element
        };
        if (Utilities.IsBindingPattern(name))
        {
            if (BindingPatternElements(name!) is { } elements)
            {
                foreach (var child in elements)
                {
                    BindInitializedVariableFlow((Node)child);
                }
            }
        }
        else
        {
            _currentFlow = CreateFlowMutation(FlowFlags.Assignment, _currentFlow!, node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2070-2075 — <c>bindVariableDeclarationFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node.parent.parent</c> is a non-optional read — a variable declaration is bound from
    /// inside its declaration list. The model's parent pointers are nullable, so the same read is the pattern that proves a
    /// grandparent exists; without one the declaration is not in a <c>for…in</c>/<c>for…of</c> head, which is what the
    /// reference's type said.</remarks>
    private void BindVariableDeclarationFlow(VariableDeclaration node)
    {
        BindEachChild(node);
        if (node.Initializer is not null
            || node.Parent?.Parent is { } grandParent && Utilities.IsForInOrOfStatement(grandParent))
        {
            BindInitializedVariableFlow(node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:2077-2087 — <c>bindBindingElementFlow</c>.</summary>
    private void BindBindingElementFlow(BindingElement node)
    {
        // When evaluating a binding pattern, the initializer is evaluated before the binding pattern, per:
        // - https://tc39.es/ecma262/#sec-destructuring-binding-patterns-runtime-semantics-iteratorbindinginitialization
        //   - `BindingElement: BindingPattern Initializer?`
        // - https://tc39.es/ecma262/#sec-runtime-semantics-keyedbindinginitialization
        //   - `BindingElement: BindingPattern Initializer?`
        Bind(node.DotDotDotToken);
        Bind(node.PropertyName);
        BindInitializer(node.Initializer);
        Bind(node.Name);
    }

    /// <summary>ported from: src/compiler/binder.ts:2089-2096 — <c>bindParameterFlow</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>bind(node.type)</c> passes a <c>TypeNode</c>; the port's slot is the capability interface
    /// <see cref="ITypeNode"/>, and <c>bind</c> takes the node class, hence the cast (§12.9). The modifier list is a node
    /// array, which <c>bindEach</c> takes through its covariant view.</remarks>
    private void BindParameterFlow(ParameterDeclaration node)
    {
        BindEach(node.Modifiers);
        Bind(node.DotDotDotToken);
        Bind(node.QuestionToken);
        Bind(node.Type as Node);
        BindInitializer(node.Initializer);
        Bind(node.Name);
    }

    /// <summary>ported from: src/compiler/binder.ts:2099-2112 — <c>bindInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: the three identity comparisons of the reference are stated as <c>ReferenceEquals</c>, the same
    /// reference identity <c>===</c> tests for flow nodes.</remarks>
    private void BindInitializer(Expression? node)
    {
        // a BindingElement/Parameter does not have side effects if initializers are not evaluated and used. (see GH#49759)
        if (node is null)
        {
            return;
        }

        var entryFlow = _currentFlow;
        Bind(node);
        if (ReferenceEquals(entryFlow, _unreachableFlow) || ReferenceEquals(entryFlow, _currentFlow))
        {
            return;
        }

        var exitFlow = CreateBranchLabel();
        AddAntecedent(exitFlow, entryFlow!);
        AddAntecedent(exitFlow, _currentFlow!);
        _currentFlow = FinishFlowLabel(exitFlow);
    }

    /// <summary>The port of TS's <c>name.elements</c> read in <c>bindInitializedVariableFlow</c> (binder.ts:2061).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BindingPattern</c> union has one <c>elements</c> member; the model has no <c>BindingPattern</c>
    /// class — the two pattern classes share only <see cref="Node"/> — so the read is this kind switch, routed through the
    /// covariant <see cref="INodeArray{T}"/> the walk needs (the object pattern's elements are binding elements, the array
    /// pattern's are nodes). A node that is not a pattern has no elements, which is the walk the caller guarded against.
    /// </remarks>
    private static INodeArray<Node>? BindingPatternElements(Node name) => name switch
    {
        ArrayBindingPattern arrayBindingPattern => arrayBindingPattern.Elements,
        ObjectBindingPattern objectBindingPattern => objectBindingPattern.Elements,
        _ => null,
    };
}
