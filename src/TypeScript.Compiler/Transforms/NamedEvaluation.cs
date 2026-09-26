#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/namedEvaluation.ts:1-489 — the whole namedEvaluation module, plus the two
// utilities.ts members it is stated with (`isAnonymousFunctionDefinition` and `isNamedEvaluation`).
/// <summary>
/// ported from: src/compiler/transformers/namedEvaluation.ts:56-497 — <c>getAssignedNameOfIdentifier</c> &hellip;
/// <c>transformNamedEvaluation</c>: the shallow rewrites that give an anonymous class/function expression the name its
/// syntactic position implies (a variable declaration's name, a property key, <c>"default"</c> for an export, and so
/// on), the <c>static { __setFunctionName(this, "C") }</c> helper block a class expression needs for the same purpose,
/// and the two guards that recognise them.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports these as module-level functions of
/// <c>transformers/namedEvaluation.ts</c>, so they land on the class named after that module and are called qualified
/// (<c>NamedEvaluation.TransformNamedEvaluation</c>). The members the reference does <em>not</em> export
/// (<c>getAssignedNameOfIdentifier</c>, <c>getAssignedNameOfPropertyName</c>,
/// <c>createClassNamedEvaluationHelperBlock</c>, <c>finishTransformNamedEvaluation</c> and the eight
/// <c>transformNamedEvaluationOf&hellip;</c> arms) are private here.
/// </para>
/// <para>
/// PORT-NOTE (<c>TransformationContext</c>): the reference's functions take the full <c>TransformationContext</c>,
/// which no port type implements (Transformer.cs's header records the model gap); they take
/// <see cref="ITransformerContext"/> instead — the same slice every other M7-B transformer factory takes, and the one
/// whose <c>Factory</c>, <c>GetEmitHelperFactory</c> and <c>HoistVariableDeclaration</c> are the three members these
/// bodies read.
/// </para>
/// <para>
/// PORT-NOTE (<c>AnonymousFunctionDefinition</c>): the reference's type is the intersection
/// <c>ClassExpression &amp; { name?: undefined } | FunctionExpression &amp; { name?: undefined } | ArrowFunction</c> and
/// its only readers are the two guards here, which test the expression's kind and (for a class expression) whether it
/// has a name or an assigned-name helper block. An intersection has no C# spelling (§12.5), so the port states the
/// guards over <see cref="Expression"/> — the same treatment every union-typed predicate gets — and the narrowing the
/// reference's guard licensed is the kind test each caller already performs.
/// </para>
/// <para>
/// PORT-NOTE (<c>WrappedExpression&lt;T&gt;</c>): the reference's <c>WrappedExpression&lt;AnonymousFunctionDefinition&gt;</c>
/// is <c>T | ParenthesizedExpression | PartiallyEmittedExpression |&hellip;</c>; the port's visitor face is
/// <see cref="Node"/>/<see cref="Expression"/>, and <c>finishTransformNamedEvaluation</c> re-wraps through
/// <see cref="NodeFactory.RestoreOuterExpressions"/> exactly as the reference does.
/// </para>
/// <para>
/// PORT-NOTE (the two <c>utilities.ts</c> members): <c>isAnonymousFunctionDefinition</c> (utilities.ts:5485, module
/// private there) and <c>isNamedEvaluation</c> (utilities.ts:5564, with its already-ported prerequisite
/// <c>isNamedEvaluationSource</c>, Utilities.Checker.cs) are stated here because this module is their only reader in
/// the transform layer; <c>isEmptyStringLiteral</c> (utilities.ts:3604) joins them for the same reason — its home
/// module has not landed. Each is reported for the batch that owns <c>Utilities</c>.
/// </para>
/// </remarks>
internal static class NamedEvaluation
{
    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:52-65 — <c>getAssignedNameOfIdentifier</c>.</summary>
    /// <remarks>
    /// Gets a string literal to use as the assigned name of an anonymous class or function declaration.
    /// <para>
    /// PORT-NOTE: the reference reads <c>original.name</c> off the narrowed union; the port's <see cref="Node"/> face
    /// carries the same slot through <see cref="INamedDeclaration"/> (<c>name</c> is present on the class declaration
    /// and function declaration arms and a string literal is built from it, so the read is the reference's own).
    /// <c>skipOuterExpressions</c> is <see cref="FactoryUtilities.SkipOuterExpressions(Node, OuterExpressionKinds)"/>.
    /// </para>
    /// </remarks>
    private static StringLiteral GetAssignedNameOfIdentifier(NodeFactory factory, Identifier name, Expression expression)
    {
        var original = Utilities.GetOriginalNode(FactoryUtilities.SkipOuterExpressions(expression))!;
        if (
            (NodeTests.IsClassDeclaration(original) || NodeTests.IsFunctionDeclaration(original))
            && ((INamedDeclaration)original).Name is null
            && Utilities.HasSyntacticModifier(original, ModifierFlags.Default))
        {
            return factory.CreateStringLiteral("default", /*isSingleQuote*/ null);
        }
        return factory.CreateStringLiteralFromNode(name, /*isSingleQuote*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:67-91 — <c>getAssignedNameOfPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference answers the object literal <c>{ assignedName, name }</c>; the port states that pair as
    /// the tuple <c>(Expression AssignedName, Node Name)</c> (the same treatment <c>TypeScriptTransform</c> records for
    /// the reference's literal-typed returns). <c>factory.getGeneratedNameForNode(name)</c> is the
    /// <see cref="NodeFactory"/>'s own member.
    /// </remarks>
    private static (Expression AssignedName, Node Name) GetAssignedNameOfPropertyName(
        ITransformerContext context,
        Node name,
        string? assignedNameText)
    {
        var factory = context.Factory;
        if (assignedNameText is not null)
        {
            var assignedName = factory.CreateStringLiteral(assignedNameText, /*isSingleQuote*/ null);
            return (assignedName, name);
        }

