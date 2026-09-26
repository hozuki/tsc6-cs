#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:1151-1163 (insertStatementsAfterPrologue/-AfterStandardPrologue),
//             :7845-7852 (createRange), :7870-7872 (moveRangePos), :7879-7884 (moveRangePastDecorators),
//             :7891-7898 (moveRangePastModifiers), :7910-7912 (createTokenRange), :8019-8021 (getInitializedVariables);
//             src/compiler/checker.ts:1479-1483 (isInstantiatedModule);
//             src/compiler/factory/nodeFactory.ts:6745-6884 (getName and the five members built on it).
/// <summary>
/// The helpers <c>transformers/ts.ts</c> calls that no landed slice of the port states yet.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): every member here belongs to a file another slice owns — <c>Utilities.cs</c> for the six
/// <c>utilities.ts</c> helpers, <c>NodeFactory</c> for the <c>getName</c> family (which
/// <c>docs/architecture.md</c> §15.9 records as the M5 residue "six <c>getName</c> members"), and <c>Checker</c> for
/// <c>isInstantiatedModule</c> (whose instance method exists but is unreachable from a transformer). They are stated
/// here, beside their only reader, so this slice is runnable; the batch report names the move. <c>Printer</c> keeps
/// its own private copies of the range helpers for the same reason (Emit/Printer.Statements.cs:2239-2274).
/// </para>
/// <para>
/// PORT-NOTE (<c>createRange</c>): TS builds a <c>{ pos, end }</c> object literal; the port's <see cref="TextRange"/>
/// is the constructible class with the same two slots, as in <c>Utilities.RangeOfNode</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>node.modifiers</c>): the model has no <c>HasModifiers</c> interface, so the two
/// <c>moveRangePast*</c> helpers read the member through <see cref="Utilities.ModifiersOf"/> — the same two calls
/// <c>Printer</c>'s copies make.
/// </para>
/// </remarks>
internal static class TypeScriptTransformSupport
{
    // ---------------------------------------------------------------------------------------------
    // utilities.ts — the range helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7845-7852 — <c>createRange</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's second parameter defaults to <c>pos</c>, which is the port's <c>null</c>.</remarks>
    public static TextRange CreateRange(int pos, int? end = null)
    {
        var rangeEnd = end ?? pos;
        Debug.Assert(rangeEnd >= pos || rangeEnd == -1);
        return new TextRange { Pos = pos, End = rangeEnd };
    }

    /// <summary>ported from: src/compiler/utilities.ts:7870-7872 — <c>moveRangePos</c>.</summary>
    public static ITextRange MoveRangePos(ITextRange range, int pos) => CreateRange(pos, range.End);

