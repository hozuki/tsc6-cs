#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/destructuring.ts:1-626 — the whole destructuring flattener
/// <summary>
/// ported from: src/compiler/transformers/destructuring.ts:99-626 — flattening a destructuring assignment or binding
/// into plain assignments and variable declarations: <c>flattenDestructuringAssignment</c>,
/// <c>flattenDestructuringBinding</c>, and the recursive element/pattern walk that builds the temporary variables,
/// default-value checks, property accesses and rest/slice calls they are made of.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports these as module-level functions of
/// <c>transformers/destructuring.ts</c>, so they land on the class named after that module and are called qualified
/// (<c>Destructuring.FlattenDestructuringBinding</c>). <see cref="FlattenLevel"/> is declared beside this class, which is
/// the treatment <c>Model/AssignmentKind.cs</c> gives the const enums of <c>utilities.ts</c>.
/// </para>
/// <para>
/// PORT-NOTE (the unions): the reference's <c>BindingOrAssignmentElement</c>, <c>BindingOrAssignmentElementTarget</c>,
/// <c>BindingOrAssignmentPattern</c>, <c>ArrayBindingOrAssignmentPattern</c> and <c>ObjectBindingOrAssignmentPattern</c>
/// have no generated interface in the model, and <c>isBindingName</c>/<c>isBindingElement</c> answer <c>bool</c> rather
/// than narrowing (§12.5, §10.1). Every slot those unions occupy is therefore <see cref="Node"/>, and the two
/// <c>Debug.assert*</c> calls the reference relies on for narrowing are kept as the port's
/// <see cref="Debug.AssertNode"/> — the same treatment the factory's binding-element helpers
/// (Parser/FactoryUtilities.cs:612-860) already record.
/// </para>
/// <para>
/// PORT-NOTE (<c>FlattenContext</c>): TS fills the interface in with an object literal at both entry points and the two
/// entries' <c>emitExpression</c>/<c>emitBindingOrAssignment</c> closures capture the entry point's locals; the port
/// states the same shape as a nested class whose two callback slots hold the entries' local functions.
/// </para>
/// <para>
/// PORT-NOTE (<c>append</c>): the reference's <c>append</c> is <see cref="Core.Append{T}"/> — it pushes onto the given
/// array and answers that same object (only an absent input allocates), which is why every accumulation below is a
/// <c>(List&lt;T&gt;?)</c> cast of its <c>IList&lt;T&gt;?</c> answer.
/// </para>
/// <para>
/// PORT-NOTE (<c>IReadOnlyList&lt;Node&gt;</c>): <c>factory/utilities.ts</c>'s
/// <c>getElementsOfBindingOrAssignmentPattern</c> is landed as
/// <see cref="FactoryUtilities.GetElementsOfBindingOrAssignmentPattern"/> answering
/// <c>IReadOnlyList&lt;Node&gt;</c> (the four element slots it reads have four different element types). Its readers in
/// this file index it and iterate it, which that type supports; where the reference passes the array to a
/// <c>core</c> helper that takes <c>IList&lt;T&gt;</c> the port writes the loop instead.
/// </para>
/// </remarks>
internal static class Destructuring
{
    /// <summary>ported from: src/compiler/transformers/destructuring.ts:66-78 — <c>FlattenContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>emitBindingOrAssignment</c> slot's <c>location</c> is the reference's non-optional
    /// <c>TextRange</c> but the second entry point's local function takes <c>TextRange | undefined</c>
    /// (<c>destructuring.ts:314</c>), so the port spells the slot <c>ITextRange?</c> — the wider of the two callers.
    /// The three pattern/element factories answer <see cref="Node"/> where TS names a union.
    /// </remarks>
    private sealed class FlattenContext
    {
        public ITransformationUtilitiesContext Context = null!;
        public FlattenLevel Level;
        public bool DownlevelIteration;
        public bool HoistTempVariables;
        public bool? HasTransformedPriorElement;
        public Action<Expression> EmitExpression = null!;
        public Action<Node, Expression, ITextRange?, Node?> EmitBindingOrAssignment = null!;
        public Func<List<Node>, Node> CreateArrayBindingOrAssignmentPattern = null!;
        public Func<List<Node>, Node> CreateObjectBindingOrAssignmentPattern = null!;
        public Func<Identifier, Node> CreateArrayBindingOrAssignmentElement = null!;
        public Func<Node, object?> Visitor = null!;
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:257-263 — the
    /// <c>{ pendingExpressions, name, value, location, original }</c> records <c>flattenDestructuringBinding</c>
    /// accumulates.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>name</c> is the <c>BindingName</c> union (which the recursive walk only fills after
    /// <c>Debug.assertNode(target, isBindingName)</c>), so the port's slot is <see cref="Node"/>.
    /// </remarks>
    private sealed class PendingDeclaration
    {
        public List<Expression>? PendingExpressions;
        public Node Name = null!;
        public Expression Value = null!;
        public ITextRange? Location;
        public Node? Original;
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:99-195 — <c>flattenDestructuringAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's first parameter is the union <c>VariableDeclaration | DestructuringAssignment</c> and its fifth a
    /// <c>boolean | undefined</c>; the port takes <see cref="Node"/> and <c>bool?</c>. The two <c>node</c> arms that the
    /// union guards (<c>node.right</c>/<c>node.left</c>) read the <see cref="BinaryExpression"/> the
    /// <c>isDestructuringAssignment</c> test licenses.
    /// </remarks>
    public static Expression FlattenDestructuringAssignment(
        Node node,
        Func<Node, object?> visitor,
        ITransformationUtilitiesContext context,
        FlattenLevel level,
        bool? needsValue = null,
        Func<Identifier, Expression, ITextRange?, Expression>? createAssignmentCallback = null)
    {
        ITextRange location = node;
        Expression? value = null;
        if (Utilities.IsDestructuringAssignment(node))
        {
            value = ((BinaryExpression)node).Right!;
            while (TransformationUtilities.IsEmptyArrayLiteral(((BinaryExpression)node).Left!) || Utilities.IsEmptyObjectLiteral(((BinaryExpression)node).Left!))
            {
                if (Utilities.IsDestructuringAssignment(value))
                {
                    location = node = value;
                    value = ((BinaryExpression)node).Right!;
                }
                else
                {
                    return Debug.CheckDefined(Utilities.VisitNode(value, visitor, Utilities.IsExpression));
                }
            }
        }

        List<Expression>? expressions = null;
        var flattenContext = new FlattenContext
        {
            Context = context,
            Level = level,
            DownlevelIteration = Js.IsTruthy(context.GetCompilerOptions().DownlevelIteration),
            HoistTempVariables = true,
            EmitExpression = EmitExpression,
            EmitBindingOrAssignment = EmitBindingOrAssignment,
            CreateArrayBindingOrAssignmentPattern = elements => MakeArrayAssignmentPattern(context.Factory, elements),
            CreateObjectBindingOrAssignmentPattern = elements => MakeObjectAssignmentPattern(context.Factory, elements),
            CreateArrayBindingOrAssignmentElement = MakeAssignmentElement,
            Visitor = visitor,
        };

        if (value is not null)
        {
            value = Utilities.VisitNode(value, visitor, Utilities.IsExpression);
            Debug.Assert(value is not null);

            if (
                NodeTests.IsIdentifier(value) && BindingOrAssignmentElementAssignsToName(node, ((Identifier)value).EscapedText!)
                || BindingOrAssignmentElementContainsNonLiteralComputedName(node)
            )
            {
                // If the right-hand value of the assignment is also an assignment target then
                // we need to cache the right-hand value.
                value = EnsureIdentifier(flattenContext, value, /*reuseIdentifierExpressions*/ false, location);
            }
            else if (Js.IsTruthy(needsValue))
            {
                // If the right-hand value of the destructuring assignment needs to be preserved (as
                // is the case when the destructuring assignment is part of a larger expression),
                // then we need to cache the right-hand value.
                value = EnsureIdentifier(flattenContext, value, /*reuseIdentifierExpressions*/ true, location);
            }
            else if (Utilities.NodeIsSynthesized(node))
            {
                location = value;
            }
        }

        FlattenBindingOrAssignmentElement(flattenContext, node, value, location, /*skipInitializer*/ Utilities.IsDestructuringAssignment(node));

        if (value is not null && Js.IsTruthy(needsValue))
        {
            if (!Core.Some(expressions))
            {
                return value;
            }

            expressions!.Add(value);
        }

        Expression? inline = context.Factory.InlineExpressions(expressions);
        return inline ?? context.Factory.CreateOmittedExpression();

        // ported from: src/compiler/transformers/destructuring.ts:146-148 — <c>emitExpression</c>.
        void EmitExpression(Expression expression)
        {
            expressions = (List<Expression>?)Core.Append(expressions, expression);
        }

        // ported from: src/compiler/transformers/destructuring.ts:150-161 — <c>emitBindingOrAssignment</c>.
        // PORT-NOTE: the reference's parameter is also named `value`, which C# does not allow a nested function to
        // shadow; the name is the only difference.
        void EmitBindingOrAssignment(Node target, Expression assignedValue, ITextRange? assignedLocation, Node? original)
        {
            Debug.AssertNode(target, createAssignmentCallback is not null ? NodeTests.IsIdentifier : Utilities.IsExpression);
            var expression = createAssignmentCallback is not null
                ? createAssignmentCallback((Identifier)target, assignedValue, assignedLocation)
                : Utilities.SetTextRange(
                    context.Factory.CreateAssignment(Debug.CheckDefined(Utilities.VisitNode((Expression)target, visitor, Utilities.IsExpression)), assignedValue),
                    assignedLocation);
            expression.Original = original;
            EmitExpression(expression);
        }
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:197-206 — <c>bindingOrAssignmentElementAssignsToName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>escapedName</c> is <c>__String</c>, a plain <c>string</c> in the port, and its
    /// <c>getTargetOfBindingOrAssignmentElement(element)!</c> is the invariant the factory's own helper handles.</remarks>
    private static bool BindingOrAssignmentElementAssignsToName(Node element, string escapedName)
    {
        var target = FactoryUtilities.GetTargetOfBindingOrAssignmentElement(element)!;
        if (TransformationUtilities.IsBindingOrAssignmentPattern(target))
        {
            return BindingOrAssignmentPatternAssignsToName(target, escapedName);
        }
        else if (NodeTests.IsIdentifier(target))
        {
            return ((Identifier)target).EscapedText == escapedName;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:208-216 — <c>bindingOrAssignmentPatternAssignsToName</c>.</summary>
    private static bool BindingOrAssignmentPatternAssignsToName(Node pattern, string escapedName)
    {
        var elements = FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(pattern);
        foreach (var element in elements)
        {
            if (BindingOrAssignmentElementAssignsToName(element, escapedName))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:218-225 — <c>bindingOrAssignmentElementContainsNonLiteralComputedName</c>.</summary>
    private static bool BindingOrAssignmentElementContainsNonLiteralComputedName(Node element)
    {
        var propertyName = FactoryUtilities.TryGetPropertyNameOfBindingOrAssignmentElement(element);
        if (propertyName is not null && NodeTests.IsComputedPropertyName(propertyName) && !Utilities.IsLiteralExpression(((ComputedPropertyName)propertyName).Expression!))
        {
            return true;
        }
        var target = FactoryUtilities.GetTargetOfBindingOrAssignmentElement(element);
        return target is not null && TransformationUtilities.IsBindingOrAssignmentPattern(target) && BindingOrAssignmentPatternContainsNonLiteralComputedName(target);
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:227-229 — <c>bindingOrAssignmentPatternContainsNonLiteralComputedName</c>.</summary>
    /// <remarks>PORT-NOTE: TS writes this as <c>!!forEach(getElementsOfBindingOrAssignmentPattern(pattern),
    /// bindingOrAssignmentElementContainsNonLiteralComputedName)</c> — "does any element answer true" — which the port
    /// states as the loop the landed <c>Core</c> helpers would need an <c>IList&lt;T&gt;</c> for.</remarks>
    private static bool BindingOrAssignmentPatternContainsNonLiteralComputedName(Node pattern)
    {
        foreach (var element in FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(pattern))
        {
            if (BindingOrAssignmentElementContainsNonLiteralComputedName(element))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:244-326 — <c>flattenDestructuringBinding</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's first parameter is the union <c>VariableDeclaration | ParameterDeclaration</c>; the port takes
    /// <see cref="Node"/> and reads <c>name</c> through the single <c>updateVariableDeclaration</c> arm that needs it.
    /// The reference's record field <c>pendingExpressions</c> is undefined at every push (its local is cleared on the line
    /// above the push) and the port keeps the field and the read for the reference's own shape.
    /// </remarks>
    public static List<VariableDeclaration> FlattenDestructuringBinding(
        Node node,
        Func<Node, object?> visitor,
        ITransformationUtilitiesContext context,
        FlattenLevel level,
        Expression? rval = null,
        bool hoistTempVariables = false,
        bool? skipInitializer = null)
    {
        List<Expression>? pendingExpressions = null;
        var pendingDeclarations = new List<PendingDeclaration>();
        var declarations = new List<VariableDeclaration>();
        var flattenContext = new FlattenContext
        {
            Context = context,
            Level = level,
            DownlevelIteration = Js.IsTruthy(context.GetCompilerOptions().DownlevelIteration),
            HoistTempVariables = hoistTempVariables,
            EmitExpression = EmitExpression,
            EmitBindingOrAssignment = EmitBindingOrAssignment,
            CreateArrayBindingOrAssignmentPattern = elements => MakeArrayBindingPattern(context.Factory, elements),
            CreateObjectBindingOrAssignmentPattern = elements => MakeObjectBindingPattern(context.Factory, elements),
            CreateArrayBindingOrAssignmentElement = name => MakeBindingElement(context.Factory, name),
            Visitor = visitor,
        };

        if (NodeTests.IsVariableDeclaration(node))
        {
            var initializer = FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(node);
            if (
                initializer is not null && (NodeTests.IsIdentifier(initializer) && BindingOrAssignmentElementAssignsToName(node, ((Identifier)initializer).EscapedText!)
                    || BindingOrAssignmentElementContainsNonLiteralComputedName(node))
            )
            {
                // If the right-hand value of the assignment is also an assignment target then
                // we need to cache the right-hand value.
                initializer = EnsureIdentifier(
                    flattenContext,
                    Debug.CheckDefined(Utilities.VisitNode(initializer, flattenContext.Visitor, Utilities.IsExpression)),
                    /*reuseIdentifierExpressions*/ false,
                    initializer);
                node = context.Factory.UpdateVariableDeclaration(
                    (VariableDeclaration)node,
                    ((VariableDeclaration)node).Name,
                    /*exclamationToken*/ null,
                    /*type*/ null,
                    initializer);
            }
        }

        FlattenBindingOrAssignmentElement(flattenContext, node, rval, node, skipInitializer);
        if (pendingExpressions is not null)
        {
            var temp = context.Factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ false);
            if (hoistTempVariables)
            {
                var value = context.Factory.InlineExpressions(pendingExpressions);
                pendingExpressions = null;
                EmitBindingOrAssignment(temp, value, /*location*/ null, /*original*/ null);
            }
            else
            {
                context.HoistVariableDeclaration(temp);
                var pendingDeclaration = Core.Last(pendingDeclarations);
                pendingDeclaration.PendingExpressions = (List<Expression>?)Core.Append(
                    pendingDeclaration.PendingExpressions,
                    context.Factory.CreateAssignment(temp, pendingDeclaration.Value));
                Core.AddRange(pendingDeclaration.PendingExpressions, pendingExpressions);
                pendingDeclaration.Value = temp;
            }
        }
        foreach (var pending in pendingDeclarations)
        {
            var variable = context.Factory.CreateVariableDeclaration(
                pending.Name,
                /*exclamationToken*/ null,
                /*type*/ null,
                pending.PendingExpressions is not null
                    ? context.Factory.InlineExpressions((List<Expression>?)Core.Append(pending.PendingExpressions, pending.Value))
                    : pending.Value);
            variable.Original = pending.Original;
            Utilities.SetTextRange(variable, pending.Location);
            declarations.Add(variable);
        }
        return declarations;

        // ported from: src/compiler/transformers/destructuring.ts:323-325 — <c>emitExpression</c>.
        void EmitExpression(Expression assignedValue)
        {
            pendingExpressions = (List<Expression>?)Core.Append(pendingExpressions, assignedValue);
        }

        // ported from: src/compiler/transformers/destructuring.ts:315-321 — <c>emitBindingOrAssignment</c>.
        void EmitBindingOrAssignment(Node target, Expression assignedValue, ITextRange? assignedLocation, Node? original)
        {
            Debug.AssertNode(target, Utilities.IsBindingName);
            if (pendingExpressions is not null)
            {
                assignedValue = context.Factory.InlineExpressions((List<Expression>?)Core.Append(pendingExpressions, assignedValue))!;
                pendingExpressions = null;
            }
            pendingDeclarations.Add(new PendingDeclaration
            {
                PendingExpressions = pendingExpressions,
                Name = target,
                Value = assignedValue,
                Location = assignedLocation,
                Original = original,
            });
        }
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:338-375 — <c>flattenBindingOrAssignmentElement</c>.</summary>
    private static void FlattenBindingOrAssignmentElement(
        FlattenContext flattenContext,
        Node element,
        Expression? value,
        ITextRange location,
        bool? skipInitializer = null)
    {
        var bindingTarget = FactoryUtilities.GetTargetOfBindingOrAssignmentElement(element)!;
        if (!Js.IsTruthy(skipInitializer))
        {
            var initializer = Utilities.VisitNode(FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(element), flattenContext.Visitor, Utilities.IsExpression);
            if (initializer is not null)
            {
                // Combine value and initializer
                if (value is not null)
                {
                    value = CreateDefaultValueCheck(flattenContext, value, initializer, location);
                    // If 'value' is not a simple expression, it could contain side-effecting code that should evaluate
                    // before an object or array binding pattern.
                    if (!TransformationUtilities.IsSimpleInlineableExpression(initializer) && TransformationUtilities.IsBindingOrAssignmentPattern(bindingTarget))
                    {
                        value = EnsureIdentifier(flattenContext, value, /*reuseIdentifierExpressions*/ true, location);
                    }
                }
                else
                {
                    value = initializer;
                }
            }
            else if (value is null)
            {
                // Use 'void 0' in absence of value and initializer
                value = flattenContext.Context.Factory.CreateVoidZero();
            }
        }
        if (TransformationUtilities.IsObjectBindingOrAssignmentPattern(bindingTarget))
        {
            FlattenObjectBindingOrAssignmentPattern(flattenContext, element, bindingTarget, value!, location);
        }
        else if (TransformationUtilities.IsArrayBindingOrAssignmentPattern(bindingTarget))
        {
            FlattenArrayBindingOrAssignmentPattern(flattenContext, element, bindingTarget, value!, location);
        }
        else
        {
            flattenContext.EmitBindingOrAssignment(bindingTarget, value!, location, element);
        }
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:386-435 — <c>flattenObjectBindingOrAssignmentPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference passes its element array straight to <c>createRestHelper</c>, whose ported slot is
    /// <c>List&lt;object?&gt;</c> (the helper casts each entry back to a node, Parser/EmitHelpers.cs:1141) while
    /// <c>GetElementsOfBindingOrAssignmentPattern</c> answers <c>IReadOnlyList&lt;Node&gt;</c>; the rest arm therefore
    /// hands it a copy of the same references. The copy exists only on that arm, and the helper reads the list without
    /// keeping it.
    /// </remarks>
    private static void FlattenObjectBindingOrAssignmentPattern(
        FlattenContext flattenContext,
        Node parent,
        Node pattern,
        Expression value,
        ITextRange location)
    {
        var elements = FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(pattern);
        var numElements = elements.Count;
        if (numElements != 1)
        {
            // For anything other than a single-element destructuring we need to generate a temporary
            // to ensure value is evaluated exactly once. Additionally, if we have zero elements
            // we need to emit *something* to ensure that in case a 'var' keyword was already emitted,
            // so in that case, we'll intentionally create that temporary.
            var reuseIdentifierExpressions = !Utilities.IsDeclarationBindingElement(parent) || numElements != 0;
            value = EnsureIdentifier(flattenContext, value, reuseIdentifierExpressions, location);
        }
        List<Node>? bindingElements = null;
        List<Expression>? computedTempVariables = null;
        for (var i = 0; i < numElements; i++)
        {
            var element = elements[i];
            if (FactoryUtilities.GetRestIndicatorOfBindingOrAssignmentElement(element) is null)
            {
                var propertyName = FactoryUtilities.GetPropertyNameOfBindingOrAssignmentElement(element)!;
                if (
                    flattenContext.Level >= FlattenLevel.ObjectRest
                    && (element.TransformFlags & (TransformFlags.ContainsRestOrSpread | TransformFlags.ContainsObjectRestOrSpread)) == 0
                    && (FactoryUtilities.GetTargetOfBindingOrAssignmentElement(element)!.TransformFlags & (TransformFlags.ContainsRestOrSpread | TransformFlags.ContainsObjectRestOrSpread)) == 0
                    && !NodeTests.IsComputedPropertyName(propertyName)
                )
                {
                    bindingElements = (List<Node>?)Core.Append(bindingElements, Utilities.VisitNode(element, flattenContext.Visitor, TransformationUtilities.IsBindingOrAssignmentElement)!);
                }
                else
                {
                    if (bindingElements is not null)
                    {
                        flattenContext.EmitBindingOrAssignment(flattenContext.CreateObjectBindingOrAssignmentPattern(bindingElements), value, location, pattern);
                        bindingElements = null;
                    }
                    var rhsValue = CreateDestructuringPropertyAccess(flattenContext, value, propertyName);
                    if (NodeTests.IsComputedPropertyName(propertyName))
                    {
                        computedTempVariables = (List<Expression>?)Core.Append(computedTempVariables, ((ElementAccessExpression)rhsValue).ArgumentExpression!);
                    }
                    FlattenBindingOrAssignmentElement(flattenContext, element, rhsValue, /*location*/ element);
                }
            }
            else if (i == numElements - 1)
            {
                if (bindingElements is not null)
                {
                    flattenContext.EmitBindingOrAssignment(flattenContext.CreateObjectBindingOrAssignmentPattern(bindingElements), value, location, pattern);
                    bindingElements = null;
                }
                var restElements = new List<object?>(elements.Count);
                foreach (var restElement in elements)
                {
                    restElements.Add(restElement);
                }
                var rhsValue = flattenContext.Context.GetEmitHelperFactory().CreateRestHelper(value, restElements, computedTempVariables, pattern);
                FlattenBindingOrAssignmentElement(flattenContext, element, rhsValue, element);
            }
        }
        if (bindingElements is not null)
        {
            flattenContext.EmitBindingOrAssignment(flattenContext.CreateObjectBindingOrAssignmentPattern(bindingElements), value, location, pattern);
        }
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:446-520 — <c>flattenArrayBindingOrAssignmentPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>every(elements, isOmittedExpression)</c> is <c>Enumerable.All</c> over the
    /// <c>IReadOnlyList&lt;Node&gt;</c> the pattern accessor answers (<c>Core.Every</c> takes an <c>IList&lt;T&gt;</c>);
    /// the two conditions around it keep the reference's precedence.
    /// </remarks>
    private static void FlattenArrayBindingOrAssignmentPattern(
        FlattenContext flattenContext,
        Node parent,
        Node pattern,
        Expression value,
        ITextRange location)
    {
        var elements = FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(pattern);
        var numElements = elements.Count;
        if (flattenContext.Level < FlattenLevel.ObjectRest && flattenContext.DownlevelIteration)
        {
            // Read the elements of the iterable into an array
            value = EnsureIdentifier(
                flattenContext,
                Utilities.SetTextRange(
                    flattenContext.Context.GetEmitHelperFactory().CreateReadHelper(
                        value,
                        numElements > 0 && FactoryUtilities.GetRestIndicatorOfBindingOrAssignmentElement(elements[numElements - 1]) is not null
                            ? null
                            : numElements),
                    location),
                /*reuseIdentifierExpressions*/ false,
                location);
        }
        else if (
            numElements != 1 && (flattenContext.Level < FlattenLevel.ObjectRest || numElements == 0)
            || elements.All(NodeTests.IsOmittedExpression)
        )
        {
            // For anything other than a single-element destructuring we need to generate a temporary
            // to ensure value is evaluated exactly once. Additionally, if we have zero elements
            // we need to emit *something* to ensure that in case a 'var' keyword was already emitted,
            // so in that case, we'll intentionally create that temporary.
            // Or all the elements of the binding pattern are omitted expression such as "var [,] = [1,2]",
            // then we will create temporary variable.
            var reuseIdentifierExpressions = !Utilities.IsDeclarationBindingElement(parent) || numElements != 0;
            value = EnsureIdentifier(flattenContext, value, reuseIdentifierExpressions, location);
        }
        List<Node>? bindingElements = null;
        List<(Identifier Id, Node Element)>? restContainingElements = null;
        for (var i = 0; i < numElements; i++)
        {
            var element = elements[i];
            if (flattenContext.Level >= FlattenLevel.ObjectRest)
            {
                // If an array pattern contains an ObjectRest, we must cache the result so that we
                // can perform the ObjectRest destructuring in a different declaration
                if ((element.TransformFlags & TransformFlags.ContainsObjectRestOrSpread) != 0
                    || Js.IsTruthy(flattenContext.HasTransformedPriorElement) && !IsSimpleBindingOrAssignmentElement(element))
                {
                    flattenContext.HasTransformedPriorElement = true;
                    var temp = flattenContext.Context.Factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ false);
                    if (flattenContext.HoistTempVariables)
                    {
                        flattenContext.Context.HoistVariableDeclaration(temp);
                    }

                    // PORT-NOTE: the reference's `append` for this one slot is stated as the two steps it performs
                    // (a fresh list when the accumulator is absent, a push otherwise), because the ported
                    // <c>Core.Append</c> is constrained to reference types and this pair is a value tuple.
                    restContainingElements ??= [];
                    restContainingElements.Add((temp, element));
                    bindingElements = (List<Node>?)Core.Append(bindingElements, flattenContext.CreateArrayBindingOrAssignmentElement(temp));
                }
                else
                {
                    bindingElements = (List<Node>?)Core.Append(bindingElements, element);
                }
            }
            else if (NodeTests.IsOmittedExpression(element))
            {
                continue;
            }
            else if (FactoryUtilities.GetRestIndicatorOfBindingOrAssignmentElement(element) is null)
            {
                var rhsValue = flattenContext.Context.Factory.CreateElementAccessExpression(value, i);
                FlattenBindingOrAssignmentElement(flattenContext, element, rhsValue, /*location*/ element);
            }
            else if (i == numElements - 1)
            {
                var rhsValue = flattenContext.Context.Factory.CreateArraySliceCall(value, i);
                FlattenBindingOrAssignmentElement(flattenContext, element, rhsValue, /*location*/ element);
            }
        }
        if (bindingElements is not null)
        {
            flattenContext.EmitBindingOrAssignment(flattenContext.CreateArrayBindingOrAssignmentPattern(bindingElements), value, location, pattern);
        }
        if (restContainingElements is not null)
        {
            foreach (var (id, element) in restContainingElements)
            {
                FlattenBindingOrAssignmentElement(flattenContext, element, id, element);
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:522-531 — <c>isSimpleBindingOrAssignmentElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>every(getElementsOfBindingOrAssignmentPattern(target),
    /// isSimpleBindingOrAssignmentElement)</c> is the loop below (see the class remarks on
    /// <c>IReadOnlyList&lt;Node&gt;</c>).</remarks>
    private static bool IsSimpleBindingOrAssignmentElement(Node element)
    {
        var target = FactoryUtilities.GetTargetOfBindingOrAssignmentElement(element);
        if (target is null || NodeTests.IsOmittedExpression(target)) return true;
        var propertyName = FactoryUtilities.TryGetPropertyNameOfBindingOrAssignmentElement(element);
        if (propertyName is not null && !Utilities.IsPropertyNameLiteral(propertyName)) return false;
        var initializer = FactoryUtilities.GetInitializerOfBindingOrAssignmentElement(element);
        if (initializer is not null && !TransformationUtilities.IsSimpleInlineableExpression(initializer)) return false;
        if (TransformationUtilities.IsBindingOrAssignmentPattern(target))
        {
            foreach (var targetElement in FactoryUtilities.GetElementsOfBindingOrAssignmentPattern(target))
            {
                if (!IsSimpleBindingOrAssignmentElement(targetElement)) return false;
            }
            return true;
        }
        return NodeTests.IsIdentifier(target);
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:541-544 — <c>createDefaultValueCheck</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>createConditionalExpression</c> passes two holes for the two punctuation
    /// tokens; the port passes <c>null</c> for both, which is the model's own spelling of them.</remarks>
    private static Expression CreateDefaultValueCheck(FlattenContext flattenContext, Expression value, Expression defaultValue, ITextRange location)
    {
        value = EnsureIdentifier(flattenContext, value, /*reuseIdentifierExpressions*/ true, location);
        return flattenContext.Context.Factory.CreateConditionalExpression(
            flattenContext.Context.Factory.CreateTypeCheck(value, "undefined"),
            /*questionToken*/ null,
            defaultValue,
            /*colonToken*/ null,
            value);
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:556-570 — <c>createDestructuringPropertyAccess</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>propertyName</c> is the <c>PropertyName</c> union, which the port takes as
    /// <see cref="Node"/> (the factory's own property-name helpers do the same, Parser/FactoryUtilities.cs:754-860).</remarks>
    private static LeftHandSideExpression CreateDestructuringPropertyAccess(FlattenContext flattenContext, Expression value, Node propertyName)
    {
        var factory = flattenContext.Context.Factory;
        if (NodeTests.IsComputedPropertyName(propertyName))
        {
            var argumentExpression = EnsureIdentifier(
                flattenContext,
                Debug.CheckDefined(Utilities.VisitNode(((ComputedPropertyName)propertyName).Expression, flattenContext.Visitor, Utilities.IsExpression)),
                /*reuseIdentifierExpressions*/ false,
                /*location*/ propertyName);
            return flattenContext.Context.Factory.CreateElementAccessExpression(value, argumentExpression);
        }
        else if (Utilities.IsStringOrNumericLiteralLike(propertyName) || NodeTests.IsBigIntLiteral(propertyName))
        {
            var argumentExpression = factory.CloneNode(propertyName);
            return flattenContext.Context.Factory.CreateElementAccessExpression(value, argumentExpression);
        }
        else
        {
            var name = flattenContext.Context.Factory.CreateIdentifier(Utilities.IdText(propertyName), /*originalKeywordKind*/ null, /*hasExtendedUnicodeEscape*/ null);
            return flattenContext.Context.Factory.CreatePropertyAccessExpression(value, name);
        }
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:583-598 — <c>ensureIdentifier</c>.</summary>
    private static Identifier EnsureIdentifier(FlattenContext flattenContext, Expression value, bool reuseIdentifierExpressions, ITextRange location)
    {
        if (NodeTests.IsIdentifier(value) && reuseIdentifierExpressions)
        {
            return (Identifier)value;
        }
        else
        {
            var temp = flattenContext.Context.Factory.CreateTempVariable(/*recordTempVariable*/ null, /*reservedInNestedScopes*/ false);
            if (flattenContext.HoistTempVariables)
            {
                flattenContext.Context.HoistVariableDeclaration(temp);
                flattenContext.EmitExpression(Utilities.SetTextRange(flattenContext.Context.Factory.CreateAssignment(temp, value), location));
            }
            else
            {
                flattenContext.EmitBindingOrAssignment(temp, value, location, /*original*/ null);
            }
            return temp;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The element/pattern builders and the two <c>Debug.assertEachNode</c> wrappers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:600-603 — <c>makeArrayBindingPattern</c>.</summary>
    /// <remarks>PORT-NOTE: <c>Debug.assertEachNode</c> had no landed member (<c>Utilities.AssertEachNode</c> is private to
    /// that class), so <see cref="AssertEachNode"/> states it here for this file's four users — the batch report asks for
    /// one home.</remarks>
    private static Node MakeArrayBindingPattern(NodeFactory factory, List<Node> elements)
    {
        AssertEachNode(elements, Utilities.IsArrayBindingElement);
        return factory.CreateArrayBindingPattern(elements);
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:605-608 — <c>makeArrayAssignmentPattern</c>.</summary>
    private static Node MakeArrayAssignmentPattern(NodeFactory factory, List<Node> elements)
    {
        AssertEachNode(elements, TransformationUtilities.IsArrayBindingOrAssignmentElement);
        return factory.CreateArrayLiteralExpression(Core.Map(elements, (element, _) => factory.Converters.ConvertToArrayAssignmentElement(element)), /*multiLine*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:610-613 — <c>makeObjectBindingPattern</c>.</summary>
    /// <remarks>PORT-NOTE: the reference hands the asserted array straight to <c>createObjectBindingPattern</c>, whose ported
    /// parameter is <c>List&lt;BindingElement&gt;</c> where this walk accumulates <c>List&lt;Node&gt;</c>; the projection is
    /// the model's element type and allocates once per pattern.</remarks>
    private static Node MakeObjectBindingPattern(NodeFactory factory, List<Node> elements)
    {
        AssertEachNode(elements, NodeTests.IsBindingElement);
        return factory.CreateObjectBindingPattern(elements.Cast<BindingElement>().ToList());
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:615-618 — <c>makeObjectAssignmentPattern</c>.</summary>
    private static Node MakeObjectAssignmentPattern(NodeFactory factory, List<Node> elements)
    {
        AssertEachNode(elements, TransformationUtilities.IsObjectBindingOrAssignmentElement);
        return factory.CreateObjectLiteralExpression(Core.Map(elements, (element, _) => factory.Converters.ConvertToObjectAssignmentElement(element)), /*multiLine*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:620-622 — <c>makeBindingElement</c>.</summary>
    private static Node MakeBindingElement(NodeFactory factory, Identifier name)
        => factory.CreateBindingElement(/*dotDotDotToken*/ null, /*propertyName*/ null, name, /*initializer*/ null);

    /// <summary>ported from: src/compiler/transformers/destructuring.ts:624-626 — <c>makeAssignmentElement</c>.</summary>
    private static Node MakeAssignmentElement(Identifier name) => name;

    /// <summary>ported from: src/compiler/debug.ts:166-172 via the reference's <c>Debug.assertEachNode</c> calls — the
    /// member <c>Utilities.AssertEachNode</c> states privately for the visitor layer.</summary>
    /// <remarks>PORT-NOTE: this is the second copy of that body in the port (the first is
    /// <c>Utilities.AssertEachNode</c>, Utilities/Utilities.Visitor.cs:348, which is private); the batch report asks for
    /// one home. TS's <c>readonly T[]</c> is <c>IEnumerable&lt;Node&gt;</c> so the accumulated lists pass without a
    /// copy.</remarks>
    private static void AssertEachNode(IEnumerable<Node> nodes, Func<Node, bool> test)
    {
        foreach (var node in nodes)
        {
            Debug.AssertNode(node, test);
        }
    }
}

/// <summary>ported from: src/compiler/transformers/destructuring.ts:81-84 — <c>FlattenLevel</c>.</summary>
/// <remarks>PORT-NOTE: the reference declares this const enum in <c>transformers/destructuring.ts</c>; the port states it
/// beside <see cref="Destructuring"/> with the same members in the same order, since
/// <c>flattenContext.level &gt;= FlattenLevel.ObjectRest</c> is a value comparison.</remarks>
internal enum FlattenLevel
{
    /// <summary><c>FlattenLevel.All</c>.</summary>
    All,

    /// <summary><c>FlattenLevel.ObjectRest</c>.</summary>
    ObjectRest,
}
