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
/// symbol suggestions is a second <c>createNameResolver</c> call over the same helpers and differs only in its
/// <c>lookup</c> callback; the port states it as <see cref="ResolveNameForSymbolSuggestion"/>, which shares
/// <see cref="ResolveName"/>'s body and switches the lookup on a flag.
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
/// does. Where the union a guard admits is wider than the face (<c>isFunctionLike</c> holds of every
/// <c>SignatureDeclaration</c>, and only the <c>FunctionLikeDeclaration</c> arm declares <c>body</c>), the file reads
/// the slot through a union read instead of casting — see <see cref="GetFunctionLikeBody"/>.
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
    internal ISymbol? ResolveName(Node? location, string name, SymbolFlags meaning, DiagnosticMessage? nameNotFoundMessage, bool isUse, bool excludeGlobals, bool forSymbolSuggestion = false)
    {
        // PORT-NOTE: TS builds two resolvers from one factory (createNameResolver, utilities.ts:11491) — resolveName
        // (checker.ts:1579) and resolveNameForSymbolSuggestion (checker.ts:1594) — whose only difference is the
        // `lookup` callback (getSymbol vs. getSuggestionForSymbolNameLookup, checker.ts:35274). The port states that
        // one difference as this switch, so the factory's single body serves both.
        ISymbol? Lookup(SymbolTable symbols, string symbolName, SymbolFlags symbolMeaning)
            => forSymbolSuggestion
                ? GetSuggestionForSymbolNameLookup(symbols, symbolName, symbolMeaning)
                : GetSymbol(symbols, symbolName, symbolMeaning);
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
                // PORT-NOTE: TS types `Node.parent` non-null, so this assignment keeps `location` non-null for the
                // code below (which calls `canHaveLocals(location)`); the port's slot is nullable, hence the `!`.
                location = location.Parent!;
            }
            // Locals of a source file are not in scope (because they get merged into the global symbol table)
            // PORT-NOTE: TS's `canHaveLocals(location) && location.locals && !isGlobalSourceFile(location)` is
            // evaluated here as this local plus the guard below, which is the same left-to-right order (the table is
            // read only when the node has locals) and lets C# see the table as non-null in the body.
            var locals = Utilities.CanHaveLocals(location) ? ((ILocalsContainer)location).Locals : null;
            if (locals is not null && !Utilities.IsGlobalSourceFile(location))
            {
                // PORT-NOTE: the reference's `location.locals` truthiness guard is what makes this table non-null;
                // the null-forgiving operator states that invariant for C#.
                if ((result = Lookup(locals!, name, meaning)) is not null)
                {
                    var useResult = true;
                    if (Utilities.IsFunctionLike(location) && lastLocation is not null && !ReferenceEquals(lastLocation, GetFunctionLikeBody(location)))
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
                    if (name != InternalSymbolName.Default.Value && (result = Lookup(moduleExports, name, meaning & SymbolFlags.ModuleMember)) is not null)
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
                    if ((result = Lookup(GetSymbolOfDeclaration((IDeclaration)location).Exports ?? _emptySymbols, name, meaning & SymbolFlags.EnumMember)) is not null)
                    {
                        if (nameNotFoundMessage is not null && Utilities.GetIsolatedModules(_compilerOptions) && (location.Flags & NodeFlags.Ambient) == 0
                            && !ReferenceEquals(Utilities.GetSourceFileOfNode(location), Utilities.GetSourceFileOfNode((Node?)result.ValueDeclaration)))
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
                            if (Lookup(ctor.Locals, name, meaning & SymbolFlags.Value) is not null)
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
                    if ((result = Lookup(GetSymbolOfDeclaration((IDeclaration)location).Members ?? _emptySymbols, name, meaning & SymbolFlags.Type)) is not null)
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
                    // PORT-NOTE: the reference reads `(location.parent as HeritageClause).token` (utilities.ts:11711), a
                    // slot only a heritage clause declares; on the parser's `Foo<T>.Bar()` shape the parent is a
                    // PropertyAccessExpression, where JS reads `undefined` and this disjunct is false. The pattern is
                    // that read, so a parent that is not a heritage clause answers no token instead of failing the cast.
                    if (ReferenceEquals(lastLocation, ((ExpressionWithTypeArguments)location).Expression)
                        && location.Parent is HeritageClause heritageClause && heritageClause.Token == SyntaxKind.ExtendsKeyword)
                    {
                        var container = heritageClause.Parent!;
                        if (Utilities.IsClassLike(container)
                            && (result = Lookup(GetSymbolOfDeclaration((IDeclaration)container).Members!, name, meaning & SymbolFlags.Type)) is not null)
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
                        if ((result = Lookup(GetSymbolOfDeclaration((IDeclaration)grandparent).Members!, name, meaning & SymbolFlags.Type)) is not null)
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
                        location = location.Parent!;
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
                        location = location.Parent!;
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
                        location = root.Parent!;
                    }

                    break;
                }
                case SyntaxKind.Parameter:
                    if (lastLocation is not null
                        && (ReferenceEquals(lastLocation, ((ParameterDeclaration)location).Initializer)
                            || (ReferenceEquals(lastLocation, ((ParameterDeclaration)location).Name) && Utilities.IsBindingPattern(lastLocation))))
                    {
                        associatedDeclarationForContainingInitializerOrBindingName ??= location;
                    }

                    break;
                case SyntaxKind.BindingElement:
                    if (lastLocation is not null
                        && (ReferenceEquals(lastLocation, ((BindingElement)location).Initializer)
                            || (ReferenceEquals(lastLocation, ((BindingElement)location).Name) && Utilities.IsBindingPattern(lastLocation))))
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
            // PORT-NOTE: `isSelfReferenceLocation` is a type predicate upstream (`node is SelfReferenceLocation`), so
            // TS keeps the declaration face here; C# states that cast.
            if (IsSelfReferenceLocation(location, lastLocation))
            {
                lastSelfReferenceLocation = (IDeclaration)location;
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
                result = Lookup(_globals, name, meaning);
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

    /// <summary>ported from: src/compiler/checker.ts:1594-1604 — <c>resolveNameForSymbolSuggestion</c>, the checker's second <c>createNameResolver</c> call.</summary>
    /// <remarks>
    /// PORT-NOTE: the factory's only difference from <see cref="ResolveName"/> is the <c>lookup</c> callback
    /// (<c>getSuggestionForSymbolNameLookup</c>, checker.ts:35274), which the shared body switches on — see that
    /// member's PORT-NOTE. The factory's other options (<c>onPropertyWithInvalidInitializer</c>,
    /// <c>onFailedToResolveSymbol</c>/<c>onSuccessfullyResolvedSymbol</c>) are absent here because the suggestion
    /// resolver leaves them at their <c>returnFalse</c>/<c>returnUndefined</c> defaults, and the body only reaches them
    /// under a non-null <c>nameNotFoundMessage</c>, which this entry point never passes.
    /// </remarks>
    internal ISymbol? ResolveNameForSymbolSuggestion(Node? location, string name, SymbolFlags meaning, DiagnosticMessage? nameNotFoundMessage, bool isUse, bool excludeGlobals)
        => ResolveName(location, name, meaning, nameNotFoundMessage, isUse, excludeGlobals, /*forSymbolSuggestion*/ true);

    /// <summary>The <c>location as FunctionLikeDeclaration</c> <c>body</c> read of utilities.ts:11545 and 11908-11914.</summary>
    /// <remarks>
    /// PORT-NOTE: both reads are guarded by <c>isFunctionLike</c> (utilities.ts:1675), whose TS signature narrows to
    /// <c>SignatureDeclaration</c> — the seven <c>FunctionLikeDeclaration</c> kinds plus the call, construct and index
    /// signatures and the function and constructor types. Only the first group declares <c>body</c>; on the
    /// signature-only kinds JS reads <c>undefined</c>, which is what the absence of
    /// <see cref="IFunctionLikeDeclarationBase"/> answers here (the treatment <c>Utilities.GetFunctionFlags</c> gives
    /// the same <c>as FunctionLikeDeclaration</c> cast).
    /// </remarks>
    private static Node? GetFunctionLikeBody(Node location)
        => location is IFunctionLikeDeclarationBase functionLike ? functionLike.Body : null;

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
        // PORT-NOTE: TS's `location as FunctionLikeDeclaration` (utilities.ts:11908) is a no-op re-view: the `body` it
        // then tests is simply absent on the signature kinds without one (the guarded caller admits a FunctionTypeNode),
        // where JS reads `undefined` and the test is false. The port reads the slot through the same union read
        // <see cref="ResolveName"/> uses, so a signature without a body answers `null` instead of failing the cast.
        var functionBody = GetFunctionLikeBody(location);
        // PORT-NOTE: TS reads `result.valueDeclaration.pos/.end` off the `Declaration` face (whose `Node` base carries
        // the range); the port reads the same range through <see cref="Node"/>.
        var valueDeclaration = (Node?)result.ValueDeclaration;
        if (NodeTests.IsParameter(lastLocation)
            && functionBody is not null
            && valueDeclaration is not null
            && valueDeclaration.Pos >= functionBody.Pos
            && valueDeclaration.End <= functionBody.End)
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
                // PORT-NOTE: `forEachChild(node, requiresScopeChangeWorker) || false` is the walk's own `default`
                // answer here — ForEachChildTable.ForEachChild stops on the first *truthy* child result (parser.ts:505,
                // its helpers test with Js.IsTruthy) and answers `default(bool)` when none matched.
                return ForEachChildTable.ForEachChild<bool>(node, child => RequiresScopeChangeWorker(child, target));
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
                // PORT-NOTE (A″): printer-gated `SymbolToString`/`TypeToString` arguments below are deferred to the message's first read via `Diagnostic.DeferArg` (Core/Diagnostic.cs); values used eagerly stay unwrapped.
                Error((Node)node, Diagnostics.Circular_definition_of_import_alias_0, Diagnostic.DeferArg(() => SymbolToString(symbol)));
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
            // PORT-NOTE: TS reads `typeOnlyDeclaration.moduleSpecifier` only in the ternary's export-star arm, which
            // the aliased condition `typeOnlyDeclarationIsExportStar` narrows to ExportDeclaration (checker.ts:4354-4358).
            // The other members of the TypeOnlyDeclaration union (ImportClause, ImportSpecifier, ImportEqualsDeclaration,
            // NamespaceImport) declare no `moduleSpecifier`, so JS reads `undefined` there and takes the resolveAlias arm;
            // the port states that narrowing as the pattern and reads the slot in the export-star arm alone.
            typeOnlyResolution = typeOnlyDeclarationIsExportStar && typeOnlyDeclaration is ExportDeclaration exportStarDeclaration
                ? ResolveExternalModuleName(exportStarDeclaration.ModuleSpecifier!, exportStarDeclaration.ModuleSpecifier!, /*ignoreErrors*/ true)
                : ResolveAlias(((IDeclaration)typeOnlyDeclaration).Symbol!);
        }

        var typeOnlyExportStarTargets = typeOnlyDeclarationIsExportStar && typeOnlyResolution is not null ? GetExportsOfModuleTable(typeOnlyResolution) : null;
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

            // PORT-NOTE: TS guards `namespace.valueDeclaration` and then reads it again inside the arm; the port
            // keeps one local so the C# flow analysis sees the same narrowed value at every read.
            var namespaceValueDeclaration = (Node?)ns.ValueDeclaration;
            // PORT-NOTE: the reference reads `namespace.valueDeclaration.initializer` before its own
            // `isVariableDeclaration` guard (checker.ts:4559-4564), so on any other declaration — a namespace, class or
            // enum declaration declares no `initializer` — JS reads `undefined` and the arm is skipped; the port reads
            // the slot in the narrowed arm, which is the value the reference's later guard would admit.
            var namespaceInitializer = namespaceValueDeclaration is VariableDeclaration namespaceVariableDeclaration
                ? namespaceVariableDeclaration.Initializer
                : null;
            if (
                namespaceValueDeclaration is not null &&
                namespaceInitializer is not null &&
                Utilities.IsInJSFile(namespaceValueDeclaration) &&
                Utilities.GetEmitModuleResolutionKind(_compilerOptions) != ModuleResolutionKind.Bundler &&
                NodeTests.IsVariableDeclaration(namespaceValueDeclaration) &&
                IsCommonJsRequire(namespaceInitializer)
            )
            {
                // PORT-NOTE: as above — the guard read `namespace.valueDeclaration.initializer` as present, so the call
                // expression's arguments are the reference's own assumption.
                var moduleName = (StringLiteral)((CallExpression)namespaceInitializer!).Arguments![0];
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
                        Error(right, Diagnostics._0_has_no_exported_member_named_1_Did_you_mean_2, namespaceName, declarationName, Diagnostic.DeferArg(() => SymbolToString(suggestionForNonexistentModule)));
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
                        // PORT-NOTE: the reference narrows `containingQualifiedName` by the `canSuggestTypeof` expression
                        // itself; C# does not track a bool local's guard, so the null-forgiving operator states the
                        // invariant the reference's own condition already established.
                        Error(
                            containingQualifiedName,
                            Diagnostics._0_refers_to_a_value_but_is_being_used_as_a_type_here_Did_you_mean_typeof_0,
                            Utilities.EntityNameToString(containingQualifiedName!)
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
                                Diagnostic.DeferArg(() => SymbolToString(exportedTypeSymbol)),
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
            MarkSymbolOfAliasDeclarationIfTypeOnly(Utilities.GetAliasDeclarationFromName(name), symbol, /*finalTarget*/ null, /*overwriteEmpty*/ true);
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
            var exported = GetCommonJsExportEquals(GetMergedSymbol(exportEquals), GetMergedSymbol(moduleSymbol)!);
            return GetMergedSymbol(exported) ?? moduleSymbol;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:5175-5179 — <c>getExportsOfSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getResolvedMembersOrExportsOfSymbol(symbol, MembersOrExportsResolutionKind.ResolvedExports)</c>
    /// selects the <c>ResolvedExports</c> slot of the symbol's links; that member is the M6TypeSupport slice's, and the
    /// Module arm calls <see cref="GetExportsOfModuleTable"/> (the internal <c>SymbolTable</c>-returning
    /// <c>getExportsOfModule</c>, checker.ts:5180 — not the <c>Symbol[]</c>-returning face
    /// <c>ITypeChecker.GetExportsOfModule</c>).
    /// </remarks>
    internal SymbolTable GetExportsOfSymbol(ISymbol symbol)
        => (symbol.Flags & SymbolFlags.LateBindingContainer) != 0
            ? GetResolvedMembersOrExportsOfSymbol(symbol, MembersOrExportsResolutionKind.ResolvedExports)
            : (symbol.Flags & SymbolFlags.Module) != 0
                ? GetExportsOfModuleTable(symbol)
                : symbol.Exports ?? _emptySymbols;

    /// <summary>ported from: src/compiler/checker.ts:5180-5186 — <c>getExportsOfModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: checker.ts holds two members of this reference name — this internal one (<c>SymbolTable</c>-returning,
    /// :5180) and the public <c>Symbol[]</c>-returning <c>ITypeChecker.getExportsOfModule</c> that the checker assembles
    /// from <c>getExportsOfModuleAsArray</c>. Because <c>Checker</c> implements <c>ITypeChecker</c> the public member owns
    /// the name, so this one is stated as <c>GetExportsOfModuleTable</c> (Main's ruling 2) and the two callers in this
    /// file use that name.
    /// <para>
    /// PORT-NOTE: the reference destructures <c>getExportsOfModuleWorker</c>'s <c>{ exports, typeOnlyExportStarMap }</c>;
    /// that worker (checker.ts:5188-5306) belongs to the <c>Checker.Symbols.cs</c> band and is called here by reference
    /// with the reference's two-element shape — see the slice report.
    /// </para>
    /// </remarks>
    internal SymbolTable GetExportsOfModuleTable(ISymbol moduleSymbol)
    {
        var links = GetSymbolLinks(moduleSymbol);
        if (links.ResolvedExports is null)
        {
            var (exports, typeOnlyExportStarMap) = GetExportsOfModuleWorker(moduleSymbol);
            links.ResolvedExports = exports;
            links.TypeOnlyExportStarMap = typeOnlyExportStarMap;
        }

        return links.ResolvedExports!;
    }

    // ---------------------------------------------------------------------------------------------
    // The declaration-to-symbol lookups (checker.ts:5314-5328)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:5314-5316 — <c>getSymbolOfDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's body is <c>getMergedSymbol(node.symbol &amp;&amp; getLateBoundSymbol(node.symbol))!</c>
    /// — the <c>!</c> is a lie its declaration tells its callers. A declaration the binder left unbound answers
    /// <c>undefined</c>; measured on <c>classStaticBlock22.ts</c> at the <c>await</c> identifier of an illegal
    /// `let await` in a static block, at the enclosing <c>VariableDeclaration</c> of an illegal binding pattern, and at
    /// the <c>PropertyAccessExpression</c> left-hand side of an assignment (where <c>canHaveSymbol</c> is true but
    /// nothing bound a symbol). Callers that can meet such a declaration are written tolerantly in the reference
    /// (<c>checker.ts:5322-5324</c>'s <c>getSymbolOfNode</c> only reaches it behind <c>canHaveSymbol</c>, and
    /// <c>checker.ts:41091</c> reads <c>symbol?.exports</c>), so the port states the same non-nullable result with the
    /// same body — without a port-only assertion that turned the reference's ordinary <c>undefined</c> into a crash.
    /// </remarks>
    internal ISymbol GetSymbolOfDeclaration(IDeclaration node)
        => GetMergedSymbol(node.Symbol is not null ? GetLateBoundSymbol(node.Symbol) : null)!;

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
            // PORT-NOTE: `walkUpBindingElementsAndPatterns` is a utilities.ts member in the port's tree
            // (Utilities/Utilities.Binder.cs), so the call is qualified; the reference's `isBindingElement` guard is what
            // makes the BindingElement cast legal.
            declNode = Utilities.WalkUpBindingElementsAndPatterns((BindingElement)declNode);
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
            || Utilities.IsStringLiteralLike(declaration)
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
            type = TryGetTypeFromEffectiveTypeNode(declaration) ?? CheckJsxAttribute((JsxAttribute)declaration);
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
                    ErrorOrSuggestion(_noImplicitAny, setter, Diagnostics.Property_0_implicitly_has_type_any_because_its_set_accessor_lacks_a_parameter_type_annotation, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                else if (getter is not null && !IsPrivateWithinAmbient(getter))
                {
                    ErrorOrSuggestion(_noImplicitAny, getter, Diagnostics.Property_0_implicitly_has_type_any_because_its_get_accessor_lacks_a_return_type_annotation, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                else if (accessor is not null && !IsPrivateWithinAmbient(accessor))
                {
                    ErrorOrSuggestion(_noImplicitAny, accessor, Diagnostics.Member_0_implicitly_has_an_1_type, Diagnostic.DeferArg(() => SymbolToString(symbol)), "any");
                }

                type = _anyType;
            }

            if (!PopTypeResolution())
            {
                if (GetAnnotatedAccessorTypeNode(getter) is not null)
                {
                    Error(getter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                else if (GetAnnotatedAccessorTypeNode(setter) is not null)
                {
                    Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                else if (GetAnnotatedAccessorTypeNode(accessor) is not null)
                {
                    Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, Diagnostic.DeferArg(() => SymbolToString(symbol)));
                }
                else if (getter is not null && _noImplicitAny)
                {
                    Error(getter, Diagnostics._0_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions, Diagnostic.DeferArg(() => SymbolToString(symbol)));
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
                    Error(setter, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_type_annotation, Diagnostic.DeferArg(() => SymbolToString(symbol)));
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
            current = current!.Parent; // TODO: GH#18217 Use SourceFile kind check instead
            if (current is not null && NodeTests.IsBinaryExpression(current))
            {
                // prototype assignments get the outer type parameters of their constructor function
                var assignmentKind = Utilities.GetAssignmentDeclarationKind(current);
                if (assignmentKind == AssignmentDeclarationKind.Prototype || assignmentKind == AssignmentDeclarationKind.PrototypeProperty)
                {
                    var symbol = GetSymbolOfDeclaration((IDeclaration)((BinaryExpression)current).Left!);
                    if (symbol.Parent is not null && Utilities.FindAncestor((Node?)symbol.Parent.ValueDeclaration, d => ReferenceEquals(current, d)) is null)
                    {
                        current = (Node?)symbol.Parent.ValueDeclaration;
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
                    // PORT-NOTE: TS's `includeThisTypes && (kind === …) && getDeclaredTypeOfClassOrInterface(…).thisType`
                    // is a JS `&&` chain whose last operand is a type, so C# states the same short-circuit as this
                    // conditional (the class-or-interface lookup still happens only when the first two operands hold).
                    var thisType = Js.IsTruthy(includeThisTypes)
                        && (kind == SyntaxKind.ClassDeclaration || kind == SyntaxKind.ClassExpression || kind == SyntaxKind.InterfaceDeclaration || IsJSConstructor(current))
                            ? GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration((IDeclaration)current)).ThisType
                            : null;
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
                Error((Node?)type.Symbol!.ValueDeclaration, Diagnostics._0_is_referenced_directly_or_indirectly_in_its_own_base_expression, Diagnostic.DeferArg(() => SymbolToString(type.Symbol!)));
                return type.ResolvedBaseConstructorType ??= _errorType;
            }

            if ((baseConstructorType.Flags & TypeFlags.Any) == 0 && !ReferenceEquals(baseConstructorType, _nullWideningType) && !IsConstructorType(baseConstructorType))
            {
                var err = Error(baseTypeNode.Expression!, Diagnostics.Type_0_is_not_a_constructor_function_type, Diagnostic.DeferArg(() => TypeToString(baseConstructorType)));
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
                        Utilities.AddRelatedInfo(err, Utilities.CreateDiagnosticForNode((Node)baseConstructorType.Symbol.Declarations[0], Diagnostics.Did_you_mean_for_0_to_be_constrained_to_type_new_args_Colon_any_1, Diagnostic.DeferArg(() => SymbolToString(baseConstructorType.Symbol)), Diagnostic.DeferArg(() => TypeToString(ctorReturn))));
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

    // ---------------------------------------------------------------------------------------------
    // The symbol band's alias, JSX and module helpers (checker.ts:2414-5200)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE (isDefinitelyReferenceToGlobalSymbolObject, checker.ts:2414-2425): this band's order file lists it, but
    // `Checker/Checker.Support.cs` landed the same member (Checker class, `Node?` parameter) while this file was being
    // written, so the port keeps that single declaration and this file does not repeat it.

    /// <summary>ported from: src/compiler/checker.ts:2436-2484 — <c>getJsxNamespace</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the pragma read): as <see cref="GetJsxFragmentFactoryEntity"/> documents, the pragma map's values stay
    /// <c>object?</c> because types.ts's <c>PragmaPseudoMap</c> is a mapped type with no C# counterpart, so the
    /// reference's <c>isArray</c> test and <c>[0]</c> index become a test for the repeated-pragma
    /// <c>List&lt;PragmaPseudoMapEntry&gt;</c>, and the entry's <c>arguments.factory</c> is
    /// <c>PragmaPseudoMapEntry.Arguments</c>.
    /// <para>
    /// PORT-NOTE (<c>visitNode(x, markAsSynthetic, test)</c>): visitorPublic.ts's <c>visitNode</c> calls the visitor
    /// only when the node is present (the <c>test</c> feeds a <c>Debug.assertNode</c> and nothing else), so the call is
    /// spelled as the guarded <see cref="MarkAsSynthetic{T}"/> call the reference's own guard already implies. That
    /// walk is visitorPublic's <c>visitEachChild</c> — the port's loud M7 blocker — see the remark on
    /// <see cref="MarkAsSynthetic{T}"/>.
    /// </para>
    /// </remarks>
    internal string GetJsxNamespace(Node? location)
    {
        if (location is not null)
        {
            var file = Utilities.GetSourceFileOfNode(location);
            if (file is not null)
            {
                if (NodeTests.IsJsxOpeningFragment(location))
                {
                    if (file.LocalJsxFragmentNamespace is not null)
                    {
                        return file.LocalJsxFragmentNamespace;
                    }

                    var jsxFragmentPragmas = file.Pragmas?.Get("jsxfrag");
                    var jsxFragmentPragma = jsxFragmentPragmas is List<Parser.PragmaPseudoMapEntry> entries
                        ? (entries.Count > 0 ? entries[0] : null)
                        : jsxFragmentPragmas as Parser.PragmaPseudoMapEntry;
                    if (jsxFragmentPragma is not null)
                    {
                        file.LocalJsxFragmentFactory = Parser.ParseIsolatedEntityName((string?)jsxFragmentPragma.Arguments["factory"] ?? "", _languageVersion);
                        if (file.LocalJsxFragmentFactory is not null)
                        {
                            MarkAsSynthetic(file.LocalJsxFragmentFactory);
                            return file.LocalJsxFragmentNamespace = Utilities.GetFirstIdentifier(file.LocalJsxFragmentFactory).EscapedText!;
                        }
                    }

                    var entity = GetJsxFragmentFactoryEntity(location);
                    if (entity is not null)
                    {
                        file.LocalJsxFragmentFactory = entity;
                        return file.LocalJsxFragmentNamespace = Utilities.GetFirstIdentifier(entity).EscapedText!;
                    }
                }
                else
                {
                    var localJsxNamespace = GetLocalJsxNamespace(file);
                    if (localJsxNamespace is not null)
                    {
                        return file.LocalJsxNamespace = localJsxNamespace;
                    }
                }
            }
        }

        if (_jsxNamespace is null)
        {
            _jsxNamespace = "React";
            if (_compilerOptions.JsxFactory is not null)
            {
                _jsxFactoryEntity = Parser.ParseIsolatedEntityName(_compilerOptions.JsxFactory, _languageVersion);
                if (_jsxFactoryEntity is not null)
                {
                    MarkAsSynthetic(_jsxFactoryEntity);
                    _jsxNamespace = Utilities.GetFirstIdentifier(_jsxFactoryEntity).EscapedText!;
                }
            }
            else if (_compilerOptions.ReactNamespace is not null)
            {
                _jsxNamespace = Utilities.EscapeLeadingUnderscores(_compilerOptions.ReactNamespace);
            }
        }

        if (_jsxFactoryEntity is null)
        {
            _jsxFactoryEntity = _factory.CreateQualifiedName(_factory.CreateIdentifier(Utilities.UnescapeLeadingUnderscores(_jsxNamespace!)), "createElement");
        }

        return _jsxNamespace!;
    }

    /// <summary>ported from: src/compiler/checker.ts:2486-2499 — <c>getLocalJsxNamespace</c>.</summary>
    internal string? GetLocalJsxNamespace(SourceFile file)
    {
        if (file.LocalJsxNamespace is not null)
        {
            return file.LocalJsxNamespace;
        }

        var jsxPragmas = file.Pragmas?.Get("jsx");
        var jsxPragma = jsxPragmas is List<Parser.PragmaPseudoMapEntry> entries
            ? (entries.Count > 0 ? entries[0] : null)
            : jsxPragmas as Parser.PragmaPseudoMapEntry;
        if (jsxPragma is not null)
        {
            file.LocalJsxFactory = Parser.ParseIsolatedEntityName((string?)jsxPragma.Arguments["factory"] ?? "", _languageVersion);
            if (file.LocalJsxFactory is not null)
            {
                MarkAsSynthetic(file.LocalJsxFactory);
                return file.LocalJsxNamespace = Utilities.GetFirstIdentifier(file.LocalJsxFactory).EscapedText!;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:2501-2504 — <c>markAsSynthetic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: loud, named blocker. The reference resets the just-parsed entity name's range and then walks it with
    /// visitorPublic.ts's <c>visitEachChild</c> (aliased as <c>visitEachChildWorker</c>, checker.ts:1134), which the port
    /// holds as the loud M7 blocker <see cref="VisitEachChildWorker"/> — resetting only the root and returning would
    /// leave the name's sub-nodes pointing into the temporary parse, so the blocker is reached instead of degrading
    /// silently. Only the pragma/option paths of <see cref="GetJsxNamespace"/> and <see cref="GetLocalJsxNamespace"/>
    /// can reach it (a file with a <c>jsx</c>/<c>jsxfrag</c> pragma or a <c>jsxFactory</c> option).
    /// </remarks>
    private T MarkAsSynthetic<T>(T node) where T : Node
    {
        Utilities.SetTextRangePosEnd(node, -1, -1);
        return (T)Utilities.VisitEachChildWorker(node, n => MarkAsSynthetic(n), /*context*/ null);
    }

    /// <summary>ported from: src/compiler/checker.ts:3206-3208 — <c>getRequiresScopeChangeCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>declarationRequiresScopeChange</c> is <c>boolean | undefined</c> and the port's slot is the
    /// same <c>bool?</c> (NodeLinks.cs), which the one caller in this file reads through <c>.Value</c> after its own
    /// null test — exactly the reference's test.
    /// </remarks>
    private bool? GetRequiresScopeChangeCache(Node node)
        => GetNodeLinks(node).DeclarationRequiresScopeChange;

    /// <summary>ported from: src/compiler/checker.ts:3209-3211 — <c>setRequiresScopeChangeCache</c>.</summary>
    private void SetRequiresScopeChangeCache(Node node, bool value)
        => GetNodeLinks(node).DeclarationRequiresScopeChange = value;

    /// <summary>ported from: src/compiler/checker.ts:3396-3398 — <c>diagnosticName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>__String | Identifier | PrivateIdentifier</c> union and its body narrows
    /// with <c>isString</c>; the port takes the union's <c>object?</c> upper bound and discriminates on <c>string</c>,
    /// which is the escaped-name half. <c>declarationNameToString</c> takes the <c>DeclarationName</c> face in TS and a
    /// <see cref="Node"/> in the port (utilities). The call sites here feed it either an escaped name or a declaration
    /// name node.
    /// </remarks>
    internal string DiagnosticName(object? nameArg)
        => nameArg is string name ? Utilities.UnescapeLeadingUnderscores(name) : Utilities.DeclarationNameToString((Node)nameArg!);

    /// <summary>ported from: src/compiler/checker.ts:3382-3394 — <c>addTypeOnlyDeclarationRelatedInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeOnlyDeclaration</c> is <c>TypeOnlyCompatibleAliasDeclaration | undefined</c>, a union the
    /// port states as <see cref="Node"/> — every arm reads only <c>kind</c>. <c>addRelatedInfo</c> and
    /// <c>createDiagnosticForNode</c> are <c>Utilities.X</c>, as this file's other diagnostic sites have them. The
    /// reference's <c>typeOnlyDeclaration.kind</c> reads the same <c>kind</c>.
    /// </remarks>
    internal Diagnostic AddTypeOnlyDeclarationRelatedInfo(Diagnostic diagnostic, Node? typeOnlyDeclaration, string unescapedName)
    {
        if (typeOnlyDeclaration is null) return diagnostic;
        return Utilities.AddRelatedInfo(
            diagnostic,
            Utilities.CreateDiagnosticForNode(
                typeOnlyDeclaration,
                typeOnlyDeclaration.Kind == SyntaxKind.ExportSpecifier || typeOnlyDeclaration.Kind == SyntaxKind.ExportDeclaration || typeOnlyDeclaration.Kind == SyntaxKind.NamespaceExport
                    ? Diagnostics._0_was_exported_here
                    : Diagnostics._0_was_imported_here,
                unescapedName));
    }

    /// <summary>ported from: src/compiler/checker.ts:3649-3662 — <c>getAnyImportSyntax</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>AnyImportOrJsDocImport</c> is the <c>ImportEqualsDeclaration | ImportClause | NamespaceImport
    /// | ImportSpecifier | JSDocImportTag</c> union with no common class, so the port answers the upper bound
    /// <see cref="Node"/> — the same reduction the assignment's rules describe for the widened unions. The three
    /// <c>parent</c> climbs are spelled with the non-null assertion TS's flow analysis supplies.
    /// </remarks>
    internal Node? GetAnyImportSyntax(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ImportEqualsDeclaration:
                return node;
            case SyntaxKind.ImportClause:
                return node.Parent;
            case SyntaxKind.NamespaceImport:
                return node.Parent!.Parent;
            case SyntaxKind.ImportSpecifier:
                return node.Parent!.Parent!.Parent;
            default:
                return null;
        }
    }


    /// <summary>ported from: src/compiler/checker.ts:3683-3702 — <c>isAliasSymbolDeclaration</c>.</summary>
    /// <remarks>
    /// <para>An alias symbol is created by one of the following declarations:</para>
    /// <code>
    /// import &lt;symbol&gt; = ...
    /// import &lt;symbol&gt; from ...
    /// import * as &lt;symbol&gt; from ...
    /// import { x as &lt;symbol&gt; } from ...
    /// export { x as &lt;symbol&gt; } from ...
    /// export * as ns &lt;symbol&gt; from ...
    /// export = &lt;EntityNameExpression&gt;
    /// export default &lt;EntityNameExpression&gt;
    /// module.exports = &lt;EntityNameExpression&gt;
    /// {&lt;Identifier&gt;}
    /// {name: &lt;EntityNameExpression&gt;}
    /// const { x } = require ...
    /// </code>
    /// <para>
    /// PORT-NOTE (home): the band's assignment lists this name among the <c>Utilities</c> members ("utilities.ts members
    /// in TS"), but the reference declares it inside <c>createTypeChecker</c> (checker.ts:3683) and its
    /// <c>isFunctionExpression(e) &amp;&amp; isJSConstructor(e)</c> arm needs <c>isJSConstructor</c> (checker.ts:37696),
    /// which the port holds as a <em>Checker instance</em> member (<c>Checker/Checker.Support.cs</c>) because five landed
    /// checker call sites read it unqualified — a static <c>Utilities</c> copy could not call it. The member is therefore
    /// a <c>Checker</c> member, faithful to the reference's class, and the four landed call sites call it unqualified;
    /// <c>Utilities.IsAliasableExpression</c> keeps its own utilities.ts home and is called from
    /// <see cref="IsAliasableOrJsExpression"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>exportAssignmentIsAlias(node)</c> is overloaded over <c>ExportAssignment | BinaryExpression</c>
    /// and the port states the single <c>Node</c>-taking member the binder already calls; the
    /// <c>node.parent.left === node</c> identity test is the reference's own; <c>getAssignmentDeclarationKind</c>,
    /// <c>isAccessExpression</c> and <c>isVariableDeclarationInitializedToBareOrAccessedRequire</c> are the landed
    /// utilities members of those names.
    /// </para>
    /// </remarks>
    internal bool IsAliasSymbolDeclaration(Node node)
    {
        return node.Kind == SyntaxKind.ImportEqualsDeclaration
            || node.Kind == SyntaxKind.NamespaceExportDeclaration
            || (node.Kind == SyntaxKind.ImportClause && ((ImportClause)node).Name is not null)
            || node.Kind == SyntaxKind.NamespaceImport
            || node.Kind == SyntaxKind.NamespaceExport
            || node.Kind == SyntaxKind.ImportSpecifier
            || node.Kind == SyntaxKind.ExportSpecifier
            || (node.Kind == SyntaxKind.ExportAssignment && Utilities.ExportAssignmentIsAlias(node))
            || (NodeTests.IsBinaryExpression(node) && Utilities.GetAssignmentDeclarationKind(node) == AssignmentDeclarationKind.ModuleExports && Utilities.ExportAssignmentIsAlias(node))
            || (Utilities.IsAccessExpression(node)
                && NodeTests.IsBinaryExpression(node.Parent!)
                && ReferenceEquals(((BinaryExpression)node.Parent!).Left, node)
                && ((BinaryExpression)node.Parent!).OperatorToken!.Kind == SyntaxKind.EqualsToken
                && IsAliasableOrJsExpression(((BinaryExpression)node.Parent!).Right!))
            || node.Kind == SyntaxKind.ShorthandPropertyAssignment
            || (node.Kind == SyntaxKind.PropertyAssignment && IsAliasableOrJsExpression(((PropertyAssignment)node).Initializer!))
            || (node.Kind == SyntaxKind.VariableDeclaration && Utilities.IsVariableDeclarationInitializedToBareOrAccessedRequire(node))
            || (node.Kind == SyntaxKind.BindingElement && Utilities.IsVariableDeclarationInitializedToBareOrAccessedRequire(node.Parent!.Parent!));
    }


    private bool IsAliasableOrJsExpression(Expression e)
        => Utilities.IsAliasableExpression(e) || (NodeTests.IsFunctionExpression(e) && IsJSConstructor(e));

    /// <summary>ported from: src/compiler/checker.ts:3664-3666 — <c>getDeclarationOfAliasSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>symbol.declarations &amp;&amp; findLast(…)</c> is the port's <c>Core.FindLast</c> over the nullable
    /// <c>List&lt;IDeclaration&gt;?</c>, and TS's <c>findLast</c> walks from the end, as this overload does. The
    /// predicate is <c>Utilities.IsAliasSymbolDeclaration</c> (the reference declares it inside
    /// <c>createTypeChecker</c> at checker.ts:3683; the port's two landed call sites already qualify it as a
    /// <c>Utilities</c> member, so it stays there — see that member's PORT-NOTE).
    /// </remarks>
    internal IDeclaration? GetDeclarationOfAliasSymbol(ISymbol symbol)
        => Core.FindLast(symbol.Declarations, (d, _) => IsAliasSymbolDeclaration((Node)d));

    /// <summary>ported from: src/compiler/checker.ts:4413-4443 — <c>markSymbolOfAliasDeclarationIfTypeOnly</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeOnlyDeclaration</c> link is <c>TypeOnlyAliasDeclaration | undefined | false</c> — three
    /// states the port keeps in the <c>object?</c> slot <c>SymbolLinks.TypeOnlyDeclaration</c> (undefined is null, the
    /// negative cache is the boxed <c>false</c>, a hit is the declaration). The declaration face of the argument is
    /// <see cref="IDeclaration"/> and the export-star declaration is the reference's
    /// <c>ExportDeclaration &amp; { isTypeOnly: true }</c>, which the port states as
    /// <see cref="ExportDeclaration"/> (the intersection has no C# counterpart and no arm reads those members).
    /// </remarks>
    internal bool MarkSymbolOfAliasDeclarationIfTypeOnly(
        IDeclaration? aliasDeclaration,
        ISymbol? immediateTarget,
        ISymbol? finalTarget,
        bool overwriteEmpty,
        ExportDeclaration? exportStarDeclaration = null,
        string? exportStarName = null)
    {
        if (aliasDeclaration is null || NodeTests.IsPropertyAccessExpression((Node)aliasDeclaration)) return false;

        // If the declaration itself is type-only, mark it and return.
        // No need to check what it resolves to.
        var sourceSymbol = GetSymbolOfDeclaration(aliasDeclaration);
        if (Utilities.IsTypeOnlyImportOrExportDeclaration((Node)aliasDeclaration))
        {
            var links = GetSymbolLinks(sourceSymbol);
            links.TypeOnlyDeclaration = aliasDeclaration;
            return true;
        }

        if (exportStarDeclaration is not null)
        {
            var links = GetSymbolLinks(sourceSymbol);
            links.TypeOnlyDeclaration = exportStarDeclaration;
            if (sourceSymbol.EscapedName != exportStarName)
            {
                links.TypeOnlyExportStarName = exportStarName;
            }

            return true;
        }

        var sourceLinks = GetSymbolLinks(sourceSymbol);
        return MarkSymbolOfAliasDeclarationIfTypeOnlyWorker(sourceLinks, immediateTarget, overwriteEmpty)
            || MarkSymbolOfAliasDeclarationIfTypeOnlyWorker(sourceLinks, finalTarget, overwriteEmpty);
    }

    /// <summary>ported from: src/compiler/checker.ts:4445-4452 — <c>markSymbolOfAliasDeclarationIfTypeOnlyWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>aliasDeclarationLinks.typeOnlyDeclaration === undefined</c> and
    /// <c>=== false</c> are the port's <c>is null</c> and <c>is false</c> patterns over the <c>object?</c> slot; the
    /// final <c>!!…</c> is <c>Js.IsTruthy</c>, which answers false for the boxed <c>false</c> and true for a
    /// declaration. <c>target.exports?.get(InternalSymbolName.ExportEquals) ?? target</c> is the port's
    /// <c>GetValueOrDefault</c> lookup, and the <c>find(…, isTypeOnlyImportOrExportDeclaration)</c> is the two-argument
    /// <c>Core.Find</c>.
    /// </remarks>
    private bool MarkSymbolOfAliasDeclarationIfTypeOnlyWorker(ISymbolLinks aliasDeclarationLinks, ISymbol? target, bool overwriteEmpty)
    {
        if (target is not null && (aliasDeclarationLinks.TypeOnlyDeclaration is null || (overwriteEmpty && aliasDeclarationLinks.TypeOnlyDeclaration is false)))
        {
            var exportSymbol = target.Exports?.GetValueOrDefault(InternalSymbolName.ExportEquals.Value) ?? target;
            var typeOnly = exportSymbol.Declarations is not null
                ? Core.Find(exportSymbol.Declarations, (d, _) => Utilities.IsTypeOnlyImportOrExportDeclaration((Node)d))
                : null;
            aliasDeclarationLinks.TypeOnlyDeclaration = (object?)typeOnly ?? GetSymbolLinks(exportSymbol).TypeOnlyDeclaration ?? false;
        }

        return Js.IsTruthy(aliasDeclarationLinks.TypeOnlyDeclaration);
    }

    /// <summary>ported from: src/compiler/checker.ts:4455-4477 — <c>getTypeOnlyAliasDeclaration</c>.</summary>
    /// <remarks>
    /// Indicates that a symbol directly or indirectly resolves to a type-only import or export.
    /// <para>
    /// PORT-NOTE: TS's three-state <c>typeOnlyDeclaration</c> slot is read through the same <c>is null</c>/<c>is
    /// false</c> patterns as the worker above, and the member answers <see cref="Node"/> (TS's
    /// <c>TypeOnlyAliasDeclaration</c>, whose arms read <c>kind</c>/<c>symbol</c> through the declaration face) —
    /// which is also what <c>ITypeChecker.GetTypeOnlyAliasDeclaration</c> and the four landed callers hold.
    /// <c>getExportsOfModule</c> here is the internal <c>SymbolTable</c>-returning one, i.e.
    /// <see cref="GetExportsOfModuleTable"/> (the public <c>Symbol[]</c>-returning name is owned by the interface —
    /// see that member's PORT-NOTE).
    /// </para>
    /// </remarks>
    internal Node? GetTypeOnlyAliasDeclaration(ISymbol? symbol, SymbolFlags? include = null)
    {
        if (symbol is null || (symbol.Flags & SymbolFlags.Alias) == 0)
        {
            return null;
        }

        var links = GetSymbolLinks(symbol);
        if (links.TypeOnlyDeclaration is null)
        {
            // We need to set a WIP value here to prevent reentrancy during `getImmediateAliasedSymbol` which, paradoxically, can depend on this
            links.TypeOnlyDeclaration = false;
            var resolved = ResolveSymbol(symbol); // do this before the `resolveImmediate` below, as it uses a different circularity cache and we might hide a circularity error if we blindly get the immediate alias first
            // While usually the alias will have been marked during the pass by the full typecheck, we may still need to calculate the alias declaration now
            MarkSymbolOfAliasDeclarationIfTypeOnly(
                symbol.Declarations is { Count: > 0 } declarations ? declarations[0] : null,
                GetDeclarationOfAliasSymbol(symbol) is not null ? GetImmediateAliasedSymbol(symbol) : null,
                resolved,
                /*overwriteEmpty*/ true);
        }

        if (include is null)
        {
            return links.TypeOnlyDeclaration is false or null ? null : (Node)links.TypeOnlyDeclaration;
        }

        if (Js.IsTruthy(links.TypeOnlyDeclaration))
        {
            var typeOnlyDeclaration = (Node)links.TypeOnlyDeclaration;
            var resolved = typeOnlyDeclaration.Kind == SyntaxKind.ExportDeclaration
                ? ResolveSymbol(GetExportsOfModuleTable(((IDeclaration)typeOnlyDeclaration).Symbol!.Parent!).GetValueOrDefault(links.TypeOnlyExportStarName ?? symbol.EscapedName!)!)
                : ResolveAlias(((IDeclaration)typeOnlyDeclaration).Symbol!);
            return (GetSymbolFlags(resolved!) & include.Value) != 0 ? typeOnlyDeclaration : null;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4480-4500 — <c>getSymbolOfPartOfRightHandSideOfImportEquals</c>.</summary>
    /// <remarks>
    /// This function is only for imports with entity names.
    /// <para>
    /// PORT-NOTE: TS's <c>EntityName</c> parameter (<c>Identifier | QualifiedName</c>) is the port's
    /// <see cref="Node"/>, narrowed by the reference's own <c>kind</c> tests; <c>entityName.parent as QualifiedName</c>
    /// is the same parent read through the node face. <c>resolveEntityName</c> is this file's landed member, whose
    /// fourth parameter is the same <c>dontResolveAlias</c>.
    /// </para>
    /// </remarks>
    internal ISymbol? GetSymbolOfPartOfRightHandSideOfImportEquals(Node entityName, bool? dontResolveAlias = null)
    {
        // There are three things we might try to look for. In the following examples,
        // the search term is enclosed in |...|:
        //
        //     import a = |b|; // Namespace
        //     import a = |b.c|; // Value, type, namespace
        //     import a = |b.c|.d; // Namespace
        if (entityName.Kind == SyntaxKind.Identifier && Utilities.IsRightSideOfQualifiedNameOrPropertyAccess(entityName))
        {
            entityName = entityName.Parent!;
        }

        // Check for case 1 and 3 in the above example
        if (entityName.Kind == SyntaxKind.Identifier || entityName.Parent!.Kind == SyntaxKind.QualifiedName)
        {
            return ResolveEntityName(entityName, SymbolFlags.Namespace, /*ignoreErrors*/ false, dontResolveAlias);
        }

        // Case 2 in above example
        // entityName.kind could be a QualifiedName or a Missing identifier
        Debug.Assert(entityName.Parent!.Kind == SyntaxKind.ImportEqualsDeclaration, "case 2 in the resolution table is a qualified name whose parent is an import-equals declaration");
        return ResolveEntityName(entityName, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, /*ignoreErrors*/ false, dontResolveAlias);
    }

    /// <summary>ported from: src/compiler/checker.ts:4506-4511 — <c>getContainingQualifiedNameNode</c>.</summary>
    internal QualifiedName GetContainingQualifiedNameNode(QualifiedName node)
    {
        while (NodeTests.IsQualifiedName(node.Parent!))
        {
            node = (QualifiedName)node.Parent!;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/checker.ts:4513-4528 — <c>tryGetQualifiedNameAsValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>left: Identifier | QualifiedName</c> starts at <c>getFirstIdentifier(node)</c> — always an
    /// identifier — and the first <c>resolveName(left, left, …)</c> therefore passes the identifier's escaped text,
    /// which is the port's string-taking <c>ResolveName</c> (this file's head PORT-NOTE states that reduction); the
    /// reference's <c>isUse</c> is true and <c>excludeGlobals</c> is the default false.
    /// </remarks>
    internal ISymbol? TryGetQualifiedNameAsValue(QualifiedName node)
    {
        Node left = Utilities.GetFirstIdentifier(node);
        var symbol = ResolveName(left, ((Identifier)left).EscapedText!, SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
        if (symbol is null)
        {
            return null;
        }

        while (NodeTests.IsQualifiedName(left.Parent!))
        {
            var type = GetTypeOfSymbol(symbol);
            symbol = GetPropertyOfType(type, ((QualifiedName)left.Parent!).Right!.EscapedText);
            if (symbol is null)
            {
                return null;
            }

            left = left.Parent!;
        }

        return symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:4996-5021 — <c>getCommonJsExportEquals</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>cloneSymbol</c>/<c>createSymbolTable</c> are the port's <c>CloneSymbol</c> (which answers the
    /// transient class) and <c>Utilities.CreateSymbolTable</c>; the table union <c>merged.exports.has/get/set</c> is the
    /// port's <c>ContainsKey</c>/indexer/<c>GetValueOrDefault</c> trio, and the two link resets are the
    /// <c>ResolvedExports</c>/<c>ResolvedMembers</c> slots. <c>InternalSymbolName.ExportEquals</c> is the port's
    /// <c>.Value</c> string constant.
    /// </remarks>
    internal ISymbol? GetCommonJsExportEquals(ISymbol? exported, ISymbol moduleSymbol)
    {
        if (exported is null || ReferenceEquals(exported, _unknownSymbol) || ReferenceEquals(exported, moduleSymbol) || moduleSymbol.Exports!.Count == 1 || (exported.Flags & SymbolFlags.Alias) != 0)
        {
            return exported;
        }

        var links = GetSymbolLinks(exported);
        if (links.CjsExportMerged is not null)
        {
            return links.CjsExportMerged;
        }

        ISymbol merged = (exported.Flags & SymbolFlags.Transient) != 0 ? exported : CloneSymbol(exported);
        merged.Flags = merged.Flags | SymbolFlags.ValueModule;
        merged.Exports ??= Utilities.CreateSymbolTable();
        moduleSymbol.Exports!.ForEach((s, name) =>
        {
            if (name == InternalSymbolName.ExportEquals.Value) return;
            merged.Exports![name] = merged.Exports!.ContainsKey(name) ? MergeSymbol(merged.Exports![name], s) : s;
        });
        if (ReferenceEquals(merged, exported))
        {
            // We just mutated a symbol, reset any cached links we may have already set
            // (Notably required to make late bound members appear)
            GetSymbolLinks(merged).ResolvedExports = null;
            GetSymbolLinks(merged).ResolvedMembers = null;
        }

        GetSymbolLinks(merged).CjsExportMerged = merged;
        return links.CjsExportMerged = merged;
    }

    /// <summary>ported from: src/compiler/checker.ts:3770-3772 — <c>getEmitSyntaxForModuleSpecifierExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the host member's <c>object?</c> face (the generated <c>ITypeCheckerHost</c> cannot state
    /// <c>ResolutionMode</c>, whose <c>ModuleKind | undefined</c> it holds as <c>ModuleKind?</c>) is read back with
    /// <c>as ModuleKind?</c>; TS's <c>isStringLiteralLike</c> narrowing is the port's
    /// <c>Utilities.IsStringLiteralLike</c> plus the <see cref="LiteralExpression"/> cast the reference's own
    /// <c>usage</c> parameter already implies.
    /// </remarks>
    internal ModuleKind? GetEmitSyntaxForModuleSpecifierExpression(Expression usage)
        => Utilities.IsStringLiteralLike(usage)
            ? _host.GetEmitSyntaxForUsageLocation(Utilities.GetSourceFileOfNode(usage), (LiteralExpression)usage) as ModuleKind?
            : null;

    /// <summary>ported from: src/compiler/checker.ts:3763-3768 — <c>isSyntacticDefault</c>.</summary>
    private static bool IsSyntacticDefault(Node node)
        => (NodeTests.IsExportAssignment(node) && !Js.IsTruthy(((ExportAssignment)node).IsExportEquals))
            || Utilities.HasSyntacticModifier(node, ModifierFlags.Default)
            || NodeTests.IsExportSpecifier(node)
            || NodeTests.IsNamespaceExport(node);

    /// <summary>ported from: src/compiler/checker.ts:3753-3761 — <c>resolveExportByName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>moduleSymbol.exports!.get(…)</c> pair is the port's <c>GetValueOrDefault</c> /
    /// <c>GetPropertyOfType</c> (whose third parameter is the reference's
    /// <c>skipObjectFunctionPropertyAugment</c>). <c>sourceNode</c> is TS's
    /// <c>TypeOnlyCompatibleAliasDeclaration | undefined</c>, which the port states as the declaration face.
    /// </remarks>
    private ISymbol? ResolveExportByName(ISymbol moduleSymbol, string name, IDeclaration? sourceNode, bool dontResolveAlias)
    {
        var exportValue = moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolName.ExportEquals.Value);
        var exportSymbol = exportValue is not null
            ? GetPropertyOfType(GetTypeOfSymbol(exportValue), name, /*skipObjectFunctionPropertyAugment*/ true)
            : moduleSymbol.Exports?.GetValueOrDefault(name);
        var resolved = ResolveSymbol(exportSymbol, dontResolveAlias);
        MarkSymbolOfAliasDeclarationIfTypeOnly(sourceNode, exportSymbol, resolved, /*overwriteEmpty*/ false);
        return resolved;
    }

    /// <summary>ported from: src/compiler/checker.ts:3774-3776 — <c>isESMFormatImportImportingCommonjsFormatFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ResolutionMode</c> is <c>ModuleKind | undefined</c> and the port states it as
    /// <c>ModuleKind?</c>, so the reference's strict equality is the port's <c>==</c> on the nullable enum.
    /// </remarks>
    private static bool IsESMFormatImportImportingCommonjsFormatFile(ModuleKind? usageMode, ModuleKind? targetMode)
        => usageMode == ModuleKind.ESNext && targetMode == ModuleKind.CommonJS;

    /// <summary>ported from: src/compiler/checker.ts:3778-3789 — <c>isOnlyImportableAsDefault</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>targetFile &amp;&amp; (…)</c> answers the source file (or <c>undefined</c>) where its
    /// callers only test truthiness — both landed call sites feed it into a <c>&amp;&amp;</c> chain — so the port states
    /// the test as <c>bool</c> over the same guard. <c>resolvedModule ??= resolveExternalModuleName(...)</c> is the same
    /// member with the reference's <c>ignoreErrors</c> true.
    /// </remarks>
    internal bool IsOnlyImportableAsDefault(Expression usage, ISymbol? resolvedModule = null)
    {
        // In Node.js, JSON modules don't get named exports
        if (ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
        {
            var usageMode = GetEmitSyntaxForModuleSpecifierExpression(usage);
            if (usageMode == ModuleKind.ESNext)
            {
                resolvedModule ??= ResolveExternalModuleName(usage, usage, /*ignoreErrors*/ true);
                var targetFile = resolvedModule is not null ? Utilities.GetSourceFileOfModule(resolvedModule) : null;
                return targetFile is not null
                    && (Utilities.IsJsonSourceFile(targetFile) || Paths.GetDeclarationFileExtension(targetFile.FileName!) == ".d.json.ts");
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3791-3848 — <c>canHaveSyntheticDefault</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>file &amp;&amp; getEmitSyntaxForModuleSpecifierExpression(usage)</c> is the port's explicit null test
    /// plus the <c>ModuleKind?</c> read of that member, and <c>usageMode !== undefined</c> is <c>is not null</c> — a
    /// <c>ModuleKind.None</c> mode still passes it, exactly as the reference's <c>!== undefined</c> does. The host's
    /// implied/format members are read through the same <c>ModuleKind</c>/<c>as ModuleKind?</c> faces
    /// <see cref="GetEmitSyntaxForModuleSpecifierExpression"/> documents, <c>host.getRedirectFromSourceFile(…) ||
    /// host.getRedirectFromOutput(…)</c> is the port's <c>??</c> (both answer a redirect object or null), and
    /// <c>require('__esModule')</c> is <c>Utilities.EscapeLeadingUnderscores("__esModule")</c>. The reference's
    /// <c>typeof file.externalModuleIndicator !== "object"</c> — true for <c>undefined</c> and for the boolean
    /// <c>true</c> marker, false for the node form — is <c>is not Node</c> over the port's <c>object?</c> slot.
    /// <c>some(defaultExportSymbol.declarations, isSyntacticDefault)</c> is the two-argument <c>Core.Some</c>.
    /// </remarks>
    internal bool CanHaveSyntheticDefault(SourceFile? file, ISymbol moduleSymbol, bool dontResolveAlias, Expression usage)
    {
        var usageMode = file is not null ? GetEmitSyntaxForModuleSpecifierExpression(usage) : null;
        if (file is not null && usageMode is not null)
        {
            var targetMode = _host.GetImpliedNodeFormatForEmit(file) as ModuleKind?;
            if (usageMode == ModuleKind.ESNext && targetMode == ModuleKind.CommonJS && ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
            {
                // In Node.js, CommonJS modules always have a synthetic default when imported into ESM
                return true;
            }

            if (usageMode == ModuleKind.ESNext && targetMode == ModuleKind.ESNext)
            {
                // No matter what the `module` setting is, if we're confident that both files
                // are ESM, there cannot be a synthetic default.
                return false;
            }

            // For other files (not node16/nodenext with impliedNodeFormat), check if we can determine
            // the module format from project references
            if (targetMode is null && file.IsDeclarationFile)
            {
                // Try to get the project reference - try both source file mapping and output file mapping
                // since declaration files can be mapped either way depending on how they're resolved
                // PORT-NOTE: the reference binds the redirect and only ever tests it for presence here (the value is
                // read nowhere in this branch), and the port's two host members answer different redirect faces, so the
                // test is stated over the two lookups instead of a common local.
                var hasRedirect = ((IModuleSpecifierResolutionHost)_host).GetRedirectFromSourceFile(file.Path) is not null
                    || _host.GetRedirectFromOutput(file.Path) is not null;
                if (hasRedirect)
                {
                    // This is a declaration file from a project reference, so we can determine
                    // its module format from the referenced project's options
                    var targetModuleKind = _host.GetEmitModuleFormatOfFile(file);
                    if (usageMode == ModuleKind.ESNext && ModuleKind.ES2015 <= targetModuleKind && targetModuleKind <= ModuleKind.ESNext)
                    {
                        return false;
                    }
                }
            }
        }

        if (!_allowSyntheticDefaultImports)
        {
            return false;
        }

        // Declaration files (and ambient modules)
        if (file is null || file.IsDeclarationFile)
        {
            // Definitely cannot have a synthetic default if they have a syntactic default member specified
            var defaultExportSymbol = ResolveExportByName(moduleSymbol, InternalSymbolName.Default.Value, /*sourceNode*/ null, /*dontResolveAlias*/ true); // Dont resolve alias because we want the immediately exported symbol's declaration
            if (defaultExportSymbol is not null && Core.Some(defaultExportSymbol.Declarations, d => IsSyntacticDefault((Node)d)))
            {
                return false;
            }

            // It _might_ still be incorrect to assume there is no __esModule marker on the import at runtime, even if there is no `default` member
            // So we check a bit more,
            if (ResolveExportByName(moduleSymbol, Utilities.EscapeLeadingUnderscores("__esModule"), /*sourceNode*/ null, dontResolveAlias) is not null)
            {
                // If there is an `__esModule` specified in the declaration (meaning someone explicitly added it or wrote it in their code),
                // it definitely is a module and does not have a synthetic default
                return false;
            }

            // There are _many_ declaration files not written with esmodules in mind that still get compiled into a format with __esModule set
            // Meaning there may be no default at runtime - however to be on the permissive side, we allow access to a synthetic default member
            // as there is no marker to indicate if the accompanying JS has `__esModule` or not, or is even native esm
            return true;
        }

        // TypeScript files never have a synthetic default (as they are always emitted with an __esModule marker) _unless_ they contain an export= statement
        if (!Utilities.IsSourceFileJS(file))
        {
            return HasExportAssignmentSymbol(moduleSymbol);
        }

        // JS files have a synthetic default if they do not contain ES2015+ module syntax (export = is not valid in js) _and_ do not have an __esModule marker
        return file.ExternalModuleIndicator is not Node && ResolveExportByName(moduleSymbol, Utilities.EscapeLeadingUnderscores("__esModule"), /*sourceNode*/ null, dontResolveAlias) is null;
    }

    /// <summary>ported from: src/compiler/checker.ts:5108-5110 — <c>hasExportAssignmentSymbol</c>.</summary>
    internal bool HasExportAssignmentSymbol(ISymbol moduleSymbol)
        => moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolName.ExportEquals.Value) is not null;

    /// <summary>ported from: src/compiler/checker.ts:5112-5114 — <c>getExportsOfModuleAsArray</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getExportsOfModule</c> here is the internal <c>SymbolTable</c>-returning one, i.e.
    /// <see cref="GetExportsOfModuleTable"/> (see that member's PORT-NOTE for the name split); the nullable
    /// <c>moduleSymbol</c> is the one <c>ITypeChecker.GetExportsOfModule</c> passes through.
    /// </remarks>
    internal List<ISymbol> GetExportsOfModuleAsArray(ISymbol? moduleSymbol)
        => SymbolsToArray(GetExportsOfModuleTable(moduleSymbol!));

    /// <summary>ported from: src/compiler/checker.ts:5116-5126 — <c>getExportsAndPropertiesOfModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>exportEquals !== moduleSymbol</c> is the reference's own identity test and
    /// <c>addRange</c> is <c>Core.AddRange</c> over the mutable list the first call returns.
    /// </remarks>
    internal List<ISymbol> GetExportsAndPropertiesOfModule(ISymbol? moduleSymbol)
    {
        var exports = GetExportsOfModuleAsArray(moduleSymbol);
        var exportEquals = ResolveExternalModuleSymbol(moduleSymbol);
        if (!ReferenceEquals(exportEquals, moduleSymbol))
        {
            var type = GetTypeOfSymbol(exportEquals);
            if (ShouldTreatPropertiesOfExternalModuleAsExports(type))
            {
                Core.AddRange(exports, GetPropertiesOfType(type));
            }
        }

        return exports;
    }

    /// <summary>ported from: src/compiler/checker.ts:5128-5144 — <c>forEachExportAndPropertyOfModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>exports.forEach((symbol, key) =&gt; …)</c> is the port's <c>SymbolTable.ForEach</c> (insertion
    /// order) and <c>forEachPropertyOfType</c> is this file's <see cref="ForEachPropertyOfType"/>.
    /// </remarks>
    internal void ForEachExportAndPropertyOfModule(ISymbol? moduleSymbol, Action<ISymbol, string>? cb)
    {
        var exports = GetExportsOfModuleTable(moduleSymbol!);
        exports.ForEach((symbol, key) =>
        {
            if (!IsReservedMemberName(key))
            {
                cb!(symbol, key);
            }
        });
        var exportEquals = ResolveExternalModuleSymbol(moduleSymbol);
        if (!ReferenceEquals(exportEquals, moduleSymbol))
        {
            var type = GetTypeOfSymbol(exportEquals);
            if (ShouldTreatPropertiesOfExternalModuleAsExports(type))
            {
                ForEachPropertyOfType(type, (symbol, escapedName) => cb!(symbol, escapedName));
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:5146-5151 — <c>tryGetMemberInModuleExports</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>symbolTable.get(memberName)</c> is the port's <c>TryGetValue</c> (the key may be absent even though
    /// TS types the table as present), the same treatment this file's other table lookups take.
    /// </remarks>
    internal ISymbol? TryGetMemberInModuleExports(string memberName, ISymbol? moduleSymbol)
    {
        var symbolTable = GetExportsOfModuleTable(moduleSymbol!);
        return symbolTable.TryGetValue(memberName, out var symbol) ? symbol : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:5153-5166 — <c>tryGetMemberInModuleExportsAndProperties</c>.</summary>
    internal ISymbol? TryGetMemberInModuleExportsAndProperties(string memberName, ISymbol? moduleSymbol)
    {
        var symbol = TryGetMemberInModuleExports(memberName, moduleSymbol);
        if (symbol is not null)
        {
            return symbol;
        }

        var exportEquals = ResolveExternalModuleSymbol(moduleSymbol);
        if (ReferenceEquals(exportEquals, moduleSymbol))
        {
            return null;
        }

        var type = GetTypeOfSymbol(exportEquals);
        return ShouldTreatPropertiesOfExternalModuleAsExports(type) ? GetPropertyOfType(type, memberName) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:5168-5174 — <c>shouldTreatPropertiesOfExternalModuleAsExports</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>flags &amp; …</c> and <c>getObjectFlags(t) &amp; ObjectFlags.Class</c> are the port's
    /// non-zero flag tests, and <c>isArrayType</c>/<c>isTupleType</c> are the landed members of their names.
    /// </remarks>
    internal bool ShouldTreatPropertiesOfExternalModuleAsExports(IType resolvedExternalModuleType)
        => !((resolvedExternalModuleType.Flags & TypeFlags.Primitive) != 0
            || (GetObjectFlags(resolvedExternalModuleType) & ObjectFlags.Class) != 0
            // `isArrayOrTupleLikeType` is too expensive to use in this auto-imports hot path
            || IsArrayType(resolvedExternalModuleType)
            || IsTupleType(resolvedExternalModuleType));

    /// <summary>ported from: src/compiler/checker.ts:15108-15117 — <c>forEachPropertyOfType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (cross-band): this member sits outside the symbol band (checker.ts:15108) but the module-export walk
    /// above is its only landed caller, and no other order file claims it, so it is declared here rather than left
    /// dangling. <c>resolveStructuredTypeMembers(type as StructuredType).members.forEach</c> is the port's
    /// <c>ResolveStructuredTypeMembers</c> over the port's structured-type class <see cref="UnionOrIntersectionType"/>
    /// (<c>ObjectType</c> derives from it — <c>Model/ObjectType.cs:38</c> — which is how TS's
    /// <c>StructuredType = ObjectType | UnionOrIntersectionType</c> is modelled) plus its ordered table's <c>ForEach</c>.
    /// </remarks>
    internal void ForEachPropertyOfType(IType type, Action<ISymbol, string> action)
    {
        type = GetReducedApparentType(type);
        if ((type.Flags & TypeFlags.StructuredType) != 0)
        {
            ResolveStructuredTypeMembers((UnionOrIntersectionType)type).Members!.ForEach((symbol, escapedName) =>
            {
                if (IsNamedMember(symbol, escapedName))
                {
                    action(symbol, escapedName);
                }
            });
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2134-2139 — <c>reportUnmeasurableMapper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the mapper as a closure <c>var</c> built in the checker's initialization block; the port
    /// keeps the built mapper in <c>_reportUnmeasurableMapper</c> (Checker.Types.cs, where the block runs) and exposes
    /// it here as the property the two landed call sites in <c>Checker.Relations.Identical.cs</c> already read. The
    /// sibling <c>restrictiveMapper</c>/<c>permissiveMapper</c> pair is surfaced the same way from
    /// <c>Checker.Instantiation.cs</c>.
    /// </remarks>
    internal TypeMapper ReportUnmeasurableMapper => _reportUnmeasurableMapper;

    /// <summary>ported from: src/compiler/checker.ts:4724-4728 — <c>resolveExternalModuleNameWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the landed caller (<see cref="ResolveExternalModuleName"/>) holds the module reference as
    /// <c>Expression?</c>, so the parameter follows it and the reference's <c>isStringLiteralLike</c> test gains the same
    /// null guard; <c>moduleReferenceExpression.text</c> is the <see cref="LiteralExpression"/> face's <c>Text</c>, the
    /// read <c>Checker.Check.Modules.cs</c>'s literal-like site uses too. The reference's
    /// <c>!ignoreErrors ? moduleReferenceExpression : undefined</c> is the port's conditional with <c>null</c>.
    /// </remarks>
    internal ISymbol? ResolveExternalModuleNameWorker(Node location, Expression? moduleReferenceExpression, DiagnosticMessage? moduleNotFoundError, bool ignoreErrors = false, bool isForAugmentation = false)
        => moduleReferenceExpression is not null && Utilities.IsStringLiteralLike(moduleReferenceExpression)
            ? ResolveExternalModule(location, ((LiteralExpression)moduleReferenceExpression).Text!, moduleNotFoundError, !ignoreErrors ? moduleReferenceExpression : null, isForAugmentation)
            : null;

    // ---------------------------------------------------------------------------------------------
    // The name resolver's diagnostic callbacks and their helper chain (checker.ts:3206-3647)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE (suggestionCount, checker.ts:2349-2355): the running counter and its cap are the checker's
    // `_suggestionCount` / `_maximumSuggestionCount` slots (Checker/Checker.Initialize.cs), so this file does not declare
    // a second copy; OnFailedToResolveSymbol below reads and increments those.

    /// <summary>ported from: src/compiler/checker.ts:3215-3234 — <c>checkAndReportErrorForInvalidInitializer</c>.</summary>
    /// <remarks>
    /// The invalid initializer error is needed in two situations: when the result is undefined, after checking for a
    /// missing "this."; and when the result is defined.
    /// <para>
    /// PORT-NOTE: <c>emitStandardClassFields</c> is <c>_emitStandardClassFields</c>, <c>textRangeContainsPositionInclusive</c>
    /// is the utilities-public member the Utilities partial holds, and the reference's
    /// <c>errorLocation &amp;&amp; propertyWithInvalidInitializer.type &amp;&amp; …</c> is the port's explicit null chain over
    /// the same two slots.
    /// </para>
    /// </remarks>
    private bool CheckAndReportErrorForInvalidInitializer(Node? errorLocation, string name, PropertyDeclaration propertyWithInvalidInitializer, ISymbol? result)
    {
        if (!_emitStandardClassFields)
        {
            if (errorLocation is not null && result is null && CheckAndReportErrorForMissingPrefix(errorLocation, name, name))
            {
                return true;
            }

            // We have a match, but the reference occurred within a property initializer and the identifier also binds
            // to a local variable in the constructor where the code will be emitted. Note that this is actually allowed
            // with emitStandardClassFields because the scope semantics are different.
            Error(
                errorLocation,
                errorLocation is not null && propertyWithInvalidInitializer.Type is not null && Utilities.TextRangeContainsPositionInclusive((Node)propertyWithInvalidInitializer.Type!, errorLocation.Pos)
                    ? Diagnostics.Type_of_instance_member_variable_0_cannot_reference_identifier_1_declared_in_the_constructor
                    : Diagnostics.Initializer_of_instance_member_variable_0_cannot_reference_identifier_1_declared_in_the_constructor,
                Utilities.DeclarationNameToString(propertyWithInvalidInitializer.Name!),
                DiagnosticName(name));
            return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3235-3296 — <c>onFailedToResolveSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>nameArg: __String | Identifier</c> is the ported escaped <c>string</c> (the head PORT-NOTE of
    /// this file states that reduction for <c>ResolveName</c>, whose option callback this is), so
    /// <c>const name = isString(nameArg) ? nameArg : nameArg.escapedText</c> is the parameter itself and the reference's
    /// <c>nameArg &amp;&amp; typeof nameArg !== "string" &amp;&amp; nodeIsSynthesized(nameArg)</c> disjunct can never hold — it is
    /// stated as its constant value with the same message choice. <c>errorLocation!.parent.kind</c> is the non-null
    /// parent read the reference's own <c>!</c> asserts, <c>getSuggestedSymbolForNonexistentSymbol</c>/
    /// <c>isUncheckedJSSuggestion</c> are the inference band's members (called by reference), <c>createError</c> is
    /// <c>CreateError</c>, <c>addErrorOrSuggestion</c> is <c>AddErrorOrSuggestion</c>, and the canonical head is
    /// <c>Utilities.GetCanonicalDiagnostic</c>.
    /// </remarks>
    private void OnFailedToResolveSymbol(
        Node? errorLocation,
        string nameArg,
        SymbolFlags meaning,
        DiagnosticMessage nameNotFoundMessage)
    {
        var name = nameArg;
        AddLazyDiagnostic(() =>
        {
            if (
                errorLocation is null
                || (errorLocation.Parent!.Kind != SyntaxKind.JSDocLink
                    && !CheckAndReportErrorForMissingPrefix(errorLocation, name, nameArg)
                    && !CheckAndReportErrorForExtendingInterface(errorLocation)
                    && !CheckAndReportErrorForUsingTypeAsNamespace(errorLocation, name, meaning)
                    && !CheckAndReportErrorForExportingPrimitiveType(errorLocation, name)
                    && !CheckAndReportErrorForUsingNamespaceAsTypeOrValue(errorLocation, name, meaning)
                    && !CheckAndReportErrorForUsingTypeAsValue(errorLocation, name, meaning)
                    && !CheckAndReportErrorForUsingValueAsType(errorLocation, name, meaning)))
            {
                ISymbol? suggestion = null;
                string? suggestedLib = null;
                // Report missing lib first
                if (nameArg.Length > 0)
                {
                    suggestedLib = GetSuggestedLibForNonExistentName(nameArg);
                    if (suggestedLib is not null)
                    {
                        Error(errorLocation, nameNotFoundMessage, DiagnosticName(nameArg), suggestedLib);
                    }
                }

                // then spelling suggestions
                if (suggestedLib is null && _suggestionCount < _maximumSuggestionCount)
                {
                    suggestion = GetSuggestedSymbolForNonexistentSymbol(errorLocation, name, meaning);
                    // PORT-NOTE: the reference's `isAmbientModule` (utilities.ts:2078) is the kind test
                    // `isModuleDeclaration(node) && …`, so its true arm is what lets the second read
                    // (`isGlobalScopeAugmentation`) take a ModuleDeclaration. The port states that narrowing as the
                    // pattern rather than casting the class/enum declaration a suggestion can also name.
                    var isGlobalScopeAugmentationDeclaration = suggestion?.ValueDeclaration is ModuleDeclaration suggestionDeclaration
                        && Utilities.IsAmbientModule(suggestionDeclaration)
                        && Utilities.IsGlobalScopeAugmentation(suggestionDeclaration);
                    if (isGlobalScopeAugmentationDeclaration)
                    {
                        suggestion = null;
                    }

                    if (suggestion is not null)
                    {
                        var suggestionName = Diagnostic.DeferArg(() => SymbolToString(suggestion));
                        var isUncheckedJS = IsUncheckedJSSuggestion(errorLocation, suggestion, /*excludeClasses*/ false);
                        var message = meaning == SymbolFlags.Namespace
                            ? Diagnostics.Cannot_find_namespace_0_Did_you_mean_1
                            : isUncheckedJS ? Diagnostics.Could_not_find_name_0_Did_you_mean_1
                            : Diagnostics.Cannot_find_name_0_Did_you_mean_1;
                        var diagnostic = CreateError(errorLocation, message, DiagnosticName(nameArg), suggestionName);
                        diagnostic.CanonicalHead = Utilities.GetCanonicalDiagnostic(nameNotFoundMessage, DiagnosticName(nameArg));
                        AddErrorOrSuggestion(!isUncheckedJS, diagnostic);
                        if (suggestion.ValueDeclaration is not null)
                        {
                            Utilities.AddRelatedInfo(
                                diagnostic,
                                Utilities.CreateDiagnosticForNode((Node)suggestion.ValueDeclaration, Diagnostics._0_is_declared_here, suggestionName));
                        }
                    }
                }

                // And then fall back to unspecified "not found"
                if (suggestion is null && suggestedLib is null && nameArg.Length > 0)
                {
                    Error(errorLocation, nameNotFoundMessage, DiagnosticName(nameArg));
                }

                _suggestionCount++;
            }
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:3298-3380 — <c>onSuccessfullyResolvedSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>result.escapedName</c> is the port's nullable <c>EscapedName</c> (asserted, as every other escaped-name
    /// read in this file does); <c>lastLocation &amp;&amp; isSourceFile(lastLocation) &amp;&amp; isExternalOrCommonJsModule(lastLocation)</c>
    /// is the port's explicit null chain plus the <c>Utilities.IsSourceFile</c>/<c>Utilities.IsExternalOrCommonJsModule</c>
    /// pair; <c>length(merged.declarations) &amp;&amp; every(…)</c> is <c>Declarations is { Count: &gt; 0 }</c> with
    /// <c>Core.Every</c>; <c>d.symbol.globalExports</c> is the <c>GlobalExports</c> table's presence;
    /// <c>errorOrSuggestion(…)</c> is <c>ErrorOrSuggestion</c> with the reference's negated <c>allowUmdGlobalAccess</c>;
    /// <c>root.parent.locals</c> is the <c>ILocalsContainer</c> read this file's <c>ResolveName</c> uses; and
    /// <c>isTypeOnlyImportDeclaration</c> is <c>Utilities.IsTypeOnlyImportDeclaration</c>. The reference's
    /// <c>result &amp;&amp;</c> guards are redundant where the port's <c>result</c> is non-nullable (the parameter is a
    /// <c>Symbol</c> in TS too — <c>resolveNameHelper</c> only calls this callback with a result).
    /// </remarks>
    private void OnSuccessfullyResolvedSymbol(
        Node? errorLocation,
        ISymbol result,
        SymbolFlags meaning,
        Node? lastLocation,
        Node? associatedDeclarationForContainingInitializerOrBindingName,
        bool withinDeferredContext)
    {
        AddLazyDiagnostic(() =>
        {
            var name = result.EscapedName!;
            var isInExternalModule = lastLocation is not null && NodeTests.IsSourceFile(lastLocation) && Utilities.IsExternalOrCommonJsModule((SourceFile)lastLocation);
            // Only check for block-scoped variable if we have an error location and are looking for the
            // name with variable meaning
            //      For example,
            //          declare module foo {
            //              interface bar {}
            //          }
            //      const foo/*1*/: foo/*2*/.bar;
            // The foo at /*1*/ and /*2*/ will share same symbol with two meanings:
            // block-scoped variable and namespace module. However, only when we
            // try to resolve name in /*1*/ which is used in variable position,
            // we want to check for block-scoped
            if (
                errorLocation is not null
                && ((meaning & SymbolFlags.BlockScopedVariable) != 0
                    || (((meaning & SymbolFlags.Class) != 0 || (meaning & SymbolFlags.Enum) != 0) && (meaning & SymbolFlags.Value) == SymbolFlags.Value)))
            {
                var exportOrLocalSymbol = GetExportSymbolOfValueSymbolIfExported(result);
                if ((exportOrLocalSymbol.Flags & SymbolFlags.BlockScopedVariable) != 0 || (exportOrLocalSymbol.Flags & SymbolFlags.Class) != 0 || (exportOrLocalSymbol.Flags & SymbolFlags.Enum) != 0)
                {
                    CheckResolvedBlockScopedVariable(exportOrLocalSymbol, errorLocation);
                }
            }

            // If we're in an external module, we can't reference value symbols created from UMD export declarations
            if (isInExternalModule && (meaning & SymbolFlags.Value) == SymbolFlags.Value && (((Node)errorLocation!).Flags & NodeFlags.JSDoc) == 0)
            {
                var merged = GetMergedSymbol(result);
                if (merged!.Declarations is { Count: > 0 } declarations
                    && Core.Every(declarations, (d, _) => NodeTests.IsNamespaceExportDeclaration((Node)d) || (NodeTests.IsSourceFile((Node)d) && ((IDeclaration)d).Symbol!.GlobalExports is not null)))
                {
                    ErrorOrSuggestion(
                        !Js.IsTruthy(_compilerOptions.AllowUmdGlobalAccess),
                        errorLocation!,
                        Diagnostics._0_refers_to_a_UMD_global_but_the_current_file_is_a_module_Consider_adding_an_import_instead,
                        Utilities.UnescapeLeadingUnderscores(name));
                }
            }

            // If we're in a parameter initializer or binding name, we can't reference the values of the parameter whose initializer we're within or parameters to the right
            if (associatedDeclarationForContainingInitializerOrBindingName is not null && !withinDeferredContext && (meaning & SymbolFlags.Value) == SymbolFlags.Value)
            {
                var candidate = GetMergedSymbol(GetLateBoundSymbol(result));
                var root = (ParameterDeclaration)Utilities.GetRootDeclaration(associatedDeclarationForContainingInitializerOrBindingName);
                // A parameter initializer or binding pattern initializer within a parameter cannot refer to itself
                if (ReferenceEquals(candidate, GetSymbolOfDeclaration((IDeclaration)associatedDeclarationForContainingInitializerOrBindingName)))
                {
                    Error(errorLocation, Diagnostics.Parameter_0_cannot_reference_itself, Utilities.DeclarationNameToString(((INamedDeclaration)associatedDeclarationForContainingInitializerOrBindingName).Name!));
                }
                // And it cannot refer to any declarations which come after it
                else if (candidate!.ValueDeclaration is not null
                    && ((Node)candidate.ValueDeclaration).Pos > associatedDeclarationForContainingInitializerOrBindingName.Pos
                    && ((ILocalsContainer)root.Parent!).Locals is not null
                    && ReferenceEquals(GetSymbol(((ILocalsContainer)root.Parent!).Locals!, candidate.EscapedName!, meaning), candidate))
                {
                    Error(
                        errorLocation,
                        Diagnostics.Parameter_0_cannot_reference_identifier_1_declared_after_it,
                        Utilities.DeclarationNameToString(((INamedDeclaration)associatedDeclarationForContainingInitializerOrBindingName).Name!),
                        Utilities.DeclarationNameToString(errorLocation!));
                }
            }

            if (errorLocation is not null
                && (meaning & SymbolFlags.Value) != 0
                && (result.Flags & SymbolFlags.Alias) != 0
                && (result.Flags & SymbolFlags.Value) == 0
                && !Utilities.IsValidTypeOnlyAliasUseSite(errorLocation))
            {
                var typeOnlyDeclaration = GetTypeOnlyAliasDeclaration(result, SymbolFlags.Value);
                if (typeOnlyDeclaration is not null)
                {
                    var message = typeOnlyDeclaration.Kind == SyntaxKind.ExportSpecifier || typeOnlyDeclaration.Kind == SyntaxKind.ExportDeclaration || typeOnlyDeclaration.Kind == SyntaxKind.NamespaceExport
                        ? Diagnostics._0_cannot_be_used_as_a_value_because_it_was_exported_using_export_type
                        : Diagnostics._0_cannot_be_used_as_a_value_because_it_was_imported_using_import_type;
                    var unescapedName = Utilities.UnescapeLeadingUnderscores(name);
                    AddTypeOnlyDeclarationRelatedInfo(
                        Error(errorLocation, message, unescapedName),
                        typeOnlyDeclaration,
                        unescapedName);
                }
            }

            // Look at 'compilerOptions.isolatedModules' and not 'getIsolatedModules(...)' (which considers 'verbatimModuleSyntax')
            // here because 'verbatimModuleSyntax' will already have an error for importing a type without 'import type'.
            if (Js.IsTruthy(_compilerOptions.IsolatedModules) && isInExternalModule && (meaning & SymbolFlags.Value) == SymbolFlags.Value)
            {
                var isGlobal = ReferenceEquals(GetSymbol(_globals, name, meaning), result);
                var nonValueSymbol = isGlobal && lastLocation is not null && NodeTests.IsSourceFile(lastLocation) && ((SourceFile)lastLocation).Locals is not null
                    ? GetSymbol(((SourceFile)lastLocation).Locals!, name, ~SymbolFlags.Value)
                    : null;
                if (nonValueSymbol is not null)
                {
                    var importDecl = Core.Find(nonValueSymbol.Declarations, (d, _) =>
                        ((Node)d).Kind == SyntaxKind.ImportSpecifier || ((Node)d).Kind == SyntaxKind.ImportClause
                        || ((Node)d).Kind == SyntaxKind.NamespaceImport || ((Node)d).Kind == SyntaxKind.ImportEqualsDeclaration);
                    if (importDecl is not null && !Utilities.IsTypeOnlyImportDeclaration((Node)importDecl))
                    {
                        Error((Node)importDecl, Diagnostics.Import_0_conflicts_with_global_value_used_in_this_file_so_must_be_declared_with_a_type_only_import_when_isolatedModules_is_enabled, Utilities.UnescapeLeadingUnderscores(name));
                    }
                }
            }
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:3400-3435 — <c>checkAndReportErrorForMissingPrefix</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>isIdentifier(errorLocation)</c> narrows the node to an identifier and reads
    /// <c>escapedText</c>; the port casts after that test, the same shape this file's other narrowed reads use.
    /// <c>nameArg</c> is TS's <c>__String | Identifier</c>, stated as <c>object?</c> for <see cref="DiagnosticName"/>.
    /// TS's <c>if (!classSymbol) break;</c> guards an unbound declaration, which the port's
    /// <c>getSymbolOfDeclaration</c> asserts instead (it is declared to answer a symbol or fail), so the guard is
    /// stated where it can hold: the symbol is read and the loop arm continues. <c>location</c> is the reference's
    /// non-null <c>let location: Node = container</c> walk; the port keeps the same walk with a nullable slot, because
    /// <c>Node.parent</c> is nullable here.
    /// </remarks>
    private bool CheckAndReportErrorForMissingPrefix(Node errorLocation, string name, object? nameArg)
    {
        if (!NodeTests.IsIdentifier(errorLocation)
            || ((Identifier)errorLocation).EscapedText != name
            || IsTypeReferenceIdentifier((Identifier)errorLocation)
            || Utilities.IsInTypeQuery(errorLocation))
        {
            return false;
        }

        var container = Utilities.GetThisContainer(errorLocation, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        Node? location = container;
        while (location is not null)
        {
            if (Utilities.IsClassLike(location.Parent!))
            {
                var classSymbol = GetSymbolOfDeclaration((IDeclaration)location.Parent!);
                // Check to see if a static member exists.
                var constructorType = GetTypeOfSymbol(classSymbol);
                if (GetPropertyOfType(constructorType, name) is not null)
                {
                    Error(errorLocation, Diagnostics.Cannot_find_name_0_Did_you_mean_the_static_member_1_0, DiagnosticName(nameArg), Diagnostic.DeferArg(() => SymbolToString(classSymbol)));
                    return true;
                }

                // No static member is present.
                // Check if we're in an instance method and look for a relevant instance member.
                if (ReferenceEquals(location, container) && !Utilities.IsStatic(location))
                {
                    var instanceType = ((InterfaceType)GetDeclaredTypeOfSymbol(classSymbol)).ThisType!;
                    if (GetPropertyOfType(instanceType, name) is not null)
                    {
                        Error(errorLocation, Diagnostics.Cannot_find_name_0_Did_you_mean_the_instance_member_this_0, DiagnosticName(nameArg));
                        return true;
                    }
                }
            }

            location = location.Parent;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3437-3444 — <c>checkAndReportErrorForExtendingInterface</c>.</summary>
    private bool CheckAndReportErrorForExtendingInterface(Node errorLocation)
    {
        var expression = GetEntityNameForExtendingInterface(errorLocation);
        if (expression is not null && ResolveEntityName(expression, SymbolFlags.Interface, /*ignoreErrors*/ true) is not null)
        {
            Error(errorLocation, Diagnostics.Cannot_extend_an_interface_0_Did_you_mean_implements, Utilities.GetTextOfNode(expression));
            return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3449-3462 — <c>getEntityNameForExtendingInterface</c>.</summary>
    /// <remarks>
    /// Climbs up parents to an ExpressionWithTypeArguments, and returns its expression, but returns undefined if that
    /// expression is not an EntityNameExpression.
    /// <para>
    /// PORT-NOTE: TS's <c>EntityNameExpression</c> has no face in the port (the assignment's widening list), so the
    /// answer is the union's bound <see cref="Node"/>; the reference's fall-through from the
    /// <c>ExpressionWithTypeArguments</c> arm into <c>default</c> is written as the explicit <c>return null</c> the
    /// fall-through reaches.
    /// </para>
    /// </remarks>
    private Node? GetEntityNameForExtendingInterface(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PropertyAccessExpression:
                return node.Parent is not null ? GetEntityNameForExtendingInterface(node.Parent) : null;
            case SyntaxKind.ExpressionWithTypeArguments:
                if (Utilities.IsEntityNameExpression(((ExpressionWithTypeArguments)node).Expression!))
                {
                    return ((ExpressionWithTypeArguments)node).Expression;
                }

                // falls through
                return null;
            default:
                return null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:3464-3490 — <c>checkAndReportErrorForUsingTypeAsNamespace</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>resolveName(errorLocation, name, …, undefined, false)</c> is the port's six-argument
    /// <c>ResolveName</c> with the null message and the default <c>excludeGlobals</c> false; the
    /// <c>isQualifiedName(parent)</c> narrowing is the port's null-checked cast, and the two <c>unescapeLeadingUnderscores</c>
    /// reads are the utilities one.
    /// </remarks>
    private bool CheckAndReportErrorForUsingTypeAsNamespace(Node errorLocation, string name, SymbolFlags meaning)
    {
        var namespaceMeaning = SymbolFlags.Namespace | (Utilities.IsInJSFile(errorLocation) ? SymbolFlags.Value : 0);
        if (meaning == namespaceMeaning)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, SymbolFlags.Type & ~namespaceMeaning, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false));
            var parent = errorLocation.Parent;
            if (symbol is not null)
            {
                if (NodeTests.IsQualifiedName(parent!))
                {
                    Debug.Assert(ReferenceEquals(((QualifiedName)parent!).Left, errorLocation), "Should only be resolving left side of qualified name as a namespace");
                    var propName = ((QualifiedName)parent!).Right!.EscapedText!;
                    var propType = GetPropertyOfType(GetDeclaredTypeOfSymbol(symbol), propName);
                    if (propType is not null)
                    {
                        Error(
                            parent!,
                            Diagnostics.Cannot_access_0_1_because_0_is_a_type_but_not_a_namespace_Did_you_mean_to_retrieve_the_type_of_the_property_1_in_0_with_0_1,
                            Utilities.UnescapeLeadingUnderscores(name),
                            Utilities.UnescapeLeadingUnderscores(propName));
                        return true;
                    }
                }

                Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_namespace_here, Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3492-3501 — <c>checkAndReportErrorForUsingValueAsType</c>.</summary>
    private bool CheckAndReportErrorForUsingValueAsType(Node errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & (SymbolFlags.Type & ~SymbolFlags.Namespace)) != 0)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, ~SymbolFlags.Type & SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false));
            if (symbol is not null && (symbol.Flags & SymbolFlags.Namespace) == 0)
            {
                Error(errorLocation, Diagnostics._0_refers_to_a_value_but_is_being_used_as_a_type_here_Did_you_mean_typeof_0, Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3503-3505 — <c>isPrimitiveTypeName</c>.</summary>
    private static bool IsPrimitiveTypeName(string name)
        => name is "any" or "string" or "number" or "boolean" or "never" or "unknown";

    /// <summary>ported from: src/compiler/checker.ts:3507-3513 — <c>checkAndReportErrorForExportingPrimitiveType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes the escaped <c>__String</c> through <c>name as string</c>; the port's parameter is already
    /// that string, so the assertion is the parameter itself.
    /// </remarks>
    private bool CheckAndReportErrorForExportingPrimitiveType(Node errorLocation, string name)
    {
        if (IsPrimitiveTypeName(name) && errorLocation.Parent!.Kind == SyntaxKind.ExportSpecifier)
        {
            Error(errorLocation, Diagnostics.Cannot_export_0_Only_local_declarations_can_be_exported_from_a_module, name);
            return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3515-3554 — <c>checkAndReportErrorForUsingTypeAsValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isHeritageClause(grandparent)</c> is <c>Utilities.IsHeritageClause</c> and the clause's
    /// <c>token</c> is the <see cref="HeritageClause"/> face's <c>Token</c>; <c>symbol &amp;&amp; getSymbolFlags(symbol)</c> is
    /// stated as the port's local flags read (the reference's <c>allFlags !== undefined</c> cannot hold when the symbol
    /// is present, since <c>getSymbolFlags</c> answers a number).
    /// </remarks>
    private bool CheckAndReportErrorForUsingTypeAsValue(Node errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & SymbolFlags.Value) != 0)
        {
            if (IsPrimitiveTypeName(name))
            {
                var grandparent = errorLocation.Parent!.Parent;
                if (grandparent is not null && grandparent.Parent is not null && NodeTests.IsHeritageClause(grandparent))
                {
                    var heritageKind = ((HeritageClause)grandparent).Token;
                    var containerKind = grandparent.Parent.Kind;
                    if (containerKind == SyntaxKind.InterfaceDeclaration && heritageKind == SyntaxKind.ExtendsKeyword)
                    {
                        Error(errorLocation, Diagnostics.An_interface_cannot_extend_a_primitive_type_like_0_It_can_only_extend_other_named_object_types, Utilities.UnescapeLeadingUnderscores(name));
                    }
                    else if (Utilities.IsClassLike(grandparent.Parent) && heritageKind == SyntaxKind.ExtendsKeyword)
                    {
                        Error(errorLocation, Diagnostics.A_class_cannot_extend_a_primitive_type_like_0_Classes_can_only_extend_constructable_values, Utilities.UnescapeLeadingUnderscores(name));
                    }
                    else if (Utilities.IsClassLike(grandparent.Parent) && heritageKind == SyntaxKind.ImplementsKeyword)
                    {
                        Error(errorLocation, Diagnostics.A_class_cannot_implement_a_primitive_type_like_0_It_can_only_implement_other_named_object_types, Utilities.UnescapeLeadingUnderscores(name));
                    }
                }
                else
                {
                    Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here, Utilities.UnescapeLeadingUnderscores(name));
                }

                return true;
            }

            var symbol = ResolveSymbol(ResolveName(errorLocation, name, SymbolFlags.Type & ~SymbolFlags.Value, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false));
            if (symbol is not null && (GetSymbolFlags(symbol) & SymbolFlags.Value) == 0)
            {
                var rawName = Utilities.UnescapeLeadingUnderscores(name);
                if (IsES2015OrLaterConstructorName(name))
                {
                    Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_es2015_or_later, rawName);
                }
                else if (MaybeMappedType(errorLocation, symbol))
                {
                    Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here_Did_you_mean_to_use_1_in_0, rawName, rawName == "K" ? "P" : "K");
                }
                else
                {
                    Error(errorLocation, Diagnostics._0_only_refers_to_a_type_but_is_being_used_as_a_value_here, rawName);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3556-3563 — <c>maybeMappedType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's three-answer visitor (<c>false</c> keeps walking, <c>true</c> matches,
    /// <c>"quit"</c> stops) is the port's quit-capable <c>Utilities.FindAncestor(Node?, Func&lt;Node, bool?&gt;)</c>, whose
    /// <c>null</c> is <c>"quit"</c>; <c>container.members.length === 1</c> is the <c>TypeLiteralNode</c> member list's
    /// count and <c>allTypesAssignableToKind</c> is the landed member of that name.
    /// </remarks>
    private bool MaybeMappedType(Node node, ISymbol symbol)
    {
        var container = (TypeLiteralNode?)Utilities.FindAncestor(node.Parent, n =>
            NodeTests.IsComputedPropertyName(n) || NodeTests.IsPropertySignature(n) ? false
            : NodeTests.IsTypeLiteralNode(n) ? true
            : null);
        if (container is not null && container.Members!.Count == 1)
        {
            var type = GetDeclaredTypeOfSymbol(symbol);
            // PORT-NOTE: the reference's `!!(…)` is the conjunction itself; the result is a bool either way.
            return (type.Flags & TypeFlags.Union) != 0 && AllTypesAssignableToKind(type, TypeFlags.StringOrNumberLiteral, /*strict*/ true);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3565-3576 — <c>isES2015OrLaterConstructorName</c>.</summary>
    private static bool IsES2015OrLaterConstructorName(string n)
        => n is "Promise" or "Symbol" or "Map" or "WeakMap" or "Set" or "WeakSet";

    /// <summary>ported from: src/compiler/checker.ts:3578-3598 — <c>checkAndReportErrorForUsingNamespaceAsTypeOrValue</c>.</summary>
    private bool CheckAndReportErrorForUsingNamespaceAsTypeOrValue(Node errorLocation, string name, SymbolFlags meaning)
    {
        if ((meaning & (SymbolFlags.Value & ~SymbolFlags.Type)) != 0)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, SymbolFlags.NamespaceModule, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false));
            if (symbol is not null)
            {
                Error(
                    errorLocation,
                    Diagnostics.Cannot_use_namespace_0_as_a_value,
                    Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }
        else if ((meaning & (SymbolFlags.Type & ~SymbolFlags.Value)) != 0)
        {
            var symbol = ResolveSymbol(ResolveName(errorLocation, name, SymbolFlags.Module, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false));
            if (symbol is not null)
            {
                Error(errorLocation, Diagnostics.Cannot_use_namespace_0_as_a_type, Utilities.UnescapeLeadingUnderscores(name));
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3600-3636 — <c>checkResolvedBlockScopedVariable</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>declaration === undefined</c> with the reference's <c>Debug.fail</c> is stated as the non-failing
    /// fail call plus a return, which is what the ported <c>Debug</c> helper does; the declaration-name reads are
    /// <c>Utilities.GetNameOfDeclaration</c> plus <c>Utilities.DeclarationNameToString</c>, and the diagnostic and its
    /// related information are the <c>Utilities.X</c> pair this file's other diagnostic sites use.
    /// </remarks>
    private void CheckResolvedBlockScopedVariable(ISymbol result, Node errorLocation)
    {
        Debug.Assert((result.Flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.Class | SymbolFlags.Enum)) != 0, "block-scoped symbols are the only ones this check admits");
        if ((result.Flags & (SymbolFlags.Function | SymbolFlags.FunctionScopedVariable | SymbolFlags.Assignment)) != 0 && (result.Flags & SymbolFlags.Class) != 0)
        {
            // constructor functions aren't block scoped
            return;
        }

        // Block-scoped variables cannot be used before their definition
        var declaration = Core.Find(result.Declarations, (d, _) =>
            Utilities.IsBlockOrCatchScoped(d) || Utilities.IsClassLike((Node)d) || ((Node)d).Kind == SyntaxKind.EnumDeclaration);
        if (declaration is null)
        {
            Debug.Fail("checkResolvedBlockScopedVariable could not find block-scoped declaration");
            return;
        }

        if ((((Node)declaration).Flags & NodeFlags.Ambient) == 0 && !IsBlockScopedNameDeclaredBeforeUse(declaration, errorLocation))
        {
            Diagnostic? diagnosticMessage = null;
            var declarationName = Utilities.DeclarationNameToString((Node)Utilities.GetNameOfDeclaration((Node)declaration)!);
            if ((result.Flags & SymbolFlags.BlockScopedVariable) != 0)
            {
                diagnosticMessage = Error(errorLocation, Diagnostics.Block_scoped_variable_0_used_before_its_declaration, declarationName);
            }
            else if ((result.Flags & SymbolFlags.Class) != 0)
            {
                diagnosticMessage = Error(errorLocation, Diagnostics.Class_0_used_before_its_declaration, declarationName);
            }
            else if ((result.Flags & SymbolFlags.RegularEnum) != 0)
            {
                diagnosticMessage = Error(errorLocation, Diagnostics.Enum_0_used_before_its_declaration, declarationName);
            }
            else
            {
                Debug.Assert((result.Flags & SymbolFlags.ConstEnum) != 0, "the last block-scoped kind is a const enum");
                if (Utilities.GetIsolatedModules(_compilerOptions))
                {
                    diagnosticMessage = Error(errorLocation, Diagnostics.Enum_0_used_before_its_declaration, declarationName);
                }
            }

            if (diagnosticMessage is not null)
            {
                Utilities.AddRelatedInfo(diagnosticMessage, Utilities.CreateDiagnosticForNode((Node)declaration, Diagnostics._0_is_declared_here, declarationName));
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:3638-3647 — <c>isSameScopeDescendentOf</c>.</summary>
    /// <remarks>
    /// Starting from 'initial' node walk up the parent chain until 'stopAt' node is reached. If at any point current node
    /// is equal to 'parent' node - return true. If current node is an IIFE, continue walking up. Return false if
    /// 'stopAt' node is reached or <c>isFunctionLike(current) === true</c>.
    /// <para>
    /// PORT-NOTE: the reference's visitor answers <c>true | "quit" | false</c>, so it binds to the port's quit-capable
    /// <c>FindAncestor</c> overload (<c>null</c> is <c>"quit"</c>, which answers no match — the reference's own
    /// <c>!!…</c> makes the quit answer false too).
    /// </para>
    /// </remarks>
    private bool IsSameScopeDescendentOf(Node initial, Node? parent, Node stopAt)
        => parent is not null && Utilities.FindAncestor(initial, n =>
            ReferenceEquals(n, parent)
                ? true
                : ReferenceEquals(n, stopAt) || (Utilities.IsFunctionLike(n) && (Utilities.GetImmediatelyInvokedFunctionExpression(n) is null || (Utilities.GetFunctionFlags(n) & FunctionFlags.AsyncGenerator) != 0))
                    ? null
                    : false) is not null;

    /// <summary>ported from: src/compiler/checker.ts:2982-3204 — <c>isBlockScopedNameDeclaredBeforeUse</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>moduleKind &amp;&amp; …</c> is a truthiness test on the enum, so it is
    /// <c>_moduleKind != ModuleKind.None</c>; <c>!compilerOptions.outFile</c> is <c>!Js.IsTruthy(…)</c> (TS's falsy string
    /// is the empty one); <c>declaration.flags</c> is read through the node face; the two <c>findAncestor</c> walks that
    /// can quit use the quit-capable overload (<c>null</c> for <c>"quit"</c>); <c>emitStandardClassFields</c> is
    /// <c>_emitStandardClassFields</c>, <c>declContainer</c> is the enclosing block-scope container, and the four local
    /// functions of the reference's body are the private members beside this one (<c>IsImmediatelyUsedInInitializerOfBlockScopedVariable</c>,
    /// <c>IsUsedInFunctionOrInstancePropertyWorker</c>, <c>IsPropertyImmediatelyReferencedWithinDeclaration</c> and
    /// <c>IsSameScopeDescendentOf</c>), as the assignment records.
    /// </remarks>
    private bool IsBlockScopedNameDeclaredBeforeUse(IDeclaration declaration, Node usage)
    {
        var declarationFile = Utilities.GetSourceFileOfNode((Node)declaration);
        var useFile = Utilities.GetSourceFileOfNode(usage);
        var declContainer = Utilities.GetEnclosingBlockScopeContainer((Node)declaration);
        if (!ReferenceEquals(declarationFile, useFile))
        {
            if (
                (_moduleKind != ModuleKind.None && (declarationFile!.ExternalModuleIndicator is not null || useFile!.ExternalModuleIndicator is not null))
                || !Js.IsTruthy(_compilerOptions.OutFile)
                || Utilities.IsInTypeQuery(usage)
                || (((Node)declaration).Flags & NodeFlags.Ambient) != 0
            )
            {
                // nodes are in different files and order cannot be determined
                return true;
            }

            // declaration is after usage
            // can be legal if usage is deferred (i.e. inside function or in initializer of instance property)
            if (IsUsedInFunctionOrInstancePropertyWorker(usage, (Node)declaration, declContainer))
            {
                return true;
            }

            var sourceFiles = _host.GetSourceFiles();
            return sourceFiles.IndexOf(declarationFile!) <= sourceFiles.IndexOf(useFile!);
        }

        // deferred usage in a type context is always OK regardless of the usage position:
        if ((usage.Flags & NodeFlags.JSDoc) != 0 || Utilities.IsInTypeQuery(usage) || IsInAmbientOrTypeNode(usage))
        {
            return true;
        }

        if (((Node)declaration).Pos <= usage.Pos && !(NodeTests.IsPropertyDeclaration((Node)declaration) && Utilities.IsThisProperty(usage.Parent!) && ((PropertyDeclaration)declaration).Initializer is null && ((PropertyDeclaration)declaration).ExclamationToken is null))
        {
            // declaration is before usage
            if (((Node)declaration).Kind == SyntaxKind.BindingElement)
            {
                // still might be illegal if declaration and usage are both binding elements (eg var [a = b, b = b] = [1, 2])
                var errorBindingElement = (BindingElement?)Utilities.GetAncestor(usage, SyntaxKind.BindingElement);
                if (errorBindingElement is not null)
                {
                    return !ReferenceEquals(Utilities.FindAncestor(errorBindingElement, NodeTests.IsBindingElement), Utilities.FindAncestor((Node)declaration, NodeTests.IsBindingElement))
                        || ((Node)declaration).Pos < errorBindingElement.Pos;
                }

                // or it might be illegal if usage happens before parent variable is declared (eg var [a] = a)
                return IsBlockScopedNameDeclaredBeforeUse((IDeclaration)Utilities.GetAncestor((Node)declaration, SyntaxKind.VariableDeclaration)!, usage);
            }
            else if (((Node)declaration).Kind == SyntaxKind.VariableDeclaration)
            {
                // still might be illegal if usage is in the initializer of the variable declaration (eg var a = a)
                return !IsImmediatelyUsedInInitializerOfBlockScopedVariable((VariableDeclaration)declaration, usage, declContainer);
            }
            else if (Utilities.IsClassLike((Node)declaration))
            {
                // still might be illegal if the usage is within a computed property name in the class (eg class A { static p = "a"; [A.p]() {} })
                // or when used within a decorator in the class (e.g. `@dec(A.x) class A { static x = "x" }`),
                // except when used in a function that is not an IIFE (e.g., `@dec(() => A.x) class A { ... }`)
                var container = Utilities.FindAncestor(usage, n =>
                    ReferenceEquals(n, declaration) ? null
                        : NodeTests.IsComputedPropertyName(n) ? ReferenceEquals(n.Parent!.Parent, declaration)
                        : !_legacyDecorators && NodeTests.IsDecorator(n) && (ReferenceEquals(n.Parent, declaration)
                            || (NodeTests.IsMethodDeclaration(n.Parent!) && ReferenceEquals(n.Parent!.Parent, declaration))
                            || (Utilities.IsGetOrSetAccessorDeclaration(n.Parent!) && ReferenceEquals(n.Parent!.Parent, declaration))
                            || (NodeTests.IsPropertyDeclaration(n.Parent!) && ReferenceEquals(n.Parent!.Parent, declaration))
                            || (NodeTests.IsParameter(n.Parent!) && ReferenceEquals(n.Parent!.Parent!.Parent, declaration)))
                            ? true
                            : false);
                if (container is null)
                {
                    return true;
                }

                if (!_legacyDecorators && NodeTests.IsDecorator(container))
                {
                    return Utilities.FindAncestor(usage, n =>
                        ReferenceEquals(n, container) ? null
                            : Utilities.IsFunctionLike(n) && Utilities.GetImmediatelyInvokedFunctionExpression(n) is null ? true
                            : false) is not null;
                }

                return false;
            }
            else if (NodeTests.IsPropertyDeclaration((Node)declaration))
            {
                // still might be illegal if a self-referencing property initializer (eg private x = this.x)
                return !IsPropertyImmediatelyReferencedWithinDeclaration((Node)declaration, usage, /*stopAtAnyPropertyDeclaration*/ false);
            }
            else if (Utilities.IsParameterPropertyDeclaration((Node)declaration, ((Node)declaration).Parent!))
            {
                // foo = this.bar is illegal in emitStandardClassFields when bar is a parameter property
                return !(_emitStandardClassFields
                    && ReferenceEquals(Utilities.GetContainingClass((Node)declaration), Utilities.GetContainingClass(usage))
                    && IsUsedInFunctionOrInstancePropertyWorker(usage, (Node)declaration, declContainer));
            }

            return true;
        }

        // declaration is after usage, but it can still be legal if usage is deferred:
        // 1. inside an export specifier
        // 2. inside a function
        // 3. inside an instance property initializer, a reference to a non-instance property
        //    (except when emitStandardClassFields: true and the reference is to a parameter property)
        // 4. inside a static property initializer, a reference to a static method in the same class
        // 5. inside a TS export= declaration (since we will move the export statement during emit to avoid TDZ)
        if (usage.Parent!.Kind == SyntaxKind.ExportSpecifier || (usage.Parent!.Kind == SyntaxKind.ExportAssignment && Js.IsTruthy(((ExportAssignment)usage.Parent).IsExportEquals)))
        {
            // export specifiers do not use the variable, they only make it available for use
            return true;
        }

        // When resolving symbols for exports, the `usage` location passed in can be the export site directly
        if (usage.Kind == SyntaxKind.ExportAssignment && Js.IsTruthy(((ExportAssignment)usage).IsExportEquals))
        {
            return true;
        }

        if (IsUsedInFunctionOrInstancePropertyWorker(usage, (Node)declaration, declContainer))
        {
            if (
                _emitStandardClassFields
                && Utilities.GetContainingClass((Node)declaration) is not null
                && (NodeTests.IsPropertyDeclaration((Node)declaration) || Utilities.IsParameterPropertyDeclaration((Node)declaration, ((Node)declaration).Parent!))
            )
            {
                return !IsPropertyImmediatelyReferencedWithinDeclaration((Node)declaration, usage, /*stopAtAnyPropertyDeclaration*/ true);
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:3089-3105 — <c>isImmediatelyUsedInInitializerOfBlockScopedVariable</c>
    /// (a local function of <c>isBlockScopedNameDeclaredBeforeUse</c>).</summary>
    /// <remarks>
    /// PORT-NOTE: the reference closes over <c>declContainer</c>; the port passes it explicitly, since the reference's
    /// local function is a private member here. <c>isSameScopeDescendentOf</c> is the private member beside this one.
    /// </remarks>
    private bool IsImmediatelyUsedInInitializerOfBlockScopedVariable(VariableDeclaration declaration, Node usage, Node declContainer)
    {
        switch (declaration.Parent!.Parent!.Kind)
        {
            case SyntaxKind.VariableStatement:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForOfStatement:
                // variable statement/for/for-of statement case,
                // use site should not be inside variable declaration (initializer of declaration or binding element)
                if (IsSameScopeDescendentOf(usage, declaration, declContainer))
                {
                    return true;
                }

                break;
        }

        // ForIn/ForOf case - use site should not be used in expression part
        var grandparent = declaration.Parent!.Parent!;
        // PORT-NOTE: TS's `forInOrOfStatement` union is `ForInStatement | ForOfStatement`
        // (types.ts:1531) and it reads `expression`, a slot only those two classes carry
        // (the port's shared base `IterationStatement`, Model/NodeClasses.cs:963, has just
        // `Statement`), so the two kinds are matched here instead of a cast to a union the
        // port has no face for.
        return Utilities.IsForInOrOfStatement(grandparent)
            && IsSameScopeDescendentOf(
                usage,
                (grandparent is ForInStatement forIn
                    ? forIn.Expression
                    : ((ForOfStatement)grandparent).Expression)!,
                declContainer);
    }

    /// <summary>ported from: src/compiler/checker.ts:3107-3109 — <c>isUsedInFunctionOrInstanceProperty</c> (a local
    /// function of <c>isBlockScopedNameDeclaredBeforeUse</c>).</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's one-line local function is a pure call-through of its worker
    /// (<c>return isUsedInFunctionOrInstancePropertyWorker(usage, declaration);</c>); the port states the worker as the
    /// private member and elides the call-through, with the two call sites in <see cref="IsBlockScopedNameDeclaredBeforeUse"/>
    /// invoking the worker directly (the same arguments, the same order).
    /// </remarks>
    private bool IsUsedInFunctionOrInstancePropertyWorker(Node usage, Node declaration, Node declContainer)
        => Utilities.FindAncestor(usage, current =>
        {
            if (ReferenceEquals(current, declContainer))
            {
                return null;
            }

            if (Utilities.IsFunctionLike(current))
            {
                return Utilities.GetImmediatelyInvokedFunctionExpression(current) is null;
            }

            if (NodeTests.IsClassStaticBlockDeclaration(current))
            {
                return declaration.Pos < usage.Pos;
            }

            var propertyDeclaration = Core.TryCast<PropertyDeclaration, Node>(current.Parent, NodeTests.IsPropertyDeclaration);
            if (propertyDeclaration is not null)
            {
                var initializerOfProperty = ReferenceEquals(propertyDeclaration.Initializer, current);
                if (initializerOfProperty)
                {
                    if (Utilities.IsStatic(current.Parent!))
                    {
                        if (declaration.Kind == SyntaxKind.MethodDeclaration)
                        {
                            return true;
                        }

                        if (NodeTests.IsPropertyDeclaration(declaration) && ReferenceEquals(Utilities.GetContainingClass(usage), Utilities.GetContainingClass(declaration)))
                        {
                            var propName = ((PropertyDeclaration)declaration).Name;
                            if (propName is Identifier || NodeTests.IsPrivateIdentifier(propName!))
                            {
                                var type = GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)declaration));
                                var staticBlocks = ((IClassLikeDeclarationBase)((Node)declaration).Parent!).Members!
                            .Where(e => NodeTests.IsClassStaticBlockDeclaration((Node)e))
                            .Cast<ClassStaticBlockDeclaration>()
                            .ToList();
                                if (IsPropertyInitializedInStaticBlocks((Identifier)propName!, type, staticBlocks, ((Node)declaration).Parent!.Pos, current.Pos))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    else
                    {
                        var isDeclarationInstanceProperty = ((Node)declaration).Kind == SyntaxKind.PropertyDeclaration && !Utilities.IsStatic(declaration);
                        if (!isDeclarationInstanceProperty || !ReferenceEquals(Utilities.GetContainingClass(usage), Utilities.GetContainingClass(declaration)))
                        {
                            return true;
                        }
                    }
                }
            }

            var decorator = Core.TryCast<Decorator, Node>(current.Parent, NodeTests.IsDecorator);
            if (decorator is not null && ReferenceEquals(decorator.Expression, current))
            {
                if (NodeTests.IsParameter(decorator.Parent!))
                {
                    return IsUsedInFunctionOrInstancePropertyWorker(decorator.Parent!.Parent!.Parent!, declaration, declContainer) ? true : null;
                }

                if (NodeTests.IsMethodDeclaration(decorator.Parent!))
                {
                    return IsUsedInFunctionOrInstancePropertyWorker(decorator.Parent!.Parent!, declaration, declContainer) ? true : null;
                }
            }

            return false;
        }) is not null;

    /// <summary>ported from: src/compiler/checker.ts:3165-3203 — <c>isPropertyImmediatelyReferencedWithinDeclaration</c>
    /// (a local function of <c>isBlockScopedNameDeclaredBeforeUse</c>).</summary>
    /// <remarks>
    /// <c>stopAtAnyPropertyDeclaration</c> is used for detecting ES-standard class field use-before-def errors.
    /// <para>
    /// PORT-NOTE: the reference types <c>declaration</c> as
    /// <c>PropertyDeclaration | ParameterPropertyDeclaration</c> (checker.ts:3166) — the true-call site at
    /// checker.ts:3081 enters with a constructor parameter property, so the union is load-bearing. C# cannot narrow
    /// through <see cref="Utilities.IsParameterPropertyDeclaration"/>, so the port states the parameter as the
    /// <c>Node</c> that both call sites already hold: the body reads only <c>usage.end > declaration.end</c>, the node
    /// identity, and the two Node-level guards, so no <c>PropertyDeclaration</c> assertion is honest at the call.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's visitor answers <c>true | "quit" | false</c>, which is the port's quit-capable
    /// <c>FindAncestor</c> overload; the final <c>ancestorChangingReferenceScope === undefined</c> is that overload's
    /// <c>null</c> answer.
    /// </para>
    /// </remarks>
    private bool IsPropertyImmediatelyReferencedWithinDeclaration(Node declaration, Node usage, bool stopAtAnyPropertyDeclaration)
    {
        // always legal if usage is after declaration
        if (usage.End > declaration.End)
        {
            return false;
        }

        // still might be legal if usage is deferred (e.g. x: any = () => this.x)
        // otherwise illegal if immediately referenced within the declaration (e.g. x: any = this.x)
        var ancestorChangingReferenceScope = Utilities.FindAncestor(usage, (Node node) =>
        {
            if (ReferenceEquals(node, declaration))
            {
                return null;
            }

            switch (node.Kind)
            {
                case SyntaxKind.ArrowFunction:
                    return true;
                case SyntaxKind.PropertyDeclaration:
                    // even when stopping at any property declaration, they need to come from the same class
                    return stopAtAnyPropertyDeclaration
                            && ((NodeTests.IsPropertyDeclaration(declaration) && ReferenceEquals(node.Parent, declaration.Parent))
                                || (Utilities.IsParameterPropertyDeclaration(declaration, declaration.Parent!) && ReferenceEquals(node.Parent, declaration.Parent!.Parent)))
                        ? null
                        : true;
                case SyntaxKind.Block:
                    switch (node.Parent!.Kind)
                    {
                        case SyntaxKind.GetAccessor:
                        case SyntaxKind.MethodDeclaration:
                        case SyntaxKind.SetAccessor:
                            return true;
                        default:
                            return false;
                    }

                default:
                    return false;
            }
        });

        return ancestorChangingReferenceScope is null;
    }

    /// <summary>ported from: src/compiler/checker.ts:35220-35225 — <c>getSuggestedLibForNonExistentName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (cross-band): this member sits at checker.ts:35220, outside the symbol band, but its only caller is
    /// <see cref="OnFailedToResolveSymbol"/> and no other order file claims it, so it is declared here.
    /// <c>getScriptTargetFeatures</c> is <see cref="Utilities.GetScriptTargetFeatures"/>; the reference's
    /// <c>typeFeatures &amp;&amp; …</c> is the port's null test.
    /// <para>
    /// PORT-NOTE: the reference reads its table with <c>firstIterator</c> (core.ts:1104), whose empty-sequence branch is
    /// <c>Debug.fail("iterator is empty")</c>; the port reads <c>Core.FirstOrUndefinedIterator</c>, the reference's own
    /// <c>firstOrUndefinedIterator</c> (core.ts:1088), so the same lookup answers <c>null</c> where the key set is empty
    /// — the value the reference's <c>typeFeatures &amp;&amp;</c> guard answers for a name with no table entry — instead
    /// of failing the process. On every non-empty key set the two reads are the same first key.
    /// </para>
    /// <para>
    /// PORT-NOTE: the empty key sets this guards against are a defect of
    /// <see cref="Utilities.GetScriptTargetFeatures"/> (Checker/Utilities.SymbolBand.cs), not of the reference: ten of
    /// its type names carry entries in the reference whose <c>array</c> is <c>emptyArray</c> — <c>Iterator</c> and
    /// <c>AsyncIterator</c> under <c>es2015</c>, <c>AsyncIterable</c>, <c>AsyncIterableIterator</c>,
    /// <c>AsyncGenerator</c> and <c>AsyncGeneratorFunction</c> under <c>es2018</c>, <c>BigInt</c> under
    /// <c>es2020</c>, <c>Float16Array</c> under <c>es2025</c>, and <c>DisposableStack</c>/<c>AsyncDisposableStack</c>
    /// under <c>esnext</c> (utilities.ts:1384-9283) — and the port's transcription dropped those keys, leaving their
    /// inner tables empty (as it dropped <c>Promise</c>'s <c>es2015</c> and <c>BigInt64Array</c>/<c>BigUint64Array</c>'s
    /// <c>es2020</c>, whose suggestion is therefore a later target than the reference's). The reference's own
    /// <c>firstIterator</c> never sees an empty key set; until the table carries those keys, this read answers
    /// <c>null</c> where the reference's answers <c>es2015</c>/<c>es2018</c>/<c>es2020</c>/<c>es2025</c>/<c>esnext</c>.
    /// </para>
    /// </remarks>
    private string? GetSuggestedLibForNonExistentName(object? nameArg)
    {
        var missingName = DiagnosticName(nameArg);
        var allFeatures = Utilities.GetScriptTargetFeatures();
        var typeFeatures = allFeatures.GetValueOrDefault(missingName);
        return typeFeatures is not null ? Core.FirstOrUndefinedIterator(typeFeatures.Keys) : null;
    }

    // ---------------------------------------------------------------------------------------------
    // The alias-target resolution family (checker.ts:3708-4284)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:4192-4196 — <c>getCommonJSPropertyAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (cross-band): declared here because <see cref="GetTargetOfImportEqualsDeclaration"/> and
    /// <see cref="GetTargetOfImportSpecifier"/> are its only callers; `isVariableDeclaration(node) &amp;&amp; node.initializer
    /// &amp;&amp; isPropertyAccessExpression(node.initializer)` is the port's null chain over the same slots.
    /// </remarks>
    private static PropertyAccessExpression? GetCommonJSPropertyAccess(Node node)
        => NodeTests.IsVariableDeclaration(node) && ((VariableDeclaration)node).Initializer is not null && NodeTests.IsPropertyAccessExpression(((VariableDeclaration)node).Initializer!)
            ? (PropertyAccessExpression)((VariableDeclaration)node).Initializer!
            : null;

    /// <summary>ported from: src/compiler/checker.ts:3936-3951 — <c>getModuleSpecifierForImportOrExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ImportEqualsDeclaration | ImportClause | NamespaceImport |
    /// ImportOrExportSpecifier</c> union and its <c>default</c> arm is <c>Debug.assertNever(node)</c>; the port takes the
    /// declaration face (which every arm is) and switches on the <c>kind</c>, with <c>null</c> for the arm the reference
    /// can never see. <c>isExternalModuleReference(node.moduleReference)</c> is the kind test the port states inline, and
    /// the three <c>parent</c> climbs are the reference's own.
    /// </remarks>
    private static Expression? GetModuleSpecifierForImportOrExport(IDeclaration node)
    {
        switch (((Node)node).Kind)
        {
            case SyntaxKind.ImportClause:
                return ((ImportDeclaration)((Node)node).Parent!).ModuleSpecifier;
            case SyntaxKind.ImportEqualsDeclaration:
                var moduleReference = ((ImportEqualsDeclaration)node).ModuleReference;
                return moduleReference is ExternalModuleReference externalModuleReference ? externalModuleReference.Expression : null;
            case SyntaxKind.NamespaceImport:
                return ((ImportDeclaration)((Node)node).Parent!.Parent!).ModuleSpecifier;
            case SyntaxKind.ImportSpecifier:
                return ((ImportDeclaration)((Node)node).Parent!.Parent!.Parent!).ModuleSpecifier;
            case SyntaxKind.ExportSpecifier:
                return ((ExportDeclaration)((Node)node).Parent!.Parent!).ModuleSpecifier;
            default:
                return null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:3708-3734 — <c>getTargetOfImportEqualsDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getCommonJSPropertyAccess(node)</c> is the private member beside this one;
    /// <c>(getLeftmostAccessExpression(…) as CallExpression).arguments[0] as StringLiteral</c> is the port's two casts over
    /// the same slots; <c>getExternalModuleRequireArgument(node) || getExternalModuleImportEqualsDeclarationExpression(node)</c>
    /// is the port's <c>??</c> (the first answers a literal or null, the second is the reference's fallback);
    /// <c>ModuleKind.Node20 &lt;= moduleKind &amp;&amp; moduleKind &lt;= ModuleKind.NodeNext</c> is the port's range test on
    /// <c>_moduleKind</c>; and <c>node.moduleReference.kind === ExternalModuleReference</c> is the port's kind read.
    /// </remarks>
    private ISymbol? GetTargetOfImportEqualsDeclaration(Node node, bool dontResolveAlias)
    {
        var commonJSPropertyAccess = GetCommonJSPropertyAccess(node);
        if (commonJSPropertyAccess is not null)
        {
            var name = (StringLiteral)((CallExpression)Utilities.GetLeftmostAccessExpression(commonJSPropertyAccess.Expression!)).Arguments![0];
            return NodeTests.IsIdentifier(commonJSPropertyAccess.Name!)
                ? ResolveSymbol(GetPropertyOfType(ResolveExternalModuleTypeByLiteral(name), ((Identifier)commonJSPropertyAccess.Name!).EscapedText))
                : null;
        }

        if (NodeTests.IsVariableDeclaration(node) || ((ImportEqualsDeclaration)node).ModuleReference!.Kind == SyntaxKind.ExternalModuleReference)
        {
            var immediate = ResolveExternalModuleName(
                node,
                Utilities.GetExternalModuleRequireArgument(node) ?? Utilities.GetExternalModuleImportEqualsDeclarationExpression(node));
            var resolved = ResolveExternalModuleSymbol(immediate);
            if (resolved is not null && ModuleKind.Node20 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
            {
                var moduleExports = GetExportOfModule(resolved, "module.exports", (IDeclaration)node, dontResolveAlias);
                if (moduleExports is not null)
                {
                    return moduleExports;
                }
            }

            MarkSymbolOfAliasDeclarationIfTypeOnly((IDeclaration)node, immediate, resolved, /*overwriteEmpty*/ false);
            return resolved;
        }

        var resolvedSymbol = GetSymbolOfPartOfRightHandSideOfImportEquals(((ImportEqualsDeclaration)node).ModuleReference!, dontResolveAlias);
        CheckAndReportErrorForResolvingImportAliasToTypeOnlySymbol(node, resolvedSymbol);
        return resolvedSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:3736-3751 — <c>checkAndReportErrorForResolvingImportAliasToTypeOnlySymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>node.isTypeOnly</c> is the <c>ImportEqualsDeclaration</c> face's slot; the type-only declaration's
    /// <c>name</c> is read through <c>INamedDeclaration</c> and handed to
    /// <c>Utilities.ModuleExportNameTextUnescaped</c>; the diagnostic and its related information are the
    /// <c>Utilities.X</c> pair used throughout this file.
    /// </remarks>
    private void CheckAndReportErrorForResolvingImportAliasToTypeOnlySymbol(Node node, ISymbol? resolved)
    {
        if (MarkSymbolOfAliasDeclarationIfTypeOnly((IDeclaration)node, /*immediateTarget*/ null, resolved, /*overwriteEmpty*/ false)
            && !((ImportEqualsDeclaration)node).IsTypeOnly)
        {
            var typeOnlyDeclaration = GetTypeOnlyAliasDeclaration(GetSymbolOfDeclaration((IDeclaration)node))!;
            var isExport = typeOnlyDeclaration.Kind == SyntaxKind.ExportSpecifier || typeOnlyDeclaration.Kind == SyntaxKind.ExportDeclaration;
            var message = isExport
                ? Diagnostics.An_import_alias_cannot_reference_a_declaration_that_was_exported_using_export_type
                : Diagnostics.An_import_alias_cannot_reference_a_declaration_that_was_imported_using_import_type;
            var relatedMessage = isExport
                ? Diagnostics._0_was_exported_here
                : Diagnostics._0_was_imported_here;

            // TODO: how to get name for export *?
            var name = typeOnlyDeclaration.Kind == SyntaxKind.ExportDeclaration ? "*" : Utilities.ModuleExportNameTextUnescaped(((INamedDeclaration)typeOnlyDeclaration).Name!);
            Utilities.AddRelatedInfo(
                Error(((ImportEqualsDeclaration)node).ModuleReference!, message),
                Utilities.CreateDiagnosticForNode(typeOnlyDeclaration, relatedMessage, name));
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:3850-3855 — <c>getTargetOfImportClause</c>.</summary>
    private ISymbol? GetTargetOfImportClause(IDeclaration node, bool dontResolveAlias)
    {
        var moduleSymbol = ResolveExternalModuleName((Node)node, ((ImportDeclaration)((Node)node).Parent!).ModuleSpecifier);
        if (moduleSymbol is not null)
        {
            return GetTargetofModuleDefault(moduleSymbol, node, dontResolveAlias);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:3857-3934 — <c>getTargetofModuleDefault</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>moduleSymbol.declarations?.find(isSourceFile)</c>,
    /// <c>exportEqualsSymbol!.valueDeclaration</c> and the <c>hasExportAssignmentSymbol</c> branch are the port's
    /// <c>Core.Find</c>, the value-declaration read and <c>HasExportAssignmentSymbol</c>; <c>isShorthandAmbientModuleSymbol</c>,
    /// <c>getESModuleInterop</c> and <c>reportNonDefaultExport</c> are the landed/private members of those names;
    /// <c>moduleKind &gt;= ModuleKind.ES2015</c> is the port's <c>_moduleKind</c> test; and the reference's
    /// <c>isImportOrExportSpecifier(node) &amp;&amp; node.propertyName || node.name</c> is the port's switch over the two
    /// specifier kinds.
    /// </remarks>
    private ISymbol? GetTargetofModuleDefault(ISymbol moduleSymbol, IDeclaration node, bool dontResolveAlias)
    {
        var file = Core.Find(moduleSymbol.Declarations, (d, _) => NodeTests.IsSourceFile((Node)d));
        var specifier = GetModuleSpecifierForImportOrExport(node);
        ISymbol? exportDefaultSymbol = null;
        ISymbol? exportModuleDotExportsSymbol = null;
        if (Utilities.IsShorthandAmbientModuleSymbol(moduleSymbol))
        {
            exportDefaultSymbol = moduleSymbol;
        }
        else if (
            file is not null && specifier is not null
            && ModuleKind.Node20 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext
            && GetEmitSyntaxForModuleSpecifierExpression(specifier) == ModuleKind.CommonJS
            && _host.GetImpliedNodeFormatForEmit((SourceFile)file) is ModuleKind.ESNext
            && (exportModuleDotExportsSymbol = ResolveExportByName(moduleSymbol, "module.exports", node, dontResolveAlias)) is not null
        )
        {
            // We have a transpiled default import where the `require` resolves to an ES module with a `module.exports` named
            // export. If `esModuleInterop` is enabled, this will work:
            //
            // const dep_1 = __importDefault(require("./dep.mjs")); // wraps like { default: require("./dep.mjs") }
            // dep_1.default; // require("./dep.mjs") -> the `module.exports` export value
            //
            // But without `esModuleInterop`, it will be broken:
            //
            // const dep_1 = require("./dep.mjs"); // the `module.exports` export value (could be primitive)
            // dep_1.default; // `default` property access on the `module.exports` export value
            //
            // We could try to resolve the 'default' property in the latter case, but it's a mistake to run in this
            // environment without `esModuleInterop`, so just error.
            if (!Utilities.GetESModuleInterop(_compilerOptions))
            {
                Error(((INamedDeclaration)node).Name!, Diagnostics.Module_0_can_only_be_default_imported_using_the_1_flag, Diagnostic.DeferArg(() => SymbolToString(moduleSymbol)), "esModuleInterop");
                return null;
            }

            MarkSymbolOfAliasDeclarationIfTypeOnly(node, exportModuleDotExportsSymbol, /*finalTarget*/ null, /*overwriteEmpty*/ false);
            return exportModuleDotExportsSymbol;
        }
        else
        {
            exportDefaultSymbol = ResolveExportByName(moduleSymbol, InternalSymbolName.Default.Value, node, dontResolveAlias);
        }

        if (specifier is null)
        {
            return exportDefaultSymbol;
        }

        var hasDefaultOnly = IsOnlyImportableAsDefault(specifier, moduleSymbol);
        var hasSyntheticDefault = CanHaveSyntheticDefault((SourceFile?)file, moduleSymbol, dontResolveAlias, specifier);
        if (exportDefaultSymbol is null && !hasSyntheticDefault && !hasDefaultOnly)
        {
            if (HasExportAssignmentSymbol(moduleSymbol) && !_allowSyntheticDefaultImports)
            {
                var compilerOptionName = _moduleKind >= ModuleKind.ES2015 ? "allowSyntheticDefaultImports" : "esModuleInterop";
                var exportEqualsSymbol = moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolName.ExportEquals.Value);
                var exportAssignment = exportEqualsSymbol?.ValueDeclaration;
                var err = Error(((INamedDeclaration)node).Name!, Diagnostics.Module_0_can_only_be_default_imported_using_the_1_flag, Diagnostic.DeferArg(() => SymbolToString(moduleSymbol)), compilerOptionName);

                if (exportAssignment is not null)
                {
                    Utilities.AddRelatedInfo(
                        err,
                        Utilities.CreateDiagnosticForNode(
                            (Node)exportAssignment,
                            Diagnostics.This_module_is_declared_with_export_and_can_only_be_used_with_a_default_import_when_using_the_0_flag,
                            compilerOptionName));
                }
            }
            else if (NodeTests.IsImportClause((Node)node))
            {
                ReportNonDefaultExport(moduleSymbol, node);
            }
            else
            {
                ErrorNoModuleMemberSymbol(moduleSymbol, moduleSymbol, (Node)node, ((INamedDeclaration)node).Name!);
            }
        }
        else if (hasSyntheticDefault || hasDefaultOnly)
        {
            // per emit behavior, a synthetic default overrides a "real" .default member if `__esModule` is not present
            var resolved = ResolveExternalModuleSymbol(moduleSymbol, dontResolveAlias) ?? ResolveSymbol(moduleSymbol, dontResolveAlias);
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, moduleSymbol, resolved, /*overwriteEmpty*/ false);
            return resolved;
        }

        MarkSymbolOfAliasDeclarationIfTypeOnly(node, exportDefaultSymbol, /*finalTarget*/ null, /*overwriteEmpty*/ false);
        return exportDefaultSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:3953-3977 — <c>reportNonDefaultExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>moduleSymbol.exports?.has(node.symbol.escapedName)</c> is the port's
    /// <c>ContainsKey</c> over the clause's own symbol, <c>exports?.get(ExportStar)</c> is the <c>GetValueOrDefault</c>
    /// pair, and the <c>find</c> over the export-star declarations is the two-argument <c>Core.Find</c>; the related
    /// information is the <c>Utilities.X</c> pair.
    /// </remarks>
    private void ReportNonDefaultExport(ISymbol moduleSymbol, IDeclaration node)
    {
        var nodeSymbol = GetSymbolOfDeclaration(node);
        if (moduleSymbol.Exports?.ContainsKey(nodeSymbol.EscapedName!) == true)
        {
            Error(
                ((INamedDeclaration)node).Name!,
                Diagnostics.Module_0_has_no_default_export_Did_you_mean_to_use_import_1_from_0_instead,
                Diagnostic.DeferArg(() => SymbolToString(moduleSymbol)),
                Diagnostic.DeferArg(() => SymbolToString(nodeSymbol)));
        }
        else
        {
            var diagnostic = Error(((INamedDeclaration)node).Name!, Diagnostics.Module_0_has_no_default_export, Diagnostic.DeferArg(() => SymbolToString(moduleSymbol)));
            var exportStar = moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolName.ExportStar.Value);
            if (exportStar is not null)
            {
                var defaultExport = Core.Find(exportStar.Declarations, (d, _) =>
                    NodeTests.IsExportDeclaration((Node)d) && ((ExportDeclaration)d).ModuleSpecifier is not null
                    && ResolveExternalModuleName((Node)d, ((ExportDeclaration)d).ModuleSpecifier)?.Exports?.ContainsKey(InternalSymbolName.Default.Value) == true);
                if (defaultExport is not null)
                {
                    Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode((Node)defaultExport, Diagnostics.export_Asterisk_does_not_re_export_a_default));
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:3979-3985 — <c>getTargetOfNamespaceImport</c>.</summary>
    private ISymbol? GetTargetOfNamespaceImport(IDeclaration node, bool dontResolveAlias)
    {
        var moduleSpecifier = ((ImportDeclaration)((Node)node).Parent!.Parent!).ModuleSpecifier;
        var immediate = ResolveExternalModuleName((Node)node, moduleSpecifier);
        var resolved = ResolveESModuleSymbol(immediate, moduleSpecifier!, dontResolveAlias, /*suppressInteropError*/ false);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, immediate, resolved, /*overwriteEmpty*/ false);
        return resolved;
    }

    /// <summary>ported from: src/compiler/checker.ts:3987-3993 — <c>getTargetOfNamespaceExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>moduleSpecifier &amp;&amp; resolveExternalModuleName(…)</c> chain is the port's
    /// explicit null tests over the same slot, because the port's <c>ResolveExternalModuleName</c> takes a nullable
    /// expression.
    /// </remarks>
    private ISymbol? GetTargetOfNamespaceExport(IDeclaration node, bool dontResolveAlias)
    {
        var moduleSpecifier = ((ExportDeclaration)((Node)node).Parent!).ModuleSpecifier;
        var immediate = moduleSpecifier is not null ? ResolveExternalModuleName((Node)node, moduleSpecifier) : null;
        var resolved = moduleSpecifier is not null ? ResolveESModuleSymbol(immediate, moduleSpecifier, dontResolveAlias, /*suppressInteropError*/ false) : null;
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, immediate, resolved, /*overwriteEmpty*/ false);
        return resolved;
    }

    /// <summary>ported from: src/compiler/checker.ts:4013-4028 — <c>combineValueAndTypeSymbols</c>.</summary>
    /// <remarks>
    /// This function creates a synthetic symbol that combines the value side of one symbol with the type/namespace side of
    /// another symbol.
    /// <para>
    /// PORT-NOTE: <c>unknownSymbol</c> is <c>_unknownSymbol</c>; <c>deduplicate(concatenate(…), equateValues)</c> is
    /// <c>Core.Deduplicate(Core.Concatenate(…), Core.EquateValues)</c> (the port's <c>EqualityComparer&lt;T&gt;</c> is a
    /// delegate, and <c>equateValues</c> is <c>Core.EquateValues</c>); <c>new Map(x)</c> is the port's
    /// <c>SymbolTable</c> copy constructor; and the reference's <c>Debug.assert(valueSymbol.declarations ||
    /// typeSymbol.declarations)</c> is stated as the same assertion.
    /// </para>
    /// </remarks>
    private ISymbol CombineValueAndTypeSymbols(ISymbol valueSymbol, ISymbol typeSymbol)
    {
        if (ReferenceEquals(valueSymbol, _unknownSymbol) && ReferenceEquals(typeSymbol, _unknownSymbol))
        {
            return _unknownSymbol;
        }

        if ((valueSymbol.Flags & (SymbolFlags.Type | SymbolFlags.Namespace)) != 0)
        {
            return valueSymbol;
        }

        var result = CreateSymbol(valueSymbol.Flags | typeSymbol.Flags, valueSymbol.EscapedName!);
        Debug.Assert(valueSymbol.Declarations is not null || typeSymbol.Declarations is not null, "one of the two symbols has declarations");
        result.Declarations = [.. Core.Deduplicate(Core.Concatenate(valueSymbol.Declarations, typeSymbol.Declarations) ?? [], Core.EquateValues)];
        result.Parent = valueSymbol.Parent ?? typeSymbol.Parent;
        if (valueSymbol.ValueDeclaration is not null) result.ValueDeclaration = valueSymbol.ValueDeclaration;
        if (typeSymbol.Members is not null) result.Members = new SymbolTable(typeSymbol.Members);
        if (valueSymbol.Exports is not null) result.Exports = new SymbolTable(valueSymbol.Exports);
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:4030-4038 — <c>getExportOfModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getExportsOfSymbol(symbol).get(nameText)</c> is the port's <c>GetValueOrDefault</c> and the link's
    /// <c>typeOnlyExportStarMap?.get(nameText)</c> is the same lookup over the star map; the four-argument
    /// <c>markSymbolOfAliasDeclarationIfTypeOnly</c> is called with the reference's six arguments (the last two are the
    /// star declaration and name).
    /// </remarks>
    private ISymbol? GetExportOfModule(ISymbol symbol, string nameText, IDeclaration? specifier, bool dontResolveAlias)
    {
        if ((symbol.Flags & SymbolFlags.Module) != 0)
        {
            var exportSymbol = GetExportsOfSymbol(symbol).GetValueOrDefault(nameText);
            var resolved = ResolveSymbol(exportSymbol, dontResolveAlias);
            var exportStarDeclaration = GetSymbolLinks(symbol).TypeOnlyExportStarMap?.GetValueOrDefault(nameText);
            MarkSymbolOfAliasDeclarationIfTypeOnly(specifier, exportSymbol, resolved, /*overwriteEmpty*/ false, exportStarDeclaration, nameText);
            return resolved;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4040-4047 — <c>getPropertyOfVariable</c>.</summary>
    private ISymbol? GetPropertyOfVariable(ISymbol symbol, string name)
    {
        if ((symbol.Flags & SymbolFlags.Variable) != 0)
        {
            var typeAnnotation = symbol.ValueDeclaration is not null ? ((VariableDeclaration)symbol.ValueDeclaration).Type : null;
            if (typeAnnotation is not null)
            {
                return ResolveSymbol(GetPropertyOfType(GetTypeFromTypeNode(typeAnnotation), name));
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4104-4128 — <c>errorNoModuleMemberSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>moduleName</c> is <see cref="GetFullyQualifiedName"/>, the declaration name is
    /// <c>Utilities.DeclarationNameToString</c>, the suggestion is <c>GetSuggestedSymbolForNonexistentModule</c> (the
    /// inference band's member of that name) and its name is <c>SymbolToString</c>; <c>moduleSymbol.exports?.has(Default)</c>
    /// is the port's <c>ContainsKey</c> test.
    /// </remarks>
    private void ErrorNoModuleMemberSymbol(ISymbol moduleSymbol, ISymbol targetSymbol, Node node, Node name)
    {
        var moduleName = GetFullyQualifiedName(moduleSymbol, node);
        var declarationName = Utilities.DeclarationNameToString(name);
        var suggestion = NodeTests.IsIdentifier(name) ? GetSuggestedSymbolForNonexistentModule((Identifier)name, targetSymbol) : null;
        if (suggestion is not null)
        {
            var suggestionName = Diagnostic.DeferArg(() => SymbolToString(suggestion));
            var diagnostic = Error(name, Diagnostics._0_has_no_exported_member_named_1_Did_you_mean_2, moduleName, declarationName, suggestionName);
            if (suggestion.ValueDeclaration is not null)
            {
                Utilities.AddRelatedInfo(diagnostic, Utilities.CreateDiagnosticForNode((Node)suggestion.ValueDeclaration, Diagnostics._0_is_declared_here, suggestionName));
            }
        }
        else
        {
            if (moduleSymbol.Exports?.ContainsKey(InternalSymbolName.Default.Value) == true)
            {
                Error(
                    name,
                    Diagnostics.Module_0_has_no_exported_member_1_Did_you_mean_to_use_import_1_from_0_instead,
                    moduleName,
                    declarationName);
            }
            else
            {
                ReportNonExportedMember(node, name, declarationName, moduleSymbol, moduleName);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:4130-4151 — <c>reportNonExportedMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>tryCast(moduleSymbol.valueDeclaration, canHaveLocals)?.locals?.get(…)</c> is the port's
    /// <c>CanHaveLocals</c>/<c>ILocalsContainer</c> pair plus <c>GetValueOrDefault</c>; <c>getSymbolIfSameReference</c> is
    /// the types band's member of that name (called by reference); the <c>map(declarations, (decl, index) =&gt; …)</c> is a
    /// <c>Select</c> over the same index-bearing projection, handed to <c>Utilities.AddRelatedInfo</c>, whose
    /// <c>params</c> array accepts the whole list.
    /// </remarks>
    private void ReportNonExportedMember(Node node, Node name, string declarationName, ISymbol moduleSymbol, string moduleName)
    {
        var valueDeclaration = moduleSymbol.ValueDeclaration;
        var localSymbols = valueDeclaration is not null && Utilities.CanHaveLocals((Node)valueDeclaration) ? ((ILocalsContainer)valueDeclaration).Locals : null;
        var localSymbol = localSymbols?.GetValueOrDefault(Utilities.ModuleExportNameTextEscaped(name));
        var exports = moduleSymbol.Exports;
        if (localSymbol is not null)
        {
            var exportedEqualsSymbol = exports?.GetValueOrDefault(InternalSymbolName.ExportEquals.Value);
            if (exportedEqualsSymbol is not null)
            {
                if (GetSymbolIfSameReference(exportedEqualsSymbol, localSymbol) is not null)
                {
                    ReportInvalidImportEqualsExportMember(node, name, declarationName, moduleName);
                }
                else
                {
                    Error(name, Diagnostics.Module_0_has_no_exported_member_1, moduleName, declarationName);
                }
            }
            else
            {
                var exportedSymbol = exports is not null ? Core.Find(SymbolsToArray(exports), (s, _) => GetSymbolIfSameReference(s, localSymbol) is not null) : null;
                var diagnostic = exportedSymbol is not null
                    ? Error(name, Diagnostics.Module_0_declares_1_locally_but_it_is_exported_as_2, moduleName, declarationName, Diagnostic.DeferArg(() => SymbolToString(exportedSymbol)))
                    : Error(name, Diagnostics.Module_0_declares_1_locally_but_it_is_not_exported, moduleName, declarationName);
                if (localSymbol.Declarations is not null)
                {
                    Utilities.AddRelatedInfo(
                        diagnostic,
                        [.. localSymbol.Declarations.Select((decl, index) => Utilities.CreateDiagnosticForNode((Node)decl, index == 0 ? Diagnostics._0_is_declared_here : Diagnostics.and_here, declarationName))]);
                }
            }
        }
        else
        {
            Error(name, Diagnostics.Module_0_has_no_exported_member_1, moduleName, declarationName);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:4153-4171 — <c>reportInvalidImportEqualsExportMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>moduleKind &gt;= ModuleKind.ES2015</c> is the port's <c>_moduleKind</c> test,
    /// <c>getESModuleInterop(compilerOptions)</c> is <c>Utilities.GetESModuleInterop</c>, and <c>isInJSFile(node)</c> is
    /// <c>Utilities.IsInJSFile</c>.
    /// </remarks>
    private void ReportInvalidImportEqualsExportMember(Node node, Node name, string declarationName, string moduleName)
    {
        if (_moduleKind >= ModuleKind.ES2015)
        {
            var message = Utilities.GetESModuleInterop(_compilerOptions)
                ? Diagnostics._0_can_only_be_imported_by_using_a_default_import
                : Diagnostics._0_can_only_be_imported_by_turning_on_the_esModuleInterop_flag_and_using_a_default_import;
            Error(name, message, declarationName);
        }
        else
        {
            if (Utilities.IsInJSFile(node))
            {
                var message = Utilities.GetESModuleInterop(_compilerOptions)
                    ? Diagnostics._0_can_only_be_imported_by_using_a_require_call_or_by_using_a_default_import
                    : Diagnostics._0_can_only_be_imported_by_using_a_require_call_or_by_turning_on_the_esModuleInterop_flag_and_using_a_default_import;
                Error(name, message, declarationName);
            }
            else
            {
                var message = Utilities.GetESModuleInterop(_compilerOptions)
                    ? Diagnostics._0_can_only_be_imported_by_using_import_1_require_2_or_a_default_import
                    : Diagnostics._0_can_only_be_imported_by_using_import_1_require_2_or_by_turning_on_the_esModuleInterop_flag_and_using_a_default_import;
                Error(name, message, declarationName, declarationName, moduleName);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:4173-4190 — <c>getTargetOfImportSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ImportSpecifier | BindingElement</c> union, stated as the bound
    /// <see cref="Node"/> and narrowed by the reference's own kind tests; the <c>propertyName || name</c> read is a switch
    /// over the two specifier faces; the <c>root</c> local is the reference's conditional (the binding element arm goes
    /// through <c>Utilities.GetRootDeclaration</c>); and <c>getExternalModuleMember(root, commonJSPropertyAccess || node,
    /// dontResolveAlias)</c> keeps the reference's short-circuit with <c>??</c>.
    /// </remarks>
    private ISymbol? GetTargetOfImportSpecifier(Node node, bool dontResolveAlias)
    {
        if (NodeTests.IsImportSpecifier(node) && Utilities.ModuleExportNameIsDefault(((ImportSpecifier)node).PropertyName ?? ((ImportSpecifier)node).Name!))
        {
            var specifier = GetModuleSpecifierForImportOrExport((IDeclaration)node);
            var moduleSymbol = specifier is not null ? ResolveExternalModuleName(node, specifier) : null;
            if (moduleSymbol is not null)
            {
                return GetTargetofModuleDefault(moduleSymbol, (IDeclaration)node, dontResolveAlias);
            }
        }

        Node root = NodeTests.IsBindingElement(node) ? (Node)Utilities.GetRootDeclaration(node) : ((Node)node).Parent!.Parent!.Parent!;
        var commonJSPropertyAccess = GetCommonJSPropertyAccess(root);
        var resolved = GetExternalModuleMember(root, (Node?)commonJSPropertyAccess ?? node, dontResolveAlias);
        var name = NodeTests.IsImportSpecifier(node)
            ? (Node?)((ImportSpecifier)node).PropertyName ?? ((ImportSpecifier)node).Name
            : ((BindingElement)node).Name;
        if (commonJSPropertyAccess is not null && resolved is not null && NodeTests.IsIdentifier(name!))
        {
            return ResolveSymbol(GetPropertyOfType(GetTypeOfSymbol(resolved), ((Identifier)name!).EscapedText), dontResolveAlias);
        }

        MarkSymbolOfAliasDeclarationIfTypeOnly((IDeclaration)node, /*immediateTarget*/ null, resolved, /*overwriteEmpty*/ false);
        return resolved;
    }

    /// <summary>ported from: src/compiler/checker.ts:4198-4204 — <c>getTargetOfNamespaceExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>canHaveSymbol(node.parent)</c> is <c>Utilities.CanHaveSymbol</c> over the reference's declaration
    /// face, and <c>node.parent.symbol</c> is the port's <c>Symbol</c> read on that parent.
    /// </remarks>
    private ISymbol? GetTargetOfNamespaceExportDeclaration(IDeclaration node, bool dontResolveAlias)
    {
        if (Utilities.CanHaveSymbol(((Node)node).Parent!))
        {
            var resolved = ResolveExternalModuleSymbol(((IDeclaration)((Node)node).Parent!).Symbol!, dontResolveAlias);
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, /*immediateTarget*/ null, resolved, /*overwriteEmpty*/ false);
            return resolved;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4206-4221 — <c>getTargetOfExportSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>name = node.propertyName || node.name</c> is the port's <c>??</c> over the <c>ExportSpecifier</c>
    /// faces; <c>moduleExportNameIsDefault</c> is the landed utilities member; <c>node.parent.parent.moduleSpecifier</c>
    /// is the enclosing <c>ExportDeclaration</c>'s slot; the reference's <c>name.kind === StringLiteral ? undefined : …</c>
    /// is the port's conditional over the same kind; and <c>!!dontResolveAlias</c> is <c>Js.IsTruthy</c>.
    /// <para>
    /// PORT-NOTE: <c>getExternalModuleMember(node.parent.parent, node, …)</c> passes the *enclosing declaration* first and
    /// the specifier second (checker.ts:4216); the port had those two roles swapped, so a named re-export resolved its
    /// module with `node` = the `ExportSpecifier` (whose `specifier` switch answers no name) — silently no resolution,
    /// no `Cannot find module` and no member lookup.
    /// </para>
    /// </remarks>
    private ISymbol? GetTargetOfExportSpecifier(IDeclaration node, SymbolFlags meaning, bool? dontResolveAlias = null)
    {
        var name = (Node?)((ExportSpecifier)node).PropertyName ?? ((ExportSpecifier)node).Name;
        if (Utilities.ModuleExportNameIsDefault(name!))
        {
            var specifier = GetModuleSpecifierForImportOrExport(node);
            var moduleSymbol = specifier is not null ? ResolveExternalModuleName((Node)node, specifier) : null;
            if (moduleSymbol is not null)
            {
                return GetTargetofModuleDefault(moduleSymbol, node, Js.IsTruthy(dontResolveAlias));
            }
        }

        var exportDeclaration = (Node)((Node)node).Parent!.Parent!;
        var resolved = ((ExportDeclaration)exportDeclaration).ModuleSpecifier is not null
            ? GetExternalModuleMember(exportDeclaration, (Node)node, Js.IsTruthy(dontResolveAlias))
            : name!.Kind == SyntaxKind.StringLiteral ? null : // Skip for invalid syntax like this: export { "x" }
            ResolveEntityName(name, meaning, /*ignoreErrors*/ false, dontResolveAlias);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, /*immediateTarget*/ null, resolved, /*overwriteEmpty*/ false);
        return resolved;
    }

    /// <summary>ported from: src/compiler/checker.ts:4223-4228 — <c>getTargetOfExportAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ExportAssignment | BinaryExpression</c> union is the port's <see cref="Node"/>, and the
    /// <c>node.expression : node.right</c> choice is the reference's kind test over the two faces.
    /// </remarks>
    private ISymbol? GetTargetOfExportAssignment(Node node, bool dontResolveAlias)
    {
        Node? expression = NodeTests.IsExportAssignment(node) ? ((ExportAssignment)node).Expression : ((BinaryExpression)node).Right;
        var resolved = expression is not null ? GetTargetOfAliasLikeExpression(expression, dontResolveAlias) : null;
        MarkSymbolOfAliasDeclarationIfTypeOnly((IDeclaration)node, /*immediateTarget*/ null, resolved, /*overwriteEmpty*/ false);
        return resolved;
    }

    /// <summary>ported from: src/compiler/checker.ts:4230-4243 — <c>getTargetOfAliasLikeExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isEntityName(expression) || isEntityNameExpression(expression)</c> is the port's
    /// <c>Utilities.IsEntityName</c>/<c>Utilities.IsEntityNameExpression</c> pair over the expression face, and
    /// <c>getNodeLinks(expression).resolvedSymbol</c> is <c>NodeLinks.ResolvedSymbol</c>.
    /// </remarks>
    private ISymbol? GetTargetOfAliasLikeExpression(Node expression, bool dontResolveAlias)
    {
        if (NodeTests.IsClassExpression(expression))
        {
            return CheckExpressionCached(expression).Symbol;
        }

        if (!Utilities.IsEntityName(expression) && !Utilities.IsEntityNameExpression(expression))
        {
            return null;
        }

        var aliasLike = ResolveEntityName(expression, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, /*ignoreErrors*/ true, dontResolveAlias);
        if (aliasLike is not null)
        {
            return aliasLike;
        }

        CheckExpressionCached(expression);
        return GetNodeLinks(expression).ResolvedSymbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:4245-4251 — <c>getTargetOfAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>AccessExpression</c> union is the port's <see cref="Node"/>; the parent guard is the
    /// reference's own binary-expression/left/EqualsToken triple.
    /// </remarks>
    private ISymbol? GetTargetOfAccessExpression(Node node, bool dontRecursivelyResolve)
    {
        if (!(NodeTests.IsBinaryExpression(node.Parent!) && ReferenceEquals(((BinaryExpression)node.Parent!).Left, node) && ((BinaryExpression)node.Parent!).OperatorToken!.Kind == SyntaxKind.EqualsToken))
        {
            return null;
        }

        return GetTargetOfAliasLikeExpression(((BinaryExpression)node.Parent!).Right!, dontRecursivelyResolve);
    }

    /// <summary>ported from: src/compiler/checker.ts:4253-4284 — <c>getTargetOfAliasDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Declaration</c> parameter is the port's <see cref="IDeclaration"/> (the four landed call sites
    /// hold declarations) and the switch's arms call this file's private members of the same names — the
    /// <c>ImportEquals | Variable</c> arm, the clause/namespace/specifier/export-specifier arms, the
    /// <c>ExportAssignment | BinaryExpression</c> arm and the shorthand/property/element-access arms. The reference's
    /// <c>SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace</c> meaning is kept verbatim.
    /// </remarks>
    internal ISymbol? GetTargetOfAliasDeclaration(IDeclaration node, bool dontRecursivelyResolve = false)
    {
        switch (((Node)node).Kind)
        {
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.VariableDeclaration:
                return GetTargetOfImportEqualsDeclaration((Node)node, dontRecursivelyResolve);
            case SyntaxKind.ImportClause:
                return GetTargetOfImportClause(node, dontRecursivelyResolve);
            case SyntaxKind.NamespaceImport:
                return GetTargetOfNamespaceImport(node, dontRecursivelyResolve);
            case SyntaxKind.NamespaceExport:
                return GetTargetOfNamespaceExport(node, dontRecursivelyResolve);
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.BindingElement:
                return GetTargetOfImportSpecifier((Node)node, dontRecursivelyResolve);
            case SyntaxKind.ExportSpecifier:
                return GetTargetOfExportSpecifier(node, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, dontRecursivelyResolve);
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.BinaryExpression:
                return GetTargetOfExportAssignment((Node)node, dontRecursivelyResolve);
            case SyntaxKind.NamespaceExportDeclaration:
                return GetTargetOfNamespaceExportDeclaration(node, dontRecursivelyResolve);
            case SyntaxKind.ShorthandPropertyAssignment:
                return ResolveEntityName(((ShorthandPropertyAssignment)node).Name!, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, /*ignoreErrors*/ true, dontRecursivelyResolve);
            case SyntaxKind.PropertyAssignment:
                return GetTargetOfAliasLikeExpression(((PropertyAssignment)node).Initializer!, dontRecursivelyResolve);
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.PropertyAccessExpression:
                return GetTargetOfAccessExpression((Node)node, dontRecursivelyResolve);
            default:
                return Debug.Fail<ISymbol?>("only alias declarations reach this switch");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:4638-4645 — <c>resolveEntityNameFromAssignmentDeclaration</c>.</summary>
    /// <remarks>
    /// 1. For prototype-property methods like <c>A.prototype.m = function () ...</c>, try to resolve names in the scope of
    /// <c>A</c> too. Note that prototype-property assignment to locations outside the current file (eg globals) doesn't
    /// work, so name resolution won't work either.
    /// 2. For property assignments like <c>{ x: function f () { } }</c>, try to resolve names in the scope of <c>f</c> too.
    /// <para>
    /// PORT-NOTE: <c>resolveName(secondaryLocation, name, meaning, undefined, true)</c> is the port's six-argument
    /// <c>ResolveName</c> (the reference's <c>name</c> is an <c>Identifier</c>, whose escaped text is the name string).
    /// </para>
    /// </remarks>
    internal ISymbol? ResolveEntityNameFromAssignmentDeclaration(Identifier name, SymbolFlags meaning)
    {
        if (IsJSDocTypeReference((Node)name.Parent!))
        {
            var secondaryLocation = GetAssignmentDeclarationLocation((TypeReferenceNode)(Node)name.Parent!);
            if (secondaryLocation is not null)
            {
                return ResolveName(secondaryLocation, name.EscapedText!, meaning, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4647-4683 — <c>getAssignmentDeclarationLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's quit-capable <c>findAncestor</c> visitor is the port's <c>bool?</c> overload
    /// (<c>null</c> is <c>"quit"</c>), and <c>getSymbolOfDeclaration(host.expression.left)</c> reads the same declaration
    /// face the rest of this file does; <c>getJSDocHost</c>/<c>getEffectiveJSDocHost</c> are the landed
    /// <c>Utilities</c> members of those names.
    /// </remarks>
    internal Node? GetAssignmentDeclarationLocation(TypeReferenceNode node)
    {
        var typeAlias = Utilities.FindAncestor((Node)node, n => !(Utilities.IsJSDocNode(n) || (n.Flags & NodeFlags.JSDoc) != 0) ? null : Utilities.IsJSDocTypeAlias(n));
        if (typeAlias is not null)
        {
            return null;
        }

        var host = Utilities.GetJSDocHost((Node)node);
        if (host is not null && NodeTests.IsExpressionStatement(host) && Utilities.IsPrototypePropertyAssignment(((ExpressionStatement)host).Expression!))
        {
            // /** @param {K} p */ X.prototype.m = function () { } <-- look for K on X's declaration
            var symbol = GetSymbolOfDeclaration((IDeclaration)((BinaryExpression)((ExpressionStatement)host).Expression!).Left!);
            return GetDeclarationOfJSPrototypeContainer(symbol);
        }

        if (host is not null && NodeTests.IsFunctionExpression(host) && Utilities.IsPrototypePropertyAssignment(host.Parent!) && NodeTests.IsExpressionStatement(host.Parent!.Parent!))
        {
            // X.prototype.m = /** @param {K} p */ function () { } <-- look for K on X's declaration
            var symbol = GetSymbolOfDeclaration((IDeclaration)((BinaryExpression)host.Parent!).Left!);
            return GetDeclarationOfJSPrototypeContainer(symbol);
        }

        if (
            host is not null && (Utilities.IsObjectLiteralMethod(host) || NodeTests.IsPropertyAssignment(host))
            && NodeTests.IsBinaryExpression(host.Parent!.Parent!)
            && Utilities.GetAssignmentDeclarationKind(host.Parent!.Parent!) == AssignmentDeclarationKind.Prototype
        )
        {
            // X.prototype = { /** @param {K} p */m() { } } <-- look for K on X's declaration
            var symbol = GetSymbolOfDeclaration((IDeclaration)((BinaryExpression)host.Parent!.Parent!).Left!);
            return GetDeclarationOfJSPrototypeContainer(symbol);
        }

        var sig = Utilities.GetEffectiveJSDocHost((Node)node);
        if (sig is not null && Utilities.IsFunctionLike(sig))
        {
            var symbol = GetSymbolOfDeclaration((IDeclaration)sig);
            return symbol.ValueDeclaration is not null ? (Node?)symbol.ValueDeclaration : null;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4685-4694 — <c>getDeclarationOfJSPrototypeContainer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>isAssignmentDeclaration(decl) ? … : hasOnlyExpressionInitializer(decl) ? … :
    /// undefined</c> chain is the port's conditional with <c>null</c>, and <c>getAssignedExpandoInitializer</c>/
    /// <c>getDeclaredExpandoInitializer</c> are the landed utilities members of those names.
    /// </remarks>
    internal Node? GetDeclarationOfJSPrototypeContainer(ISymbol symbol)
    {
        var declaration = symbol.Parent!.ValueDeclaration;
        if (declaration is null)
        {
            return null;
        }

        var initializer = Utilities.IsAssignmentDeclaration(declaration) ? Utilities.GetAssignedExpandoInitializer((Node)declaration)
            : Utilities.HasOnlyExpressionInitializer((Node)declaration) ? Utilities.GetDeclaredExpandoInitializer((Node)declaration)
            : null;
        return initializer ?? (Node)declaration;
    }

    /// <summary>ported from: src/compiler/checker.ts:4049-4102 — <c>getExternalModuleMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two union parameters (<c>ImportDeclaration | ExportDeclaration | VariableDeclaration |
    /// JSDocImportTag</c> and <c>ImportOrExportSpecifier | BindingElement | PropertyAccessExpression</c>) are the port's
    /// bound <see cref="Node"/>, discriminated by the reference's own kind tests; <c>getExternalModuleRequireArgument(node)
    /// || (node as …).moduleSpecifier!</c> is the port's <c>??</c> over the three concrete <c>ModuleSpecifier</c> slots
    /// (<c>Check.Modules.cs</c>'s literal-read switch is the same shape), <c>nameText</c> is
    /// <c>Utilities.ModuleExportNameTextEscaped</c>, and <c>moduleSymbol.exports.get(ExportEquals)</c> is the port's
    /// <c>ContainsKey</c>. The reference hands the raw <c>specifier</c> to <c>getExportOfModule</c>, whose alias marking
    /// answers false immediately for the one specifier face that is not a declaration (a property access), so the port
    /// passes the declaration face or null — the same answer, no side effect skipped.
    /// </remarks>
    internal ISymbol? GetExternalModuleMember(Node node, Node specifier, bool dontResolveAlias = false)
    {
        var moduleSpecifier = Utilities.GetExternalModuleRequireArgument(node) ?? node switch
        {
            ImportDeclaration importDeclaration => importDeclaration.ModuleSpecifier,
            ExportDeclaration exportDeclaration => exportDeclaration.ModuleSpecifier,
            JSDocImportTag jsDocImportTag => jsDocImportTag.ModuleSpecifier,
            _ => null,
        };
        var moduleSymbol = ResolveExternalModuleName(node, moduleSpecifier)!; // TODO: GH#18217
        Node? name = specifier switch
        {
            ImportSpecifier importSpecifier => (Node?)importSpecifier.PropertyName ?? importSpecifier.Name,
            ExportSpecifier exportSpecifier => (Node?)exportSpecifier.PropertyName ?? exportSpecifier.Name,
            BindingElement bindingElement => bindingElement.Name,
            PropertyAccessExpression propertyAccess => propertyAccess.Name,
            _ => null,
        };
        if (name is null || (!NodeTests.IsIdentifier(name) && name.Kind != SyntaxKind.StringLiteral))
        {
            return null;
        }

        var nameText = Utilities.ModuleExportNameTextEscaped(name);
        var suppressInteropError = nameText == InternalSymbolName.Default.Value && _allowSyntheticDefaultImports;
        var targetSymbol = ResolveESModuleSymbol(moduleSymbol, moduleSpecifier!, /*dontResolveAlias*/ false, suppressInteropError);
        if (targetSymbol is not null)
        {
            // Note: The empty string is a valid module export name:
            //
            //   import { "" as foo } from "./foo";
            //   export { foo as "" };
            //
            if (nameText.Length > 0 || name.Kind == SyntaxKind.StringLiteral)
            {
                if (Utilities.IsShorthandAmbientModuleSymbol(moduleSymbol!))
                {
                    return moduleSymbol;
                }

                ISymbol? symbolFromVariable;
                // First check if module was specified with "export=". If so, get the member from the resolved type
                if (moduleSymbol is not null && moduleSymbol.Exports is not null && moduleSymbol.Exports.ContainsKey(InternalSymbolName.ExportEquals.Value))
                {
                    symbolFromVariable = GetPropertyOfType(GetTypeOfSymbol(targetSymbol), nameText, /*skipObjectFunctionPropertyAugment*/ true);
                }
                else
                {
                    symbolFromVariable = GetPropertyOfVariable(targetSymbol, nameText);
                }

                // if symbolFromVariable is export - get its final target
                symbolFromVariable = ResolveSymbol(symbolFromVariable, dontResolveAlias);

                var symbolFromModule = GetExportOfModule(targetSymbol, nameText, specifier as IDeclaration, dontResolveAlias);
                if (symbolFromModule is null && nameText == InternalSymbolName.Default.Value)
                {
                    var file = Core.Find(moduleSymbol!.Declarations, (d, _) => NodeTests.IsSourceFile((Node)d));
                    if (IsOnlyImportableAsDefault(moduleSpecifier!, moduleSymbol) || CanHaveSyntheticDefault((SourceFile?)file, moduleSymbol, dontResolveAlias, moduleSpecifier!))
                    {
                        symbolFromModule = ResolveExternalModuleSymbol(moduleSymbol, dontResolveAlias) ?? ResolveSymbol(moduleSymbol, dontResolveAlias);
                    }
                }

                var symbol = symbolFromModule is not null && symbolFromVariable is not null && !ReferenceEquals(symbolFromModule, symbolFromVariable)
                    ? CombineValueAndTypeSymbols(symbolFromVariable, symbolFromModule)
                    : symbolFromModule ?? symbolFromVariable;

                if (Utilities.IsImportOrExportSpecifier(specifier) && IsOnlyImportableAsDefault(moduleSpecifier!, moduleSymbol) && nameText != InternalSymbolName.Default.Value)
                {
                    Error(name, Diagnostics.Named_imports_from_a_JSON_file_into_an_ECMAScript_module_are_not_allowed_when_module_is_set_to_0, Enum.GetName(_moduleKind));
                }
                else if (symbol is null)
                {
                    ErrorNoModuleMemberSymbol(moduleSymbol!, targetSymbol, node, name);
                }

                return symbol;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:5026-5084 — <c>resolveESModuleSymbol</c>.</summary>
    /// <remarks>
    /// An external module with an 'export =' declaration may be referenced as an ES6 module provided the 'export ='
    /// references a symbol that is at least declared as a module or a variable. The target of the 'export =' may combine
    /// other declarations with the module or variable (e.g. a class/module, function/module, interface/variable).
    /// <para>
    /// PORT-NOTE: <c>getDeclarationOfKind(symbol, SyntaxKind.SourceFile)</c> is the <c>Utilities</c> member of that name,
    /// <c>getNamespaceDeclarationNode</c> is <c>Utilities.GetNamespaceDeclarationNode</c> (which answers the bound node
    /// face), <c>isImportCall(referenceParent)</c> is the landed utilities predicate, and the host's implied-format read
    /// is the <c>is ModuleKind.ESNext</c> pattern (or <c>as ModuleKind?</c> where the value is compared twice) this file
    /// already uses. <c>CloneTypeAsModuleType</c>, <c>HasSignatures</c> and the two synthetic-default type builders are
    /// the members of those names.
    /// </para>
    /// </remarks>
    private ISymbol? ResolveESModuleSymbol(ISymbol? moduleSymbol, Node referencingLocation, bool dontResolveAlias, bool suppressInteropError)
    {
        var symbol = ResolveExternalModuleSymbol(moduleSymbol, dontResolveAlias);

        if (!dontResolveAlias && symbol is not null)
        {
            if (!suppressInteropError && (symbol.Flags & (SymbolFlags.Module | SymbolFlags.Variable)) == 0 && Utilities.GetDeclarationOfKind(symbol, SyntaxKind.SourceFile) is null)
            {
                var compilerOptionName = _moduleKind >= ModuleKind.ES2015
                    ? "allowSyntheticDefaultImports"
                    : "esModuleInterop";

                Error(referencingLocation, Diagnostics.This_module_can_only_be_referenced_with_ECMAScript_imports_Slashexports_by_turning_on_the_0_flag_and_referencing_its_default_export, compilerOptionName);

                return symbol;
            }

            var referenceParent = referencingLocation.Parent!;
            var namespaceImport = NodeTests.IsImportDeclaration(referenceParent) ? Utilities.GetNamespaceDeclarationNode(referenceParent) : null;
            if (namespaceImport is not null || Utilities.IsImportCall(referenceParent))
            {
                var reference = Utilities.IsImportCall(referenceParent) ? ((CallExpression)referenceParent).Arguments![0] : ((ImportDeclaration)referenceParent).ModuleSpecifier!;
                var type = GetTypeOfSymbol(symbol);
                var defaultOnlyType = GetTypeWithSyntheticDefaultOnly(type, symbol, moduleSymbol!, (Expression)reference);
                if (defaultOnlyType is not null)
                {
                    return CloneTypeAsModuleType(symbol, defaultOnlyType, referenceParent);
                }

                var targetFile = Core.Find(moduleSymbol?.Declarations, (d, _) => NodeTests.IsSourceFile((Node)d));
                var usageMode = GetEmitSyntaxForModuleSpecifierExpression((Expression)reference);
                ISymbol? exportModuleDotExportsSymbol = null;
                if (
                    namespaceImport is not null && targetFile is not null
                    && ModuleKind.Node20 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext
                    && usageMode == ModuleKind.CommonJS && _host.GetImpliedNodeFormatForEmit((SourceFile)targetFile) is ModuleKind.ESNext
                    && (exportModuleDotExportsSymbol = ResolveExportByName(symbol, "module.exports", namespaceImport as IDeclaration, dontResolveAlias)) is not null)
                {
                    if (!suppressInteropError && (symbol.Flags & (SymbolFlags.Module | SymbolFlags.Variable)) == 0)
                    {
                        Error(referencingLocation, Diagnostics.This_module_can_only_be_referenced_with_ECMAScript_imports_Slashexports_by_turning_on_the_0_flag_and_referencing_its_default_export, "esModuleInterop");
                    }

                    if (Utilities.GetESModuleInterop(_compilerOptions) && HasSignatures(type))
                    {
                        return CloneTypeAsModuleType(exportModuleDotExportsSymbol, type, referenceParent);
                    }

                    return exportModuleDotExportsSymbol;
                }

                var isEsmCjsRef = targetFile is not null && IsESMFormatImportImportingCommonjsFormatFile(usageMode, _host.GetImpliedNodeFormatForEmit((SourceFile)targetFile) as ModuleKind?);
                if (Utilities.GetESModuleInterop(_compilerOptions) || isEsmCjsRef)
                {
                    if (
                        HasSignatures(type)
                        || GetPropertyOfType(type, InternalSymbolName.Default.Value, /*skipObjectFunctionPropertyAugment*/ true) is not null
                        || isEsmCjsRef
                    )
                    {
                        var moduleType = (type.Flags & TypeFlags.StructuredType) != 0
                            ? GetTypeWithSyntheticDefaultImportType(type, symbol, moduleSymbol!, (Expression)reference)
                            : (IType)CreateDefaultPropertyWrapperForModule(symbol, symbol.Parent);
                        return CloneTypeAsModuleType(symbol, moduleType, referenceParent);
                    }
                }
            }
        }

        return symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:5086-5088 — <c>hasSignatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>some(getSignaturesOfStructuredType(…))</c> is <c>Core.Some</c> over the port's list-returning member,
    /// whose null answer is the reference's empty array for the truthiness test.
    /// </remarks>
    private bool HasSignatures(IType type)
        => Core.Some(GetSignaturesOfStructuredType(type, SignatureKind.Call)) || Core.Some(GetSignaturesOfStructuredType(type, SignatureKind.Construct));

    /// <summary>ported from: src/compiler/checker.ts:5093-5106 — <c>cloneTypeAsModuleType</c>.</summary>
    /// <remarks>
    /// Create a new symbol which has the module's type less the call and construct signatures.
    /// <para>
    /// PORT-NOTE: the reference's <c>result.links.target</c>/<c>originatingImport</c>/<c>type</c> writes are the port's
    /// <c>GetSymbolLinks(result)</c> slots (the split symbol-links table), <c>symbol.declarations.slice()</c> is the
    /// list copy, <c>new Map(x)</c> is the <c>SymbolTable</c> copy constructor, and
    /// <c>resolveStructuredTypeMembers(moduleType as StructuredType).members</c> is the port's
    /// <c>(UnionOrIntersectionType)</c> face with the same two slots (the same treatment
    /// <c>ForEachPropertyOfType</c> documents).
    /// </para>
    /// </remarks>
    private ISymbol CloneTypeAsModuleType(ISymbol symbol, IType moduleType, Node referenceParent)
    {
        var result = CreateSymbol(symbol.Flags, symbol.EscapedName!);
        result.Declarations = symbol.Declarations is not null ? [.. symbol.Declarations] : [];
        result.Parent = symbol.Parent;
        GetSymbolLinks(result).Target = symbol;
        GetSymbolLinks(result).OriginatingImport = referenceParent;
        if (symbol.ValueDeclaration is not null) result.ValueDeclaration = symbol.ValueDeclaration;
        if (Js.IsTruthy(symbol.ConstEnumOnlyModule)) result.ConstEnumOnlyModule = true;
        if (symbol.Members is not null) result.Members = new SymbolTable(symbol.Members);
        if (symbol.Exports is not null) result.Exports = new SymbolTable(symbol.Exports);
        var resolvedModuleType = ResolveStructuredTypeMembers((UnionOrIntersectionType)moduleType); // Should already be resolved from the signature checks above
        GetSymbolLinks(result).Type = CreateAnonymousType(result, resolvedModuleType.Members!, [], [], resolvedModuleType.IndexInfos ?? []);
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:4730-4962 — <c>resolveExternalModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>contextSpecifier</c> is one <c>||</c> chain of six arms; C# has no such chain over
    /// six different shapes, so the port states the same arms in the reference's order as a guarded cascade (each arm
    /// runs only while <c>contextSpecifier</c> is still null) — the same short-circuit, spelled out. <c>or(…)</c> is the
    /// disjunction the visitor spells inline, <c>findAncestor(location, isImportCall)</c> is
    /// <c>Utilities.FindAncestor(location, Utilities.IsImportCall)</c>, and the import/export arm reads the three
    /// concrete <c>ModuleSpecifier</c> slots the same way <c>GetExternalModuleMember</c> does.
    /// <para>
    /// PORT-NOTE: <c>host.getModeForUsageLocation</c>/<c>host.getDefaultResolutionModeForFile</c>/
    /// <c>host.getResolvedModule</c>/<c>host.getSourceFile</c> are the port's <c>_host</c> reads; the reference's
    /// <c>suggestedExtensions.find(([actualExt, _importExt]) =&gt; host.fileExists(absoluteRef + actualExt))?.[1]</c> is the
    /// port's <c>List&lt;(string ActualExt, string ImportExt)&gt;</c> (<c>_suggestedExtensions</c>) with <c>Find</c> and the
    /// tuple accessors; <c>errorOrSuggestion</c> is <c>ErrorOrSuggestion</c>; and the two diagnostic chains are
    /// <c>Utilities.CreateModuleNotFoundChain</c>/<c>Utilities.ChainDiagnosticMessages</c>. The nested
    /// <c>getSuggestedImportSource</c> is the private member beside this one, and <c>emitModuleKindIsNonNodeESM</c> is the
    /// already-landed <c>Utilities.EmitModuleKindIsNonNodeEsm</c> (the port's spelling).
    /// </para>
    /// </remarks>
    internal ISymbol? ResolveExternalModule(Node location, string moduleReference, DiagnosticMessage? moduleNotFoundError, Node? errorNode, bool isForAugmentation = false)
    {
        static bool IsTypeOnlyNode(Node node) => node switch
        {
            ImportClause importClause => importClause.IsTypeOnly,
            ImportEqualsDeclaration importEqualsDeclaration => importEqualsDeclaration.IsTypeOnly,
            ExportDeclaration exportDeclaration => exportDeclaration.IsTypeOnly,
            _ => false,
        };

        if (errorNode is not null && moduleReference.StartsWith("@types/", StringComparison.Ordinal))
        {
            var diag = Diagnostics.Cannot_import_type_declaration_files_Consider_importing_0_instead_of_1;
            var withoutAtTypePrefix = moduleReference["@types/".Length..];
            Error(errorNode, diag, withoutAtTypePrefix, moduleReference);
        }

        var ambientModule = TryFindAmbientModule(moduleReference, /*withAugmentations*/ true);
        if (ambientModule is not null)
        {
            return ambientModule;
        }

        var currentSourceFile = Utilities.GetSourceFileOfNode(location);
        Node? contextSpecifier = null;
        if (Utilities.IsStringLiteralLike(location))
        {
            contextSpecifier = location;
        }

        if (contextSpecifier is null)
        {
            var moduleDeclaration = NodeTests.IsModuleDeclaration(location)
                ? location
                : location.Parent is not null && NodeTests.IsModuleDeclaration(location.Parent) && ReferenceEquals(((ModuleDeclaration)location.Parent).Name, location) ? location.Parent : null;
            if (moduleDeclaration is not null)
            {
                contextSpecifier = ((ModuleDeclaration)moduleDeclaration).Name;
            }
        }

        if (contextSpecifier is null && Utilities.IsLiteralImportTypeNode(location))
        {
            contextSpecifier = ((LiteralTypeNode)((ImportTypeNode)location).Argument!).Literal;
        }

        if (contextSpecifier is null && Utilities.IsVariableDeclarationInitializedToBareOrAccessedRequire(location))
        {
            contextSpecifier = Utilities.GetModuleSpecifierOfBareOrAccessedRequire(location);
        }

        if (contextSpecifier is null)
        {
            var importCall = Utilities.FindAncestor(location, Utilities.IsImportCall);
            if (importCall is not null)
            {
                contextSpecifier = ((CallExpression)importCall).Arguments![0];
            }
        }

        if (contextSpecifier is null)
        {
            var importOrExport = Utilities.FindAncestor(location, n => NodeTests.IsImportDeclaration(n) || NodeTests.IsJSDocImportTag(n) || NodeTests.IsExportDeclaration(n));
            if (importOrExport is not null)
            {
                contextSpecifier = importOrExport switch
                {
                    ImportDeclaration importDeclaration => importDeclaration.ModuleSpecifier,
                    ExportDeclaration exportDeclaration => exportDeclaration.ModuleSpecifier,
                    JSDocImportTag jsDocImportTag => jsDocImportTag.ModuleSpecifier,
                    _ => null,
                };
            }
        }

        if (contextSpecifier is null)
        {
            var importEquals = Utilities.FindAncestor(location, Utilities.IsExternalModuleImportEqualsDeclaration);
            if (importEquals is not null)
            {
                contextSpecifier = ((ExternalModuleReference)((ImportEqualsDeclaration)importEquals).ModuleReference!).Expression;
            }
        }

        var mode = (contextSpecifier is not null && Utilities.IsStringLiteralLike(contextSpecifier)
            ? _host.GetModeForUsageLocation(currentSourceFile!, (LiteralExpression)contextSpecifier)
            : _host.GetDefaultResolutionModeForFile(currentSourceFile!)) as ModuleKind?;
        var moduleResolutionKind = Utilities.GetEmitModuleResolutionKind(_compilerOptions);
        var resolvedModule = _host.GetResolvedModule(currentSourceFile, moduleReference, mode)?.ResolvedModule;
        var resolutionDiagnostic = errorNode is not null && resolvedModule is not null ? Program.GetResolutionDiagnostic(_compilerOptions, resolvedModule, currentSourceFile!.IsDeclarationFile) : null;
        var sourceFile = resolvedModule is not null
            && (resolutionDiagnostic is null || resolutionDiagnostic == Diagnostics.Module_0_was_resolved_to_1_but_jsx_is_not_set)
            ? _host.GetSourceFile(resolvedModule.ResolvedFileName)
            : null;
        if (sourceFile is not null)
        {
            // If there's a resolutionDiagnostic we need to report it even if a sourceFile is found.
            if (resolutionDiagnostic is not null)
            {
                Error(errorNode, resolutionDiagnostic, moduleReference, resolvedModule!.ResolvedFileName);
            }

            if (Js.IsTruthy(resolvedModule!.ResolvedUsingTsExtension) && Parser.IsDeclarationFileName(moduleReference))
            {
                var importOrExport = Utilities.FindAncestor(location, NodeTests.IsImportDeclaration) is { } importDeclaration ? ((ImportDeclaration)importDeclaration).ImportClause
                    : Utilities.FindAncestor(location, n => NodeTests.IsImportEqualsDeclaration(n) || NodeTests.IsExportDeclaration(n));
                if (errorNode is not null && importOrExport is not null && !IsTypeOnlyNode(importOrExport) || Utilities.FindAncestor(location, Utilities.IsImportCall) is not null)
                {
                    Error(
                        errorNode,
                        Diagnostics.A_declaration_file_cannot_be_imported_without_import_type_Did_you_mean_to_import_an_implementation_file_0_instead,
                        GetSuggestedImportSource(moduleReference, mode, Debug.CheckDefined(Utilities.TryExtractTSExtension(moduleReference))));
                }
            }
            else if (Js.IsTruthy(resolvedModule!.ResolvedUsingTsExtension) && !ModuleNameResolver.ShouldAllowImportingTsExtension(_compilerOptions, currentSourceFile!.FileName))
            {
                var importOrExport = Utilities.FindAncestor(location, NodeTests.IsImportDeclaration) is { } importDeclaration2 ? ((ImportDeclaration)importDeclaration2).ImportClause
                    : Utilities.FindAncestor(location, n => NodeTests.IsImportEqualsDeclaration(n) || NodeTests.IsExportDeclaration(n));
                if (errorNode is not null && !(importOrExport is not null && IsTypeOnlyNode(importOrExport) || Utilities.FindAncestor(location, NodeTests.IsImportTypeNode) is not null))
                {
                    var tsExtension = Debug.CheckDefined(Utilities.TryExtractTSExtension(moduleReference));
                    Error(errorNode, Diagnostics.An_import_path_can_only_end_with_a_0_extension_when_allowImportingTsExtensions_is_enabled, tsExtension);
                }
            }
            else if (
                Js.IsTruthy(_compilerOptions.RewriteRelativeImportExtensions)
                && (location.Flags & NodeFlags.Ambient) == 0
                && !Parser.IsDeclarationFileName(moduleReference)
                && !Utilities.IsLiteralImportTypeNode(location)
                && !Utilities.IsPartOfTypeOnlyImportOrExportDeclaration(location)
            )
            {
                var shouldRewrite = Utilities.ShouldRewriteModuleSpecifier(moduleReference, _compilerOptions);
                if (!Js.IsTruthy(resolvedModule!.ResolvedUsingTsExtension) && shouldRewrite)
                {
                    Error(
                        errorNode,
                        Diagnostics.This_relative_import_path_is_unsafe_to_rewrite_because_it_looks_like_a_file_name_but_actually_resolves_to_0,
                        Paths.GetRelativePathFromFile(Paths.GetNormalizedAbsolutePath(currentSourceFile!.FileName!, ((IModuleSpecifierResolutionHost)_host).GetCurrentDirectory()), resolvedModule.ResolvedFileName!, Utilities.HostGetCanonicalFileName((IModuleResolutionHost)_host)));
                }
                else if (Js.IsTruthy(resolvedModule.ResolvedUsingTsExtension) && !shouldRewrite && Utilities.SourceFileMayBeEmitted(sourceFile, _host))
                {
                    Error(
                        errorNode,
                        Diagnostics.This_import_uses_a_0_extension_to_resolve_to_an_input_TypeScript_file_but_will_not_be_rewritten_during_emit_because_it_is_not_a_relative_path,
                        Paths.GetAnyExtensionFromPath(moduleReference));
                }
                else if (Js.IsTruthy(resolvedModule.ResolvedUsingTsExtension) && shouldRewrite)
                {
                    var redirect = ((IModuleSpecifierResolutionHost)_host).GetRedirectFromSourceFile(sourceFile.Path)?.ResolvedRef;
                    if (redirect is not null)
                    {
                        var ignoreCase = ((IModuleSpecifierResolutionHost)_host).UseCaseSensitiveFileNames() == false;
                        var ownRootDir = _host.GetCommonSourceDirectory();
                        var otherRootDir = Program.GetCommonSourceDirectoryOfConfig(redirect.CommandLine!, ignoreCase);
                        var rootDirPath = Paths.GetRelativePathFromDirectory(ownRootDir, otherRootDir, ignoreCase);
                        var outDirPath = Paths.GetRelativePathFromDirectory(_compilerOptions.OutDir ?? ownRootDir, redirect.CommandLine!.Options!.OutDir ?? otherRootDir, ignoreCase);
                        if (rootDirPath != outDirPath)
                        {
                            Error(
                                errorNode,
                                Diagnostics.This_import_path_is_unsafe_to_rewrite_because_it_resolves_to_another_project_and_the_relative_path_between_the_projects_output_files_is_not_the_same_as_the_relative_path_between_its_input_files);
                        }
                    }
                }
            }

            if (sourceFile.Symbol is not null)
            {
                if (errorNode is not null && Js.IsTruthy(resolvedModule!.IsExternalLibraryImport) && !Utilities.ResolutionExtensionIsTSOrJson(resolvedModule.Extension ?? ""))
                {
                    ErrorOnImplicitAnyModule(/*isError*/ false, errorNode, currentSourceFile!, mode, resolvedModule, moduleReference);
                }

                if (errorNode is not null && (_moduleKind == ModuleKind.Node16 || _moduleKind == ModuleKind.Node18))
                {
                    var isSyncImport = (currentSourceFile!.ImpliedNodeFormat as ModuleKind? == ModuleKind.CommonJS && Utilities.FindAncestor(location, Utilities.IsImportCall) is null) || Utilities.FindAncestor(location, NodeTests.IsImportEqualsDeclaration) is not null;
                    // An override clause will take effect for type-only imports and import types, and allows importing the types across formats, regardless of
                    // normal mode restrictions
                    var overrideHost = Utilities.FindAncestor(location, l => NodeTests.IsImportTypeNode(l) || NodeTests.IsExportDeclaration(l) || NodeTests.IsImportDeclaration(l) || NodeTests.IsJSDocImportTag(l));
                    if (isSyncImport && currentSourceFile.ImpliedNodeFormat as ModuleKind? == ModuleKind.ESNext && !Utilities.HasResolutionModeOverride(overrideHost))
                    {
                        if (Utilities.FindAncestor(location, NodeTests.IsImportEqualsDeclaration) is not null)
                        {
                            // ImportEquals in a ESM file resolving to another ESM file
                            Error(errorNode, Diagnostics.Module_0_cannot_be_imported_using_this_construct_The_specifier_only_resolves_to_an_ES_module_which_cannot_be_imported_with_require_Use_an_ECMAScript_import_instead, moduleReference);
                        }
                        else
                        {
                            // CJS file resolving to an ESM file
                            IDiagnosticMessageChain? diagnosticDetails = null;
                            var ext = Utilities.TryGetExtensionFromPath(currentSourceFile!.FileName!);
                            if (ext == Extension.Ts.Value || ext == Extension.Js.Value || ext == Extension.Tsx.Value || ext == Extension.Jsx.Value)
                            {
                                diagnosticDetails = Utilities.CreateModeMismatchDetails(currentSourceFile);
                            }

                            var message = Js.IsTruthy(overrideHost?.Kind == SyntaxKind.ImportDeclaration && ((ImportDeclaration)overrideHost).ImportClause is { } clause && clause.IsTypeOnly)
                                ? Diagnostics.Type_only_import_of_an_ECMAScript_module_from_a_CommonJS_module_must_have_a_resolution_mode_attribute
                                : Js.IsTruthy(overrideHost?.Kind == SyntaxKind.ImportType)
                                ? Diagnostics.Type_import_of_an_ECMAScript_module_from_a_CommonJS_module_must_have_a_resolution_mode_attribute
                                : Diagnostics.The_current_file_is_a_CommonJS_module_whose_imports_will_produce_require_calls_however_the_referenced_file_is_an_ECMAScript_module_and_cannot_be_imported_with_require_Consider_writing_a_dynamic_import_0_call_instead;
                            _diagnostics.Add(Utilities.CreateDiagnosticForNodeFromMessageChain(
                                Utilities.GetSourceFileOfNode(errorNode)!,
                                errorNode,
                                Utilities.ChainDiagnosticMessages(diagnosticDetails, message, moduleReference)));
                        }
                    }
                }

                // merged symbol is module declaration symbol combined with all augmentations
                return GetMergedSymbol(sourceFile.Symbol);
            }

            if (errorNode is not null && moduleNotFoundError is not null && !Utilities.IsSideEffectImport(errorNode))
            {
                // report errors only if it was requested
                Error(errorNode, Diagnostics.File_0_is_not_a_module, sourceFile.FileName);
            }

            return null;
        }

        if (_patternAmbientModules is not null)
        {
            var pattern = Core.FindBestPatternMatch(_patternAmbientModules, m => (Core.Pattern)m.Pattern!, moduleReference);
            if (pattern is not null)
            {
                // If the module reference matched a pattern ambient module ('*.foo') but there's also a
                // module augmentation by the specific name requested ('a.foo'), we store the merged symbol
                // by the augmentation name ('a.foo'), because asking for *.foo should not give you exports
                // from a.foo.
                var augmentation = _patternAmbientModuleAugmentations is not null && _patternAmbientModuleAugmentations.TryGetValue(moduleReference, out var augmented) ? augmented : null;
                if (augmentation is not null)
                {
                    return GetMergedSymbol(augmentation);
                }

                return GetMergedSymbol(pattern.Symbol);
            }
        }

        if (errorNode is null)
        {
            return null;
        }

        // May be an untyped module. If so, ignore resolutionDiagnostic.
        if (resolvedModule is not null && !Utilities.ResolutionExtensionIsTSOrJson(resolvedModule.Extension ?? "") && resolutionDiagnostic is null || resolutionDiagnostic == Diagnostics.Could_not_find_a_declaration_file_for_module_0_1_implicitly_has_an_any_type)
        {
            if (isForAugmentation)
            {
                var diag = Diagnostics.Invalid_module_name_in_augmentation_Module_0_resolves_to_an_untyped_module_at_1_which_cannot_be_augmented;
                Error(errorNode, diag, moduleReference, resolvedModule!.ResolvedFileName);
            }
            else
            {
                ErrorOnImplicitAnyModule(/*isError*/ Js.IsTruthy(_noImplicitAny) && moduleNotFoundError is not null, errorNode, currentSourceFile!, mode, resolvedModule!, moduleReference);
            }

            // Failed imports and untyped modules are both treated in an untyped manner; only difference is whether we give a diagnostic first.
            return null;
        }

        if (moduleNotFoundError is not null)
        {
            // See if this was possibly a projectReference redirect
            if (resolvedModule is not null)
            {
                var redirect = ((IModuleSpecifierResolutionHost)_host).GetRedirectFromSourceFile(resolvedModule.ResolvedFileName!);
                if (redirect?.OutputDts is not null)
                {
                    Error(errorNode, Diagnostics.Output_file_0_has_not_been_built_from_source_file_1, redirect.OutputDts, resolvedModule.ResolvedFileName);
                    return null;
                }
            }

            if (resolutionDiagnostic is not null)
            {
                Error(errorNode, resolutionDiagnostic, moduleReference, resolvedModule!.ResolvedFileName);
            }
            else
            {
                var isExtensionlessRelativePathImport = Paths.PathIsRelative(moduleReference) && !Paths.HasExtension(moduleReference);
                var resolutionIsNode16OrNext = moduleResolutionKind == ModuleResolutionKind.Node16
                    || moduleResolutionKind == ModuleResolutionKind.NodeNext;
                if (
                    Utilities.GetResolveJsonModule(_compilerOptions) == false
                    && Paths.FileExtensionIs(moduleReference, Extension.Json.Value)
                    && moduleResolutionKind != ModuleResolutionKind.Classic
                    && Utilities.HasJsonModuleEmitEnabled(_compilerOptions)
                )
                {
                    Error(errorNode, Diagnostics.Cannot_find_module_0_Consider_using_resolveJsonModule_to_import_module_with_json_extension, moduleReference);
                }
                else if (mode == ModuleKind.ESNext && resolutionIsNode16OrNext && isExtensionlessRelativePathImport)
                {
                    var absoluteRef = Paths.GetNormalizedAbsolutePath(moduleReference, Paths.GetDirectoryPath(currentSourceFile!.Path!));
                    var suggestedExt = _suggestedExtensions.Find(e => _host.FileExists(absoluteRef + e.ActualExt)).ImportExt;
                    if (suggestedExt is not null)
                    {
                        Error(errorNode, Diagnostics.Relative_import_paths_need_explicit_file_extensions_in_ECMAScript_imports_when_moduleResolution_is_node16_or_nodenext_Did_you_mean_0, moduleReference + suggestedExt);
                    }
                    else
                    {
                        Error(errorNode, Diagnostics.Relative_import_paths_need_explicit_file_extensions_in_ECMAScript_imports_when_moduleResolution_is_node16_or_nodenext_Consider_adding_an_extension_to_the_import_path);
                    }
                }
                else
                {
                    if (_host.GetResolvedModule(currentSourceFile, moduleReference, mode)?.AlternateResult is not null)
                    {
                        var errorInfo = Utilities.CreateModuleNotFoundChain(currentSourceFile!, _host, moduleReference, mode, moduleReference);
                        ErrorOrSuggestion(/*isError*/ true, errorNode, Utilities.ChainDiagnosticMessages(errorInfo, moduleNotFoundError, moduleReference));
                    }
                    else
                    {
                        Error(errorNode, moduleNotFoundError, moduleReference);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:4947-4961 — <c>getSuggestedImportSource</c> (a local function of
    /// <c>resolveExternalModule</c>).</summary>
    /// <remarks>
    /// Direct users to import source with .js extension if outputting an ES module.
    /// <para>
    /// PORT-NOTE: <c>emitModuleKindIsNonNodeESM(moduleKind)</c> is the landed <c>Utilities.EmitModuleKindIsNonNodeEsm</c>
    /// (the port's spelling of that member); <c>removeExtension</c> is <c>Utilities.RemoveExtension</c> and
    /// <c>shouldAllowImportingTsExtension</c> is <c>ModuleNameResolver.ShouldAllowImportingTsExtension</c>. The reference
    /// closes over <c>moduleReference</c>/<c>mode</c>/<c>compilerOptions</c>; the port passes the first two explicitly
    /// (mode is only read for the ES-module test) because its local function is a private member here.
    /// </para>
    /// </remarks>
    private string GetSuggestedImportSource(string moduleReference, ModuleKind? mode, string tsExtension)
    {
        var importSourceWithoutExtension = Utilities.RemoveExtension(moduleReference, tsExtension);
        if (Utilities.EmitModuleKindIsNonNodeEsm(_moduleKind) || mode == ModuleKind.ESNext)
        {
            var preferTs = Parser.IsDeclarationFileName(moduleReference) && ModuleNameResolver.ShouldAllowImportingTsExtension(_compilerOptions);
            var ext = tsExtension == Extension.Mts.Value || tsExtension == Extension.Dmts.Value ? preferTs ? ".mts" : ".mjs"
                : tsExtension == Extension.Cts.Value || tsExtension == Extension.Dmts.Value ? preferTs ? ".cts" : ".cjs"
                : preferTs ? ".ts" : ".js";
            return importSourceWithoutExtension + ext;
        }

        return importSourceWithoutExtension;
    }

    /// <summary>ported from: src/compiler/checker.ts:4964-4983 — <c>errorOnImplicitAnyModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference destructures <c>{ packageId, resolvedFileName }</c> from its
    /// <c>ResolvedModuleFull</c> parameter; the port keeps the whole record and reads the two slots (the generated
    /// <c>ResolvedModuleFull</c> has both). <c>isSideEffectImport</c>, <c>isExternalModuleNameRelative</c> and
    /// <c>createModuleNotFoundChain</c> are the members of those names, and the chain is built by
    /// <c>Utilities.ChainDiagnosticMessages</c>.
    /// </remarks>
    private void ErrorOnImplicitAnyModule(bool isError, Node errorNode, SourceFile sourceFile, ModuleKind? mode, IResolvedModuleFull resolvedModule, string moduleReference)
    {
        if (Utilities.IsSideEffectImport(errorNode))
        {
            return;
        }

        IDiagnosticMessageChain? errorInfo = null;
        if (!Utilities.IsExternalModuleNameRelative(moduleReference) && resolvedModule.PackageId is not null)
        {
            errorInfo = Utilities.CreateModuleNotFoundChain(sourceFile, _host, moduleReference, mode, resolvedModule.PackageId.Name!);
        }

        ErrorOrSuggestion(
            isError,
            errorNode,
            Utilities.ChainDiagnosticMessages(
                errorInfo,
                Diagnostics.Could_not_find_a_declaration_file_for_module_0_1_implicitly_has_an_any_type,
                moduleReference,
                resolvedModule.ResolvedFileName));
    }
}
