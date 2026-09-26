#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/esDecorators.ts:670-1600, :2276-2533 — the class/class-element half of
// `transformESDecorators`'s body, plus the pseudo-descriptor and metadata factories it builds its statements from.
/// <summary>
/// ported from: src/compiler/transformers/esDecorators.ts:670-1600 and :2276-2533 — <c>transformClassLike</c> through
/// <c>visitPropertyDeclaration</c>, then <c>createDescriptorMethod</c> through <c>createSymbolMetadataReference</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the unions): TS's <c>ClassLikeDeclaration</c> is the union of <see cref="ClassDeclaration"/> and
/// <see cref="ClassExpression"/>, whose only common model face is <see cref="IClassLikeDeclarationBase"/> (the choice
/// <c>Utilities.Binder.cs</c> records for the same union); the reference's <c>ClassElement</c> is
/// <see cref="IClassElement"/>; <c>PropertyName</c>/<c>VisitResult&lt;Statement&gt;</c> are the port's <see cref="Node"/>
/// and the visitor slot's <c>object?</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>ModifiersArray</c>): TS's <c>ModifiersArray</c> is <c>NodeArray&lt;Modifier&gt;</c>, which the port
/// states as the <c>NodeArray&lt;Node&gt;?</c> every <c>Modifiers</c> slot holds (decorators and modifiers share the
/// slot). The one place the reference hands those modifiers to a function-expression builder — which takes
/// <c>List&lt;ModifierToken&gt;?</c> — is <see cref="AsModifierTokens"/>, the same narrowing
/// <c>FactoryUtilities.AsModifierTokens</c> performs for the landed parser helpers.
/// </para>
/// <para>
/// PORT-NOTE (defaulted parameters): TS's <c>createDescriptor?</c> callback defaults to <c>undefined</c> and its
/// <c>modifiers</c> parameter is <c>ModifiersArray | undefined</c>; both are stated as C# <c>null</c>.
/// </para>
/// <para>
/// PORT-NOTE (structural <c>SourceMapRange</c>): where the reference hands a <em>node</em> to
/// <c>setSourceMapRange</c> (e.g. <c>setSourceMapRange(backingField.name, node.name)</c>), the port converts through
/// <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/> because no port node implements
/// <see cref="ISourceMapRange"/> — the gap <c>Transforms/ClassThis.cs</c> records for the same call.
/// </para>
/// </remarks>
internal sealed partial class ESDecoratorTransformer
{
    // ---------------------------------------------------------------------------------------------
    // esDecorators.ts:670-1047 — the class
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:670-1047 — <c>transformClassLike</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>forEachEntry</c> over <c>classInfo.memberInfos</c> is the two ordered loops over
    /// <see cref="ClassInfo.MemberInfos"/> (that member's own note records why the order matters). The reference's
    /// <c>append</c>/<c>addRange</c> pairs are <see cref="Append{T}"/>/<see cref="AddRange{T}"/>, and its
    /// <c>thisVisitor</c> closure is the local function below, which captures <c>outerThis</c> and
    /// <c>classDefinitionStatements</c> exactly as the reference's closure does.</remarks>
    private CallExpression TransformClassLike(IClassLikeDeclarationBase node)
    {
        _context.StartLexicalEnvironment();

        // When a class has class decorators we end up transforming it into a statement that would otherwise give it an
        // assigned name. If the class doesn't have an assigned name, we'll give it an assigned name of `""`)
        if (!NamedEvaluation.ClassHasDeclaredOrExplicitlyAssignedName((Node)node) && Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ false, (Node)node))
        {
            node = InjectNamedEvaluationHelperBlock(node, _factory.CreateStringLiteral(""));
        }

        var classReference = TypeScriptTransformSupport.GetLocalName(_factory, (Node)node, /*allowComments*/ false, /*allowSourceMaps*/ false, /*ignoreAssignedName*/ true);
        var classInfo = CreateClassInfo(node);
        var classDefinitionStatements = new List<IStatement>();
        List<IStatement>? leadingBlockStatements = null;
        List<IStatement>? trailingBlockStatements = null;
        ConstructorDeclaration? syntheticConstructor = null;
        NodeArray<HeritageClause>? heritageClauses = null;
        var shouldTransformPrivateStaticElementsInClass = false;

        // 1. Class decorators are evaluated outside of the private name scope of the class.
        var classDecorators = TransformAllDecoratorsOfDeclaration(TransformationUtilities.GetAllDecoratorsOfClass((Node)node, /*useLegacyDecorators*/ false));
        if (classDecorators is not null)
        {
            // - Since class decorators don't have privileged access to private names defined inside the class,
            //   they must be evaluated outside of the class body.
            // - Since a class decorator can replace the class constructor, we must define a variable to keep track
            //   of the mutated class.
            // - Since a class decorator can add extra initializers, we must define a variable to keep track of
            //   extra initializers.
            classInfo.ClassDecoratorsName = _factory.CreateUniqueName("_classDecorators", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
            classInfo.ClassDescriptorName = _factory.CreateUniqueName("_classDescriptor", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
            classInfo.ClassExtraInitializersName = _factory.CreateUniqueName("_classExtraInitializers", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
            Debug.AssertIsDefined(classInfo.ClassThis);
            classDefinitionStatements.Add(CreateLet(classInfo.ClassDecoratorsName, _factory.CreateArrayLiteralExpression(classDecorators, /*multiLine*/ null)));
            classDefinitionStatements.Add(CreateLet(classInfo.ClassDescriptorName!));
            classDefinitionStatements.Add(CreateLet(classInfo.ClassExtraInitializersName!, _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)));
            classDefinitionStatements.Add(CreateLet(classInfo.ClassThis!));

            if (classInfo.HasStaticPrivateClassElements)
            {
                shouldTransformPrivateStaticElementsInClass = true;
                _shouldTransformPrivateStaticElementsInFile = true;
            }
        }

        // Rewrite `super` in static initializers so that we can use the correct `this`.
        var extendsClause = Utilities.GetHeritageClause(node.HeritageClauses, SyntaxKind.ExtendsKeyword);
        var extendsElement = extendsClause is null ? null : Core.FirstOrUndefined(extendsClause.Types);
        Expression? extendsExpression = extendsElement is null ? null : Utilities.VisitNode(extendsElement.Expression, Visitor, Utilities.IsExpression);
        if (extendsExpression is not null)
        {
            classInfo.ClassSuper = _factory.CreateUniqueName("_classSuper", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);

            // Ensure we do not give the class or function an assigned name due to the variable by prefixing it
            // with `0, `.
            var unwrapped = FactoryUtilities.SkipOuterExpressions(extendsExpression);
            var safeExtendsExpression = NodeTests.IsClassExpression(unwrapped!) && ((ClassExpression)unwrapped!).Name is null ||
                    NodeTests.IsFunctionExpression(unwrapped!) && ((FunctionExpression)unwrapped!).Name is null ||
                    NodeTests.IsArrowFunction(unwrapped!) ?
                _factory.CreateComma(_factory.CreateNumericLiteral(0, /*numericLiteralFlags*/ null), extendsExpression) :
                extendsExpression;
            classDefinitionStatements.Add(CreateLet(classInfo.ClassSuper!, safeExtendsExpression));
            var updatedExtendsElement = _factory.UpdateExpressionWithTypeArguments(extendsElement, classInfo.ClassSuper, /*typeArguments*/ null);
            var updatedExtendsClause = _factory.UpdateHeritageClause(extendsClause, [updatedExtendsElement]);
            heritageClauses = _factory.CreateNodeArray([updatedExtendsClause], /*hasTrailingComma*/ null);
        }

        Expression renamedClassThis = classInfo.ClassThis is not null ? classInfo.ClassThis : _factory.CreateThis();

        // 3. The name of the class is assigned.
        //
        // If the class did not have a name, the caller should have performed injectClassNamedEvaluationHelperBlockIfMissing
        // prior to calling this function if a name was needed.

        // 4. For each member:
        //    a. Member Decorators are evaluated
        //    b. Computed Property Name is evaluated, if present

        // We visit members in two passes:
        // - The first pass visits methods, accessors, and fields to collect decorators and computed property names.
        // - The second pass visits the constructor to add instance initializers.
        //
        // NOTE: If there are no constructors, but there are instance initializers, a synthetic constructor is added.
        EnterClass(classInfo);

        leadingBlockStatements = Append(leadingBlockStatements, CreateMetadata(classInfo.MetadataReference, classInfo.ClassSuper));

        // Since the constructor can appear anywhere in the class body and its transform depends on other class elements,
        // we must first visit all non-constructor members, then visit the constructor, all while maintaining document order.
        var members = node.Members;
        members = Utilities.VisitNodes(members, node => NodeTests.IsConstructorDeclaration(node) ? node : ClassElementVisitor(node), Utilities.IsClassElement);
        members = Utilities.VisitNodes(members, node => NodeTests.IsConstructorDeclaration(node) ? ClassElementVisitor(node) : node, Utilities.IsClassElement);
        if (_pendingExpressions is not null)
        {
            Identifier? outerThis = null;

            // If a pending expression contains a lexical `this`, we'll need to capture the lexical `this` of the
            // container and transform it in the expression. This ensures we use the correct `this` in the resulting
            // class `static` block. We don't use substitution here because the size of the tree we are visiting
            // is likely to be small and doesn't justify the complexity of introducing substitution.
            object? ThisVisitor(Node node)
            {
                if ((node.TransformFlags & TransformFlags.ContainsLexicalThis) == 0)
                {
                    return node;
                }

                switch (node.Kind)
                {
                    case SyntaxKind.ThisKeyword:
                        if (outerThis is null)
                        {
                            outerThis = _factory.CreateUniqueName("_outerThis", GeneratedIdentifierFlags.Optimistic);
                            classDefinitionStatements.Insert(0, CreateLet(outerThis, _factory.CreateThis()));
                        }
                        return outerThis;

                    default:
                        return Utilities.VisitEachChildWorker(node, ThisVisitor, _context);
                }
            }

            foreach (var pendingExpression in _pendingExpressions)
            {
                var expression = Utilities.VisitNode(pendingExpression, ThisVisitor, Utilities.IsExpression);

                var statement = _factory.CreateExpressionStatement(expression!);
                leadingBlockStatements = Append(leadingBlockStatements, statement);
            }
            _pendingExpressions = null;
        }
        ExitClass();

        if (Core.Some(classInfo.PendingInstanceInitializers) && Utilities.GetFirstConstructorWithBody((Node)node) is null)
        {
            var initializerStatements = PrepareConstructor(node, classInfo);
            if (initializerStatements is not null)
            {
                var extendsClauseElement = Utilities.GetEffectiveBaseTypeNode((Node)node);
                var isDerivedClass = extendsClauseElement is not null && FactoryUtilities.SkipOuterExpressions(extendsClauseElement.Expression!).Kind != SyntaxKind.NullKeyword;
                var constructorStatements = new List<IStatement>();
                if (isDerivedClass)
                {
                    var spreadArguments = _factory.CreateSpreadElement(_factory.CreateIdentifier("arguments"));
                    var superCall = _factory.CreateCallExpression(_factory.CreateSuper(), /*typeArguments*/ null, [spreadArguments]);
                    constructorStatements.Add(_factory.CreateExpressionStatement(superCall));
                }

                constructorStatements.AddRange(initializerStatements);

                var constructorBody = _factory.CreateBlock(constructorStatements, /*multiLine*/ true);
                syntheticConstructor = _factory.CreateConstructorDeclaration(/*modifiers*/ null, [], constructorBody);
            }
        }

        // Used in class definition steps 5, 7, and 11
        if (classInfo.StaticMethodExtraInitializersName is not null)
        {
            classDefinitionStatements.Add(
                CreateLet(classInfo.StaticMethodExtraInitializersName, _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)));
        }

        // Used in class definition steps 6, 8, and during construction
        if (classInfo.InstanceMethodExtraInitializersName is not null)
        {
            classDefinitionStatements.Add(
                CreateLet(classInfo.InstanceMethodExtraInitializersName, _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)));
        }

