namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:1079-1094 (bindEachFunctionsFirst, bindEach, bindEachChild)
// ported from: src/compiler/binder.ts:1096-1240 (bindChildren)
/// <summary>
/// Descending into a node's children: TS's <c>bindEachFunctionsFirst</c>, <c>bindEach</c>, <c>bindEachChild</c> and
/// the per-kind <c>bindChildren</c> that decides which children are bound and in what order.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS types the statement lists <c>NodeArray&lt;Node&gt;</c> and passes them straight to <c>forEachChild</c>'s
/// <c>cbNodes</c>. Port lists are typed by their element interface (<c>SourceFile.statements</c> is
/// <c>NodeArray&lt;IStatement&gt;</c>), and <c>INodeArray&lt;out T&gt;</c> is the covariant face that accepts every one
/// of them — which is also exactly the type the generated table's <c>cbNodes</c> callback takes, so
/// <c>BindEach</c> can be passed to it as a method group. The elements are node classes at run time, so the walk casts
/// each element to <see cref="Node"/> once, the same treatment <c>getModuleInstanceStateForAliasTarget</c> documents
/// (Binder/Binder.cs).
/// <para>
/// PORT-NOTE: TS's <c>forEach</c> stops when its callback answers a truthy value; <c>bind</c> and <c>bindEach</c> answer
/// nothing, so neither walk ever stops early and the port states the <c>foreach</c> that remains.
/// </para>
/// <para>
/// PORT-NOTE: <c>bindChildren</c>'s switch dispatches into the flow graph (M5-2) and the JSDoc deferral set (M5-3) for
/// every kind whose binding is a flow concern. Those arms are the call sites the milestone's other slices fill, and
/// each is written with the narrowest parameter type the arm's own guard proves: the concrete class where TS narrows to
/// one, and for TS's unions the common face — <see cref="IStatement"/> for
/// <c>ReturnStatement | ThrowStatement</c> and <c>BreakOrContinueStatement</c>, <see cref="IterationStatement"/> for
/// <c>ForInOrForOfStatement</c>, <see cref="MemberExpression"/> for <c>AccessExpression</c>,
/// <see cref="JSDocTag"/> for the JSDoc type-alias union, <see cref="BinaryExpression"/> for <c>DestructuringAssignment</c>.
/// The arms whose binding is entirely this slice's (<c>SourceFile</c>, <c>Block</c>/<c>ModuleBlock</c>, the
/// assignment-pattern carry-over and the default walk) are ported in full.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:1079-1082 — <c>bindEachFunctionsFirst</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two callbacks answer <c>bind(n) | undefined</c>, i.e. always nothing, so both walks bind every
    /// node they are given; the port states each as the guard it is.
    /// </remarks>
    private void BindEachFunctionsFirst(INodeArray<INode>? nodes)
    {
        BindEach(nodes, n =>
        {
            if (n.Kind == SyntaxKind.FunctionDeclaration) Bind(n);
        });
        BindEach(nodes, n =>
        {
            if (n.Kind != SyntaxKind.FunctionDeclaration) Bind(n);
        });
    }

    /// <summary>ported from: src/compiler/binder.ts:1084-1090 — <c>bindEach</c>.</summary>
    private void BindEach(INodeArray<INode>? nodes, Action<Node>? bindFunction = null)
    {
        if (nodes is null)
        {
            return;
        }

        bindFunction ??= Bind;
        foreach (var node in nodes)
        {
            bindFunction((Node)node);
        }
    }

    /// <summary>ported from: src/compiler/binder.ts:1092-1094 — <c>bindEachChild</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEachChild(node, bind, bindEach)</c> has the two-callback shape; the port's void-callback
    /// overload (Parser/ForEachChildTable.Overloads.cs) is exactly it. <c>BindEach</c> is passed through a lambda
    /// rather than as a method group because its own signature takes an optional second parameter, which a method group
    /// conversion does not carry.
    /// </remarks>
    private void BindEachChild(Node node) => ForEachChildTable.ForEachChild(node, Bind, nodes => BindEach(nodes));

    /// <summary>ported from: src/compiler/binder.ts:1096-1240 — <c>bindChildren</c>.</summary>
    private void BindChildren(Node node)
    {
        var saveInAssignmentPattern = _inAssignmentPattern;
        // Most nodes aren't valid in an assignment pattern, so we clear the value here
        // and set it before we descend into nodes that could actually be part of an assignment pattern.
        _inAssignmentPattern = false;

        // Clear Unreachable flag from previous binding (for incremental scenarios)
        if (Utilities.IsPotentiallyExecutableNode(node))
        {
            node.Flags &= ~NodeFlags.Unreachable;
        }

        if (_currentFlow == _unreachableFlow)
        {
            if (Utilities.CanHaveFlowNode(node))
            {
                ((IFlowContainer)node).FlowNode = null;
            }

            if (Utilities.IsPotentiallyExecutableNode(node))
            {
                node.Flags |= NodeFlags.Unreachable;
            }

            BindEachChild(node);
            BindJSDoc(node);
            _inAssignmentPattern = saveInAssignmentPattern;
            return;
        }

        if (SyntaxKind.FirstStatement <= node.Kind && node.Kind <= SyntaxKind.LastStatement && Utilities.CanHaveFlowNode(node))
        {
            ((IFlowContainer)node).FlowNode = _currentFlow;
        }

        switch (node.Kind)
        {
            case SyntaxKind.WhileStatement:
                BindWhileStatement((WhileStatement)node);
                break;
            case SyntaxKind.DoStatement:
                BindDoStatement((DoStatement)node);
                break;
            case SyntaxKind.ForStatement:
                BindForStatement((ForStatement)node);
                break;
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                BindForInOrForOfStatement((IterationStatement)node);
                break;
            case SyntaxKind.IfStatement:
                BindIfStatement((IfStatement)node);
                break;
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.ThrowStatement:
                BindReturnOrThrow((IStatement)node);
                break;
            case SyntaxKind.BreakStatement:
            case SyntaxKind.ContinueStatement:
                BindBreakOrContinueStatement((IStatement)node);
                break;
            case SyntaxKind.TryStatement:
                BindTryStatement((TryStatement)node);
                break;
            case SyntaxKind.SwitchStatement:
                BindSwitchStatement((SwitchStatement)node);
                break;
            case SyntaxKind.CaseBlock:
                BindCaseBlock((CaseBlock)node);
                break;
            case SyntaxKind.CaseClause:
                BindCaseClause((CaseClause)node);
                break;
            case SyntaxKind.ExpressionStatement:
                BindExpressionStatement((ExpressionStatement)node);
                break;
            case SyntaxKind.LabeledStatement:
                BindLabeledStatement((LabeledStatement)node);
                break;
            case SyntaxKind.PrefixUnaryExpression:
                BindPrefixUnaryExpressionFlow((PrefixUnaryExpression)node);
                break;
            case SyntaxKind.PostfixUnaryExpression:
                BindPostfixUnaryExpressionFlow((PostfixUnaryExpression)node);
                break;
            case SyntaxKind.BinaryExpression:
                if (Utilities.IsDestructuringAssignment(node))
                {
                    // Carry over whether we are in an assignment pattern to
                    // binary expressions that could actually be an initializer
                    _inAssignmentPattern = saveInAssignmentPattern;
                    BindDestructuringAssignmentFlow((BinaryExpression)node);
                    return;
                }

                BindBinaryExpressionFlow((BinaryExpression)node);
                break;
            case SyntaxKind.DeleteExpression:
                BindDeleteExpressionFlow((DeleteExpression)node);
                break;
            case SyntaxKind.ConditionalExpression:
                BindConditionalExpressionFlow((ConditionalExpression)node);
                break;
            case SyntaxKind.VariableDeclaration:
                BindVariableDeclarationFlow((VariableDeclaration)node);
                break;
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                BindAccessExpressionFlow((MemberExpression)node);
                break;
            case SyntaxKind.CallExpression:
                BindCallExpressionFlow((CallExpression)node);
                break;
            case SyntaxKind.NonNullExpression:
                BindNonNullExpressionFlow((NonNullExpression)node);
                break;
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
                BindJSDocTypeAlias((JSDocTag)node);
                break;
            case SyntaxKind.JSDocImportTag:
                BindJSDocImportTag((JSDocImportTag)node);
                break;

            // In source files and blocks, bind functions first to match hoisting that occurs at runtime
            case SyntaxKind.SourceFile:
            {
                BindEachFunctionsFirst(((SourceFile)node).Statements);
                Bind(((SourceFile)node).EndOfFileToken);
                break;
            }

            case SyntaxKind.Block:
            case SyntaxKind.ModuleBlock:
                BindEachFunctionsFirst(((Block)node).Statements);
                break;

            case SyntaxKind.BindingElement:
                BindBindingElementFlow((BindingElement)node);
                break;
            case SyntaxKind.Parameter:
                BindParameterFlow((ParameterDeclaration)node);
                break;
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.SpreadElement:
                // Carry over whether we are in an assignment pattern of Object and Array literals
                // as well as their children that are valid assignment targets.
                _inAssignmentPattern = saveInAssignmentPattern;
                // falls through
                goto default;
            default:
                BindEachChild(node);
                break;
        }

        BindJSDoc(node);
        _inAssignmentPattern = saveInAssignmentPattern;
    }
}