        if (Utilities.IsPropertyNameLiteral(name) || NodeTests.IsPrivateIdentifier(name))
        {
            var assignedName = factory.CreateStringLiteralFromNode(name, /*isSingleQuote*/ null);
            return (assignedName, name);
        }

        var computedExpression = ((ComputedPropertyName)name).Expression!;
        if (Utilities.IsPropertyNameLiteral(computedExpression) && !NodeTests.IsIdentifier(computedExpression))
        {
            var assignedName = factory.CreateStringLiteralFromNode(computedExpression, /*isSingleQuote*/ null);
            return (assignedName, name);
        }

        var generatedName = factory.GetGeneratedNameForNode(name, /*flags*/ null);
        context.HoistVariableDeclaration(generatedName);

        var key = context.GetEmitHelperFactory().CreatePropKeyHelper(computedExpression);
        var assignment = factory.CreateAssignment(generatedName, key);
        var updatedName = factory.UpdateComputedPropertyName((ComputedPropertyName)name, assignment);
        return (generatedName, updatedName);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:94-119 — <c>createClassNamedEvaluationHelperBlock</c>.</summary>
    /// <remarks>
    /// Creates a class <c>static {}</c> block used to dynamically set the name of a class.
    /// <para>
    /// Produces:
    /// <code>
    /// static { __setFunctionName(this, "C"); }
    /// </code>
    /// </para>
    /// <para>
    /// PORT-NOTE (the <c>assignedName</c> marker): <c>getOrCreateEmitNode(block).assignedName = assignedName</c> marks
    /// the block as a NamedEvaluation helper and stashes the expression used to resolve the assigned name; the port
    /// reads the same slot through <see cref="EmitNodes.GetOrCreateEmitNode"/> and <see cref="IEmitNode.AssignedName"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>thisExpression</c>): TS defaults the parameter to <c>context.factory.createThis()</c>; a C# default
    /// value may not read another parameter, so the port defaults it to <c>null</c> and builds the same value at the
    /// head of the body — the identical treatment <c>ClassThis.CreateClassThisAssignmentBlock</c> records.
    /// </para>
    /// </remarks>
    private static ClassStaticBlockDeclaration CreateClassNamedEvaluationHelperBlock(
        ITransformerContext context,
        Expression assignedName,
        Expression? thisExpression = null)
    {
        var factory = context.Factory;
        var expression = context.GetEmitHelperFactory().CreateSetFunctionNameHelper(thisExpression ?? factory.CreateThis(), assignedName, /*prefix*/ null);
        var statement = factory.CreateExpressionStatement(expression);
        var body = factory.CreateBlock([statement], /*multiLine*/ false);
        var block = factory.CreateClassStaticBlockDeclaration(body);

        // We use `emitNode.assignedName` to indicate this is a NamedEvaluation helper block
        // and to stash the expression used to resolve the assigned name.
        EmitNodes.GetOrCreateEmitNode(block).AssignedName = assignedName;
        return block;
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:139-151 — <c>isClassNamedEvaluationHelperBlock</c>.</summary>
    /// <remarks>
    /// Gets whether a node is a <c>static {}</c> block containing only a single call to the <c>__setFunctionName</c>
    /// helper where that call's second argument is the value stored in the <c>assignedName</c> property of the block's
    /// <c>EmitNode</c>.
    /// <para>
    /// PORT-NOTE: the reference's <c>node is ClassNamedEvaluationHelperBlock</c> is a type guard whose answer refines
    /// the block's body/statement/expression chain; the port answers <c>bool</c> (mapping rule (d)) and reads the same
    /// chain through the model's class members, which is what the guard's refinement licensed. The identity test
    /// <c>arguments[1] === node.emitNode?.assignedName</c> is <see cref="ReferenceEquals"/>.
    /// </para>
    /// </remarks>
    public static bool IsClassNamedEvaluationHelperBlock(Node node)
    {
        if (!NodeTests.IsClassStaticBlockDeclaration(node)
            || ((ClassStaticBlockDeclaration)node).Body!.Statements!.Count != 1)
        {
            return false;
        }

        var statement = (Node)((ClassStaticBlockDeclaration)node).Body!.Statements![0];
        return NodeTests.IsExpressionStatement(statement)
            && EmitHelpers.IsCallToHelper(((ExpressionStatement)statement).Expression!, "___setFunctionName")
            && ((CallExpression)((ExpressionStatement)statement).Expression!).Arguments!.Count >= 2
            && ReferenceEquals(((CallExpression)((ExpressionStatement)statement).Expression!).Arguments![1], node.EmitNode?.AssignedName);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:156-159 — <c>classHasExplicitlyAssignedName</c>.</summary>
    /// <remarks>
    /// Gets whether a <c>ClassLikeDeclaration</c> has a <c>static {}</c> block containing only a single call to the
    /// <c>__setFunctionName</c> helper.
    /// <para>
    /// PORT-NOTE: the reference's <c>ClassLikeDeclaration</c> is the class/class-expression union, which the model
    /// carries as <see cref="IClassLikeDeclarationBase"/>; <c>some</c> is <see cref="Core.Some{T}(IList{T}?, Func{T, bool}?)"/>
    /// and its element is the model's <c>IClassElement</c> face, so the guard receives the <see cref="Node"/> cast.
    /// </para>
    /// </remarks>
    public static bool ClassHasExplicitlyAssignedName(Node node)
        => node.EmitNode?.AssignedName is not null
            && Core.Some(((IClassLikeDeclarationBase)node).Members, member => IsClassNamedEvaluationHelperBlock((Node)member));

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:165-168 — <c>classHasDeclaredOrExplicitlyAssignedName</c>.</summary>
    /// <remarks>
    /// Gets whether a <c>ClassLikeDeclaration</c> has a declared name or contains a <c>static {}</c> block containing
    /// only a single call to the <c>__setFunctionName</c> helper.
    /// </remarks>
    public static bool ClassHasDeclaredOrExplicitlyAssignedName(Node node)
        => ((IClassLikeDeclarationBase)node).Name is not null || ClassHasExplicitlyAssignedName(node);

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:173-231 — <c>injectClassNamedEvaluationHelperBlockIfMissing</c>.</summary>
    /// <remarks>
    /// Injects a class <c>static {}</c> block used to dynamically set the name of a class, if one does not already
    /// exist.
    /// <para>
    /// PORT-NOTE (the generic shape): TS declares two overloads whose relationship is an <c>Extract</c> — the answer is
    /// the same kind as the argument. C# states that as one member generic in the class-like node type, which is the
    /// same constraint the reference's <c>T extends ClassLikeDeclaration</c> states.
    /// </para>
    /// <para>
    /// PORT-NOTE (the constraint): the reference's <c>T extends ClassLikeDeclaration</c> is the class/class-expression
    /// union, which the port's callers hold as <see cref="IClassLikeDeclarationBase"/> as often as they hold the
    /// concrete node; the constraint is therefore the interface, and the two <c>update*</c> calls narrow through the
    /// kind test the reference's overloads resolved statically.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>insertionIndex</c>): the reference's <c>findIndex(&hellip;) + 1</c> is
    /// <see cref="Core.FindIndex{T}(IList{T}?, Func{T, int, bool}, int)"/>'s own <c>-1</c>-or-index answer plus one, so
    /// a class with no <c>_classThis</c> block inserts at the head — the reference's behaviour.
    /// </para>
    /// </remarks>
    public static T InjectClassNamedEvaluationHelperBlockIfMissing<T>(
        ITransformerContext context,
        T node,
        Expression assignedName,
        Expression? thisExpression = null)
        where T : IClassLikeDeclarationBase
    {
        // NOTE: If the class has a `_classThis` assignment block, this helper will be injected after that block.
        if (ClassHasExplicitlyAssignedName((Node)(object)node))
        {
            return node;
        }

        var factory = context.Factory;
        var namedEvaluationBlock = CreateClassNamedEvaluationHelperBlock(context, assignedName, thisExpression);
        if (node.Name is not null)
        {
            EmitNodes.SetSourceMapRange(
                (Node)namedEvaluationBlock.Body!.Statements![0],
                TypeScriptTransformSupport.SourceMapRangeOf(node.Name));
        }

        var members = node.Members!;
        var insertionIndex = Core.FindIndex(members, (m, _) => ClassThis.IsClassThisAssignmentBlock((Node)m)) + 1;
        var newMembersArray = new List<IClassElement>(members.Take(insertionIndex));
        newMembersArray.Add(namedEvaluationBlock);
        newMembersArray.AddRange(members.Skip(insertionIndex));

        var newMembers = factory.CreateNodeArray(newMembersArray, /*hasTrailingComma*/ null);
        Utilities.SetTextRange(newMembers, members);

        var classNode = (Node)(object)node;
        Node updated = NodeTests.IsClassDeclaration(classNode)
            ? factory.UpdateClassDeclaration(
                (ClassDeclaration)classNode,
                ((ClassDeclaration)classNode).Modifiers,
                ((ClassDeclaration)classNode).Name,
                ((ClassDeclaration)classNode).TypeParameters,
                ((ClassDeclaration)classNode).HeritageClauses,
                newMembers)
            : factory.UpdateClassExpression(
                (ClassExpression)classNode,
                ((ClassExpression)classNode).Modifiers,
                ((ClassExpression)classNode).Name,
                ((ClassExpression)classNode).TypeParameters,
                ((ClassExpression)classNode).HeritageClauses,
                newMembers);

        EmitNodes.GetOrCreateEmitNode(updated).AssignedName = assignedName;
        return (T)(object)updated;
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:236-254 — <c>finishTransformNamedEvaluation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>cast(injectClassNamedEvaluationHelperBlockIfMissing(&hellip;), isClassExpression)</c>
    /// is the overload resolution the port's generic member already performs (<c>T</c> is the class expression here);
    /// the callback the reference passes to <c>restoreOuterExpressions</c> is the factory's own default.
    /// </remarks>
    private static Expression FinishTransformNamedEvaluation(
        ITransformerContext context,
        Expression expression,
        Expression assignedName,
        bool? ignoreEmptyStringLiteral)
    {
        if (Js.IsTruthy(ignoreEmptyStringLiteral) && NodeTests.IsStringLiteral(assignedName) && IsEmptyStringLiteral((StringLiteral)assignedName))
        {
            return expression;
        }

        var factory = context.Factory;
        var innerExpression = (Expression)FactoryUtilities.SkipOuterExpressions(expression);

        var updatedExpression = NodeTests.IsClassExpression(innerExpression)
            ? (Expression)InjectClassNamedEvaluationHelperBlockIfMissing(context, (ClassExpression)innerExpression, assignedName)
            : context.GetEmitHelperFactory().CreateSetFunctionNameHelper(innerExpression, assignedName, /*prefix*/ null);

        return factory.RestoreOuterExpressions(expression, updatedExpression, /*kinds*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:256-274 — <c>transformNamedEvaluationOfPropertyAssignment</c>.</summary>
    private static Node TransformNamedEvaluationOfPropertyAssignment(
        ITransformerContext context,
        PropertyAssignment node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var (assignedName, name) = GetAssignedNameOfPropertyName(context, node.Name!, assignedNameText);
        var initializer = FinishTransformNamedEvaluation(context, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdatePropertyAssignment(
            node,
            name,
            initializer);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:274-294 — <c>transformNamedEvaluationOfShorthandAssignmentProperty</c>.</summary>
    private static Node TransformNamedEvaluationOfShorthandAssignmentProperty(
        ITransformerContext context,
        ShorthandPropertyAssignment node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var assignedName = assignedNameText is not null
            ? factory.CreateStringLiteral(assignedNameText, /*isSingleQuote*/ null)
            : GetAssignedNameOfIdentifier(factory, node.Name!, node.ObjectAssignmentInitializer!);
        var objectAssignmentInitializer = FinishTransformNamedEvaluation(context, node.ObjectAssignmentInitializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateShorthandPropertyAssignment(
            node,
            node.Name,
            objectAssignmentInitializer);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:294-322 — <c>transformNamedEvaluationOfVariableDeclaration</c>.</summary>
    private static Node TransformNamedEvaluationOfVariableDeclaration(
        ITransformerContext context,
        VariableDeclaration node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var assignedName = assignedNameText is not null
            ? factory.CreateStringLiteral(assignedNameText, /*isSingleQuote*/ null)
            : GetAssignedNameOfIdentifier(factory, (Identifier)node.Name!, node.Initializer!);
        var initializer = FinishTransformNamedEvaluation(context, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateVariableDeclaration(
            node,
            node.Name,
            node.ExclamationToken,
            node.Type,
            initializer);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:322-354 — <c>transformNamedEvaluationOfParameterDeclaration</c>.</summary>
    private static Node TransformNamedEvaluationOfParameterDeclaration(
        ITransformerContext context,
        ParameterDeclaration node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var assignedName = assignedNameText is not null
            ? factory.CreateStringLiteral(assignedNameText, /*isSingleQuote*/ null)
            : GetAssignedNameOfIdentifier(factory, (Identifier)node.Name!, node.Initializer!);
        var initializer = FinishTransformNamedEvaluation(context, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateParameterDeclaration(
            node,
            node.Modifiers,
            node.DotDotDotToken,
            node.Name,
            node.QuestionToken,
            node.Type,
            initializer);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:354-384 — <c>transformNamedEvaluationOfBindingElement</c>.</summary>
    private static Node TransformNamedEvaluationOfBindingElement(
        ITransformerContext context,
        BindingElement node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var assignedName = assignedNameText is not null
            ? factory.CreateStringLiteral(assignedNameText, /*isSingleQuote*/ null)
            : GetAssignedNameOfIdentifier(factory, (Identifier)node.Name!, node.Initializer!);
        var initializer = FinishTransformNamedEvaluation(context, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateBindingElement(
            node,
            node.DotDotDotToken,
            node.PropertyName,
            node.Name,
            initializer);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:384-405 — <c>transformNamedEvaluationOfPropertyDeclaration</c>.</summary>
    private static Node TransformNamedEvaluationOfPropertyDeclaration(
        ITransformerContext context,
        PropertyDeclaration node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var (assignedName, name) = GetAssignedNameOfPropertyName(context, node.Name!, assignedNameText);
        var initializer = FinishTransformNamedEvaluation(context, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdatePropertyDeclaration(
            node,
            node.Modifiers,
            name,
            node.QuestionToken ?? node.ExclamationToken,
            node.Type,
            initializer);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:405-444 — <c>transformNamedEvaluationOfAssignmentExpression</c>.</summary>
    private static Node TransformNamedEvaluationOfAssignmentExpression(
        ITransformerContext context,
        BinaryExpression node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var assignedName = assignedNameText is not null
            ? factory.CreateStringLiteral(assignedNameText, /*isSingleQuote*/ null)
            : GetAssignedNameOfIdentifier(factory, (Identifier)node.Left!, node.Right!);
        var right = FinishTransformNamedEvaluation(context, node.Right!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateBinaryExpression(
            node,
            node.Left,
            node.OperatorToken,
            right);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:444-469 — <c>transformNamedEvaluationOfExportAssignment</c>.</summary>
    private static Node TransformNamedEvaluationOfExportAssignment(
        ITransformerContext context,
        ExportAssignment node,
        bool? ignoreEmptyStringLiteral,
        string? assignedNameText)
    {
        var factory = context.Factory;
        var assignedName = assignedNameText is not null
            ? factory.CreateStringLiteral(assignedNameText, /*isSingleQuote*/ null)
            : factory.CreateStringLiteral(Js.IsTruthy(node.IsExportEquals) ? "" : "default", /*isSingleQuote*/ null);
        var expression = FinishTransformNamedEvaluation(context, node.Expression!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateExportAssignment(
            node,
            node.Modifiers,
            expression);
    }

    /// <summary>ported from: src/compiler/transformers/namedEvaluation.ts:469-489 — <c>transformNamedEvaluation</c>.</summary>
    /// <remarks>
    /// Performs a shallow transformation of a <c>NamedEvaluation</c> node, such that a valid name will be assigned.
    /// <para>
    /// PORT-NOTE: TS declares two overloads whose relationship is an <c>Extract</c> — the answer keeps the argument's
    /// kind and its <c>operatorToken</c>/<c>name</c>; the port states one member answering <see cref="Node"/>, which is
    /// the union the reference's <c>NamedEvaluation</c> refines to (every arm returns the node it was given, updated).
    /// </para>
    /// </remarks>
    public static Node TransformNamedEvaluation(
        ITransformerContext context,
        Node node,
        bool? ignoreEmptyStringLiteral = null,
        string? assignedName = null)
        => node.Kind switch
        {
            SyntaxKind.PropertyAssignment => TransformNamedEvaluationOfPropertyAssignment(context, (PropertyAssignment)node, ignoreEmptyStringLiteral, assignedName),
            SyntaxKind.ShorthandPropertyAssignment => TransformNamedEvaluationOfShorthandAssignmentProperty(context, (ShorthandPropertyAssignment)node, ignoreEmptyStringLiteral, assignedName),
            SyntaxKind.VariableDeclaration => TransformNamedEvaluationOfVariableDeclaration(context, (VariableDeclaration)node, ignoreEmptyStringLiteral, assignedName),
            SyntaxKind.Parameter => TransformNamedEvaluationOfParameterDeclaration(context, (ParameterDeclaration)node, ignoreEmptyStringLiteral, assignedName),
            SyntaxKind.BindingElement => TransformNamedEvaluationOfBindingElement(context, (BindingElement)node, ignoreEmptyStringLiteral, assignedName),
            SyntaxKind.PropertyDeclaration => TransformNamedEvaluationOfPropertyDeclaration(context, (PropertyDeclaration)node, ignoreEmptyStringLiteral, assignedName),
            SyntaxKind.BinaryExpression => TransformNamedEvaluationOfAssignmentExpression(context, (BinaryExpression)node, ignoreEmptyStringLiteral, assignedName),
            SyntaxKind.ExportAssignment => TransformNamedEvaluationOfExportAssignment(context, (ExportAssignment)node, ignoreEmptyStringLiteral, assignedName),
            _ => node,
        };

    // ---------------------------------------------------------------------------------------------
    // utilities.ts — the three members this slice is the first reader of
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:5485-5505 — <c>isAnonymousFunctionDefinition</c>.</summary>
    /// <remarks>
    /// Indicates whether an expression is an anonymous function definition.
    /// <para>
    /// PORT-NOTE (home): the reference declares this module-private in <c>utilities.ts</c> and its only readers are
    /// <c>isNamedEvaluation</c> (below) and the two transform modules <c>namedEvaluation.ts</c>/<c>esDecorators.ts</c>;
    /// the port's <c>Utilities</c> home has not landed the member, so it is stated here for this slice's readers and is
    /// reported for the batch that owns that file.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>cb</c>): the reference's callback takes the <c>AnonymousFunctionDefinition</c> union; the port's
    /// callback takes the node class, which is the union's erased representation (see the class remarks) — every
    /// reference callback reads only members the node class carries.
    /// </para>
    /// </remarks>
    /// <seealso href="https://tc39.es/ecma262/#sec-isanonymousfunctiondefinition"/>
    public static bool IsAnonymousFunctionDefinition(Expression node, Func<Node, bool>? cb = null)
    {
        node = (Expression)FactoryUtilities.SkipOuterExpressions(node);
        switch (node.Kind)
        {
            case SyntaxKind.ClassExpression:
                if (ClassHasDeclaredOrExplicitlyAssignedName(node))
                {
                    return false;
                }
                break;
            case SyntaxKind.FunctionExpression:
                if (((FunctionExpression)node).Name is not null)
                {
                    return false;
                }
                break;
            case SyntaxKind.ArrowFunction:
                break;
            default:
                return false;
        }
        return cb is not null ? cb(node) : true;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5564-5581 — <c>isNamedEvaluation</c>.</summary>
    /// <remarks>
    /// Indicates whether a node is a potential source of an assigned name for a class, function, or arrow function.
    /// <para>
    /// PORT-NOTE (home and <c>cb</c>): as <see cref="IsAnonymousFunctionDefinition"/>; the reference's
    /// <c>isNamedEvaluationSource</c> prerequisite is the already-ported
    /// <see cref="Utilities.IsNamedEvaluationSource"/>.
    /// </para>
    /// </remarks>
    public static bool IsNamedEvaluation(Node node, Func<Node, bool>? cb = null)
    {
        if (!Utilities.IsNamedEvaluationSource(node)) return false;
        switch (node.Kind)
        {
            case SyntaxKind.PropertyAssignment:
                return IsAnonymousFunctionDefinition(((PropertyAssignment)node).Initializer!, cb);
            case SyntaxKind.ShorthandPropertyAssignment:
                return IsAnonymousFunctionDefinition(((ShorthandPropertyAssignment)node).ObjectAssignmentInitializer!, cb);
            case SyntaxKind.VariableDeclaration:
                return IsAnonymousFunctionDefinition(((VariableDeclaration)node).Initializer!, cb);
            case SyntaxKind.Parameter:
                return IsAnonymousFunctionDefinition(((ParameterDeclaration)node).Initializer!, cb);
            case SyntaxKind.BindingElement:
                return IsAnonymousFunctionDefinition(((BindingElement)node).Initializer!, cb);
            case SyntaxKind.PropertyDeclaration:
                return IsAnonymousFunctionDefinition(((PropertyDeclaration)node).Initializer!, cb);
            case SyntaxKind.BinaryExpression:
                return IsAnonymousFunctionDefinition(((BinaryExpression)node).Right!, cb);
            case SyntaxKind.ExportAssignment:
                return IsAnonymousFunctionDefinition(((ExportAssignment)node).Expression!, cb);
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:3604-3616 — <c>isEmptyStringLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): as <see cref="IsAnonymousFunctionDefinition"/> — the member's home module has not landed, so
    /// this slice's first reader states it and reports it.
    /// </remarks>
    public static bool IsEmptyStringLiteral(StringLiteral node)
    {
        if (node.TextSourceNode is not null)
        {
            switch (node.TextSourceNode.Kind)
            {
                case SyntaxKind.StringLiteral:
                    return IsEmptyStringLiteral((StringLiteral)node.TextSourceNode);
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                    return node.Text == "";
            }
            return false;
        }
        return node.Text == "";
    }
}