        // Used in class definition steps 7, 8, 12, and construction
        if (classInfo.MemberInfos is not null)
        {
            foreach (var (memberInfo, member) in classInfo.MemberInfos)
            {
                if (Utilities.IsStatic((Node)member))
                {
                    classDefinitionStatements.Add(CreateLet(memberInfo.MemberDecoratorsName));
                    if (memberInfo.MemberInitializersName is not null)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberInitializersName, _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)));
                    }
                    if (memberInfo.MemberExtraInitializersName is not null)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberExtraInitializersName, _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)));
                    }
                    if (memberInfo.MemberDescriptorName is not null)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberDescriptorName));
                    }
                }
            }
        }

        // Used in class definition steps 7, 8, 12, and construction
        if (classInfo.MemberInfos is not null)
        {
            foreach (var (memberInfo, member) in classInfo.MemberInfos)
            {
                if (!Utilities.IsStatic((Node)member))
                {
                    classDefinitionStatements.Add(CreateLet(memberInfo.MemberDecoratorsName));
                    if (memberInfo.MemberInitializersName is not null)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberInitializersName, _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)));
                    }
                    if (memberInfo.MemberExtraInitializersName is not null)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberExtraInitializersName, _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)));
                    }
                    if (memberInfo.MemberDescriptorName is not null)
                    {
                        classDefinitionStatements.Add(CreateLet(memberInfo.MemberDescriptorName));
                    }
                }
            }
        }

        // 5. Static non-field element decorators are applied
        leadingBlockStatements = AddRange(leadingBlockStatements, classInfo.StaticNonFieldDecorationStatements);

        // 6. Non-static non-field element decorators are applied
        leadingBlockStatements = AddRange(leadingBlockStatements, classInfo.NonStaticNonFieldDecorationStatements);

        // 7. Static field element decorators are applied
        leadingBlockStatements = AddRange(leadingBlockStatements, classInfo.StaticFieldDecorationStatements);

        // 8. Non-static field element decorators are applied
        leadingBlockStatements = AddRange(leadingBlockStatements, classInfo.NonStaticFieldDecorationStatements);

        // 9. Class decorators are applied
        // 10. Class binding is initialized
        if (classInfo.ClassDescriptorName is not null && classInfo.ClassDecoratorsName is not null && classInfo.ClassExtraInitializersName is not null && classInfo.ClassThis is not null)
        {
            leadingBlockStatements ??= [];

            // produces:
            //  __esDecorate(null, _classDescriptor = { value: this }, _classDecorators, { kind: "class", name: this.name, metadata }, _classExtraInitializers);
            var valueProperty = _factory.CreatePropertyAssignment("value", renamedClassThis);
            var classDescriptor = _factory.CreateObjectLiteralExpression([valueProperty], /*multiLine*/ null);
            var classDescriptorAssignment = _factory.CreateAssignment(classInfo.ClassDescriptorName, classDescriptor);
            var classNameReference = _factory.CreatePropertyAccessExpression(renamedClassThis, "name");
            var esDecorateHelper = _emitHelpers.CreateESDecorateHelper(
                _factory.CreateNull(),
                classDescriptorAssignment,
                classInfo.ClassDecoratorsName,
                new ESDecorateClassContext(classNameReference, classInfo.MetadataReference),
                _factory.CreateNull(),
                classInfo.ClassExtraInitializersName);
            var esDecorateStatement = _factory.CreateExpressionStatement(esDecorateHelper);
            EmitNodes.SetSourceMapRange(esDecorateStatement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators((Node)node)));
            leadingBlockStatements.Add(esDecorateStatement);

            // produces:
            //  C = _classThis = _classDescriptor.value;
            var classDescriptorValueReference = _factory.CreatePropertyAccessExpression(classInfo.ClassDescriptorName, "value");
            var classThisAssignment = _factory.CreateAssignment(classInfo.ClassThis, classDescriptorValueReference);
            var classReferenceAssignment = _factory.CreateAssignment(classReference, classThisAssignment);
            leadingBlockStatements.Add(_factory.CreateExpressionStatement(classReferenceAssignment));
        }

        // produces:
        //  if (metadata) Object.defineProperty(C, Symbol.metadata, { configurable: true, writable: true, value: metadata });
        leadingBlockStatements!.Add(CreateSymbolMetadata(renamedClassThis, classInfo.MetadataReference));

        // 11. Static extra initializers are evaluated
        // 12. Static fields are initialized (incl. extra initializers) and static blocks are evaluated
        if (Core.Some(classInfo.PendingStaticInitializers))
        {
            foreach (var initializer in classInfo.PendingStaticInitializers!)
            {
                var initializerStatement = _factory.CreateExpressionStatement(initializer);
                EmitNodes.SetSourceMapRange(initializerStatement, TypeScriptTransformSupport.SourceMapRangeOf(EmitNodes.GetSourceMapRange(initializer)));
                trailingBlockStatements = Append(trailingBlockStatements, initializerStatement);
            }
            classInfo.PendingStaticInitializers = null;
        }

        // 13. Class extra initializers are evaluated
        if (classInfo.ClassExtraInitializersName is not null)
        {
            var runClassInitializersHelper = _emitHelpers.CreateRunInitializersHelper(renamedClassThis, classInfo.ClassExtraInitializersName, /*value*/ null);
            var runClassInitializersStatement = _factory.CreateExpressionStatement(runClassInitializersHelper);
            EmitNodes.SetSourceMapRange(runClassInitializersStatement, NameOrDecoratorRange(node));
            trailingBlockStatements = Append(trailingBlockStatements, runClassInitializersStatement);
        }

        // If there are no other static initializers to run, combine the leading and trailing block statements
        if (leadingBlockStatements is not null && trailingBlockStatements is not null && !classInfo.HasStaticInitializers)
        {
            leadingBlockStatements.AddRange(trailingBlockStatements);
            trailingBlockStatements = null;
        }

        // prepare a leading `static {}` block, if necessary
        //
        // produces:
        //  class C {
        //      static { ... }
        //      ...
        //  }
        var leadingStaticBlock = leadingBlockStatements is not null
            ? _factory.CreateClassStaticBlockDeclaration(_factory.CreateBlock(leadingBlockStatements, /*multiLine*/ true))
            : null;

        if (leadingStaticBlock is not null && shouldTransformPrivateStaticElementsInClass)
        {
            // We use `InternalEmitFlags.TransformPrivateStaticElements` as a marker on a class static block
            // to inform the classFields transform that it shouldn't rename `this` to `_classThis` in the
            // transformed class static block.
            EmitNodes.SetInternalEmitFlags(leadingStaticBlock, InternalEmitFlags.TransformPrivateStaticElements);
        }

        // prepare a trailing `static {}` block, if necessary
        //
        // produces:
        //  class C {
        //      ...
        //      static { ... }
        //  }
        var trailingStaticBlock = trailingBlockStatements is not null
            ? _factory.CreateClassStaticBlockDeclaration(_factory.CreateBlock(trailingBlockStatements, /*multiLine*/ true))
            : null;

        if (leadingStaticBlock is not null || syntheticConstructor is not null || trailingStaticBlock is not null)
        {
            var newMembers = new List<IClassElement>();

            // add the NamedEvaluation helper block, if needed
            var existingNamedEvaluationHelperBlockIndex = Core.FindIndex(members!, static (member, _) => NamedEvaluation.IsClassNamedEvaluationHelperBlock((Node)member));

            // add the leading `static {}` block
            if (leadingStaticBlock is not null)
            {
                // add the `static {}` block after any existing NamedEvaluation helper block, if one exists.
                newMembers.AddRange(members!.GetRange(0, existingNamedEvaluationHelperBlockIndex + 1));
                newMembers.Add(leadingStaticBlock);
                newMembers.AddRange(members!.GetRange(existingNamedEvaluationHelperBlockIndex + 1, members.Count - existingNamedEvaluationHelperBlockIndex - 1));
            }
            else
            {
                newMembers.AddRange(members!);
            }

            // append the synthetic constructor, if necessary
            if (syntheticConstructor is not null)
            {
                newMembers.Add(syntheticConstructor);
            }

            // append a trailing `static {}` block, if necessary
            if (trailingStaticBlock is not null)
            {
                newMembers.Add(trailingStaticBlock);
            }

            members = Utilities.SetTextRange(_factory.CreateNodeArray(newMembers, /*hasTrailingComma*/ null), members);
        }

        var lexicalEnvironment = _context.EndLexicalEnvironment();
        ClassExpression classExpression;
        if (classDecorators is not null)
        {
            // We use `var` instead of `let` so we can leverage NamedEvaluation to define the class name
            // and still be able to ensure it is initialized prior to any use in `static {}`.

            // produces:
            //  (() => {
            //      let _classDecorators = [...];
            //      let _classDescriptor;
            //      let _classExtraInitializers = [];
            //      let _classThis;
            //      ...
            //      var C = class {
            //          static {
            //              __esDecorate(null, _classDescriptor = { value: this }, _classDecorators, ...);
            //              // `C` is initialized here
            //              C = _classThis = _classDescriptor.value;
            //          }
            //          static x = 1;
            //          static y = C.x; // `C` will already be defined here.
            //          static { ... }
            //      };
            //      return C;
            //  })();

            var classExpressionValue = _factory.CreateClassExpression(/*modifiers*/ null, /*name*/ null, /*typeParameters*/ null, heritageClauses, members);
            if (classInfo.ClassThis is not null)
            {
                classExpressionValue = ClassThis.InjectClassThisAssignmentIfMissing(_factory, classExpressionValue, classInfo.ClassThis);
            }
            classExpression = classExpressionValue;

            var classReferenceDeclaration = _factory.CreateVariableDeclaration(classReference, /*exclamationToken*/ null, /*type*/ null, classExpression);
            var classReferenceVarDeclList = _factory.CreateVariableDeclarationList([classReferenceDeclaration], /*flags*/ null);
            Expression returnExpr = classInfo.ClassThis is not null ? _factory.CreateAssignment(classReference, classInfo.ClassThis) : classReference;
            classDefinitionStatements.Add(_factory.CreateVariableStatement(/*modifiers*/ null, classReferenceVarDeclList));
            classDefinitionStatements.Add(_factory.CreateReturnStatement(returnExpr));
        }
        else
        {
            // produces:
            //  return <classExpression>;
            classExpression = _factory.CreateClassExpression(/*modifiers*/ null, node.Name, /*typeParameters*/ null, heritageClauses, members);
            classDefinitionStatements.Add(_factory.CreateReturnStatement(classExpression));
        }

        if (shouldTransformPrivateStaticElementsInClass)
        {
            EmitNodes.AddInternalEmitFlags(classExpression, InternalEmitFlags.TransformPrivateStaticElements);
            foreach (var member in classExpression.Members!)
            {
                if ((Utilities.IsPrivateIdentifierClassElementDeclaration((Node)member) || Utilities.IsAutoAccessorPropertyDeclaration((Node)member)) && Utilities.HasStaticModifier((Node)member))
                {
                    EmitNodes.AddInternalEmitFlags((Node)member, InternalEmitFlags.TransformPrivateStaticElements);
                }
            }
        }

        NodeFactory.SetOriginalNode(classExpression, (Node)node);
        return _factory.CreateImmediatelyInvokedArrowFunction(_factory.MergeLexicalEnvironment(classDefinitionStatements, lexicalEnvironment));
    }

    /// <summary>The reference's <c>injectClassNamedEvaluationHelperBlockIfMissing(context, node, name)</c> call, for a
    /// class-like node the caller holds through the union's interface.</summary>
    /// <remarks>PORT-NOTE: TS's generic overload infers <c>T</c> from the argument, and every one of its call sites here
    /// passes either a <c>ClassDeclaration</c> or a <c>ClassExpression</c>; the port's <c>IClassLikeDeclarationBase</c>
    /// slot cannot instantiate that generic (the type argument must be the <see cref="Node"/> class the constraint
    /// names), so the kind test below supplies it — the same <c>isClassDeclaration</c> arm the reference's own callers
    /// take when they cast the answer back.</remarks>
    private IClassLikeDeclarationBase InjectNamedEvaluationHelperBlock(IClassLikeDeclarationBase node, Expression assignedName)
        => node.Kind == SyntaxKind.ClassDeclaration
            ? NamedEvaluation.InjectClassNamedEvaluationHelperBlockIfMissing(_context, (ClassDeclaration)node, assignedName)
            : NamedEvaluation.InjectClassNamedEvaluationHelperBlockIfMissing(_context, (ClassExpression)node, assignedName);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1049-1052 — <c>isDecoratedClassLike</c>.</summary>
    private static bool IsDecoratedClassLike(Node node)
        => Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ false, node)
            || Utilities.ChildIsDecorated(/*useLegacyDecorators*/ false, node);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1054-1123 — <c>visitClassDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>singleOrMany(statements)</c> is the reference's own expression (one
    /// statement → the statement, otherwise the array), which the visitor slot's <c>object?</c> carries — the same
    /// treatment <c>ES2015Transform.Classes.cs:31</c> records.</remarks>
    private object? VisitClassDeclaration(ClassDeclaration node)
    {
        if (IsDecoratedClassLike(node))
        {
            var statements = new List<IStatement>();
            var originalClass = (IClassLikeDeclarationBase?)Utilities.GetOriginalNode(node, Utilities.IsClassLike) ?? node;
            var className = originalClass.Name is not null ? _factory.CreateStringLiteralFromNode(originalClass.Name) : _factory.CreateStringLiteral("default");
            var isExport = Utilities.HasSyntacticModifier(node, ModifierFlags.Export);
            var isDefault = Utilities.HasSyntacticModifier(node, ModifierFlags.Default);
            if (node.Name is null)
            {
                node = (ClassDeclaration)InjectNamedEvaluationHelperBlock(node, className);
            }
            if (isExport && isDefault)
            {
                var iife = TransformClassLike(node);
                if (node.Name is not null)
                {
                    //  let C = (() => { ... })();
                    //  export default C;
                    var varDecl = _factory.CreateVariableDeclaration(TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null), /*exclamationToken*/ null, /*type*/ null, iife);
                    NodeFactory.SetOriginalNode(varDecl, node);

                    var varDecls = _factory.CreateVariableDeclarationList([varDecl], NodeFlags.Let);
                    var varStatement = _factory.CreateVariableStatement(/*modifiers*/ null, varDecls);
                    statements.Add(varStatement);

                    var exportStatement = _factory.CreateExportDefault(TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null));
                    NodeFactory.SetOriginalNode(exportStatement, node);
                    EmitNodes.SetCommentRange(exportStatement, EmitNodes.GetCommentRange(node));
                    EmitNodes.SetSourceMapRange(exportStatement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(node)));
                    statements.Add(exportStatement);
                }
                else
                {
                    //  export default (() => { ... })();
                    var exportStatement = _factory.CreateExportDefault(iife);
                    NodeFactory.SetOriginalNode(exportStatement, node);
                    EmitNodes.SetCommentRange(exportStatement, EmitNodes.GetCommentRange(node));
                    EmitNodes.SetSourceMapRange(exportStatement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(node)));
                    statements.Add(exportStatement);
                }
            }
            else
            {
                //  let C = (() => { ... })();
                Debug.AssertIsDefined(node.Name, "A class declaration that is not a default export must have a name.");
                var iife = TransformClassLike(node);
                Func<Node, object?> modifierVisitorNoExport = isExport
                    ? node => NodeTests.IsExportModifier(node)
                        ? null
                        : ModifierVisitor(node)
                    : ModifierVisitor;
                var modifiers = Utilities.VisitNodes(node.Modifiers, modifierVisitorNoExport, Utilities.IsModifier);
                var declName = TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true);
                var varDecl = _factory.CreateVariableDeclaration(declName, /*exclamationToken*/ null, /*type*/ null, iife);
                NodeFactory.SetOriginalNode(varDecl, node);

                var varDecls = _factory.CreateVariableDeclarationList([varDecl], NodeFlags.Let);
                var varStatement = _factory.CreateVariableStatement(modifiers, varDecls);
                NodeFactory.SetOriginalNode(varStatement, node);
                EmitNodes.SetCommentRange(varStatement, EmitNodes.GetCommentRange(node));
                statements.Add(varStatement);
                if (isExport)
                {
                    //  export { C };
                    var exportStatement = _factory.CreateExternalModuleExport(declName);
                    NodeFactory.SetOriginalNode(exportStatement, node);
                    statements.Add(exportStatement);
                }
            }
            return statements.Count == 1 ? statements[0] : statements;
        }
        else
        {
            var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
            var heritageClauses = Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause);
            EnterClass(/*classInfo*/ null);
            var members = Utilities.VisitNodes(node.Members, ClassElementVisitor, Utilities.IsClassElement);
            ExitClass();
            return _factory.UpdateClassDeclaration(node, modifiers, node.Name, /*typeParameters*/ null, heritageClauses, members);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1125-1139 — <c>visitClassExpression</c>.</summary>
    private object? VisitClassExpression(ClassExpression node)
    {
        if (IsDecoratedClassLike(node))
        {
            var iife = TransformClassLike(node);
            NodeFactory.SetOriginalNode(iife, node);
            return iife;
        }
        else
        {
            var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
            var heritageClauses = Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause);
            EnterClass(/*classInfo*/ null);
            var members = Utilities.VisitNodes(node.Members, ClassElementVisitor, Utilities.IsClassElement);
            ExitClass();
            return _factory.UpdateClassExpression(node, modifiers, (Identifier?)node.Name, /*typeParameters*/ null, heritageClauses, members);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // esDecorators.ts:1141-1234 — the constructor and the class-element finish
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1141-1160 — <c>prepareConstructor</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>_parent</c> parameter is unused; it is kept so the two call sites read
    /// like the reference's.</remarks>
    private List<IStatement>? PrepareConstructor(IClassLikeDeclarationBase parent, ClassInfo classInfo)
    {
        // Decorated instance members can add "extra" initializers to the instance. If a class contains any instance
        // fields, we'll inject the `__runInitializers()` call for these extra initializers into the initializer of
        // the first class member that will be initialized. However, if the class does not contain any fields that
        // we can piggyback on, we need to synthesize a `__runInitializers()` call in the constructor instead.
        if (Core.Some(classInfo.PendingInstanceInitializers))
        {
            // If there are instance extra initializers we need to add them to the body along with any
            // field initializers
            List<IStatement> statements =
            [
                _factory.CreateExpressionStatement(
                    _factory.InlineExpressions(classInfo.PendingInstanceInitializers)),
            ];

            classInfo.PendingInstanceInitializers = null;
            return statements;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1162-1193 — <c>transformConstructorBodyWorker</c>.</summary>
    private void TransformConstructorBodyWorker(List<IStatement> statementsOut, NodeArray<IStatement> statementsIn, int statementOffset, IReadOnlyList<int> superPath, int superPathDepth, IReadOnlyList<IStatement> initializerStatements)
    {
        var superStatementIndex = superPath[superPathDepth];
        var superStatement = statementsIn[superStatementIndex];
        statementsOut.AddRange(Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, statementOffset, superStatementIndex - statementOffset)!);
        if (NodeTests.IsTryStatement((Node)superStatement))
        {
            var tryBlockStatements = new List<IStatement>();

            TransformConstructorBodyWorker(
                tryBlockStatements,
                ((TryStatement)(Node)superStatement).TryBlock!.Statements!,
                /*statementOffset*/ 0,
                superPath,
                superPathDepth + 1,
                initializerStatements);

            var tryBlockStatementsArray = _factory.CreateNodeArray(tryBlockStatements, /*hasTrailingComma*/ null);
            Utilities.SetTextRange(tryBlockStatementsArray, ((TryStatement)(Node)superStatement).TryBlock!.Statements!);

            statementsOut.Add(_factory.UpdateTryStatement(
                (TryStatement)superStatement,
                _factory.UpdateBlock(((TryStatement)(Node)superStatement).TryBlock, tryBlockStatements),
                Utilities.VisitNode(((TryStatement)(Node)superStatement).CatchClause, Visitor, NodeTests.IsCatchClause),
                Utilities.VisitNode(((TryStatement)(Node)superStatement).FinallyBlock, Visitor, NodeTests.IsBlock)));
        }
        else
        {
            statementsOut.AddRange(Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, superStatementIndex, 1)!);
            statementsOut.AddRange(initializerStatements);
        }
        statementsOut.AddRange(Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, superStatementIndex + 1)!);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1195-1224 — <c>visitConstructorDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>copyPrologue</c> is <see cref="NodeFactory.CopyPrologue"/> and
    /// <c>findSuperStatementIndexPath</c> is <see cref="TransformationUtilities.FindSuperStatementIndexPath"/> — the
    /// same two ports the landed <c>TypeScriptTransform</c> uses for its reference call.</remarks>
    private ConstructorDeclaration VisitConstructorDeclaration(ConstructorDeclaration node)
    {
        EnterClassElement(node);
        var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
        var parameters = Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter);
        Block? body = null;
        if (node.Body is not null && _classInfo is not null)
        {
            // If there are instance extra initializers we need to add them to the body along with any
            // field initializers
            var initializerStatements = PrepareConstructor(_classInfo.Class, _classInfo);
            if (initializerStatements is not null)
            {
                var statements = new List<IStatement>();
                var nonPrologueStart = _factory.CopyPrologue(node.Body.Statements, statements, /*ensureUseStrict*/ false, Visitor);
                var superStatementIndices = TransformationUtilities.FindSuperStatementIndexPath(node.Body.Statements!, nonPrologueStart);
                if (superStatementIndices.Count > 0)
                {
                    TransformConstructorBodyWorker(statements, node.Body.Statements!, nonPrologueStart, superStatementIndices, 0, initializerStatements);
                }
                else
                {
                    statements.AddRange(initializerStatements);
                    statements.AddRange(Utilities.VisitNodes(node.Body.Statements, Visitor, Utilities.IsStatement)!);
                }
                body = _factory.CreateBlock(statements, /*multiLine*/ true);
                NodeFactory.SetOriginalNode(body, node.Body);
                Utilities.SetTextRange(body, node.Body);
            }
        }

        body ??= Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock);
        ExitClassElement();
        return _factory.UpdateConstructorDeclaration(node, modifiers, parameters, body);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1226-1234 — <c>finishClassElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>updated !== original</c> is a node-identity test, so it is
    /// <see cref="object.ReferenceEquals(object?, object?)"/> here (the model's nodes are classes, not value types).</remarks>
    private static Node FinishClassElement(Node updated, Node original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitNodes.SetCommentRange(updated, original);
            EmitNodes.SetSourceMapRange(updated, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(original)));
        }
        return updated;
    }

    /// <summary>The reference's <c>{ modifiers, referencedName, name, initializersName, extraInitializersName,
    /// descriptorName, thisArg }</c> object literal of <c>partialTransformClassElement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference returns a fresh object literal from each of the three return sites, two of
    /// which leave the same slots undefined; the port states the one shared shape.</remarks>
    private sealed class PartialClassElementResult
    {
        /// <summary>The `{ modifiers, name, descriptorName }` destructuring of the three descriptor forwarders.</summary>
        public void Deconstruct(out NodeArray<Node>? modifiers, out Node? name, out Identifier? descriptorName)
        {
            modifiers = Modifiers;
            name = Name;
            descriptorName = DescriptorName;
        }

        /// <summary>The `{ modifiers, name, initializersName, extraInitializersName, descriptorName, thisArg }`
        /// destructuring of <c>visitPropertyDeclaration</c>.</summary>
        public void Deconstruct(out NodeArray<Node>? modifiers, out Node? name, out Identifier? initializersName, out Identifier? extraInitializersName, out Identifier? descriptorName, out Identifier? thisArg)
        {
            modifiers = Modifiers;
            name = Name;
            initializersName = InitializersName;
            extraInitializersName = ExtraInitializersName;
            descriptorName = DescriptorName;
            thisArg = ThisArg;
        }

        public NodeArray<Node>? Modifiers { get; init; }

        public Expression? ReferencedName { get; init; }

        public Node? Name { get; init; }

        public Identifier? InitializersName { get; init; }

        public Identifier? ExtraInitializersName { get; init; }

        public Identifier? DescriptorName { get; init; }

        public Identifier? ThisArg { get; init; }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1236-1408 — <c>partialTransformClassElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the generic): TS's <c>TNode extends MethodDeclaration | PropertyDeclaration |
    /// GetAccessorDeclaration | SetAccessorDeclaration</c> and its <c>createDescriptor</c> parameter's refined
    /// <c>TNode &amp; { readonly name: PrivateIdentifier }</c> collapse to the member's own model class here, because the
    /// refinement has no model counterpart (the same collapse <c>ES2015Transform.Classes.cs</c> records for the
    /// <c>ThisCapturing*</c> aliases); the callback is only ever invoked after
    /// <c>isPrivateIdentifierClassElementDeclaration(member)</c>, so each implementation reads its member's name as a
    /// <see cref="PrivateIdentifier"/> exactly as the reference's refinement licenses.
    /// <para>
    /// PORT-NOTE (the <c>??=</c> pairs): the reference's <c>classInfo.memberInfos ??= new Map()</c> plus
    /// <c>set(member, memberInfo)</c> are the ordered <see cref="ClassInfo.MemberInfos"/> list's lazy creation and
    /// append, and its four <c>classInfo.*DecorationStatements ??= []</c> arms are the same slots' lazy creation.
    /// </para>
    /// </remarks>
    private PartialClassElementResult PartialTransformClassElement<TNode>(
        TNode member,
        ClassInfo? classInfo,
        Func<TNode, NodeArray<Node>?, Expression>? createDescriptor = null)
        where TNode : Node
    {
        Expression? referencedName = null;
        Node? name = null;
        Identifier? initializersName = null;
        Identifier? extraInitializersName = null;
        Identifier? thisArg = null;
        Identifier? descriptorName = null;
        if (classInfo is null)
        {
            var modifiers = Utilities.VisitNodes(ElementModifiers(member), ModifierVisitor, Utilities.IsModifier);
            EnterName();
            name = VisitPropertyName(((IClassElement)member).Name!);
            ExitName();
            return new PartialClassElementResult { Modifiers = modifiers, ReferencedName = referencedName, Name = name, InitializersName = initializersName, DescriptorName = descriptorName, ThisArg = thisArg };
        }

        // Member decorators require privileged access to private names. However, computed property
        // evaluation occurs interspersed with decorator evaluation. This means that if we encounter
        // a computed property name we must inline decorator evaluation.
        var memberDecorators = TransformAllDecoratorsOfDeclaration(TransformationUtilities.GetAllDecoratorsOfClassElement((IClassElement)member, (Node)classInfo.Class, /*useLegacyDecorators*/ false));
        var memberModifiers = Utilities.VisitNodes(ElementModifiers(member), ModifierVisitor, Utilities.IsModifier);

        if (memberDecorators is not null)
        {
            var memberDecoratorsName = CreateHelperVariable(member, "decorators");
            var memberDecoratorsArray = _factory.CreateArrayLiteralExpression(memberDecorators, /*multiLine*/ null);
            var memberDecoratorsAssignment = _factory.CreateAssignment(memberDecoratorsName, memberDecoratorsArray);
            var memberInfo = new MemberInfo { MemberDecoratorsName = memberDecoratorsName };
            classInfo.MemberInfos ??= [];
            classInfo.MemberInfos.Add(new MemberInfoEntry((IClassElement)member, memberInfo));
            _pendingExpressions ??= [];
            _pendingExpressions.Add(memberDecoratorsAssignment);

            // 5. Static non-field (method/getter/setter/auto-accessor) element decorators are applied
            // 6. Non-static non-field (method/getter/setter/auto-accessor) element decorators are applied
            // 7. Static field (excl. auto-accessor) element decorators are applied
            // 8. Non-static field (excl. auto-accessor) element decorators are applied
            var statements = Utilities.IsMethodOrAccessor(member) || Utilities.IsAutoAccessorPropertyDeclaration(member)
                ? Utilities.IsStatic(member)
                    ? classInfo.StaticNonFieldDecorationStatements ??= []
                    : classInfo.NonStaticNonFieldDecorationStatements ??= []
                : NodeTests.IsPropertyDeclaration(member) && !Utilities.IsAutoAccessorPropertyDeclaration(member)
                    ? Utilities.IsStatic(member)
                        ? classInfo.StaticFieldDecorationStatements ??= []
                        : classInfo.NonStaticFieldDecorationStatements ??= []
                    : Debug.Fail<List<IStatement>>();

            var kind = Utilities.IsGetAccessor(member) ? "getter" :
                Utilities.IsSetAccessor(member) ? "setter" :
                NodeTests.IsMethodDeclaration(member) ? "method" :
                Utilities.IsAutoAccessorPropertyDeclaration(member) ? "accessor" :
                NodeTests.IsPropertyDeclaration(member) ? "field" :
                Debug.Fail<string>();

            ESDecorateName propertyName;
            var memberName = ((IClassElement)member).Name!;
            if (NodeTests.IsIdentifier(memberName) || NodeTests.IsPrivateIdentifier(memberName))
            {
                propertyName = new ESDecorateName(computed: false, memberName);
            }
            else if (Utilities.IsPropertyNameLiteral(memberName))
            {
                propertyName = new ESDecorateName(computed: true, _factory.CreateStringLiteralFromNode(memberName));
            }
            else
            {
                var expression = ((ComputedPropertyName)memberName).Expression!;
                if (Utilities.IsPropertyNameLiteral(expression) && !NodeTests.IsIdentifier(expression))
                {
                    propertyName = new ESDecorateName(computed: true, _factory.CreateStringLiteralFromNode(expression));
                }
                else
                {
                    EnterName();
                    (referencedName, name) = VisitReferencedPropertyName(memberName);
                    propertyName = new ESDecorateName(computed: true, referencedName);
                    ExitName();
                }
            }

            var context = new ESDecorateClassElementContext(
                kind,
                propertyName,
                Utilities.IsStatic(member),
                NodeTests.IsPrivateIdentifier(memberName),
                new ESDecorateClassElementAccess(
                    // 15.7.3 CreateDecoratorAccessObject (kind, name)
                    // 2. If _kind_ is ~field~, ~method~, ~accessor~, or ~getter~, then ...
                    NodeTests.IsPropertyDeclaration(member) || Utilities.IsGetAccessor(member) || NodeTests.IsMethodDeclaration(member),
                    // 3. If _kind_ is ~field~, ~accessor~, or ~setter~, then ...
                    NodeTests.IsPropertyDeclaration(member) || Utilities.IsSetAccessor(member)),
                classInfo.MetadataReference);

            if (Utilities.IsMethodOrAccessor(member))
            {
                // produces (public elements):
                //  __esDecorate(this, null, _static_member_decorators, { kind: "method", name: "...", static: true, private: false, access: { ... } }, _staticExtraInitializers);
                //  __esDecorate(this, null, _member_decorators, { kind: "method", name: "...", static: false, private: false, access: { ... } }, _instanceExtraInitializers);
                //  __esDecorate(this, null, _static_member_decorators, { kind: "getter", name: "...", static: true, private: false, access: { ... } }, _staticExtraInitializers);
                //  __esDecorate(this, null, _member_decorators, { kind: "getter", name: "...", static: false, private: false, access: { ... } }, _instanceExtraInitializers);
                //  __esDecorate(this, null, _static_member_decorators, { kind: "setter", name: "...", static: true, private: false, access: { ... } }, _staticExtraInitializers);
                //  __esDecorate(this, null, _member_decorators, { kind: "setter", name: "...", static: false, private: false, access: { ... } }, _instanceExtraInitializers);
                //
                // produces (private elements):
                //  __esDecorate(this, _static_member_descriptor = { value() { ... } }, _static_member_decorators, { kind: "method", name: "...", static: true, private: true, access: { ... } }, _staticExtraInitializers);
                //  __esDecorate(this, _member_descriptor = { value() { ... } }, _member_decorators, { kind: "method", name: "...", static: false, private: true, access: { ... } }, _instanceExtraInitializers);
                //  __esDecorate(this, _static_member_descriptor = { get() { ... } }, _static_member_decorators, { kind: "getter", name: "...", static: true, private: true, access: { ... } }, _staticExtraInitializers);
                //  __esDecorate(this, _member_descriptor = { get() { ... } }, _member_decorators, { kind: "getter", name: "...", static: false, private: true, access: { ... } }, _instanceExtraInitializers);
                //  __esDecorate(this, _static_member_descriptor = { set() { ... } }, _static_member_decorators, { kind: "setter", name: "...", static: true, private: true, access: { ... } }, _staticExtraInitializers);
                //  __esDecorate(this, _member_descriptor = { set() { ... } }, _member_decorators, { kind: "setter", name: "...", static: false, private: true, access: { ... } }, _instanceExtraInitializers);

                var methodExtraInitializersName = Utilities.IsStatic(member) ? classInfo.StaticMethodExtraInitializersName : classInfo.InstanceMethodExtraInitializersName;
                Debug.AssertIsDefined(methodExtraInitializersName);

                Expression? descriptor = null;
                if (Utilities.IsPrivateIdentifierClassElementDeclaration(member) && createDescriptor is not null)
                {
                    descriptor = createDescriptor(member, Utilities.VisitNodes(memberModifiers, static node => NodeTests.IsAsyncModifier(node) ? node : null, Utilities.IsModifier));
                    memberInfo.MemberDescriptorName = descriptorName = CreateHelperVariable(member, "descriptor");
                    descriptor = _factory.CreateAssignment(descriptorName, descriptor);
                }

                var esDecorateExpression = _emitHelpers.CreateESDecorateHelper(_factory.CreateThis(), descriptor ?? _factory.CreateNull(), memberDecoratorsName, context, _factory.CreateNull(), methodExtraInitializersName);
                var esDecorateStatement = _factory.CreateExpressionStatement(esDecorateExpression);
                EmitNodes.SetSourceMapRange(esDecorateStatement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(member)));
                statements.Add(esDecorateStatement);
            }
            else if (NodeTests.IsPropertyDeclaration(member))
            {
                initializersName = memberInfo.MemberInitializersName ??= CreateHelperVariable(member, "initializers");
                extraInitializersName = memberInfo.MemberExtraInitializersName ??= CreateHelperVariable(member, "extraInitializers");

                if (Utilities.IsStatic(member))
                {
                    thisArg = classInfo.ClassThis;
                }

                Expression? descriptor = null;
                if (Utilities.IsPrivateIdentifierClassElementDeclaration(member) && Utilities.HasAccessorModifier(member) && createDescriptor is not null)
                {
                    descriptor = createDescriptor(member, /*modifiers*/ null);
                    memberInfo.MemberDescriptorName = descriptorName = CreateHelperVariable(member, "descriptor");
                    descriptor = _factory.CreateAssignment(descriptorName, descriptor);
                }

                // produces:
                //  _static_field_initializers = __esDecorate(null, null, _static_member_decorators, { kind: "field", name: "...", static: true, private: ..., access: { ... } }, _staticExtraInitializers);
                //  _field_initializers = __esDecorate(null, null, _member_decorators, { kind: "field", name: "...", static: false, private: ..., access: { ... } }, _instanceExtraInitializers);
                var esDecorateExpression = _emitHelpers.CreateESDecorateHelper(
                    Utilities.IsAutoAccessorPropertyDeclaration(member)
                        ? _factory.CreateThis()
                        : _factory.CreateNull(),
                    descriptor ?? _factory.CreateNull(),
                    memberDecoratorsName,
                    context,
                    initializersName,
                    extraInitializersName);
                var esDecorateStatement = _factory.CreateExpressionStatement(esDecorateExpression);
                EmitNodes.SetSourceMapRange(esDecorateStatement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(member)));
                statements.Add(esDecorateStatement);
            }
        }

        if (name is null)
        {
            EnterName();
            name = VisitPropertyName(((IClassElement)member).Name!);
            ExitName();
        }

        if (!Core.Some(memberModifiers) && (NodeTests.IsMethodDeclaration(member) || NodeTests.IsPropertyDeclaration(member)))
        {
            // Don't emit leading comments on the name for methods and properties without modifiers, otherwise we
            // will end up printing duplicate comments.
            EmitNodes.SetEmitFlags(name!, EmitFlags.NoLeadingComments);
        }

        return new PartialClassElementResult { Modifiers = memberModifiers, ReferencedName = referencedName, Name = name, InitializersName = initializersName, ExtraInitializersName = extraInitializersName, DescriptorName = descriptorName, ThisArg = thisArg };
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1410-1423 — <c>visitMethodDeclaration</c>.</summary>
    private object? VisitMethodDeclaration(MethodDeclaration node)
    {
        EnterClassElement(node);
        var (modifiers, name, descriptorName) = PartialTransformClassElement(node, _classInfo, CreateMethodDescriptorObject);
        if (descriptorName is not null)
        {
            ExitClassElement();
            return FinishClassElement(CreateMethodDescriptorForwarder(modifiers, name, descriptorName), node);
        }
        else
        {
            var parameters = Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter);
            var body = Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock);
            ExitClassElement();
            return FinishClassElement(_factory.UpdateMethodDeclaration(node, modifiers, node.AsteriskToken, name, /*questionToken*/ null, /*typeParameters*/ null, parameters, /*type*/ null, body), node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1425-1438 — <c>visitGetAccessorDeclaration</c>.</summary>
    private object? VisitGetAccessorDeclaration(GetAccessorDeclaration node)
    {
        EnterClassElement(node);
        var (modifiers, name, descriptorName) = PartialTransformClassElement(node, _classInfo, CreateGetAccessorDescriptorObject);
        if (descriptorName is not null)
        {
            ExitClassElement();
            return FinishClassElement(CreateGetAccessorDescriptorForwarder(modifiers, name, descriptorName), node);
        }
        else
        {
            var parameters = Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter);
            var body = Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock);
            ExitClassElement();
            return FinishClassElement(_factory.UpdateGetAccessorDeclaration(node, modifiers, name, parameters, /*type*/ null, body), node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1440-1453 — <c>visitSetAccessorDeclaration</c>.</summary>
    private object? VisitSetAccessorDeclaration(SetAccessorDeclaration node)
    {
        EnterClassElement(node);
        var (modifiers, name, descriptorName) = PartialTransformClassElement(node, _classInfo, CreateSetAccessorDescriptorObject);
        if (descriptorName is not null)
        {
            ExitClassElement();
            return FinishClassElement(CreateSetAccessorDescriptorForwarder(modifiers, name, descriptorName), node);
        }
        else
        {
            var parameters = Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter);
            var body = Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock);
            ExitClassElement();
            return FinishClassElement(_factory.UpdateSetAccessorDeclaration(node, modifiers, name, parameters, body), node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1455-1493 — <c>visitClassStaticBlockDeclaration</c>.</summary>
    private object? VisitClassStaticBlockDeclaration(ClassStaticBlockDeclaration node)
    {
        EnterClassElement(node);

        object? result;
        if (NamedEvaluation.IsClassNamedEvaluationHelperBlock(node))
        {
            result = Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
        else if (ClassThis.IsClassThisAssignmentBlock(node))
        {
            var savedClassThis = _classThis;
            _classThis = null;
            result = Utilities.VisitEachChildWorker(node, Visitor, _context);
            _classThis = savedClassThis;
        }
        else
        {
            node = Utilities.VisitEachChildWorker(node, Visitor, _context);
            result = node;
            if (_classInfo is not null)
            {
                _classInfo.HasStaticInitializers = true;
                if (Core.Some(_classInfo.PendingStaticInitializers))
                {
                    // If we tried to inject the pending initializers into the current block, we might run into
                    // variable name collisions due to sharing this blocks scope. To avoid this, we inject a new
                    // static block that contains the pending initializers that precedes this block.
                    var statements = new List<IStatement>();
                    foreach (var initializer in _classInfo.PendingStaticInitializers!)
                    {
                        var initializerStatement = _factory.CreateExpressionStatement(initializer);
                        EmitNodes.SetSourceMapRange(initializerStatement, TypeScriptTransformSupport.SourceMapRangeOf(EmitNodes.GetSourceMapRange(initializer)));
                        statements.Add(initializerStatement);
                    }
                    var body = _factory.CreateBlock(statements, /*multiLine*/ true);
                    var staticBlock = _factory.CreateClassStaticBlockDeclaration(body);
                    result = new List<Node> { staticBlock, node };
                    _classInfo.PendingStaticInitializers = null;
                }
            }
        }

        ExitClassElement();
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:1495-1628 — <c>visitPropertyDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's array answer (<c>[backingField, getter, setter]</c>) is the port's
    /// <c>List&lt;Node&gt;</c>, which is the shape the visitor slot's <c>object?</c> discriminates.</remarks>
    private object? VisitPropertyDeclaration(PropertyDeclaration node)
    {
        if (Utilities.IsNamedEvaluation(node, IsAnonymousClassNeedingAssignedName))
        {
            node = (PropertyDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, node, CanIgnoreEmptyStringLiteralInAssignedName(node.Initializer));
        }

        EnterClassElement(node);

        // TODO(rbuckton): We support decorating `declare x` fields with legacyDecorators, but we currently don't
        //                 support them with esDecorators. We need to consider whether we will support them in the
        //                 future, and how. For now, these should be elided by the `ts` transform.
        Debug.Assert(!Utilities.IsAmbientPropertyDeclaration(node), "Not yet implemented.");

        // 10.2.1.3 RS: EvaluateBody
        //   Initializer : `=` AssignmentExpression
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |AssignmentExpression| with argument _functionObject_.[[ClassFieldInitializerName]].
        //     ...

        Func<PropertyDeclaration, NodeArray<Node>?, Expression>? createDescriptor = Utilities.HasAccessorModifier(node) ? CreateAccessorPropertyDescriptorObject : null;
        var (modifiers, name, initializersName, extraInitializersName, descriptorName, thisArg) = PartialTransformClassElement(node, _classInfo, createDescriptor);

        _context.StartLexicalEnvironment();

        Expression? initializer = Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression);
        if (initializersName is not null)
        {
            initializer = _emitHelpers.CreateRunInitializersHelper(
                thisArg is not null ? thisArg : _factory.CreateThis(),
                initializersName,
                initializer is not null ? initializer : _factory.CreateVoidZero());
        }

        if (Utilities.IsStatic(node) && _classInfo is not null && initializer is not null)
        {
            _classInfo.HasStaticInitializers = true;
        }

        var declarations = _context.EndLexicalEnvironment();
        if (Core.Some(declarations))
        {
            var statements = new List<IStatement>(declarations!);
            statements.Add(_factory.CreateReturnStatement(initializer));
            initializer = _factory.CreateImmediatelyInvokedArrowFunction(statements);
        }

        if (_classInfo is not null)
        {
            if (Utilities.IsStatic(node))
            {
                initializer = InjectPendingInitializers(_classInfo, /*isStatic*/ true, initializer);
                if (extraInitializersName is not null)
                {
                    _classInfo.PendingStaticInitializers ??= [];
                    _classInfo.PendingStaticInitializers.Add(
                        _emitHelpers.CreateRunInitializersHelper(
                            _classInfo.ClassThis is not null ? _classInfo.ClassThis : _factory.CreateThis(),
                            extraInitializersName,
                            /*value*/ null));
                }
            }
            else
            {
                initializer = InjectPendingInitializers(_classInfo, /*isStatic*/ false, initializer);
                if (extraInitializersName is not null)
                {
                    _classInfo.PendingInstanceInitializers ??= [];
                    _classInfo.PendingInstanceInitializers.Add(
                        _emitHelpers.CreateRunInitializersHelper(
                            _factory.CreateThis(),
                            extraInitializersName,
                            /*value*/ null));
                }
            }
        }

        ExitClassElement();

        if (Utilities.HasAccessorModifier(node) && descriptorName is not null)
        {
            // given:
            //  accessor #x = 1;
            //
            // emits:
            //  static {
            //      _esDecorate(null, _private_x_descriptor = { get() { return this.#x_1; }, set(value) { this.#x_1 = value; } }, ...)
            //  }
            //  ...
            //  #x_1 = 1;
            //  get #x() { return _private_x_descriptor.get.call(this); }
            //  set #x(value) { _private_x_descriptor.set.call(this, value); }

            var commentRange = EmitNodes.GetCommentRange(node);
            var sourceMapRange = EmitNodes.GetSourceMapRange(node);

            // Since we're creating two declarations where there was previously one, cache
            // the expression for any computed property names.
            var nodeName = node.Name!;
            var getterName = nodeName;
            var setterName = nodeName;
            if (NodeTests.IsComputedPropertyName(nodeName) && !TransformationUtilities.IsSimpleInlineableExpression(((ComputedPropertyName)nodeName).Expression!))
            {
                var cacheAssignment = FactoryUtilities.FindComputedPropertyNameCacheAssignment((ComputedPropertyName)nodeName);
                if (cacheAssignment is not null)
                {
                    getterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)nodeName, Utilities.VisitNode(((ComputedPropertyName)nodeName).Expression, Visitor, Utilities.IsExpression));
                    setterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)nodeName, cacheAssignment.Left);
                }
                else
                {
                    var temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);
                    EmitNodes.SetSourceMapRange(temp, TypeScriptTransformSupport.SourceMapRangeOf(((ComputedPropertyName)nodeName).Expression!));
                    var expression = Utilities.VisitNode(((ComputedPropertyName)nodeName).Expression, Visitor, Utilities.IsExpression);
                    var assignment = _factory.CreateAssignment(temp, expression);
                    EmitNodes.SetSourceMapRange(assignment, TypeScriptTransformSupport.SourceMapRangeOf(((ComputedPropertyName)nodeName).Expression!));
                    getterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)nodeName, assignment);
                    setterName = _factory.UpdateComputedPropertyName((ComputedPropertyName)nodeName, temp);
                }
            }

            var modifiersWithoutAccessor = Utilities.VisitNodes(modifiers, static node => node.Kind != SyntaxKind.AccessorKeyword ? node : null, Utilities.IsModifier);

            var backingField = FactoryUtilities.CreateAccessorPropertyBackingField(_factory, node, modifiersWithoutAccessor, initializer);
            NodeFactory.SetOriginalNode(backingField, node);
            EmitNodes.SetEmitFlags(backingField, EmitFlags.NoComments);
            EmitNodes.SetSourceMapRange(backingField, TypeScriptTransformSupport.SourceMapRangeOf(sourceMapRange));
            EmitNodes.SetSourceMapRange(backingField.Name!, TypeScriptTransformSupport.SourceMapRangeOf(node.Name!));

            var getter = CreateGetAccessorDescriptorForwarder(modifiersWithoutAccessor, getterName, descriptorName);
            NodeFactory.SetOriginalNode(getter, node);
            EmitNodes.SetCommentRange(getter, commentRange);
            EmitNodes.SetSourceMapRange(getter, TypeScriptTransformSupport.SourceMapRangeOf(sourceMapRange));

            var setter = CreateSetAccessorDescriptorForwarder(modifiersWithoutAccessor, setterName, descriptorName);
            NodeFactory.SetOriginalNode(setter, node);
            EmitNodes.SetEmitFlags(setter, EmitFlags.NoComments);
            EmitNodes.SetSourceMapRange(setter, TypeScriptTransformSupport.SourceMapRangeOf(sourceMapRange));

            return new List<Node> { backingField, getter, setter };
        }

        return FinishClassElement(_factory.UpdatePropertyDeclaration(node, modifiers, name, /*questionOrExclamationToken*/ null, /*type*/ null, initializer), node);
    }

    // ---------------------------------------------------------------------------------------------
    // esDecorators.ts:2276-2533 — the descriptor, metadata and forwarder factories
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2276-2301 — <c>createDescriptorMethod</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>parameters: readonly ParameterDeclaration[]</c> is the
    /// <c>NodeArray&lt;ParameterDeclaration&gt;?</c> its callers hand it; the modifiers are narrowed to
    /// <c>List&lt;ModifierToken&gt;?</c> for the function-expression builder by <see cref="AsModifierTokens"/>.</remarks>
    private PropertyAssignment CreateDescriptorMethod(
        Node original,
        PrivateIdentifier name,
        NodeArray<Node>? modifiers,
        PunctuationToken? asteriskToken,
        string kind,
        NodeArray<ParameterDeclaration>? parameters,
        Block? body)
    {
        var func = _factory.CreateFunctionExpression(
            AsModifierTokens(modifiers),
            asteriskToken,
            /*name*/ null,
            /*typeParameters*/ null,
            parameters,
            /*type*/ null,
            body ?? _factory.CreateBlock(/*statements*/ null, /*multiLine*/ null));
        NodeFactory.SetOriginalNode(func, original);
        EmitNodes.SetSourceMapRange(func, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(original)));
        EmitNodes.SetEmitFlags(func, EmitFlags.NoComments);

        var prefix = kind is "get" or "set" ? kind : null;
        var functionName = _factory.CreateStringLiteralFromNode(name, /*isSingleQuote*/ null);
        var namedFunction = _emitHelpers.CreateSetFunctionNameHelper(func, functionName, prefix);
        var method = _factory.CreatePropertyAssignment(_factory.CreateIdentifier(kind), namedFunction);
        NodeFactory.SetOriginalNode(method, original);
        EmitNodes.SetSourceMapRange(method, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(original)));
        EmitNodes.SetEmitFlags(method, EmitFlags.NoComments);
        return method;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2303-2318 — <c>createMethodDescriptorObject</c>.</summary>
    private ObjectLiteralExpression CreateMethodDescriptorObject(MethodDeclaration node, NodeArray<Node>? modifiers)
        => _factory.CreateObjectLiteralExpression(
        [
            CreateDescriptorMethod(
                node,
                (PrivateIdentifier)node.Name!,
                modifiers,
                node.AsteriskToken,
                "value",
                Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
                Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock)),
        ], /*multiLine*/ null);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2320-2335 — <c>createGetAccessorDescriptorObject</c>.</summary>
    private ObjectLiteralExpression CreateGetAccessorDescriptorObject(GetAccessorDeclaration node, NodeArray<Node>? modifiers)
        => _factory.CreateObjectLiteralExpression(
        [
            CreateDescriptorMethod(
                node,
                (PrivateIdentifier)node.Name!,
                modifiers,
                /*asteriskToken*/ null,
                "get",
                /*parameters*/ null,
                Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock)),
        ], /*multiLine*/ null);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2337-2352 — <c>createSetAccessorDescriptorObject</c>.</summary>
    private ObjectLiteralExpression CreateSetAccessorDescriptorObject(SetAccessorDeclaration node, NodeArray<Node>? modifiers)
        => _factory.CreateObjectLiteralExpression(
        [
            CreateDescriptorMethod(
                node,
                (PrivateIdentifier)node.Name!,
                modifiers,
                /*asteriskToken*/ null,
                "set",
                Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
                Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock)),
        ], /*multiLine*/ null);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2354-2407 — <c>createAccessorPropertyDescriptorObject</c>.</summary>
    private ObjectLiteralExpression CreateAccessorPropertyDescriptorObject(PropertyDeclaration node, NodeArray<Node>? modifiers)
    {
        //  {
        //      get() { return this.${privateName}; },
        //      set(value) { this.${privateName} = value; },
        //  }

        return _factory.CreateObjectLiteralExpression(
        [
            CreateDescriptorMethod(
                node,
                (PrivateIdentifier)node.Name!,
                modifiers,
                /*asteriskToken*/ null,
                "get",
                /*parameters*/ null,
                _factory.CreateBlock(
                [
                    _factory.CreateReturnStatement(
                        _factory.CreatePropertyAccessExpression(
                            _factory.CreateThis(),
                            _factory.GetGeneratedPrivateNameForNode(node.Name))),
                ], /*multiLine*/ null)),
            CreateDescriptorMethod(
                node,
                (PrivateIdentifier)node.Name!,
                modifiers,
                /*asteriskToken*/ null,
                "set",
                _factory.CreateNodeArray(
                [
                    _factory.CreateParameterDeclaration(
                        /*modifiers*/ null,
                        /*dotDotDotToken*/ null,
                        "value",
                        /*questionToken*/ null,
                        /*type*/ null,
                        /*initializer*/ null),
                ], /*hasTrailingComma*/ null),
                _factory.CreateBlock(
                [
                    _factory.CreateExpressionStatement(
                        _factory.CreateAssignment(
                            _factory.CreatePropertyAccessExpression(
                                _factory.CreateThis(),
                                _factory.GetGeneratedPrivateNameForNode(node.Name)),
                            _factory.CreateIdentifier("value"))),
                ], /*multiLine*/ null)),
        ], /*multiLine*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2409-2432 — <c>createMethodDescriptorForwarder</c>.</summary>
    private GetAccessorDeclaration CreateMethodDescriptorForwarder(NodeArray<Node>? modifiers, Node? name, Identifier descriptorName)
    {
        // strip off all but the `static` modifier
        var strippedModifiers = Utilities.VisitNodes(modifiers, static node => NodeTests.IsStaticModifier(node) ? node : null, Utilities.IsModifier);
        return _factory.CreateGetAccessorDeclaration(
            strippedModifiers,
            name,
            [],
            /*type*/ null,
            _factory.CreateBlock(
            [
                _factory.CreateReturnStatement(
                    _factory.CreatePropertyAccessExpression(
                        descriptorName,
                        _factory.CreateIdentifier("value"))),
            ], /*multiLine*/ null));
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2434-2461 — <c>createGetAccessorDescriptorForwarder</c>.</summary>
    private GetAccessorDeclaration CreateGetAccessorDescriptorForwarder(NodeArray<Node>? modifiers, Node? name, Identifier descriptorName)
    {
        // strip off all but the `static` modifier
        var strippedModifiers = Utilities.VisitNodes(modifiers, static node => NodeTests.IsStaticModifier(node) ? node : null, Utilities.IsModifier);
        return _factory.CreateGetAccessorDeclaration(
            strippedModifiers,
            name,
            [],
            /*type*/ null,
            _factory.CreateBlock(
            [
                _factory.CreateReturnStatement(
                    _factory.CreateFunctionCallCall(
                        _factory.CreatePropertyAccessExpression(
                            descriptorName,
                            _factory.CreateIdentifier("get")),
                        _factory.CreateThis(),
                        [])),
            ], /*multiLine*/ null));
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2463-2486 — <c>createSetAccessorDescriptorForwarder</c>.</summary>
    private SetAccessorDeclaration CreateSetAccessorDescriptorForwarder(NodeArray<Node>? modifiers, Node? name, Identifier descriptorName)
    {
        // strip off all but the `static` modifier
        var strippedModifiers = Utilities.VisitNodes(modifiers, static node => NodeTests.IsStaticModifier(node) ? node : null, Utilities.IsModifier);
        return _factory.CreateSetAccessorDeclaration(
            strippedModifiers,
            name,
            [
                _factory.CreateParameterDeclaration(
                    /*modifiers*/ null,
                    /*dotDotDotToken*/ null,
                    "value",
                    /*questionToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null),
            ],
            _factory.CreateBlock(
            [
                _factory.CreateReturnStatement(
                    _factory.CreateFunctionCallCall(
                        _factory.CreatePropertyAccessExpression(
                            descriptorName,
                            _factory.CreateIdentifier("set")),
                        _factory.CreateThis(),
                        [_factory.CreateIdentifier("value")])),
            ], /*multiLine*/ null));
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2488-2509 — <c>createMetadata</c>.</summary>
    private VariableStatement CreateMetadata(Identifier name, Identifier? classSuper)
    {
        var varDecl = _factory.CreateVariableDeclaration(
            name,
            /*exclamationToken*/ null,
            /*type*/ null,
            _factory.CreateConditionalExpression(
                _factory.CreateLogicalAnd(
                    _factory.CreateTypeCheck(_factory.CreateIdentifier("Symbol"), "function"),
                    _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Symbol"), "metadata")),
                (PunctuationToken)_factory.CreateToken(SyntaxKind.QuestionToken),
                _factory.CreateCallExpression(
                    _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Object"), "create"),
                    /*typeArguments*/ null,
                    [classSuper is not null ? CreateSymbolMetadataReference(classSuper) : _factory.CreateNull()]),
                (PunctuationToken)_factory.CreateToken(SyntaxKind.ColonToken),
                _factory.CreateVoidZero()));
        return _factory.CreateVariableStatement(/*modifiers*/ null, _factory.CreateVariableDeclarationList([varDecl], NodeFlags.Const));
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2511-2521 — <c>createSymbolMetadata</c>.</summary>
    private IfStatement CreateSymbolMetadata(Expression target, Identifier value)
    {
        var defineProperty = _factory.CreateObjectDefinePropertyCall(
            target,
            _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Symbol"), "metadata"),
            _factory.CreatePropertyDescriptor(
                new PropertyDescriptorAttributes { Configurable = true, Writable = true, Enumerable = true, Value = value },
                /*singleLine*/ true));
        return EmitNodes.SetEmitFlags(
            _factory.CreateIfStatement(value, _factory.CreateExpressionStatement(defineProperty), /*elseStatement*/ null),
            EmitFlags.SingleLine);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:2523-2531 — <c>createSymbolMetadataReference</c>.</summary>
    private BinaryExpression CreateSymbolMetadataReference(Identifier classSuper)
        => _factory.CreateBinaryExpression(
            _factory.CreateElementAccessExpression(
                classSuper,
                _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Symbol"), "metadata")),
            SyntaxKind.QuestionQuestionToken,
            _factory.CreateNull());

    // ---------------------------------------------------------------------------------------------
    // The reference's own `append`/`addRange`/`asModifierTokens`, stated where this file needs them
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/core.ts:1141-1147 — <c>append</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>append&lt;T&gt;(to: T[] | undefined, value: T | undefined): T[] | undefined</c> is
    /// generic over the array's element type with a null-preserving answer; the port states the same body over the
    /// <c>List&lt;T&gt;</c> its arrays are (docs/architecture.md §12.9).</remarks>
    private static List<T>? Append<T>(List<T>? to, T? value)
        where T : class
    {
        if (value is null)
        {
            return to;
        }
        to ??= [];
        to.Add(value);
        return to;
    }

    /// <summary>The <c>Modifiers</c> array of a class element.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ClassElement</c> carries <c>modifiers</c>; the model declares the slot on each
    /// concrete declaration instead (decorators and modifiers share it), so this states the reference's
    /// <c>member.modifiers</c> as the kind test its four member classes are.</remarks>
    private static NodeArray<Node>? ElementModifiers(Node member) => member switch
    {
        MethodDeclaration method => method.Modifiers,
        PropertyDeclaration property => property.Modifiers,
        GetAccessorDeclaration getAccessor => getAccessor.Modifiers,
        SetAccessorDeclaration setAccessor => setAccessor.Modifiers,
        _ => null,
    };

    /// <summary>Narrows a parsed node's modifier array to the <see cref="ModifierToken"/> list the factory's
    /// function-expression builder takes.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>ModifiersArray</c> is <c>Modifier[]</c> there, so
    /// <c>createFunctionExpression</c> accepts the very array <c>visitNodes</c> produced; the port's <c>Modifiers</c>
    /// slot is <c>NodeArray&lt;Node&gt;?</c> (decorators share it), so the elements are projected — the same narrowing
    /// <c>FactoryUtilities.AsModifierTokens</c> performs for the landed parser helpers.</remarks>
    private static List<ModifierToken>? AsModifierTokens(NodeArray<Node>? modifiers)
        => modifiers is null ? null : Core.Map(modifiers, static (modifier, _) => (ModifierToken)modifier);

    /// <summary>ported from: src/compiler/core.ts:1103-1117 — <c>addRange</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="Append{T}"/>; the reference's three-argument overloads do not occur here, so
    /// this is the reference's own two-array body.</remarks>
    private static List<T>? AddRange<T>(List<T>? to, List<T>? from)
        where T : class
    {
        if (from is null)
        {
            return to;
        }
        to ??= [];
        to.AddRange(from);
        return to;
    }
}
