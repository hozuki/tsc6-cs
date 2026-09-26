#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2018.ts:1332-1485 — the substitution half of `transformES2018`.
/// <summary>
/// ported from: src/compiler/transformers/es2018.ts:1332-1485 — the <c>onEmitNode</c>/<c>onSubstituteNode</c> hooks
/// <c>transformES2018</c> installs, and the <c>super</c> rewrites they drive.
/// </summary>
/// <remarks>
/// PORT-NOTE (the split): the reference states these members among the rest of the factory's closures; the port keeps
/// them in this partial half because they are the only members that run after the transform walk, through the two
/// context hooks the constructor replaces.
/// </remarks>
internal sealed partial class ES2018Transformer
{
    /// <summary>ported from: src/compiler/transformers/es2018.ts:1332-1358 — <c>enableSubstitutionForAsyncMethodsWithSuper</c>.</summary>
    private void EnableSubstitutionForAsyncMethodsWithSuper()
    {
        if ((_enabledSubstitutions & ESNextSubstitutionFlags.AsyncMethodsWithSuper) == 0)
        {
            _enabledSubstitutions |= ESNextSubstitutionFlags.AsyncMethodsWithSuper;

            // We need to enable substitutions for call, property access, and element access
            // if we need to rewrite super calls.
            _context.EnableSubstitution(SyntaxKind.CallExpression);
            _context.EnableSubstitution(SyntaxKind.PropertyAccessExpression);
            _context.EnableSubstitution(SyntaxKind.ElementAccessExpression);

            // We need to be notified when entering and exiting declarations that bind super.
            _context.EnableEmitNotification(SyntaxKind.ClassDeclaration);
            _context.EnableEmitNotification(SyntaxKind.MethodDeclaration);
            _context.EnableEmitNotification(SyntaxKind.GetAccessor);
            _context.EnableEmitNotification(SyntaxKind.SetAccessor);
            _context.EnableEmitNotification(SyntaxKind.Constructor);
            // We need to be notified when entering the generated accessor arrow functions.
            _context.EnableEmitNotification(SyntaxKind.VariableStatement);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1360-1389 — <c>onEmitNode</c>.</summary>
    /// <remarks>
    /// <para>Called by the printer just before a node is printed.</para>
    /// <para>PORT-NOTE: the reference's <c>enabledSubstitutions &amp; …</c> and its <c>else if (enabledSubstitutions
    /// &amp;&amp; …)</c> are both enum tests in the port (<c>NodeCheckFlags</c> values are combined with <c>|</c> here,
    /// so the reference's <c>? x : 0 as NodeCheckFlags</c> arms state the enum's zero member).</para>
    /// </remarks>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        // If we need to support substitutions for `super` in an async method,
        // we should track it here.
        if ((_enabledSubstitutions & ESNextSubstitutionFlags.AsyncMethodsWithSuper) != 0 && IsSuperContainer(node))
        {
            var superContainerFlags = (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAccessInAsync) ? NodeCheckFlags.MethodWithSuperPropertyAccessInAsync : NodeCheckFlags.None)
                | (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync) ? NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync : NodeCheckFlags.None);
            if (superContainerFlags != _enclosingSuperContainerFlags)
            {
                var savedEnclosingSuperContainerFlags = _enclosingSuperContainerFlags;
                _enclosingSuperContainerFlags = superContainerFlags;
                _previousOnEmitNode(hint, node, emitCallback);
                _enclosingSuperContainerFlags = savedEnclosingSuperContainerFlags;
                return;
            }
        }
        // Disable substitution in the generated super accessor itself.
        else if (_enabledSubstitutions != ESNextSubstitutionFlags.None && IsSubstitutedSuperAccessor(Utilities.GetNodeId(node)))
        {
            var savedEnclosingSuperContainerFlags = _enclosingSuperContainerFlags;
            _enclosingSuperContainerFlags = NodeCheckFlags.None;
            _previousOnEmitNode(hint, node, emitCallback);
            _enclosingSuperContainerFlags = savedEnclosingSuperContainerFlags;
            return;
        }

