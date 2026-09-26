namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:11491-12026 (createNameResolver: resolveNameHelper and its local helpers)
// ported from: src/compiler/checker.ts:4290-4293 (isNonLocalAlias)
// ported from: src/compiler/checker.ts:4295-4299 (resolveSymbol)
// ported from: src/compiler/checker.ts:4301-4320 (resolveAlias)
// ported from: src/compiler/checker.ts:4351-4391 (getSymbolFlags)
// ported from: src/compiler/checker.ts:4502-4504 (getFullyQualifiedName)
// ported from: src/compiler/checker.ts:4533-4630 (resolveEntityName)
// ported from: src/compiler/checker.ts:4702-4714 (getExpandoSymbol)
// ported from: src/compiler/checker.ts:4716-4722 (resolveExternalModuleName)
// ported from: src/compiler/checker.ts:4985-4994 (resolveExternalModuleSymbol)
// ported from: src/compiler/checker.ts:5175-5179 (getExportsOfSymbol)
// ported from: src/compiler/checker.ts:5314-5328 (getSymbolOfDeclaration, getSymbolOfNode, getParentOfSymbol)
// ported from: src/compiler/checker.ts:11821-11827 (getTypeForDeclarationFromJSDocComment)
// ported from: src/compiler/checker.ts:12523-12672 (isParameterOfContextSensitiveSignature … getTypeOfVariableOrParameterOrPropertyWorker)
// ported from: src/compiler/checker.ts:12674-12695 (getAnnotatedAccessorTypeNode, getAnnotatedAccessorType)
// ported from: src/compiler/checker.ts:12706-12776 (getTypeOfAccessors, getWriteTypeOfAccessors)
// ported from: src/compiler/checker.ts:12778-12837 (getBaseTypeVariableOfClass … getTypeOfFuncClassEnumModuleWorker)
// ported from: src/compiler/checker.ts:12844-12871 (getTypeOfAlias)
// ported from: src/compiler/checker.ts:13007-13009 (getTargetType)
// ported from: src/compiler/checker.ts:13026-13148 (appendTypeParameters … getLocalTypeParametersOfClassOrInterfaceOrTypeAlias)
// ported from: src/compiler/checker.ts:13156-13184 (isMixinConstructorType, getBaseTypeNodeOfClass)
// ported from: src/compiler/checker.ts:13206-13251 (getBaseConstructorTypeOfClass)
// ported from: src/compiler/checker.ts:13279-13310 (getBaseTypes)
/// <summary>
/// The symbol-side helpers the checker shares with everything else: the name resolver, the alias-resolution family,
/// the declaration/expando lookups, and the <c>getTypeOf*</c> workers that answer the type of a declaration.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>resolveName</c>): TS builds the checker's <c>resolveName</c> at checker.ts:1579 with
/// <c>createNameResolver({ … })</c> (utilities.ts:11491), a factory whose whole closure becomes checker state and
/// whose option callbacks (<c>lookup: getSymbol</c>, <c>getSymbolOfDeclaration</c>, <c>error</c>, <c>globals</c>,
/// <c>requireSymbol</c>, <c>argumentsSymbol</c>, <c>onPropertyWithInvalidInitializer</c>, <c>onFailedToResolveSymbol</c>,
/// <c>onSuccessfullyResolvedSymbol</c>, <c>getRequiresScopeChangeCache</c>, <c>setRequiresScopeChangeCache</c>) are
/// members of this class. The port therefore states the factory's returned <c>resolveNameHelper</c> as the member
/// <see cref="ResolveName"/> — the same six-argument shape, with the <c>__String | Identifier</c> first name argument
/// already reduced to the escaped <c>string</c> the ported call sites hold (<c>Checker.TypeReferences.cs</c>'s
/// <c>GetGlobalSymbol</c> calls it with six arguments) — and the factory's four local helpers
/// (<c>useOuterVariableScopeInParameter</c>, <c>getIsDeferredContext</c>, <c>isSelfReferenceLocation</c>,
/// <c>isTypeParameterSymbolDeclaredInContainer</c>, plus the two nested inside the first) as private members beside
/// it. <c>Checker.cs</c>'s remarks list <c>resolveName</c> as the M6 utilities slice's slot; this file declares it
/// because the landed callers already invoke <c>ResolveName</c> and nothing else does.
/// </para>
/// <para>
/// PORT-NOTE (<c>resolveNameForSymbolSuggestion</c>, checker.ts:1594): the sibling factory the checker builds for
/// symbol suggestions is a second <c>createNameResolver</c> call over the same helpers and has no landed caller, so it
/// stays unported here (the M6 utilities slice owns that slot).
/// </para>
/// <para>
/// PORT-NOTE (deviation from TS's shapes): TS's <c>Symbol.declarations</c> may be <c>undefined</c> and the port's is
/// <c>List&lt;IDeclaration&gt;?</c>, so <c>symbol.declarations &amp;&amp; …</c> is <c>symbol.Declarations is not null &amp;&amp; …</c>;
/// TS's arrays are the port's <c>List</c>s with <c>.Count</c>; <c>emptyArray</c> is a fresh <c>[]</c> (the port does not
/// share one, <c>Checker.TypeReferences.cs</c>'s note); truthiness tests over possibly-absent values are
/// <c>Js.IsTruthy</c> or a <c>is not null</c> pattern; and TS's <c>Symbol[]</c>-returning <c>getExportsOfModule</c> is
/// this file's only unresolved name clash — see the slice report.
/// </para>
/// <para>
/// PORT-NOTE (casts): several TS bodies reach a declaration through the <c>Declaration</c> face of a node
/// (<c>node.symbol</c>, <c>node.locals</c>, <c>(location as FunctionLikeDeclaration).body</c>). The port's nodes are
/// concrete classes and <c>Node</c> carries none of those slots, so the file casts to the generated face that declares
/// them (<see cref="IDeclaration"/>, <see cref="ILocalsContainer"/>, <see cref="IFunctionLikeDeclarationBase"/>,
/// <see cref="ISignatureDeclarationBase"/>, <see cref="INamedDeclaration"/>) exactly where TS's own union narrowing
/// does.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // The name resolver (utilities.ts:11491-12026, built at checker.ts:1579)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:11497-12005 — <c>resolveNameHelper</c>, the checker's <c>resolveName</c> (checker.ts:1579).</summary>
    /// <remarks>
    /// PORT-NOTE: the factory's closure — <c>compilerOptions</c>, <c>globals</c>, <c>requireSymbol</c>,
    /// <c>argumentsSymbol</c>, <c>isolatedModulesLikeFlagName</c>, <c>emitStandardClassFields</c> and the empty table —
    /// is this class's state (<c>_compilerOptions</c>, <c>_globals</c>, <c>_requireSymbol</c>, <c>_argumentsSymbol</c>,
    /// <c>_isolatedModulesLikeFlagName</c>, <c>_emitStandardClassFields</c>) and the port's own <c>_emptySymbols</c>
    /// replaces the table the factory created (the two are the same empty map; the class slot is the one the rest of
    /// the ported resolver uses, e.g. <c>AddUndefinedToGlobalsOrErrorOnRedeclaration</c>).
    /// <para>
    /// PORT-NOTE: <c>break loop</c> — the reference's labelled break out of the climb — is <c>goto BreakLoop</c> to the
    /// statement after the loop; a plain <c>break</c> keeps the reference's meaning inside the <c>switch</c>, and the
    /// two fall-through cases (<c>SourceFile</c> → <c>ModuleDeclaration</c>, <c>ArrowFunction</c> →
    /// <c>MethodDeclaration</c>) are <c>goto case</c> as C# requires.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>nameArg: __String | Identifier</c> is the ported <c>string</c> escaped name, so
    /// the reference's <c>isString(nameArg) ? nameArg : nameArg.escapedText</c> is the parameter itself, and the
    /// comparisons against <c>location.name.escapedText</c>/<c>className.escapedText</c> compare escaped names. The
    /// three option callbacks the reference passes to <c>createNameResolver</c> are called with the reference's
    /// argument lists (<c>CheckAndReportErrorForInvalidInitializer</c>, <c>OnFailedToResolveSymbol</c>,
    /// <c>OnSuccessfullyResolvedSymbol</c>); they are declared by the slice that ports checker.ts:3206-3400.
    /// </para>
    /// </remarks>
    internal ISymbol? ResolveName(Node? location, string name, SymbolFlags meaning, DiagnosticMessage? nameNotFoundMessage, bool isUse, bool excludeGlobals)
    {
        var originalLocation = location; // needed for did-you-mean error reporting, which gathers candidates starting from the original location
        ISymbol? result = null;
        Node? lastLocation = null;
        IDeclaration? lastSelfReferenceLocation = null;
        PropertyDeclaration? propertyWithInvalidInitializer = null;
        Node? associatedDeclarationForContainingInitializerOrBindingName = null;
        var withinDeferredContext = false;
        Node? grandparent;

        while (location is not null)
        {
            if (name == "const" && Utilities.IsConstAssertion(location))
            {
                // `const` in an `as const` has no symbol, but issues no error because there is no *actual* lookup of the type
                // (it refers to the constant type of the expression instead)
                return null;
            }
            if (Utilities.IsModuleOrEnumDeclaration(location) && lastLocation is not null && ReferenceEquals(((INamedDeclaration)location).Name, lastLocation))
            {
                // If lastLocation is the name of a namespace or enum, skip the parent since it will have is own locals that could
                // conflict.
                lastLocation = location;
                location = location.Parent;
            }
            // Locals of a source file are not in scope (because they get merged into the global symbol table)
            if (Utilities.CanHaveLocals(location) && ((ILocalsContainer)location).Locals is { } locals && !Utilities.IsGlobalSourceFile(location))
            {
                if ((result = GetSymbol(locals, name, meaning)) is not null)
                {
                    var useResult = true;
                    if (Utilities.IsFunctionLike(location) && lastLocation is not null && !ReferenceEquals(lastLocation, ((IFunctionLikeDeclarationBase)location).Body))
                    {
                        // symbol lookup restrictions for function-like declarations
                        // - Type parameters of a function are in scope in the entire function declaration, including the parameter
                        //   list and return type. However, local types are only in scope in the function body.
                        // - parameters are only in the scope of function body
                        // This restriction does not apply to JSDoc comment types because they are parented
                        // at a higher level than type parameters would normally be
                        if ((meaning & result.Flags & SymbolFlags.Type) != 0 && lastLocation.Kind != SyntaxKind.JSDoc)
                        {
                            useResult = (result.Flags & SymbolFlags.TypeParameter) != 0
                                // type parameters are visible in parameter list, return type and type parameter list
                                ? (lastLocation.Flags & NodeFlags.Synthesized) != 0 // Synthetic fake scopes are added for signatures so type parameters are accessible from them
                                    || ReferenceEquals(lastLocation, ((ISignatureDeclarationBase)location).Type)
                                    || lastLocation.Kind == SyntaxKind.Parameter
                                    || lastLocation.Kind == SyntaxKind.JSDocParameterTag
                                    || lastLocation.Kind == SyntaxKind.JSDocReturnTag
                                    || lastLocation.Kind == SyntaxKind.TypeParameter
                                // local types not visible outside the function body
                                : false;
                        }
                        if ((meaning & result.Flags & SymbolFlags.Variable) != 0)
                        {
                            // expression inside parameter will lookup as normal variable scope when targeting es2015+
                            if (UseOuterVariableScopeInParameter(result, location, lastLocation))
                            {
                                useResult = false;
                            }
                            else if ((result.Flags & SymbolFlags.FunctionScopedVariable) != 0)
                            {
                                // parameters are visible only inside function body, parameter list and return type
                                // technically for parameter list case here we might mix parameters and variables declared in function,
                                // however it is detected separately when checking initializers of parameters
                                // to make sure that they reference no variables declared after them.
                                useResult = lastLocation.Kind == SyntaxKind.Parameter
                                    || (lastLocation.Flags & NodeFlags.Synthesized) != 0 // Synthetic fake scopes are added for signatures so parameters are accessible from them
                                    || (ReferenceEquals(lastLocation, ((ISignatureDeclarationBase)location).Type)
                                        && Utilities.FindAncestor((Node?)result.ValueDeclaration, NodeTests.IsParameter) is not null);
                            }
                        }
                    }
                    else if (location.Kind == SyntaxKind.ConditionalType)
                    {
                        // A type parameter declared using 'infer T' in a conditional type is visible only in
                        // the true branch of the conditional type.
                        useResult = ReferenceEquals(lastLocation, ((ConditionalTypeNode)location).TrueType);
                    }

                    if (useResult)
                    {
                        goto BreakLoop;
                    }

                    result = null;
                }
            }
            withinDeferredContext = withinDeferredContext || GetIsDeferredContext(location, lastLocation);
            switch (location.Kind)
            {
                case SyntaxKind.SourceFile:
                    if (!Utilities.IsExternalOrCommonJsModule((SourceFile)location))
                    {
                        break;
                    }
                    goto case SyntaxKind.ModuleDeclaration;
                case SyntaxKind.ModuleDeclaration:
                {
                    var moduleExports = GetSymbolOfDeclaration((IDeclaration)location).Exports ?? _emptySymbols;
                    if (location.Kind == SyntaxKind.SourceFile || (NodeTests.IsModuleDeclaration(location) && (location.Flags & NodeFlags.Ambient) != 0 && !Utilities.IsGlobalScopeAugmentation((ModuleDeclaration)location)))
                    {
                        // It's an external module. First see if the module has an export default and if the local
                        // name of that export default matches.
                        if (moduleExports.TryGetValue(InternalSymbolName.Default.Value, out var defaultExport))
                        {
                            result = defaultExport;
                            var localSymbol = Utilities.GetLocalSymbolForExportDefault(result);
                            if (localSymbol is not null && (result.Flags & meaning) != 0 && localSymbol.EscapedName == name)
                            {
                                goto BreakLoop;
                            }

                            result = null;
                        }

                        // Because of module/namespace merging, a module's exports are in scope,
                        // yet we never want to treat an export specifier as putting a member in scope.
                        // Therefore, if the name we find is purely an export specifier, it is not actually considered in scope.
                        // Two things to note about this:
                        //     1. We have to check this without calling getSymbol. The problem with calling getSymbol
                        //        on an export specifier is that it might find the export specifier itself, and try to
                        //        resolve it as an alias. This will cause the checker to consider the export specifier
                        //        a circular alias reference when it might not be.
                        //     2. We check === SymbolFlags.Alias in order to check that the symbol is *purely*
                        //        an alias. If we used &, we'd be throwing out symbols that have non alias aspects,
                        //        which is not the desired behavior.
                        if (moduleExports.TryGetValue(name, out var moduleExport)
                            && moduleExport.Flags == SymbolFlags.Alias
                            && (Utilities.GetDeclarationOfKind<Node>(moduleExport, SyntaxKind.ExportSpecifier) is not null
                                || Utilities.GetDeclarationOfKind<Node>(moduleExport, SyntaxKind.NamespaceExport) is not null))
                        {
                            break;
                        }
                    }

                    // ES6 exports are also visible locally (except for 'default'), but commonjs exports are not (except typedefs)
                    if (name != InternalSymbolName.Default.Value && (result = GetSymbol(moduleExports, name, meaning & SymbolFlags.ModuleMember)) is not null)
                    {
                        if (NodeTests.IsSourceFile(location) && ((SourceFile)location).CommonJsModuleIndicator is not null
                            && !Core.Some(result.Declarations, d => Utilities.IsJSDocTypeAlias((Node)d)))
                        {
                            result = null;
                        }
                        else
                        {
                            goto BreakLoop;
                        }
                    }

                    break;
                }
                case SyntaxKind.EnumDeclaration:
                    if ((result = GetSymbol(GetSymbolOfDeclaration((IDeclaration)location).Exports ?? _emptySymbols, name, meaning & SymbolFlags.EnumMember)) is not null)
                    {
                        if (nameNotFoundMessage is not null && Utilities.GetIsolatedModules(_compilerOptions) && (location.Flags & NodeFlags.Ambient) == 0
                            && !ReferenceEquals(Utilities.GetSourceFileOfNode(location), Utilities.GetSourceFileOfNode(result.ValueDeclaration)))
                        {
                            Error(
                                originalLocation,
                                Diagnostics.Cannot_access_0_from_another_file_without_qualification_when_1_is_enabled_Use_2_instead,
                                Utilities.UnescapeLeadingUnderscores(name),
                                _isolatedModulesLikeFlagName,
                                $"{Utilities.UnescapeLeadingUnderscores(GetSymbolOfDeclaration((IDeclaration)location).EscapedName ?? "")}.{Utilities.UnescapeLeadingUnderscores(name)}"
                            );
                        }

                        goto BreakLoop;
                    }

                    break;
                case SyntaxKind.PropertyDeclaration:
                    // TypeScript 1.0 spec (April 2014): 8.4.1
                    // Initializer expressions for instance member variables are evaluated in the scope
                    // of the class constructor body but are not permitted to reference parameters or
                    // local variables of the constructor. This effectively means that entities from outer scopes
                    // by the same name as a constructor parameter or local variable are inaccessible
                    // in initializer expressions for instance member variables.
                    if (!Utilities.IsStatic(location))
                    {
                        var ctor = Utilities.FindConstructorDeclaration(location.Parent!);
                        if (ctor is not null && ctor.Locals is not null)
                        {
                            if (GetSymbol(ctor.Locals, name, meaning & SymbolFlags.Value) is not null)
                            {
                                // Remember the property node, it will be used later to report appropriate error
                                Debug.AssertNode(location, NodeTests.IsPropertyDeclaration);
                                propertyWithInvalidInitializer = (PropertyDeclaration)location;
                            }
                        }
                    }

                    break;
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                    // The below is used to lookup type parameters within a class or interface, as they are added to the class/interface locals
                    // These can never be latebound, so the symbol's raw members are sufficient. `getMembersOfNode` cannot be used, as it would
                    // trigger resolving late-bound names, which we may already be in the process of doing while we're here!
                    if ((result = GetSymbol(GetSymbolOfDeclaration((IDeclaration)location).Members ?? _emptySymbols, name, meaning & SymbolFlags.Type)) is not null)
                    {
                        if (!IsTypeParameterSymbolDeclaredInContainer(result, location))
                        {
                            // ignore type parameters not declared in this container
                            result = null;
                            break;
                        }

                        if (lastLocation is not null && Utilities.IsStatic(lastLocation))
                        {
                            // TypeScript 1.0 spec (April 2014): 3.4.1
                            // The scope of a type parameter extends over the entire declaration with which the type
                            // parameter list is associated, with the exception of static member declarations in classes.
                            if (nameNotFoundMessage is not null)
                            {
                                Error(originalLocation, Diagnostics.Static_members_cannot_reference_class_type_parameters);
                            }

                            return null;
                        }

                        goto BreakLoop;
                    }

                    if (NodeTests.IsClassExpression(location) && (meaning & SymbolFlags.Class) != 0)
                    {
                        var className = ((ClassExpression)location).Name;
                        if (className is not null && name == className.EscapedText)
                        {
                            result = ((IDeclaration)location).Symbol;
                            goto BreakLoop;
                        }
                    }

                    break;
                case SyntaxKind.ExpressionWithTypeArguments:
                    // The type parameters of a class are not in scope in the base class expression.
                    if (ReferenceEquals(lastLocation, ((ExpressionWithTypeArguments)location).Expression)
                        && ((HeritageClause)location.Parent!).Token == SyntaxKind.ExtendsKeyword)
                    {
                        var container = location.Parent!.Parent!;
                        if (Utilities.IsClassLike(container)
                            && (result = GetSymbol(GetSymbolOfDeclaration((IDeclaration)container).Members!, name, meaning & SymbolFlags.Type)) is not null)
                        {
                            if (nameNotFoundMessage is not null)
                            {
                                Error(originalLocation, Diagnostics.Base_class_expressions_cannot_reference_class_type_parameters);
                            }

                            return null;
                        }
                    }

                    break;
                // It is not legal to reference a class's own type parameters from a computed property name that
                // belongs to the class. For example:
                //
                //   function foo<T>() { return '' }
                //   class C<T> { // <-- Class's own type parameter T
                //       [foo<T>()]() { } // <-- Reference to T from class's own computed property
                //   }
                //
                case SyntaxKind.ComputedPropertyName:
                    grandparent = location.Parent!.Parent!;
                    if (Utilities.IsClassLike(grandparent) || grandparent.Kind == SyntaxKind.InterfaceDeclaration)
                    {
                        // A reference to this grandparent's type parameters would be an error
                        if ((result = GetSymbol(GetSymbolOfDeclaration((IDeclaration)grandparent).Members!, name, meaning & SymbolFlags.Type)) is not null)
                        {
                            if (nameNotFoundMessage is not null)
                            {
                                Error(originalLocation, Diagnostics.A_computed_property_name_cannot_reference_a_type_parameter_from_its_containing_type);
                            }

                            return null;
                        }
                    }

                    break;
                case SyntaxKind.ArrowFunction:
                    // when targeting ES6 or higher there is no 'arguments' in an arrow function
                    // for lower compile targets the resolved symbol is used to emit an error
                    if (Utilities.GetEmitScriptTarget(_compilerOptions) >= ScriptTarget.ES2015)
                    {
                        break;
                    }

                    goto case SyntaxKind.MethodDeclaration;
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.FunctionDeclaration:
                    if ((meaning & SymbolFlags.Variable) != 0 && name == "arguments")
                    {
                        result = _argumentsSymbol;
                        goto BreakLoop;
                    }

                    break;
                case SyntaxKind.FunctionExpression:
                    if ((meaning & SymbolFlags.Variable) != 0 && name == "arguments")
                    {
                        result = _argumentsSymbol;
                        goto BreakLoop;
                    }

                    if ((meaning & SymbolFlags.Function) != 0)
                    {
                        var functionName = ((FunctionExpression)location).Name;
                        if (functionName is not null && name == functionName.EscapedText)
                        {
                            result = ((IDeclaration)location).Symbol;
                            goto BreakLoop;
                        }
                    }

                    break;
                case SyntaxKind.Decorator:
                    // Decorators are resolved at the class declaration. Resolving at the parameter
                    // or member would result in looking up locals in the method.
                    //
                    //   function y() {}
                    //   class C {
                    //       method(@y x, y) {} // <-- decorator y should be resolved at the class declaration, not the parameter.
                    //   }
                    //
                    if (location.Parent is not null && location.Parent.Kind == SyntaxKind.Parameter)
                    {
                        location = location.Parent;
                    }
                    //
                    //   function y() {}
                    //   class C {
                    //       @y method(x, y) {} // <-- decorator y should be resolved at the class declaration, not the method.
                    //   }
                    //

                    // class Decorators are resolved outside of the class to avoid referencing type parameters of that class.
                    //
                    //   type T = number;
                    //   declare function y(x: T): any;
                    //   @param(1 as T) // <-- T should resolve to the type alias outside of class C
                    //   class C<T> {}
                    if (location.Parent is not null && (Utilities.IsClassElement(location.Parent) || location.Parent.Kind == SyntaxKind.ClassDeclaration))
                    {
                        location = location.Parent;
                    }

                    break;
                case SyntaxKind.JSDocTypedefTag:
                case SyntaxKind.JSDocCallbackTag:
                case SyntaxKind.JSDocEnumTag:
                case SyntaxKind.JSDocImportTag:
                {
                    // js type aliases do not resolve names from their host, so skip past it
                    var root = Utilities.GetJSDocRoot(location);
                    if (root is not null)
                    {
                        location = root.Parent;
                    }

                    break;
                }
                case SyntaxKind.Parameter:
                    if (lastLocation is not null
                        && (ReferenceEquals(lastLocation, ((ParameterDeclaration)location).Initializer)
                            || (ReferenceEquals(lastLocation, ((ParameterDeclaration)location).Name) && NodeTests.IsBindingPattern(lastLocation))))
                    {
                        associatedDeclarationForContainingInitializerOrBindingName ??= location;
                    }

                    break;
                case SyntaxKind.BindingElement:
                    if (lastLocation is not null
                        && (ReferenceEquals(lastLocation, ((BindingElement)location).Initializer)
                            || (ReferenceEquals(lastLocation, ((BindingElement)location).Name) && NodeTests.IsBindingPattern(lastLocation))))
                    {
                        if (Utilities.IsPartOfParameterDeclaration(location) && associatedDeclarationForContainingInitializerOrBindingName is null)
                        {
                            associatedDeclarationForContainingInitializerOrBindingName = location;
                        }
                    }

                    break;
                case SyntaxKind.InferType:
                    if ((meaning & SymbolFlags.TypeParameter) != 0)
                    {
                        var inferTypeParameter = ((InferTypeNode)location).TypeParameter!;
                        if (inferTypeParameter.Name is not null && name == inferTypeParameter.Name.EscapedText)
                        {
                            result = inferTypeParameter.Symbol;
                            goto BreakLoop;
                        }
                    }

                    break;
                case SyntaxKind.ExportSpecifier:
                    // External module export bindings shouldn't be resolved to local symbols.
                    if (lastLocation is not null
                        && ReferenceEquals(lastLocation, ((ExportSpecifier)location).PropertyName)
                        && (((ExportDeclaration)((ExportSpecifier)location).Parent!.Parent!).ModuleSpecifier) is not null)
                    {
                        location = location.Parent!.Parent!.Parent!;
                    }

                    break;
            }
            if (IsSelfReferenceLocation(location, lastLocation))
            {
                lastSelfReferenceLocation = location;
            }

            lastLocation = location;
            location = NodeTests.IsJSDocTemplateTag(location)
                ? (Node?)Utilities.GetEffectiveContainerForJSDocTemplateTag((JSDocTemplateTag)location) ?? location.Parent
                : NodeTests.IsJSDocParameterTag(location) || NodeTests.IsJSDocReturnTag(location)
                    ? (Node?)Utilities.GetHostSignatureFromJSDoc(location) ?? location.Parent
                    : location.Parent;
        }

    BreakLoop:
        // We just climbed up parents looking for the name, meaning that we started in a descendant node of `lastLocation`.
        // If `result === lastSelfReferenceLocation.symbol`, that means that we are somewhere inside `lastSelfReferenceLocation` looking up a name, and resolving to `lastLocation` itself.
        // That means that this is a self-reference of `lastLocation`, and shouldn't count this when considering whether `lastLocation` is used.
        if (isUse && result is not null && (lastSelfReferenceLocation is null || !ReferenceEquals(result, lastSelfReferenceLocation.Symbol)))
        {
            result.IsReferenced = (result.IsReferenced ?? SymbolFlags.None) | meaning;
        }

        if (result is null)
        {
            if (lastLocation is not null)
            {
                Debug.AssertNode(lastLocation, NodeTests.IsSourceFile);
                var lastLocationFile = (SourceFile)lastLocation;
                if (lastLocationFile.CommonJsModuleIndicator is not null && name == "exports" && (meaning & ((IDeclaration)lastLocation).Symbol!.Flags) != 0)
                {
                    return ((IDeclaration)lastLocation).Symbol;
                }
            }

            if (!excludeGlobals)
            {
                result = GetSymbol(_globals, name, meaning);
            }
        }
        if (result is null)
        {
            if (originalLocation is not null && Utilities.IsInJSFile(originalLocation) && originalLocation.Parent is not null)
            {
                if (Utilities.IsRequireCall(originalLocation.Parent, /*requireStringLiteralLikeArgument*/ false))
                {
                    return _requireSymbol;
                }
            }
        }

        if (nameNotFoundMessage is not null)
        {
            if (propertyWithInvalidInitializer is not null && CheckAndReportErrorForInvalidInitializer(originalLocation, name, propertyWithInvalidInitializer, result))
            {
                return null;
            }

            if (result is null)
            {
                OnFailedToResolveSymbol(originalLocation, name, meaning, nameNotFoundMessage);
            }
            else
            {
                OnSuccessfullyResolvedSymbol(originalLocation, result, meaning, lastLocation, associatedDeclarationForContainingInitializerOrBindingName, withinDeferredContext);
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/utilities.ts:11906-11983 — <c>useOuterVariableScopeInParameter</c> (a local function of the name resolver).</summary>
    /// <remarks>
    /// PORT-NOTE: TS nests this inside <c>createNameResolver</c>; the port states it as a private member of this class
    /// because the resolver is one here. Its two nested functions (<c>requiresScopeChange</c>,
    /// <c>requiresScopeChangeWorker</c>) are private members beside it, and the resolver's
    /// <c>emitStandardClassFields</c> closure variable is the class's <c>_emitStandardClassFields</c> slot.
    /// </remarks>
    private bool UseOuterVariableScopeInParameter(ISymbol result, Node location, Node lastLocation)
    {
        var target = Utilities.GetEmitScriptTarget(_compilerOptions);
        var functionLocation = (IFunctionLikeDeclarationBase)location;
        if (NodeTests.IsParameter(lastLocation)
            && functionLocation.Body is not null
            && result.ValueDeclaration is not null
            && result.ValueDeclaration.Pos >= functionLocation.Body.Pos
            && result.ValueDeclaration.End <= functionLocation.Body.End)
        {
            // check for several cases where we introduce temporaries that require moving the name/initializer of the parameter to the body
            // - static field in a class expression
            // - optional chaining pre-es2020
            // - nullish coalesce pre-es2020
            // - spread assignment in binding pattern pre-es2017
            if (target >= ScriptTarget.ES2015)
            {
                var declarationRequiresScopeChange = GetRequiresScopeChangeCache(location);
                if (declarationRequiresScopeChange is null)
                {
                    declarationRequiresScopeChange = Core.Some(((ISignatureDeclarationBase)location).Parameters, p => RequiresScopeChange(p, target));
                    SetRequiresScopeChangeCache(location, declarationRequiresScopeChange.Value);
                }

                return !declarationRequiresScopeChange.Value;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:11926-11933 — <c>requiresScopeChange</c> (nested in <c>useOuterVariableScopeInParameter</c>).</summary>
    private bool RequiresScopeChange(Node node, ScriptTarget target)
    {
        var parameter = (ParameterDeclaration)node;
        return RequiresScopeChangeWorker(parameter.Name!, target)
            || (parameter.Initializer is not null && RequiresScopeChangeWorker(parameter.Initializer, target));
    }

    /// <summary>ported from: src/compiler/utilities.ts:11935-11970 — <c>requiresScopeChangeWorker</c> (nested in <c>useOuterVariableScopeInParameter</c>).</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>forEachChild(node, requiresScopeChangeWorker) || false</c> is
    /// <c>ForEachChildTable.ForEachChild&lt;bool&gt;(…) ?? false</c>, the port's walk of a node's children.
    /// </remarks>
    private bool RequiresScopeChangeWorker(Node node, ScriptTarget target)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
                // do not descend into these
                return false;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.PropertyAssignment:
                return RequiresScopeChangeWorker(((INamedDeclaration)node).Name!, target);
            case SyntaxKind.PropertyDeclaration:
                // static properties in classes introduce temporary variables
                if (Utilities.HasStaticModifier(node))
                {
                    return !_emitStandardClassFields;
                }

                return RequiresScopeChangeWorker(((INamedDeclaration)node).Name!, target);
            default:
                // null coalesce and optional chain pre-es2020 produce temporary variables
                if (Utilities.IsNullishCoalesce(node) || Utilities.IsOptionalChain(node))
                {
                    return target < ScriptTarget.ES2020;
                }

                if (NodeTests.IsBindingElement(node) && ((BindingElement)node).DotDotDotToken is not null && NodeTests.IsObjectBindingPattern(node.Parent!))
                {
                    return target < ScriptTarget.ES2017;
                }

                if (Utilities.IsTypeNode(node)) return false;
                return ForEachChildTable.ForEachChild<bool>(node, child => RequiresScopeChangeWorker(child, target)) ?? false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:11985-11995 — <c>getIsDeferredContext</c> (a local function of the name resolver).</summary>
    private static bool GetIsDeferredContext(Node location, Node? lastLocation)
    {
        if (location.Kind != SyntaxKind.ArrowFunction && location.Kind != SyntaxKind.FunctionExpression)
        {
            // initializers in instance property declaration of class like entities are executed in constructor and thus deferred
            return NodeTests.IsTypeQueryNode(location) || ((
                Utilities.IsFunctionLikeDeclaration(location)
                || (location.Kind == SyntaxKind.PropertyDeclaration && !Utilities.IsStatic(location))
            ) && (lastLocation is null || !ReferenceEquals(lastLocation, ((INamedDeclaration)location).Name))); // A name is evaluated within the enclosing scope - so it shouldn't count as deferred
        }

        if (lastLocation is not null && ReferenceEquals(lastLocation, ((INamedDeclaration)location).Name))
        {
            return false;
        }

        // generator functions and async functions are not inlined in control flow when immediately invoked
        if (((IFunctionLikeDeclarationBase)location).AsteriskToken is not null || Utilities.HasSyntacticModifier(location, ModifierFlags.Async))
        {
            return true;
        }

        return Utilities.GetImmediatelyInvokedFunctionExpression(location) is null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:11997-12011 — <c>isSelfReferenceLocation</c> (a local function of the name resolver).</summary>
    private static bool IsSelfReferenceLocation(Node node, Node? lastLocation)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Parameter:
                return lastLocation is not null && ReferenceEquals(lastLocation, ((ParameterDeclaration)node).Name);
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.ModuleDeclaration: // For `namespace N { N; }`
                return true;
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:12013-12025 — <c>isTypeParameterSymbolDeclaredInContainer</c> (a local function of the name resolver).</summary>
    private static bool IsTypeParameterSymbolDeclaredInContainer(ISymbol symbol, Node container)
    {
        if (symbol.Declarations is not null)
        {
            foreach (var decl in symbol.Declarations)
            {
                if (decl.Kind == SyntaxKind.TypeParameter)
                {
                    var parent = NodeTests.IsJSDocTemplateTag(((Node)decl).Parent!)
                        ? Utilities.GetJSDocHost(((Node)decl).Parent!)
                        : ((Node)decl).Parent;
                    if (ReferenceEquals(parent, container))
                    {
                        return !(NodeTests.IsJSDocTemplateTag(((Node)decl).Parent!)
                            && Core.Some(((JSDoc)((Node)decl).Parent!.Parent!).Tags, t => Utilities.IsJSDocTypeAlias(t)));
                    }
                }
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Alias resolution (checker.ts:4290-4391)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:4290-4293 — <c>isNonLocalAlias</c>.</summary>
    /// <remarks>
    /// Indicates that a symbol is an alias that does not merge with a local declaration. OR Is a JSContainer which may
    /// merge an alias with a local declaration.
    /// <para>
    /// PORT-NOTE: TS's <c>excludes</c> default is a default parameter value; the port states the same default.
    /// </para>
    /// </remarks>
    private static bool IsNonLocalAlias(ISymbol? symbol, SymbolFlags excludes = SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace)
    {
        if (symbol is null) return false;
        return (symbol.Flags & (SymbolFlags.Alias | excludes)) == SymbolFlags.Alias
            || ((symbol.Flags & SymbolFlags.Alias) != 0 && (symbol.Flags & SymbolFlags.Assignment) != 0);
    }

    /// <summary>ported from: src/compiler/checker.ts:4295-4299 — <c>resolveSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two declaration-only overloads (<c>Symbol → Symbol</c> and
    /// <c>Symbol | undefined → Symbol | undefined</c>) collapse into this one nullable member; the landed call sites
    /// (<c>Checker.Symbols.cs</c>, <c>Checker.Conditional.cs</c>) take the second.
    /// </remarks>
    internal ISymbol? ResolveSymbol(ISymbol? symbol, bool dontResolveAlias = false)
        => !dontResolveAlias && IsNonLocalAlias(symbol) ? ResolveAlias(symbol!) : symbol;

    /// <summary>ported from: src/compiler/checker.ts:4301-4320 — <c>resolveAlias</c>.</summary>
    /// <remarks>
    /// <c>resolvingSymbol</c> (checker.ts:2068) is <c>_resolvingSymbol</c> and <c>unknownSymbol</c> (checker.ts:2067) is
    /// <c>_unknownSymbol</c> — the two symbols the M6-2 state block declares.
    /// </remarks>
    internal ISymbol ResolveAlias(ISymbol symbol)
    {
        Debug.Assert((symbol.Flags & SymbolFlags.Alias) != 0, "Should only get Alias here.");
        var links = GetSymbolLinks(symbol);
        if (links.AliasTarget is null)
        {
            links.AliasTarget = _resolvingSymbol;
            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node is null) return Debug.Fail<ISymbol>();
            var target = GetTargetOfAliasDeclaration(node);
            if (ReferenceEquals(links.AliasTarget, _resolvingSymbol))
            {
                links.AliasTarget = target ?? _unknownSymbol;
            }
            else
            {
                Error(node, Diagnostics.Circular_definition_of_import_alias_0, SymbolToString(symbol));
            }
        }
        else if (ReferenceEquals(links.AliasTarget, _resolvingSymbol))
        {
            links.AliasTarget = _unknownSymbol;
        }

        return links.AliasTarget!;
    }

    /// <summary>ported from: src/compiler/checker.ts:4351-4391 — <c>getSymbolFlags</c>.</summary>
    /// <remarks>
    /// Gets combined flags of a <c>symbol</c> and all alias targets it resolves to. <c>resolveAlias</c> is typically
    /// recursive over chains of aliases, but stops mid-chain if an alias is merged with another exported symbol.
    /// <para>
    /// PORT-NOTE: <c>typeOnlyExportStarTargets?.get(name) === target</c> is the port's <c>SymbolTable.TryGetValue</c>
    /// pair; <c>seenSymbols</c> is a <c>HashSet&lt;ISymbol&gt;</c> with reference identity, which is what TS's
    /// <c>Set</c> is.
    /// </para>
    /// </remarks>
    internal SymbolFlags GetSymbolFlags(ISymbol symbol, bool? excludeTypeOnlyMeanings = null, bool? excludeLocalMeanings = null)
    {
        var typeOnlyDeclaration = Js.IsTruthy(excludeTypeOnlyMeanings) ? GetTypeOnlyAliasDeclaration(symbol) : null;
        var typeOnlyDeclarationIsExportStar = typeOnlyDeclaration is not null && NodeTests.IsExportDeclaration(typeOnlyDeclaration);
        ISymbol? typeOnlyResolution = null;
        if (typeOnlyDeclaration is not null)
        {
            var moduleSpecifier = ((ExportDeclaration)typeOnlyDeclaration).ModuleSpecifier;
            typeOnlyResolution = typeOnlyDeclarationIsExportStar
                ? ResolveExternalModuleName(moduleSpecifier!, moduleSpecifier!, /*ignoreErrors*/ true)
                : ResolveAlias(((IDeclaration)typeOnlyDeclaration).Symbol!);
        }

        var typeOnlyExportStarTargets = typeOnlyDeclarationIsExportStar && typeOnlyResolution is not null ? GetExportsOfModule(typeOnlyResolution) : null;
        var flags = Js.IsTruthy(excludeLocalMeanings) ? SymbolFlags.None : symbol.Flags;
        HashSet<ISymbol>? seenSymbols = null;
        while ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            var target = GetExportSymbolOfValueSymbolIfExported(ResolveAlias(symbol));
            var isTypeOnlyExportStarTarget = false;
            if (typeOnlyExportStarTargets is not null && target.EscapedName is not null)
            {
                isTypeOnlyExportStarTarget = typeOnlyExportStarTargets.TryGetValue(target.EscapedName, out var starTarget) && ReferenceEquals(starTarget, target);
            }

            if (
                (!typeOnlyDeclarationIsExportStar && ReferenceEquals(target, typeOnlyResolution)) ||
                isTypeOnlyExportStarTarget
            )
            {
                break;
            }

            if (ReferenceEquals(target, _unknownSymbol))
            {
                return SymbolFlags.All;
            }

            // Optimizations - try to avoid creating or adding to
            // `seenSymbols` if possible
            if (ReferenceEquals(target, symbol) || seenSymbols?.Contains(target) == true)
            {
                break;
            }

            if ((target.Flags & SymbolFlags.Alias) != 0)
            {
                if (seenSymbols is not null)
                {
                    seenSymbols.Add(target);
                }
                else
                {
                    seenSymbols = new HashSet<ISymbol> { symbol, target };
                }
            }

            flags |= target.Flags;
            symbol = target;
        }

        return flags;
    }

    // ---------------------------------------------------------------------------------------------
    // Qualified-name resolution (checker.ts:4502-4630)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:4502-4504 — <c>getFullyQualifiedName</c>.</summary>
    internal string GetFullyQualifiedName(ISymbol symbol, Node? containingLocation = null)
        => symbol.Parent is not null
            ? GetFullyQualifiedName(symbol.Parent, containingLocation) + "." + SymbolToString(symbol)
            : SymbolToString(symbol, containingLocation, /*meaning*/ null, SymbolFormatFlags.DoNotIncludeSymbolChain | SymbolFormatFlags.AllowAnyNodeKind);

    /// <summary>ported from: src/compiler/checker.ts:4533-4630 — <c>resolveEntityName</c>.</summary>
    /// <remarks>
    /// Resolves a qualified name and any involved aliases.
    /// <para>
    /// PORT-NOTE: TS's <c>EntityNameOrEntityNameExpression</c> is <c>Identifier | QualifiedName | PropertyAccessExpression</c>;
    /// the port takes <see cref="Node"/> (the union's upper bound, the same choice
    /// <c>Checker.TypeReferences.cs</c>'s <c>ResolveTypeReferenceName</c> documents) and discriminates on the kind as the
    /// reference does. <c>getExportsOfSymbol</c> is <see cref="GetExportsOfSymbol"/> — landed callers
    /// (<c>Checker.Conditional.cs</c>) already invoke that name.
    /// </para>
    /// </remarks>
    internal ISymbol? ResolveEntityName(Node name, SymbolFlags meaning, bool? ignoreErrors = null, bool? dontResolveAlias = null, Node? location = null)
    {
        if (Utilities.NodeIsMissing(name))
        {
            return null;
        }

        var namespaceMeaning = SymbolFlags.Namespace | (Utilities.IsInJSFile(name) ? meaning & SymbolFlags.Value : 0);
        ISymbol? symbol;
        if (name.Kind == SyntaxKind.Identifier)
        {
            var message = meaning == namespaceMeaning || Utilities.NodeIsSynthesized(name) ? Diagnostics.Cannot_find_namespace_0 : GetCannotFindNameDiagnosticForName(Utilities.GetFirstIdentifier(name));
            var symbolFromJSPrototype = Utilities.IsInJSFile(name) && !Utilities.NodeIsSynthesized(name) ? ResolveEntityNameFromAssignmentDeclaration((Identifier)name, meaning) : null;
            symbol = GetMergedSymbol(ResolveName(location ?? name, ((Identifier)name).EscapedText!, meaning, Js.IsTruthy(ignoreErrors) || symbolFromJSPrototype is not null ? null : message, /*isUse*/ true, /*excludeGlobals*/ false));
            if (symbol is null)
            {
                return GetMergedSymbol(symbolFromJSPrototype);
            }
        }
        else if (name.Kind == SyntaxKind.QualifiedName || name.Kind == SyntaxKind.PropertyAccessExpression)
        {
            Node left = name.Kind == SyntaxKind.QualifiedName ? ((QualifiedName)name).Left! : ((PropertyAccessExpression)name).Expression!;
            Node right = name.Kind == SyntaxKind.QualifiedName ? ((QualifiedName)name).Right! : ((PropertyAccessExpression)name).Name!;
            var ns = ResolveEntityName(left, namespaceMeaning, ignoreErrors, /*dontResolveAlias*/ false, location);
            if (ns is null || Utilities.NodeIsMissing(right))
            {
                return null;
            }

            if (ReferenceEquals(ns, _unknownSymbol))
            {
                return ns;
            }

            if (
                ns.ValueDeclaration is not null &&
                Utilities.IsInJSFile((Node)ns.ValueDeclaration) &&
                Utilities.GetEmitModuleResolutionKind(_compilerOptions) != ModuleResolutionKind.Bundler &&
                NodeTests.IsVariableDeclaration((Node)ns.ValueDeclaration) &&
                ((VariableDeclaration)ns.ValueDeclaration).Initializer is not null &&
                IsCommonJsRequire(((VariableDeclaration)ns.ValueDeclaration).Initializer!)
            )
            {
                var moduleName = (StringLiteral)((CallExpression)((VariableDeclaration)ns.ValueDeclaration).Initializer!).Arguments![0];
                var moduleSym = ResolveExternalModuleName(moduleName, moduleName);
                if (moduleSym is not null)
                {
                    var resolvedModuleSymbol = ResolveExternalModuleSymbol(moduleSym);
                    if (resolvedModuleSymbol is not null)
                    {
                        ns = resolvedModuleSymbol;
                    }
                }
            }

            symbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(ns), ((Identifier)right).EscapedText!, meaning));
            if (symbol is null && (ns.Flags & SymbolFlags.Alias) != 0)
            {
                // `namespace` can be resolved further if there was a symbol merge with a re-export
                symbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(ResolveAlias(ns)), ((Identifier)right).EscapedText!, meaning));
            }

            if (symbol is null)
            {
                if (!Js.IsTruthy(ignoreErrors))
                {
                    var namespaceName = GetFullyQualifiedName(ns);
                    var declarationName = Utilities.DeclarationNameToString(right);
                    var suggestionForNonexistentModule = GetSuggestedSymbolForNonexistentModule((Identifier)right, ns);
                    if (suggestionForNonexistentModule is not null)
                    {
                        Error(right, Diagnostics._0_has_no_exported_member_named_1_Did_you_mean_2, namespaceName, declarationName, SymbolToString(suggestionForNonexistentModule));
                        return null;
                    }

                    var containingQualifiedName = NodeTests.IsQualifiedName(name) ? GetContainingQualifiedNameNode((QualifiedName)name) : null;
                    var canSuggestTypeof = _globalObjectType is not null // <-- can't pull on types if global types aren't initialized yet
                        && (meaning & SymbolFlags.Type) != 0
                        && containingQualifiedName is not null
                        && !NodeTests.IsTypeOfExpression(containingQualifiedName.Parent!)
                        && TryGetQualifiedNameAsValue(containingQualifiedName) is not null;
                    if (canSuggestTypeof)
                    {
                        Error(
                            containingQualifiedName,
                            Diagnostics._0_refers_to_a_value_but_is_being_used_as_a_type_here_Did_you_mean_typeof_0,
                            EntityNameToString(containingQualifiedName)
                        );
                        return null;
                    }

                    if ((meaning & SymbolFlags.Namespace) != 0 && NodeTests.IsQualifiedName(name.Parent!))
                    {
                        var exportedTypeSymbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(ns), ((Identifier)right).EscapedText!, SymbolFlags.Type));
                        if (exportedTypeSymbol is not null)
                        {
                            Error(
                                ((QualifiedName)name.Parent!).Right,
                                Diagnostics.Cannot_access_0_1_because_0_is_a_type_but_not_a_namespace_Did_you_mean_to_retrieve_the_type_of_the_property_1_in_0_with_0_1,
                                SymbolToString(exportedTypeSymbol),
                                Utilities.UnescapeLeadingUnderscores(((QualifiedName)name.Parent!).Right!.EscapedText!)
                            );
                            return null;
                        }
                    }

                    Error(right, Diagnostics.Namespace_0_has_no_exported_member_1, namespaceName, declarationName);
                }

                return null;
            }
        }
        else
        {
            return Debug.Fail<ISymbol?>("Unknown entity name kind.");
        }

        if (!Utilities.NodeIsSynthesized(name) && Utilities.IsEntityName(name) && ((symbol.Flags & SymbolFlags.Alias) != 0 || name.Parent!.Kind == SyntaxKind.ExportAssignment))
        {
            MarkSymbolOfAliasDeclarationIfTypeOnly(GetAliasDeclarationFromName(name), symbol, /*finalTarget*/ null, /*overwriteEmpty*/ true);
        }

        return (symbol.Flags & meaning) != 0 || Js.IsTruthy(dontResolveAlias) ? symbol : ResolveAlias(symbol);
    }

    // ---------------------------------------------------------------------------------------------
    // Expando and external-module helpers (checker.ts:4702-4722, 4985-5179)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:4702-4714 — <c>getExpandoSymbol</c>.</summary>
    /// <remarks>
    /// Get the real symbol of a declaration with an expando initializer.
    /// <para>
    /// Normally, declarations have an associated symbol, but when a declaration has an expando initializer, the
    /// expando's symbol is the one that has all the members merged into it.
    /// </para>
    /// </remarks>
    internal ISymbol? GetExpandoSymbol(ISymbol symbol)
    {
        var decl = symbol.ValueDeclaration;
        if (decl is null || !Utilities.IsInJSFile((Node)decl) || (symbol.Flags & SymbolFlags.TypeAlias) != 0 || Utilities.GetExpandoInitializer((Node)decl, /*isPrototypeAssignment*/ false) is not null)
        {
            return null;
        }

        var init = NodeTests.IsVariableDeclaration((Node)decl) ? Utilities.GetDeclaredExpandoInitializer((Node)decl) : Utilities.GetAssignedExpandoInitializer((Node)decl);
        if (init is not null)
        {
            var initSymbol = GetSymbolOfNode(init);
            if (initSymbol is not null)
            {
                return MergeJSSymbols(initSymbol, symbol);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4716-4722 — <c>resolveExternalModuleName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's four-argument checker member (<c>location, moduleReferenceExpression, ignoreErrors?, errorMessage?</c>)
    /// shares its name with <c>ITypeChecker.resolveExternalModuleName(moduleSpecifier)</c>, the one-argument wrapper
    /// the returned <c>TypeChecker</c> object builds at checker.ts:49715 (<c>(moduleSpecifier) =&gt;
    /// resolveExternalModuleName(moduleSpecifier, moduleSpecifier)</c>). The port keeps this member's checker shape —
    /// every landed caller passes two arguments — and the report names the wrapper's owner.
    /// </remarks>
    internal ISymbol? ResolveExternalModuleName(Node location, Expression? moduleReferenceExpression, bool? ignoreErrors = null, DiagnosticMessage? errorMessage = null)
    {
        var isClassic = Utilities.GetEmitModuleResolutionKind(_compilerOptions) == ModuleResolutionKind.Classic;
        errorMessage ??= GetCannotResolveModuleNameErrorForSpecificModule(moduleReferenceExpression) ?? (isClassic
            ? Diagnostics.Cannot_find_module_0_Did_you_mean_to_set_the_moduleResolution_option_to_nodenext_or_to_add_aliases_to_the_paths_option
            : Diagnostics.Cannot_find_module_0_or_its_corresponding_type_declarations);
        return ResolveExternalModuleNameWorker(location, moduleReferenceExpression, Js.IsTruthy(ignoreErrors) ? null : errorMessage, Js.IsTruthy(ignoreErrors));
    }

    /// <summary>ported from: src/compiler/checker.ts:4985-4994 — <c>resolveExternalModuleSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two declaration-only overloads collapse into this one nullable member, the same treatment
    /// <see cref="ResolveSymbol"/> gets.
    /// </remarks>
    internal ISymbol? ResolveExternalModuleSymbol(ISymbol? moduleSymbol, bool? dontResolveAlias = null)
    {
        if (moduleSymbol?.Exports is not null)
        {
            moduleSymbol.Exports.TryGetValue(InternalSymbolName.ExportEquals.Value, out var exportEqualsMember);
            var exportEquals = ResolveSymbol(exportEqualsMember, Js.IsTruthy(dontResolveAlias));
            var exported = GetCommonJsExportEquals(GetMergedSymbol(exportEquals), GetMergedSymbol(moduleSymbol));
            return GetMergedSymbol(exported) ?? moduleSymbol;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:5175-5179 — <c>getExportsOfSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getResolvedMembersOrExportsOfSymbol(symbol, MembersOrExportsResolutionKind.ResolvedExports)</c>
    /// selects the <c>ResolvedExports</c> slot of the symbol's links; that member is the M6TypeSupport slice's, and the
    /// Module arm calls the checker's own <c>getExportsOfModule</c> (the <c>SymbolTable</c>-returning member, not the
    /// <c>Symbol[]</c>-returning face <c>ITypeChecker.GetExportsOfModule</c> declares) — see the slice report.
    /// </remarks>
    internal SymbolTable GetExportsOfSymbol(ISymbol symbol)
        => (symbol.Flags & SymbolFlags.LateBindingContainer) != 0
            ? GetResolvedMembersOrExportsOfSymbol(symbol, MembersOrExportsResolutionKind.ResolvedExports)
            : (symbol.Flags & SymbolFlags.Module) != 0
                ? GetExportsOfModule(symbol)
                : symbol.Exports ?? _emptySymbols;

    // ---------------------------------------------------------------------------------------------
    // The declaration-to-symbol lookups (checker.ts:5314-5328)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:5314-5316 — <c>getSymbolOfDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.symbol &amp;&amp; getLateBoundSymbol(node.symbol)</c> answers <c>undefined</c> for an unbound
    /// node although TS declares the result <c>Symbol</c>; the port asserts the invariant the ported callers rely on
    /// (every one of them dereferences the result immediately) rather than handing a null back to them.
    /// </remarks>
    internal ISymbol GetSymbolOfDeclaration(IDeclaration node)
        => Debug.CheckDefined(GetMergedSymbol(node.Symbol is not null ? GetLateBoundSymbol(node.Symbol) : null), "a declaration always has a symbol once it is bound");

    /// <summary>ported from: src/compiler/checker.ts:5322-5324 — <c>getSymbolOfNode</c>.</summary>
    /// <remarks>
    /// Get the merged symbol for a node. If you know the node is a <c>Declaration</c>, it is faster and more type safe
    /// to use <c>getSymbolOfDeclaration</c> instead.
    /// </remarks>
    internal ISymbol? GetSymbolOfNode(Node node)
        => Utilities.CanHaveSymbol(node) ? GetSymbolOfDeclaration((IDeclaration)node) : null;

    /// <summary>ported from: src/compiler/checker.ts:5326-5328 — <c>getParentOfSymbol</c>.</summary>
    internal ISymbol? GetParentOfSymbol(ISymbol symbol)
        => GetMergedSymbol(symbol.Parent is not null ? GetLateBoundSymbol(symbol.Parent) : null);

    // ---------------------------------------------------------------------------------------------
    // The type of a declaration (checker.ts:11821-13251)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:11821-11827 — <c>getTypeForDeclarationFromJSDocComment</c>.</summary>
    internal IType? GetTypeForDeclarationFromJSDocComment(Node declaration)
    {
        var jsdocType = Utilities.GetJSDocType(declaration);
        if (jsdocType is not null)
        {
            return GetTypeFromTypeNode(jsdocType);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:12523-12535 — <c>isParameterOfContextSensitiveSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this immediately before <c>getTypeOfVariableOrParameterOrProperty</c> and only that
    /// member's worker calls it; the port states it as a private member of the same file.
    /// </remarks>
    private bool IsParameterOfContextSensitiveSignature(ISymbol symbol)
    {
        var decl = symbol.ValueDeclaration;
        if (decl is null)
        {
            return false;
        }

        var declNode = (Node)decl;
        if (NodeTests.IsBindingElement(declNode))
        {
            declNode = WalkUpBindingElementsAndPatterns(declNode);
        }

        if (NodeTests.IsParameter(declNode))
        {
            return IsContextSensitiveFunctionOrObjectLiteralMethod(declNode.Parent!);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:12537-12552 — <c>getTypeOfVariableOrParameterOrProperty</c>.</summary>
    internal IType GetTypeOfVariableOrParameterOrProperty(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.Type is null)
        {
            var type = GetTypeOfVariableOrParameterOrPropertyWorker(symbol);
            // For a contextually typed parameter it is possible that a type has already
            // been assigned (in assignTypeToParameterAndFixTypeParameters), and we want
            // to preserve this type. In fact, we need to _prefer_ that type, but it won't
            // be assigned until contextual typing is complete, so we need to defer in
            // cases where contextual typing may take place.
            if (links.Type is null && !IsParameterOfContextSensitiveSignature(symbol))
            {
                links.Type = type;
            }

            return type;
        }

        return links.Type;
    }

    /// <summary>ported from: src/compiler/checker.ts:12554-12672 — <c>getTypeOfVariableOrParameterOrPropertyWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the worker is a local function of the resolver-less <c>getTypeOfVariableOrParameterOrProperty</c>,
    /// so the port states it as a private member beside it. The last arm's
    /// <c>Debug.fail("Unhandled declaration kind! " + Debug.formatSyntaxKind(…))</c> becomes
    /// <c>Debug.Fail&lt;IType&gt;</c> and the symbol half of TS's message is <c>Utilities.SymbolName</c> (the port has
    /// no <c>Debug.FormatSymbol</c>); only the debug-only message text differs.
    /// </remarks>
    private IType GetTypeOfVariableOrParameterOrPropertyWorker(ISymbol symbol)
    {
        // Handle prototype property
        if ((symbol.Flags & SymbolFlags.Prototype) != 0)
        {
            return GetTypeOfPrototypeProperty(symbol);
        }

        // CommonsJS require and module both have type any.
        if (ReferenceEquals(symbol, _requireSymbol))
        {
            return _anyType;
        }

        if ((symbol.Flags & SymbolFlags.ModuleExports) != 0 && symbol.ValueDeclaration is not null)
        {
            var fileSymbol = GetSymbolOfDeclaration((IDeclaration)Utilities.GetSourceFileOfNode((Node)symbol.ValueDeclaration)!);
            var result = CreateSymbol(fileSymbol.Flags, "exports");
            result.Declarations = fileSymbol.Declarations is not null ? [.. fileSymbol.Declarations] : [];
            result.Parent = symbol;
            GetSymbolLinks(result).Target = fileSymbol;
            if (fileSymbol.ValueDeclaration is not null) result.ValueDeclaration = fileSymbol.ValueDeclaration;
            if (fileSymbol.Members is not null) result.Members = new SymbolTable(fileSymbol.Members);
            if (fileSymbol.Exports is not null) result.Exports = new SymbolTable(fileSymbol.Exports);
            var members = Utilities.CreateSymbolTable();
            members["exports"] = result;
            return CreateAnonymousType(symbol, members, [], [], []);
        }

        Debug.AssertIsDefined(symbol.ValueDeclaration);
        var declaration = (Node)symbol.ValueDeclaration;
        // Handle export default expressions
        if (NodeTests.IsSourceFile(declaration) && Utilities.IsJsonSourceFile((SourceFile)declaration))
        {
            var sourceFile = (SourceFile)declaration;
            if (sourceFile.Statements is null || sourceFile.Statements.Count == 0)
            {
                return _emptyObjectType;
            }

            return GetWidenedType(GetWidenedLiteralType(CheckExpression(((ExpressionStatement)sourceFile.Statements[0]).Expression!)));
        }

        if (Utilities.IsAccessor(declaration))
        {
            // Binding of certain patterns in JS code will occasionally mark symbols as both properties
            // and accessors. Here we dispatch to accessor resolution if needed.
            return GetTypeOfAccessors(symbol);
        }

        // Handle variable, parameter or property
        if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
        {
            // Symbol is property of some kind that is merged with something - should use `getTypeOfFuncClassEnumModule` and not `getTypeOfVariableOrParameterOrProperty`
            if ((symbol.Flags & SymbolFlags.ValueModule) != 0 && (symbol.Flags & SymbolFlags.Assignment) == 0)
            {
                return GetTypeOfFuncClassEnumModule(symbol);
            }

            return ReportCircularityError(symbol);
        }

        IType type;
        if (declaration.Kind == SyntaxKind.ExportAssignment)
        {
            type = WidenTypeForVariableLikeDeclaration(TryGetTypeFromEffectiveTypeNode(declaration) ?? CheckExpressionCached(((ExportAssignment)declaration).Expression!), declaration);
        }
        else if (
            NodeTests.IsBinaryExpression(declaration) ||
            (Utilities.IsInJSFile(declaration) &&
                ((NodeTests.IsCallExpression(declaration) || (NodeTests.IsPropertyAccessExpression(declaration) || Utilities.IsBindableStaticElementAccessExpression(declaration)) && NodeTests.IsBinaryExpression(declaration.Parent!))))
        )
        {
            type = GetWidenedTypeForAssignmentDeclaration(symbol);
        }
        else if (
            NodeTests.IsPropertyAccessExpression(declaration)
            || NodeTests.IsElementAccessExpression(declaration)
            || NodeTests.IsIdentifier(declaration)
            || NodeTests.IsStringLiteralLike(declaration)
            || NodeTests.IsNumericLiteral(declaration)
            || NodeTests.IsClassDeclaration(declaration)
            || NodeTests.IsFunctionDeclaration(declaration)
            || (NodeTests.IsMethodDeclaration(declaration) && !Utilities.IsObjectLiteralMethod(declaration))
            || NodeTests.IsMethodSignature(declaration)
            || NodeTests.IsSourceFile(declaration)
        )
        {
            // Symbol is property of some kind that is merged with something - should use `getTypeOfFuncClassEnumModule` and not `getTypeOfVariableOrParameterOrProperty`
            if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0)
            {
                return GetTypeOfFuncClassEnumModule(symbol);
            }

            type = NodeTests.IsBinaryExpression(declaration.Parent!)
                ? GetWidenedTypeForAssignmentDeclaration(symbol)
                : TryGetTypeFromEffectiveTypeNode(declaration) ?? _anyType;
        }
        else if (NodeTests.IsPropertyAssignment(declaration))
        {
            type = TryGetTypeFromEffectiveTypeNode(declaration) ?? CheckPropertyAssignment(declaration);
        }
        else if (NodeTests.IsJsxAttribute(declaration))
        {
            type = TryGetTypeFromEffectiveTypeNode(declaration) ?? CheckJsxAttribute(declaration);
        }
        else if (NodeTests.IsShorthandPropertyAssignment(declaration))
        {
            type = TryGetTypeFromEffectiveTypeNode(declaration) ?? CheckExpressionForMutableLocation(((ShorthandPropertyAssignment)declaration).Name!, CheckMode.Normal);
        }
        else if (Utilities.IsObjectLiteralMethod(declaration))
        {
            type = TryGetTypeFromEffectiveTypeNode(declaration) ?? CheckObjectLiteralMethod(declaration, CheckMode.Normal);
        }
        else if (
            NodeTests.IsParameter(declaration)
            || NodeTests.IsPropertyDeclaration(declaration)
            || NodeTests.IsPropertySignature(declaration)
            || NodeTests.IsVariableDeclaration(declaration)
            || NodeTests.IsBindingElement(declaration)
            || Utilities.IsJSDocPropertyLikeTag(declaration)
        )
        {
            type = GetWidenedTypeForVariableLikeDeclaration(declaration, /*reportErrors*/ true);
        }
        // getTypeOfSymbol dispatches some JS merges incorrectly because their symbol flags are not mutually exclusive.
        // Re-dispatch based on valueDeclaration.kind instead.
        else if (NodeTests.IsEnumDeclaration(declaration))
        {
            type = GetTypeOfFuncClassEnumModule(symbol);
        }
        else if (NodeTests.IsEnumMember(declaration))
        {
            type = GetTypeOfEnumMember(symbol);
        }
        else
        {
            // PORT-NOTE: TS's message is `"Unhandled declaration kind! " + Debug.formatSyntaxKind(…) + " for " +
            // Debug.formatSymbol(symbol)`; the port has no `Debug.FormatSymbol` and uses `Utilities.SymbolName`, so
            // only the debugger-facing text differs.
            return Debug.Fail<IType>("Unhandled declaration kind! " + Debug.FormatSyntaxKind(declaration.Kind) + " for " + Utilities.SymbolName(symbol));
        }

        if (!PopTypeResolution())
        {
            // Symbol is property of some kind that is merged with something - should use `getTypeOfFuncClassEnumModule` and not `getTypeOfVariableOrParameterOrProperty`
            if ((symbol.Flags & SymbolFlags.ValueModule) != 0 && (symbol.Flags & SymbolFlags.Assignment) == 0)
            {
                return GetTypeOfFuncClassEnumModule(symbol);
            }

            return ReportCircularityError(symbol);
        }

        return type;
    }

    /// <summary>ported from: src/compiler/checker.ts:12674-12690 — <c>getAnnotatedAccessorTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>AccessorDeclaration | PropertyDeclaration | undefined</c>; the port
    /// takes <see cref="Node"/> — the landed caller in <c>Checker.Queries.cs</c> passes
    /// <c>GetDeclarationOfKind&lt;Node&gt;(…)</c> — and switches on the same three kinds. The auto-accessor arm's
    /// <c>Debug.assert(hasAccessorModifier(accessor))</c> is kept as the port's <c>Debug.Assert</c>.
    /// </remarks>
    private ITypeNode? GetAnnotatedAccessorTypeNode(Node? accessor)
    {
        if (accessor is not null)
        {
            switch (accessor.Kind)
            {
                case SyntaxKind.GetAccessor:
                {
                    var getterTypeAnnotation = Utilities.GetEffectiveReturnTypeNode(accessor);
                    return getterTypeAnnotation;
                }

                case SyntaxKind.SetAccessor:
                {
                    var setterTypeAnnotation = Utilities.GetEffectiveSetAccessorTypeAnnotationNode((SetAccessorDeclaration)accessor);
                    return setterTypeAnnotation;
                }

                case SyntaxKind.PropertyDeclaration:
                {
                    Debug.Assert(Utilities.HasAccessorModifier(accessor));
                    var accessorTypeAnnotation = Utilities.GetEffectiveTypeAnnotationNode(accessor);
                    return accessorTypeAnnotation;
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:12692-12695 — <c>getAnnotatedAccessorType</c>.</summary>
    internal IType? GetAnnotatedAccessorType(Node? accessor)
    {
        var node = GetAnnotatedAccessorTypeNode(accessor);
        return node is not null ? GetTypeFromTypeNode(node) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:12706-12754 — <c>getTypeOfAccessors</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's single <c>||</c> chain over possibly-absent types is the port's sequence of <c>??=</c>
    /// assignments, which fills the same first answer and stops at the same point.
    /// <para>
    /// PORT-NOTE: the third <c>popTypeResolution</c> arm calls <c>error(setter, …)</c> in the reference (an
    /// <c>accessor</c> annotation is available there but the reference names the setter); the port keeps the
    /// reference's argument verbatim.
    /// </para>
    /// </remarks>
    internal IType GetTypeOfAccessors(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.Type is null)
        {
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
            {
                return _errorType;
            }

            var getter = Utilities.GetDeclarationOfKind<Node>(symbol, SyntaxKind.GetAccessor);
            var setter = Utilities.GetDeclarationOfKind<Node>(symbol, SyntaxKind.SetAccessor);
            var accessor = Core.TryCast<Node, Node>(Utilities.GetDeclarationOfKind<Node>(symbol, SyntaxKind.PropertyDeclaration), Utilities.IsAutoAccessorPropertyDeclaration);

            // We try to resolve a getter type annotation, a setter type annotation, or a getter function
            // body return type inference, in that order.
            var type = getter is not null && Utilities.IsInJSFile(getter) ? GetTypeForDeclarationFromJSDocComment(getter) : null;
            type ??= GetAnnotatedAccessorType(getter);
            type ??= GetAnnotatedAccessorType(setter);
            type ??= GetAnnotatedAccessorType(accessor);
            type ??= getter is not null && ((IFunctionLikeDeclarationBase)getter).Body is not null ? GetReturnTypeFromBody(getter) : null;
            type ??= accessor is not null ? GetWidenedTypeForVariableLikeDeclaration(accessor, /*reportErrors*/ true) : null;
            if (type is null)
            {
                if (setter is not null && !IsPrivateWithinAmbient(setter))
                {
                    ErrorOrSuggestion(_noImplicitAny, setter, Diagnostics.Property_0_implicitly_has_type_any_because_its_set_accessor_lacks_a_parameter_type_annotation, SymbolToString(symbol));
                }
                else if (getter is not null && !IsPrivateWithinAmbient(getter))
                {
                    ErrorOrSuggestion(_noImplicitAny, getter, Diagnostics.Property_0_implicitly_has_type_any_because_its_get_accessor_lacks_a_return_type_annotation, SymbolToString(symbol));
                }
                else if (accessor is not null && !IsPrivateWithinAmbient(accessor))
                {
                    ErrorOrSuggestion(_noImplicitAny, accessor, Diagnostics.Member_0_implicitly_has_an_1_type, SymbolToString(symbol), "any");
                }

                type = _anyType;
            }

            if (!PopTypeResolution())
            {
                if (GetAnnotatedAccessorTypeNode(getter) is not null)
                {
                    Error(getter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
                }
                else if (GetAnnotatedAccessorTypeNode(setter) is not null)
                {
                    Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
                }
                else if (GetAnnotatedAccessorTypeNode(accessor) is not null)
                {
                    Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
                }
                else if (getter is not null && _noImplicitAny)
                {
                    Error(getter, Diagnostics._0_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions, SymbolToString(symbol));
                }

                type = _anyType;
            }

            links.Type ??= type;
        }

        return links.Type!;
    }

    /// <summary>ported from: src/compiler/checker.ts:12756-12776 — <c>getWriteTypeOfAccessors</c>.</summary>
    internal IType GetWriteTypeOfAccessors(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.WriteType is null)
        {
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.WriteType))
            {
                return _errorType;
            }

            var setter = Utilities.GetDeclarationOfKind<Node>(symbol, SyntaxKind.SetAccessor)
                ?? Core.TryCast<Node, Node>(Utilities.GetDeclarationOfKind<Node>(symbol, SyntaxKind.PropertyDeclaration), Utilities.IsAutoAccessorPropertyDeclaration);
            var writeType = GetAnnotatedAccessorType(setter);
            if (!PopTypeResolution())
            {
                if (GetAnnotatedAccessorTypeNode(setter) is not null)
                {
                    Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, SymbolToString(symbol));
                }

                writeType = _anyType;
            }

            // Absent an explicit setter type annotation we use the read type of the accessor.
            links.WriteType ??= writeType ?? GetTypeOfAccessors(symbol);
        }

        return links.WriteType!;
    }

    /// <summary>ported from: src/compiler/checker.ts:12778-12783 — <c>getBaseTypeVariableOfClass</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's tail <c>find((baseConstructorType as IntersectionType).types, …)</c> answers
    /// <c>TypeVariable | undefined</c>; the port's <c>Core.Find</c> over the nullable generated slot answers the same.
    /// </remarks>
    private IType? GetBaseTypeVariableOfClass(ISymbol symbol)
    {
        var baseConstructorType = GetBaseConstructorTypeOfClass(GetDeclaredTypeOfClassOrInterface(symbol));
        return (baseConstructorType.Flags & TypeFlags.TypeVariable) != 0 ? baseConstructorType
            : (baseConstructorType.Flags & TypeFlags.Intersection) != 0 ? Core.Find(((IIntersectionType)baseConstructorType).Types, (t, _) => (t.Flags & TypeFlags.TypeVariable) != 0)
            : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:12785-12801 — <c>getTypeOfFuncClassEnumModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>links = merged.links</c> reads the transient symbol's own links; the port states that read
    /// through <c>GetSymbolLinks</c>, which returns that same slot for a transient symbol.
    /// </remarks>
    internal IType GetTypeOfFuncClassEnumModule(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        var originalLinks = links;
        if (links.Type is null)
        {
            var expando = symbol.ValueDeclaration is not null ? GetSymbolOfExpando((Node)symbol.ValueDeclaration, /*allowDeclaration*/ false) : null;
            if (expando is not null)
            {
                var merged = MergeJSSymbols(symbol, expando);
                if (merged is not null)
                {
                    // note:we overwrite links because we just cloned the symbol
                    symbol = merged;
                    links = GetSymbolLinks(merged);
                }
            }

            originalLinks.Type = links.Type = GetTypeOfFuncClassEnumModuleWorker(symbol);
        }

        return links.Type!;
    }

    /// <summary>ported from: src/compiler/checker.ts:12803-12837 — <c>getTypeOfFuncClassEnumModuleWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares <c>type</c> twice, once inside the CommonJS arm and once for the anonymous
    /// object type; C# rejects that (CS0136, an inner local and a method-scope local), so the first is named
    /// <c>assignmentType</c>.
    /// </remarks>
    private IType GetTypeOfFuncClassEnumModuleWorker(ISymbol symbol)
    {
        var declaration = symbol.ValueDeclaration;
        if ((symbol.Flags & SymbolFlags.Module) != 0 && Utilities.IsShorthandAmbientModuleSymbol(symbol))
        {
            return _anyType;
        }
        else if (
            declaration is not null && (declaration.Kind == SyntaxKind.BinaryExpression ||
                Utilities.IsAccessExpression((Node)declaration) && ((Node)declaration).Parent!.Kind == SyntaxKind.BinaryExpression)
        )
        {
            return GetWidenedTypeForAssignmentDeclaration(symbol);
        }
        else if ((symbol.Flags & SymbolFlags.ValueModule) != 0 && declaration is not null && NodeTests.IsSourceFile((Node)declaration) && ((SourceFile)declaration).CommonJsModuleIndicator is not null)
        {
            var resolvedModule = ResolveExternalModuleSymbol(symbol);
            if (!ReferenceEquals(resolvedModule, symbol))
            {
                if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
                {
                    return _errorType;
                }

                var exportEquals = GetMergedSymbol(symbol.Exports![InternalSymbolName.ExportEquals.Value])!;
                var assignmentType = GetWidenedTypeForAssignmentDeclaration(exportEquals, ReferenceEquals(exportEquals, resolvedModule) ? null : resolvedModule);
                if (!PopTypeResolution())
                {
                    return ReportCircularityError(symbol);
                }

                return assignmentType;
            }
        }

        var type = CreateObjectType(ObjectFlags.Anonymous, symbol);
        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var baseTypeVariable = GetBaseTypeVariableOfClass(symbol);
            return baseTypeVariable is not null ? GetIntersectionType([type, baseTypeVariable]) : type;
        }
        else
        {
            return _strictNullChecks && (symbol.Flags & SymbolFlags.Optional) != 0 ? GetOptionalType(type, isProperty: true) : type;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:12844-12871 — <c>getTypeOfAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>symbol.declarations &amp;&amp; …</c> is the port's <c>symbol.Declarations is not null</c> guard,
    /// <c>symbol.declarations!.length</c> is <c>symbol.Declarations!.Count &gt; 0</c>, and
    /// <c>firstDefined(exportSymbol?.declarations, …)</c> passes the nullable list to <c>Core.FirstDefined</c>, which
    /// answers null for an absent list exactly as the reference does.
    /// </remarks>
    internal IType GetTypeOfAlias(ISymbol symbol)
    {
        var links = GetSymbolLinks(symbol);
        if (links.Type is null)
        {
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
            {
                return _errorType;
            }

            var targetSymbol = ResolveAlias(symbol);
            var exportSymbol = symbol.Declarations is not null
                ? GetTargetOfAliasDeclaration(GetDeclarationOfAliasSymbol(symbol)!, /*dontRecursivelyResolve*/ true)
                : null;
            var declaredType = Core.FirstDefined(exportSymbol?.Declarations, (d, _) => NodeTests.IsExportAssignment((Node)d) ? TryGetTypeFromEffectiveTypeNode((Node)d) : null);

            // It only makes sense to get the type of a value symbol. If the result of resolving
            // the alias is not a value, then it has no type. To get the type associated with a
            // type symbol, call getDeclaredTypeOfSymbol.
            // This check is important because without it, a call to getTypeOfSymbol could end
            // up recursively calling getTypeOfAlias, causing a stack overflow.
            links.Type ??= exportSymbol?.Declarations is not null && IsDuplicatedCommonJSExport(exportSymbol.Declarations) && symbol.Declarations!.Count > 0 ? GetFlowTypeFromCommonJSExport(exportSymbol)
                : IsDuplicatedCommonJSExport(symbol.Declarations) ? _autoType
                : declaredType is not null ? declaredType
                : (GetSymbolFlags(targetSymbol) & SymbolFlags.Value) != 0 ? GetTypeOfSymbol(targetSymbol)
                : _errorType;

            if (!PopTypeResolution())
            {
                ReportCircularityError(exportSymbol ?? symbol);
                return links.Type ??= _errorType;
            }
        }

        return links.Type!;
    }

    // ---------------------------------------------------------------------------------------------
    // The target of a type reference (checker.ts:13007-13009)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:13007-13009 — <c>getTargetType</c>.</summary>
    internal IType GetTargetType(IType type)
        => (GetObjectFlags(type) & ObjectFlags.Reference) != 0 ? ((ITypeReference)type).Target! : type;

    // ---------------------------------------------------------------------------------------------
    // The type parameters of a class or interface (checker.ts:13026-13148)
    // ---------------------------------------------------------------------------------------------

    // Appends the type parameters given by a list of declarations to a set of type parameters and returns the resulting set.
    // The function allocates a new array if the input type parameter set is undefined, but otherwise it modifies the set
    // in-place and returns the same array.
    /// <summary>ported from: src/compiler/checker.ts:13029-13034 — <c>appendTypeParameters</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>appendIfUnique</c> is <c>Core.AppendIfUnique</c>, and the port's <c>List</c> is the mutable
    /// array the reference's comment describes, so the in-place modification and the returned instance are the same
    /// thing. The parameter is stated <c>IList</c> (the port's array face) and null-guarded so a possibly-absent
    /// declaration list is the no-op the reference's always-present array is.
    /// </remarks>
    private List<ITypeParameter>? AppendTypeParameters(List<ITypeParameter>? typeParameters, IList<TypeParameterDeclaration>? declarations)
    {
        if (declarations is not null)
        {
            foreach (var declaration in declarations)
            {
                typeParameters = Core.AppendIfUnique(typeParameters, GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(declaration))) as List<ITypeParameter>;
            }
        }

        return typeParameters;
    }

    // Return the outer type parameters of a node or undefined if the node has no outer type parameters.
    /// <summary>ported from: src/compiler/checker.ts:13037-13108 — <c>getOuterTypeParameters</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>while (true) { node = node.parent; … }</c> walks a possibly-absent parent chain and answers
    /// <c>undefined</c> at the top; the port keeps the same walk over a <c>Node?</c> local because <c>Node.Parent</c>
    /// is nullable here. The switch has no matching arm for most kinds, which is the reference's own "keep climbing".
    /// </remarks>
    internal List<ITypeParameter>? GetOuterTypeParameters(Node node, bool? includeThisTypes = null)
    {
        Node? current = node;
        while (true)
        {
            current = current.Parent; // TODO: GH#18217 Use SourceFile kind check instead
            if (current is not null && NodeTests.IsBinaryExpression(current))
            {
                // prototype assignments get the outer type parameters of their constructor function
                var assignmentKind = Utilities.GetAssignmentDeclarationKind(current);
                if (assignmentKind == AssignmentDeclarationKind.Prototype || assignmentKind == AssignmentDeclarationKind.PrototypeProperty)
                {
                    var symbol = GetSymbolOfDeclaration((IDeclaration)((BinaryExpression)current).Left!);
                    if (symbol.Parent is not null && Utilities.FindAncestor((Node?)symbol.Parent.ValueDeclaration, d => ReferenceEquals(current, d)) is null)
                    {
                        current = symbol.Parent.ValueDeclaration!;
                    }
                }
            }

            if (current is null)
            {
                return null;
            }

            var kind = current.Kind;
            switch (kind)
            {
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.JSDocFunctionType:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.TypeAliasDeclaration:
                case SyntaxKind.JSDocTemplateTag:
                case SyntaxKind.JSDocTypedefTag:
                case SyntaxKind.JSDocEnumTag:
                case SyntaxKind.JSDocCallbackTag:
                case SyntaxKind.MappedType:
                case SyntaxKind.ConditionalType:
                {
                    var outerTypeParameters = GetOuterTypeParameters(current, includeThisTypes);
                    if ((kind == SyntaxKind.FunctionExpression || kind == SyntaxKind.ArrowFunction || Utilities.IsObjectLiteralMethod(current)) && IsContextSensitive(current))
                    {
                        var signature = Core.FirstOrUndefined(GetSignaturesOfType(GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)current)), SignatureKind.Call));
                        if (signature?.TypeParameters is not null)
                        {
                            return [.. (outerTypeParameters ?? []), .. signature.TypeParameters];
                        }
                    }

                    if (kind == SyntaxKind.MappedType)
                    {
                        return Core.Append(outerTypeParameters, GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration((IDeclaration)((MappedTypeNode)current).TypeParameter!))) as List<ITypeParameter>;
                    }
                    else if (kind == SyntaxKind.ConditionalType)
                    {
                        return Core.Concatenate(outerTypeParameters, GetInferTypeParameters((ConditionalTypeNode)current)) as List<ITypeParameter>;
                    }

                    var outerAndOwnTypeParameters = AppendTypeParameters(outerTypeParameters, Utilities.GetEffectiveTypeParameterDeclarations(current));
                    var thisType = Js.IsTruthy(includeThisTypes)
                        && (kind == SyntaxKind.ClassDeclaration || kind == SyntaxKind.ClassExpression || kind == SyntaxKind.InterfaceDeclaration || IsJSConstructor(current))
                        && GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration((IDeclaration)current)).ThisType;
                    return thisType is not null ? Core.Append(outerAndOwnTypeParameters, thisType) as List<ITypeParameter> : outerAndOwnTypeParameters;
                }

                case SyntaxKind.JSDocParameterTag:
                {
                    var paramSymbol = Utilities.GetParameterSymbolFromJSDoc((JSDocParameterTag)current);
                    if (paramSymbol is not null)
                    {
                        current = (Node?)paramSymbol.ValueDeclaration;
                    }

                    break;
                }

                case SyntaxKind.JSDoc:
                {
                    var jsDocOuterTypeParameters = GetOuterTypeParameters(current, includeThisTypes);
                    var tags = ((JSDoc)current).Tags;
                    return tags is not null
                        ? AppendTypeParameters(jsDocOuterTypeParameters, Core.FlatMap<JSDocTag, TypeParameterDeclaration>(tags, (t, _) => NodeTests.IsJSDocTemplateTag(t) ? ((JSDocTemplateTag)t).TypeParameters : null))
                        : jsDocOuterTypeParameters;
                }
            }
        }
    }

    // The outer type parameters are those defined by enclosing generic classes, methods, or functions.
    /// <summary>ported from: src/compiler/checker.ts:13111-13127 — <c>getOuterTypeParametersOfClassOrInterface</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>symbol.declarations?.find(…)!</c> asserts a declaration exists, which the reference's own
    /// <c>Debug.assert</c> states; the port keeps the assertion and lets it narrow the local.
    /// </remarks>
    internal List<ITypeParameter>? GetOuterTypeParametersOfClassOrInterface(ISymbol symbol)
    {
        IDeclaration? declaration = (symbol.Flags & SymbolFlags.Class) != 0 || (symbol.Flags & SymbolFlags.Function) != 0
            ? symbol.ValueDeclaration
            : Core.Find(symbol.Declarations, (decl, _) =>
            {
                if (((Node)decl).Kind == SyntaxKind.InterfaceDeclaration)
                {
                    return true;
                }

                if (((Node)decl).Kind != SyntaxKind.VariableDeclaration)
                {
                    return false;
                }

                var initializer = ((VariableDeclaration)(Node)decl).Initializer;
                return initializer is not null && (initializer.Kind == SyntaxKind.FunctionExpression || initializer.Kind == SyntaxKind.ArrowFunction);
            });
        Debug.Assert(declaration is not null, "Class was missing valueDeclaration -OR- non-class had no interface declarations");
        return GetOuterTypeParameters((Node)declaration);
    }

    // The local type parameters are the combined set of type parameters from all declarations of the class,
    // interface, or type alias.
    /// <summary>ported from: src/compiler/checker.ts:13130-13148 — <c>getLocalTypeParametersOfClassOrInterfaceOrTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows each declaration to <c>InterfaceDeclaration | TypeAliasDeclaration | JSDocTypedefTag |
    /// JSDocCallbackTag</c> before passing it to <c>getEffectiveTypeParameterDeclarations</c>; the port's nodes are
    /// concrete classes, so the loop casts each declaration to <see cref="Node"/> and lets that member take the same
    /// union's upper bound.
    /// </remarks>
    internal List<ITypeParameter>? GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(ISymbol symbol)
    {
        if (symbol.Declarations is null)
        {
            return null;
        }

        List<ITypeParameter>? result = null;
        foreach (var node in symbol.Declarations)
        {
            if (
                ((Node)node).Kind == SyntaxKind.InterfaceDeclaration ||
                ((Node)node).Kind == SyntaxKind.ClassDeclaration ||
                ((Node)node).Kind == SyntaxKind.ClassExpression ||
                IsJSConstructor((Node)node) ||
                Utilities.IsTypeAlias((Node)node)
            )
            {
                result = AppendTypeParameters(result, Utilities.GetEffectiveTypeParameterDeclarations((Node)node));
            }
        }

        return result;
    }

    // A type is a mixin constructor if it has a single construct signature taking no type parameters and a single
    // rest parameter of type any[].
    /// <summary>ported from: src/compiler/checker.ts:13158-13168 — <c>isMixinConstructorType</c>.</summary>
    internal bool IsMixinConstructorType(IType type)
    {
        var signatures = GetSignaturesOfType(type, SignatureKind.Construct);
        if (signatures.Count == 1)
        {
            var s = signatures[0];
            if (s.TypeParameters is null && Core.Length(s.Parameters) == 1 && SignatureHasRestParameter(s))
            {
                var paramType = GetTypeOfParameter(s.Parameters![0]);
                return IsTypeAny(paramType) || ReferenceEquals(GetElementTypeOfArrayType(paramType), _anyType);
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:13181-13184 — <c>getBaseTypeNodeOfClass</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>decl &amp;&amp; getEffectiveBaseTypeNode(decl)</c> answers <c>undefined</c> for a class with no
    /// declaration; the port keeps that as the null arm of the conditional.
    /// </remarks>
    internal ExpressionWithTypeArguments? GetBaseTypeNodeOfClass(IInterfaceType type)
    {
        var decl = Utilities.GetClassLikeDeclarationOfSymbol(type.Symbol!);
        return decl is not null ? (ExpressionWithTypeArguments?)Utilities.GetEffectiveBaseTypeNode(decl) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:13206-13251 — <c>getBaseConstructorTypeOfClass</c>.</summary>
    /// <remarks>
    /// The base constructor of a class can resolve to undefinedType if the class has no extends clause, errorType if an
    /// error occurred during resolution of the extends expression, nullType if the extends expression is the null
    /// value, anyType if the extends expression has type any, or an object type with at least one construct signature.
    /// <para>
    /// PORT-NOTE: TS tests <c>type.resolvedBaseConstructorType</c> for truthiness and declares the return <c>Type</c>;
    /// the port's slot is nullable, so the guard is <c>is null</c> (the same absent value) and the final read asserts
    /// the slot that every path above assigned. The <c>type as ObjectType</c> cast of
    /// <c>resolveStructuredTypeMembers</c> is the port's <see cref="UnionOrIntersectionType"/>, which
    /// <c>ObjectType</c> derives from (so it is the same upcast).
    /// </para>
    /// </remarks>
    internal IType GetBaseConstructorTypeOfClass(IInterfaceType type)
    {
        if (type.ResolvedBaseConstructorType is null)
        {
            var decl = Utilities.GetClassLikeDeclarationOfSymbol(type.Symbol!);
            var extended = decl is not null ? (ExpressionWithTypeArguments?)Utilities.GetEffectiveBaseTypeNode(decl) : null;
            var baseTypeNode = GetBaseTypeNodeOfClass(type);
            if (baseTypeNode is null)
            {
                return type.ResolvedBaseConstructorType = _undefinedType;
            }

            if (!PushTypeResolution(type, TypeSystemPropertyName.ResolvedBaseConstructorType))
            {
                return _errorType;
            }

            var baseConstructorType = CheckExpression(baseTypeNode.Expression!);
            if (extended is not null && !ReferenceEquals(baseTypeNode, extended))
            {
                Debug.Assert(extended.TypeArguments is null); // Because this is in a JS file, and baseTypeNode is in an @extends tag
                CheckExpression(extended.Expression!);
            }

            if ((baseConstructorType.Flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
            {
                // Resolving the members of a class requires us to resolve the base class of that class.
                // We force resolution here such that we catch circularities now.
                ResolveStructuredTypeMembers((UnionOrIntersectionType)baseConstructorType);
            }

            if (!PopTypeResolution())
            {
                Error((Node?)type.Symbol!.ValueDeclaration, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_base_expression, SymbolToString(type.Symbol!));
                return type.ResolvedBaseConstructorType ??= _errorType;
            }

            if ((baseConstructorType.Flags & TypeFlags.Any) == 0 && !ReferenceEquals(baseConstructorType, _nullWideningType) && !IsConstructorType(baseConstructorType))
            {
                var err = Error(baseTypeNode.Expression!, Diagnostics.Type_0_is_not_a_constructor_function_type, TypeToString(baseConstructorType));
                if ((baseConstructorType.Flags & TypeFlags.TypeParameter) != 0)
                {
                    var constraint = GetConstraintFromTypeParameter((ITypeParameter)baseConstructorType);
                    IType ctorReturn = _unknownType;
                    if (constraint is not null)
                    {
                        var ctorSig = GetSignaturesOfType(constraint, SignatureKind.Construct);
                        if (Core.Length(ctorSig) > 0)
                        {
                            ctorReturn = GetReturnTypeOfSignature(ctorSig[0]);
                        }
                    }

                    if (baseConstructorType.Symbol!.Declarations is not null)
                    {
                        Utilities.AddRelatedInfo(err, Utilities.CreateDiagnosticForNode((Node)baseConstructorType.Symbol.Declarations[0], Diagnostics.Did_you_mean_for_0_to_be_constrained_to_type_new_args_Colon_any_1, SymbolToString(baseConstructorType.Symbol), TypeToString(ctorReturn)));
                    }
                }

                return type.ResolvedBaseConstructorType ??= _errorType;
            }

            type.ResolvedBaseConstructorType ??= baseConstructorType;
        }

        // PORT-NOTE: every path above assigned the slot (which is what the reference's declared `Type` return relies
        // on); Debug.CheckDefined states that invariant instead of handing the caller a null.
        return Debug.CheckDefined(type.ResolvedBaseConstructorType, "a resolved base constructor type is assigned before it is read");
    }

    /// <summary>ported from: src/compiler/checker.ts:13279-13310 — <c>getBaseTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the three members the reference resolves through here — <c>getTupleBaseType</c>,
    /// <c>resolveBaseTypesOfClass</c>, <c>resolveBaseTypesOfInterface</c> and <c>reportCircularBaseType</c>
    /// (checker.ts:13312-13430) — are called by reference; the slice report lists them. TS's <c>type as TupleType</c>
    /// is this file's cast to <see cref="ITupleType"/>, the face <c>getTupleBaseType</c> takes.
    /// <para>
    /// PORT-NOTE: the reference's declared <c>BaseType[]</c> return reads <c>type.resolvedBaseTypes</c>, which a
    /// failing <c>pushTypeResolution</c> leaves to the frame that already pushed it (that frame assigns the resolving
    /// empty list before it recurses), so the slot is assigned on every path; the port asserts it with
    /// <c>Debug.CheckDefined</c> because the landed caller reads <c>.Count</c> off the result.
    /// </para>
    /// </remarks>
    internal List<IType> GetBaseTypes(IInterfaceType type)
    {
        if ((GetObjectFlags(type) & (ObjectFlags.ClassOrInterface | ObjectFlags.Reference)) == 0)
        {
            return [];
        }

        if (type.BaseTypesResolved != true)
        {
            if (PushTypeResolution(type, TypeSystemPropertyName.ResolvedBaseTypes))
            {
                if ((type.ObjectFlags & ObjectFlags.Tuple) != 0)
                {
                    type.ResolvedBaseTypes = [GetTupleBaseType((ITupleType)type)];
                }
                else if ((type.Symbol!.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
                {
                    if ((type.Symbol!.Flags & SymbolFlags.Class) != 0)
                    {
                        ResolveBaseTypesOfClass(type);
                    }

                    if ((type.Symbol!.Flags & SymbolFlags.Interface) != 0)
                    {
                        ResolveBaseTypesOfInterface(type);
                    }
                }
                else
                {
                    Debug.Fail("type must be class or interface");
                }

                if (!PopTypeResolution() && type.Symbol!.Declarations is not null)
                {
                    foreach (var declaration in type.Symbol!.Declarations)
                    {
                        if (((Node)declaration).Kind == SyntaxKind.ClassDeclaration || ((Node)declaration).Kind == SyntaxKind.InterfaceDeclaration)
                        {
                            ReportCircularBaseType((Node)declaration, type);
                        }
                    }
                }
            }

            type.BaseTypesResolved = true;
        }

        return Debug.CheckDefined(type.ResolvedBaseTypes, "a base-types resolution always assigns resolvedBaseTypes");
    }
}
