using System.Diagnostics;

namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:4160-4267 (FlowNode, FlowNodeBase and the variant payloads)
// ported from: src/compiler/binder.ts:495-497 (createFlowNode)
/// <summary>
/// One node of the control-flow graph the binder builds for a source file.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>FlowNodeBase</c> as an interface and <c>FlowNode</c> as the union of the nine
/// interfaces that extend it (types.ts:4170-4179). C# has no union type, and the variants are not distinct at run
/// time either — <c>flags</c> is the discriminator the checker switches on. The port therefore has one concrete
/// class for the whole union, and it implements the generated <see cref="IFlowNodeBase"/> that stands for
/// <c>FlowNodeBase</c>. <c>tools/Gen</c> maps every slot TS types <c>FlowNode</c> onto this class
/// (<c>TsTypeMapper.s_overrides</c>), which is what lets <see cref="IFlowContainer.FlowNode"/> and
/// <c>SourceFile.EndFlowNode</c> hold one.
/// <para>
/// PORT-NOTE: each TS variant interface narrows <c>node</c>/<c>antecedent</c> to its own payload
/// (types.ts:4190-4259); one C# class cannot, so the base slot keeps <see cref="IFlowNodeBase"/>'s
/// <c>object?</c> and a consumer casts exactly where TS's checker inserted an implicit narrowing. The two
/// object payloads TS defines have their own classes below.
/// </para>
/// <para>
/// PORT-NOTE: <c>id</c> is a plain writable slot rather than a value assigned here: TS's own flow-node factory
/// leaves it <c>0</c> and the checker's flow-type cache fills it in (<c>getFlowNodeId</c>).
/// </para>
/// <para>
/// PORT-NOTE: the class also declares the nine variant interfaces. They are empty in the port — the generated
/// <c>IFlow*</c> interfaces kept the base members only, because each TS variant narrows <c>node</c>/<c>antecedent</c>
/// to a different type and one C# class cannot — so implementing them costs nothing at run time and is what lets the
/// slots that TS types by a variant (<c>FlowReduceLabelData.target</c> is a <c>FlowLabel</c>) accept a flow node at
/// all. The discriminator stays <see cref="Flags"/>, exactly as at run time upstream.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebugDisplay,nq}")]
internal partial class FlowNode : IFlowNodeBase, IFlowArrayMutation, IFlowAssignment, IFlowCall, IFlowCondition, IFlowLabel, IFlowReduceLabel, IFlowStart, IFlowSwitchClause, IFlowUnreachable
{
    /// <summary>ported from: src/compiler/types.ts:4184 — <c>FlowNodeBase.flags</c>.</summary>
    public FlowFlags Flags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4185 — <c>FlowNodeBase.id</c>.</summary>
    public int Id { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4186 — <c>FlowNodeBase.node</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>unknown</c> has no C# counterpart, so the slot stays <c>object?</c>.</remarks>
    public object? Node { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4187 — <c>FlowNodeBase.antecedent</c>.</summary>
    /// <remarks>PORT-NOTE: <c>FlowNode | FlowNode[] | undefined</c> widens to <c>object?</c>: the array branch is a
    /// <c>List&lt;FlowNode&gt;</c> and shares no base with the single-node branch (§12.3).</remarks>
    public object? Antecedent { get; set; }

    /// <summary>ported from: src/compiler/debug.ts:519-535 — <c>__tsDebuggerDisplay</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS attaches this with <c>Object.defineProperties</c> from <see cref="Debug.AttachFlowNodeDebugInfo"/>;
    /// C# exposes it as a real member and attaches it with <see cref="DebuggerDisplayAttribute"/> on the class,
    /// which is the same relationship the debugger sees.
    /// </remarks>
    internal string DebugDisplay
    {
        get
        {
            var flags = (int)Flags;
            var header = (flags & (int)FlowFlags.Start) != 0 ? "FlowStart"
                : (flags & (int)FlowFlags.BranchLabel) != 0 ? "FlowBranchLabel"
                : (flags & (int)FlowFlags.LoopLabel) != 0 ? "FlowLoopLabel"
                : (flags & (int)FlowFlags.Assignment) != 0 ? "FlowAssignment"
                : (flags & (int)FlowFlags.TrueCondition) != 0 ? "FlowTrueCondition"
                : (flags & (int)FlowFlags.FalseCondition) != 0 ? "FlowFalseCondition"
                : (flags & (int)FlowFlags.SwitchClause) != 0 ? "FlowSwitchClause"
                : (flags & (int)FlowFlags.ArrayMutation) != 0 ? "FlowArrayMutation"
                : (flags & (int)FlowFlags.Call) != 0 ? "FlowCall"
                : (flags & (int)FlowFlags.ReduceLabel) != 0 ? "FlowReduceLabel"
                : (flags & (int)FlowFlags.Unreachable) != 0 ? "FlowUnreachable"
                : "UnknownFlow";
            var remainingFlags = flags & ~((int)FlowFlags.Referenced - 1);
            return $"{header}{(remainingFlags != 0 ? $" ({Debug.FormatFlowFlags((FlowFlags)remainingFlags)})" : "")}";
        }
    }

    /// <summary>ported from: src/compiler/debug.ts:536-540 — <c>__debugFlowFlags</c>.</summary>
    internal string DebugFlowFlags => Debug.FormatFlowFlags(Flags);

    /// <summary>ported from: src/compiler/binder.ts:495-497 — <c>createFlowNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's factory is a module-level function of <c>binder.ts</c>; the port keeps it next to the class
    /// it constructs, so the two halves of "what a flow node is" do not drift apart. <c>id</c> starts at 0, as
    /// upstream.
    /// </remarks>
    public static FlowNode CreateFlowNode(FlowFlags flags, object? node, object? antecedent)
        => Debug.AttachFlowNodeDebugInfo(new FlowNode
        {
            Flags = flags,
            Id = 0,
            Node = node,
            Antecedent = antecedent,
        });
}

/// <summary>ported from: src/compiler/types.ts:4250-4255 — <c>FlowSwitchClauseData</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS builds this payload as an object literal in <c>createFlowSwitchClause</c>; a C# class is the
/// same shape with a name the slots can be typed by.
/// </remarks>
internal partial class FlowSwitchClauseData : IFlowSwitchClauseData
{
    /// <summary>ported from: src/compiler/types.ts:4251 — <c>FlowSwitchClauseData.switchStatement</c>.</summary>
    public SwitchStatement? SwitchStatement { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4252 — <c>FlowSwitchClauseData.clauseStart</c>.</summary>
    public int ClauseStart { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4253 — <c>FlowSwitchClauseData.clauseEnd</c>.</summary>
    public int ClauseEnd { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:4261-4265 — <c>FlowReduceLabelData</c>.</summary>
internal partial class FlowReduceLabelData : IFlowReduceLabelData
{
    /// <summary>ported from: src/compiler/types.ts:4262 — <c>FlowReduceLabelData.target</c>.</summary>
    /// <remarks>PORT-NOTE: TS types this <c>FlowLabel</c>; the port's generated interface spells it
    /// <see cref="IFlowLabel"/>, the mixin that stands for the <c>FlowLabel</c> variant.</remarks>
    public IFlowLabel? Target { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4263 — <c>FlowReduceLabelData.antecedents</c>.</summary>
    public List<FlowNode>? Antecedents { get; set; }
}