        _previousOnEmitNode(hint, node, emitCallback);
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1391-1397 — <c>onSubstituteNode</c>.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node);
        if (hint == EmitHint.Expression && _enclosingSuperContainerFlags != NodeCheckFlags.None)
        {
            return SubstituteExpression((Expression)node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1399-1409 — <c>substituteExpression</c>.</summary>
    private Node SubstituteExpression(Expression node)
        => node.Kind switch
        {
            SyntaxKind.PropertyAccessExpression => SubstitutePropertyAccessExpression((PropertyAccessExpression)node),
            SyntaxKind.ElementAccessExpression => SubstituteElementAccessExpression((ElementAccessExpression)node),
            SyntaxKind.CallExpression => SubstituteCallExpression((CallExpression)node),
            _ => node,
        };

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1411-1422 — <c>substitutePropertyAccessExpression</c>.</summary>
    private PropertyAccessExpression SubstitutePropertyAccessExpression(PropertyAccessExpression node)
    {
        if (node.Expression!.Kind == SyntaxKind.SuperKeyword)
        {
            return Utilities.SetTextRange(
                _factory.CreatePropertyAccessExpression(
                    _factory.CreateUniqueName("_super", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel),
                    node.Name),
                node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1424-1432 — <c>substituteElementAccessExpression</c>.</summary>
    private Expression SubstituteElementAccessExpression(ElementAccessExpression node)
    {
        if (node.Expression!.Kind == SyntaxKind.SuperKeyword)
        {
            return CreateSuperElementAccessInAsyncMethod(
                node.ArgumentExpression!,
                node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1434-1450 — <c>substituteCallExpression</c>.</summary>
    private Expression SubstituteCallExpression(CallExpression node)
    {
        var expression = node.Expression!;
        if (Utilities.IsSuperProperty(expression))
        {
            var argumentExpression = NodeTests.IsPropertyAccessExpression(expression)
                ? (Expression)SubstitutePropertyAccessExpression((PropertyAccessExpression)expression)
                : SubstituteElementAccessExpression((ElementAccessExpression)expression);
            return _factory.CreateCallExpression(
                _factory.CreatePropertyAccessExpression(argumentExpression, "call"),
                /*typeArguments*/ null,
                [
                    _factory.CreateThis(),
                    .. node.Arguments!,
                ]);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1452-1459 — <c>isSuperContainer</c>.</summary>
    private static bool IsSuperContainer(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.ClassDeclaration
            || kind == SyntaxKind.Constructor
            || kind == SyntaxKind.MethodDeclaration
            || kind == SyntaxKind.GetAccessor
            || kind == SyntaxKind.SetAccessor;
    }

    /// <summary>ported from: src/compiler/transformers/es2018.ts:1461-1485 — <c>createSuperElementAccessInAsyncMethod</c>.</summary>
    /// <remarks>PORT-NOTE: unlike the ES2017 member of the same name, this one builds its element-access helper
    /// through <c>factory.createIdentifier("_superIndex")</c> (no generated name) — the reference's own text at
    /// es2018.ts:1468/1477.</remarks>
    private LeftHandSideExpression CreateSuperElementAccessInAsyncMethod(Expression argumentExpression, ITextRange location)
    {
        if ((_enclosingSuperContainerFlags & NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync) != 0)
        {
            return Utilities.SetTextRange(
                _factory.CreatePropertyAccessExpression(
                    _factory.CreateCallExpression(
                        _factory.CreateIdentifier("_superIndex"),
                        /*typeArguments*/ null,
                        [argumentExpression]),
                    "value"),
                location);
        }
        else
        {
            return Utilities.SetTextRange(
                _factory.CreateCallExpression(
                    _factory.CreateIdentifier("_superIndex"),
                    /*typeArguments*/ null,
                    [argumentExpression]),
                location);
        }
    }
}
