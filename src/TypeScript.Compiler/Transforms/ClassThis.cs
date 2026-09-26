#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/classThis.ts:1-148 — the whole classThis module
/// <summary>
/// ported from: src/compiler/transformers/classThis.ts:33-148 — the <c>_classThis</c> helper: the <c>static {}</c>
/// block that captures the static <c>this</c>, the class query and node guard that recognise one, and the injector that
/// puts one in front of a class's members.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports these as module-level functions of
/// <c>transformers/classThis.ts</c>, so they land on the class named after that module and are called qualified
/// (<c>ClassThis.InjectClassThisAssignmentIfMissing</c>). <c>createClassThisAssignmentBlock</c> is not exported by the
/// reference, so it is private here.
/// </para>
/// <para>
/// PORT-NOTE (<c>ClassThisAssignmentBlock</c>): the reference's exported type is the intersection
/// <c>ClassStaticBlockDeclaration &amp; { body: … }</c>, which refines the block's shape so the guard
/// <c>isClassThisAssignmentBlock</c> hands its callers a body/statement/expression chain that needs no casts, and so the
/// class-field printers can read those three members through it. An intersection has no C# spelling (§12.5), so the
/// three members below answer the unrefined <see cref="ClassStaticBlockDeclaration"/> and the guard answers
/// <c>bool</c> — the treatment every narrowing predicate gets.
/// </para>
/// <para>
/// PORT-NOTE (<c>thisExpression</c>): TS defaults the parameter to <c>factory.createThis()</c>; a C# default value may
/// not read another parameter, so the port defaults it to <c>null</c> and applies <c>?? factory.CreateThis()</c> at the
/// head of both bodies — the same value, built at the same moment (the injector hands the default straight through to
/// the block factory, which is the only reader of it).
/// </para>
/// <para>
/// PORT-NOTE (<c>setSourceMapRange</c>): the reference hands the class name's <em>node</em> to the range slot, which is
/// legal there because <c>SourceMapRange</c> is an interface that <c>TextRange</c> satisfies structurally; no port type
/// satisfies <see cref="ISourceMapRange"/> structurally (<see cref="Node"/> implements
/// <see cref="ITextRange"/> only), so the range holds a copy of the name's <c>Pos</c>/<c>End</c> — the two members the
/// printer reads, and the reference's range reads as the same two values (<c>source</c> is absent on both, so
/// <c>emitSourceMapSource</c> falls back to the printer's own source exactly as it does there). docs/architecture.md
/// §15.5 records the same structural gap on the read side (<c>getSourceMapRange</c>).
/// </para>
/// <para>
/// PORT-NOTE (<c>isClassThisAssignmentBlock</c>): TS's <c>isAssignmentExpression(statement.expression, true)</c> is a
/// type guard, so the reference reads <c>.left</c>/<c>.right</c> straight off the call; the port's predicate answers
/// <c>bool</c>, and the binary test that follows it is the narrowing the reference gets from the guard — it cannot fail
/// on an expression the predicate accepted, so the short-circuit order is unchanged.
/// </para>
/// </remarks>
internal static class ClassThis
{
    /// <summary>ported from: src/compiler/transformers/classThis.ts:33-50 — <c>createClassThisAssignmentBlock</c>.</summary>
    /// <remarks>
    /// <para>
    /// Produces:
    /// <code>
    /// static { _classThis = this; }
    /// </code>
    /// </para>
    /// <para>
    /// PORT-NOTE (the <c>classThis</c> marker): <c>getOrCreateEmitNode(block).classThis = classThis</c> marks the block
    /// as the <c>_classThis</c> assignment helper and stashes the variable; the port reads the same slot through
    /// <see cref="EmitNodes.GetOrCreateEmitNode"/> and <see cref="IEmitNode.ClassThis"/>.
    /// </para>
    /// </remarks>
    private static ClassStaticBlockDeclaration CreateClassThisAssignmentBlock(
        NodeFactory factory,
        Identifier classThis,
        ThisExpression? thisExpression = null)
    {
        var expression = factory.CreateAssignment(classThis, thisExpression ?? factory.CreateThis());
        var statement = factory.CreateExpressionStatement(expression);
        var body = factory.CreateBlock([statement], /*multiLine*/ false);
        var block = factory.CreateClassStaticBlockDeclaration(body);

        // We use `emitNode.classThis` to indicate this is a `_classThis` assignment helper block
        // and to stash the variable used for `_classThis`.
        EmitNodes.GetOrCreateEmitNode(block).ClassThis = classThis;

        return block;
    }

