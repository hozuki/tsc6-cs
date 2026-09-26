namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:953-1077 (bindContainer)
/// <summary>
/// The scope half of the binder's walk: TS's <c>bindContainer</c>, which saves the container/block-scope/flow state,
/// descends into the children, and puts the results back on the node it opened.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's parameter is <c>Mutable&lt;HasContainerFlags&gt;</c>. <c>Mutable&lt;T&gt;</c> is typespace-only
/// (§15.9) and <c>HasContainerFlags</c> is a union of container node types with no C# face, so the port takes the
/// honest upper bound <see cref="Node"/> (§12.9) — which is also the only type whose <c>flags</c> can be written.
/// <para>
/// PORT-NOTE: TS casts to <c>FunctionLikeDeclaration | ClassStaticBlockDeclaration</c> twice (binder.ts:1013, 1019-1024)
/// and writes <c>endFlowNode</c>/<c>returnFlowNode</c> on the same union. <c>FunctionLikeDeclaration</c> is a union of
/// seven classes, but the port states its slots on <see cref="IFunctionLikeDeclarationBase"/> —
/// <c>endFlowNode</c>/<c>returnFlowNode</c>/<c>body</c>/<c>asteriskToken</c> all live there — so the union only has to
/// be handled for <see cref="ClassStaticBlockDeclaration"/>, which declares the same slots without implementing the
/// interface. Hence the three small readers/writers below; the writes are no-ops for a node that is neither, which is
/// what TS's unchecked cast would have been.
/// </para>
/// <para>
/// PORT-NOTE: M5-2 supplies the flow-graph members this member calls — <c>createBranchLabel</c>
/// (binder.ts:1356-1378), <c>addAntecedent</c> (binder.ts:1399-1407) and <c>finishFlowLabel</c>
/// (binder.ts:1424-1431) — and they are called here by the reference names.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:953-1077 — <c>bindContainer</c>.</summary>
    private void BindContainer(Node node, ContainerFlags containerFlags)
    {
        // Before we recurse into a node's children, we first save the existing parent, container
        // and block-container.  Then after we pop out of processing the children, we restore
        // these saved values.
        var saveContainer = _container;
        var saveThisParentContainer = _thisParentContainer;
        var savedBlockScopeContainer = _blockScopeContainer;
        var savedInReturnPosition = _inReturnPosition;

        if (node.Kind == SyntaxKind.ArrowFunction && ((ArrowFunction)node).Body!.Kind != SyntaxKind.Block) _inReturnPosition = true;
        // Depending on what kind of node this is, we may have to adjust the current container
        // and block-container.   If the current node is a container, then it is automatically
        // considered the current block-container as well.  Also, for containers that we know
        // may contain locals, we eagerly initialize the .locals field. We do this because
        // it's highly likely that the .locals will be needed to place some child in (for example,
        // a parameter, or variable declaration).
        //
        // However, we do not proactively create the .locals for block-containers because it's
        // totally normal and common for block-containers to never actually have a block-scoped
        // variable in them.  We don't want to end up allocating an object for every 'block' we
        // run into when most of them won't be necessary.
        //
        // Finally, if this is a block-container, then we clear out any existing .locals object
        // it may contain within it.  This happens in incremental scenarios.  Because we can be
        // reusing a node from a previous compilation, that node may have had 'locals' created
        // for it.  We must clear this so we don't accidentally move any stale data forward from
        // a previous compilation.
        if ((containerFlags & ContainerFlags.IsContainer) != 0)
        {
            if (node.Kind != SyntaxKind.ArrowFunction)
            {
                _thisParentContainer = _container;
            }

            _container = _blockScopeContainer = node;
            if ((containerFlags & ContainerFlags.HasLocals) != 0)
            {
                var localsContainer = (ILocalsContainer)node;
                localsContainer.Locals = Utilities.CreateSymbolTable();
                AddToContainerChain(localsContainer);
            }
        }
        else if ((containerFlags & ContainerFlags.IsBlockScopedContainer) != 0)
        {
            _blockScopeContainer = node;
            if ((containerFlags & ContainerFlags.HasLocals) != 0)
            {
                ((ILocalsContainer)_blockScopeContainer).Locals = null;
            }
        }

        if ((containerFlags & ContainerFlags.IsControlFlowContainer) != 0)
        {
            var saveCurrentFlow = _currentFlow;
            var saveBreakTarget = _currentBreakTarget;
            var saveContinueTarget = _currentContinueTarget;
            var saveReturnTarget = _currentReturnTarget;
            var saveExceptionTarget = _currentExceptionTarget;
            var saveActiveLabelList = _activeLabelList;
            var saveHasExplicitReturn = _hasExplicitReturn;
            var saveSeenThisKeyword = _seenThisKeyword;
            var isImmediatelyInvoked = (
                (containerFlags & ContainerFlags.IsFunctionExpression) != 0 &&
                !Utilities.HasSyntacticModifier(node, ModifierFlags.Async) &&
                node is IFunctionLikeDeclarationBase { AsteriskToken: null } &&
                Utilities.GetImmediatelyInvokedFunctionExpression(node) is not null
            ) || node.Kind == SyntaxKind.ClassStaticBlockDeclaration;
            // A non-async, non-generator IIFE is considered part of the containing control flow. Return statements behave
            // similarly to break statements that exit to a label just past the statement body.
            if (!isImmediatelyInvoked)
            {
                _currentFlow = FlowNode.CreateFlowNode(FlowFlags.Start, /*node*/ null, /*antecedent*/ null);
                if ((containerFlags & (ContainerFlags.IsFunctionExpression | ContainerFlags.IsObjectLiteralOrClassExpressionMethodOrAccessor)) != 0)
                {
                    // PORT-NOTE: TS assigns the node after narrowing it to the five function-expression-shaped
                    // kinds; the flow node's slot is object?-typed, so the port assigns the node as it stands.
                    _currentFlow.Node = node;
                }
            }

            // We create a return control flow graph for IIFEs and constructors. For constructors
            // we use the return control flow graph in strict property initialization checks.
            _currentReturnTarget = isImmediatelyInvoked || node.Kind == SyntaxKind.Constructor || (Utilities.IsInJSFile(node) && (node.Kind == SyntaxKind.FunctionDeclaration || node.Kind == SyntaxKind.FunctionExpression)) ? CreateBranchLabel() : null;
            _currentExceptionTarget = null;
            _currentBreakTarget = null;
            _currentContinueTarget = null;
            _activeLabelList = null;
            _hasExplicitReturn = false;
            _seenThisKeyword = false;
            BindChildren(node);

            // Reset flags (for incremental scenarios)
            node.Flags &= ~(NodeFlags.ReachabilityAndEmitFlags | NodeFlags.ContainsThis);
            if (((_currentFlow!.Flags & FlowFlags.Unreachable) == 0)
                && (containerFlags & ContainerFlags.IsFunctionLike) != 0
                && Utilities.NodeIsPresent(GetFunctionLikeBody(node)))
            {
                node.Flags |= NodeFlags.HasImplicitReturn;
                if (_hasExplicitReturn) node.Flags |= NodeFlags.HasExplicitReturn;
                SetFunctionLikeEndFlowNode(node, _currentFlow);
            }

            if (_seenThisKeyword)
            {
                node.Flags |= NodeFlags.ContainsThis;
            }

            if (node.Kind == SyntaxKind.SourceFile)
            {
                node.Flags |= _emitFlags;
                ((SourceFile)node).EndFlowNode = _currentFlow;
            }

            if (_currentReturnTarget is not null)
            {
                AddAntecedent(_currentReturnTarget, _currentFlow!);
                _currentFlow = FinishFlowLabel(_currentReturnTarget);
                if (node.Kind == SyntaxKind.Constructor || node.Kind == SyntaxKind.ClassStaticBlockDeclaration || (Utilities.IsInJSFile(node) && (node.Kind == SyntaxKind.FunctionDeclaration || node.Kind == SyntaxKind.FunctionExpression)))
                {
                    SetFunctionLikeReturnFlowNode(node, _currentFlow);
                }
            }

            if (!isImmediatelyInvoked)
            {
                _currentFlow = saveCurrentFlow;
            }

            _currentBreakTarget = saveBreakTarget;
            _currentContinueTarget = saveContinueTarget;
            _currentReturnTarget = saveReturnTarget;
            _currentExceptionTarget = saveExceptionTarget;
            _activeLabelList = saveActiveLabelList;
            _hasExplicitReturn = saveHasExplicitReturn;
            _seenThisKeyword = (containerFlags & ContainerFlags.PropagatesThisKeyword) != 0 ? saveSeenThisKeyword || _seenThisKeyword : saveSeenThisKeyword;
        }
        else if ((containerFlags & ContainerFlags.IsInterface) != 0)
        {
            var saveSeenThisKeyword = _seenThisKeyword;
            _seenThisKeyword = false;
            BindChildren(node);
            Debug.AssertNotNode(node, NodeTests.IsIdentifier); // ContainsThis cannot overlap with HasExtendedUnicodeEscape on Identifier
            node.Flags = _seenThisKeyword ? node.Flags | NodeFlags.ContainsThis : node.Flags & ~NodeFlags.ContainsThis;
            _seenThisKeyword = saveSeenThisKeyword;
        }
        else
        {
            BindChildren(node);
        }

        _inReturnPosition = savedInReturnPosition;
        _container = saveContainer;
        _thisParentContainer = saveThisParentContainer;
        _blockScopeContainer = savedBlockScopeContainer;
    }

    /// <summary>
    /// The <c>body</c> of TS's <c>FunctionLikeDeclaration | ClassStaticBlockDeclaration</c> (binder.ts:1019).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>FunctionLikeDeclaration</c> is a union of seven classes and the eighth member of TS's cast,
    /// <see cref="ClassStaticBlockDeclaration"/>, declares the same slots without declaring
    /// <see cref="IFunctionLikeDeclarationBase"/>; this reader is the union read the port cannot write as one cast.
    /// </remarks>
    private static Node? GetFunctionLikeBody(Node node)
        => node switch
        {
            IFunctionLikeDeclarationBase functionLike => functionLike.Body,
            ClassStaticBlockDeclaration staticBlock => staticBlock.Body,
            _ => null,
        };

    /// <summary>The <c>endFlowNode</c> write of binder.ts:1023.</summary>
    /// <remarks>PORT-NOTE: see <see cref="GetFunctionLikeBody"/>.</remarks>
    private static void SetFunctionLikeEndFlowNode(Node node, FlowNode? flowNode)
    {
        switch (node)
        {
            case IFunctionLikeDeclarationBase functionLike:
                functionLike.EndFlowNode = flowNode;
                break;
            case ClassStaticBlockDeclaration staticBlock:
                staticBlock.EndFlowNode = flowNode;
                break;
        }
    }

    /// <summary>The <c>returnFlowNode</c> write of binder.ts:1042.</summary>
    /// <remarks>PORT-NOTE: see <see cref="GetFunctionLikeBody"/>.</remarks>
    private static void SetFunctionLikeReturnFlowNode(Node node, FlowNode? flowNode)
    {
        switch (node)
        {
            case IFunctionLikeDeclarationBase functionLike:
                functionLike.ReturnFlowNode = flowNode;
                break;
            case ClassStaticBlockDeclaration staticBlock:
                staticBlock.ReturnFlowNode = flowNode;
                break;
        }
    }
}
