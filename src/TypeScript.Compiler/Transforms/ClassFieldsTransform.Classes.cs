#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classFields.ts:1707-2432 — the class half of `transformClassFields`: the
// facts pass, the class declaration/expression rewrite, `transformClassMembers`, and the constructor synthesis and
// initializer hoisting.
/// <summary>ported from: src/compiler/transformers/classFields.ts:1707-2432 — the class declarations, expressions,
/// members and constructor of <c>transformClassFields</c>.</summary>
/// <remarks>
/// PORT-NOTE (the class-like union): the reference's <c>ClassDeclaration | ClassExpression</c> and its
/// <c>ClassLikeDeclaration</c> are the model's <see cref="IClassLikeDeclarationBase"/>; the members that need the
/// concrete node (<c>name</c>, the two <c>update*</c> calls, <c>emitNode</c>) are reached through the kind tests the
/// reference's overloads resolved statically. The constructor/body readers take <see cref="Node"/> because their only
/// class-like reads are <c>members</c> and the effective base type.
/// </remarks>
internal sealed partial class ClassFieldsTransformer
{
    /// <summary>ported from: src/compiler/transformers/classFields.ts:1707-1709 — <c>getPrivateInstanceMethodsAndAccessors</c>.</summary>
    private static List<IClassElement> GetPrivateInstanceMethodsAndAccessors(Node node)
    {
        var result = new List<IClassElement>();
        var members = ((IClassLikeDeclarationBase)node).Members;
        if (members is not null)
        {
            foreach (var member in members)
            {
                if (TransformationUtilities.IsNonStaticMethodOrAccessorWithPrivateName(member))
                {
                    result.Add(member);
                }
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1711-1777 — <c>getClassFacts</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>member.transformFlags &amp; TransformFlags.X</c> is truthiness of a number, so the port states
    /// the <c>!= 0</c> test; <c>containsInstancePrivateElements ||= x</c> is the same short-circuit on the port's field
    /// locals.
    /// </remarks>
    private ClassFieldsFacts GetClassFacts(Node node)
    {
        var facts = ClassFieldsFacts.None;
        var original = Utilities.GetOriginalNode(node)!;
        if (Utilities.IsClassLike(original) && Utilities.ClassOrConstructorParameterIsDecorated(_legacyDecorators, original))
        {
            facts |= ClassFieldsFacts.ClassWasDecorated;
        }
        if (_shouldTransformPrivateElementsOrClassStaticBlocks && (ClassThis.ClassHasClassThisAssignment((IClassLikeDeclarationBase)node) || NamedEvaluation.ClassHasExplicitlyAssignedName(node)))
        {
            facts |= ClassFieldsFacts.NeedsClassConstructorReference;
        }
        var containsPublicInstanceFields = false;
        var containsInitializedPublicInstanceFields = false;
        var containsInstancePrivateElements = false;
        var containsInstanceAutoAccessors = false;
        foreach (var member in ((IClassLikeDeclarationBase)node).Members!)
        {
            var memberNode = (Node)member;
            if (Utilities.IsStatic(memberNode))
            {
                if (
                    member.Name is not null
                    && (NodeTests.IsPrivateIdentifier(member.Name!) || Utilities.IsAutoAccessorPropertyDeclaration(memberNode))
                    && _shouldTransformPrivateElementsOrClassStaticBlocks)
                {
                    facts |= ClassFieldsFacts.NeedsClassConstructorReference;
                }
                else if (Utilities.IsAutoAccessorPropertyDeclaration(memberNode) && _shouldTransformAutoAccessors == Ternary.True && ((IClassLikeDeclarationBase)node).Name is null && node.EmitNode?.ClassThis is null)
                {
                    facts |= ClassFieldsFacts.NeedsClassConstructorReference;
                }
                if (NodeTests.IsPropertyDeclaration(memberNode) || NodeTests.IsClassStaticBlockDeclaration(memberNode))
                {
                    if (_shouldTransformThisInStaticInitializers && (memberNode.TransformFlags & TransformFlags.ContainsLexicalThis) != 0)
                    {
                        facts |= ClassFieldsFacts.NeedsSubstitutionForThisInClassStaticField;
                        if ((facts & ClassFieldsFacts.ClassWasDecorated) == 0)
                        {
                            facts |= ClassFieldsFacts.NeedsClassConstructorReference;
                        }
                    }
                    if (_shouldTransformSuperInStaticInitializers && (memberNode.TransformFlags & TransformFlags.ContainsLexicalSuper) != 0)
                    {
                        if ((facts & ClassFieldsFacts.ClassWasDecorated) == 0)
                        {
                            facts |= ClassFieldsFacts.NeedsClassConstructorReference | ClassFieldsFacts.NeedsClassSuperReference;
                        }
                    }
                }
            }
            else if (!Utilities.HasAbstractModifier(Utilities.GetOriginalNode(memberNode)!))
            {
                if (Utilities.IsAutoAccessorPropertyDeclaration(memberNode))
                {
                    containsInstanceAutoAccessors = true;
                    containsInstancePrivateElements = containsInstancePrivateElements || Utilities.IsPrivateIdentifierClassElementDeclaration(memberNode);
                }
                else if (Utilities.IsPrivateIdentifierClassElementDeclaration(memberNode))
                {
                    containsInstancePrivateElements = true;
                    if (_resolver.HasNodeCheckFlag(memberNode, NodeCheckFlags.ContainsConstructorReference))
                    {
                        facts |= ClassFieldsFacts.NeedsClassConstructorReference;
                    }
                }
                else if (NodeTests.IsPropertyDeclaration(memberNode))
                {
                    containsPublicInstanceFields = true;
                    containsInitializedPublicInstanceFields = containsInitializedPublicInstanceFields || ((PropertyDeclaration)memberNode).Initializer is not null;
                }
            }
        }

        var willHoistInitializersToConstructor = _shouldTransformInitializersUsingDefine && containsPublicInstanceFields
            || _shouldTransformInitializersUsingSet && containsInitializedPublicInstanceFields
            || _shouldTransformPrivateElementsOrClassStaticBlocks && containsInstancePrivateElements
            || _shouldTransformPrivateElementsOrClassStaticBlocks && containsInstanceAutoAccessors && _shouldTransformAutoAccessors == Ternary.True;

        if (willHoistInitializersToConstructor)
        {
            facts |= ClassFieldsFacts.WillHoistInitializersToConstructor;
        }

        return facts;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1779-1794 — <c>visitExpressionWithTypeArgumentsInHeritageClause</c>.</summary>
    private object? VisitExpressionWithTypeArgumentsInHeritageClause(ExpressionWithTypeArguments node)
    {
        var facts = _lexicalEnvironment?.Data?.Facts ?? ClassFieldsFacts.None;
        if ((facts & ClassFieldsFacts.NeedsClassSuperReference) != 0)
        {
            var temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ true);
            GetClassLexicalEnvironment().SuperClassReference = temp;
            return _factory.UpdateExpressionWithTypeArguments(
                node,
                _factory.CreateAssignment(
                    temp,
                    Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
                /*typeArguments*/ null);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1796-1855 — <c>visitInNewClassLexicalEnvironment</c>.</summary>
    private U VisitInNewClassLexicalEnvironment<T, U>(T node, Func<T, ClassFieldsFacts, U> visitor)
        where T : Node, IClassLikeDeclarationBase
    {
        var savedCurrentClassContainer = _currentClassContainer;
        var savedPendingExpressions = _pendingExpressions;
        var savedLexicalEnvironment = _lexicalEnvironment;
        _currentClassContainer = node;
        _pendingExpressions = null;
        StartClassLexicalEnvironment();

        var shouldAlwaysTransformPrivateStaticElements = (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TransformPrivateStaticElements) != 0;
        if (_shouldTransformPrivateElementsOrClassStaticBlocks || shouldAlwaysTransformPrivateStaticElements)
        {
            var name = Utilities.GetNameOfDeclaration(node);
            if (name is Identifier identifier)
            {
                GetPrivateIdentifierEnvironment().Data.ClassName = identifier;
            }
            else if (node.EmitNode?.AssignedName is not null)
            {
                if (NodeTests.IsStringLiteral(node.EmitNode.AssignedName))
                {
                    var assignedName = (StringLiteral)node.EmitNode.AssignedName;
                    // If the class name was assigned from a string literal based on an Identifier, use the Identifier
                    // as the prefix.
                    if (assignedName.TextSourceNode is not null && NodeTests.IsIdentifier(assignedName.TextSourceNode))
                    {
                        GetPrivateIdentifierEnvironment().Data.ClassName = (Identifier)assignedName.TextSourceNode;
                    }
                    // If the class name was assigned from a string literal that is a valid identifier, create an
                    // identifier from it.
                    else if (ScannerUtilities.IsIdentifierText(assignedName.Text!, _languageVersion))
                    {
                        var prefixName = _factory.CreateIdentifier(assignedName.Text!);
                        GetPrivateIdentifierEnvironment().Data.ClassName = prefixName;
                    }
                }
            }
        }

        if (_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            var privateInstanceMethodsAndAccessors = GetPrivateInstanceMethodsAndAccessors(node);
            if (Core.Some(privateInstanceMethodsAndAccessors))
            {
                GetPrivateIdentifierEnvironment().Data.WeakSetName = CreateHoistedVariableForClass(
                    "instances",
                    ((INamedDeclaration)privateInstanceMethodsAndAccessors[0]).Name!,
                    /*suffix*/ null);
            }
        }

        var facts = GetClassFacts(node);
        if (facts != ClassFieldsFacts.None)
        {
            GetClassLexicalEnvironment().Facts = facts;
        }

        if ((facts & ClassFieldsFacts.NeedsSubstitutionForThisInClassStaticField) != 0)
        {
            EnableSubstitutionForClassStaticThisOrSuperReference();
        }

        var result = visitor(node, facts);
        EndClassLexicalEnvironment();
        Debug.Assert(ReferenceEquals(_lexicalEnvironment, savedLexicalEnvironment));
        _currentClassContainer = savedCurrentClassContainer;
        _pendingExpressions = savedPendingExpressions;
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1857-1859 — <c>visitClassDeclaration</c>.</summary>
    private List<IStatement> VisitClassDeclaration(ClassDeclaration node)
        => VisitInNewClassLexicalEnvironment(node, VisitClassDeclarationInNewClassLexicalEnvironment);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1861-1950 — <c>visitClassDeclarationInNewClassLexicalEnvironment</c>.</summary>
    private List<IStatement> VisitClassDeclarationInNewClassLexicalEnvironment(ClassDeclaration node, ClassFieldsFacts facts)
    {
        // If a class has private static fields, or a static field has a `this` or `super` reference,
        // then we need to allocate a temp variable to hold on to that reference.
        BinaryExpression? pendingClassReferenceAssignment = null;
        if ((facts & ClassFieldsFacts.NeedsClassConstructorReference) != 0)
        {
            // If we aren't transforming class static blocks, then we can't reuse `_classThis` since in
            // `class C { ... static { _classThis = ... } }; _classThis = C` the outer assignment would occur *after*
            // class static blocks evaluate and would overwrite the replacement constructor produced by class
            // decorators.

            // If we are transforming class static blocks, then we can reuse `_classThis` since the assignment
            // will be evaluated *before* the transformed static blocks are evaluated and thus won't overwrite
            // the replacement constructor.

            if (_shouldTransformPrivateElementsOrClassStaticBlocks && node.EmitNode?.ClassThis is not null)
            {
                GetClassLexicalEnvironment().ClassConstructor = node.EmitNode.ClassThis;
                pendingClassReferenceAssignment = _factory.CreateAssignment(node.EmitNode.ClassThis, TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ true));
            }
            else
            {
                var temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ true);
                GetClassLexicalEnvironment().ClassConstructor = _factory.CloneNode(temp);
                pendingClassReferenceAssignment = _factory.CreateAssignment(temp, TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ true));
            }
        }

        if (node.EmitNode?.ClassThis is not null)
        {
            GetClassLexicalEnvironment().ClassThis = node.EmitNode.ClassThis;
        }

        var isClassWithConstructorReference = _resolver.HasNodeCheckFlag(node, NodeCheckFlags.ContainsConstructorReference);
        var isExport = Utilities.HasSyntacticModifier(node, ModifierFlags.Export);
        var isDefault = Utilities.HasSyntacticModifier(node, ModifierFlags.Default);
        var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
        var heritageClauses = Utilities.VisitNodes(node.HeritageClauses, HeritageClauseVisitor, NodeTests.IsHeritageClause);
        var (members, prologue) = TransformClassMembers(node);

        var statements = new List<IStatement>();
        if (pendingClassReferenceAssignment is not null)
        {
            GetPendingExpressions().Insert(0, pendingClassReferenceAssignment);
        }

        // Write any pending expressions from elided or moved computed property names
        if (Core.Some(_pendingExpressions))
        {
            statements.Add(_factory.CreateExpressionStatement(_factory.InlineExpressions(_pendingExpressions)));
        }

        if (
            _shouldTransformInitializersUsingSet
            || _shouldTransformPrivateElementsOrClassStaticBlocks
            || (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TransformPrivateStaticElements) != 0)
        {
            // Emit static property assignment. Because classDeclaration is lexically evaluated,
            // it is safe to emit static property assignment after classDeclaration
            // From ES6 specification:
            //      HasLexicalDeclaration (N) : Determines if the argument identifier has a binding in this environment record that was created using
            //                                  a lexical declaration such as a LexicalDeclaration or a ClassDeclaration.

            var staticProperties = TransformationUtilities.GetStaticPropertiesAndClassStaticBlock(node);
            if (staticProperties.Count > 0)
            {
                AddPropertyOrClassStaticBlockStatements(statements, staticProperties, TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ true));
            }
        }

        if (statements.Count > 0 && isExport && isDefault)
        {
            modifiers = Utilities.VisitNodes(modifiers, modifier => FactoryUtilities.IsExportOrDefaultModifier(modifier) ? null : modifier, Utilities.IsModifier);
            statements.Add(_factory.CreateExportAssignment(
                /*modifiers*/ null,
                /*isExportEquals*/ false,
                TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true)));
        }

        var alias = GetClassLexicalEnvironment().ClassConstructor;
        if (isClassWithConstructorReference && alias is not null)
        {
            EnableSubstitutionForClassAliases();
            _classAliases![TransformationUtilities.GetOriginalNodeId(node)] = alias;
        }

        var classDecl = _factory.UpdateClassDeclaration(
            node,
            modifiers,
            node.Name,
            /*typeParameters*/ null,
            heritageClauses,
            members);
        statements.Insert(0, classDecl);

        if (prologue is not null)
        {
            statements.Insert(0, _factory.CreateExpressionStatement(prologue));
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1952-1954 — <c>visitClassExpression</c>.</summary>
    private Expression VisitClassExpression(ClassExpression node)
        => VisitInNewClassLexicalEnvironment(node, VisitClassExpressionInNewClassLexicalEnvironment);

    /// <summary>ported from: src/compiler/transformers/classFields.ts:1956-2075 — <c>visitClassExpressionInNewClassLexicalEnvironment</c>.</summary>
    /// <remarks>
    /// If this class expression is a transformation of a decorated class declaration, then we want to output the
    /// pendingExpressions as statements, not as inlined expressions with the class statement.
    /// <para>
    /// In this case, we use pendingStatements to produce the same output as the class declaration transformation. The
    /// VariableStatement visitor will insert these statements after the class expression variable statement.
    /// </para>
    /// </remarks>
    private Expression VisitClassExpressionInNewClassLexicalEnvironment(ClassExpression node, ClassFieldsFacts facts)
    {
        var isDecoratedClassDeclaration = (facts & ClassFieldsFacts.ClassWasDecorated) != 0;
        var staticPropertiesOrClassStaticBlocks = TransformationUtilities.GetStaticPropertiesAndClassStaticBlock(node);
        var isClassWithConstructorReference = _resolver.HasNodeCheckFlag(node, NodeCheckFlags.ContainsConstructorReference);
        var requiresBlockScopedVar = _resolver.HasNodeCheckFlag(node, NodeCheckFlags.BlockScopedBindingInLoop);

        Identifier? temp = null;
        Identifier CreateClassTempVar()
        {
            // If we aren't transforming class static blocks, then we can't reuse `_classThis` since in
            // `_classThis = class { ... static { _classThis = ... } }` the outer assignment would occur *after*
            // class static blocks evaluate and would overwrite the replacement constructor produced by class
            // decorators.

            // If we are transforming class static blocks, then we can reuse `_classThis` since the assignment
            // will be evaluated *before* the transformed static blocks are evaluated and thus won't overwrite
            // the replacement constructor.

            if (_shouldTransformPrivateElementsOrClassStaticBlocks && node.EmitNode?.ClassThis is not null)
            {
                var classThis = node.EmitNode.ClassThis;
                GetClassLexicalEnvironment().ClassConstructor = classThis;
                return classThis;
            }

            var newTemp = _factory.CreateTempVariable(
                requiresBlockScopedVar ? _context.AddBlockScopedVariable : _context.HoistVariableDeclaration,
                /*reservedInNestedScopes*/ true);
            GetClassLexicalEnvironment().ClassConstructor = _factory.CloneNode(newTemp);
            return newTemp;
        }

        if (node.EmitNode?.ClassThis is not null)
        {
            GetClassLexicalEnvironment().ClassThis = node.EmitNode.ClassThis;
        }

        if ((facts & ClassFieldsFacts.NeedsClassConstructorReference) != 0)
        {
            temp ??= CreateClassTempVar();
        }

        var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
        var heritageClauses = Utilities.VisitNodes(node.HeritageClauses, HeritageClauseVisitor, NodeTests.IsHeritageClause);
        var (members, prologue) = TransformClassMembers(node);
        var classExpression = _factory.UpdateClassExpression(
            node,
            modifiers,
            node.Name,
            /*typeParameters*/ null,
            heritageClauses,
            members);

        var expressions = new List<Expression>();
        if (prologue is not null)
        {
            expressions.Add(prologue);
        }

        // Static initializers are transformed to `static {}` blocks when `useDefineForClassFields: false`
        // and not also transforming static blocks.
        var hasTransformableStatics = (_shouldTransformPrivateElementsOrClassStaticBlocks
            || (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TransformPrivateStaticElements) != 0)
            && staticPropertiesOrClassStaticBlocks.Any(member =>
                NodeTests.IsClassStaticBlockDeclaration((Node)member)
                || Utilities.IsPrivateIdentifierClassElementDeclaration((Node)member)
                || _shouldTransformInitializers && TransformationUtilities.IsInitializedProperty(member));

        if (hasTransformableStatics || Core.Some(_pendingExpressions))
        {
            if (isDecoratedClassDeclaration)
            {
                Debug.AssertIsDefined(_pendingStatements, "Decorated classes transformed by TypeScript are expected to be within a variable declaration.");

                // Write any pending expressions from elided or moved computed property names
                if (Core.Some(_pendingExpressions))
                {
                    Core.AddRange(_pendingStatements, Core.Map(_pendingExpressions, (expression, _) => (IStatement)_factory.CreateExpressionStatement(expression)));
                }

                if (staticPropertiesOrClassStaticBlocks.Count > 0)
                {
                    AddPropertyOrClassStaticBlockStatements(
                        _pendingStatements!,
                        staticPropertiesOrClassStaticBlocks,
                        node.EmitNode?.ClassThis ?? TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ true));
                }

                if (temp is not null)
                {
                    expressions.Add(_factory.CreateAssignment(temp, classExpression));
                }
                else if (_shouldTransformPrivateElementsOrClassStaticBlocks && node.EmitNode?.ClassThis is not null)
                {
                    expressions.Add(_factory.CreateAssignment(node.EmitNode.ClassThis, classExpression));
                }
                else
                {
                    expressions.Add(classExpression);
                }
            }
            else
            {
                temp ??= CreateClassTempVar();
                if (isClassWithConstructorReference)
                {
                    // record an alias as the class name is not in scope for statics.
                    EnableSubstitutionForClassAliases();
                    var alias = _factory.CloneNode(temp);
                    alias.EmitNode!.AutoGenerate!.Flags &= ~GeneratedIdentifierFlags.ReservedInNestedScopes;
                    _classAliases![TransformationUtilities.GetOriginalNodeId(node)] = alias;
                }

                expressions.Add(_factory.CreateAssignment(temp, classExpression));

                // Add any pending expressions leftover from elided or relocated computed property names
                Core.AddRange(expressions, _pendingExpressions);
                Core.AddRange(expressions, GenerateInitializedPropertyExpressionsOrClassStaticBlock(staticPropertiesOrClassStaticBlocks, temp));
                expressions.Add(_factory.CloneNode(temp));
            }
        }
        else
        {
            expressions.Add(classExpression);
        }

        if (expressions.Count > 1)
        {
            // To preserve the behavior of the old emitter, we explicitly indent
            // the body of a class with static initializers.
            EmitNodes.AddEmitFlags(classExpression, EmitFlags.Indented);
            foreach (var expression in expressions)
            {
                FactoryUtilities.StartOnNewLine(expression);
            }
        }

        return _factory.InlineExpressions(expressions);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2077-2083 — <c>visitClassStaticBlockDeclaration</c>.</summary>
    private object? VisitClassStaticBlockDeclaration(ClassStaticBlockDeclaration node)
    {
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
        // ClassStaticBlockDeclaration for classes are transformed in `visitClassDeclaration` or `visitClassExpression`.
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2085-2096 — <c>visitThisExpression</c>.</summary>
    private Expression VisitThisExpression(ThisExpression node)
    {
        if (
            _shouldTransformThisInStaticInitializers
            && _currentClassElement is not null
            && NodeTests.IsClassStaticBlockDeclaration((Node)_currentClassElement)
            && _lexicalEnvironment?.Data is not null)
        {
            var data = _lexicalEnvironment.Data;
            return data.ClassThis ?? data.ClassConstructor ?? (Expression)node;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2098-2197 — <c>transformClassMembers</c>.</summary>
    private (NodeArray<IClassElement> Members, Expression? Prologue) TransformClassMembers(Node node)
    {
        var shouldTransformPrivateStaticElementsInClass = (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TransformPrivateStaticElements) != 0;
        var classMembers = ((IClassLikeDeclarationBase)node).Members!;

        // Declare private names
        if (_shouldTransformPrivateElementsOrClassStaticBlocks || _shouldTransformPrivateStaticElementsInFile)
        {
            foreach (var member in classMembers)
            {
                if (Utilities.IsPrivateIdentifierClassElementDeclaration((Node)member))
                {
                    var name = (PrivateIdentifier)((INamedDeclaration)member).Name!;
                    if (ShouldTransformClassElementToWeakMap((Node)member))
                    {
                        AddPrivateIdentifierToEnvironment((Node)member, name, AddPrivateIdentifierClassElementToEnvironment);
                    }
                    else
                    {
                        var privateEnv = GetPrivateIdentifierEnvironment();
                        TransformationUtilities.SetPrivateIdentifier(privateEnv, name, UntransformedPrivateIdentifierInfo.Instance);
                    }
                }
            }

            if (_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                if (Core.Some(GetPrivateInstanceMethodsAndAccessors(node)))
                {
                    CreateBrandCheckWeakSetForPrivateMethods();
                }
            }

            if (ShouldTransformAutoAccessorsInCurrentClass())
            {
                foreach (var member in classMembers)
                {
                    if (Utilities.IsAutoAccessorPropertyDeclaration((Node)member))
                    {
                        var storageName = _factory.GetGeneratedPrivateNameForNode(((INamedDeclaration)member).Name, /*prefix*/ null, "_accessor_storage");
                        if (
                            _shouldTransformPrivateElementsOrClassStaticBlocks
                            || shouldTransformPrivateStaticElementsInClass && Utilities.HasStaticModifier((Node)member))
                        {
                            AddPrivateIdentifierToEnvironment((PropertyDeclaration)member, storageName, AddPrivateIdentifierPropertyDeclarationToEnvironment);
                        }
                        else
                        {
                            var privateEnv = GetPrivateIdentifierEnvironment();
                            TransformationUtilities.SetPrivateIdentifier(privateEnv, storageName, UntransformedPrivateIdentifierInfo.Instance);
                        }
                    }
                }
            }
        }

        NodeArray<IClassElement>? members = Utilities.VisitNodes(classMembers, ClassElementVisitor, Utilities.IsClassElement);

        // Create a synthetic constructor if necessary
        ConstructorDeclaration? syntheticConstructor = null;
        if (!Core.Some(members, member => NodeTests.IsConstructorDeclaration((Node)member)))
        {
            syntheticConstructor = TransformConstructor(/*constructor*/ null, node);
        }

        Expression? prologue = null;

        // If there are pending expressions create a class static block in which to evaluate them, but only if
        // class static blocks are not also being transformed. This block will be injected at the top of the class
        // to ensure that expressions from computed property names are evaluated before any other static
        // initializers.
        ClassStaticBlockDeclaration? syntheticStaticBlock = null;
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks && Core.Some(_pendingExpressions))
        {
            var statement = (IStatement)_factory.CreateExpressionStatement(_factory.InlineExpressions(_pendingExpressions));
            if ((((Node)statement).TransformFlags & TransformFlags.ContainsLexicalThisOrSuper) != 0)
            {
                // If there are `this` or `super` references from computed property names, shift the expression
                // into an arrow function to be evaluated in the outer scope so that `this` and `super` are
                // properly captured.
                var temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                var arrow = _factory.CreateArrowFunction(
                    /*modifiers*/ null,
                    /*typeParameters*/ null,
                    /*parameters*/ [],
                    /*type*/ null,
                    /*equalsGreaterThanToken*/ null,
                    _factory.CreateBlock([statement], /*multiLine*/ null));
                prologue = _factory.CreateAssignment(temp, arrow);
                statement = _factory.CreateExpressionStatement(_factory.CreateCallExpression(temp, /*typeArguments*/ null, []));
            }

            var block = _factory.CreateBlock([statement], /*multiLine*/ null);
            syntheticStaticBlock = _factory.CreateClassStaticBlockDeclaration(block);
            _pendingExpressions = null;
        }

        // If we created a synthetic constructor or class static block, add them to the visited members
        // and return a new array.
        if (syntheticConstructor is not null || syntheticStaticBlock is not null)
        {
            // PORT-NOTE: the reference's four `append` calls skip an undefined value and its `addRange` extends the
            // list; the port states both directly, which is the same list in the same order.
            var membersArray = new List<IClassElement>();
            var classThisAssignmentBlock = Core.Find(members, (member, _) => ClassThis.IsClassThisAssignmentBlock((Node)member));
            var classNamedEvaluationHelperBlock = Core.Find(members, (member, _) => NamedEvaluation.IsClassNamedEvaluationHelperBlock((Node)member));
            if (classThisAssignmentBlock is not null) membersArray.Add(classThisAssignmentBlock);
            if (classNamedEvaluationHelperBlock is not null) membersArray.Add(classNamedEvaluationHelperBlock);
            if (syntheticConstructor is not null) membersArray.Add(syntheticConstructor);
            if (syntheticStaticBlock is not null) membersArray.Add(syntheticStaticBlock);
            IReadOnlyList<IClassElement> remainingMembers = classThisAssignmentBlock is not null || classNamedEvaluationHelperBlock is not null
                ? [.. members!.Where(member => !ReferenceEquals(member, classThisAssignmentBlock) && !ReferenceEquals(member, classNamedEvaluationHelperBlock))]
                : members!;
            membersArray.AddRange(remainingMembers);
            members = Utilities.SetTextRange(_factory.CreateNodeArray(membersArray, /*hasTrailingComma*/ null), classMembers);
        }

        return (members!, prologue);
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2199-2213 — <c>createBrandCheckWeakSetForPrivateMethods</c>.</summary>
    private void CreateBrandCheckWeakSetForPrivateMethods()
    {
        var weakSetName = GetPrivateIdentifierEnvironment().Data.WeakSetName;
        Debug.Assert(weakSetName is not null, "weakSetName should be set in private identifier environment");

        GetPendingExpressions().Add(
            _factory.CreateAssignment(
                weakSetName!,
                _factory.CreateNewExpression(
                    _factory.CreateIdentifier("WeakSet"),
                    /*typeArguments*/ null,
                    [])));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2215-2247 — <c>transformConstructor</c>.</summary>
    private ConstructorDeclaration? TransformConstructor(ConstructorDeclaration? constructor, Node container)
    {
        constructor = Utilities.VisitNode(constructor, Visitor, NodeTests.IsConstructorDeclaration);
        if (_lexicalEnvironment?.Data is null || (_lexicalEnvironment.Data.Facts & ClassFieldsFacts.WillHoistInitializersToConstructor) == 0)
        {
            return constructor;
        }

        var extendsClauseElement = Utilities.GetEffectiveBaseTypeNode(container);
        var isDerivedClass = extendsClauseElement is not null
            && FactoryUtilities.SkipOuterExpressions(extendsClauseElement.Expression!).Kind != SyntaxKind.NullKeyword;
        var parameters = Utilities.VisitParameterList(constructor?.Parameters, Visitor, _context);
        var body = TransformConstructorBody(container, constructor, isDerivedClass);
        if (body is null)
        {
            return constructor;
        }

        if (constructor is not null)
        {
            Debug.Assert(parameters is not null);
            return _factory.UpdateConstructorDeclaration(constructor, /*modifiers*/ null, parameters, body);
        }

        return FactoryUtilities.StartOnNewLine(
            NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateConstructorDeclaration(
                        /*modifiers*/ null,
                        parameters ?? [],
                        body),
                    constructor ?? container),
                constructor));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2249-2309 — <c>transformConstructorBodyWorker</c>.</summary>
    private void TransformConstructorBodyWorker(
        List<IStatement> statementsOut,
        NodeArray<IStatement> statementsIn,
        int statementOffset,
        List<int> superPath,
        int superPathDepth,
        List<IStatement> initializerStatements,
        ConstructorDeclaration constructor)
    {
        var superStatementIndex = superPath[superPathDepth];
        var superStatement = (Node)statementsIn[superStatementIndex];
        Core.AddRange(
            statementsOut,
            Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, statementOffset, superStatementIndex - statementOffset));
        statementOffset = superStatementIndex + 1;
        if (NodeTests.IsTryStatement(superStatement))
        {
            var tryBlockStatements = new List<IStatement>();

            TransformConstructorBodyWorker(
                tryBlockStatements,
                ((TryStatement)superStatement).TryBlock!.Statements!,
                /*statementOffset*/ 0,
                superPath,
                superPathDepth + 1,
                initializerStatements,
                constructor);

            var tryBlockStatementsArray = _factory.CreateNodeArray(tryBlockStatements, /*hasTrailingComma*/ null);
            Utilities.SetTextRange(tryBlockStatementsArray, ((TryStatement)superStatement).TryBlock!.Statements);

            statementsOut.Add(_factory.UpdateTryStatement(
                (TryStatement)superStatement,
                _factory.UpdateBlock(((TryStatement)superStatement).TryBlock!, tryBlockStatements),
                Utilities.VisitNode(((TryStatement)superStatement).CatchClause, Visitor, NodeTests.IsCatchClause),
                Utilities.VisitNode(((TryStatement)superStatement).FinallyBlock, Visitor, NodeTests.IsBlock)));
        }
        else
        {
            Core.AddRange(statementsOut, Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, superStatementIndex, 1));

            // Add the property initializers. Transforms this:
            //
            //  public x = 1;
            //
            // Into this:
            //
            //  constructor() {
            //      this.x = 1;
            //  }
            //
            // If we do useDefineForClassFields, they'll be converted elsewhere.
            // We instead *remove* them from the transformed output at this stage.

            // parameter-property assignments should occur immediately after the prologue and `super()`,
            // so only count the statements that immediately follow.
            while (statementOffset < statementsIn.Count)
            {
                var statement = (Node)statementsIn[statementOffset];
                if (Utilities.IsParameterPropertyDeclaration(Utilities.GetOriginalNode(statement)!, constructor))
                {
                    statementOffset++;
                }
                else
                {
                    break;
                }
            }

            Core.AddRange(statementsOut, initializerStatements);
        }

        Core.AddRange(statementsOut, Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, statementOffset));
    }

    /// <summary>ported from: src/compiler/transformers/classFields.ts:2311-2432 — <c>transformConstructorBody</c>.</summary>
    private Block? TransformConstructorBody(Node node, ConstructorDeclaration? constructor, bool isDerivedClass)
    {
        var instanceProperties = TransformationUtilities.GetProperties(node, /*requireInitializer*/ false, /*isStatic*/ false);
        var properties = instanceProperties;
        if (!_useDefineForClassFields)
        {
            properties = [.. properties.Where(property => property.Initializer is not null || NodeTests.IsPrivateIdentifier(property.Name!) || Utilities.HasAccessorModifier(property))];
        }

        var privateMethodsAndAccessors = GetPrivateInstanceMethodsAndAccessors(node);
        var needsConstructorBody = properties.Count > 0 || privateMethodsAndAccessors.Count > 0;

        // Only generate synthetic constructor when there are property initializers to move.
        if (constructor is null && !needsConstructorBody)
        {
            return null;
        }

        _context.ResumeLexicalEnvironment();

        var needsSyntheticConstructor = constructor is null && isDerivedClass;
        var statementOffset = 0;
        var statements = new List<IStatement>();

        // Add the property initializers. Transforms this:
        //
        //  public x = 1;
        //
        // Into this:
        //
        //  constructor() {
        //      this.x = 1;
        //  }
        //
        var initializerStatements = new List<IStatement>();
        var receiver = _factory.CreateThis();

        // private methods can be called in property initializers, they should execute first.
        AddInstanceMethodStatements(initializerStatements, privateMethodsAndAccessors, receiver);
        if (constructor is not null)
        {
            var parameterProperties = instanceProperties.Where(property => Utilities.IsParameterPropertyDeclaration(Utilities.GetOriginalNode(property)!, constructor)).ToList();
            var nonParameterProperties = properties.Where(property => !Utilities.IsParameterPropertyDeclaration(Utilities.GetOriginalNode(property)!, constructor)).ToList();
            AddPropertyOrClassStaticBlockStatements(initializerStatements, parameterProperties, receiver);
            AddPropertyOrClassStaticBlockStatements(initializerStatements, nonParameterProperties, receiver);
        }
        else
        {
            AddPropertyOrClassStaticBlockStatements(initializerStatements, properties, receiver);
        }

        if (constructor?.Body is not null)
        {
            statementOffset = _factory.CopyPrologue(constructor.Body.Statements, statements, /*ensureUseStrict*/ false, Visitor);
            var superStatementIndices = TransformationUtilities.FindSuperStatementIndexPath(constructor.Body.Statements!, statementOffset);
            if (superStatementIndices.Count > 0)
            {
                TransformConstructorBodyWorker(
                    statements,
                    constructor.Body.Statements!,
                    statementOffset,
                    superStatementIndices,
                    /*superPathDepth*/ 0,
                    initializerStatements,
                    constructor);
            }
            else
            {
                // parameter-property assignments should occur immediately after the prologue and `super()`,
                // so only count the statements that immediately follow.
                while (statementOffset < constructor.Body.Statements!.Count)
                {
                    var statement = (Node)constructor.Body.Statements![statementOffset];
                    if (Utilities.IsParameterPropertyDeclaration(Utilities.GetOriginalNode(statement)!, constructor))
                    {
                        statementOffset++;
                    }
                    else
                    {
                        break;
                    }
                }
                Core.AddRange(statements, initializerStatements);
                Core.AddRange(statements, Utilities.VisitNodes(constructor.Body.Statements!, Visitor, Utilities.IsStatement, statementOffset));
            }
        }
        else
        {
            if (needsSyntheticConstructor)
            {
                // Add a synthetic `super` call:
                //
                //  super(...arguments);
                //
                statements.Add(
                    _factory.CreateExpressionStatement(
                        _factory.CreateCallExpression(
                            _factory.CreateSuper(),
                            /*typeArguments*/ null,
                            [_factory.CreateSpreadElement(_factory.CreateIdentifier("arguments"))])));
            }
            Core.AddRange(statements, initializerStatements);
        }

        statements = _factory.MergeLexicalEnvironment(statements, _context.EndLexicalEnvironment());

        if (statements.Count == 0 && constructor is null)
        {
            return null;
        }

        var multiLine = constructor?.Body is not null && constructor.Body.Statements!.Count >= statements.Count
            ? constructor.Body.MultiLine ?? statements.Count > 0
            : statements.Count > 0;

        return Utilities.SetTextRange(
            _factory.CreateBlock(
                Utilities.SetTextRange(
                    _factory.CreateNodeArray(statements, /*hasTrailingComma*/ null),
                    /*location*/ (ITextRange?)constructor?.Body?.Statements ?? (ITextRange?)((IClassLikeDeclarationBase)node).Members),
                multiLine),
            /*location*/ constructor?.Body);
    }
}
