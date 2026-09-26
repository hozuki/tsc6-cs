#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2015.ts:4845-5026 — the emit and substitution hooks: captured `this`,
// block-scoped binding renaming and the module-level helpers that enable them.
/// <summary>
/// The printer-hook half of the ES2015 downlevel transform.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>factory.getGeneratedNameForNode</c> lives on the <see cref="NodeFactory"/> itself, while
/// <c>isInternalName</c> is <see cref="FactoryUtilities.IsInternalName"/> and the parse-tree walk is
/// <see cref="Utilities.GetParseTreeNode(Node?, Func{Node, bool}?)"/> — the same spellings <c>TypeScriptTransform</c>
/// uses for its own substitution slice.
/// </remarks>
internal sealed partial class ES2015Transformer
{
    /// <summary>ported from: src/compiler/transformers/es2015.ts:4855-4873 — <c>onEmitNode</c>.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if ((_enabledSubstitutions & ES2015SubstitutionFlags.CapturedThis) != 0 && Utilities.IsFunctionLike(node))
        {
            // If we are tracking a captured `this`, keep track of the enclosing function.
            var ancestorFacts = EnterSubtree(
                HierarchyFacts.FunctionExcludes,
                (Utilities.GetEmitFlags(node) & EmitFlags.CapturesThis) != 0
                    ? HierarchyFacts.FunctionIncludes | HierarchyFacts.CapturesThis
                    : HierarchyFacts.FunctionIncludes);
            _previousOnEmitNode(hint, node, emitCallback);
            ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
            return;
        }
        _previousOnEmitNode(hint, node, emitCallback);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4875-4884 — <c>enableSubstitutionsForBlockScopedBindings</c>.</summary>
    private void EnableSubstitutionsForBlockScopedBindings()
    {
        if ((_enabledSubstitutions & ES2015SubstitutionFlags.BlockScopedBindings) == 0)
        {
            _enabledSubstitutions |= ES2015SubstitutionFlags.BlockScopedBindings;
            _context.EnableSubstitution(SyntaxKind.Identifier);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4886-4904 — <c>enableSubstitutionsForCapturedThis</c>.</summary>
    private void EnableSubstitutionsForCapturedThis()
    {
        if ((_enabledSubstitutions & ES2015SubstitutionFlags.CapturedThis) == 0)
        {
            _enabledSubstitutions |= ES2015SubstitutionFlags.CapturedThis;
            _context.EnableSubstitution(SyntaxKind.ThisKeyword);
            _context.EnableEmitNotification(SyntaxKind.Constructor);
            _context.EnableEmitNotification(SyntaxKind.MethodDeclaration);
            _context.EnableEmitNotification(SyntaxKind.GetAccessor);
            _context.EnableEmitNotification(SyntaxKind.SetAccessor);
            _context.EnableEmitNotification(SyntaxKind.ArrowFunction);
            _context.EnableEmitNotification(SyntaxKind.FunctionExpression);
            _context.EnableEmitNotification(SyntaxKind.FunctionDeclaration);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4906-4921 — <c>onSubstituteNode</c>.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node);

        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression(node);
        }

        if (NodeTests.IsIdentifier(node))
        {
            return SubstituteIdentifier((Identifier)node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4923-4940 — <c>substituteIdentifier</c>.</summary>
    private Node SubstituteIdentifier(Identifier node)
    {
        // Only substitute the identifier if we have enabled substitutions for block-scoped
        // bindings.
        if ((_enabledSubstitutions & ES2015SubstitutionFlags.BlockScopedBindings) != 0 && !FactoryUtilities.IsInternalName(node))
        {
            var original = Utilities.GetParseTreeNode<Identifier>(node, NodeTests.IsIdentifier);
            if (original is not null && IsNameOfDeclarationWithCollidingName(original))
            {
                return Utilities.SetTextRange(_factory.GetGeneratedNameForNode(original, /*flags*/ null), node);
            }
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4942-4958 — <c>isNameOfDeclarationWithCollidingName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>node.parent as NamedDeclaration</c> is the model's
    /// <see cref="INamedDeclaration"/>; <c>resolver.isDeclarationWithCollidingName</c> takes an
    /// <see cref="IDeclaration"/>, which every arm's parent kind implements.</remarks>
    private bool IsNameOfDeclarationWithCollidingName(Identifier node)
    {
        switch (node.Parent!.Kind)
        {
            case SyntaxKind.BindingElement:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.VariableDeclaration:
                return ReferenceEquals(((INamedDeclaration)node.Parent!).Name, node)
                    && _resolver.IsDeclarationWithCollidingName((IDeclaration)node.Parent!);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4960-4975 — <c>substituteExpression</c>.</summary>
    private Node SubstituteExpression(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                return SubstituteExpressionIdentifier((Identifier)node);

            case SyntaxKind.ThisKeyword:
                return SubstituteThisKeyword(node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4977-4986 — <c>substituteExpressionIdentifier</c>.</summary>
    private Node SubstituteExpressionIdentifier(Identifier node)
    {
        if ((_enabledSubstitutions & ES2015SubstitutionFlags.BlockScopedBindings) != 0 && !FactoryUtilities.IsInternalName(node))
        {
            var declaration = _resolver.GetReferencedDeclarationWithCollidingName(node);
            if (declaration is not null && !(Utilities.IsClassLike((Node)declaration) && IsPartOfClassBody((Node)declaration, node)))
            {
                return Utilities.SetTextRange(
                    _factory.GetGeneratedNameForNode(Utilities.GetNameOfDeclaration((Node)declaration) as Node, /*flags*/ null),
                    node);
            }
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4988-5015 — <c>isPartOfClassBody</c>.</summary>
    private bool IsPartOfClassBody(Node declaration, Identifier node)
    {
        Node? currentNode = Utilities.GetParseTreeNode(node);
        if (currentNode is null
            || ReferenceEquals(currentNode, declaration)
            || currentNode.End <= declaration.Pos
            || currentNode.Pos >= declaration.End)
        {
            // if the node has no correlation to a parse tree node, its definitely not
            // part of the body.
            // if the node is outside of the document range of the declaration, its
            // definitely not part of the body.
            return false;
        }
        var blockScope = Utilities.GetEnclosingBlockScopeContainer(declaration);
        while (currentNode is not null)
        {
            if (ReferenceEquals(currentNode, blockScope) || ReferenceEquals(currentNode, declaration))
            {
                // if we are in the enclosing block scope of the declaration, we are definitely
                // not inside the class body.
                return false;
            }
            if (Utilities.IsClassElement(currentNode) && ReferenceEquals(currentNode.Parent, declaration))
            {
                return true;
            }
            currentNode = currentNode.Parent;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:5017-5025 — <c>substituteThisKeyword</c>.</summary>
    private Node SubstituteThisKeyword(Node node)
    {
        if ((_enabledSubstitutions & ES2015SubstitutionFlags.CapturedThis) != 0
            && (_hierarchyFacts & HierarchyFacts.CapturesThis) != 0)
        {
            return Utilities.SetTextRange(CreateCapturedThis(), node);
        }
        return node;
    }
}
