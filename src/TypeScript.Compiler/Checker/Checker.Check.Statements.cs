namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:44781-45054 (the collision checks and convertAutoToAny)
// ported from: src/compiler/checker.ts:45057-45239 (checkVariableLikeDeclaration)
// ported from: src/compiler/checker.ts:45241-45328 (the variable/member declaration checks)
// ported from: src/compiler/checker.ts:45330-45457 (the truthiness helpers)
// ported from: src/compiler/checker.ts:45459-45678 (the statement checks and the for-of entry points)
// ported from: src/compiler/checker.ts:45680-46500 (the iterated-type and iteration-types machinery)
// ported from: src/compiler/checker.ts:46502-46749 (the break/return/with/switch/labeled/throw/try checks)
/// <summary>
/// The statement half of the checker body: the collision checks a declaration name takes part in, the checks for
/// variable-like declarations, the checks for every statement kind that is a container, and the iterated-type and
/// iteration-types machinery every for-of, spread, destructuring and yield site asks.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: <c>checkVariableDeclaration</c> (checker.ts:45282-45287) and its <c>tracing?.push</c>/<c>tracing?.pop</c>
/// pair. The port's tracing facade (<c>Core/Tracing.cs</c>) is not wired into the checker — no landed checker member
/// calls it — so the pair is omitted rather than invented; the observable result (the walked declaration) is
/// unchanged. This is the precedent <c>getVariancesWorker</c> set (Checker.Relations.Identical.cs:251-255).
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>getIterationTypesKeyFromIterationTypeKind</c> (checker.ts:54240) turns an
/// <c>IterationTypeKind</c> into the *name* of one of the three <c>IterationTypes</c> slots, because TS indexes the
/// record by that name. C# cannot index an object by a string, so the two members that used it
/// (<c>getIterationTypeOfIterable</c>, <c>getIterationTypeOfGeneratorFunctionReturnType</c>) select the same slot
/// with a <c>switch</c> over the same kind, in the same order. The helper itself is outside this band and stays with
/// the band that owns checker.ts:54240.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>setCachedIterationTypes</c> (checker.ts:45903-45905) returns the value it stored, and the nine
/// iteration-type readers return its result directly. The landed port declaration (Checker.Iteration.cs:148) answers
/// <c>void</c>, so each <c>return setCachedIterationTypes(...)</c> is spelled as the store followed by returning the
/// same value.
/// </para>
/// <para>
/// PORT-NOTE: the two iteration-type sentinels are landed as <see cref="IIterationTypes"/> (Checker.Types.cs:334 and
/// :337) while the machinery flows the concrete <see cref="IterationTypes"/> (<c>createIterationTypes</c> answers the
/// class, Checker.Types.cs:891; the two cache accessors take and answer it, Checker.Iteration.cs:134/:148). Identity
/// against the sentinels is therefore tested with <see cref="object.ReferenceEquals(object, object)"/> — TS's
/// <c>===</c> — and a sentinel that is *returned* is cast to the class, which holds because both sentinels are
/// <see cref="IterationTypes"/> instances (Checker.Types.cs:491-492).
/// </para>
/// <para>
/// PORT-NOTE: <c>isReferenceToSomeType(type, resolver.getGlobalBuiltinIteratorTypes())</c> (checker.ts:46117, :46265).
/// The landed <c>IsReferenceToSomeType</c> takes <c>IList&lt;IType&gt;</c> (Checker.BaseTypes.cs:64) while the landed
/// resolver's <c>GetGlobalBuiltinIteratorTypes</c> answers <c>List&lt;IGenericType&gt;</c>
/// (Checker.Iteration.cs:60), and <c>IList&lt;T&gt;</c> is invariant; the port materializes the covariant view of the
/// five-element list at the two call sites. Both files are sibling slices, so the signature pair is reported rather
/// than changed here.
/// </para>
/// <para>
/// PORT-NOTE: TS's checking members state a union parameter and read the union's slots. The port has no C# face for
/// those unions (<c>SignatureDeclaration</c>, <c>VariableDeclaration | BindingElement</c>, the five-declaration
/// variable-like union), so the parameter is <see cref="Node"/> — what the source-element walk holds — and the
/// class-specific slots are read with the two inline kind switches the landed
/// <c>getContextualTypeForInitializerExpression</c> already uses (Checker.Inference.cs:1712-1721).
/// </para>
/// <para>
/// PORT-NOTE: the members this file calls but does not declare — the source-element walk (<c>CheckSourceElement</c>,
/// <c>CheckDecorators</c>, <c>CheckComputedPropertyName</c>, <c>RegisterForUnusedIdentifiersCheck</c>), the grammar
/// checks, the relations entry points, the token-level flag accessor <c>GetNodeCheckFlags</c>, the two assembly
/// facades (<c>TypeToString</c>/<c>SymbolToString</c>/<c>GetSymbolAtLocation</c>/<c>GetAwaitedType</c>) and the
/// utilities this band needs that are not landed — are each listed with their owner in the slice report.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The collision checks (checker.ts:44781-44982)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:44781-44792 — <c>checkCollisionWithArgumentsInGeneratedCode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>nodeIsMissing((node as FunctionLikeDeclaration).body)</c> is an unchecked cast: the
    /// parameter is the <c>SignatureDeclaration</c> union, which includes the body-less <c>MethodSignature</c> and
    /// <c>JSDocFunctionType</c> kinds that reach this member from <c>checkSignatureDeclaration</c>. Their <c>body</c> is
    /// <c>undefined</c>, so <c>nodeIsMissing</c> answers <c>true</c> and the check returns before touching
    /// <c>parameters</c>; the port states that as the soft cast and <see cref="Utilities.NodeIsMissing"/>'s own
    /// <c>null</c> arm, which is <c>true</c> as well.
    /// </remarks>
    internal void CheckCollisionWithArgumentsInGeneratedCode(Node node)
    {
        // no rest parameters \ declaration context \ overload - no codegen impact
        var functionLike = node as IFunctionLikeDeclarationBase;
        if (_languageVersion >= ScriptTarget.ES2015
            || !Utilities.HasRestParameter(node)
            || (node.Flags & NodeFlags.Ambient) != 0
            || Utilities.NodeIsMissing(functionLike?.Body))
        {
            return;
        }

        foreach (var p in functionLike!.Parameters!)
        {
            if (p.Name is not null && !Utilities.IsBindingPattern(p.Name) && ((Identifier)p.Name).EscapedText == _argumentsSymbol!.EscapedName)
            {
                ErrorSkippedOn("noEmit", p, Diagnostics.Duplicate_identifier_arguments_Compiler_uses_arguments_to_initialize_rest_parameters);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44799-44836 — <c>needCollisionCheckForIdentifier</c>.</summary>
    /// <remarks>
    /// <para>Checks whether an {@link Identifier}, in the context of another node, would collide with a runtime value of <paramref name="name"/> in an outer scope. This is used to check for collisions for downlevel transformations that require names like <c>Object</c>, <c>Promise</c>, <c>Reflect</c>, <c>require</c>, <c>exports</c>, etc.</para>
    /// </remarks>
    internal bool NeedCollisionCheckForIdentifier(Node node, Identifier? identifier, string name)
    {
        if (identifier?.EscapedText != name)
        {
            return false;
        }

        if (node.Kind is SyntaxKind.PropertyDeclaration
            or SyntaxKind.PropertySignature
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.MethodSignature
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor
            or SyntaxKind.PropertyAssignment)
        {
            // it is ok to have member named '_super', '_this', `Promise`, etc. - member access is always qualified
            return false;
        }

        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            // ambient context - no codegen impact
            return false;
        }

        if (NodeTests.IsImportClause(node) || NodeTests.IsImportEqualsDeclaration(node) || NodeTests.IsImportSpecifier(node))
        {
            // type-only imports do not require collision checks against runtime values.
            if (Utilities.IsTypeOnlyImportOrExportDeclaration(node))
            {
                return false;
            }
        }

        var root = Utilities.GetRootDeclaration(node);
        if (NodeTests.IsParameter(root) && Utilities.NodeIsMissing(((IFunctionLikeDeclarationBase)root.Parent!).Body))
        {
            // just an overload - no codegen impact
            return false;
        }

        return true;
    }

    // this function will run after checking the source file so 'CaptureThis' is correct for all nodes
    /// <summary>ported from: src/compiler/checker.ts:44839-44853 — <c>checkIfThisIsCapturedInEnclosingScope</c>.</summary>
    internal void CheckIfThisIsCapturedInEnclosingScope(Node node)
    {
        Utilities.FindAncestor(node, current =>
        {
            if ((GetNodeCheckFlags(current) & NodeCheckFlags.CaptureThis) != 0)
            {
                var isDeclaration = node.Kind != SyntaxKind.Identifier;
                if (isDeclaration)
                {
                    Error((Node?)Utilities.GetNameOfDeclaration(node), Diagnostics.Duplicate_identifier_this_Compiler_uses_variable_declaration_this_to_capture_this_reference);
                }
                else
                {
                    Error(node, Diagnostics.Expression_resolves_to_variable_declaration_this_that_compiler_uses_to_capture_this_reference);
                }
                return true;
            }
            return false;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:44855-44869 — <c>checkIfNewTargetIsCapturedInEnclosingScope</c>.</summary>
    internal void CheckIfNewTargetIsCapturedInEnclosingScope(Node node)
    {
        Utilities.FindAncestor(node, current =>
        {
            if ((GetNodeCheckFlags(current) & NodeCheckFlags.CaptureNewTarget) != 0)
            {
                var isDeclaration = node.Kind != SyntaxKind.Identifier;
                if (isDeclaration)
                {
                    Error((Node?)Utilities.GetNameOfDeclaration(node), Diagnostics.Duplicate_identifier_newTarget_Compiler_uses_variable_declaration_newTarget_to_capture_new_target_meta_property_reference);
                }
                else
                {
                    Error(node, Diagnostics.Expression_resolves_to_variable_declaration_newTarget_that_compiler_uses_to_capture_new_target_meta_property_reference);
                }
                return true;
            }
            return false;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:44871-44892 — <c>checkCollisionWithRequireExportsInGeneratedCode</c>.</summary>
    internal void CheckCollisionWithRequireExportsInGeneratedCode(Node node, Identifier? name)
    {
        // No need to check for require or exports for ES6 modules and later
        if (_host.GetEmitModuleFormatOfFile(Utilities.GetSourceFileOfNode(node)) >= ModuleKind.ES2015)
        {
            return;
        }

        if (name is null || !NeedCollisionCheckForIdentifier(node, name, "require") && !NeedCollisionCheckForIdentifier(node, name, "exports"))
        {
            return;
        }

        // Uninstantiated modules shouldnt do this check
        if (NodeTests.IsModuleDeclaration(node) && Binder.GetModuleInstanceState((ModuleDeclaration)node) != ModuleInstanceState.Instantiated)
        {
            return;
        }

        // In case of variable declaration, node.parent is variable statement so look at the variable statement's parent
        var parent = GetDeclarationContainer(node);
        if (parent.Kind == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule((SourceFile)parent))
        {
            // If the declaration happens to be in external module, report error that require and exports are reserved keywords
            ErrorSkippedOn("noEmit", name, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_in_top_level_scope_of_a_module, Utilities.DeclarationNameToString(name), Utilities.DeclarationNameToString(name));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44894-44910 — <c>checkCollisionWithGlobalPromiseInGeneratedCode</c>.</summary>
    internal void CheckCollisionWithGlobalPromiseInGeneratedCode(Node node, Identifier? name)
    {
        if (name is null || _languageVersion >= ScriptTarget.ES2017 || !NeedCollisionCheckForIdentifier(node, name, "Promise"))
        {
            return;
        }

        // Uninstantiated modules shouldnt do this check
        if (NodeTests.IsModuleDeclaration(node) && Binder.GetModuleInstanceState((ModuleDeclaration)node) != ModuleInstanceState.Instantiated)
        {
            return;
        }

        // In case of variable declaration, node.parent is variable statement so look at the variable statement's parent
        var parent = GetDeclarationContainer(node);
        if (parent.Kind == SyntaxKind.SourceFile && Utilities.IsExternalOrCommonJsModule((SourceFile)parent) && (parent.Flags & NodeFlags.HasAsyncFunctions) != 0)
        {
            // If the declaration happens to be in external module, report error that Promise is a reserved identifier.
            ErrorSkippedOn("noEmit", name, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_in_top_level_scope_of_a_module_containing_async_functions, Utilities.DeclarationNameToString(name), Utilities.DeclarationNameToString(name));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44912-44919 — <c>recordPotentialCollisionWithWeakMapSetInGeneratedCode</c>.</summary>
    internal void RecordPotentialCollisionWithWeakMapSetInGeneratedCode(Node node, Identifier name)
    {
        if (_languageVersion <= ScriptTarget.ES2021
            && (NeedCollisionCheckForIdentifier(node, name, "WeakMap") || NeedCollisionCheckForIdentifier(node, name, "WeakSet")))
        {
            _potentialWeakMapSetCollisions.Add(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44921-44927 — <c>checkWeakMapSetCollision</c>.</summary>
    internal void CheckWeakMapSetCollision(Node node)
    {
        var enclosingBlockScope = Utilities.GetEnclosingBlockScopeContainer(node);
        if ((GetNodeCheckFlags(enclosingBlockScope) & NodeCheckFlags.ContainsClassWithPrivateIdentifiers) != 0)
        {
            Debug.Assert(
                Utilities.IsNamedDeclaration(node) && ((INamedDeclaration)node).Name is Identifier collisionName && collisionName.EscapedText is not null,
                "The target of a WeakMap/WeakSet collision check should be an identifier");
            ErrorSkippedOn("noEmit", node, Diagnostics.Compiler_reserves_name_0_when_emitting_private_identifier_downlevel, ((Identifier)((INamedDeclaration)node).Name!).EscapedText!);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44929-44936 — <c>recordPotentialCollisionWithReflectInGeneratedCode</c>.</summary>
    internal void RecordPotentialCollisionWithReflectInGeneratedCode(Node node, Identifier? name)
    {
        if (name is not null
            && _languageVersion >= ScriptTarget.ES2015
            && _languageVersion <= ScriptTarget.ES2021
            && NeedCollisionCheckForIdentifier(node, name, "Reflect"))
        {
            _potentialReflectCollisions.Add(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44938-44965 — <c>checkReflectCollision</c>.</summary>
    internal void CheckReflectCollision(Node node)
    {
        var hasCollision = false;
        if (NodeTests.IsClassExpression(node))
        {
            // ClassExpression names don't contribute to their containers, but do matter for any of their block-scoped members.
            foreach (var member in ((IClassLikeDeclarationBase)node).Members!)
            {
                if ((GetNodeCheckFlags((Node)member) & NodeCheckFlags.ContainsSuperPropertyInStaticInitializer) != 0)
                {
                    hasCollision = true;
                    break;
                }
            }
        }
        else if (NodeTests.IsFunctionExpression(node))
        {
            // FunctionExpression names don't contribute to their containers, but do matter for their contents
            if ((GetNodeCheckFlags(node) & NodeCheckFlags.ContainsSuperPropertyInStaticInitializer) != 0)
            {
                hasCollision = true;
            }
        }
        else
        {
            var container = Utilities.GetEnclosingBlockScopeContainer(node);
            if ((GetNodeCheckFlags(container) & NodeCheckFlags.ContainsSuperPropertyInStaticInitializer) != 0)
            {
                hasCollision = true;
            }
        }
        if (hasCollision)
        {
            Debug.Assert(Utilities.IsNamedDeclaration(node) && ((INamedDeclaration)node).Name is Identifier, "The target of a Reflect collision check should be an identifier");
            ErrorSkippedOn("noEmit", node, Diagnostics.Duplicate_identifier_0_Compiler_reserves_name_1_when_emitting_super_references_in_static_initializers, Utilities.DeclarationNameToString(((INamedDeclaration)node).Name), "Reflect");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44967-44982 — <c>checkCollisionsForDeclarationName</c>.</summary>
    internal void CheckCollisionsForDeclarationName(Node node, Identifier? name)
    {
        if (name is null) return;
        CheckCollisionWithRequireExportsInGeneratedCode(node, name);
        CheckCollisionWithGlobalPromiseInGeneratedCode(node, name);
        RecordPotentialCollisionWithWeakMapSetInGeneratedCode(node, name);
        RecordPotentialCollisionWithReflectInGeneratedCode(node, name);
        if (Utilities.IsClassLike(node))
        {
            CheckTypeNameIsReserved(name, Diagnostics.Class_name_cannot_be_0);
            if ((node.Flags & NodeFlags.Ambient) == 0)
            {
                CheckClassNameCollisionWithObject(name);
            }
        }
        else if (NodeTests.IsEnumDeclaration(node))
        {
            CheckTypeNameIsReserved(name, Diagnostics.Enum_name_cannot_be_0);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:44984-45050 — <c>checkVarDeclaredNamesNotShadowed</c>.</summary>
    internal void CheckVarDeclaredNamesNotShadowed(Node node)
    {
        // - ScriptBody : StatementList
        // It is a Syntax Error if any element of the LexicallyDeclaredNames of StatementList
        // also occurs in the VarDeclaredNames of StatementList.

        // - Block : { StatementList }
        // It is a Syntax Error if any element of the LexicallyDeclaredNames of StatementList
        // also occurs in the VarDeclaredNames of StatementList.

        // Variable declarations are hoisted to the top of their function scope. They can shadow
        // block scoped declarations, which bind tighter. this will not be flagged as duplicate definition
        // by the binder as the declaration scope is different.
        // A non-initialized declaration is a no-op as the block declaration will resolve before the var
        // declaration. the problem is if the declaration has an initializer. this will act as a write to the
        // block declared value. this is fine for let, but not const.
        // Only consider declarations with initializers, uninitialized const declarations will not
        // step on a let/const variable.
        // Do not consider const and const declarations, as duplicate block-scoped declarations
        // are handled by the binder.
        // We are only looking for const declarations that step on let\const declarations from a
        // different scope. e.g.:
        //      {
        //          const x = 0; // localDeclarationSymbol obtained after name resolution will correspond to this declaration
        //          const x = 0; // symbol for this declaration will be 'symbol'
        //      }

        // skip block-scoped variables and parameters
        if ((GetCombinedNodeFlagsCached(node) & NodeFlags.BlockScoped) != 0 || Utilities.IsPartOfParameterDeclaration(node))
        {
            return;
        }

        // NOTE: in ES6 spec initializer is required in variable declarations where name is binding pattern
        // so we'll always treat binding elements as initialized

        var symbol = GetSymbolOfDeclaration((IDeclaration)node);
        if ((symbol.Flags & SymbolFlags.FunctionScopedVariable) != 0)
        {
            var name = ((INamedDeclaration)node).Name;
            if (name is not Identifier identifierName)
            {
                Debug.Fail("A function-scoped variable declaration must have an identifier name");
                return;
            }

            var localDeclarationSymbol = ResolveName(node, identifierName.EscapedText!, SymbolFlags.Variable, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
            if (localDeclarationSymbol is not null
                && !ReferenceEquals(localDeclarationSymbol, symbol)
                && (localDeclarationSymbol.Flags & SymbolFlags.BlockScopedVariable) != 0)
            {
                if ((GetDeclarationNodeFlagsFromSymbol(localDeclarationSymbol) & NodeFlags.BlockScoped) != 0)
                {
                    var varDeclList = (VariableDeclarationList)Utilities.GetAncestor((Node?)localDeclarationSymbol.ValueDeclaration, SyntaxKind.VariableDeclarationList)!;
                    var container = varDeclList.Parent!.Kind == SyntaxKind.VariableStatement && varDeclList.Parent!.Parent is not null
                        ? varDeclList.Parent!.Parent
                        : null;

                    // names of block-scoped and function scoped variables can collide only
                    // if block scoped variable is defined in the function\module\source file scope (because of variable hoisting)
                    var namesShareScope = container is not null
                        && (container.Kind == SyntaxKind.Block && Utilities.IsFunctionLike(container.Parent!)
                            || container.Kind == SyntaxKind.ModuleBlock
                            || container.Kind == SyntaxKind.ModuleDeclaration
                            || container.Kind == SyntaxKind.SourceFile);

                    // here we know that function scoped variable is "shadowed" by block scoped one
                    // a var declatation can't hoist past a lexical declaration and it results in a SyntaxError at runtime
                    if (!namesShareScope)
                    {
                        // PORT-NOTE (A″): the argument is a DiagnosticArgument (Core/Diagnostic.cs) so the printer-gated stringifier runs on the first read of the message text.
                        var localName = Diagnostic.DeferArg(() => SymbolToString(localDeclarationSymbol));
                        Error(node, Diagnostics.Cannot_initialize_outer_scoped_variable_0_in_the_same_scope_as_block_scoped_declaration_1, localName, localName);
                    }
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45052-45054 — <c>convertAutoToAny</c>.</summary>
    internal IType ConvertAutoToAny(IType type)
        => ReferenceEquals(type, _autoType) ? _anyType : ReferenceEquals(type, _autoArrayType) ? _anyArrayType : type;

    // ---------------------------------------------------------------------------------------------
    // The variable-like declaration checks (checker.ts:45057-45307)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:45057-45239 — <c>checkVariableLikeDeclaration</c>.</summary>
    /// <remarks>
    /// <para>Check variable, parameter, or property declaration.</para>
    /// <para>PORT-NOTE: TS's parameter is the union <c>ParameterDeclaration | PropertyDeclaration | PropertySignature | VariableDeclaration | BindingElement</c> and its body reads the union's <c>name</c>, <c>type</c>, <c>initializer</c>, <c>propertyName</c> and <c>dotDotDotToken</c> slots. The port has no face for that union: the parameter is the <see cref="Node"/> the source-element walk holds, <c>name</c> is read through <see cref="INamedDeclaration"/> (which all five classes implement), <c>propertyName</c>/<c>dotDotDotToken</c> are read off the <see cref="BindingElement"/> the reference's own guard narrows to, and the two slots that four or five classes carry without a shared face (<c>type</c>, <c>initializer</c>) are read with the inline kind switches the landed <c>getContextualTypeForInitializerExpression</c> uses (Checker.Inference.cs:1712-1721).</para>
    /// </remarks>
    internal void CheckVariableLikeDeclaration(Node node)
    {
        CheckDecorators(node);
        var nodeName = ((INamedDeclaration)node).Name;
        Expression? nodeInitializer = node switch
        {
            VariableDeclaration variableDeclaration => variableDeclaration.Initializer,
            ParameterDeclaration parameterDeclaration => parameterDeclaration.Initializer,
            PropertyDeclaration propertyDeclaration => propertyDeclaration.Initializer,
            PropertySignature propertySignature => propertySignature.Initializer,
            BindingElement bindingElement => bindingElement.Initializer,
            _ => null,
        };
        if (!NodeTests.IsBindingElement(node))
        {
            ITypeNode? typeAnnotation = node switch
            {
                VariableDeclaration variableDeclaration => variableDeclaration.Type,
                ParameterDeclaration parameterDeclaration => parameterDeclaration.Type,
                PropertyDeclaration propertyDeclaration => propertyDeclaration.Type,
                PropertySignature propertySignature => propertySignature.Type,
                _ => null,
            };
            CheckSourceElement((Node?)typeAnnotation);
        }

        // JSDoc `function(string, string): string` syntax results in parameters with no name
        if (nodeName is null)
        {
            return;
        }

        // For a computed property, just check the initializer and exit
        // Do not use hasDynamicName here, because that returns false for well known symbols.
        // We want to perform checkComputedPropertyName for all computed properties, including
        // well known symbols.
        if (nodeName.Kind == SyntaxKind.ComputedPropertyName)
        {
            CheckComputedPropertyName((ComputedPropertyName)nodeName!);
            if (Utilities.HasOnlyExpressionInitializer(node) && nodeInitializer is not null)
            {
                CheckExpressionCached(nodeInitializer);
            }
        }

        if (NodeTests.IsBindingElement(node))
        {
            var bindingElement = (BindingElement)node;
            // PORT-NOTE: the same absent-slot read as the parameter arm above — the reference's
            // `(getContainingFunction(node) as FunctionLikeDeclaration).body` (checker.ts:45100-45104) yields
            // `undefined` for a containing signature that has no body (call/construct/method signature, function type),
            // which `nodeIsMissing` reports as missing; C# needs the safe cast and `?.Body` to state that.
            if (bindingElement.PropertyName is not null
                && NodeTests.IsIdentifier(nodeName)
                && Utilities.IsPartOfParameterDeclaration(node)
                && Utilities.NodeIsMissing((Utilities.GetContainingFunction(node) as IFunctionLikeDeclarationBase)?.Body))
            {
                // type F = ({a: string}) => void;
                //               ^^^^^^
                // variable renaming in function type notation is confusing,
                // so we forbid it even if noUnusedLocals is not enabled
                _potentialUnusedRenamedBindingElementsInTypes.Add(bindingElement);
                return;
            }

            if (NodeTests.IsObjectBindingPattern(bindingElement.Parent!) && bindingElement.DotDotDotToken is not null && _languageVersion < ScriptTarget.ES2018) // LanguageFeatureMinimumTarget.ObjectSpreadRest
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.Rest);
            }
            // check computed properties inside property names of binding elements
            if (bindingElement.PropertyName is not null && bindingElement.PropertyName.Kind == SyntaxKind.ComputedPropertyName)
            {
                CheckComputedPropertyName((ComputedPropertyName)bindingElement.PropertyName!);
            }

            // check private/protected variable access
            var parent = node.Parent!.Parent!;
            var parentCheckMode = bindingElement.DotDotDotToken is not null ? CheckMode.RestBindingElement : CheckMode.Normal;
            var parentType = GetTypeForBindingElementParent(parent, parentCheckMode);
            var name = bindingElement.PropertyName ?? nodeName;
            if (parentType is not null && !Utilities.IsBindingPattern(name))
            {
                var exprType = GetLiteralTypeFromPropertyName(name);
                if (Utilities.IsTypeUsableAsPropertyName(exprType))
                {
                    var nameText = Utilities.GetPropertyNameFromType(exprType);
                    var property = GetPropertyOfType(parentType, nameText);
                    if (property is not null)
                    {
                        MarkPropertyAsReferenced(property, /*nodeForCheckWriteOnly*/ null, /*isSelfTypeAccess*/ false); // A destructuring is never a write-only reference.
                        Expression? parentInitializer = parent switch
                        {
                            VariableDeclaration variableDeclaration => variableDeclaration.Initializer,
                            ParameterDeclaration parameterDeclaration => parameterDeclaration.Initializer,
                            _ => null,
                        };
                        CheckPropertyAccessibility(node, parentInitializer is not null && parentInitializer.Kind == SyntaxKind.SuperKeyword, /*writing*/ false, parentType, property);
                    }
                }
            }
        }

        // For a binding pattern, check contained binding elements
        if (Utilities.IsBindingPattern(nodeName))
        {
            if (nodeName.Kind == SyntaxKind.ArrayBindingPattern && _languageVersion < ScriptTarget.ES2015 && Js.IsTruthy(_compilerOptions.DownlevelIteration)) // LanguageFeatureMinimumTarget.BindingPatterns
            {
                CheckExternalEmitHelpers(node, ExternalEmitHelpers.Read);
            }

            foreach (var element in ElementsOfBindingPattern(nodeName)!)
            {
                CheckSourceElement(element);
            }
        }
        // For a parameter declaration with an initializer, error and exit if the containing function doesn't have a body
        // PORT-NOTE: `as` is C#'s safe cast, and that is exactly what the reference's `(getContainingFunction(node) as
        // FunctionLikeDeclaration)` is. For a containing signature with no body slot (call/construct/index signature,
        // function type) an *unchecked* cast throws in C# where TS's read yields `undefined`, which `nodeIsMissing`
        // reports as missing (utilities.ts:1079-1085); `?.Body` states that absent-slot read.
        if (nodeInitializer is not null && Utilities.IsPartOfParameterDeclaration(node)
            && Utilities.NodeIsMissing((Utilities.GetContainingFunction(node) as IFunctionLikeDeclarationBase)?.Body))
        {
            Error(node, Diagnostics.A_parameter_initializer_is_only_allowed_in_a_function_or_constructor_implementation);
            return;
        }
        // For a binding pattern, validate the initializer and exit
        if (Utilities.IsBindingPattern(nodeName))
        {
            if (IsInAmbientOrTypeNode(node))
            {
                return;
            }
            var needCheckInitializer = Utilities.HasOnlyExpressionInitializer(node) && nodeInitializer is not null && node.Parent!.Parent!.Kind != SyntaxKind.ForInStatement;
            // PORT-NOTE: the reference's !some(node.name.elements, not(isOmittedExpression)); the two binding-pattern
            // classes share no IList<Node> face in the model, so the port walks the same list instead. The predicate is
            // true when *no* element is a real one (the pattern is empty or all holes), so it starts true and the first
            // non-omitted element clears it — the reference's leading `!`.
            var needCheckWidenedType = true;
            foreach (var element in ElementsOfBindingPattern(nodeName)!)
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    needCheckWidenedType = false;
                    break;
                }
            }
            if (needCheckInitializer || needCheckWidenedType)
            {
                // Don't validate for-in initializer as it is already an error
                var widenedType = GetWidenedTypeForVariableLikeDeclaration(node);
                if (needCheckInitializer)
                {
                    // PORT-NOTE: the reference's `needCheckInitializer` guard proves nodeInitializer non-null; C# does not carry the
                    // narrowing through a bool local, so the assertion is stated here.
                    var initializerType = CheckExpressionCached(nodeInitializer!);
                    if (_strictNullChecks && needCheckWidenedType)
                    {
                        CheckNonNullNonVoidType(initializerType, node);
                    }
                    else
                    {
                        CheckTypeAssignableToAndOptionallyElaborate(initializerType, GetWidenedTypeForVariableLikeDeclaration(node), node, nodeInitializer);
                    }
                }
                // check the binding pattern with empty elements
                if (needCheckWidenedType)
                {
                    if (nodeName.Kind == SyntaxKind.ArrayBindingPattern)
                    {
                        CheckIteratedTypeOrElementType(IterationUse.Destructuring, widenedType, _undefinedType, node);
                    }
                    else if (_strictNullChecks)
                    {
                        CheckNonNullNonVoidType(widenedType, node);
                    }
                }
            }
            return;
        }
        // For a commonjs `const x = require`, validate the alias and exit
        var symbol = GetSymbolOfDeclaration((IDeclaration)node);
        if ((symbol.Flags & SymbolFlags.Alias) != 0
            && (Utilities.IsVariableDeclarationInitializedToBareOrAccessedRequire(node) || Utilities.IsBindingElementOfBareOrAccessedRequire(node)))
        {
            CheckAliasSymbol(node);
            return;
        }

        if (nodeName.Kind == SyntaxKind.BigIntLiteral)
        {
            Error(nodeName!, Diagnostics.A_bigint_literal_cannot_be_used_as_a_property_name);
        }

        var type = ConvertAutoToAny(GetTypeOfSymbol(symbol));
        if (ReferenceEquals(node, symbol.ValueDeclaration))
        {
            // Node is the primary declaration of the symbol, just validate the initializer
            // Don't validate for-in initializer as it is already an error
            var effectiveInitializer = Utilities.HasOnlyExpressionInitializer(node) ? Utilities.GetEffectiveInitializer(node) : null;
            if (effectiveInitializer is not null)
            {
                var isJSObjectLiteralInitializer = Utilities.IsInJSFile(node)
                    && NodeTests.IsObjectLiteralExpression(effectiveInitializer)
                    && (((ObjectLiteralExpression)effectiveInitializer).Properties!.Count == 0 || Utilities.IsPrototypeAccess(nodeName))
                    && symbol.Exports?.Count > 0;
                if (!isJSObjectLiteralInitializer && node.Parent!.Parent!.Kind != SyntaxKind.ForInStatement)
                {
                    var initializerType = CheckExpressionCached(effectiveInitializer);
                    CheckTypeAssignableToAndOptionallyElaborate(initializerType, type, node, effectiveInitializer, /*headMessage*/ null);
                    var blockScopeKind = GetCombinedNodeFlagsCached(node) & NodeFlags.BlockScoped;
                    if (blockScopeKind == NodeFlags.AwaitUsing)
                    {
                        var globalAsyncDisposableType = GetGlobalAsyncDisposableType(/*reportErrors*/ true);
                        var globalDisposableType = GetGlobalDisposableType(/*reportErrors*/ true);
                        if (!ReferenceEquals(globalAsyncDisposableType, _emptyObjectType) && !ReferenceEquals(globalDisposableType, _emptyObjectType))
                        {
                            var optionalDisposableType = GetUnionType([globalAsyncDisposableType, globalDisposableType, _nullType, _undefinedType]);
                            CheckTypeAssignableTo(WidenTypeForVariableLikeDeclaration(initializerType, node), optionalDisposableType, effectiveInitializer, Diagnostics.The_initializer_of_an_await_using_declaration_must_be_either_an_object_with_a_Symbol_asyncDispose_or_Symbol_dispose_method_or_be_null_or_undefined);
                        }
                    }
                    else if (blockScopeKind == NodeFlags.Using)
                    {
                        var globalDisposableType = GetGlobalDisposableType(/*reportErrors*/ true);
                        if (!ReferenceEquals(globalDisposableType, _emptyObjectType))
                        {
                            var optionalDisposableType = GetUnionType([globalDisposableType, _nullType, _undefinedType]);
                            CheckTypeAssignableTo(WidenTypeForVariableLikeDeclaration(initializerType, node), optionalDisposableType, effectiveInitializer, Diagnostics.The_initializer_of_a_using_declaration_must_be_either_an_object_with_a_Symbol_dispose_method_or_be_null_or_undefined);
                        }
                    }
                }
            }
            if (symbol.Declarations is not null && symbol.Declarations.Count > 1)
            {
                if (Core.Some(symbol.Declarations, d => !ReferenceEquals(d, node) && Utilities.IsVariableLike((Node)d) && !AreDeclarationFlagsIdentical(d, (IDeclaration)node)))
                {
                    Error(nodeName!, Diagnostics.All_declarations_of_0_must_have_identical_modifiers, Utilities.DeclarationNameToString(nodeName));
                }
            }
        }
        else
        {
            // Node is a secondary declaration, check that type is identical to primary declaration and check that
            // initializer is consistent with type associated with the node
            var declarationType = ConvertAutoToAny(GetWidenedTypeForVariableLikeDeclaration(node));

            if (!IsErrorType(type) && !IsErrorType(declarationType)
                && !IsTypeIdenticalTo(type, declarationType)
                && (symbol.Flags & SymbolFlags.Assignment) == 0)
            {
                ErrorNextVariableOrPropertyDeclarationMustHaveSameType(symbol.ValueDeclaration, type, (IDeclaration)node, declarationType);
            }
            if (Utilities.HasOnlyExpressionInitializer(node) && nodeInitializer is not null)
            {
                CheckTypeAssignableToAndOptionallyElaborate(CheckExpressionCached(nodeInitializer), declarationType, node, nodeInitializer, /*headMessage*/ null);
            }
            if (symbol.ValueDeclaration is not null && !AreDeclarationFlagsIdentical((IDeclaration)node, symbol.ValueDeclaration))
            {
                Error(nodeName!, Diagnostics.All_declarations_of_0_must_have_identical_modifiers, Utilities.DeclarationNameToString(nodeName));
            }
        }
        if (node.Kind != SyntaxKind.PropertyDeclaration && node.Kind != SyntaxKind.PropertySignature)
        {
            // We know we don't have a binding pattern or computed name here
            CheckExportsOnMergedDeclarations((IDeclaration)node);
            if (node.Kind == SyntaxKind.VariableDeclaration || node.Kind == SyntaxKind.BindingElement)
            {
                CheckVarDeclaredNamesNotShadowed(node);
            }
            CheckCollisionsForDeclarationName(node, (Identifier?)nodeName);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45057-45239 — the <c>node.name.elements</c> read of <c>checkVariableLikeDeclaration</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS's <c>node.name.elements</c> in <c>checkVariableLikeDeclaration</c> is read off <c>BindingPattern</c>; the model has only the two concrete classes (<see cref="ArrayBindingPattern"/>/<see cref="ObjectBindingPattern"/>), so the port reads the slot with the same two-arm switch the two <c>isArrayBindingPattern</c> tests in the reference already distinguish.</para>
    /// </remarks>
    private static IReadOnlyList<Node>? ElementsOfBindingPattern(Node bindingPattern)
        => bindingPattern switch
        {
            ArrayBindingPattern arrayBindingPattern => arrayBindingPattern.Elements,
            ObjectBindingPattern objectBindingPattern => objectBindingPattern.Elements,
            _ => null,
        };

    /// <summary>ported from: src/compiler/checker.ts:45241-45257 — <c>errorNextVariableOrPropertyDeclarationMustHaveSameType</c>.</summary>
    internal void ErrorNextVariableOrPropertyDeclarationMustHaveSameType(IDeclaration? firstDeclaration, IType firstType, IDeclaration nextDeclaration, IType nextType)
    {
        var nextDeclarationName = Utilities.GetNameOfDeclaration((Node)nextDeclaration);
        var message = nextDeclaration.Kind is SyntaxKind.PropertyDeclaration or SyntaxKind.PropertySignature
            ? Diagnostics.Subsequent_property_declarations_must_have_the_same_type_Property_0_must_be_of_type_1_but_here_has_type_2
            : Diagnostics.Subsequent_variable_declarations_must_have_the_same_type_Variable_0_must_be_of_type_1_but_here_has_type_2;
        var declName = Utilities.DeclarationNameToString((Node?)nextDeclarationName);
        var err = Error(
            (Node?)nextDeclarationName,
            message,
            declName,
            Diagnostic.DeferArg(() => TypeToString(firstType)),
            Diagnostic.DeferArg(() => TypeToString(nextType)));
        if (firstDeclaration is not null)
        {
            Utilities.AddRelatedInfo(err, Utilities.CreateDiagnosticForNode((Node)firstDeclaration, Diagnostics._0_was_also_declared_here, declName));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45259-45280 — <c>areDeclarationFlagsIdentical</c>.</summary>
    internal bool AreDeclarationFlagsIdentical(IDeclaration left, IDeclaration right)
    {
        if (left.Kind == SyntaxKind.Parameter && right.Kind == SyntaxKind.VariableDeclaration
            || left.Kind == SyntaxKind.VariableDeclaration && right.Kind == SyntaxKind.Parameter)
        {
            // Differences in optionality between parameters and variables are allowed.
            return true;
        }

        if (Utilities.HasQuestionToken((Node)left) != Utilities.HasQuestionToken((Node)right))
        {
            return false;
        }

        var interestingFlags = ModifierFlags.Private
            | ModifierFlags.Protected
            | ModifierFlags.Async
            | ModifierFlags.Abstract
            | ModifierFlags.Readonly
            | ModifierFlags.Static;

        return Utilities.GetSelectedEffectiveModifierFlags((Node)left, interestingFlags) == Utilities.GetSelectedEffectiveModifierFlags((Node)right, interestingFlags);
    }

    /// <summary>ported from: src/compiler/checker.ts:45282-45287 — <c>checkVariableDeclaration</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: the reference's <c>tracing?.push</c>/<c>tracing?.pop</c> pair is omitted; see the file note.</para>
    /// </remarks>
    internal void CheckVariableDeclaration(VariableDeclaration node)
    {
        CheckGrammarVariableDeclaration(node);
        CheckVariableLikeDeclaration(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:45289-45292 — <c>checkBindingElement</c>.</summary>
    internal void CheckBindingElement(BindingElement node)
    {
        CheckGrammarBindingElement(node);
        CheckVariableLikeDeclaration(node);
    }

    /// <summary>ported from: src/compiler/checker.ts:45294-45301 — <c>checkVariableDeclarationList</c>.</summary>
    internal void CheckVariableDeclarationList(VariableDeclarationList node)
    {
        var blockScopeKind = Utilities.GetCombinedNodeFlags(node) & NodeFlags.BlockScoped;
        if ((blockScopeKind == NodeFlags.Using || blockScopeKind == NodeFlags.AwaitUsing) && _languageVersion < ScriptTarget.ESNext) // LanguageFeatureMinimumTarget.UsingAndAwaitUsing
        {
            CheckExternalEmitHelpers(node, ExternalEmitHelpers.AddDisposableResourceAndDisposeResources);
        }

        foreach (var declaration in node.Declarations!)
        {
            CheckSourceElement(declaration);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45303-45307 — <c>checkVariableStatement</c>.</summary>
    internal void CheckVariableStatement(VariableStatement node)
    {
        // Grammar checking
        if (!CheckGrammarModifiers(node) && !CheckGrammarVariableDeclarationList(node.DeclarationList!)) CheckGrammarForDisallowedBlockScopedVariableStatement(node);
        CheckVariableDeclarationList(node.DeclarationList!);
    }

    /// <summary>ported from: src/compiler/checker.ts:45309-45314 — <c>checkExpressionStatement</c>.</summary>
    internal void CheckExpressionStatement(ExpressionStatement node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);

        CheckExpression(node.Expression!);
    }

    /// <summary>ported from: src/compiler/checker.ts:45316-45328 — <c>checkIfStatement</c>.</summary>
    internal void CheckIfStatement(IfStatement node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);
        var type = CheckTruthinessExpression(node.Expression!);
        CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(node.Expression!, type, (Node?)node.ThenStatement);
        CheckSourceElement((Node?)node.ThenStatement);

        if (node.ThenStatement!.Kind == SyntaxKind.EmptyStatement)
        {
            Error((Node)node.ThenStatement!, Diagnostics.The_body_of_an_if_statement_cannot_be_the_empty_statement);
        }

        CheckSourceElement((Node?)node.ElseStatement);
    }

    /// <summary>ported from: src/compiler/checker.ts:45330-45398 — <c>checkTestingKnownTruthyCallableOrAwaitableOrEnumMemberType</c>.</summary>
    internal void CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(Expression condExpr, IType condType, Node? body = null)
    {
        if (!_strictNullChecks) return;
        BothHelper(condExpr, body);

        void BothHelper(Expression condExpr, Node? body)
        {
            condExpr = Utilities.SkipParentheses(condExpr);

            Helper(condExpr, body);

            while (NodeTests.IsBinaryExpression(condExpr) && (((BinaryExpression)condExpr).OperatorToken!.Kind == SyntaxKind.BarBarToken || ((BinaryExpression)condExpr).OperatorToken!.Kind == SyntaxKind.QuestionQuestionToken))
            {
                condExpr = Utilities.SkipParentheses(((BinaryExpression)condExpr).Left!);
                Helper(condExpr, body);
            }
        }

        void Helper(Expression condExpr, Node? body)
        {
            var location = Utilities.IsLogicalOrCoalescingBinaryExpression(condExpr) ? Utilities.SkipParentheses(((BinaryExpression)condExpr).Right!) : condExpr;
            if (Utilities.IsModuleExportsAccessExpression(location))
            {
                return;
            }
            if (Utilities.IsLogicalOrCoalescingBinaryExpression(location))
            {
                BothHelper(location, body);
                return;
            }
            var type = ReferenceEquals(location, condExpr) ? condType : CheckExpression(location);
            if ((type.Flags & TypeFlags.EnumLiteral) != 0 && NodeTests.IsPropertyAccessExpression(location) && ((GetNodeLinks(((PropertyAccessExpression)location).Expression!).ResolvedSymbol ?? _unknownSymbol).Flags & SymbolFlags.Enum) != 0)
            {
                // EnumLiteral type at condition with known value is always truthy or always falsy, likely an error
                Error(location, Diagnostics.This_condition_will_always_return_0, Js.IsTruthy(((ILiteralType)type).Value) ? "true" : "false");
                return;
            }
            var isPropertyExpressionCast = NodeTests.IsPropertyAccessExpression(location) && IsTypeAssertion(((PropertyAccessExpression)location).Expression!);
            if (!HasTypeFacts(type, TypeFacts.Truthy) || isPropertyExpressionCast) return;

            // While it technically should be invalid for any known-truthy value
            // to be tested, we de-scope to functions and Promises unreferenced in
            // the block as a heuristic to identify the most common bugs. There
            // are too many false positives for values sourced from type
            // definitions without strictNullChecks otherwise.
            var callSignatures = GetSignaturesOfType(type, SignatureKind.Call);
            var isPromise = GetAwaitedTypeOfPromise(type) is not null;
            if (callSignatures.Count == 0 && !isPromise)
            {
                return;
            }

            var testedNode = NodeTests.IsIdentifier(location) ? location
                : NodeTests.IsPropertyAccessExpression(location) ? ((PropertyAccessExpression)location).Name
                : null;
            var testedSymbol = testedNode is not null ? GetSymbolAtLocation(testedNode) : null;
            if (testedSymbol is null && !isPromise)
            {
                return;
            }

            var isUsed = testedSymbol is not null && NodeTests.IsBinaryExpression(condExpr.Parent!) && IsSymbolUsedInBinaryExpressionChain(condExpr.Parent!, testedSymbol)
                || testedSymbol is not null && body is not null && IsSymbolUsedInConditionBody(condExpr, body, testedNode!, testedSymbol);
            if (!isUsed)
            {
                if (isPromise)
                {
                    ErrorAndMaybeSuggestAwait(
                        location,
                        /*maybeMissingAwait*/ true,
                        Diagnostics.This_condition_will_always_return_true_since_this_0_is_always_defined,
                        GetTypeNameForErrorDisplay(type));
                }
                else
                {
                    Error(location, Diagnostics.This_condition_will_always_return_true_since_this_function_is_always_defined_Did_you_mean_to_call_it_instead);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45400-45438 — <c>isSymbolUsedInConditionBody</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS passes its recursive visitor to <c>forEachChild</c> by name; the port's walk is <c>ForEachChildTable.ForEachChild&lt;T&gt;(Node, Func&lt;Node, T?&gt;, …)</c>, whose callback is the nullable answer of the same visitor, so the two recursive calls are spelled as one-argument lambdas over the local function (<c>child =&gt; Check(child)</c>) — the same shape the landed child walks use (Checker.Check.Expressions.cs:1534).</para>
    /// </remarks>
    internal bool IsSymbolUsedInConditionBody(Expression expr, Node body, Node testedNode, ISymbol testedSymbol)
    {
        return ForEachChildTable.ForEachChild<bool>(body, child => Check(child));

        bool Check(Node childNode)
        {
            if (NodeTests.IsIdentifier(childNode))
            {
                var childSymbol = GetSymbolAtLocation(childNode);
                if (childSymbol is not null && ReferenceEquals(childSymbol, testedSymbol))
                {
                    // If the test was a simple identifier, the above check is sufficient
                    if (NodeTests.IsIdentifier(expr) || NodeTests.IsIdentifier(testedNode) && NodeTests.IsBinaryExpression(testedNode.Parent!))
                    {
                        return true;
                    }
                    // Otherwise we need to ensure the symbol is called on the same target
                    Node? testedExpression = testedNode.Parent;
                    Node? childExpression = childNode.Parent;
                    while (testedExpression is not null && childExpression is not null)
                    {
                        if (NodeTests.IsIdentifier(testedExpression) && NodeTests.IsIdentifier(childExpression)
                            || testedExpression.Kind == SyntaxKind.ThisKeyword && childExpression.Kind == SyntaxKind.ThisKeyword)
                        {
                            return ReferenceEquals(GetSymbolAtLocation(testedExpression), GetSymbolAtLocation(childExpression));
                        }
                        else if (NodeTests.IsPropertyAccessExpression(testedExpression) && NodeTests.IsPropertyAccessExpression(childExpression))
                        {
                            if (!ReferenceEquals(
                                GetSymbolAtLocation(((PropertyAccessExpression)testedExpression).Name!),
                                GetSymbolAtLocation(((PropertyAccessExpression)childExpression).Name!)))
                            {
                                return false;
                            }
                            childExpression = ((PropertyAccessExpression)childExpression).Expression;
                            testedExpression = ((PropertyAccessExpression)testedExpression).Expression;
                        }
                        else if (NodeTests.IsCallExpression(testedExpression) && NodeTests.IsCallExpression(childExpression))
                        {
                            childExpression = ((CallExpression)childExpression).Expression;
                            testedExpression = ((CallExpression)testedExpression).Expression;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
            return ForEachChildTable.ForEachChild<bool>(childNode, child => Check(child));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45440-45457 — <c>isSymbolUsedInBinaryExpressionChain</c>.</summary>
    internal bool IsSymbolUsedInBinaryExpressionChain(Node node, ISymbol testedSymbol)
    {
        while (NodeTests.IsBinaryExpression(node) && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.AmpersandAmpersandToken)
        {
            var isUsed = ForEachChildTable.ForEachChild<bool>(((BinaryExpression)node).Right!, child => Visit(child));
            if (isUsed)
            {
                return true;
            }
            node = node.Parent!;
        }
        return false;

        bool Visit(Node child)
        {
            if (NodeTests.IsIdentifier(child))
            {
                var symbol = GetSymbolAtLocation(child);
                if (symbol is not null && ReferenceEquals(symbol, testedSymbol))
                {
                    return true;
                }
            }
            return ForEachChildTable.ForEachChild<bool>(child, grandChild => Visit(grandChild));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The truthiness checks and the iteration statements (checker.ts:45459-45678)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:45459-45465 — <c>checkDoStatement</c>.</summary>
    internal void CheckDoStatement(DoStatement node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);

        CheckSourceElement((Node?)node.Statement);
        CheckTruthinessExpression(node.Expression!);
    }

    /// <summary>ported from: src/compiler/checker.ts:45467-45473 — <c>checkWhileStatement</c>.</summary>
    internal void CheckWhileStatement(WhileStatement node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);

        CheckTruthinessExpression(node.Expression!);
        CheckSourceElement((Node?)node.Statement);
    }

    /// <summary>ported from: src/compiler/checker.ts:45475-45492 — <c>checkTruthinessOfType</c>.</summary>
    internal IType CheckTruthinessOfType(IType type, Node node)
    {
        if ((type.Flags & TypeFlags.Void) != 0)
        {
            Error(node, Diagnostics.An_expression_of_type_void_cannot_be_tested_for_truthiness);
        }
        else
        {
            var semantics = GetSyntacticTruthySemantics(node);
            if (semantics != PredicateSemantics.Sometimes)
            {
                Error(
                    node,
                    semantics == PredicateSemantics.Always
                        ? Diagnostics.This_kind_of_expression_is_always_truthy
                        : Diagnostics.This_kind_of_expression_is_always_falsy);
            }
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:45494-45528 — <c>getSyntacticTruthySemantics</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS's <c>skipOuterExpressions</c> is the <c>factory/utilities.ts</c> member the checker imports as a re-export; the landed port declaration is <c>FactoryUtilities.SkipOuterExpressions</c> (Parser/FactoryUtilities.cs:541), which is what <c>Utilities.SkipParentheses</c> also calls (<c>Parser/FactoryUtilities.cs</c> note at Utilities.cs:502). The <c>StringLiteral | NoSubstitutionTemplateLiteral</c> arm reads <c>text</c>, a slot both classes inherit from <see cref="LiteralExpression"/>, which is the face the port casts to.</para>
    /// </remarks>
    internal PredicateSemantics GetSyntacticTruthySemantics(Node node)
    {
        node = FactoryUtilities.SkipOuterExpressions(node);
        switch (node.Kind)
        {
            case SyntaxKind.NumericLiteral:
                // Allow `while(0)` or `while(1)`
                if (((NumericLiteral)node).Text is "0" or "1")
                {
                    return PredicateSemantics.Sometimes;
                }
                return PredicateSemantics.Always;
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.RegularExpressionLiteral:
                return PredicateSemantics.Always;
            case SyntaxKind.VoidExpression:
            case SyntaxKind.NullKeyword:
                return PredicateSemantics.Never;
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.StringLiteral:
                return Js.IsTruthy(((LiteralExpression)node).Text) ? PredicateSemantics.Always : PredicateSemantics.Never;
            case SyntaxKind.ConditionalExpression:
                return GetSyntacticTruthySemantics(((ConditionalExpression)node).WhenTrue!) | GetSyntacticTruthySemantics(((ConditionalExpression)node).WhenFalse!);
            case SyntaxKind.Identifier:
                if (ReferenceEquals(GetResolvedSymbol((Identifier)node), _undefinedSymbol))
                {
                    return PredicateSemantics.Never;
                }
                return PredicateSemantics.Sometimes;
        }
        return PredicateSemantics.Sometimes;
    }

    /// <summary>ported from: src/compiler/checker.ts:45530-45532 — <c>checkTruthinessExpression</c>.</summary>
    internal IType CheckTruthinessExpression(Expression node, CheckMode? checkMode = null)
        => CheckTruthinessOfType(CheckExpression(node, checkMode ?? CheckMode.Normal), node);

    /// <summary>ported from: src/compiler/checker.ts:45534-45557 — <c>checkForStatement</c>.</summary>
    internal void CheckForStatement(ForStatement node)
    {
        // Grammar checking
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if (node.Initializer is not null && node.Initializer.Kind == SyntaxKind.VariableDeclarationList)
            {
                CheckGrammarVariableDeclarationList((VariableDeclarationList)node.Initializer);
            }
        }

        if (node.Initializer is not null)
        {
            if (node.Initializer.Kind == SyntaxKind.VariableDeclarationList)
            {
                CheckVariableDeclarationList((VariableDeclarationList)node.Initializer);
            }
            else
            {
                CheckExpression((Expression)node.Initializer);
            }
        }

        if (node.Condition is not null) CheckTruthinessExpression(node.Condition);
        if (node.Incrementor is not null) CheckExpression(node.Incrementor);
        CheckSourceElement((Node?)node.Statement);
        if (node.Locals is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45559-45621 — <c>checkForOfStatement</c>.</summary>
    internal void CheckForOfStatement(ForOfStatement node)
    {
        CheckGrammarForInOrForOfStatement(node);

        var container = Utilities.GetContainingFunctionOrClassStaticBlock(node);
        if (node.AwaitModifier is not null)
        {
            if (container is not null && NodeTests.IsClassStaticBlockDeclaration(container))
            {
                GrammarErrorOnNode(node.AwaitModifier, Diagnostics.for_await_loops_cannot_be_used_inside_a_class_static_block);
            }
            else
            {
                var functionFlags = Utilities.GetFunctionFlags(container);
                if ((functionFlags & (FunctionFlags.Invalid | FunctionFlags.Async)) == FunctionFlags.Async && _languageVersion < ScriptTarget.ES2018) // LanguageFeatureMinimumTarget.ForAwaitOf
                {
                    // for..await..of in an async function or async generator function prior to ESNext requires the __asyncValues helper
                    CheckExternalEmitHelpers(node, ExternalEmitHelpers.ForAwaitOfIncludes);
                }
            }
        }
        else if (Js.IsTruthy(_compilerOptions.DownlevelIteration) && _languageVersion < ScriptTarget.ES2015) // LanguageFeatureMinimumTarget.ForOf
        {
            // for..of prior to ES2015 requires the __values helper when downlevelIteration is enabled
            CheckExternalEmitHelpers(node, ExternalEmitHelpers.ForOfIncludes);
        }

        // Check the LHS and RHS
        // If the LHS is a declaration, just check it as a variable declaration, which will in turn check the RHS
        // via checkRightHandSideOfForOf.
        // If the LHS is an expression, check the LHS, as a destructuring assignment or as a reference.
        // Then check that the RHS is assignable to it.
        if (node.Initializer!.Kind == SyntaxKind.VariableDeclarationList)
        {
            CheckVariableDeclarationList((VariableDeclarationList)node.Initializer!);
        }
        else
        {
            var varExpr = node.Initializer!;
            var iteratedType = CheckRightHandSideOfForOf(node);

            // There may be a destructuring assignment on the left side
            if (varExpr.Kind == SyntaxKind.ArrayLiteralExpression || varExpr.Kind == SyntaxKind.ObjectLiteralExpression)
            {
                // iteratedType may be undefined. In this case, we still want to check the structure of
                // varExpr, in particular making sure it's a valid LeftHandSideExpression. But we'd like
                // to short circuit the type relation checking as much as possible, so we pass the unknownType.
                CheckDestructuringAssignment((Expression)varExpr, iteratedType ?? _errorType);
            }
            else
            {
                var leftType = CheckExpression((Expression)varExpr);
                CheckReferenceExpression(
                    (Expression)varExpr,
                    Diagnostics.The_left_hand_side_of_a_for_of_statement_must_be_a_variable_or_a_property_access,
                    Diagnostics.The_left_hand_side_of_a_for_of_statement_may_not_be_an_optional_property_access);

                // iteratedType will be undefined if the rightType was missing properties/signatures
                // required to get its iteratedType (like [Symbol.iterator] or next). This may be
                // because we accessed properties from anyType, or it may have led to an error inside
                // getElementTypeOfIterable.
                if (iteratedType is not null)
                {
                    CheckTypeAssignableToAndOptionallyElaborate(iteratedType, leftType, varExpr, node.Expression);
                }
            }
        }

        CheckSourceElement((Node?)node.Statement);
        if (node.Locals is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45623-45673 — <c>checkForInStatement</c>.</summary>
    internal void CheckForInStatement(ForInStatement node)
    {
        // Grammar checking
        CheckGrammarForInOrForOfStatement(node);

        var rightType = GetNonNullableTypeIfNeeded(CheckExpression(node.Expression!));
        // TypeScript 1.0 spec (April 2014): 5.4
        // In a 'for-in' statement of the form
        // for (let VarDecl in Expr) Statement
        //   VarDecl must be a variable declaration without a type annotation that declares a variable of type Any,
        //   and Expr must be an expression of type Any, an object type, or a type parameter type.
        if (node.Initializer!.Kind == SyntaxKind.VariableDeclarationList)
        {
            var declarations = ((VariableDeclarationList)node.Initializer!).Declarations!;
            var variable = declarations.Count > 0 ? declarations[0] : null;
            if (variable is not null && Utilities.IsBindingPattern(variable.Name))
            {
                Error(variable.Name!, Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_destructuring_pattern);
            }
            CheckVariableDeclarationList((VariableDeclarationList)node.Initializer!);
        }
        else
        {
            // In a 'for-in' statement of the form
            // for (Var in Expr) Statement
            //   Var must be an expression classified as a reference of type Any or the String primitive type,
            //   and Expr must be an expression of type Any, an object type, or a type parameter type.
            var varExpr = node.Initializer!;
            var leftType = CheckExpression((Expression)varExpr);
            if (varExpr.Kind == SyntaxKind.ArrayLiteralExpression || varExpr.Kind == SyntaxKind.ObjectLiteralExpression)
            {
                Error(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_cannot_be_a_destructuring_pattern);
            }
            else if (!IsTypeAssignableTo(GetIndexTypeOrString(rightType), leftType))
            {
                Error(varExpr, Diagnostics.The_left_hand_side_of_a_for_in_statement_must_be_of_type_string_or_any);
            }
            else
            {
                // run check only former check succeeded to avoid cascading errors
                CheckReferenceExpression(
                    (Expression)varExpr,
                    Diagnostics.The_left_hand_side_of_a_for_in_statement_must_be_a_variable_or_a_property_access,
                    Diagnostics.The_left_hand_side_of_a_for_in_statement_may_not_be_an_optional_property_access);
            }
        }

        // unknownType is returned i.e. if node.expression is identifier whose name cannot be resolved
        // in this case error about missing name is already reported - do not report extra one
        if (ReferenceEquals(rightType, _neverType) || !IsTypeAssignableToKind(rightType, TypeFlags.NonPrimitive | TypeFlags.InstantiableNonPrimitive))
        {
            Error(node.Expression!, Diagnostics.The_right_hand_side_of_a_for_in_statement_must_be_of_type_any_an_object_type_or_a_type_parameter_but_here_has_type_0, Diagnostic.DeferArg(() => TypeToString(rightType)));
        }

        CheckSourceElement((Node?)node.Statement);
        if (node.Locals is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45675-45678 — <c>checkRightHandSideOfForOf</c>.</summary>
    internal IType CheckRightHandSideOfForOf(ForOfStatement statement)
    {
        var use = statement.AwaitModifier is not null ? IterationUse.ForAwaitOf : IterationUse.ForOf;
        return CheckIteratedTypeOrElementType(use, CheckNonNullExpression(statement.Expression!), _undefinedType, statement.Expression);
    }

    // ---------------------------------------------------------------------------------------------
    // The iterated type of a value (checker.ts:45680-45840)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:45680-45685 — <c>checkIteratedTypeOrElementType</c>.</summary>
    internal IType CheckIteratedTypeOrElementType(IterationUse use, IType inputType, IType sentType, Node? errorNode)
    {
        if (IsTypeAny(inputType))
        {
            return inputType;
        }
        return GetIteratedTypeOrElementType(use, inputType, sentType, errorNode, /*checkAssignability*/ true) ?? _anyType;
    }

    /// <summary>ported from: src/compiler/checker.ts:45692-45811 — <c>getIteratedTypeOrElementType</c>.</summary>
    /// <remarks>
    /// <para>When consuming an iterable type in a for..of, spread, or iterator destructuring assignment we want to get the iterated type of an iterable for ES2015 or later, or the iterated type of an iterable (if defined globally) or element type of an array like for ES2015 or earlier.</para>
    /// <para>PORT-NOTE: <c>compilerOptions.downlevelIteration</c> and <c>compilerOptions.noUncheckedIndexedAccess</c> are <c>bool?</c> slots in the generated options; the reference's <c>&amp;&amp;</c> over them is the port's <c>Js.IsTruthy</c> of the same option (<c>Checker.cs:100</c> uses the same helper for <c>experimentalDecorators</c>). The nested <c>getIterationDiagnosticDetails</c> answers TS's two-element array as a tuple, which is what its two call-site destructuring reads.</para>
    /// </remarks>
    internal IType? GetIteratedTypeOrElementType(IterationUse use, IType inputType, IType sentType, Node? errorNode, bool checkAssignability)
    {
        var allowAsyncIterables = (use & IterationUse.AllowsAsyncIterablesFlag) != 0;
        if (ReferenceEquals(inputType, _neverType))
        {
            if (errorNode is not null)
            {
                ReportTypeNotIterableError(errorNode, inputType, allowAsyncIterables);
            }
            return null;
        }

        var iterableExists = (!ReferenceEquals(GetGlobalIterableType(/*reportErrors*/ false), _emptyGenericType));
        var uplevelIteration = _languageVersion >= ScriptTarget.ES2015 && iterableExists;
        var downlevelIteration = (!uplevelIteration) && Js.IsTruthy(_compilerOptions.DownlevelIteration);
        var possibleOutOfBounds = Js.IsTruthy(_compilerOptions.NoUncheckedIndexedAccess) && (use & IterationUse.PossiblyOutOfBounds) != 0;

        // Get the iterated type of an `Iterable<T>` or `IterableIterator<T>` only in ES2015
        // or higher, when inside of an async generator or for-await-if, or when
        // downlevelIteration is requested.
        if (uplevelIteration || downlevelIteration || allowAsyncIterables)
        {
            // We only report errors for an invalid iterable type in ES2015 or higher.
            var iterationTypes = GetIterationTypesOfIterable(inputType, use, uplevelIteration ? errorNode : null);
            if (checkAssignability)
            {
                if (iterationTypes is not null)
                {
                    var diagnostic = (use & IterationUse.ForOfFlag) != 0 ? Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_for_of_will_always_send_0 :
                        (use & IterationUse.SpreadFlag) != 0 ? Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_array_spread_will_always_send_0 :
                        (use & IterationUse.DestructuringFlag) != 0 ? Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_array_destructuring_will_always_send_0 :
                        (use & IterationUse.YieldStarFlag) != 0 ? Diagnostics.Cannot_delegate_iteration_to_value_because_the_next_method_of_its_iterator_expects_type_1_but_the_containing_generator_will_always_send_0 :
                        null;
                    if (diagnostic is not null)
                    {
                        CheckTypeAssignableTo(sentType, iterationTypes.NextType!, errorNode, diagnostic);
                    }
                }
            }
            if (iterationTypes is not null || uplevelIteration)
            {
                return possibleOutOfBounds ? IncludeUndefinedInIndexSignature(iterationTypes?.YieldType) : iterationTypes?.YieldType;
            }
        }

        var arrayType = inputType;
        var hasStringConstituent = false;

        // If strings are permitted, remove any string-like constituents from the array type.
        // This allows us to find other non-string element types from an array unioned with
        // a string.
        if ((use & IterationUse.AllowsStringInputFlag) != 0)
        {
            if ((arrayType.Flags & TypeFlags.Union) != 0)
            {
                // After we remove all types that are StringLike, we will know if there was a string constituent
                // based on whether the result of filter is a new array.
                var arrayTypes = ((IUnionType)inputType).Types;
                var filteredTypes = Core.Filter(arrayTypes, t => (t.Flags & TypeFlags.StringLike) == 0);
                if (!ReferenceEquals(filteredTypes, arrayTypes))
                {
                    arrayType = GetUnionType((List<IType>)filteredTypes!, UnionReduction.Subtype);
                }
            }
            else if ((arrayType.Flags & TypeFlags.StringLike) != 0)
            {
                arrayType = _neverType;
            }

            hasStringConstituent = (!ReferenceEquals(arrayType, inputType));
            if (hasStringConstituent)
            {
                // Now that we've removed all the StringLike types, if no constituents remain, then the entire
                // arrayOrStringType was a string.
                if ((arrayType.Flags & TypeFlags.Never) != 0)
                {
                    return possibleOutOfBounds ? IncludeUndefinedInIndexSignature(_stringType) : _stringType;
                }
            }
        }

        if (!IsArrayLikeType(arrayType))
        {
            if (errorNode is not null)
            {
                // Which error we report depends on whether we allow strings or if there was a
                // string constituent. For example, if the input type is number | string, we
                // want to say that number is not an array type. But if the input was just
                // number and string input is allowed, we want to say that number is not an
                // array type or a string type.
                var allowsStrings = (use & IterationUse.AllowsStringInputFlag) != 0 && !hasStringConstituent;
                var (defaultDiagnostic, maybeMissingAwait) = GetIterationDiagnosticDetails(allowsStrings, downlevelIteration);
                ErrorAndMaybeSuggestAwait(
                    errorNode,
                    maybeMissingAwait && GetAwaitedTypeOfPromise(arrayType) is not null,
                    defaultDiagnostic,
                    Diagnostic.DeferArg(() => TypeToString(arrayType)));
            }
            return hasStringConstituent
                ? possibleOutOfBounds ? IncludeUndefinedInIndexSignature(_stringType) : _stringType
                : null;
        }

        var arrayElementType = GetIndexTypeOfType(arrayType, _numberType);
        if (hasStringConstituent && arrayElementType is not null)
        {
            // This is just an optimization for the case where arrayOrStringType is string | string[]
            if ((arrayElementType.Flags & TypeFlags.StringLike) != 0 && !Js.IsTruthy(_compilerOptions.NoUncheckedIndexedAccess))
            {
                return _stringType;
            }

            List<IType> constituents = possibleOutOfBounds
                ? [arrayElementType, _stringType, _undefinedType]
                : [arrayElementType, _stringType];
            return GetUnionType(constituents, UnionReduction.Subtype);
        }

        return (use & IterationUse.PossiblyOutOfBounds) != 0 ? IncludeUndefinedInIndexSignature(arrayElementType) : arrayElementType;

        (DiagnosticMessage, bool) GetIterationDiagnosticDetails(bool allowsStrings, bool downlevelIteration)
        {
            if (downlevelIteration)
            {
                return allowsStrings
                    ? (Diagnostics.Type_0_is_not_an_array_type_or_a_string_type_or_does_not_have_a_Symbol_iterator_method_that_returns_an_iterator, true)
                    : (Diagnostics.Type_0_is_not_an_array_type_or_does_not_have_a_Symbol_iterator_method_that_returns_an_iterator, true);
            }

            var yieldType = GetIterationTypeOfIterable(use, IterationTypeKind.Yield, inputType, /*errorNode*/ null);

            if (yieldType is not null)
            {
                return (Diagnostics.Type_0_can_only_be_iterated_through_when_using_the_downlevelIteration_flag_or_with_a_target_of_es2015_or_higher, false);
            }

            if (IsES2015OrLaterIterable(inputType.Symbol?.EscapedName))
            {
                return (Diagnostics.Type_0_can_only_be_iterated_through_when_using_the_downlevelIteration_flag_or_with_a_target_of_es2015_or_higher, true);
            }

            return allowsStrings
                ? (Diagnostics.Type_0_is_not_an_array_type_or_a_string_type, true)
                : (Diagnostics.Type_0_is_not_an_array_type, true);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:45813-45828 — <c>isES2015OrLaterIterable</c>.</summary>
    internal static bool IsES2015OrLaterIterable(string? n)
        => n is "Float32Array"
            or "Float64Array"
            or "Int16Array"
            or "Int32Array"
            or "Int8Array"
            or "NodeList"
            or "Uint16Array"
            or "Uint32Array"
            or "Uint8Array"
            or "Uint8ClampedArray";

    /// <summary>ported from: src/compiler/checker.ts:45833-45840 — <c>getIterationTypeOfIterable</c>.</summary>
    /// <remarks>
    /// <para>Gets the requested "iteration type" from an <c>Iterable</c>-like or <c>AsyncIterable</c>-like type.</para>
    /// </remarks>
    internal IType? GetIterationTypeOfIterable(IterationUse use, IterationTypeKind typeKind, IType inputType, Node? errorNode)
    {
        if (IsTypeAny(inputType))
        {
            return null;
        }

        var iterationTypes = GetIterationTypesOfIterable(inputType, use, errorNode);
        return iterationTypes is null ? null : IterationTypeOfKind(iterationTypes, typeKind);
    }

    /// <summary>ported from: src/compiler/checker.ts:45838 and :46487 — the <c>iterationTypes[getIterationTypesKeyFromIterationTypeKind(typeKind)]</c> read.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS indexes the <c>IterationTypes</c> record by the *name* of the slot (<c>"yieldType" | "returnType" | "nextType"</c>, checker.ts:54240-54251), because a JS object can be indexed by a string. C# cannot, so the port switches on the same <see cref="IterationTypeKind"/> to read the same slot, in the same order; the name-mapping helper itself stays with the band that owns checker.ts:54240.</para>
    /// </remarks>
    private static IType? IterationTypeOfKind(IterationTypes iterationTypes, IterationTypeKind typeKind)
        => typeKind switch
        {
            IterationTypeKind.Yield => iterationTypes.YieldType,
            IterationTypeKind.Return => iterationTypes.ReturnType,
            _ => iterationTypes.NextType,
        };

    // ---------------------------------------------------------------------------------------------
    // The iteration types of an iterable (checker.ts:45874-45984)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:45874-45897 — <c>combineIterationTypes</c>.</summary>
    /// <remarks>
    /// <para>Combines multiple <c>IterationTypes</c> records.</para>
    /// <para>If <paramref name="array"/> is empty or all elements are missing or are references to <c>noIterationTypes</c>, then <c>noIterationTypes</c> is returned. Otherwise, an <c>IterationTypes</c> record is returned for the combined iteration types.</para>
    /// <para>PORT-NOTE: the reference's <c>append(list, value)</c> grows and answers its first argument; the port's <c>Core.Append</c> answers <c>IList&lt;T&gt;?</c> (Core/Core.Arrays.cs:972), which is why each of the three reads carries the cast to the <c>List&lt;IType&gt;?</c> the reference's local is declared with. The two sentinel answers are the casts the file note describes.</para>
    /// </remarks>
    internal IterationTypes CombineIterationTypes(IList<IterationTypes> array)
    {
        List<IType>? yieldTypes = null;
        List<IType>? returnTypes = null;
        List<IType>? nextTypes = null;
        foreach (var iterationTypes in array)
        {
            if (iterationTypes is null || ReferenceEquals(iterationTypes, _noIterationTypes))
            {
                continue;
            }
            if (ReferenceEquals(iterationTypes, _anyIterationTypes))
            {
                return (IterationTypes)_anyIterationTypes;
            }
            yieldTypes = (List<IType>)Core.Append(yieldTypes, iterationTypes.YieldType)!;
            returnTypes = (List<IType>)Core.Append(returnTypes, iterationTypes.ReturnType)!;
            nextTypes = (List<IType>)Core.Append(nextTypes, iterationTypes.NextType)!;
        }
        if (yieldTypes is not null || returnTypes is not null || nextTypes is not null)
        {
            return CreateIterationTypes(
                yieldTypes is not null ? GetUnionType(yieldTypes) : null!,
                returnTypes is not null ? GetUnionType(returnTypes) : null!,
                nextTypes is not null ? GetIntersectionType(nextTypes) : null!);
        }
        return (IterationTypes)_noIterationTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:45928-45984 — <c>getIterationTypesOfIterable</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types from an <c>Iterable</c>-like or <c>AsyncIterable</c>-like type.</para>
    /// <para>At every level that involves analyzing return types of signatures, we union the return types of all the signatures. At any step of this process we could run into a dead end, meaning either the property is missing or we run into the <c>anyType</c>; if either of those happens, the helper returns <c>undefined</c> to signal that the iteration type could not be found. If a property is missing and the previous step did not result in <c>any</c>, the caller's request for errors is honoured before the answer.</para>
    /// <para>PORT-NOTE: TS's <c>{ errors: undefined, skipLogging: true }</c> and <c>{ errors: undefined }</c> literals are the port's <see cref="ErrorOutputContainer"/> (the one concrete implementation of the generated face, Checker.Relations.Entries.cs:1029); the conditional-initialized local keeps the reference's shape. The reference's <c>addRelatedInfo(rootDiag, ...errors)</c> spread is the port's collection-expression spread, the same spelling <c>Checker.Relations.Structured.cs:256</c> uses.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfIterable(IType type, IterationUse use, Node? errorNode)
    {
        type = GetReducedType(type);
        if (IsTypeAny(type))
        {
            return (IterationTypes)_anyIterationTypes;
        }

        if ((type.Flags & TypeFlags.Union) == 0)
        {
            IErrorOutputContainer? errorOutputContainer = errorNode is not null ? new ErrorOutputContainer { SkipLogging = true } : null;
            var iterationTypes = GetIterationTypesOfIterableWorker(type, use, errorNode, errorOutputContainer);
            if (ReferenceEquals(iterationTypes, _noIterationTypes))
            {
                if (errorNode is not null)
                {
                    var rootDiag = ReportTypeNotIterableError(errorNode, type, (use & IterationUse.AllowsAsyncIterablesFlag) != 0);
                    if (errorOutputContainer?.Errors is not null)
                    {
                        Utilities.AddRelatedInfo(rootDiag, [.. errorOutputContainer.Errors]);
                    }
                }
                return null;
            }
            else if (errorOutputContainer?.Errors?.Count > 0)
            {
                foreach (var diag in errorOutputContainer.Errors)
                {
                    _diagnostics.Add((Diagnostic)diag);
                }
            }
            return iterationTypes;
        }

        var cacheKey = (use & IterationUse.AllowsAsyncIterablesFlag) != 0 ? "iterationTypesOfAsyncIterable" : "iterationTypesOfIterable";
        var cachedTypes = GetCachedIterationTypes(type, cacheKey);
        if (cachedTypes is not null)
        {
            return ReferenceEquals(cachedTypes, _noIterationTypes) ? null : cachedTypes;
        }

        List<IterationTypes>? allIterationTypes = null;
        foreach (var constituent in ((IUnionType)type).Types!)
        {
            IErrorOutputContainer? errorOutputContainer = errorNode is not null ? new ErrorOutputContainer() : null;
            var constituentIterationTypes = GetIterationTypesOfIterableWorker(constituent, use, errorNode, errorOutputContainer);
            if (ReferenceEquals(constituentIterationTypes, _noIterationTypes))
            {
                if (errorNode is not null)
                {
                    var rootDiag = ReportTypeNotIterableError(errorNode, type, (use & IterationUse.AllowsAsyncIterablesFlag) != 0);
                    if (errorOutputContainer?.Errors is not null)
                    {
                        Utilities.AddRelatedInfo(rootDiag, [.. errorOutputContainer.Errors]);
                    }
                }
                SetCachedIterationTypes(type, cacheKey, (IterationTypes)_noIterationTypes);
                return null;
            }
            else if (errorOutputContainer?.Errors?.Count > 0)
            {
                foreach (var diag in errorOutputContainer.Errors)
                {
                    _diagnostics.Add((Diagnostic)diag);
                }
            }

            allIterationTypes = (List<IterationTypes>)Core.Append(allIterationTypes, constituentIterationTypes)!;
        }

        var iterationTypesForUnion = allIterationTypes is not null ? CombineIterationTypes(allIterationTypes) : (IterationTypes)_noIterationTypes;
        SetCachedIterationTypes(type, cacheKey, iterationTypesForUnion);
        return ReferenceEquals(iterationTypesForUnion, _noIterationTypes) ? null : iterationTypesForUnion;
    }

    /// <summary>ported from: src/compiler/checker.ts:45986-45999 — <c>getAsyncFromSyncIterationTypes</c>.</summary>
    internal IterationTypes GetAsyncFromSyncIterationTypes(IterationTypes iterationTypes, Node? errorNode)
    {
        if (ReferenceEquals(iterationTypes, _noIterationTypes)) return (IterationTypes)_noIterationTypes;
        if (ReferenceEquals(iterationTypes, _anyIterationTypes)) return (IterationTypes)_anyIterationTypes;
        var yieldType = iterationTypes.YieldType;
        var returnType = iterationTypes.ReturnType;
        var nextType = iterationTypes.NextType;
        // if we're requesting diagnostics, report errors for a missing `Awaited<T>`.
        if (errorNode is not null)
        {
            GetGlobalAwaitedSymbol(/*reportErrors*/ true);
        }
        return CreateIterationTypes(
            GetAwaitedType(yieldType!, errorNode) ?? _anyType,
            GetAwaitedType(returnType!, errorNode) ?? _anyType,
            nextType!);
    }

    /// <summary>ported from: src/compiler/checker.ts:46011-46075 — <c>getIterationTypesOfIterableWorker</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types from a non-union type.</para>
    /// <para>If no iteration types can be found, <c>noIterationTypes</c> is returned to indicate to the caller that it should report an error. Otherwise, an <c>IterationTypes</c> record is returned.</para>
    /// <para>The type is not a union; it is an <c>Iterable</c>-like or <c>AsyncIterable</c>-like type whose iteration types come either from a cache or from its members.</para>
    /// </remarks>
    internal IterationTypes GetIterationTypesOfIterableWorker(IType type, IterationUse use, Node? errorNode, IErrorOutputContainer? errorOutputContainer)
    {
        if (IsTypeAny(type))
        {
            return (IterationTypes)_anyIterationTypes;
        }

        // If we are reporting errors and encounter a cached `noIterationTypes`, we should ignore the cached value and continue as if nothing was cached.
        // In addition, we should not cache any new results for this call.
        var noCache = false;

        if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
        {
            var iterationTypes = GetIterationTypesOfIterableCached(type, AsyncIterationTypesResolver)
                ?? GetIterationTypesOfIterableFast(type, AsyncIterationTypesResolver);
            if (iterationTypes is not null)
            {
                if (ReferenceEquals(iterationTypes, _noIterationTypes) && errorNode is not null)
                {
                    // ignore the cached value
                    noCache = true;
                }
                else
                {
                    return (use & IterationUse.ForOfFlag) != 0
                        ? GetAsyncFromSyncIterationTypes(iterationTypes, errorNode)
                        : iterationTypes;
                }
            }
            iterationTypes = GetIterationTypesOfIterableSlow(type, AsyncIterationTypesResolver, errorNode, errorOutputContainer, noCache);
            if (!ReferenceEquals(iterationTypes, _noIterationTypes))
            {
                return iterationTypes;
            }
        }

        if ((use & IterationUse.AllowsSyncIterablesFlag) != 0)
        {
            var iterationTypes = GetIterationTypesOfIterableCached(type, SyncIterationTypesResolver)
                ?? GetIterationTypesOfIterableFast(type, SyncIterationTypesResolver);
            if (iterationTypes is not null)
            {
                if (ReferenceEquals(iterationTypes, _noIterationTypes) && errorNode is not null)
                {
                    // ignore the cached value
                    noCache = true;
                }
                else
                {
                    if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
                    {
                        // for a sync iterable in an async context, only use the cached types if they are valid.
                        if (!ReferenceEquals(iterationTypes, _noIterationTypes))
                        {
                            iterationTypes = GetAsyncFromSyncIterationTypes(iterationTypes, errorNode);
                            if (noCache) return iterationTypes;
                            SetCachedIterationTypes(type, "iterationTypesOfAsyncIterable", iterationTypes);
                            return iterationTypes;
                        }
                    }
                    else
                    {
                        return iterationTypes;
                    }
                }
            }

            iterationTypes = GetIterationTypesOfIterableSlow(type, SyncIterationTypesResolver, errorNode, errorOutputContainer, noCache);
            if (!ReferenceEquals(iterationTypes, _noIterationTypes))
            {
                if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
                {
                    iterationTypes = GetAsyncFromSyncIterationTypes(iterationTypes, errorNode);
                    if (noCache) return iterationTypes;
                    SetCachedIterationTypes(type, "iterationTypesOfAsyncIterable", iterationTypes);
                    return iterationTypes;
                }
                else
                {
                    return iterationTypes;
                }
            }
        }

        return (IterationTypes)_noIterationTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:46084-46086 — <c>getIterationTypesOfIterableCached</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types of an <c>Iterable</c>-like or <c>AsyncIterable</c>-like type from the cache.</para>
    /// <para>NOTE: You probably don't want to call this directly and should be calling</para>
    /// <para><c>getIterationTypesOfIterable</c> instead.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfIterableCached(IType type, IterationTypesResolver resolver)
        => GetCachedIterationTypes(type, resolver.IterableCacheKey);

    /// <summary>ported from: src/compiler/checker.ts:46100-46130 — <c>getIterationTypesOfIterableFast</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types of an <c>Iterable</c>-like or <c>AsyncIterable</c>-like type from common heuristics.</para>
    /// <para>If the type was previously analyzed and no iteration types were found, <c>noIterationTypes</c> is returned. If iteration types were found, an <c>IterationTypes</c> record is returned. Otherwise, <c>undefined</c> is returned to indicate to the caller that it should perform a more exhaustive analysis.</para>
    /// <para>As an optimization, if the type is an instantiation of one of the global</para>
    /// <para><c>Iterable&lt;T, TReturn, TNext&gt;</c>, <c>AsyncIterable&lt;T, TReturn, TNext&gt;</c>, <c>IteratorObject&lt;T, TReturn, TNext&gt;</c>, <c>AsyncIteratorObject&lt;T, TReturn, TNext&gt;</c>, <c>IterableIterator&lt;T, TReturn, TNext&gt;</c>, <c>AsyncIterableIterator&lt;T, TReturn, TNext&gt;</c>, <c>Generator&lt;T, TReturn, TNext&gt;</c> or <c>AsyncGenerator&lt;T, TReturn, TNext&gt;</c> types, the related type arguments are read directly; and if it is an instantiation of the global <c>ArrayIterator&lt;T&gt;</c>, <c>MapIterator&lt;T&gt;</c>, <c>SetIterator&lt;T&gt;</c>, <c>StringIterator&lt;T&gt;</c> or <c>ReadableStreamAsyncIterator&lt;T&gt;</c> types, its first type argument and the builtin iterator return type are.</para>
    /// <para>PORT-NOTE: TS's <c>return setCachedIterationTypes(…)</c> is spelled as the store followed by returning the stored value, and the resolver's <c>List&lt;IGenericType&gt;</c> is handed to the landed <c>IsReferenceToSomeType(IType?, IList&lt;IType&gt;)</c> through the covariant view of the same list; see the file note.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfIterableFast(IType type, IterationTypesResolver resolver)
    {
        if (IsReferenceToType(type, resolver.GetGlobalIterableType(/*reportErrors*/ false))
            || IsReferenceToType(type, resolver.GetGlobalIteratorObjectType(/*reportErrors*/ false))
            || IsReferenceToType(type, resolver.GetGlobalIterableIteratorType(/*reportErrors*/ false))
            || IsReferenceToType(type, resolver.GetGlobalGeneratorType(/*reportErrors*/ false)))
        {
            var typeArguments = GetTypeArguments((ITypeReference)type);
            var yieldType = typeArguments[0];
            var returnType = typeArguments[1];
            var nextType = typeArguments[2];
            var iterationTypes = CreateIterationTypes(
                resolver.ResolveIterationType(yieldType, /*errorNode*/ null) ?? yieldType,
                resolver.ResolveIterationType(returnType, /*errorNode*/ null) ?? returnType,
                nextType);
            SetCachedIterationTypes(type, resolver.IterableCacheKey, iterationTypes);
            return iterationTypes;
        }

        if (IsReferenceToSomeType(type, new List<IType>(resolver.GetGlobalBuiltinIteratorTypes())))
        {
            var typeArguments = GetTypeArguments((ITypeReference)type);
            var yieldType = typeArguments[0];
            var returnType = GetBuiltinIteratorReturnType();
            var nextType = _unknownType;
            var iterationTypes = CreateIterationTypes(
                resolver.ResolveIterationType(yieldType, /*errorNode*/ null) ?? yieldType,
                resolver.ResolveIterationType(returnType, /*errorNode*/ null) ?? returnType,
                nextType);
            SetCachedIterationTypes(type, resolver.IterableCacheKey, iterationTypes);
            return iterationTypes;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:46132-46136 — <c>getPropertyNameForKnownSymbolName</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS's answer is a <c>__String</c>; the port's <c>__String</c> is erased, so the property name is a <c>string</c>, as in <see cref="Utilities.GetPropertyNameFromType"/>.</para>
    /// </remarks>
    internal string GetPropertyNameForKnownSymbolName(string symbolName)
    {
        var ctorType = GetGlobalESSymbolConstructorSymbol(/*reportErrors*/ false);
        var uniqueType = ctorType is not null ? GetTypeOfPropertyOfType(GetTypeOfSymbol(ctorType), Utilities.EscapeLeadingUnderscores(symbolName)) : null;
        return uniqueType is not null && Utilities.IsTypeUsableAsPropertyName(uniqueType)
            ? Utilities.GetPropertyNameFromType(uniqueType)
            : $"__@{symbolName}";
    }

    /// <summary>ported from: src/compiler/checker.ts:46148-46167 — <c>getIterationTypesOfIterableSlow</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types of an <c>Iterable</c>-like or <c>AsyncIterable</c>-like type from its members.</para>
    /// <para>If the *yield*, *return*, and *next* types were found, an <c>IterationTypes</c> record is returned. Otherwise, <c>noIterationTypes</c> is returned.</para>
    /// </remarks>
    internal IterationTypes GetIterationTypesOfIterableSlow(IType type, IterationTypesResolver resolver, Node? errorNode, IErrorOutputContainer? errorOutputContainer, bool noCache)
    {
        var method = GetPropertyOfType(type, GetPropertyNameForKnownSymbolName(resolver.IteratorSymbolName));
        var methodType = method is not null && (method.Flags & SymbolFlags.Optional) == 0 ? GetTypeOfSymbol(method) : null;
        if (IsTypeAny(methodType))
        {
            if (noCache) return (IterationTypes)_anyIterationTypes;
            SetCachedIterationTypes(type, resolver.IterableCacheKey, (IterationTypes)_anyIterationTypes);
            return (IterationTypes)_anyIterationTypes;
        }

        List<ISignature>? allSignatures = methodType is not null ? GetSignaturesOfType(methodType, SignatureKind.Call) : null;
        var validSignatures = Core.Filter(allSignatures, sig => GetMinArgumentCount(sig) == 0);
        if (!Core.Some(validSignatures))
        {
            if (errorNode is not null && Core.Some(allSignatures))
            {
                CheckTypeAssignableTo(type, resolver.GetGlobalIterableType(/*reportErrors*/ true), errorNode, /*headMessage*/ null, /*containingMessageChain*/ null, errorOutputContainer);
            }
            if (noCache) return (IterationTypes)_noIterationTypes;
            SetCachedIterationTypes(type, resolver.IterableCacheKey, (IterationTypes)_noIterationTypes);
            return (IterationTypes)_noIterationTypes;
        }

        var iteratorType = GetIntersectionType(Core.Map(validSignatures, (sig, _) => GetReturnTypeOfSignature(sig))!);
        var iterationTypes = GetIterationTypesOfIteratorWorker(iteratorType, resolver, errorNode, errorOutputContainer, noCache) ?? (IterationTypes)_noIterationTypes;
        if (noCache) return iterationTypes;
        SetCachedIterationTypes(type, resolver.IterableCacheKey, iterationTypes);
        return iterationTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:46169-46185 — <c>reportTypeNotIterableError</c>.</summary>
    internal Diagnostic ReportTypeNotIterableError(Node errorNode, IType type, bool allowAsyncIterables)
    {
        var message = allowAsyncIterables
            ? Diagnostics.Type_0_must_have_a_Symbol_asyncIterator_method_that_returns_an_async_iterator
            : Diagnostics.Type_0_must_have_a_Symbol_iterator_method_that_returns_an_iterator;
        var suggestAwait =
            // for (const x of Promise<...>) or [...Promise<...>]
            GetAwaitedTypeOfPromise(type) is not null
            // for (const x of AsyncIterable<...>)
            || (!allowAsyncIterables
                && NodeTests.IsForOfStatement(errorNode.Parent!)
                && ReferenceEquals(((ForOfStatement)errorNode.Parent!).Expression, errorNode)
                && !ReferenceEquals(GetGlobalAsyncIterableType(/*reportErrors*/ false), _emptyGenericType)
                && IsTypeAssignableTo(type, CreateTypeFromGenericGlobalType(GetGlobalAsyncIterableType(/*reportErrors*/ false), [_anyType, _anyType, _anyType])));
        return ErrorAndMaybeSuggestAwait(errorNode, suggestAwait, message, Diagnostic.DeferArg(() => TypeToString(type)));
    }

    // ---------------------------------------------------------------------------------------------
    // The iteration types of an iterator (checker.ts:46193-46500)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:46193-46195 — <c>getIterationTypesOfIterator</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types from an <c>Iterator</c>-like or <c>AsyncIterator</c>-like type.</para>
    /// <para>If the *yield*, *return*, and *next* types were found, an <c>IterationTypes</c> record is returned.</para>
    /// <para>Otherwise, <c>undefined</c> is returned.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfIterator(IType type, IterationTypesResolver resolver, Node? errorNode, IErrorOutputContainer? errorOutputContainer)
        => GetIterationTypesOfIteratorWorker(type, resolver, errorNode, errorOutputContainer, /*noCache*/ false);

    /// <summary>ported from: src/compiler/checker.ts:46206-46221 — <c>getIterationTypesOfIteratorWorker</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types from an <c>Iterator</c>-like or <c>AsyncIterator</c>-like type.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfIteratorWorker(IType type, IterationTypesResolver resolver, Node? errorNode, IErrorOutputContainer? errorOutputContainer, bool noCache)
    {
        if (IsTypeAny(type))
        {
            return (IterationTypes)_anyIterationTypes;
        }

        var iterationTypes = GetIterationTypesOfIteratorCached(type, resolver)
            ?? GetIterationTypesOfIteratorFast(type, resolver);

        if (ReferenceEquals(iterationTypes, _noIterationTypes) && errorNode is not null)
        {
            iterationTypes = null;
            noCache = true;
        }

        iterationTypes ??= GetIterationTypesOfIteratorSlow(type, resolver, errorNode, errorOutputContainer, noCache);
        return ReferenceEquals(iterationTypes, _noIterationTypes) ? null : iterationTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:46230-46232 — <c>getIterationTypesOfIteratorCached</c>.</summary>
    /// <remarks>
    /// <para>Gets the iteration types of an <c>Iterator</c>-like or <c>AsyncIterator</c>-like type from the cache.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfIteratorCached(IType type, IterationTypesResolver resolver)
        => GetCachedIterationTypes(type, resolver.IteratorCacheKey);

    /// <summary>ported from: src/compiler/checker.ts:46246-46276 — <c>getIterationTypesOfIteratorFast</c>.</summary>
    /// <remarks>
    /// <para>Gets the iteration types of an <c>Iterator</c>-like or <c>AsyncIterator</c>-like type from the cache or from common heuristics.</para>
    /// <para>If the type was previously analyzed and no iteration types were found, <c>noIterationTypes</c> is returned. If iteration types were found, an <c>IterationTypes</c> record is returned. Otherwise, <c>undefined</c> is returned to indicate to the caller that it should perform a more exhaustive analysis.</para>
    /// <para>As an optimization, the type arguments of an instantiation of one of the global</para>
    /// <para><c>IterableIterator&lt;T, TReturn, TNext&gt;</c>, <c>AsyncIterableIterator&lt;T, TReturn, TNext&gt;</c>, <c>IteratorObject&lt;T, TReturn, TNext&gt;</c>, <c>AsyncIteratorObject&lt;T, TReturn, TNext&gt;</c>, <c>Iterator&lt;T, TReturn, TNext&gt;</c>, <c>AsyncIterator&lt;T, TReturn, TNext&gt;</c>, <c>Generator&lt;T, TReturn, TNext&gt;</c> or <c>AsyncGenerator&lt;T, TReturn, TNext&gt;</c> types are read directly; and the first type argument and the builtin iterator return type of an instantiation of the global <c>ArrayIterator&lt;T&gt;</c>, <c>MapIterator&lt;T&gt;</c>, <c>SetIterator&lt;T&gt;</c>, <c>StringIterator&lt;T&gt;</c> or <c>ReadableStreamAsyncIterator&lt;T&gt;</c> types are.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfIteratorFast(IType type, IterationTypesResolver resolver)
    {
        if (IsReferenceToType(type, resolver.GetGlobalIterableIteratorType(/*reportErrors*/ false))
            || IsReferenceToType(type, resolver.GetGlobalIteratorType(/*reportErrors*/ false))
            || IsReferenceToType(type, resolver.GetGlobalIteratorObjectType(/*reportErrors*/ false))
            || IsReferenceToType(type, resolver.GetGlobalGeneratorType(/*reportErrors*/ false)))
        {
            var typeArguments = GetTypeArguments((ITypeReference)type);
            var yieldType = typeArguments[0];
            var returnType = typeArguments[1];
            var nextType = typeArguments[2];
            var iterationTypes = CreateIterationTypes(yieldType, returnType, nextType);
            SetCachedIterationTypes(type, resolver.IteratorCacheKey, iterationTypes);
            return iterationTypes;
        }

        if (IsReferenceToSomeType(type, new List<IType>(resolver.GetGlobalBuiltinIteratorTypes())))
        {
            var typeArguments = GetTypeArguments((ITypeReference)type);
            var yieldType = typeArguments[0];
            var returnType = GetBuiltinIteratorReturnType();
            var nextType = _unknownType;
            var iterationTypes = CreateIterationTypes(yieldType, returnType, nextType);
            SetCachedIterationTypes(type, resolver.IteratorCacheKey, iterationTypes);
            return iterationTypes;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:46278-46285 — <c>isIteratorResult</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: the reference's <c>getTypeOfPropertyOfType(type, "done") || falseType</c> is the port's null coalescing over the same lookup; the missing property answers <c>undefined</c> on both sides.</para>
    /// </remarks>
    internal bool IsIteratorResult(IType type, IterationTypeKind kind)
    {
        // From https://tc39.github.io/ecma262/#sec-iteratorresult-interface:
        // > [done] is the result status of an iterator `next` method call. If the end of the iterator was reached `done` is `true`.
        // > If the end was not reached `done` is `false` and a value is available.
        // > If a `done` property (either own or inherited) does not exist, it is consider to have the value `false`.
        var doneType = GetTypeOfPropertyOfType(type, "done") ?? _falseType;
        return IsTypeAssignableTo(kind == IterationTypeKind.Yield ? _falseType : _trueType, doneType);
    }

    /// <summary>ported from: src/compiler/checker.ts:46287-46289 — <c>isYieldIteratorResult</c>.</summary>
    internal bool IsYieldIteratorResult(IType type)
        => IsIteratorResult(type, IterationTypeKind.Yield);

    /// <summary>ported from: src/compiler/checker.ts:46291-46293 — <c>isReturnIteratorResult</c>.</summary>
    internal bool IsReturnIteratorResult(IType type)
        => IsIteratorResult(type, IterationTypeKind.Return);

    /// <summary>ported from: src/compiler/checker.ts:46302-46339 — <c>getIterationTypesOfIteratorResult</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield* and *return* types of an <c>IteratorResult</c>-like type.</para>
    /// <para>If a *yield* or a *return* type cannot be determined, <c>noIterationTypes</c> is returned to indicate to the caller that it should handle the error. Otherwise, an <c>IterationTypes</c> record is returned.</para>
    /// <para>From https://tc39.github.io/ecma262/#sec-iteratorresult-interface: if the iterator does not have a return</para>
    /// <para>value, <c>value</c> is <c>undefined</c>; in that case the <c>value</c> property may be absent from the conforming object if it does not inherit an explicit <c>value</c> property.</para>
    /// </remarks>
    internal IterationTypes GetIterationTypesOfIteratorResult(IType type)
    {
        if (IsTypeAny(type))
        {
            return (IterationTypes)_anyIterationTypes;
        }

        var cachedTypes = GetCachedIterationTypes(type, "iterationTypesOfIteratorResult");
        if (cachedTypes is not null)
        {
            return cachedTypes;
        }

        // As an optimization, if the type is an instantiation of one of the global `IteratorYieldResult<T>`
        // or `IteratorReturnResult<TReturn>` types, then just grab its type argument.
        if (IsReferenceToType(type, GetGlobalIteratorYieldResultType(/*reportErrors*/ false)))
        {
            var yieldType = GetTypeArguments((ITypeReference)type)[0];
            var iterationTypes = CreateIterationTypes(yieldType, /*returnType*/ null!, /*nextType*/ null!);
            SetCachedIterationTypes(type, "iterationTypesOfIteratorResult", iterationTypes);
            return iterationTypes;
        }
        if (IsReferenceToType(type, GetGlobalIteratorReturnResultType(/*reportErrors*/ false)))
        {
            var returnType = GetTypeArguments((ITypeReference)type)[0];
            var iterationTypes = CreateIterationTypes(/*yieldType*/ null!, returnType, /*nextType*/ null!);
            SetCachedIterationTypes(type, "iterationTypesOfIteratorResult", iterationTypes);
            return iterationTypes;
        }

        // Choose any constituents that can produce the requested iteration type.
        var yieldIteratorResult = FilterType(type, IsYieldIteratorResult);
        var yieldTypeForResult = (!ReferenceEquals(yieldIteratorResult, _neverType)) ? GetTypeOfPropertyOfType(yieldIteratorResult, "value") : null;

        var returnIteratorResult = FilterType(type, IsReturnIteratorResult);
        var returnTypeForResult = (!ReferenceEquals(returnIteratorResult, _neverType)) ? GetTypeOfPropertyOfType(returnIteratorResult, "value") : null;

        if (yieldTypeForResult is null && returnTypeForResult is null)
        {
            SetCachedIterationTypes(type, "iterationTypesOfIteratorResult", (IterationTypes)_noIterationTypes);
            return (IterationTypes)_noIterationTypes;
        }

        var resultTypes = CreateIterationTypes(yieldTypeForResult!, returnTypeForResult ?? _voidType, /*nextType*/ null!);
        SetCachedIterationTypes(type, "iterationTypesOfIteratorResult", resultTypes);
        return resultTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:46348-46456 — <c>getIterationTypesOfMethod</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types of the <c>next()</c>, <c>return()</c> or <c>throw()</c> method of an <c>Iterator</c>-like or <c>AsyncIterator</c>-like type.</para>
    /// <para>If the *yield*, *return*, and *next* types were found, an <c>IterationTypes</c> record is returned. Otherwise, <c>undefined</c> is returned.</para>
    /// <para>PORT-NOTE: the reference's <c>methodName: "next" | "return" | "throw"</c> is the port's <c>string</c>, the three names being the three property lookups the member performs. The globals' member tables are <see cref="SymbolTable"/> and are read with <c>TryGetValue</c>, which is the port's <c>Map.get</c>-answers-undefined shape; the anonymous type's <c>mapper</c> is read off <see cref="IAnonymousType"/> and narrowed to the checker's <c>TypeMapper</c> class.</para>
    /// </remarks>
    internal IterationTypes? GetIterationTypesOfMethod(IType type, IterationTypesResolver resolver, string methodName, Node? errorNode, IErrorOutputContainer? errorOutputContainer)
    {
        var method = GetPropertyOfType(type, methodName);

        // Ignore 'return' or 'throw' if they are missing.
        if (method is null && methodName != "next")
        {
            return null;
        }

        var methodType = method is not null && !(methodName == "next" && (method.Flags & SymbolFlags.Optional) != 0)
            ? methodName == "next" ? GetTypeOfSymbol(method) : GetTypeWithFacts(GetTypeOfSymbol(method), TypeFacts.NEUndefinedOrNull)
            : null;

        if (IsTypeAny(methodType))
        {
            return (IterationTypes)_anyIterationTypes;
        }

        // Both async and non-async iterators *must* have a `next` method.
        List<ISignature> methodSignatures = methodType is not null ? GetSignaturesOfType(methodType, SignatureKind.Call) : [];
        if (methodSignatures.Count == 0)
        {
            if (errorNode is not null)
            {
                var diagnostic = methodName == "next"
                    ? resolver.MustHaveANextMethodDiagnostic
                    : resolver.MustBeAMethodDiagnostic;
                if (errorOutputContainer is not null)
                {
                    errorOutputContainer.Errors ??= [];
                    errorOutputContainer.Errors.Add(Utilities.CreateDiagnosticForNode(errorNode, diagnostic, methodName));
                }
                else
                {
                    Error(errorNode, diagnostic, methodName);
                }
            }
            return methodName == "next" ? (IterationTypes)_noIterationTypes : null;
        }

        // If the method signature comes exclusively from the global iterator or generator type,
        // create iteration types from its type arguments like `getIterationTypesOfIteratorFast`
        // does (so as to remove `undefined` from the next and return types). We arrive here when
        // a contextual type for a generator was not a direct reference to one of those global types,
        // but looking up `methodType` referred to one of them (and nothing else). E.g., in
        // `interface SpecialIterator extends Iterator<number> {}`, `SpecialIterator` is not a
        // reference to `Iterator`, but its `next` member derives exclusively from `Iterator`.
        if (methodType is { Symbol: not null } && methodSignatures.Count == 1)
        {
            var globalGeneratorType = resolver.GetGlobalGeneratorType(/*reportErrors*/ false);
            var globalIteratorType = resolver.GetGlobalIteratorType(/*reportErrors*/ false);
            var generatorMembers = globalGeneratorType.Symbol?.Members;
            var iteratorMembers = globalIteratorType.Symbol?.Members;
            var isGeneratorMethod = generatorMembers is not null
                && generatorMembers.TryGetValue(methodName, out var generatorMember)
                && ReferenceEquals(generatorMember, methodType.Symbol);
            var isIteratorMethod = (!isGeneratorMethod)
                && iteratorMembers is not null
                && iteratorMembers.TryGetValue(methodName, out var iteratorMember)
                && ReferenceEquals(iteratorMember, methodType.Symbol);
            if (isGeneratorMethod || isIteratorMethod)
            {
                var globalType = isGeneratorMethod ? globalGeneratorType : globalIteratorType;
                var mapper = (TypeMapper)((IAnonymousType)methodType).Mapper!;
                return CreateIterationTypes(
                    GetMappedType(globalType.TypeParameters![0], mapper),
                    GetMappedType(globalType.TypeParameters![1], mapper),
                    methodName == "next" ? GetMappedType(globalType.TypeParameters![2], mapper) : null!);
            }
        }

        // Extract the first parameter and return type of each signature.
        List<IType>? methodParameterTypes = null;
        List<IType>? methodReturnTypes = null;
        foreach (var signature in methodSignatures)
        {
            if (methodName != "throw" && Core.Some(signature.Parameters))
            {
                methodParameterTypes = (List<IType>)Core.Append(methodParameterTypes, GetTypeAtPosition(signature, 0))!;
            }
            methodReturnTypes = (List<IType>)Core.Append(methodReturnTypes, GetReturnTypeOfSignature(signature))!;
        }

        // Resolve the *next* or *return* type from the first parameter of a `next()` or
        // `return()` method, respectively.
        List<IType>? returnTypes = null;
        IType? nextType = null;
        if (methodName != "throw")
        {
            var methodParameterType = methodParameterTypes is not null ? GetUnionType(methodParameterTypes) : _unknownType;
            if (methodName == "next")
            {
                // The value of `next(value)` is *not* awaited by async generators
                nextType = methodParameterType;
            }
            else if (methodName == "return")
            {
                // The value of `return(value)` *is* awaited by async generators
                var resolvedMethodParameterType = resolver.ResolveIterationType(methodParameterType, errorNode) ?? _anyType;
                returnTypes = (List<IType>)Core.Append(returnTypes, resolvedMethodParameterType)!;
            }
        }

        // Resolve the *yield* and *return* types from the return type of the method (i.e. `IteratorResult`)
        IType yieldType;
        var methodReturnType = methodReturnTypes is not null ? GetIntersectionType(methodReturnTypes) : _neverType;
        var resolvedMethodReturnType = resolver.ResolveIterationType(methodReturnType, errorNode) ?? _anyType;
        var iterationTypes = GetIterationTypesOfIteratorResult(resolvedMethodReturnType);
        if (ReferenceEquals(iterationTypes, _noIterationTypes))
        {
            if (errorNode is not null)
            {
                if (errorOutputContainer is not null)
                {
                    errorOutputContainer.Errors ??= [];
                    errorOutputContainer.Errors.Add(Utilities.CreateDiagnosticForNode(errorNode, resolver.MustHaveAValueDiagnostic, methodName));
                }
                else
                {
                    Error(errorNode, resolver.MustHaveAValueDiagnostic, methodName);
                }
            }
            yieldType = _anyType;
            returnTypes = (List<IType>)Core.Append(returnTypes, _anyType)!;
        }
        else
        {
            yieldType = iterationTypes.YieldType!;
            returnTypes = (List<IType>)Core.Append(returnTypes, iterationTypes.ReturnType!)!;
        }

        return CreateIterationTypes(yieldType, GetUnionType(returnTypes!), nextType!);
    }

    /// <summary>ported from: src/compiler/checker.ts:46468-46475 — <c>getIterationTypesOfIteratorSlow</c>.</summary>
    /// <remarks>
    /// <para>Gets the *yield*, *return*, and *next* types of an <c>Iterator</c>-like or <c>AsyncIterator</c>-like type from its members.</para>
    /// </remarks>
    internal IterationTypes GetIterationTypesOfIteratorSlow(IType type, IterationTypesResolver resolver, Node? errorNode, IErrorOutputContainer? errorOutputContainer, bool noCache)
    {
        var iterationTypes = CombineIterationTypes(
        [
            GetIterationTypesOfMethod(type, resolver, "next", errorNode, errorOutputContainer)!,
            GetIterationTypesOfMethod(type, resolver, "return", errorNode, errorOutputContainer)!,
            GetIterationTypesOfMethod(type, resolver, "throw", errorNode, errorOutputContainer)!,
        ]);
        if (noCache) return iterationTypes;
        SetCachedIterationTypes(type, resolver.IteratorCacheKey, iterationTypes);
        return iterationTypes;
    }

    /// <summary>ported from: src/compiler/checker.ts:46482-46489 — <c>getIterationTypeOfGeneratorFunctionReturnType</c>.</summary>
    /// <remarks>
    /// <para>Gets the requested "iteration type" from a type that is either <c>Iterable</c>-like, <c>Iterator</c>-like, <c>IterableIterator</c>-like, or <c>Generator</c>-like (for a non-async generator); or <c>AsyncIterable</c>-like, <c>AsyncIterator</c>-like, <c>AsyncIterableIterator</c>-like, or <c>AsyncGenerator</c>-like (for an async generator).</para>
    /// </remarks>
    internal IType? GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind kind, IType returnType, bool isAsyncGenerator)
    {
        if (IsTypeAny(returnType))
        {
            return null;
        }

        var iterationTypes = GetIterationTypesOfGeneratorFunctionReturnType(returnType, isAsyncGenerator);
        return iterationTypes is null ? null : IterationTypeOfKind(iterationTypes, kind);
    }

    /// <summary>ported from: src/compiler/checker.ts:46491-46500 — <c>getIterationTypesOfGeneratorFunctionReturnType</c>.</summary>
    internal IterationTypes? GetIterationTypesOfGeneratorFunctionReturnType(IType type, bool isAsyncGenerator)
    {
        if (IsTypeAny(type))
        {
            return (IterationTypes)_anyIterationTypes;
        }

        var use = isAsyncGenerator ? IterationUse.AsyncGeneratorReturnType : IterationUse.GeneratorReturnType;
        var resolver = isAsyncGenerator ? AsyncIterationTypesResolver : SyncIterationTypesResolver;
        return GetIterationTypesOfIterable(type, use, /*errorNode*/ null)
            ?? GetIterationTypesOfIterator(type, resolver, /*errorNode*/ null, /*errorOutputContainer*/ null);
    }

    // ---------------------------------------------------------------------------------------------
    // The return/with/switch/labeled/throw/try checks (checker.ts:46502-46749)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:46502-46507 — <c>checkBreakOrContinueStatement</c>.</summary>
    internal void CheckBreakOrContinueStatement(Node node)
    {
        // Grammar checking
        if (!CheckGrammarStatementInAmbientContext(node)) CheckGrammarBreakOrContinueStatement(node);

        // TODO: Check that target label is valid
    }

    /// <summary>ported from: src/compiler/checker.ts:46509-46520 — <c>unwrapReturnType</c>.</summary>
    internal IType? UnwrapReturnType(IType returnType, FunctionFlags functionFlags)
    {
        var isGenerator = (functionFlags & FunctionFlags.Generator) != 0;
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        if (isGenerator)
        {
            var returnIterationType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, isAsync);
            if (returnIterationType is null)
            {
                return _errorType;
            }
            return isAsync ? GetAwaitedTypeNoAlias(UnwrapAwaitedType(returnIterationType)) : returnIterationType;
        }
        return isAsync ? GetAwaitedTypeNoAlias(returnType) ?? _errorType : returnType;
    }

    /// <summary>ported from: src/compiler/checker.ts:46522-46525 — <c>isUnwrappedReturnTypeUndefinedVoidOrAny</c>.</summary>
    internal bool IsUnwrappedReturnTypeUndefinedVoidOrAny(Node func, IType returnType)
    {
        var type = UnwrapReturnType(returnType, Utilities.GetFunctionFlags(func));
        return type is not null
            && (MaybeTypeOfKind(type, TypeFlags.Void) || (type.Flags & (TypeFlags.Any | TypeFlags.Undefined)) != 0);
    }

    /// <summary>ported from: src/compiler/checker.ts:46527-46568 — <c>checkReturnStatement</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: the reference declares a second <c>exprType</c> inside the constructor arm, shadowing the first; C# forbids a local from shadowing an enclosing one, and the two expressions are the same read of the same node, so the port uses the first local in both arms.</para>
    /// </remarks>
    internal void CheckReturnStatement(ReturnStatement node)
    {
        // Grammar checking
        if (CheckGrammarStatementInAmbientContext(node))
        {
            return;
        }

        var container = Utilities.GetContainingFunctionOrClassStaticBlock(node);
        if (container is not null && NodeTests.IsClassStaticBlockDeclaration(container))
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_return_statement_cannot_be_used_inside_a_class_static_block);
            return;
        }

        if (container is null)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_return_statement_can_only_be_used_within_a_function_body);
            return;
        }

        var signature = GetSignatureFromDeclaration(container);
        var returnType = GetReturnTypeOfSignature(signature);
        if (_strictNullChecks || node.Expression is not null || (returnType.Flags & TypeFlags.Never) != 0)
        {
            var exprType = node.Expression is not null ? CheckExpressionCached(node.Expression) : _undefinedType;
            if (container.Kind == SyntaxKind.SetAccessor)
            {
                if (node.Expression is not null)
                {
                    Error(node, Diagnostics.Setters_cannot_return_a_value);
                }
            }
            else if (container.Kind == SyntaxKind.Constructor)
            {
                if (node.Expression is not null && !CheckTypeAssignableToAndOptionallyElaborate(exprType, returnType, node, node.Expression))
                {
                    Error(node, Diagnostics.Return_type_of_constructor_signature_must_be_assignable_to_the_instance_type_of_the_class);
                }
            }
            else if (GetReturnTypeFromAnnotation(container) is not null)
            {
                var unwrappedReturnType = UnwrapReturnType(returnType, Utilities.GetFunctionFlags(container)) ?? returnType;
                CheckReturnExpression(container, unwrappedReturnType, node, node.Expression, exprType);
            }
        }
        else if (container.Kind != SyntaxKind.Constructor && Js.IsTruthy(_compilerOptions.NoImplicitReturns) && !IsUnwrappedReturnTypeUndefinedVoidOrAny(container, returnType))
        {
            // The function has a return type, but the return statement doesn't have an expression.
            Error(node, Diagnostics.Not_all_code_paths_return_a_value);
        }
    }

    // When checking an arrow expression such as `(x) => exp`, then `node` is the expression `exp`.
    // Otherwise, `node` is a return statement.
    /// <summary>ported from: src/compiler/checker.ts:46572-46605 — <c>checkReturnExpression</c>.</summary>
    internal void CheckReturnExpression(
        Node container,
        IType unwrappedReturnType,
        Node node,
        Expression? expr,
        IType exprType,
        bool inConditionalExpression = false)
    {
        var excludeJSDocTypeAssertions = Utilities.IsInJSFile(node);
        var functionFlags = Utilities.GetFunctionFlags(container);
        if (expr is not null)
        {
            var unwrappedExpr = Utilities.SkipParentheses(expr, excludeJSDocTypeAssertions);
            if (NodeTests.IsConditionalExpression(unwrappedExpr))
            {
                CheckReturnExpression(container, unwrappedReturnType, node, ((ConditionalExpression)unwrappedExpr).WhenTrue!, CheckExpression(((ConditionalExpression)unwrappedExpr).WhenTrue!), /*inConditionalExpression*/ true);
                CheckReturnExpression(container, unwrappedReturnType, node, ((ConditionalExpression)unwrappedExpr).WhenFalse!, CheckExpression(((ConditionalExpression)unwrappedExpr).WhenFalse!), /*inConditionalExpression*/ true);
                return;
            }
        }

        var inReturnStatement = node.Kind == SyntaxKind.ReturnStatement;
        var unwrappedExprType = (functionFlags & FunctionFlags.Async) != 0
            ? CheckAwaitedType(
                exprType,
                /*withAlias*/ false,
                node,
                Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member)
            : exprType;

        var effectiveExpr = expr is not null ? GetEffectiveCheckNode(expr) : null; // The effective expression for diagnostics purposes.
        var errorNode = inReturnStatement && !inConditionalExpression ? node : (Node?)effectiveExpr;

        CheckTypeAssignableToAndOptionallyElaborate(unwrappedExprType, unwrappedReturnType, errorNode, effectiveExpr);
    }

    /// <summary>ported from: src/compiler/checker.ts:46607-46623 — <c>checkWithStatement</c>.</summary>
    internal void CheckWithStatement(WithStatement node)
    {
        // Grammar checking for withStatement
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if ((node.Flags & NodeFlags.AwaitContext) != 0)
            {
                GrammarErrorOnFirstToken(node, Diagnostics.with_statements_are_not_allowed_in_an_async_function_block);
            }
        }

        CheckExpression(node.Expression!);

        var sourceFile = Utilities.GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            var start = Utilities.GetSpanOfTokenAtPosition(sourceFile, node.Pos).Start;
            var end = ((Node)node.Statement!).Pos;
            GrammarErrorAtPos(sourceFile, start, end - start, Diagnostics.The_with_statement_is_not_supported_All_symbols_in_a_with_block_will_have_type_any);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:46625-46671 — <c>checkSwitchStatement</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS's <c>clause.statements</c> and <c>clause.fallthroughFlowNode</c> are read off the <c>CaseOrDefaultClause</c> union, for which the model has no face; the port reads both slots with the same two-kind switch the reference's own <c>clause.kind</c> tests distinguish.</para>
    /// </remarks>
    internal void CheckSwitchStatement(SwitchStatement node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);

        Node? firstDefaultClause = null;
        var hasDuplicateDefaultClause = false;

        var expressionType = CheckExpression(node.Expression!);

        foreach (var clause in node.CaseBlock!.Clauses!)
        {
            // Grammar check for duplicate default clauses, skip if we already report duplicate default clause
            if (clause.Kind == SyntaxKind.DefaultClause && !hasDuplicateDefaultClause)
            {
                if (firstDefaultClause is null)
                {
                    firstDefaultClause = clause;
                }
                else
                {
                    GrammarErrorOnNode(clause, Diagnostics.A_default_clause_cannot_appear_more_than_once_in_a_switch_statement);
                    hasDuplicateDefaultClause = true;
                }
            }

            if (clause.Kind == SyntaxKind.CaseClause)
            {
                AddLazyDiagnostic(CreateLazyCaseClauseDiagnostics((CaseClause)clause));
            }
            var clauseStatements = clause switch
            {
                CaseClause caseClause => caseClause.Statements,
                DefaultClause defaultClause => defaultClause.Statements,
                _ => null,
            };
            foreach (var statement in clauseStatements!)
            {
                CheckSourceElement(statement);
            }
            var fallthroughFlowNode = clause switch
            {
                CaseClause caseClause => caseClause.FallthroughFlowNode,
                DefaultClause defaultClause => defaultClause.FallthroughFlowNode,
                _ => null,
            };
            if (Js.IsTruthy(_compilerOptions.NoFallthroughCasesInSwitch) && fallthroughFlowNode is not null && IsReachableFlowNode(fallthroughFlowNode))
            {
                Error(clause, Diagnostics.Fallthrough_case_in_switch);
            }

            Action CreateLazyCaseClauseDiagnostics(CaseClause clause)
            {
                return () =>
                {
                    // TypeScript 1.0 spec (April 2014): 5.9
                    // In a 'switch' statement, each 'case' expression must be of a type that is comparable
                    // to or from the type of the 'switch' expression.
                    var caseType = CheckExpression(clause.Expression!);

                    if (!IsTypeEqualityComparableTo(expressionType, caseType))
                    {
                        // expressionType is not comparable to caseType, try the reversed check and report errors if it fails
                        CheckTypeComparableTo(caseType, expressionType, clause.Expression!, /*headMessage*/ null);
                    }
                };
            }
        }
        if (node.CaseBlock.Locals is not null)
        {
            RegisterForUnusedIdentifiersCheck(node.CaseBlock);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:46673-46694 — <c>checkLabeledStatement</c>.</summary>
    internal void CheckLabeledStatement(LabeledStatement node)
    {
        // Grammar checking
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            Utilities.FindAncestor(node.Parent, (Func<Node, bool?>)(current =>
            {
                if (Utilities.IsFunctionLike(current))
                {
                    return null;
                }
                if (current.Kind == SyntaxKind.LabeledStatement && ((LabeledStatement)current).Label!.EscapedText == node.Label!.EscapedText)
                {
                    GrammarErrorOnNode(node.Label!, Diagnostics.Duplicate_label_0, Utilities.GetTextOfNode(node.Label!));
                    return true;
                }
                return false;
            }));
        }

        if ((node.Label!.Flags & NodeFlags.Unreachable) != 0 && _compilerOptions.AllowUnusedLabels != true)
        {
            ErrorOrSuggestion(_compilerOptions.AllowUnusedLabels == false, node.Label!, Diagnostics.Unused_label);
        }

        // ensure that label is unique
        CheckSourceElement((Node?)node.Statement);
    }

    /// <summary>ported from: src/compiler/checker.ts:46696-46707 — <c>checkThrowStatement</c>.</summary>
    internal void CheckThrowStatement(ThrowStatement node)
    {
        // Grammar checking
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if (node.Expression is Identifier expressionIdentifier && !Js.IsTruthy(expressionIdentifier.EscapedText))
            {
                GrammarErrorAfterFirstToken(node, Diagnostics.Line_break_not_permitted_here);
            }
        }

        if (node.Expression is not null)
        {
            CheckExpression(node.Expression);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:46709-46749 — <c>checkTryStatement</c>.</summary>
    internal void CheckTryStatement(TryStatement node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);

        CheckBlock(node.TryBlock!);
        var catchClause = node.CatchClause;
        if (catchClause is not null)
        {
            // Grammar checking
            if (catchClause.VariableDeclaration is not null)
            {
                var declaration = catchClause.VariableDeclaration;
                CheckVariableLikeDeclaration(declaration);
                var typeNode = Utilities.GetEffectiveTypeAnnotationNode(declaration);
                if (typeNode is not null)
                {
                    var type = GetTypeFromTypeNode(typeNode);
                    if (type is not null && (type.Flags & TypeFlags.AnyOrUnknown) == 0)
                    {
                        GrammarErrorOnFirstToken((Node)typeNode, Diagnostics.Catch_clause_variable_type_annotation_must_be_any_or_unknown_if_specified);
                    }
                }
                else if (declaration.Initializer is not null)
                {
                    GrammarErrorOnFirstToken(declaration.Initializer, Diagnostics.Catch_clause_variable_cannot_have_an_initializer);
                }
                else
                {
                    var blockLocals = catchClause.Block!.Locals;
                    if (blockLocals is not null)
                    {
                        Utilities.ForEachKey<string, object>(catchClause.Locals!.Keys, (string caughtName) =>
                        {
                            var blockLocal = blockLocals.TryGetValue(caughtName, out var found) ? found : null;
                            if (blockLocal?.ValueDeclaration is not null && (blockLocal.Flags & SymbolFlags.BlockScopedVariable) != 0)
                            {
                                GrammarErrorOnNode((Node)blockLocal.ValueDeclaration, Diagnostics.Cannot_redeclare_identifier_0_in_catch_clause, Utilities.UnescapeLeadingUnderscores(caughtName));
                            }
                            return null;
                        });
                    }
                }
            }

            CheckBlock(catchClause.Block!);
        }

        if (node.FinallyBlock is not null)
        {
            CheckBlock(node.FinallyBlock);
        }
    }
    /// <summary>ported from: src/compiler/checker.ts:44763-44778 — <c>checkBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>checkBlock(node: Block)</c> and its one non-<c>Block</c> caller casts a
    /// <c>ModuleBlock</c> (<c>checkBlock(node as Block)</c>, checker.ts:49213, the <c>Block</c>/<c>ModuleBlock</c> arm
    /// of <c>checkSourceElementWorker</c>). In the port <c>ModuleBlock</c> is NOT a <c>Block</c>
    /// (<c>Model/NodeClasses.cs:1640</c> versus <c>:133</c>), so that cast would throw at run time for every
    /// <c>module A { }</c>; the parameter is therefore <c>Node</c> and the two slots are read per kind. That is
    /// semantically identical to the reference: TS's <c>ModuleBlock</c> has no <c>locals</c>
    /// (types.ts:3657-3661), so the reference's <c>if (node.locals)</c> is already false on that arm, and
    /// <c>statements</c> exists on both kinds.
    /// <para>
    /// PORT-NOTE: the reference's <c>forEach(node.statements, checkSourceElement)</c> is the landed
    /// <c>Core.ForEach&lt;T, TU&gt;(IList&lt;T&gt;?, Func&lt;T, int, TU?&gt;)</c> idiom, whose callback must return a value
    /// because C# has no <c>void</c> type argument (the same shape <c>Checker.CallResolution.cs:94</c> uses).
    /// <c>IsFunctionOrModuleBlock</c> is a <c>utilitiesPublic.ts</c> member (utilitiesPublic.ts:1727), so it is
    /// <c>Utilities</c>' and called qualified.
    /// </para>
    /// </remarks>
    internal void CheckBlock(Node node)
    {
        // Grammar checking for SyntaxKind.Block
        if (node.Kind == SyntaxKind.Block)
        {
            CheckGrammarStatementInAmbientContext(node);
        }

        var statements = node is Block block ? block.Statements : ((ModuleBlock)node).Statements;
        if (Utilities.IsFunctionOrModuleBlock(node))
        {
            var saveFlowAnalysisDisabled = _flowAnalysisDisabled;
            Core.ForEach(statements, (IStatement statement, int _) =>
            {
                CheckSourceElement(statement);
                return (object?)null;
            });
            _flowAnalysisDisabled = saveFlowAnalysisDisabled;
        }
        else
        {
            Core.ForEach(statements, (IStatement statement, int _) =>
            {
                CheckSourceElement(statement);
                return (object?)null;
            });
        }

        // checker.ts:44776-44777 — `node.locals`, which only the Block arm has (types.ts:3657-3661).
        if (node is Block blockWithLocals && blockWithLocals.Locals is not null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

}