    /// <summary>ported from: src/compiler/transformers/classThis.ts:62-88 — <c>isClassThisAssignmentBlock</c>.</summary>
    /// <remarks>
    /// Gets whether a node is a <c>static {}</c> block containing only a single assignment of the static <c>this</c> to
    /// the <c>_classThis</c> (or similar) variable stored in the <c>classThis</c> property of the block's
    /// <c>EmitNode</c>.
    /// <para>
    /// PORT-NOTE: the reference's guard is <c>node.emitNode?.classThis === statement.expression.left</c> — the
    /// identity test is <see cref="ReferenceEquals"/> here, and the absent emit node reads as <c>null</c> on both sides
    /// (TS's <c>undefined</c>, which its <c>===</c> also refuses to match against an identifier).
    /// </para>
    /// </remarks>
    public static bool IsClassThisAssignmentBlock(Node node)
    {
        if (!NodeTests.IsClassStaticBlockDeclaration(node)
            || ((ClassStaticBlockDeclaration)node).Body!.Statements!.Count != 1)
        {
            return false;
        }

        var statement = (Node)((ClassStaticBlockDeclaration)node).Body!.Statements![0];
        if (!NodeTests.IsExpressionStatement(statement))
        {
            return false;
        }

        var expression = ((ExpressionStatement)statement).Expression;
        return expression is not null
            && Utilities.IsAssignmentExpression(expression, /*excludeCompoundAssignment*/ true)
            && expression is BinaryExpression assignment
            && assignment.Left is Identifier left
            && ReferenceEquals(node.EmitNode?.ClassThis, left)
            && assignment.Right!.Kind == SyntaxKind.ThisKeyword;
    }

    /// <summary>ported from: src/compiler/transformers/classThis.ts:90-96 — <c>classHasClassThisAssignment</c>.</summary>
    /// <remarks>
    /// Gets whether a <c>ClassLikeDeclaration</c> has a <c>static {}</c> block containing only a single assignment to a
    /// <c>_classThis</c> (or similar) variable.
    /// <para>
    /// PORT-NOTE: the reference's <c>ClassLikeDeclaration</c> is the class/class-expression union, which the model
    /// carries as <see cref="IClassLikeDeclarationBase"/>; <c>some</c> is <see cref="Core.Some{T}(IList{T}?, Func{T, bool}?)"/>
    /// and its element is the model's <c>IClassElement</c> face, so the guard receives the <see cref="Node"/> cast.
    /// </para>
    /// </remarks>
    public static bool ClassHasClassThisAssignment(IClassLikeDeclarationBase node)
        => ((Node)node).EmitNode?.ClassThis is not null
            && Core.Some(node.Members, member => IsClassThisAssignmentBlock((Node)member));

    /// <summary>ported from: src/compiler/transformers/classThis.ts:98-147 — <c>injectClassThisAssignmentIfMissing</c>.</summary>
    /// <remarks>
    /// <para>
    /// Injects a class <c>static {}</c> block containing only an assignment of the static <c>this</c> to a
    /// <c>_classThis</c> (or similar) variable. Given:
    /// <code>
    /// class C {
    /// }
    /// </code>
    /// produces:
    /// <code>
    /// class C {
    ///     static { _classThis = this; }
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// PORT-NOTE (the two overloads): TS declares a signature whose answer is
    /// <c>Extract&lt;ClassLikeDeclaration, Pick&lt;T, "kind"&gt;&gt;</c> — the same concrete class the caller handed in —
    /// over an implementation whose answer is the union; the port states the one generic member and answers
    /// <c>T</c>, which is that type. The constraint is the model's class-like face plus the
    /// <see cref="Node"/> base it is cast through, since <c>ClassDeclaration</c> and <c>ClassExpression</c> share no
    /// base class (their <c>ClassLikeDeclaration</c> union is an interface in the model).
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>createNodeArray</c>): the reference's <c>[staticBlock, ...node.members]</c> spreads the class's
    /// members into a fresh array; the port fills the same fresh <c>List&lt;IClassElement&gt;</c> and hands it to the
    /// factory, which owns the array's <c>NodeArray</c> identity and trailing-comma flag.
    /// </para>
    /// </remarks>
    public static T InjectClassThisAssignmentIfMissing<T>(
        NodeFactory factory,
        T node,
        Identifier classThis,
        ThisExpression? thisExpression = null)
        where T : Node, IClassLikeDeclarationBase
    {
        if (ClassHasClassThisAssignment(node))
        {
            return node;
        }

        var staticBlock = CreateClassThisAssignmentBlock(factory, classThis, thisExpression);
        if (node.Name is not null)
        {
            EmitNodes.SetSourceMapRange(
                (Node)staticBlock.Body!.Statements![0],
                new SourceMapRange { Pos = node.Name.Pos, End = node.Name.End });
        }

        var members = new List<IClassElement> { staticBlock };
        members.AddRange(node.Members ?? []);
        var memberArray = factory.CreateNodeArray(members, null);
        Utilities.SetTextRange(memberArray, node.Members);

        var updatedNode = NodeTests.IsClassDeclaration(node)
            ? (Node)factory.UpdateClassDeclaration(
                (ClassDeclaration)(Node)node,
                ((ClassDeclaration)(Node)node).Modifiers,
                ((ClassDeclaration)(Node)node).Name,
                ((ClassDeclaration)(Node)node).TypeParameters,
                ((ClassDeclaration)(Node)node).HeritageClauses,
                memberArray)
            : factory.UpdateClassExpression(
                (ClassExpression)(Node)node,
                ((ClassExpression)(Node)node).Modifiers,
                ((ClassExpression)(Node)node).Name,
                ((ClassExpression)(Node)node).TypeParameters,
                ((ClassExpression)(Node)node).HeritageClauses,
                memberArray);

        EmitNodes.GetOrCreateEmitNode(updatedNode).ClassThis = classThis;
        return (T)updatedNode;
    }
}