    /// <summary>ported from: src/compiler/utilities.ts:7879-7884 — <c>moveRangePastDecorators</c>.</summary>
    public static ITextRange MoveRangePastDecorators(Node node)
    {
        var lastDecorator = Utilities.CanHaveModifiers(node)
            ? Utilities.ModifiersOf(node)?.LastOrDefault(NodeTests.IsDecorator)
            : null;
        return lastDecorator is not null && !ScannerUtilities.PositionIsSynthesized(lastDecorator.End)
            ? MoveRangePos(node, lastDecorator.End)
            : node;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7891-7898 — <c>moveRangePastModifiers</c>.</summary>
    public static ITextRange MoveRangePastModifiers(Node node)
    {
        if (NodeTests.IsPropertyDeclaration(node) || NodeTests.IsMethodDeclaration(node))
        {
            return MoveRangePos(node, ((INamedDeclaration)node).Name!.Pos);
        }

        var lastModifier = Utilities.CanHaveModifiers(node) ? Utilities.ModifiersOf(node)?.LastOrDefault() : null;
        return lastModifier is not null && !ScannerUtilities.PositionIsSynthesized(lastModifier.End)
            ? MoveRangePos(node, lastModifier.End)
            : MoveRangePastDecorators(node);
    }

    /// <summary>ported from: src/compiler/utilities.ts:7910-7912 — <c>createTokenRange</c>.</summary>
    /// <remarks>PORT-NOTE: <c>tokenToString(token)!</c> is <see cref="ScannerUtilities.TokenToString"/>, which answers
    /// <c>string?</c>; the reference's non-null assertion is the <c>!</c> — every kind it is called with is a keyword
    /// or punctuation whose name the table holds.</remarks>
    public static TextRange CreateTokenRange(int pos, SyntaxKind token)
        => CreateRange(pos, pos + ScannerUtilities.TokenToString(token)!.Length);

    // ---------------------------------------------------------------------------------------------
    // utilities.ts — the prologue insert and the initialized-variable filter
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:1151-1153 — <c>insertStatementsAfterStandardPrologue</c>.</summary>
    /// <remarks>PORT-NOTE: TS's two <c>insertStatementsAfter*</c> members share the private
    /// <c>insertStatementsAfterPrologue</c> (utilities.ts:1124-1134); only the standard one has a caller here, so only
    /// it and that private helper are stated. The reference's generic <c>&lt;T extends Statement&gt;</c> is
    /// <see cref="IStatement"/>, the element type every caller here uses.</remarks>
    public static List<IStatement> InsertStatementsAfterStandardPrologue(List<IStatement> to, List<IStatement>? from)
        => InsertStatementsAfterPrologue(to, from, Utilities.IsPrologueDirective);

    /// <summary>ported from: src/compiler/utilities.ts:1124-1134 — <c>insertStatementsAfterPrologue</c>.</summary>
    /// <remarks>PORT-NOTE: <c>to.splice(statementIndex, 0, ...from)</c> is <see cref="List{T}.InsertRange"/>.</remarks>
    private static List<IStatement> InsertStatementsAfterPrologue(
        List<IStatement> to,
        List<IStatement>? from,
        Func<Node, bool> isPrologueDirective)
    {
        if (from is null || from.Count == 0) return to;
        var statementIndex = 0;
        // skip all prologue directives to insert at the correct position
        for (; statementIndex < to.Count; ++statementIndex)
        {
            if (!isPrologueDirective((Node)to[statementIndex]))
            {
                break;
            }
        }

        to.InsertRange(statementIndex, from);
        return to;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8019-8021 — <c>getInitializedVariables</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>filter</c> answers a fresh array of the narrowed <c>InitializedVariableDeclaration</c>; the
    /// port's <see cref="Core.Filter{T}"/> may hand back its input (Core.Arrays.cs:285), so the result is materialized
    /// to keep the reference's fresh-array shape. <c>isInitializedVariable</c> (utilities.ts:8024-8026) is inlined as
    /// the predicate, since its only reader is this member.
    /// </remarks>
    public static List<VariableDeclaration> GetInitializedVariables(VariableDeclarationList node)
        => (Core.Filter(node.Declarations, IsInitializedVariable) ?? []).ToList();

    /// <summary>ported from: src/compiler/utilities.ts:8024-8026 — <c>isInitializedVariable</c>.</summary>
    private static bool IsInitializedVariable(VariableDeclaration node)
        => NodeTests.IsVariableDeclaration(node) && node.Initializer is not null;

    // ---------------------------------------------------------------------------------------------
    // checker.ts — isInstantiatedModule
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:1479-1483 — <c>isInstantiatedModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the port already states this body as the <c>Checker</c> instance member
    /// <c>IsInstantiatedModule</c> (Checker/Checker.Support.cs:1855-1864); a transformer has no checker instance, and
    /// the reference's declaration is a file-scope function of <c>checker.ts</c> that both callers reach, so this is
    /// the same body over the same <see cref="Binder.GetModuleInstanceState"/>.
    /// </remarks>
    public static bool IsInstantiatedModule(ModuleDeclaration node, bool preserveConstEnums)
    {
        var moduleState = Binder.GetModuleInstanceState(node);
        return moduleState == ModuleInstanceState.Instantiated
            || (preserveConstEnums && moduleState == ModuleInstanceState.ConstEnumOnly);
    }

    // ---------------------------------------------------------------------------------------------
    // factory/nodeFactory.ts — the getName family (docs/architecture.md §15.9, the M5 residue)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6745-6758 — <c>getName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getNameOfDeclaration</c>/<c>getNonAssignedNameOfDeclaration</c> are
    /// <see cref="Utilities.GetNameOfDeclaration"/>/<see cref="Utilities.GetNonAssignedNameOfDeclaration"/>, both of
    /// which answer <c>Node? | string?</c> in the port, so the guard narrows the same way TS's does.
    /// <c>emitFlags</c>' default <c>0</c> is <see cref="EmitFlags.None"/>, and <c>if (emitFlags)</c> is the port's
    /// <c>!= EmitFlags.None</c>.
    /// </remarks>
    private static Identifier GetName(
        NodeFactory factory,
        Node? node,
        bool? allowComments,
        bool? allowSourceMaps,
        EmitFlags emitFlags = EmitFlags.None,
        bool? ignoreAssignedName = null)
    {
        var nodeName = Js.IsTruthy(ignoreAssignedName)
            ? (node is not null ? Utilities.GetNonAssignedNameOfDeclaration(node) : null)
            : Utilities.GetNameOfDeclaration(node);
        if (nodeName is Identifier identifier && !Utilities.IsGeneratedIdentifier(identifier))
        {
            // TODO(rbuckton): Does this need to be parented?
            var name = Utilities.SetParent(Utilities.SetTextRange(factory.CloneNode(identifier), identifier), identifier.Parent);
            emitFlags |= Utilities.GetEmitFlags(identifier);
            if (allowSourceMaps != true) emitFlags |= EmitFlags.NoSourceMap;
            if (allowComments != true) emitFlags |= EmitFlags.NoComments;
            if (emitFlags != EmitFlags.None) EmitNodes.SetEmitFlags(name!, emitFlags);
            return name!;
        }

        return factory.GetGeneratedNameForNode(node, /*flags*/ null);
    }

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6784-6786 — <c>getInternalName</c>.</summary>
    public static Identifier GetInternalName(NodeFactory factory, Node node, bool? allowComments, bool? allowSourceMaps)
        => GetName(factory, node, allowComments, allowSourceMaps, EmitFlags.LocalName | EmitFlags.InternalName);

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6799-6801 — <c>getLocalName</c>.</summary>
    public static Identifier GetLocalName(NodeFactory factory, Node node, bool? allowComments, bool? allowSourceMaps, bool? ignoreAssignedName = null)
        => GetName(factory, node, allowComments, allowSourceMaps, EmitFlags.LocalName, ignoreAssignedName);

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6814-6816 — <c>getExportName</c>.</summary>
    public static Identifier GetExportName(NodeFactory factory, Node node, bool? allowComments, bool? allowSourceMaps)
        => GetName(factory, node, allowComments, allowSourceMaps, EmitFlags.ExportName);

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6824-6826 — <c>getDeclarationName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>Declaration | undefined</c>; the port's <c>getName</c> takes <c>Node?</c>
    /// for the same reason.</remarks>
    public static Identifier GetDeclarationName(NodeFactory factory, Node? node, bool? allowComments, bool? allowSourceMaps)
        => GetName(factory, node, allowComments, allowSourceMaps);

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6836-6845 — <c>getNamespaceMemberName</c>.</summary>
    public static PropertyAccessExpression GetNamespaceMemberName(
        NodeFactory factory,
        Identifier ns,
        Identifier name,
        bool? allowComments,
        bool? allowSourceMaps)
    {
        var qualifiedName = factory.CreatePropertyAccessExpression(
            ns,
            Utilities.NodeIsSynthesized(name) ? name : factory.CloneNode(name));
        Utilities.SetTextRange(qualifiedName, name);
        var emitFlags = EmitFlags.None;
        if (allowSourceMaps != true) emitFlags |= EmitFlags.NoSourceMap;
        if (allowComments != true) emitFlags |= EmitFlags.NoComments;
        if (emitFlags != EmitFlags.None) EmitNodes.SetEmitFlags(qualifiedName, emitFlags);
        return qualifiedName;
    }

    /// <summary>ported from: src/compiler/factory/nodeFactory.ts:6857-6862 — <c>getExternalModuleOrNamespaceExportName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>Identifier | PropertyAccessExpression</c> is the port's
    /// <see cref="MemberExpression"/>, the model's common base for the two arms.</remarks>
    public static MemberExpression GetExternalModuleOrNamespaceExportName(
        NodeFactory factory,
        Identifier? ns,
        Node node,
        bool? allowComments,
        bool? allowSourceMaps)
    {
        if (ns is not null && Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            return GetNamespaceMemberName(factory, ns, GetName(factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null), allowComments, allowSourceMaps);
        }

        return GetExportName(factory, node, allowComments, allowSourceMaps);
    }

    // ---------------------------------------------------------------------------------------------
    // The `setSourceMapRange` adapter
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The value <see cref="EmitNodes.SetSourceMapRange{T}"/> wants for the ranges <c>ts.ts</c> passes it.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE (the model gap): TS's <c>setSourceMapRange(node, range: SourceMapRange | undefined)</c> is
    /// <em>structural</em> — <c>SourceMapRange = ReadonlyTextRange &amp; { source?: SourceMapSource }</c> — so a node
    /// (or a <c>NodeArray</c>) is a legal range, and <c>ts.ts</c> passes one at four call sites
    /// (<c>ts.ts:2016</c>, <c>:2038</c>, <c>:2566</c> and <c>:1617</c>). The port's <see cref="ISourceMapRange"/> adds a
    /// non-optional <c>Source</c> slot that neither <see cref="Node"/> nor <see cref="NodeArray{T}"/> declares, so the
    /// port's <c>SetSourceMapRange</c> cannot take them as they stand (its reader,
    /// <see cref="EmitNodes.GetSourceMapRange"/>, already widens to <see cref="ITextRange"/> and casts back). This
    /// adapter states the same range as the port's own <see cref="SourceMapRange"/>, capturing <c>Pos</c>/<c>End</c> at
    /// the moment the reference's assignment would have aliased the node — the reader only ever reads those two slots
    /// off it. The generator's fix (widening the setter's parameter to <see cref="ITextRange"/>) is in this slice's
    /// report.
    /// </remarks>
    public static ISourceMapRange SourceMapRangeOf(ITextRange range)
        => range as ISourceMapRange ?? new SourceMapRange { Pos = range.Pos, End = range.End };
}
