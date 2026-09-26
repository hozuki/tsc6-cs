namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-0 的前置模型：<c>Symbol</c> 的具体类、<c>createSymbolTable</c> 的落点，以及流节点类型
/// （docs/architecture.md §12、§15 的 M5 前置）。
/// </summary>
/// <remarks>
/// 这些成员是 binder 的唯一硬前置：没有它们，M5 的每一个切片都编不过。测的是它们自己的语义（构造后的槽位、
/// 符号表的键、流节点的展示串），不是接线。
/// </remarks>
public class SymbolAndFlowNodeTests
{
    // ---------------------------------------------------------------------------------------------
    // Symbol / createSymbolTable
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Symbol_KeepsItsFlagsAndName_AndLeavesEveryCacheSlotEmpty()
    {
        var symbol = new Symbol(SymbolFlags.Function | SymbolFlags.ValueModule, "f");

        Assert.Equal(SymbolFlags.Function | SymbolFlags.ValueModule, symbol.Flags);
        Assert.Equal("f", symbol.EscapedName);
        Assert.Equal(0, symbol.Id);
        Assert.Equal(0, symbol.MergeId);
        Assert.Null(symbol.Declarations);
        Assert.Null(symbol.ValueDeclaration);
        Assert.Null(symbol.Members);
        Assert.Null(symbol.Exports);
        Assert.Null(symbol.GlobalExports);
        Assert.Null(symbol.Parent);
        Assert.Null(symbol.ExportSymbol);
        Assert.Null(symbol.ConstEnumOnlyModule);
        Assert.Null(symbol.IsReferenced);
        Assert.Null(symbol.LastAssignmentPos);
        Assert.Null(symbol.IsReplaceableByMethod);
        Assert.Null(symbol.AssignmentDeclarationMembers);
        Assert.Null(symbol.Links);
    }

    [Fact]
    public void CreateSymbolTable_KeysByEscapedName_AndALaterSymbolReplacesTheEarlierOne()
    {
        var a = new Symbol(SymbolFlags.BlockScopedVariable, "a");
        var b = new Symbol(SymbolFlags.Function, "b");
        var replacement = new Symbol(SymbolFlags.Function, "a");

        var table = Utilities.CreateSymbolTable(new List<ISymbol> { a, b });

        Assert.Equal(2, table.Count);
        Assert.Same(a, table["a"]);
        Assert.Same(b, table["b"]);

        // TS's Map.set overwrites, and a symbol that is both a value and a type is declared twice under one name.
        var overwritten = Utilities.CreateSymbolTable(new List<ISymbol> { a, replacement });

        Assert.Single(overwritten);
        Assert.Same(replacement, overwritten["a"]);

        Assert.Empty(Utilities.CreateSymbolTable());
    }

    // ---------------------------------------------------------------------------------------------
    // FlowNode and its payloads
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CreateFlowNode_FillsFlagsPayloadAndAntecedent_AndStartsAtIdZero()
    {
        var start = FlowNode.CreateFlowNode(FlowFlags.Start, null, null);
        var condition = FlowNode.CreateFlowNode(FlowFlags.TrueCondition, start, start);

        Assert.Equal(FlowFlags.TrueCondition, condition.Flags);
        Assert.Equal(0, condition.Id);
        Assert.Same(start, condition.Node);
        Assert.Same(start, condition.Antecedent);
        Assert.Null(start.Node);
        Assert.Null(start.Antecedent);
    }

    [Fact]
    public void TheDebugDisplay_NamesTheVariantByItsFlag_AndKeepsOnlyTheSharedBitsInParentheses()
    {
        var branch = FlowNode.CreateFlowNode(FlowFlags.BranchLabel, null, null);

        Assert.Equal("FlowBranchLabel", branch.DebugDisplay);
        Assert.Equal("BranchLabel", branch.DebugFlowFlags);

        // `Referenced` and above are reference-counting bits, not part of the variant's name (debug.ts:534).
        var shared = FlowNode.CreateFlowNode(FlowFlags.Start | FlowFlags.Shared, null, null);

        Assert.Equal("FlowStart (Shared)", shared.DebugDisplay);

        var unknown = FlowNode.CreateFlowNode(FlowFlags.Unreachable, null, null);

        Assert.Equal("FlowUnreachable", unknown.DebugDisplay);
    }

    [Fact]
    public void AReduceLabelPayload_AcceptsAFlowLabelAndItsAntecedents()
    {
        var label = FlowNode.CreateFlowNode(FlowFlags.BranchLabel, null, null);
        var data = new FlowReduceLabelData { Target = label, Antecedents = new List<FlowNode> { label } };

        Assert.Same(label, data.Target);
        Assert.Same(label, Assert.Single(data.Antecedents!));
    }

    [Fact]
    public void ASwitchClausePayload_CarriesTheClauseRangeItWasBuiltFrom()
    {
        var data = new FlowSwitchClauseData { ClauseStart = 1, ClauseEnd = 3 };

        Assert.Null(data.SwitchStatement);
        Assert.Equal(1, data.ClauseStart);
        Assert.Equal(3, data.ClauseEnd);
    }
}
