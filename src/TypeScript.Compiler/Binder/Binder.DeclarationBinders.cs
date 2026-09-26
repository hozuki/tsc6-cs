namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:3097-3106 (bindPropertyWorker, bindAnonymousTypeWorker)
// ported from: src/compiler/binder.ts:3648-3682 (bindVariableDeclarationOrBindingElement)
// ported from: src/compiler/binder.ts:3684-3707 (bindParameter)
// ported from: src/compiler/binder.ts:3709-3724 (bindFunctionDeclaration)
// ported from: src/compiler/binder.ts:3726-3738 (bindFunctionExpression)
// ported from: src/compiler/binder.ts:3740-3752 (bindPropertyOrMethodOrAccessor)
// ported from: src/compiler/binder.ts:3754-3758 (getInferTypeContainer)
// ported from: src/compiler/binder.ts:3759-3785 (bindTypeParameter)
/// <summary>
/// The per-kind declaration binders that <c>bindWorker</c> dispatches to: a property, an anonymous type, a variable
/// or binding element, a parameter, a function, a method/accessor and a type parameter.
/// </summary>
/// <remarks>
/// PORT-NOTE: every parameter TS types by a union of node types is stated here at the honest upper bound the arm's own
/// guard proves, and the union's own slots are read by a pattern over the members that declare them — the two treatments
/// §12.9 records. Where TS's union is narrower than <see cref="Node"/> the narrower face is used
/// (<see cref="IFunctionLikeDeclarationBase"/>, <see cref="INamedDeclaration"/>).
/// <para>
/// PORT-NOTE: the strict-mode checks these members call — <c>checkStrictModeEvalOrArguments</c> (binder.ts:2648-2660),
/// <c>checkStrictModeFunctionName</c> (binder.ts:2674-2679) and <c>checkStrictModeFunctionDeclaration</c>
/// (binder.ts:2695-2709) — belong to the strict-mode slice (M5-3) and are called here by the reference names.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:3097-3102 — <c>bindPropertyWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>PropertyDeclaration | PropertySignature</c> share no face that declares <c>questionToken</c>, so it
    /// is read through a pattern over the two classes.
    /// </remarks>
    internal ISymbol? BindPropertyWorker(Node node)
    {
        var isAutoAccessor = Utilities.IsAutoAccessorPropertyDeclaration(node);
        var includes = isAutoAccessor ? SymbolFlags.Accessor : SymbolFlags.Property;
        var excludes = isAutoAccessor ? SymbolFlags.AccessorExcludes : SymbolFlags.PropertyExcludes;
        var questionToken = node switch
        {
            PropertyDeclaration declaration => declaration.QuestionToken,
            PropertySignature signature => signature.QuestionToken,
            _ => null,
        };
        return BindPropertyOrMethodOrAccessor((IDeclaration)node, includes | (questionToken is not null ? SymbolFlags.Optional : SymbolFlags.None), excludes);
    }

    /// <summary>ported from: src/compiler/binder.ts:3104-3106 — <c>bindAnonymousTypeWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>TypeLiteralNode | MappedTypeNode | JSDocTypeLiteral</c> union, which shares
    /// no face but <see cref="Node"/>; the one thing the body needs is the <c>Declaration</c> face.
    /// </remarks>
    internal ISymbol BindAnonymousTypeWorker(Node node)
        => BindAnonymousDeclaration((IDeclaration)node, SymbolFlags.TypeLiteral, InternalSymbolName.Type.Value);

    /// <summary>ported from: src/compiler/binder.ts:3648-3682 — <c>bindVariableDeclarationOrBindingElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>VariableDeclaration | BindingElement</c> share <see cref="INamedDeclaration"/>'s <c>name</c>, which
    /// is the only slot read before the declaration faces are needed.
    /// <para>
    /// PORT-NOTE: TS's <c>!getJSDocTypeTag(node)</c> is a truthiness test on the tag a lookup returns; a reference
    /// answers the same question with <c>is null</c>. <c>getJSDocTypeTag</c> (utilities.ts:4462-4464) is not ported —
    /// this call names it.
    /// </para>
    /// </remarks>
    internal void BindVariableDeclarationOrBindingElement(Node node)
    {
        var name = ((INamedDeclaration)node).Name;
        if (_inStrictMode)
        {
            CheckStrictModeEvalOrArguments(node, name);
        }

        if (!Utilities.IsBindingPattern(name))
        {
            var possibleVariableDecl = node.Kind == SyntaxKind.VariableDeclaration ? node : node.Parent!.Parent!;
            if (
                Utilities.IsInJSFile(node) &&
                Utilities.IsVariableDeclarationInitializedToBareOrAccessedRequire(possibleVariableDecl) &&
                Utilities.GetJSDocTypeTag(node) is null &&
                (Utilities.GetCombinedModifierFlags(node) & ModifierFlags.Export) == 0
            )
            {
                DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
            }
            else if (Utilities.IsBlockOrCatchScoped((IDeclaration)node))
            {
                BindBlockScopedDeclaration((IDeclaration)node, SymbolFlags.BlockScopedVariable, SymbolFlags.BlockScopedVariableExcludes);
            }
            else if (Utilities.IsPartOfParameterDeclaration(node))
            {
                // It is safe to walk up parent chain to find whether the node is a destructuring parameter declaration
                // because its parent chain has already been set up, since parents are set before descending into children.
                //
                // If node is a binding element in parameter declaration, we need to use ParameterExcludes.
                // Using ParameterExcludes flag allows the compiler to report an error on duplicate identifiers in Parameter Declaration
                // For example:
                //      function foo([a,a]) {} // Duplicate Identifier error
                //      function bar(a,a) {}   // Duplicate Identifier error, parameter declaration in this case is handled in bindParameter
                //                             // which correctly set excluded symbols
                DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
            }
            else
            {
                DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.FunctionScopedVariable, SymbolFlags.FunctionScopedVariableExcludes);
            }
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3684-3707 — <c>bindParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ParameterDeclaration | JSDocParameterTag</c>, which both expose <c>name</c> and
    /// <c>flags</c>. Only <c>ParameterDeclaration</c> is a <c>NamedDeclaration</c> (a <c>JSDocPropertyLikeTag</c>'s
    /// <c>name</c> is an <c>EntityName</c>), so the union access is the kind test the reference's own guard already
    /// reads, not a cast to <see cref="INamedDeclaration"/>.
    /// <para>
    /// PORT-NOTE: TS's <c>isParameterPropertyDeclaration(node, node.parent)</c> is a type predicate that narrows
    /// <c>node</c> to a property parameter for the rest of the block; C# cannot narrow through a predicate, so the
    /// narrowed reads (<c>questionToken</c>, <c>parent.parent</c>) are taken off the cast the guard licenses.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>Parameters.indexOf</c> is reached through the parameter's narrowed parent
    /// (<c>ParameterDeclaration.parent: SignatureDeclaration</c>). The port's <c>Node.Parent</c> is untyped, so the
    /// signature face supplies the list.
    /// </para>
    /// </remarks>
    internal void BindParameter(Node node)
    {
        if (node.Kind == SyntaxKind.JSDocParameterTag && _container!.Kind != SyntaxKind.JSDocSignature)
        {
            return;
        }

        var name = NodeTests.IsJSDocParameterTag(node) ? ((JSDocPropertyLikeTag)node).Name : ((ParameterDeclaration)node).Name;
        if (_inStrictMode && (node.Flags & NodeFlags.Ambient) == 0)
        {
            // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a
            // strict mode FunctionLikeDeclaration or FunctionExpression(13.1)
            CheckStrictModeEvalOrArguments(node, name);
        }

        if (Utilities.IsBindingPattern(name))
        {
            var parameter = (ParameterDeclaration)node;
            var parent = (ISignatureDeclarationBase)parameter.Parent!;
            BindAnonymousDeclaration((IDeclaration)node, SymbolFlags.FunctionScopedVariable, "__" + parent.Parameters!.IndexOf(parameter));
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable((IDeclaration)node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
        }

        // If this is a property-parameter, then also declare the property symbol into the
        // containing class.
        if (Utilities.IsParameterPropertyDeclaration(node, node.Parent!))
        {
            var parameter = (ParameterDeclaration)node;
            var classDeclaration = parameter.Parent!.Parent!;
            var classSymbol = ((IDeclaration)classDeclaration).Symbol!;
            DeclareSymbol(classSymbol.Members!, classSymbol, (IDeclaration)node, SymbolFlags.Property | (parameter.QuestionToken is not null ? SymbolFlags.Optional : SymbolFlags.None), SymbolFlags.PropertyExcludes);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3709-3724 — <c>bindFunctionDeclaration</c>.</summary>
    internal void BindFunctionDeclaration(FunctionDeclaration node)
    {
        if (!_file.IsDeclarationFile && (node.Flags & NodeFlags.Ambient) == 0)
        {
            if (Utilities.IsAsyncFunction(node))
            {
                _emitFlags |= NodeFlags.HasAsyncFunctions;
            }
        }

        CheckStrictModeFunctionName((IFunctionLikeDeclarationBase)node);
        if (_inStrictMode)
        {
            CheckStrictModeFunctionDeclaration(node);
            BindBlockScopedDeclaration(node, SymbolFlags.Function, SymbolFlags.FunctionExcludes);
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Function, SymbolFlags.FunctionExcludes);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:3726-3738 — <c>bindFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>FunctionExpression | ArrowFunction</c>; the union's one shared read is
    /// <c>name</c>, which is <c>never</c> on the arrow half, so the read is a pattern over the function-expression half
    /// and the arrow always gets the anonymous name TS's own types give it.
    /// </remarks>
    internal ISymbol BindFunctionExpression(Node node)
    {
        if (!_file.IsDeclarationFile && (node.Flags & NodeFlags.Ambient) == 0)
        {
            if (Utilities.IsAsyncFunction(node))
            {
                _emitFlags |= NodeFlags.HasAsyncFunctions;
            }
        }

        if (_currentFlow is not null)
        {
            ((IFlowContainer)node).FlowNode = _currentFlow;
        }

        CheckStrictModeFunctionName((IFunctionLikeDeclarationBase)node);
        var name = node is FunctionExpression functionExpression ? functionExpression.Name : null;
        var bindingName = name is not null
            ? Debug.CheckDefined(name.EscapedText, "a function expression name must have escaped text")
            : InternalSymbolName.Function.Value;
        return BindAnonymousDeclaration((IDeclaration)node, SymbolFlags.Function, bindingName);
    }

    /// <summary>ported from: src/compiler/binder.ts:3740-3752 — <c>bindPropertyOrMethodOrAccessor</c>.</summary>
    internal ISymbol? BindPropertyOrMethodOrAccessor(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
    {
        if (!_file.IsDeclarationFile && (((Node)node).Flags & NodeFlags.Ambient) == 0 && Utilities.IsAsyncFunction((Node)node))
        {
            _emitFlags |= NodeFlags.HasAsyncFunctions;
        }

        if (_currentFlow is not null && Utilities.IsObjectLiteralOrClassExpressionMethodOrAccessor((Node)node))
        {
            ((IFlowContainer)node).FlowNode = _currentFlow;
        }

        return Utilities.HasDynamicName((Node)node)
            ? BindAnonymousDeclaration(node, symbolFlags, InternalSymbolName.Computed.Value)
            : DeclareSymbolAndAddToSymbolTable(node, symbolFlags, symbolExcludes);
    }

    /// <summary>ported from: src/compiler/binder.ts:3754-3758 — <c>getInferTypeContainer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS ends with <c>extendsType &amp;&amp; extendsType.parent as ConditionalTypeNode</c>; the walk's own
    /// predicate proves the parent is a conditional type, so the port states that cast rather than a conditional one.
    /// </remarks>
    private static ConditionalTypeNode? GetInferTypeContainer(Node node)
    {
        var extendsType = Utilities.FindAncestor(node, n => n.Parent is not null && NodeTests.IsConditionalTypeNode(n.Parent) && ((ConditionalTypeNode)n.Parent).ExtendsType == n);
        return extendsType is not null ? (ConditionalTypeNode)extendsType.Parent! : null;
    }

    /// <summary>ported from: src/compiler/binder.ts:3759-3785 — <c>bindTypeParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two containers are <c>HasLocals | undefined</c> and <c>ConditionalTypeNode | undefined</c>;
    /// <c>getEffectiveContainerForJSDocTemplateTag</c> answers <see cref="INode"/>, so that arm reads the locals off the
    /// capability face and asserts the node with it.
    /// </remarks>
    internal void BindTypeParameter(TypeParameterDeclaration node)
    {
        if (NodeTests.IsJSDocTemplateTag(node.Parent!))
        {
            var container = Utilities.GetEffectiveContainerForJSDocTemplateTag((JSDocTemplateTag)node.Parent!);
            if (container is not null)
            {
                Debug.AssertNode((Node)container, Utilities.CanHaveLocals);
                var localsContainer = (ILocalsContainer)container;
                var locals = localsContainer.Locals ?? (localsContainer.Locals = Utilities.CreateSymbolTable());
                DeclareSymbol(locals, /*parent*/ null, node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
            }
            else
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
            }
        }
        else if (node.Parent!.Kind == SyntaxKind.InferType)
        {
            var container = GetInferTypeContainer(node.Parent!);
            if (container is not null)
            {
                Debug.AssertNode(container, Utilities.CanHaveLocals);
                var locals = container.Locals ?? (container.Locals = Utilities.CreateSymbolTable());
                DeclareSymbol(locals, /*parent*/ null, node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
            }
            else
            {
                BindAnonymousDeclaration(node, SymbolFlags.TypeParameter, GetDeclarationName(node)!);
            }
        }
        else
        {
            DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.TypeParameter, SymbolFlags.TypeParameterExcludes);
        }
    }
}
