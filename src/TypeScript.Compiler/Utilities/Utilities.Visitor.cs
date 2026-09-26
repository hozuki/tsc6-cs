#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:9704-9705 — Visitor (the port's Func<Node, object?> face)
// ported from: src/compiler/types.ts:9714-9732 — NodeVisitor
// ported from: src/compiler/types.ts:9740-9756 — NodesVisitor
// ported from: src/compiler/visitorPublic.ts:148-176 — visitNode
// ported from: src/compiler/visitorPublic.ts:223-271 — visitNodes
// ported from: src/compiler/visitorPublic.ts:289-311 — visitArray
// ported from: src/compiler/visitorPublic.ts:328-376 — visitArrayWorker
// ported from: src/compiler/visitorPublic.ts:382-387 — visitLexicalEnvironment
// ported from: src/compiler/visitorPublic.ts:395-418 — visitParameterList
// ported from: src/compiler/visitorPublic.ts:420-434 — addDefaultValueAssignmentsIfNeeded
// ported from: src/compiler/visitorPublic.ts:436-443 — addDefaultValueAssignmentIfNeeded
// ported from: src/compiler/visitorPublic.ts:445-472 — addDefaultValueAssignmentForBindingPattern
// ported from: src/compiler/visitorPublic.ts:474-502 — addDefaultValueAssignmentForInitializer
// ported from: src/compiler/visitorPublic.ts:522-535 — visitFunctionBody
// ported from: src/compiler/visitorPublic.ts:543-557 — visitIterationBody
// ported from: src/compiler/visitorPublic.ts:564-576 — visitCommaListElements
// ported from: src/compiler/visitorPublic.ts:598-605 — visitEachChild (the port keeps checker.ts:1134's alias name)
// ported from: src/compiler/visitorPublic.ts:607-619 — VisitEachChildFunction and VisitEachChildTable
// ported from: src/compiler/visitorPublic.ts:624-1798 — visitEachChildTable, all 135 entries
// ported from: src/compiler/visitorPublic.ts:1805-1807 — extractSingleNode
// ported from: src/compiler/transformer.ts:669-696 — nullTransformationContext
// ported from: src/compiler/utilitiesPublic.ts (and utilities.ts) — the predicates this module reads that no earlier
//              slice had declared (see the last section of this file)

/// <summary>
/// ported from: src/compiler/visitorPublic.ts:1-1808 — the whole module: the visitor/copy surface
/// (<c>visitNode</c>, <c>visitNodes</c>, <c>visitArray</c>, <c>visitLexicalEnvironment</c>, <c>visitParameterList</c>,
/// <c>visitFunctionBody</c>, <c>visitIterationBody</c>, <c>visitCommaListElements</c>, <c>visitEachChild</c> and the
/// 135-kind <c>visitEachChildTable</c>), <c>extractSingleNode</c>, and the two first-class visitor function types
/// (<c>NodeVisitor</c>, <c>NodesVisitor</c>) the table is threaded with.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): every member of <c>visitorPublic.ts</c> lands on <see cref="Utilities"/> and is called
/// qualified, which is the ruling <c>Utilities.VisitEachChildWorker</c> itself already recorded. The one name that is
/// not the reference's own is that member's: TS calls it <c>visitEachChild</c> and <c>checker.ts:1134</c> imports it as
/// <c>visitEachChildWorker</c>, and the checker's three call sites (<c>checker.ts:2503</c>, <c>:7345</c>, <c>:8420</c>,
/// <c>:9271</c>) are written against the alias — so the port keeps the alias name and this file states the reference's
/// <c>visitEachChild</c> under it.
/// </para>
/// <para>
/// PORT-NOTE (the visitor faces): TS's <c>Visitor</c> is <c>(node) =&gt; VisitResult&lt;TOut&gt;</c> with
/// <c>VisitResult&lt;T&gt; = T | readonly Node[] | undefined</c>, which the port spells <c>Func&lt;Node, object?&gt;</c>
/// and discriminates the same way <c>NodeFactory.CopyCustomPrologue</c> does (<c>null</c>, then
/// <c>IEnumerable&lt;Node&gt;</c>, then a node). <c>NodeVisitor</c> and <c>NodesVisitor</c> are <em>interfaces</em> whose
/// call signatures are generic over the node type, i.e. first-class generic functions; C# cannot instantiate a generic
/// delegate per call, so the port states them as abstract classes with one generic method each (declared at the bottom
/// of this file). Every call site in the table infers the type variable from the argument it passes, which is exactly
/// what TS's inference did: the result was always the type of the node whose children were being walked, or the type the
/// <c>test</c> predicate narrows to, and those two are the same on every node in this model.
/// </para>
/// <para>
/// PORT-NOTE (<c>visitEachChild</c>'s context slot): the reference's default is <c>nullTransformationContext</c>
/// (transformer.ts:669), whose factory is the real one and whose lexical-environment members are no-ops. The checker's
/// call sites pass <c>/*context*/ undefined</c>, so <see cref="NullTransformationContext"/> is what they run against —
/// which is why the table can build nodes while the transformer layer (M7-B) is still unported.
/// </para>
/// <para>
/// PORT-NOTE (the table): TS stores one anonymous function per <c>SyntaxKind</c> in an object literal and dispatches
/// with <c>fn === undefined ? node : fn(...)</c>; the port states the same table as a <c>switch</c> expression over
/// <c>node.Kind</c> whose <c>_ =&gt; node</c> arm is that fallback, with one private method per entry so each keeps the
/// reference's own function name and its <c>ported from</c> line. The entries' <c>_nodesVisitor</c>/<c>_nodeVisitor</c>/
/// <c>_tokenVisitor</c> parameters are dropped where the reference never reads them; the ones a body does read keep the
/// reference's name and position.
/// </para>
/// <para>
/// PORT-NOTE (<c>NodeArray</c> identity): the reference's <c>visitNodes</c> hands back the <em>same array object</em>
/// when no element changed, and the factory's <c>update*</c> family returns the original node when every argument is
/// reference-equal to the original's slot. Both carry through the port unchanged: <see cref="VisitNodes{TNode}"/> takes
/// and returns <c>NodeArray&lt;TNode&gt;</c> (not <c>NodeArray&lt;Node&gt;</c>) so no copy is materialized, and
/// <see cref="VisitArrayWorker{TNode}"/> returns its input when nothing changed instead of a sentinel.
/// </para>
/// <para>
/// PORT-NOTE (assertions): <c>debug.ts</c>'s <c>assertNode</c> is the landed <see cref="Debug.AssertNode"/>;
/// <c>assertEachNode</c> had no ported member, so <see cref="AssertEachNode"/> states it here, with the same
/// <c>"Unexpected node."</c> fallback <see cref="Debug.AssertNode"/> uses.
/// </para>
/// <para>
/// PORT-NOTE (the <c>lift</c> slot): the four statement arms that pass <c>context.factory.liftToBlock</c> as the
/// visitor's lift state the same two conversions <see cref="VisitIterationBody"/> documents — the list has to be
/// materialized for the factory's <c>List&lt;Node&gt;</c> parameter and its <c>IStatement</c> answer cast back to the
/// class the walker's nodes are — because the port's <c>lift</c> slot is the reference's
/// <c>(node: readonly Node[]) =&gt; Node</c> and C# cannot adapt the method group. The copy is inert:
/// <c>liftToBlock</c> reads its argument once and answers a node it builds itself.
/// </para>
/// </remarks>

internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:148-176 — visitNode
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:172 — the <c>isArray(visited)</c> test.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isArray</c> answers for the JS array a visitor may return; in the visitor it is always either
    /// a <c>Node</c> or a node array, and the port's node arrays are <c>List&lt;T&gt;</c>/<c>NodeArray&lt;T&gt;</c>. The
    /// non-generic <c>IEnumerable</c> test is what covers the interface-typed element lists
    /// (<c>NodeArray&lt;IStatement&gt;</c>), which <c>IEnumerable&lt;Node&gt;</c> does not (invariance); <c>Node</c> is
    /// excluded because a node is never the array.
    /// </remarks>
    private static bool IsNodeArray(object? value) => value is not Node and System.Collections.IEnumerable;

    /// <summary>ported from: src/compiler/visitorPublic.ts:148-176 — <c>visitNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares three overloads whose only difference is the precision of the return type (the two
    /// generic ones refine it through the <c>test</c> predicate). C# states the implementation overload once and lets
    /// the type variable carry what <c>TIn | TVisited | TOut</c> carried. The one conversion the model's
    /// class/interface split needs is <c>(Node)(object)</c>, because <c>TNode</c> is only known to implement
    /// <see cref="INode"/> and the visitor slot is typed over the <see cref="Node"/> class (contract §3 — the same
    /// boundary <c>NodeFactory.CopyCustomPrologue</c> and <c>SyntacticNodeBuilder.Engine</c> state).
    /// </remarks>
    public static TNode? VisitNode<TNode>(
        TNode? node,
        Func<Node, object?> visitor,
        Func<Node, bool>? test = null,
        Func<IReadOnlyList<Node>, Node?>? lift = null)
        where TNode : class, INode
    {
        if (node is null)
        {
            // If the input type is undefined, then the output type can be undefined.
            return null;
        }

        var visited = visitor((Node)(object)node);

        Node? visitedNode;
        if (visited is null)
        {
            // If the visited node is undefined, then the visitor must have returned undefined,
            // so the visitor must have been declared as able to return undefined, so TOut must be
            // potentially undefined.
            return null;
        }
        else if (IsNodeArray(visited))
        {
            // PORT-NOTE: TS's `isArray(visited)` (visitorPublic.ts:172) is a runtime array test over `NodeArray<T>`
            // (an array subclass), so every element type matches. The port's node arrays are `List<T>`/`NodeArray<T>`,
            // and a `NodeArray<IStatement>` is *not* an `IEnumerable<Node>` (generics are invariant), which is what
            // `IsNodeArray` exists for. The list is passed on directly when it already is a `List<Node>`, otherwise the
            // elements are cast one by one — every one of them is a `Node` at run time.
            var visitedList = visited as IReadOnlyList<Node> ?? [.. ((System.Collections.IEnumerable)visited).Cast<object?>().Select(item => (Node)item!)];
            visitedNode = (lift ?? ExtractSingleNode)(visitedList);
        }
        else
        {
            visitedNode = (Node)visited;
        }

        Debug.AssertNode(visitedNode, test);
        return visitedNode is null ? null : (TNode)(object)visitedNode;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:1805-1807 — <c>extractSingleNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return is <c>Node | undefined</c> and its body is <c>singleOrUndefined(nodes)</c>, whose
    /// <c>IList&lt;T&gt;</c> parameter an <c>IReadOnlyList&lt;Node&gt;</c> argument cannot be passed to without a copy;
    /// the body is therefore the reference's own expression. TS keeps this function module-private; the port makes it
    /// public because <c>SyntacticNodeBuilder.Engine</c>'s file-local copy is deleted in favour of it.
    /// </remarks>
    public static Node? ExtractSingleNode(IReadOnlyList<Node> nodes)
    {
        Debug.Assert(nodes.Count <= 1, "Too many nodes written to output.");
        return nodes.Count == 1 ? nodes[0] : null;
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:223-271 (visitNodes), :289-311 (visitArray), :328-376 (visitArrayWorker)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:223-271 — <c>visitNodes</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's two generic overloads differ from the implementation overload only in the return type's
    /// precision, so C# states one member, generic in the array's element type. That element type is the array's own
    /// (the reference's <c>TIn</c>), which is what every caller in the table stores back into the same slot it read
    /// from; where the reference's <c>TOut</c> (the <c>test</c> predicate's type) is narrower than <c>TIn</c>, the two
    /// are the same node type on this model, so no call site needs a cast.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's sparse-array guard (<c>node !== undefined</c>) cannot occur — a <c>NodeArray&lt;T&gt;</c> is a
    /// dense <c>List&lt;T&gt;</c> — while the <c>visited === undefined</c> half is kept exactly: a visitor that returns
    /// <c>null</c> makes the element <em>disappear</em> from the copy, which is the reference's own behaviour (the
    /// <c>if (visited)</c> block has no <c>else</c>). <c>Visitor</c>'s <c>undefined</c> is the port's <c>null</c>.
    /// </para>
    /// </remarks>
    public static NodeArray<TNode>? VisitNodes<TNode>(
        NodeArray<TNode>? nodes,
        Func<Node, object?>? visitor,
        Func<Node, bool>? test = null,
        int? start = null,
        int? count = null)
        where TNode : class, INode
    {
        if (nodes is null)
        {
            // If the input type is undefined, then the output type can be undefined.
            return nodes;
        }

        // Ensure start and count have valid values
        var length = nodes.Count;
        if (start is null || start < 0)
        {
            start = 0;
        }

        if (count is null || count > length - start)
        {
            count = length - start;
        }

        bool? hasTrailingComma;
        var pos = -1;
        var end = -1;
        if (start > 0 || count < length)
        {
            // Since this is a fragment of a node array, we do not copy over the previous location
            // and will only copy over `hasTrailingComma` if we are including the last element.
            hasTrailingComma = nodes.HasTrailingComma && start + count == length;
        }
        else
        {
            pos = nodes.Pos;
            end = nodes.End;
            hasTrailingComma = nodes.HasTrailingComma;
        }

        var updated = VisitArrayWorker(nodes, visitor, test, start.Value, count.Value);
        if (!ReferenceEquals(updated, nodes))
        {
            // TODO(rbuckton): Remove dependency on `ts.factory` in favor of a provided factory.
            var updatedArray = NodeFactory.Factory.CreateNodeArray(updated, hasTrailingComma);
            SetTextRangePosEnd(updatedArray, pos, end);
            return updatedArray;
        }

        // If we are here, updated === nodes. This means that it's still a NodeArray,
        // and also that its contents passed the tests in visitArrayWorker, so has contents
        // of type TOut.
        return nodes;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:289-311 — <c>visitArray</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>readonly TIn[]</c> is the port's <c>NodeArray&lt;TNode&gt;</c> — every array of nodes in the
    /// port is a <c>NodeArray</c> (docs/architecture.md §12.9), which is also what makes the shared worker's
    /// <c>slice</c> a <c>GetRange</c>.
    /// </remarks>
    public static IReadOnlyList<TNode>? VisitArray<TNode>(
        NodeArray<TNode>? nodes,
        Func<Node, object?>? visitor,
        Func<Node, bool>? test = null,
        int? start = null,
        int? count = null)
        where TNode : class, INode
    {
        if (nodes is null)
        {
            // If the input type is undefined, then the output type can be undefined.
            return nodes;
        }

        // Ensure start and count have valid values
        var length = nodes.Count;
        if (start is null || start < 0)
        {
            start = 0;
        }

        if (count is null || count > length - start)
        {
            count = length - start;
        }

        return VisitArrayWorker(nodes, visitor, test, start.Value, count.Value);
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:328-376 — <c>visitArrayWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns the input array itself when nothing changed and its callers compare identity; the port does
    /// the same (the return type is the input's <c>List&lt;TNode&gt;</c> base, so returning <c>nodes</c> is that object)
    /// rather than a sentinel, which is what preserves <c>visitNodes</c>'s "unchanged array comes back identical"
    /// contract. TS's <c>Debug.assertEachNode</c> is <see cref="AssertEachNode"/>.
    /// </remarks>
    private static List<TNode> VisitArrayWorker<TNode>(
        List<TNode> nodes,
        Func<Node, object?>? visitor,
        Func<Node, bool>? test,
        int start,
        int count)
        where TNode : class, INode
    {
        List<TNode>? updated = null;

        var length = nodes.Count;
        if (start > 0 || count < length)
        {
            // If we are not visiting all of the original nodes, we must always create a new array.
            updated = [];
        }

        // Visit each original node.
        for (var i = 0; i < count; i++)
        {
            var node = nodes[i + start];
            var visited = visitor is not null ? visitor((Node)(object)node) : (object?)node;
            if (updated is not null || visited is null || !ReferenceEquals(visited, node))
            {
                if (updated is null)
                {
                    // Ensure we have a copy of `nodes`, up to the current index.
                    updated = nodes.GetRange(0, i);
                    AssertEachNode(updated, test);
                }
                if (visited is not null)
                {
                    if (IsNodeArray(visited))
                    {
                        foreach (var item in (System.Collections.IEnumerable)visited)
                        {
                            var visitedNode = (Node)item!;
                            Debug.AssertNode(visitedNode, test);
                            updated.Add((TNode)(object)visitedNode);
                        }
                    }
                    else
                    {
                        var visitedNode = (Node)visited;
                        Debug.AssertNode(visitedNode, test);
                        updated.Add((TNode)(object)visitedNode);
                    }
                }
            }
        }

        if (updated is not null)
        {
            // If we have an updated array, then all items will have been tested.
            return updated;
        }

        // If we are going to return the original array, ensure it passes the test.
        AssertEachNode(nodes, test);
        return nodes;
    }

    /// <summary>ported from: src/compiler/debug.ts:307-318 — <c>assertEachNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>debug.ts</c>'s <c>assertEachNode</c> had no landed counterpart (the same gap
    /// <c>Utilities.VisitNodes</c> states for <c>assertNode</c>). TS's <c>readonly T[]</c> is
    /// <c>IEnumerable&lt;INode&gt;</c> so that any node array of any element type can be passed without a copy, and the
    /// <c>Node</c> the assertion takes is the cast §3 licenses.
    /// </remarks>
    private static void AssertEachNode(IEnumerable<INode> nodes, Func<Node, bool>? test)
    {
        foreach (var node in nodes)
        {
            Debug.AssertNode((Node)(Node)(object)node, test);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:382-387 — visitLexicalEnvironment
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:382-387 — <c>visitLexicalEnvironment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>nodesVisitor: NodesVisitor = visitNodes</c> default is the port's <c>null</c>-means-default
    /// slot (C# cannot name <see cref="DefaultNodesVisitor.Instance"/> in a parameter initializer of a partial class
    /// declared before it); the reference's module-level <c>factory</c> is <see cref="NodeFactory.Factory"/>, since
    /// <c>mergeLexicalEnvironment</c> and <c>ensureUseStrict</c> are the singleton's, not the context's.
    /// </remarks>
    public static NodeArray<IStatement> VisitLexicalEnvironment(
        NodeArray<IStatement> statements,
        Func<Node, object?> visitor,
        IVisitorTransformationContext context,
        int? start = null,
        bool? ensureUseStrict = null,
        NodesVisitor? nodesVisitor = null)
    {
        context.StartLexicalEnvironment();
        statements = (nodesVisitor ?? DefaultNodesVisitor.Instance).Visit(statements, visitor, Utilities.IsStatement, start)!;
        if (ensureUseStrict == true) statements = NodeFactory.Factory.EnsureUseStrict(statements);
        return NodeFactory.Factory.MergeLexicalEnvironment(statements, context.EndLexicalEnvironment());
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:395-502 — visitParameterList and the addDefaultValueAssignment family
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:395-418 — <c>visitParameterList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads (the second admits <c>undefined</c>); C# states the wider one. The
    /// <c>nodesVisitor</c> slot keeps the reference's shape, and <c>getEmitScriptTarget</c> is the ported
    /// <see cref="GetEmitScriptTarget"/> over the context's options.
    /// </remarks>
    public static NodeArray<ParameterDeclaration>? VisitParameterList(
        NodeArray<ParameterDeclaration>? nodes,
        Func<Node, object?> visitor,
        IVisitorTransformationContext context,
        NodesVisitor? nodesVisitor = null)
    {
        NodeArray<ParameterDeclaration>? updated = null;
        context.StartLexicalEnvironment();
        if (nodes is not null)
        {
            context.SetLexicalEnvironmentFlags(LexicalEnvironmentFlags.InParameters, true);
            updated = (nodesVisitor ?? DefaultNodesVisitor.Instance).Visit(nodes, visitor, NodeTests.IsParameter);

            // As of ES2015, any runtime execution of that occurs in for a parameter (such as evaluating an
            // initializer or a binding pattern), occurs in its own lexical scope. As a result, any expression
            // that we might transform that introduces a temporary variable would fail as the temporary variable
            // exists in a different lexical scope. To address this, we move any binding patterns and initializers
            // in a parameter list to the body if we detect a variable being hoisted while visiting a parameter list
            // when the emit target is greater than ES2015.
            if (
                (context.GetLexicalEnvironmentFlags() & LexicalEnvironmentFlags.VariablesHoistedInParameters) != 0
                && GetEmitScriptTarget(context.GetCompilerOptions()) >= ScriptTarget.ES2015)
            {
                updated = AddDefaultValueAssignmentsIfNeeded(updated!, context);
            }
            context.SetLexicalEnvironmentFlags(LexicalEnvironmentFlags.InParameters, false);
        }
        context.SuspendLexicalEnvironment();
        return updated;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:420-434 — <c>addDefaultValueAssignmentsIfNeeded</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>result: ParameterDeclaration[] | undefined</c> is <c>List&lt;ParameterDeclaration&gt;?</c> and
    /// <c>parameters.slice(0, i)</c> is <c>GetRange(0, i)</c> (the array's element type, not a copy into a new one).
    /// </remarks>
    private static NodeArray<ParameterDeclaration> AddDefaultValueAssignmentsIfNeeded(NodeArray<ParameterDeclaration> parameters, IVisitorTransformationContext context)
    {
        List<ParameterDeclaration>? result = null;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var updated = AddDefaultValueAssignmentIfNeeded(parameter, context);
            if (result is not null || !ReferenceEquals(updated, parameter))
            {
                result ??= parameters.GetRange(0, i);
                result[i] = updated;
            }
        }
        if (result is not null)
        {
            return SetTextRange(context.Factory!.CreateNodeArray(result, parameters.HasTrailingComma), parameters);
        }
        return parameters;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:436-443 — <c>addDefaultValueAssignmentIfNeeded</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's chain of truthiness tests is a chain of <c>is not null</c> tests here; <c>parameter.name</c> is
    /// the <c>BindingName</c> union, i.e. a node slot.
    /// </remarks>
    private static ParameterDeclaration AddDefaultValueAssignmentIfNeeded(ParameterDeclaration parameter, IVisitorTransformationContext context)
    {
        // A rest parameter cannot have a binding pattern or an initializer,
        // so let's just ignore it.
        return parameter.DotDotDotToken is not null ? parameter
            : Utilities.IsBindingPattern(parameter.Name!) ? AddDefaultValueAssignmentForBindingPattern(parameter, context)
            : parameter.Initializer is not null ? AddDefaultValueAssignmentForInitializer(parameter, (Identifier)parameter.Name!, parameter.Initializer, context)
            : parameter;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:445-472 — <c>addDefaultValueAssignmentForBindingPattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference destructures <c>const { factory } = context</c>; the port reads the same slot once.
    /// <c>createVariableDeclarationList([...])</c> is the two-argument member (the reference passes no <c>flags</c>).
    /// </remarks>
    private static ParameterDeclaration AddDefaultValueAssignmentForBindingPattern(ParameterDeclaration parameter, IVisitorTransformationContext context)
    {
        var factory = context.Factory!;
        context.AddInitializationStatement(
            factory.CreateVariableStatement(
                /*modifiers*/ null,
                factory.CreateVariableDeclarationList(
                    [
                        factory.CreateVariableDeclaration(
                            parameter.Name!,
                            /*exclamationToken*/ null,
                            parameter.Type,
                            parameter.Initializer is not null
                                ? factory.CreateConditionalExpression(
                                    factory.CreateStrictEquality(
                                        factory.GetGeneratedNameForNode(parameter, /*flags*/ null),
                                        factory.CreateVoidZero()),
                                    /*questionToken*/ null,
                                    parameter.Initializer,
                                    /*colonToken*/ null,
                                    factory.GetGeneratedNameForNode(parameter, /*flags*/ null))
                                : factory.GetGeneratedNameForNode(parameter, /*flags*/ null)),
                    ],
                    /*flags*/ null)));
        return factory.UpdateParameterDeclaration(parameter, parameter.Modifiers, parameter.DotDotDotToken, factory.GetGeneratedNameForNode(parameter, /*flags*/ null), parameter.QuestionToken, parameter.Type, /*initializer*/ null);
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:474-502 — <c>addDefaultValueAssignmentForInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the nesting and the order are the reference's own: the assignment carries the parameter as its text
    /// range and <c>NoComments</c>, the block the same, and the initializer's flags are OR-ed as the reference writes
    /// them (<c>NoSourceMap | getEmitFlags(initializer) | NoComments</c>).
    /// </remarks>
    private static ParameterDeclaration AddDefaultValueAssignmentForInitializer(ParameterDeclaration parameter, Identifier name, Expression initializer, IVisitorTransformationContext context)
    {
        var factory = context.Factory!;
        context.AddInitializationStatement(
            factory.CreateIfStatement(
                factory.CreateTypeCheck(factory.CloneNode(name), "undefined"),
                EmitNodes.SetEmitFlags(
                    SetTextRange(
                        factory.CreateBlock(
                            [
                                factory.CreateExpressionStatement(
                                    EmitNodes.SetEmitFlags(
                                        SetTextRange(
                                            factory.CreateAssignment(
                                                EmitNodes.SetEmitFlags(factory.CloneNode(name), EmitFlags.NoSourceMap),
                                                EmitNodes.SetEmitFlags(initializer, EmitFlags.NoSourceMap | GetEmitFlags(initializer) | EmitFlags.NoComments)),
                                            parameter),
                                        EmitFlags.NoComments)),
                            ],
                            /*multiLine*/ null),
                        parameter),
                    EmitFlags.SingleLine | EmitFlags.NoTrailingSourceMap | EmitFlags.NoTokenSourceMaps | EmitFlags.NoComments),
                /*elseStatement*/ null));
        return factory.UpdateParameterDeclaration(parameter, parameter.Modifiers, parameter.DotDotDotToken, parameter.Name, parameter.QuestionToken, parameter.Type, /*initializer*/ null);
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:522-535 — visitFunctionBody
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:522-535 — <c>visitFunctionBody</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's six overloads differ only in whether the body is a <c>FunctionBody</c>, a <c>ConciseBody</c> or
    /// <c>undefined</c>, and in whether the caller supplies a <c>nodeVisitor</c>; the port states one member, generic
    /// in the body's own type, which is the type every overload returned for its own input. <c>some(declarations)</c> is
    /// <see cref="Core.Some{T}"/>; <c>converters.convertToFunctionBlock</c> is the factory's converter.
    /// </remarks>
    public static TNode? VisitFunctionBody<TNode>(
        TNode? node,
        Func<Node, object?> visitor,
        IVisitorTransformationContext context,
        NodeVisitor? nodeVisitor = null)
        where TNode : class, INode
    {
        context.ResumeLexicalEnvironment();
        var updated = (nodeVisitor ?? DefaultNodeVisitor.Instance).Visit(node, visitor, Utilities.IsConciseBody);
        var declarations = context.EndLexicalEnvironment();
        if (Core.Some(declarations))
        {
            if (updated is null)
            {
                return (TNode)(object)(Node)context.Factory!.CreateBlock(declarations, /*multiLine*/ null);
            }
            var block = context.Factory!.Converters!.ConvertToFunctionBlock((Node)(object)updated, /*multiLine*/ null);
            var statements = NodeFactory.Factory.MergeLexicalEnvironment(block.Statements, declarations);
            return (TNode)(object)(Node)context.Factory!.UpdateBlock(block, statements);
        }
        return updated;
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:543-557 — visitIterationBody
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:543-557 — <c>visitIterationBody</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>lift</c> argument is <c>context.factory.liftToBlock</c>, whose ported signature takes
    /// <c>List&lt;Node&gt;</c> and answers <c>IStatement</c>; the visitor's <c>lift</c> slot is
    /// <c>Func&lt;IReadOnlyList&lt;Node&gt;, Node?&gt;</c> (the reference's <c>readonly Node[] =&gt; Node</c>), so each
    /// call site states the two conversions §3 fixes — the list has to be materialized and the statement cast back to
    /// the class the walker's nodes are. The copy is inert: <c>liftToBlock</c> reads its argument once and answers a node
    /// it builds itself.
    /// </remarks>
    public static IStatement VisitIterationBody(
        IStatement body,
        Func<Node, object?> visitor,
        IVisitorTransformationContext context,
        NodeVisitor? nodeVisitor = null)
    {
        context.StartBlockScope();
        var updated = (nodeVisitor ?? DefaultNodeVisitor.Instance).Visit(
            body,
            visitor,
            Utilities.IsStatement,
            n => (Node)context.Factory!.LiftToBlock([.. n]))!;
        Debug.Assert(updated is not null);
        var declarations = context.EndBlockScope();
        if (Core.Some(declarations))
        {
            if (NodeTests.IsBlock((Node)updated))
            {
                declarations!.AddRange(((Block)updated).Statements!);
                return context.Factory!.UpdateBlock((Block)updated, declarations);
            }
            declarations!.Add(updated);
            return context.Factory!.CreateBlock(declarations, /*multiLine*/ null);
        }
        return updated;
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:564-576 — visitCommaListElements
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:564-576 — <c>visitCommaListElements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>discardVisitor: Visitor = visitor</c> default is the port's <c>null</c>-means-"the visitor"
    /// slot; the reference's identity test is kept alongside it, so a caller that passes the same delegate explicitly
    /// takes the same branch.
    /// </remarks>
    public static NodeArray<Expression> VisitCommaListElements(
        NodeArray<Expression> elements,
        Func<Node, object?> visitor,
        Func<Node, object?>? discardVisitor = null)
    {
        if (discardVisitor is null || ReferenceEquals(discardVisitor, visitor) || elements.Count <= 1)
        {
            return VisitNodes(elements, visitor, Utilities.IsExpression)!;
        }

        var i = 0;
        var length = elements.Count;
        return VisitNodes(elements, node =>
        {
            var discarded = i < length - 1;
            i++;
            return discarded ? discardVisitor(node) : visitor(node);
        }, Utilities.IsExpression)!;
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:598-605 — visitEachChild (the name checker.ts:1134 aliases it to)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:598-605 — <c>visitEachChild</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's <c>node === undefined</c> arm is unreachable for this parameter, which is non-nullable
    /// because all four ported call sites hold a node (<c>checker.ts:2503</c>, <c>:7345</c>, <c>:8420</c>, <c>:9271</c>);
    /// TS's <c>T | undefined</c> overload is the one that admits <c>undefined</c>, and the port's node builder records
    /// the same narrowing for <c>getSynthesizedDeepClone</c>. <c>visitEachChildTable[node.kind]</c> is the
    /// <c>switch</c> below, whose <c>_ =&gt; node</c> arm is the reference's <c>fn === undefined ? node : …</c> — a node
    /// kind with no children (and every token) comes back as the node itself, unchanged.
    /// </para>
    /// <para>
    /// PORT-NOTE: the three slots after <c>context</c> keep the reference's order — <c>nodesVisitor</c>,
    /// <c>tokenVisitor</c>, <c>nodeVisitor</c> — and its defaults (<c>visitNodes</c> and <c>visitNode</c>, both stated
    /// here as their singleton implementations). The returned node is the input's own type (TS's <c>&lt;T extends Node&gt;
    /// (node: T, …): T</c>), which is why every table entry builds its node with the factory's same-kind
    /// <c>update*</c>; the <c>(TNode)(object)</c> at the end is the same §3 conversion the walker's nodes need.
    /// </para>
    /// </remarks>
    public static TNode VisitEachChildWorker<TNode>(
        TNode node,
        Func<Node, object?> visitor,
        IVisitorTransformationContext? context = null,
        NodesVisitor? nodesVisitor = null,
        Func<Node, object?>? tokenVisitor = null,
        NodeVisitor? nodeVisitor = null)
        where TNode : Node
    {
        if (node is null)
        {
            // If the input type is undefined, then the output type can be undefined.
            return null!;
        }

        context ??= NullTransformationContext.Instance;
        nodesVisitor ??= DefaultNodesVisitor.Instance;
        nodeVisitor ??= DefaultNodeVisitor.Instance;

        Node updated = node.Kind switch
        {
            // ported from: src/compiler/visitorPublic.ts:1246
            SyntaxKind.QualifiedName => VisitEachChildOfQualifiedName((QualifiedName)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1254
            SyntaxKind.ComputedPropertyName => VisitEachChildOfComputedPropertyName((ComputedPropertyName)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1262
            SyntaxKind.TypeParameter => VisitEachChildOfTypeParameterDeclaration((TypeParameterDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1272
            SyntaxKind.Parameter => VisitEachChildOfParameterDeclaration((ParameterDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1284
            SyntaxKind.Decorator => VisitEachChildOfDecorator((Decorator)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1292
            SyntaxKind.PropertySignature => VisitEachChildOfPropertySignature((PropertySignature)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1302
            SyntaxKind.PropertyDeclaration => VisitEachChildOfPropertyDeclaration((PropertyDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1314
            SyntaxKind.MethodSignature => VisitEachChildOfMethodSignature((MethodSignature)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1326
            SyntaxKind.MethodDeclaration => VisitEachChildOfMethodDeclaration((MethodDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1340
            SyntaxKind.Constructor => VisitEachChildOfConstructorDeclaration((ConstructorDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1349
            SyntaxKind.GetAccessor => VisitEachChildOfGetAccessorDeclaration((GetAccessorDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1360
            SyntaxKind.SetAccessor => VisitEachChildOfSetAccessorDeclaration((SetAccessorDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1370
            SyntaxKind.ClassStaticBlockDeclaration => VisitEachChildOfClassStaticBlockDeclaration((ClassStaticBlockDeclaration)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1379
            SyntaxKind.CallSignature => VisitEachChildOfCallSignatureDeclaration((CallSignatureDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1388
            SyntaxKind.ConstructSignature => VisitEachChildOfConstructSignatureDeclaration((ConstructSignatureDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1397
            SyntaxKind.IndexSignature => VisitEachChildOfIndexSignatureDeclaration((IndexSignatureDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1407
            SyntaxKind.TypePredicate => VisitEachChildOfTypePredicateNode((TypePredicateNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1416
            SyntaxKind.TypeReference => VisitEachChildOfTypeReferenceNode((TypeReferenceNode)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1424
            SyntaxKind.FunctionType => VisitEachChildOfFunctionTypeNode((FunctionTypeNode)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1433
            SyntaxKind.ConstructorType => VisitEachChildOfConstructorTypeNode((ConstructorTypeNode)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1443
            SyntaxKind.TypeQuery => VisitEachChildOfTypeQueryNode((TypeQueryNode)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1451
            SyntaxKind.TypeLiteral => VisitEachChildOfTypeLiteralNode((TypeLiteralNode)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1458
            SyntaxKind.ArrayType => VisitEachChildOfArrayTypeNode((ArrayTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1465
            SyntaxKind.TupleType => VisitEachChildOfTupleTypeNode((TupleTypeNode)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1472
            SyntaxKind.OptionalType => VisitEachChildOfOptionalTypeNode((OptionalTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1479
            SyntaxKind.RestType => VisitEachChildOfRestTypeNode((RestTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1486
            SyntaxKind.UnionType => VisitEachChildOfUnionTypeNode((UnionTypeNode)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1493
            SyntaxKind.IntersectionType => VisitEachChildOfIntersectionTypeNode((IntersectionTypeNode)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1500
            SyntaxKind.ConditionalType => VisitEachChildOfConditionalTypeNode((ConditionalTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1510
            SyntaxKind.InferType => VisitEachChildOfInferTypeNode((InferTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1517
            SyntaxKind.ImportType => VisitEachChildOfImportTypeNode((ImportTypeNode)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1528
            SyntaxKind.ImportTypeAssertionContainer => VisitEachChildOfImportTypeAssertionContainer((ImportTypeAssertionContainer)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1536
            SyntaxKind.NamedTupleMember => VisitEachChildOfNamedTupleMember((NamedTupleMember)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1546
            SyntaxKind.ParenthesizedType => VisitEachChildOfParenthesizedType((ParenthesizedTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1553
            SyntaxKind.TypeOperator => VisitEachChildOfTypeOperatorNode((TypeOperatorNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1560
            SyntaxKind.IndexedAccessType => VisitEachChildOfIndexedAccessType((IndexedAccessTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1568
            SyntaxKind.MappedType => VisitEachChildOfMappedType((MappedTypeNode)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1580
            SyntaxKind.LiteralType => VisitEachChildOfLiteralTypeNode((LiteralTypeNode)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1587
            SyntaxKind.TemplateLiteralType => VisitEachChildOfTemplateLiteralType((TemplateLiteralTypeNode)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1595
            SyntaxKind.TemplateLiteralTypeSpan => VisitEachChildOfTemplateLiteralTypeSpan((TemplateLiteralTypeSpan)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1604
            SyntaxKind.ObjectBindingPattern => VisitEachChildOfObjectBindingPattern((ObjectBindingPattern)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1611
            SyntaxKind.ArrayBindingPattern => VisitEachChildOfArrayBindingPattern((ArrayBindingPattern)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1618
            SyntaxKind.BindingElement => VisitEachChildOfBindingElement((BindingElement)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1629
            SyntaxKind.ArrayLiteralExpression => VisitEachChildOfArrayLiteralExpression((ArrayLiteralExpression)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1636
            SyntaxKind.ObjectLiteralExpression => VisitEachChildOfObjectLiteralExpression((ObjectLiteralExpression)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1643
            SyntaxKind.PropertyAccessExpression => VisitEachChildOfPropertyAccessExpression((PropertyAccessExpression)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1658
            SyntaxKind.ElementAccessExpression => VisitEachChildOfElementAccessExpression((ElementAccessExpression)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1673
            SyntaxKind.CallExpression => VisitEachChildOfCallExpression((CallExpression)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1690
            SyntaxKind.NewExpression => VisitEachChildOfNewExpression((NewExpression)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1699
            SyntaxKind.TaggedTemplateExpression => VisitEachChildOfTaggedTemplateExpression((TaggedTemplateExpression)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1708
            SyntaxKind.TypeAssertionExpression => VisitEachChildOfTypeAssertionExpression((TypeAssertion)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1716
            SyntaxKind.ParenthesizedExpression => VisitEachChildOfParenthesizedExpression((ParenthesizedExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1723
            SyntaxKind.FunctionExpression => VisitEachChildOfFunctionExpression((FunctionExpression)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1736
            SyntaxKind.ArrowFunction => VisitEachChildOfArrowFunction((ArrowFunction)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1748
            SyntaxKind.DeleteExpression => VisitEachChildOfDeleteExpression((DeleteExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1755
            SyntaxKind.TypeOfExpression => VisitEachChildOfTypeOfExpression((TypeOfExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1762
            SyntaxKind.VoidExpression => VisitEachChildOfVoidExpression((VoidExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1769
            SyntaxKind.AwaitExpression => VisitEachChildOfAwaitExpression((AwaitExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1776
            SyntaxKind.PrefixUnaryExpression => VisitEachChildOfPrefixUnaryExpression((JsonMinusNumericLiteral)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1783
            SyntaxKind.PostfixUnaryExpression => VisitEachChildOfPostfixUnaryExpression((PostfixUnaryExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1790
            SyntaxKind.BinaryExpression => VisitEachChildOfBinaryExpression((BinaryExpression)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1799
            SyntaxKind.ConditionalExpression => VisitEachChildOfConditionalExpression((ConditionalExpression)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1810
            SyntaxKind.TemplateExpression => VisitEachChildOfTemplateExpression((TemplateExpression)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1818
            SyntaxKind.YieldExpression => VisitEachChildOfYieldExpression((YieldExpression)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1826
            SyntaxKind.SpreadElement => VisitEachChildOfSpreadElement((SpreadElement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1833
            SyntaxKind.ClassExpression => VisitEachChildOfClassExpression((ClassExpression)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1844
            SyntaxKind.ExpressionWithTypeArguments => VisitEachChildOfExpressionWithTypeArguments((ExpressionWithTypeArguments)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1852
            SyntaxKind.AsExpression => VisitEachChildOfAsExpression((AsExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1860
            SyntaxKind.SatisfiesExpression => VisitEachChildOfSatisfiesExpression((SatisfiesExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1868
            SyntaxKind.NonNullExpression => VisitEachChildOfNonNullExpression((NonNullExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1880
            SyntaxKind.MetaProperty => VisitEachChildOfMetaProperty((MetaProperty)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1888
            SyntaxKind.TemplateSpan => VisitEachChildOfTemplateSpan((TemplateSpan)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1897
            SyntaxKind.Block => VisitEachChildOfBlock((Block)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:1904
            SyntaxKind.VariableStatement => VisitEachChildOfVariableStatement((VariableStatement)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1912
            SyntaxKind.ExpressionStatement => VisitEachChildOfExpressionStatement((ExpressionStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1919
            SyntaxKind.IfStatement => VisitEachChildOfIfStatement((IfStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1928
            SyntaxKind.DoStatement => VisitEachChildOfDoStatement((DoStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1936
            SyntaxKind.WhileStatement => VisitEachChildOfWhileStatement((WhileStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1944
            SyntaxKind.ForStatement => VisitEachChildOfForStatement((ForStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1954
            SyntaxKind.ForInStatement => VisitEachChildOfForInStatement((ForInStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1963
            SyntaxKind.ForOfStatement => VisitEachChildOfForOfStatement((ForOfStatement)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:1973
            SyntaxKind.ContinueStatement => VisitEachChildOfContinueStatement((ContinueStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1980
            SyntaxKind.BreakStatement => VisitEachChildOfBreakStatement((BreakStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1987
            SyntaxKind.ReturnStatement => VisitEachChildOfReturnStatement((ReturnStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:1994
            SyntaxKind.WithStatement => VisitEachChildOfWithStatement((WithStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2002
            SyntaxKind.SwitchStatement => VisitEachChildOfSwitchStatement((SwitchStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2010
            SyntaxKind.LabeledStatement => VisitEachChildOfLabeledStatement((LabeledStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2018
            SyntaxKind.ThrowStatement => VisitEachChildOfThrowStatement((ThrowStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2025
            SyntaxKind.TryStatement => VisitEachChildOfTryStatement((TryStatement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2034
            SyntaxKind.VariableDeclaration => VisitEachChildOfVariableDeclaration((VariableDeclaration)(Node)(object)node, visitor, context, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:2044
            SyntaxKind.VariableDeclarationList => VisitEachChildOfVariableDeclarationList((VariableDeclarationList)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2051
            SyntaxKind.FunctionDeclaration => VisitEachChildOfFunctionDeclaration((FunctionDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor, tokenVisitor),
            // ported from: src/compiler/visitorPublic.ts:2064
            SyntaxKind.ClassDeclaration => VisitEachChildOfClassDeclaration((ClassDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2075
            SyntaxKind.InterfaceDeclaration => VisitEachChildOfInterfaceDeclaration((InterfaceDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2086
            SyntaxKind.TypeAliasDeclaration => VisitEachChildOfTypeAliasDeclaration((TypeAliasDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2096
            SyntaxKind.EnumDeclaration => VisitEachChildOfEnumDeclaration((EnumDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2105
            SyntaxKind.ModuleDeclaration => VisitEachChildOfModuleDeclaration((ModuleDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2114
            SyntaxKind.ModuleBlock => VisitEachChildOfModuleBlock((ModuleBlock)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2121
            SyntaxKind.CaseBlock => VisitEachChildOfCaseBlock((CaseBlock)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2128
            SyntaxKind.NamespaceExportDeclaration => VisitEachChildOfNamespaceExportDeclaration((NamespaceExportDeclaration)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2135
            SyntaxKind.ImportEqualsDeclaration => VisitEachChildOfImportEqualsDeclaration((ImportEqualsDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2145
            SyntaxKind.ImportDeclaration => VisitEachChildOfImportDeclaration((ImportDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2155
            SyntaxKind.ImportAttributes => VisitEachChildOfImportAttributes((ImportAttributes)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2163
            SyntaxKind.ImportAttribute => VisitEachChildOfImportAttribute((ImportAttribute)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2171
            SyntaxKind.ImportClause => VisitEachChildOfImportClause((ImportClause)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2180
            SyntaxKind.NamespaceImport => VisitEachChildOfNamespaceImport((NamespaceImport)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2187
            SyntaxKind.NamespaceExport => VisitEachChildOfNamespaceExport((NamespaceExport)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2194
            SyntaxKind.NamedImports => VisitEachChildOfNamedImports((NamedImports)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2201
            SyntaxKind.ImportSpecifier => VisitEachChildOfImportSpecifier((ImportSpecifier)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2210
            SyntaxKind.ExportAssignment => VisitEachChildOfExportAssignment((ExportAssignment)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2218
            SyntaxKind.ExportDeclaration => VisitEachChildOfExportDeclaration((ExportDeclaration)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2229
            SyntaxKind.NamedExports => VisitEachChildOfNamedExports((NamedExports)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2236
            SyntaxKind.ExportSpecifier => VisitEachChildOfExportSpecifier((ExportSpecifier)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2246
            SyntaxKind.ExternalModuleReference => VisitEachChildOfExternalModuleReference((ExternalModuleReference)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2254
            SyntaxKind.JsxElement => VisitEachChildOfJsxElement((JsxElement)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2263
            SyntaxKind.JsxSelfClosingElement => VisitEachChildOfJsxSelfClosingElement((JsxSelfClosingElement)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2272
            SyntaxKind.JsxOpeningElement => VisitEachChildOfJsxOpeningElement((JsxOpeningElement)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2281
            SyntaxKind.JsxClosingElement => VisitEachChildOfJsxClosingElement((JsxClosingElement)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2288
            SyntaxKind.JsxNamespacedName => ForEachChildInJsxNamespacedName((JsxNamespacedName)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2296
            SyntaxKind.JsxFragment => VisitEachChildOfJsxFragment((JsxFragment)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2305
            SyntaxKind.JsxAttribute => VisitEachChildOfJsxAttribute((JsxAttribute)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2313
            SyntaxKind.JsxAttributes => VisitEachChildOfJsxAttributes((JsxAttributes)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2320
            SyntaxKind.JsxSpreadAttribute => VisitEachChildOfJsxSpreadAttribute((JsxSpreadAttribute)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2327
            SyntaxKind.JsxExpression => VisitEachChildOfJsxExpression((JsxExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2335
            SyntaxKind.CaseClause => VisitEachChildOfCaseClause((CaseClause)(Node)(object)node, visitor, context, nodesVisitor, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2343
            SyntaxKind.DefaultClause => VisitEachChildOfDefaultClause((DefaultClause)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2350
            SyntaxKind.HeritageClause => VisitEachChildOfHeritageClause((HeritageClause)(Node)(object)node, visitor, context, nodesVisitor),
            // ported from: src/compiler/visitorPublic.ts:2357
            SyntaxKind.CatchClause => VisitEachChildOfCatchClause((CatchClause)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2366
            SyntaxKind.PropertyAssignment => VisitEachChildOfPropertyAssignment((PropertyAssignment)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2374
            SyntaxKind.ShorthandPropertyAssignment => VisitEachChildOfShorthandPropertyAssignment((ShorthandPropertyAssignment)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2382
            SyntaxKind.SpreadAssignment => VisitEachChildOfSpreadAssignment((SpreadAssignment)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2390
            SyntaxKind.EnumMember => VisitEachChildOfEnumMember((EnumMember)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2399
            SyntaxKind.SourceFile => VisitEachChildOfSourceFile((SourceFile)(Node)(object)node, visitor, context),
            // ported from: src/compiler/visitorPublic.ts:2407
            SyntaxKind.PartiallyEmittedExpression => VisitEachChildOfPartiallyEmittedExpression((PartiallyEmittedExpression)(Node)(object)node, visitor, context, nodeVisitor),
            // ported from: src/compiler/visitorPublic.ts:2414
            SyntaxKind.CommaListExpression => VisitEachChildOfCommaListExpression((CommaListExpression)(Node)(object)node, visitor, context, nodesVisitor),
            _ => node,
        };
        return (TNode)(object)updated;
    }

    // ---------------------------------------------------------------------------------------------
    // visitorPublic.ts:624-1798 — visitEachChildTable
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/visitorPublic.ts:1246 — <c>visitEachChildOfQualifiedName</c>.</summary>
    private static Node VisitEachChildOfQualifiedName(QualifiedName node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateQualifiedName(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Left, visitor, Utilities.IsEntityName)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Right, visitor, NodeTests.IsIdentifier))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1254 — <c>visitEachChildOfComputedPropertyName</c>.</summary>
    private static Node VisitEachChildOfComputedPropertyName(ComputedPropertyName node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateComputedPropertyName(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1262 — <c>visitEachChildOfTypeParameterDeclaration</c>.</summary>
    private static Node VisitEachChildOfTypeParameterDeclaration(TypeParameterDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypeParameterDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifier),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier)),
            nodeVisitor.Visit(node.Constraint, visitor, Utilities.IsTypeNode),
            nodeVisitor.Visit(node.Default, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1272 — <c>visitEachChildOfParameterDeclaration</c>.</summary>
    private static Node VisitEachChildOfParameterDeclaration(ParameterDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateParameterDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            tokenVisitor is not null ? nodeVisitor.Visit(node.DotDotDotToken, tokenVisitor, NodeTests.IsDotDotDotToken) : node.DotDotDotToken,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsBindingName)),
            tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionToken, tokenVisitor, NodeTests.IsQuestionToken) : node.QuestionToken,
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1284 — <c>visitEachChildOfDecorator</c>.</summary>
    private static Node VisitEachChildOfDecorator(Decorator node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateDecorator(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1292 — <c>visitEachChildOfPropertySignature</c>.</summary>
    private static Node VisitEachChildOfPropertySignature(PropertySignature node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdatePropertySignature(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifier),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionToken, tokenVisitor, NodeTests.IsQuestionToken) : node.QuestionToken,
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1302 — <c>visitEachChildOfPropertyDeclaration</c>.</summary>
    private static Node VisitEachChildOfPropertyDeclaration(PropertyDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdatePropertyDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            // QuestionToken and ExclamationToken are mutually exclusive in PropertyDeclaration
            tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionToken ?? node.ExclamationToken, tokenVisitor, FactoryUtilities.IsQuestionOrExclamationToken) : node.QuestionToken ?? node.ExclamationToken,
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1314 — <c>visitEachChildOfMethodSignature</c>.</summary>
    private static Node VisitEachChildOfMethodSignature(MethodSignature node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateMethodSignature(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifier),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionToken, tokenVisitor, NodeTests.IsQuestionToken) : node.QuestionToken,
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.Parameters, visitor, NodeTests.IsParameter),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1326 — <c>visitEachChildOfMethodDeclaration</c>.</summary>
    private static Node VisitEachChildOfMethodDeclaration(MethodDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateMethodDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            tokenVisitor is not null ? nodeVisitor.Visit(node.AsteriskToken, tokenVisitor, NodeTests.IsAsteriskToken) : node.AsteriskToken,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionToken, tokenVisitor, NodeTests.IsQuestionToken) : node.QuestionToken,
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            VisitParameterList(node.Parameters, visitor, context, nodesVisitor),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            VisitFunctionBody(node.Body!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1340 — <c>visitEachChildOfConstructorDeclaration</c>.</summary>
    private static Node VisitEachChildOfConstructorDeclaration(ConstructorDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateConstructorDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            VisitParameterList(node.Parameters, visitor, context, nodesVisitor),
            VisitFunctionBody(node.Body!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1349 — <c>visitEachChildOfGetAccessorDeclaration</c>.</summary>
    private static Node VisitEachChildOfGetAccessorDeclaration(GetAccessorDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateGetAccessorDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            VisitParameterList(node.Parameters, visitor, context, nodesVisitor),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            VisitFunctionBody(node.Body!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1360 — <c>visitEachChildOfSetAccessorDeclaration</c>.</summary>
    private static Node VisitEachChildOfSetAccessorDeclaration(SetAccessorDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateSetAccessorDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            VisitParameterList(node.Parameters, visitor, context, nodesVisitor),
            VisitFunctionBody(node.Body!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1370 — <c>visitEachChildOfClassStaticBlockDeclaration</c>.</summary>
    private static Node VisitEachChildOfClassStaticBlockDeclaration(ClassStaticBlockDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
    {
        context.StartLexicalEnvironment();
        context.SuspendLexicalEnvironment();
        return context.Factory!.UpdateClassStaticBlockDeclaration(
            node,
            VisitFunctionBody(node.Body, visitor, context, nodeVisitor)
        );
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:1379 — <c>visitEachChildOfCallSignatureDeclaration</c>.</summary>
    private static Node VisitEachChildOfCallSignatureDeclaration(CallSignatureDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateCallSignature(
            node,
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.Parameters, visitor, NodeTests.IsParameter),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1388 — <c>visitEachChildOfConstructSignatureDeclaration</c>.</summary>
    private static Node VisitEachChildOfConstructSignatureDeclaration(ConstructSignatureDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateConstructSignature(
            node,
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.Parameters, visitor, NodeTests.IsParameter),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1397 — <c>visitEachChildOfIndexSignatureDeclaration</c>.</summary>
    private static Node VisitEachChildOfIndexSignatureDeclaration(IndexSignatureDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateIndexSignature(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            nodesVisitor.Visit(node.Parameters, visitor, NodeTests.IsParameter),
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1407 — <c>visitEachChildOfTypePredicateNode</c>.</summary>
    private static Node VisitEachChildOfTypePredicateNode(TypePredicateNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypePredicateNode(
            node,
            nodeVisitor.Visit(node.AssertsModifier, visitor, NodeTests.IsAssertsKeyword),
            Debug.CheckDefined(nodeVisitor.Visit(node.ParameterName, visitor, FactoryUtilities.IsIdentifierOrThisTypeNode)),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1416 — <c>visitEachChildOfTypeReferenceNode</c>.</summary>
    private static Node VisitEachChildOfTypeReferenceNode(TypeReferenceNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypeReferenceNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.TypeName, visitor, Utilities.IsEntityName)),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1424 — <c>visitEachChildOfFunctionTypeNode</c>.</summary>
    private static Node VisitEachChildOfFunctionTypeNode(FunctionTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateFunctionTypeNode(
            node,
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.Parameters, visitor, NodeTests.IsParameter),
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1433 — <c>visitEachChildOfConstructorTypeNode</c>.</summary>
    private static Node VisitEachChildOfConstructorTypeNode(ConstructorTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateConstructorTypeNode(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifier),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.Parameters, visitor, NodeTests.IsParameter),
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1443 — <c>visitEachChildOfTypeQueryNode</c>.</summary>
    private static Node VisitEachChildOfTypeQueryNode(TypeQueryNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypeQueryNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.ExprName, visitor, Utilities.IsEntityName)),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1451 — <c>visitEachChildOfTypeLiteralNode</c>.</summary>
    private static Node VisitEachChildOfTypeLiteralNode(TypeLiteralNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateTypeLiteralNode(
            node,
            nodesVisitor.Visit(node.Members, visitor, Utilities.IsTypeElement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1458 — <c>visitEachChildOfArrayTypeNode</c>.</summary>
    private static Node VisitEachChildOfArrayTypeNode(ArrayTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateArrayTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.ElementType, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1465 — <c>visitEachChildOfTupleTypeNode</c>.</summary>
    private static Node VisitEachChildOfTupleTypeNode(TupleTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateTupleTypeNode(
            node,
            nodesVisitor.Visit(node.Elements, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1472 — <c>visitEachChildOfOptionalTypeNode</c>.</summary>
    private static Node VisitEachChildOfOptionalTypeNode(OptionalTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateOptionalTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1479 — <c>visitEachChildOfRestTypeNode</c>.</summary>
    private static Node VisitEachChildOfRestTypeNode(RestTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateRestTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1486 — <c>visitEachChildOfUnionTypeNode</c>.</summary>
    private static Node VisitEachChildOfUnionTypeNode(UnionTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateUnionTypeNode(
            node,
            nodesVisitor.Visit(node.Types, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1493 — <c>visitEachChildOfIntersectionTypeNode</c>.</summary>
    private static Node VisitEachChildOfIntersectionTypeNode(IntersectionTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateIntersectionTypeNode(
            node,
            nodesVisitor.Visit(node.Types, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1500 — <c>visitEachChildOfConditionalTypeNode</c>.</summary>
    private static Node VisitEachChildOfConditionalTypeNode(ConditionalTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateConditionalTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.CheckType, visitor, Utilities.IsTypeNode)),
            Debug.CheckDefined(nodeVisitor.Visit(node.ExtendsType, visitor, Utilities.IsTypeNode)),
            Debug.CheckDefined(nodeVisitor.Visit(node.TrueType, visitor, Utilities.IsTypeNode)),
            Debug.CheckDefined(nodeVisitor.Visit(node.FalseType, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1510 — <c>visitEachChildOfInferTypeNode</c>.</summary>
    private static Node VisitEachChildOfInferTypeNode(InferTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateInferTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.TypeParameter, visitor, NodeTests.IsTypeParameterDeclaration))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1517 — <c>visitEachChildOfImportTypeNode</c>.</summary>
    private static Node VisitEachChildOfImportTypeNode(ImportTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateImportTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Argument, visitor, Utilities.IsTypeNode)),
            nodeVisitor.Visit(node.Attributes, visitor, NodeTests.IsImportAttributes),
            nodeVisitor.Visit(node.Qualifier, visitor, Utilities.IsEntityName),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode),
            node.IsTypeOf
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1528 — <c>visitEachChildOfImportTypeAssertionContainer</c>.</summary>
    private static Node VisitEachChildOfImportTypeAssertionContainer(ImportTypeAssertionContainer node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateImportTypeAssertionContainer(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.AssertClause, visitor, NodeTests.IsAssertClause)),
            node.MultiLine
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1536 — <c>visitEachChildOfNamedTupleMember</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the port's <c>NamedTupleMember.dotDotDotToken</c>/<c>questionToken</c> slots are <c>Token?</c> where
    /// the factory's <c>updateNamedTupleMember</c> takes <c>PunctuationToken?</c> — the narrowing the model gives the
    /// other token slots (Model/NodeClasses.cs) — so each of the two arms states the cast §3 licenses. Nothing is
    /// converted at runtime: those two slots only ever hold <c>?</c>/<c>!</c>/<c>.</c> punctuation tokens.
    /// </remarks>
    private static Node VisitEachChildOfNamedTupleMember(NamedTupleMember node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateNamedTupleMember(
            node,
            (PunctuationToken?)(tokenVisitor is not null ? nodeVisitor.Visit(node.DotDotDotToken, tokenVisitor, NodeTests.IsDotDotDotToken) : node.DotDotDotToken),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier)),
            (PunctuationToken?)(tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionToken, tokenVisitor, NodeTests.IsQuestionToken) : node.QuestionToken),
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1546 — <c>visitEachChildOfParenthesizedType</c>.</summary>
    private static Node VisitEachChildOfParenthesizedType(ParenthesizedTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateParenthesizedType(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1553 — <c>visitEachChildOfTypeOperatorNode</c>.</summary>
    private static Node VisitEachChildOfTypeOperatorNode(TypeOperatorNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypeOperatorNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1560 — <c>visitEachChildOfIndexedAccessType</c>.</summary>
    private static Node VisitEachChildOfIndexedAccessType(IndexedAccessTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateIndexedAccessTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.ObjectType, visitor, Utilities.IsTypeNode)),
            Debug.CheckDefined(nodeVisitor.Visit(node.IndexType, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1568 — <c>visitEachChildOfMappedType</c>.</summary>
    private static Node VisitEachChildOfMappedType(MappedTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateMappedTypeNode(
            node,
            tokenVisitor is not null ? nodeVisitor.Visit(node.ReadonlyToken, tokenVisitor, FactoryUtilities.IsReadonlyKeywordOrPlusOrMinusToken) : node.ReadonlyToken,
            Debug.CheckDefined(nodeVisitor.Visit(node.TypeParameter, visitor, NodeTests.IsTypeParameterDeclaration)),
            nodeVisitor.Visit(node.NameType, visitor, Utilities.IsTypeNode),
            tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionToken, tokenVisitor, FactoryUtilities.IsQuestionOrPlusOrMinusToken) : node.QuestionToken,
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            nodesVisitor.Visit(node.Members, visitor, Utilities.IsTypeElement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1580 — <c>visitEachChildOfLiteralTypeNode</c>.</summary>
    private static Node VisitEachChildOfLiteralTypeNode(LiteralTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateLiteralTypeNode(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Literal, visitor, Utilities.IsLiteralTypeLiteral))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1587 — <c>visitEachChildOfTemplateLiteralType</c>.</summary>
    private static Node VisitEachChildOfTemplateLiteralType(TemplateLiteralTypeNode node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTemplateLiteralType(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Head, visitor, NodeTests.IsTemplateHead)),
            nodesVisitor.Visit(node.TemplateSpans, visitor, NodeTests.IsTemplateLiteralTypeSpan)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1595 — <c>visitEachChildOfTemplateLiteralTypeSpan</c>.</summary>
    private static Node VisitEachChildOfTemplateLiteralTypeSpan(TemplateLiteralTypeSpan node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTemplateLiteralTypeSpan(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Literal, visitor, Utilities.IsTemplateMiddleOrTemplateTail))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1604 — <c>visitEachChildOfObjectBindingPattern</c>.</summary>
    private static Node VisitEachChildOfObjectBindingPattern(ObjectBindingPattern node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateObjectBindingPattern(
            node,
            nodesVisitor.Visit(node.Elements, visitor, NodeTests.IsBindingElement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1611 — <c>visitEachChildOfArrayBindingPattern</c>.</summary>
    private static Node VisitEachChildOfArrayBindingPattern(ArrayBindingPattern node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateArrayBindingPattern(
            node,
            nodesVisitor.Visit(node.Elements, visitor, Utilities.IsArrayBindingElement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1618 — <c>visitEachChildOfBindingElement</c>.</summary>
    private static Node VisitEachChildOfBindingElement(BindingElement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateBindingElement(
            node,
            tokenVisitor is not null ? nodeVisitor.Visit(node.DotDotDotToken, tokenVisitor, NodeTests.IsDotDotDotToken) : node.DotDotDotToken,
            nodeVisitor.Visit(node.PropertyName, visitor, Utilities.IsPropertyName),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsBindingName)),
            nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1629 — <c>visitEachChildOfArrayLiteralExpression</c>.</summary>
    private static Node VisitEachChildOfArrayLiteralExpression(ArrayLiteralExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateArrayLiteralExpression(
            node,
            nodesVisitor.Visit(node.Elements, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1636 — <c>visitEachChildOfObjectLiteralExpression</c>.</summary>
    private static Node VisitEachChildOfObjectLiteralExpression(ObjectLiteralExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateObjectLiteralExpression(
            node,
            nodesVisitor.Visit(node.Properties, visitor, Utilities.IsObjectLiteralElementLike)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1643 — <c>visitEachChildOfPropertyAccessExpression</c>.</summary>
    private static Node VisitEachChildOfPropertyAccessExpression(PropertyAccessExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => Utilities.IsPropertyAccessChain(node) ?
            context.Factory!.UpdatePropertyAccessChain(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
                tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionDotToken, tokenVisitor, NodeTests.IsQuestionDotToken) : node.QuestionDotToken,
                Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsMemberName))
            ) :
            context.Factory!.UpdatePropertyAccessExpression(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
                Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsMemberName))
            );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1658 — <c>visitEachChildOfElementAccessExpression</c>.</summary>
    private static Node VisitEachChildOfElementAccessExpression(ElementAccessExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => Utilities.IsElementAccessChain(node) ?
            context.Factory!.UpdateElementAccessChain(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
                tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionDotToken, tokenVisitor, NodeTests.IsQuestionDotToken) : node.QuestionDotToken,
                Debug.CheckDefined(nodeVisitor.Visit(node.ArgumentExpression, visitor, Utilities.IsExpression))
            ) :
            context.Factory!.UpdateElementAccessExpression(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
                Debug.CheckDefined(nodeVisitor.Visit(node.ArgumentExpression, visitor, Utilities.IsExpression))
            );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1673 — <c>visitEachChildOfCallExpression</c>.</summary>
    private static Node VisitEachChildOfCallExpression(CallExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => Utilities.IsCallChain(node) ?
            context.Factory!.UpdateCallChain(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
                tokenVisitor is not null ? nodeVisitor.Visit(node.QuestionDotToken, tokenVisitor, NodeTests.IsQuestionDotToken) : node.QuestionDotToken,
                nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode),
                nodesVisitor.Visit(node.Arguments, visitor, Utilities.IsExpression)
            ) :
            context.Factory!.UpdateCallExpression(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
                nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode),
                nodesVisitor.Visit(node.Arguments, visitor, Utilities.IsExpression)
            );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1690 — <c>visitEachChildOfNewExpression</c>.</summary>
    private static Node VisitEachChildOfNewExpression(NewExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateNewExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode),
            nodesVisitor.Visit(node.Arguments, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1699 — <c>visitEachChildOfTaggedTemplateExpression</c>.</summary>
    private static Node VisitEachChildOfTaggedTemplateExpression(TaggedTemplateExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTaggedTemplateExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Tag, visitor, Utilities.IsExpression)),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode),
            Debug.CheckDefined(nodeVisitor.Visit(node.Template, visitor, Utilities.IsTemplateLiteral))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1708 — <c>visitEachChildOfTypeAssertionExpression</c>.</summary>
    private static Node VisitEachChildOfTypeAssertionExpression(TypeAssertion node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypeAssertion(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1716 — <c>visitEachChildOfParenthesizedExpression</c>.</summary>
    private static Node VisitEachChildOfParenthesizedExpression(ParenthesizedExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateParenthesizedExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1723 — <c>visitEachChildOfFunctionExpression</c>.</summary>
    private static Node VisitEachChildOfFunctionExpression(FunctionExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateFunctionExpression(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifier),
            tokenVisitor is not null ? nodeVisitor.Visit(node.AsteriskToken, tokenVisitor, NodeTests.IsAsteriskToken) : node.AsteriskToken,
            nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            VisitParameterList(node.Parameters, visitor, context, nodesVisitor),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            VisitFunctionBody(node.Body, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1736 — <c>visitEachChildOfArrowFunction</c>.</summary>
    private static Node VisitEachChildOfArrowFunction(ArrowFunction node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateArrowFunction(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifier),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            VisitParameterList(node.Parameters, visitor, context, nodesVisitor),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            tokenVisitor is not null ? Debug.CheckDefined(nodeVisitor.Visit(node.EqualsGreaterThanToken, tokenVisitor, NodeTests.IsEqualsGreaterThanToken)) : node.EqualsGreaterThanToken,
            VisitFunctionBody(node.Body, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1748 — <c>visitEachChildOfDeleteExpression</c>.</summary>
    private static Node VisitEachChildOfDeleteExpression(DeleteExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateDeleteExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1755 — <c>visitEachChildOfTypeOfExpression</c>.</summary>
    private static Node VisitEachChildOfTypeOfExpression(TypeOfExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypeOfExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1762 — <c>visitEachChildOfVoidExpression</c>.</summary>
    private static Node VisitEachChildOfVoidExpression(VoidExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateVoidExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1769 — <c>visitEachChildOfAwaitExpression</c>.</summary>
    private static Node VisitEachChildOfAwaitExpression(AwaitExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateAwaitExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1776 — <c>visitEachChildOfPrefixUnaryExpression</c>.</summary>
    private static Node VisitEachChildOfPrefixUnaryExpression(JsonMinusNumericLiteral node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdatePrefixUnaryExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Operand, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1783 — <c>visitEachChildOfPostfixUnaryExpression</c>.</summary>
    private static Node VisitEachChildOfPostfixUnaryExpression(PostfixUnaryExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdatePostfixUnaryExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Operand, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1790 — <c>visitEachChildOfBinaryExpression</c>.</summary>
    private static Node VisitEachChildOfBinaryExpression(BinaryExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateBinaryExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Left, visitor, Utilities.IsExpression)),
            tokenVisitor is not null ? Debug.CheckDefined(nodeVisitor.Visit(node.OperatorToken, tokenVisitor, FactoryUtilities.IsBinaryOperatorToken)) : node.OperatorToken,
            Debug.CheckDefined(nodeVisitor.Visit(node.Right, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1799 — <c>visitEachChildOfConditionalExpression</c>.</summary>
    private static Node VisitEachChildOfConditionalExpression(ConditionalExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateConditionalExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Condition, visitor, Utilities.IsExpression)),
            tokenVisitor is not null ? Debug.CheckDefined(nodeVisitor.Visit(node.QuestionToken, tokenVisitor, NodeTests.IsQuestionToken)) : node.QuestionToken,
            Debug.CheckDefined(nodeVisitor.Visit(node.WhenTrue, visitor, Utilities.IsExpression)),
            tokenVisitor is not null ? Debug.CheckDefined(nodeVisitor.Visit(node.ColonToken, tokenVisitor, NodeTests.IsColonToken)) : node.ColonToken,
            Debug.CheckDefined(nodeVisitor.Visit(node.WhenFalse, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1810 — <c>visitEachChildOfTemplateExpression</c>.</summary>
    private static Node VisitEachChildOfTemplateExpression(TemplateExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTemplateExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Head, visitor, NodeTests.IsTemplateHead)),
            nodesVisitor.Visit(node.TemplateSpans, visitor, NodeTests.IsTemplateSpan)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1818 — <c>visitEachChildOfYieldExpression</c>.</summary>
    private static Node VisitEachChildOfYieldExpression(YieldExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateYieldExpression(
            node,
            tokenVisitor is not null ? nodeVisitor.Visit(node.AsteriskToken, tokenVisitor, NodeTests.IsAsteriskToken) : node.AsteriskToken,
            nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1826 — <c>visitEachChildOfSpreadElement</c>.</summary>
    private static Node VisitEachChildOfSpreadElement(SpreadElement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateSpreadElement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1833 — <c>visitEachChildOfClassExpression</c>.</summary>
    private static Node VisitEachChildOfClassExpression(ClassExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateClassExpression(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.HeritageClauses, visitor, NodeTests.IsHeritageClause),
            nodesVisitor.Visit(node.Members, visitor, Utilities.IsClassElement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1844 — <c>visitEachChildOfExpressionWithTypeArguments</c>.</summary>
    private static Node VisitEachChildOfExpressionWithTypeArguments(ExpressionWithTypeArguments node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateExpressionWithTypeArguments(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1852 — <c>visitEachChildOfAsExpression</c>.</summary>
    private static Node VisitEachChildOfAsExpression(AsExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateAsExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1860 — <c>visitEachChildOfSatisfiesExpression</c>.</summary>
    private static Node VisitEachChildOfSatisfiesExpression(SatisfiesExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateSatisfiesExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1868 — <c>visitEachChildOfNonNullExpression</c>.</summary>
    private static Node VisitEachChildOfNonNullExpression(NonNullExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => Utilities.IsOptionalChain(node) ?
            context.Factory!.UpdateNonNullChain(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
            ) :
            context.Factory!.UpdateNonNullExpression(
                node,
                Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
            );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1880 — <c>visitEachChildOfMetaProperty</c>.</summary>
    private static Node VisitEachChildOfMetaProperty(MetaProperty node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateMetaProperty(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1888 — <c>visitEachChildOfTemplateSpan</c>.</summary>
    private static Node VisitEachChildOfTemplateSpan(TemplateSpan node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTemplateSpan(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Literal, visitor, Utilities.IsTemplateMiddleOrTemplateTail))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1897 — <c>visitEachChildOfBlock</c>.</summary>
    private static Node VisitEachChildOfBlock(Block node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateBlock(
            node,
            nodesVisitor.Visit(node.Statements, visitor, Utilities.IsStatement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1904 — <c>visitEachChildOfVariableStatement</c>.</summary>
    private static Node VisitEachChildOfVariableStatement(VariableStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateVariableStatement(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.DeclarationList, visitor, NodeTests.IsVariableDeclarationList))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1912 — <c>visitEachChildOfExpressionStatement</c>.</summary>
    private static Node VisitEachChildOfExpressionStatement(ExpressionStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateExpressionStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1919 — <c>visitEachChildOfIfStatement</c>.</summary>
    private static Node VisitEachChildOfIfStatement(IfStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateIfStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            Debug.CheckDefined(nodeVisitor.Visit(node.ThenStatement, visitor, Utilities.IsStatement, n => (Node)context.Factory!.LiftToBlock([.. n]))),
            nodeVisitor.Visit(node.ElseStatement, visitor, Utilities.IsStatement, n => (Node)context.Factory!.LiftToBlock([.. n]))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1928 — <c>visitEachChildOfDoStatement</c>.</summary>
    private static Node VisitEachChildOfDoStatement(DoStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateDoStatement(
            node,
            VisitIterationBody(node.Statement!, visitor, context, nodeVisitor),
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1936 — <c>visitEachChildOfWhileStatement</c>.</summary>
    private static Node VisitEachChildOfWhileStatement(WhileStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateWhileStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            VisitIterationBody(node.Statement!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1944 — <c>visitEachChildOfForStatement</c>.</summary>
    private static Node VisitEachChildOfForStatement(ForStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateForStatement(
            node,
            nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsForInitializer),
            nodeVisitor.Visit(node.Condition, visitor, Utilities.IsExpression),
            nodeVisitor.Visit(node.Incrementor, visitor, Utilities.IsExpression),
            VisitIterationBody(node.Statement!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1954 — <c>visitEachChildOfForInStatement</c>.</summary>
    private static Node VisitEachChildOfForInStatement(ForInStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateForInStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsForInitializer)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            VisitIterationBody(node.Statement!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1963 — <c>visitEachChildOfForOfStatement</c>.</summary>
    private static Node VisitEachChildOfForOfStatement(ForOfStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateForOfStatement(
            node,
            tokenVisitor is not null ? nodeVisitor.Visit(node.AwaitModifier, tokenVisitor, NodeTests.IsAwaitKeyword) : node.AwaitModifier,
            Debug.CheckDefined(nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsForInitializer)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            VisitIterationBody(node.Statement!, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1973 — <c>visitEachChildOfContinueStatement</c>.</summary>
    private static Node VisitEachChildOfContinueStatement(ContinueStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateContinueStatement(
            node,
            nodeVisitor.Visit(node.Label, visitor, NodeTests.IsIdentifier)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1980 — <c>visitEachChildOfBreakStatement</c>.</summary>
    private static Node VisitEachChildOfBreakStatement(BreakStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateBreakStatement(
            node,
            nodeVisitor.Visit(node.Label, visitor, NodeTests.IsIdentifier)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1987 — <c>visitEachChildOfReturnStatement</c>.</summary>
    private static Node VisitEachChildOfReturnStatement(ReturnStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateReturnStatement(
            node,
            nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:1994 — <c>visitEachChildOfWithStatement</c>.</summary>
    private static Node VisitEachChildOfWithStatement(WithStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateWithStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Statement, visitor, Utilities.IsStatement, n => (Node)context.Factory!.LiftToBlock([.. n])))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2002 — <c>visitEachChildOfSwitchStatement</c>.</summary>
    private static Node VisitEachChildOfSwitchStatement(SwitchStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateSwitchStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            Debug.CheckDefined(nodeVisitor.Visit(node.CaseBlock, visitor, NodeTests.IsCaseBlock))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2010 — <c>visitEachChildOfLabeledStatement</c>.</summary>
    private static Node VisitEachChildOfLabeledStatement(LabeledStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateLabeledStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Label, visitor, NodeTests.IsIdentifier)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Statement, visitor, Utilities.IsStatement, n => (Node)context.Factory!.LiftToBlock([.. n])))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2018 — <c>visitEachChildOfThrowStatement</c>.</summary>
    private static Node VisitEachChildOfThrowStatement(ThrowStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateThrowStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2025 — <c>visitEachChildOfTryStatement</c>.</summary>
    private static Node VisitEachChildOfTryStatement(TryStatement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTryStatement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.TryBlock, visitor, NodeTests.IsBlock)),
            nodeVisitor.Visit(node.CatchClause, visitor, NodeTests.IsCatchClause),
            nodeVisitor.Visit(node.FinallyBlock, visitor, NodeTests.IsBlock)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2034 — <c>visitEachChildOfVariableDeclaration</c>.</summary>
    private static Node VisitEachChildOfVariableDeclaration(VariableDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateVariableDeclaration(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsBindingName)),
            tokenVisitor is not null ? nodeVisitor.Visit(node.ExclamationToken, tokenVisitor, NodeTests.IsExclamationToken) : node.ExclamationToken,
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2044 — <c>visitEachChildOfVariableDeclarationList</c>.</summary>
    private static Node VisitEachChildOfVariableDeclarationList(VariableDeclarationList node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateVariableDeclarationList(
            node,
            nodesVisitor.Visit(node.Declarations, visitor, NodeTests.IsVariableDeclaration)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2051 — <c>visitEachChildOfFunctionDeclaration</c>.</summary>
    private static Node VisitEachChildOfFunctionDeclaration(FunctionDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor, Func<Node, object?>? tokenVisitor)
        => context.Factory!.UpdateFunctionDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifier),
            tokenVisitor is not null ? nodeVisitor.Visit(node.AsteriskToken, tokenVisitor, NodeTests.IsAsteriskToken) : node.AsteriskToken,
            nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            VisitParameterList(node.Parameters, visitor, context, nodesVisitor),
            nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode),
            VisitFunctionBody(node.Body, visitor, context, nodeVisitor)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2064 — <c>visitEachChildOfClassDeclaration</c>.</summary>
    private static Node VisitEachChildOfClassDeclaration(ClassDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateClassDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.HeritageClauses, visitor, NodeTests.IsHeritageClause),
            nodesVisitor.Visit(node.Members, visitor, Utilities.IsClassElement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2075 — <c>visitEachChildOfInterfaceDeclaration</c>.</summary>
    private static Node VisitEachChildOfInterfaceDeclaration(InterfaceDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateInterfaceDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier)),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            nodesVisitor.Visit(node.HeritageClauses, visitor, NodeTests.IsHeritageClause),
            nodesVisitor.Visit(node.Members, visitor, Utilities.IsTypeElement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2086 — <c>visitEachChildOfTypeAliasDeclaration</c>.</summary>
    private static Node VisitEachChildOfTypeAliasDeclaration(TypeAliasDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateTypeAliasDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier)),
            nodesVisitor.Visit(node.TypeParameters, visitor, NodeTests.IsTypeParameterDeclaration),
            Debug.CheckDefined(nodeVisitor.Visit(node.Type, visitor, Utilities.IsTypeNode))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2096 — <c>visitEachChildOfEnumDeclaration</c>.</summary>
    private static Node VisitEachChildOfEnumDeclaration(EnumDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateEnumDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier)),
            nodesVisitor.Visit(node.Members, visitor, NodeTests.IsEnumMember)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2105 — <c>visitEachChildOfModuleDeclaration</c>.</summary>
    private static Node VisitEachChildOfModuleDeclaration(ModuleDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateModuleDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, FactoryUtilities.IsModuleName)),
            nodeVisitor.Visit(node.Body, visitor, Utilities.IsModuleBody)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2114 — <c>visitEachChildOfModuleBlock</c>.</summary>
    private static Node VisitEachChildOfModuleBlock(ModuleBlock node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateModuleBlock(
            node,
            nodesVisitor.Visit(node.Statements, visitor, Utilities.IsStatement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2121 — <c>visitEachChildOfCaseBlock</c>.</summary>
    private static Node VisitEachChildOfCaseBlock(CaseBlock node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateCaseBlock(
            node,
            nodesVisitor.Visit(node.Clauses, visitor, Utilities.IsCaseOrDefaultClause)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2128 — <c>visitEachChildOfNamespaceExportDeclaration</c>.</summary>
    private static Node VisitEachChildOfNamespaceExportDeclaration(NamespaceExportDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateNamespaceExportDeclaration(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2135 — <c>visitEachChildOfImportEqualsDeclaration</c>.</summary>
    private static Node VisitEachChildOfImportEqualsDeclaration(ImportEqualsDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateImportEqualsDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            node.IsTypeOnly,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier)),
            Debug.CheckDefined(nodeVisitor.Visit(node.ModuleReference, visitor, Utilities.IsModuleReference))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2145 — <c>visitEachChildOfImportDeclaration</c>.</summary>
    private static Node VisitEachChildOfImportDeclaration(ImportDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateImportDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            nodeVisitor.Visit(node.ImportClause, visitor, NodeTests.IsImportClause),
            Debug.CheckDefined(nodeVisitor.Visit(node.ModuleSpecifier, visitor, Utilities.IsExpression)),
            nodeVisitor.Visit(node.Attributes, visitor, NodeTests.IsImportAttributes)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2155 — <c>visitEachChildOfImportAttributes</c>.</summary>
    private static Node VisitEachChildOfImportAttributes(ImportAttributes node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateImportAttributes(
            node,
            nodesVisitor.Visit(node.Elements, visitor, NodeTests.IsImportAttribute),
            node.MultiLine
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2163 — <c>visitEachChildOfImportAttribute</c>.</summary>
    private static Node VisitEachChildOfImportAttribute(ImportAttribute node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateImportAttribute(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsImportAttributeName)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Value, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2171 — <c>visitEachChildOfImportClause</c>.</summary>
    private static Node VisitEachChildOfImportClause(ImportClause node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateImportClause(
            node,
            node.PhaseModifier,
            nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier),
            nodeVisitor.Visit(node.NamedBindings, visitor, Utilities.IsNamedImportBindings)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2180 — <c>visitEachChildOfNamespaceImport</c>.</summary>
    private static Node VisitEachChildOfNamespaceImport(NamespaceImport node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateNamespaceImport(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2187 — <c>visitEachChildOfNamespaceExport</c>.</summary>
    private static Node VisitEachChildOfNamespaceExport(NamespaceExport node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateNamespaceExport(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2194 — <c>visitEachChildOfNamedImports</c>.</summary>
    private static Node VisitEachChildOfNamedImports(NamedImports node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateNamedImports(
            node,
            nodesVisitor.Visit(node.Elements, visitor, NodeTests.IsImportSpecifier)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2201 — <c>visitEachChildOfImportSpecifier</c>.</summary>
    private static Node VisitEachChildOfImportSpecifier(ImportSpecifier node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateImportSpecifier(
            node,
            node.IsTypeOnly,
            nodeVisitor.Visit(node.PropertyName, visitor, NodeTests.IsModuleExportName),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2210 — <c>visitEachChildOfExportAssignment</c>.</summary>
    private static Node VisitEachChildOfExportAssignment(ExportAssignment node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateExportAssignment(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2218 — <c>visitEachChildOfExportDeclaration</c>.</summary>
    private static Node VisitEachChildOfExportDeclaration(ExportDeclaration node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateExportDeclaration(
            node,
            nodesVisitor.Visit(node.Modifiers, visitor, Utilities.IsModifierLike),
            node.IsTypeOnly,
            nodeVisitor.Visit(node.ExportClause, visitor, Utilities.IsNamedExportBindings),
            nodeVisitor.Visit(node.ModuleSpecifier, visitor, Utilities.IsExpression),
            nodeVisitor.Visit(node.Attributes, visitor, NodeTests.IsImportAttributes)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2229 — <c>visitEachChildOfNamedExports</c>.</summary>
    private static Node VisitEachChildOfNamedExports(NamedExports node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateNamedExports(
            node,
            nodesVisitor.Visit(node.Elements, visitor, NodeTests.IsExportSpecifier)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2236 — <c>visitEachChildOfExportSpecifier</c>.</summary>
    private static Node VisitEachChildOfExportSpecifier(ExportSpecifier node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateExportSpecifier(
            node,
            node.IsTypeOnly,
            nodeVisitor.Visit(node.PropertyName, visitor, NodeTests.IsModuleExportName),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsModuleExportName))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2246 — <c>visitEachChildOfExternalModuleReference</c>.</summary>
    private static Node VisitEachChildOfExternalModuleReference(ExternalModuleReference node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateExternalModuleReference(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2254 — <c>visitEachChildOfJsxElement</c>.</summary>
    private static Node VisitEachChildOfJsxElement(JsxElement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxElement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.OpeningElement, visitor, NodeTests.IsJsxOpeningElement)),
            nodesVisitor.Visit(node.Children, visitor, Utilities.IsJsxChild),
            Debug.CheckDefined(nodeVisitor.Visit(node.ClosingElement, visitor, NodeTests.IsJsxClosingElement))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2263 — <c>visitEachChildOfJsxSelfClosingElement</c>.</summary>
    private static Node VisitEachChildOfJsxSelfClosingElement(JsxSelfClosingElement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxSelfClosingElement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.TagName, visitor, Utilities.IsJsxTagNameExpression)),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode),
            Debug.CheckDefined(nodeVisitor.Visit(node.Attributes, visitor, NodeTests.IsJsxAttributes))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2272 — <c>visitEachChildOfJsxOpeningElement</c>.</summary>
    private static Node VisitEachChildOfJsxOpeningElement(JsxOpeningElement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxOpeningElement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.TagName, visitor, Utilities.IsJsxTagNameExpression)),
            nodesVisitor.Visit(node.TypeArguments, visitor, Utilities.IsTypeNode),
            Debug.CheckDefined(nodeVisitor.Visit(node.Attributes, visitor, NodeTests.IsJsxAttributes))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2281 — <c>visitEachChildOfJsxClosingElement</c>.</summary>
    private static Node VisitEachChildOfJsxClosingElement(JsxClosingElement node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxClosingElement(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.TagName, visitor, Utilities.IsJsxTagNameExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2288 — <c>forEachChildInJsxNamespacedName</c>.</summary>
    private static Node ForEachChildInJsxNamespacedName(JsxNamespacedName node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxNamespacedName(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Namespace, visitor, NodeTests.IsIdentifier)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2296 — <c>visitEachChildOfJsxFragment</c>.</summary>
    private static Node VisitEachChildOfJsxFragment(JsxFragment node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxFragment(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.OpeningFragment, visitor, NodeTests.IsJsxOpeningFragment)),
            nodesVisitor.Visit(node.Children, visitor, Utilities.IsJsxChild),
            Debug.CheckDefined(nodeVisitor.Visit(node.ClosingFragment, visitor, NodeTests.IsJsxClosingFragment))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2305 — <c>visitEachChildOfJsxAttribute</c>.</summary>
    private static Node VisitEachChildOfJsxAttribute(JsxAttribute node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxAttribute(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsJsxAttributeName)),
            nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsStringLiteralOrJsxExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2313 — <c>visitEachChildOfJsxAttributes</c>.</summary>
    private static Node VisitEachChildOfJsxAttributes(JsxAttributes node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateJsxAttributes(
            node,
            nodesVisitor.Visit(node.Properties, visitor, Utilities.IsJsxAttributeLike)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2320 — <c>visitEachChildOfJsxSpreadAttribute</c>.</summary>
    private static Node VisitEachChildOfJsxSpreadAttribute(JsxSpreadAttribute node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxSpreadAttribute(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2327 — <c>visitEachChildOfJsxExpression</c>.</summary>
    private static Node VisitEachChildOfJsxExpression(JsxExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateJsxExpression(
            node,
            nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2335 — <c>visitEachChildOfCaseClause</c>.</summary>
    private static Node VisitEachChildOfCaseClause(CaseClause node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateCaseClause(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression)),
            nodesVisitor.Visit(node.Statements, visitor, Utilities.IsStatement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2343 — <c>visitEachChildOfDefaultClause</c>.</summary>
    private static Node VisitEachChildOfDefaultClause(DefaultClause node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateDefaultClause(
            node,
            nodesVisitor.Visit(node.Statements, visitor, Utilities.IsStatement)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2350 — <c>visitEachChildOfHeritageClause</c>.</summary>
    private static Node VisitEachChildOfHeritageClause(HeritageClause node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateHeritageClause(
            node,
            nodesVisitor.Visit(node.Types, visitor, NodeTests.IsExpressionWithTypeArguments)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2357 — <c>visitEachChildOfCatchClause</c>.</summary>
    private static Node VisitEachChildOfCatchClause(CatchClause node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateCatchClause(
            node,
            nodeVisitor.Visit(node.VariableDeclaration, visitor, NodeTests.IsVariableDeclaration),
            Debug.CheckDefined(nodeVisitor.Visit(node.Block, visitor, NodeTests.IsBlock))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2366 — <c>visitEachChildOfPropertyAssignment</c>.</summary>
    private static Node VisitEachChildOfPropertyAssignment(PropertyAssignment node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdatePropertyAssignment(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            Debug.CheckDefined(nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2374 — <c>visitEachChildOfShorthandPropertyAssignment</c>.</summary>
    private static Node VisitEachChildOfShorthandPropertyAssignment(ShorthandPropertyAssignment node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateShorthandPropertyAssignment(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, NodeTests.IsIdentifier)),
            nodeVisitor.Visit(node.ObjectAssignmentInitializer, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2382 — <c>visitEachChildOfSpreadAssignment</c>.</summary>
    private static Node VisitEachChildOfSpreadAssignment(SpreadAssignment node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateSpreadAssignment(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2390 — <c>visitEachChildOfEnumMember</c>.</summary>
    private static Node VisitEachChildOfEnumMember(EnumMember node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdateEnumMember(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Name, visitor, Utilities.IsPropertyName)),
            nodeVisitor.Visit(node.Initializer, visitor, Utilities.IsExpression)
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2399 — <c>visitEachChildOfSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference passes only the visited statements, because TS's <c>updateSourceFile</c> defaults the
    /// remaining five slots to the node's own members (<c>nodeFactory.ts:1547-1556</c>); the port has no
    /// default-from-node parameter, so the entry states the same five reads the reference's defaults make, in the
    /// factory's order.
    /// </remarks>
    private static Node VisitEachChildOfSourceFile(SourceFile node, Func<Node, object?> visitor, IVisitorTransformationContext context)
        => context.Factory!.UpdateSourceFile(
            node,
            VisitLexicalEnvironment(node.Statements!, visitor, context),
            node.IsDeclarationFile,
            node.ReferencedFiles,
            node.TypeReferenceDirectives,
            node.HasNoDefaultLib,
            node.LibReferenceDirectives);

    /// <summary>ported from: src/compiler/visitorPublic.ts:2407 — <c>visitEachChildOfPartiallyEmittedExpression</c>.</summary>
    private static Node VisitEachChildOfPartiallyEmittedExpression(PartiallyEmittedExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodeVisitor nodeVisitor)
        => context.Factory!.UpdatePartiallyEmittedExpression(
            node,
            Debug.CheckDefined(nodeVisitor.Visit(node.Expression, visitor, Utilities.IsExpression))
        );

    /// <summary>ported from: src/compiler/visitorPublic.ts:2414 — <c>visitEachChildOfCommaListExpression</c>.</summary>
    private static Node VisitEachChildOfCommaListExpression(CommaListExpression node, Func<Node, object?> visitor, IVisitorTransformationContext context, NodesVisitor nodesVisitor)
        => context.Factory!.UpdateCommaListExpression(
            node,
            nodesVisitor.Visit(node.Elements, visitor, Utilities.IsExpression)
        );
    // ---------------------------------------------------------------------------------------------
    // The predicates visitorPublic.ts reads that no earlier slice had declared
    // (utilitiesPublic.ts / utilities.ts — their home module's class is this one, §15.15)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1849-1853 — <c>isArrayBindingElement</c>.</summary>
    /// <remarks>PORT-NOTE: stated here because this slice is its first reader (the port's convention for a member no order file assigns).</remarks>
    public static bool IsArrayBindingElement(Node node)
        => node.Kind is SyntaxKind.BindingElement or SyntaxKind.OmittedExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1666-1671 — <c>isBindingName</c>.</summary>
    public static bool IsBindingName(Node node)
        => node.Kind is SyntaxKind.Identifier or SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2160-2163 — <c>isConciseBody</c>.</summary>
    public static bool IsConciseBody(Node node)
        => NodeTests.IsBlock(node)
            || Utilities.IsExpression(node);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2170-2173 — <c>isForInitializer</c>.</summary>
    public static bool IsForInitializer(Node node)
        => NodeTests.IsVariableDeclarationList(node)
            || Utilities.IsExpression(node);

    /// <summary>ported from: src/compiler/utilities.ts:11137-11141 — <c>isJsxAttributeName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>Parser</c> holds a private <c>IsImportAttributeName</c>/<c>IsJsxAttributeName</c> of its own — a
    /// different class, so neither is a duplicate of this member; the reference's home for both is
    /// <c>utilities.ts</c>, whose members land on <see cref="Utilities"/>.
    /// </remarks>
    public static bool IsJsxAttributeName(Node node)
        => node.Kind is SyntaxKind.Identifier or SyntaxKind.JsxNamespacedName;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2460-2466 — <c>isJsxTagNameExpression</c>.</summary>
    public static bool IsJsxTagNameExpression(Node node)
        => node.Kind is SyntaxKind.ThisKeyword or SyntaxKind.Identifier or SyntaxKind.PropertyAccessExpression or SyntaxKind.JsxNamespacedName;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2071-2081 — <c>isLiteralTypeLiteral</c>.</summary>
    public static bool IsLiteralTypeLiteral(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.PrefixUnaryExpression:
                return true;
            default:
                return IsLiteralExpression(node);
        }
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1778-1780 — <c>isModifierLike</c>.</summary>
    public static bool IsModifierLike(Node node)
        => Utilities.IsModifier(node) || NodeTests.IsDecorator(node);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2175-2180 — <c>isModuleBody</c>.</summary>
    public static bool IsModuleBody(Node node)
        => node.Kind is SyntaxKind.ModuleBlock or SyntaxKind.ModuleDeclaration or SyntaxKind.Identifier;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2451-2456 — <c>isModuleReference</c>.</summary>
    public static bool IsModuleReference(Node node)
        => node.Kind is SyntaxKind.ExternalModuleReference or SyntaxKind.QualifiedName or SyntaxKind.Identifier;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1445-1447 — <c>isNamedExportBindings</c>.</summary>
    public static bool IsNamedExportBindings(Node node)
        => node.Kind is SyntaxKind.NamespaceExport or SyntaxKind.NamedExports;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2196-2200 — <c>isNamedImportBindings</c>.</summary>
    public static bool IsNamedImportBindings(Node node)
        => node.Kind is SyntaxKind.NamedImports or SyntaxKind.NamespaceImport;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2483-2487 — <c>isStringLiteralOrJsxExpression</c>.</summary>
    public static bool IsStringLiteralOrJsxExpression(Node node)
        => node.Kind is SyntaxKind.StringLiteral or SyntaxKind.JsxExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1989-1993 — <c>isTemplateLiteral</c>.</summary>
    public static bool IsTemplateLiteral(Node node)
        => node.Kind is SyntaxKind.TemplateExpression or SyntaxKind.NoSubstitutionTemplateLiteral;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1526-1530 — <c>isTemplateMiddleOrTemplateTail</c>.</summary>
    public static bool IsTemplateMiddleOrTemplateTail(Node node)
        => node.Kind is SyntaxKind.TemplateMiddle or SyntaxKind.TemplateTail;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1574-1576 — <c>isImportAttributeName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>Parser</c> holds a <c>IsImportAttributeName</c> of its own, but it is <c>parser.ts:2710</c>'s
    /// private scanner callback — a different function in a different class, so neither is a duplicate of the other.
    /// </remarks>
    public static bool IsImportAttributeName(Node node)
        => NodeTests.IsStringLiteral(node) || NodeTests.IsIdentifier(node);

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:12348-12352 — the `nodesClone` slot of getSynthesizedDeepCloneWorker
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:12348-12352 — the <c>nodesClone</c> slot of
    /// <c>getSynthesizedDeepCloneWorker</c>, as a <c>NodesVisitor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>nodesClone</c> is itself generic in the element type, so it is assignable to
    /// <c>visitEachChild</c>'s <c>nodesVisitor</c> slot; the port's <see cref="NodesVisitor"/> is that same capability,
    /// and this class forwards each array to the reference's own one-argument clone (the <c>replaceNode</c> arm or the
    /// plain one). The <c>visitor</c>/<c>test</c>/<c>start</c>/<c>count</c> parameters the reference's <c>nodesClone</c>
    /// never names are ignored, exactly as a JS function declared with fewer parameters ignores them.
    /// </remarks>
    private sealed class DeepCloneNodesVisitor : NodesVisitor
    {
        private readonly Func<Node, Node?>? _replaceNode;

        public DeepCloneNodesVisitor(Func<Node, Node?>? replaceNode) => _replaceNode = replaceNode;

        public override NodeArray<TNode>? Visit<TNode>(
            NodeArray<TNode>? nodes,
            Func<Node, object?>? visitor,
            Func<Node, bool>? test = null,
            int? start = null,
            int? count = null)
        {
            if (nodes is null) return null;
            return _replaceNode is not null
                ? GetSynthesizedDeepClonesWithReplacements(nodes, /*includeTrivia*/ true, _replaceNode)
                : GetSynthesizedDeepClones(nodes);
        }
    }
}

/// <summary>ported from: src/compiler/types.ts:9714-9732 — <c>NodeVisitor</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares an interface with two generic call signatures, i.e. a first-class generic function. C# cannot
/// instantiate a generic delegate per call, so the port states the same capability as an abstract class whose one
/// generic method infers the node type from its argument — exactly what TS's inference supplied at every call site in
/// <c>visitEachChildTable</c> (the result was never annotated: it was the type of the node handed in, or the type the
/// <c>test</c> predicate narrows it to, and those are the same node type here). The two overloads collapse into one
/// because C#'s optional <c>test</c> covers the reference's second signature, and <c>TIn | TVisited | TOut</c> collapses
/// to the single type variable the argument determines.
/// </para>
/// <para>
/// PORT-NOTE: the type variable is constrained to <see cref="INode"/> rather than <see cref="Node"/> because a slot may
/// hold a categorisation interface (<c>ITypeNode</c>, <c>IStatement</c>, …); the conversion to the class the visitor
/// slot is typed over is §3's, stated once at each such point.
/// </para>
/// </remarks>
internal abstract class NodeVisitor
{
    public abstract TNode? Visit<TNode>(
        TNode? node,
        Func<Node, object?> visitor,
        Func<Node, bool>? test = null,
        Func<IReadOnlyList<Node>, Node?>? lift = null)
        where TNode : class, INode;
}

/// <summary>ported from: src/compiler/types.ts:9740-9756 — <c>NodesVisitor</c>.</summary>
/// <remarks>
/// PORT-NOTE: as <see cref="NodeVisitor"/>, an abstract class with one generic method in place of a generic function
/// type. TS's <c>TInArray extends NodeArray&lt;TIn&gt; | undefined</c> is the port's <c>NodeArray&lt;TNode&gt;?</c>: the
/// array's own element type is what the caller stores the result back into, so the single type variable carries both
/// <c>TIn</c> and <c>TOut</c> — they are the same node type at every call site in the table.
/// </remarks>
internal abstract class NodesVisitor
{
    public abstract NodeArray<TNode>? Visit<TNode>(
        NodeArray<TNode>? nodes,
        Func<Node, object?>? visitor,
        Func<Node, bool>? test = null,
        int? start = null,
        int? count = null)
        where TNode : class, INode;
}

/// <summary>ported from: src/compiler/visitorPublic.ts:223-271 — <c>visitNodes</c>, as the default
/// <c>NodesVisitor</c>.</summary>
/// <remarks>PORT-NOTE: TS's default argument is the function itself (<c>nodesVisitor = visitNodes</c>); a C# parameter
/// default cannot name a type declared later in the same file, so the slot defaults to <c>null</c> and each member reads
/// this singleton — the same function, at the same moment.</remarks>
internal sealed class DefaultNodesVisitor : NodesVisitor
{
    public static readonly DefaultNodesVisitor Instance = new();

    private DefaultNodesVisitor()
    {
    }

    public override NodeArray<TNode>? Visit<TNode>(
        NodeArray<TNode>? nodes,
        Func<Node, object?>? visitor,
        Func<Node, bool>? test = null,
        int? start = null,
        int? count = null)
        => Utilities.VisitNodes(nodes, visitor, test, start, count);
}

/// <summary>ported from: src/compiler/visitorPublic.ts:148-176 — <c>visitNode</c>, as the default
/// <c>NodeVisitor</c>.</summary>
/// <remarks>PORT-NOTE: as <see cref="DefaultNodesVisitor"/> — TS's default argument is <c>visitNode</c> itself.</remarks>
internal sealed class DefaultNodeVisitor : NodeVisitor
{
    public static readonly DefaultNodeVisitor Instance = new();

    private DefaultNodeVisitor()
    {
    }

    public override TNode? Visit<TNode>(
        TNode? node,
        Func<Node, object?> visitor,
        Func<Node, bool>? test = null,
        Func<IReadOnlyList<Node>, Node?>? lift = null)
        where TNode : class
        => Utilities.VisitNode(node, visitor, test, lift);
}

/// <summary>ported from: src/compiler/types.ts:9552-9565 — the slice of <c>CoreTransformationContext</c> that
/// <c>visitorPublic.ts</c> reads.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS types <c>visitEachChild</c>'s <c>context</c> as the full <c>TransformationContext</c>. The port's
/// generated <see cref="ITransformationContext"/> declares <c>INodeFactory? factory</c>, and the port's
/// <see cref="NodeFactory"/> does not implement <see cref="INodeFactory"/> (docs/architecture.md §15.9: the six
/// <c>getName</c> members and the <c>CreateNodeArray&lt;T&gt;</c> constraint keep them apart), so no type in the port can
/// implement that interface — starting from it would make the visitor unwritable. This interface is the eleven members
/// the visitor functions here actually read: the factory, the compiler options (for
/// <c>getEmitScriptTarget</c>), and the nine lexical-environment/block-scope steps. It follows the same ruling
/// <see cref="IEmitHelperFactoryContext"/> records for <c>factory/emitHelpers.ts</c>, and its <c>Factory</c> slot is the
/// concrete <see cref="NodeFactory"/> for the same reason.
/// </para>
/// </remarks>
internal interface IVisitorTransformationContext
{
    /// <summary>ported from: src/compiler/types.ts:9559 — <c>CoreTransformationContext.factory</c>.</summary>
    NodeFactory Factory { get; }

    /// <summary>ported from: src/compiler/types.ts:9560 — <c>CoreTransformationContext.getCompilerOptions</c>.</summary>
    ICompilerOptions GetCompilerOptions();

    /// <summary>ported from: src/compiler/types.ts:9561 — <c>CoreTransformationContext.startLexicalEnvironment</c>.</summary>
    void StartLexicalEnvironment();

    /// <summary>ported from: src/compiler/types.ts:9562 — <c>CoreTransformationContext.setLexicalEnvironmentFlags</c>.</summary>
    void SetLexicalEnvironmentFlags(LexicalEnvironmentFlags flags, bool value);

    /// <summary>ported from: src/compiler/types.ts:9563 — <c>CoreTransformationContext.getLexicalEnvironmentFlags</c>.</summary>
    LexicalEnvironmentFlags GetLexicalEnvironmentFlags();

    /// <summary>ported from: src/compiler/types.ts:9564 — <c>CoreTransformationContext.suspendLexicalEnvironment</c>.</summary>
    void SuspendLexicalEnvironment();

    /// <summary>ported from: src/compiler/types.ts:9565 — <c>CoreTransformationContext.resumeLexicalEnvironment</c>.</summary>
    void ResumeLexicalEnvironment();

    /// <summary>ported from: src/compiler/types.ts:9566 — <c>CoreTransformationContext.endLexicalEnvironment</c>.</summary>
    List<IStatement>? EndLexicalEnvironment();

    /// <summary>ported from: src/compiler/types.ts:9568 — <c>CoreTransformationContext.startBlockScope</c>.</summary>
    void StartBlockScope();

    /// <summary>ported from: src/compiler/types.ts:9569 — <c>CoreTransformationContext.endBlockScope</c>.</summary>
    List<IStatement>? EndBlockScope();

    /// <summary>ported from: src/compiler/types.ts:9571 — <c>CoreTransformationContext.addInitializationStatement</c>.</summary>
    void AddInitializationStatement(IStatement? node);
}

/// <summary>ported from: src/compiler/transformer.ts:669-696 — <c>nullTransformationContext</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: <c>visitorPublic.ts</c>'s <c>visitEachChild</c> defaults its <c>context</c> to this object, and the
/// checker's call sites pass <c>undefined</c> for it, so this singleton is what every checker-driven walk runs against:
/// the factory is the real one and every lexical-environment/block-scope member is a no-op, exactly as the reference's
/// object literal states them. The members that make the object literal the full <c>TransformationContext</c> — the three
/// <c>notImplemented</c> getters (<c>getEmitResolver</c>, <c>getEmitHost</c>, <c>getEmitHelperFactory</c>,
/// <c>readEmitHelpers</c>, <c>isSubstitutionEnabled</c>, <c>isEmitNotificationEnabled</c>), the emit-helper and
/// substitution setters, and <c>addDiagnostic</c> — are not declared here: no visitor function reads them, and
/// <see cref="IVisitorTransformationContext"/> is the slice this file's callers use. They land with <c>transformer.ts</c>
/// (M7-B), which is where the reference declares this value.
/// </para>
/// <para>
/// PORT-NOTE (home): TS declares it in <c>transformer.ts</c>, whose port is the unlanded M7-B slice. It is stated here
/// because this file is its only caller until that slice lands — the same "the reader declares it" rule the predicates
/// at the end of <see cref="Utilities"/> follow.
/// </para>
/// </remarks>
internal sealed class NullTransformationContext : IVisitorTransformationContext
{
    /// <summary>ported from: src/compiler/transformer.ts:669 — the <c>nullTransformationContext</c> value.</summary>
    public static readonly NullTransformationContext Instance = new();

    private NullTransformationContext()
    {
    }

    /// <summary>ported from: src/compiler/transformer.ts:670 — <c>factory: factory</c>.</summary>
    public NodeFactory Factory => NodeFactory.Factory;

    /// <summary>ported from: src/compiler/transformer.ts:671 — <c>getCompilerOptions: () =&gt; ({})</c>.</summary>
    public ICompilerOptions GetCompilerOptions() => new CompilerOptions();

    /// <summary>ported from: src/compiler/transformer.ts:675 — <c>startLexicalEnvironment: noop</c>.</summary>
    public void StartLexicalEnvironment()
    {
    }

    /// <summary>ported from: src/compiler/transformer.ts:676 — <c>resumeLexicalEnvironment: noop</c>.</summary>
    public void ResumeLexicalEnvironment()
    {
    }

    /// <summary>ported from: src/compiler/transformer.ts:677 — <c>suspendLexicalEnvironment: noop</c>.</summary>
    public void SuspendLexicalEnvironment()
    {
    }

    /// <summary>ported from: src/compiler/transformer.ts:678 — <c>endLexicalEnvironment: returnUndefined</c>.</summary>
    public List<IStatement>? EndLexicalEnvironment() => null;

    /// <summary>ported from: src/compiler/transformer.ts:679 — <c>setLexicalEnvironmentFlags: noop</c>.</summary>
    public void SetLexicalEnvironmentFlags(LexicalEnvironmentFlags flags, bool value)
    {
    }

    /// <summary>ported from: src/compiler/transformer.ts:680 — <c>getLexicalEnvironmentFlags: () =&gt; 0</c>.</summary>
    public LexicalEnvironmentFlags GetLexicalEnvironmentFlags() => LexicalEnvironmentFlags.None;

    /// <summary>ported from: src/compiler/transformer.ts:683 — <c>addInitializationStatement: noop</c>.</summary>
    public void AddInitializationStatement(IStatement? node)
    {
    }

    /// <summary>ported from: src/compiler/transformer.ts:684 — <c>startBlockScope: noop</c>.</summary>
    public void StartBlockScope()
    {
    }

    /// <summary>ported from: src/compiler/transformer.ts:685 — <c>endBlockScope: returnUndefined</c>.</summary>
    public List<IStatement>? EndBlockScope() => null;
}
