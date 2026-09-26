namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-2 流图的端到端证据：绑定后 <c>flowNode</c> 链上的流节点形状、分支标签的合并（<c>finishFlowLabel</c>）、
/// 赋值/数组变异/调用产生的流变异节点、<c>possiblyExhaustive</c>/<c>fallthroughFlowNode</c>/<c>flowNodeWhenTrue</c>
/// 这些写回树上的槽，以及两个不属任何 M5 切片的 GAP 工具（<c>getCombinedModifierFlags</c>、<c>symbolName</c>）。
/// </summary>
/// <remarks>
/// 断言全部取自上游 binder 写下的东西，不经过 checker。判定方式与 M5-1 的 <c>BinderSkeletonTests</c> 不同：那里断言
/// 符号表，这里断言**控制流图的形状** —— 每条语句的 <c>flowNode</c> 是“进入该语句时的当前流”，于是「下一条语句的
/// <c>flowNode</c>」就是「上一条语句跑完后留下的流」，这正是 <c>createFlowMutation</c>/<c>createFlowCondition</c> 的产物
/// 可观测的方式。
/// <para>
/// 只有 <c>NodeFlags.None..LastStatement</c> 之间的语句节点带 <c>flowNode</c>（<c>canHaveFlowNode</c>，
/// utilities.ts:4535-4560；<c>Block</c> 的 kind 在 <c>FirstStatement</c> 之前，因此块本身没有该槽，TS 也一样），
/// 所以分支流都从「分支里的语句」读回，而不是从块节点读。
/// </para>
/// </remarks>
public class BinderFlowTests
{
    private static SourceFile ParseAndBind(string text, ICompilerOptions? options = null)
    {
        var file = Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, /*setParentNodes*/ false, ScriptKind.TS);
        Binder.BindSourceFile(file, options ?? new FakeCompilerOptions { Target = ScriptTarget.ESNext });
        return file;
    }

    private static T Statement<T>(SourceFile file, int index) where T : Node
        => Assert.IsType<T>(file.Statements![index]);

    /// <summary>一条流节点的前驱数组（<c>FlowNode.antecedent</c> 的列表形态）。</summary>
    private static List<FlowNode> Antecedents(FlowNode flow) => Assert.IsType<List<FlowNode>>(flow.Antecedent);

    /// <summary>一条流节点的唯一前驱。</summary>
    private static FlowNode Antecedent(FlowNode flow) => Assert.IsType<FlowNode>(flow.Antecedent);

    /// <summary>
    /// 断言流节点带有某个标志位。不比对全部位：<c>Referenced</c>/<c>Shared</c> 是流图在把节点登记为前驱时补的
    /// （binder.ts:1368-1371），与这里要证明的形状无关。
    /// </summary>
    private static void AssertHasFlag(FlowFlags expected, FlowFlags actual)
        => Assert.Equal(expected, actual & expected);

    [Fact]
    public void BindSourceFile_ChainsStatementsThroughTheEntryFlowOfEach()
    {
        var file = ParseAndBind("""
            var v = 1;
            next;
            """);

        // 源文件是 control-flow container，bindContainer 于是开一个 Start 流；第一条语句进入时看到的就是它
        // （binder.ts:1007-1012, 1129-1132）。
        var variableStatement = Statement<VariableStatement>(file, 0);
        AssertHasFlag(FlowFlags.Start, variableStatement.FlowNode!.Flags);

        // `var v = 1` 的初始化器是一条 flow mutation（binder.ts:2070-2075 → 2058-2068），所以下一条语句的 flowNode
        // 是那条变异节点：Assignment 位、node 指向 VariableDeclaration、前驱是进入时的 Start 流。
        var next = Statement<ExpressionStatement>(file, 1);
        AssertHasFlag(FlowFlags.Assignment, next.FlowNode!.Flags);
        Assert.Same(variableStatement.DeclarationList!.Declarations![0], next.FlowNode.Node);
        AssertHasFlag(FlowFlags.Start, Antecedent(next.FlowNode).Flags);

        // 源文件结束时 bindContainer 把当前流写到 endFlowNode（binder.ts:1037-1040）。
        Assert.NotNull(file.EndFlowNode);
    }

    [Fact]
    public void BindWhileStatement_TargetsContinueAtTheLoopLabelAndBreakAtThePostLoopLabel()
    {
        var file = ParseAndBind("""
            while (x) {
                if (y) continue;
                if (z) break;
                w();
            }
            after;
            """);
        var whileStatement = Statement<WhileStatement>(file, 0);
        var block = Assert.IsType<Block>(whileStatement.Statement);
        var firstIf = Assert.IsType<IfStatement>(block.Statements![0]);
        var continueStatement = Assert.IsType<ContinueStatement>(firstIf.ThenStatement);
        var breakStatement = Assert.IsType<BreakStatement>(Assert.IsType<IfStatement>(block.Statements![1]).ThenStatement);

        // 循环体第一条语句的 flowNode 是 `x` 的 True 条件节点，它的唯一前驱就是 createLoopLabel() 开出的
        // preWhileLabel —— LoopLabel 位证明那条链走到了循环标签（binder.ts:1515-1526）。
        var loopLabel = Antecedent(firstIf.FlowNode!);
        AssertHasFlag(FlowFlags.LoopLabel, loopLabel.Flags);
        AssertHasFlag(FlowFlags.TrueCondition, firstIf.FlowNode!.Flags);

        // `continue` 的目标由 setContinueTarget 写成循环标签（binder.ts:1505-1513），于是 continue 语句进入时的流
        // 成为该标签的前驱。
        Assert.Contains(continueStatement.FlowNode, Antecedents(loopLabel));

        // 循环之后当前流是 postWhileLabel：`break` 与循环体的正常出口两条前驱让它不再被 finishFlowLabel 折叠成单点
        // （binder.ts:1422-1431）。
        var after = Statement<ExpressionStatement>(file, 1);
        AssertHasFlag(FlowFlags.BranchLabel, after.FlowNode!.Flags);
        Assert.Contains(breakStatement.FlowNode, Antecedents(after.FlowNode));
    }

    [Fact]
    public void BindIfStatement_TurnsANarrowingConditionIntoOneConditionNodePerBranch()
    {
        var file = ParseAndBind("""
            if (a !== undefined) t(); else e();
            """);
        var ifStatement = Statement<IfStatement>(file, 0);
        var thenStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenStatement);
        var elseStatement = Assert.IsType<ExpressionStatement>(ifStatement.ElseStatement);

        // `a !== undefined` 是 narrowing 表达式，于是 createFlowCondition 为两个分支各造一个条件节点，节点上的
        // node 就是被测试的表达式本身（binder.ts:1380-1399, 1487-1493）。
        AssertHasFlag(FlowFlags.TrueCondition, thenStatement.FlowNode!.Flags);
        Assert.Same(ifStatement.Expression, thenStatement.FlowNode.Node);
        AssertHasFlag(FlowFlags.FalseCondition, elseStatement.FlowNode!.Flags);
        Assert.Same(ifStatement.Expression, elseStatement.FlowNode.Node);

        // 两个条件节点都以进入 if 时的流为前驱。
        Assert.Same(ifStatement.FlowNode, Antecedent(thenStatement.FlowNode));
        Assert.Same(ifStatement.FlowNode, Antecedent(elseStatement.FlowNode));
    }

    [Fact]
    public void BindIfStatement_DoesNotOpenAConditionForANonNarrowingCondition()
    {
        var file = ParseAndBind("""
            if (foo()) t(); else e();
            """);
        var ifStatement = Statement<IfStatement>(file, 0);
        var thenStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenStatement);
        var elseStatement = Assert.IsType<ExpressionStatement>(ifStatement.ElseStatement);

        // 调用表达式（实参里没有可窄化引用）不是 narrowing 表达式，createFlowCondition 于是把前驱原样交回，
        // finishFlowLabel 折叠单点前驱 —— 分支的 flowNode 就是进入 if 的那个流本身（binder.ts:1394-1396）。
        Assert.Same(ifStatement.FlowNode, thenStatement.FlowNode);
        Assert.Same(ifStatement.FlowNode, elseStatement.FlowNode);
    }

    [Fact]
    public void BindLogicalLikeExpression_TargetsTheOperandsBranches()
    {
        var file = ParseAndBind("""
            if (a && b) t(); else e();
            """);
        var ifStatement = Statement<IfStatement>(file, 0);
        var condition = Assert.IsType<BinaryExpression>(ifStatement.Expression);
        var thenStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenStatement);
        var elseStatement = Assert.IsType<ExpressionStatement>(ifStatement.ElseStatement);

        // `a && b` 是语句条件，所以 bindCondition 不再为整体造条件节点，而是 bindLogicalLikeExpression 把 `a` 的
        // 真假分支接到 preRightLabel/elseLabel 上，再把 `b` 接到 then/else 上（binder.ts:1487-1493, 1847-1868）。
        // then 分支因此只剩一个前驱：`b` 的 True 条件节点。
        var trueCondition = thenStatement.FlowNode!;
        AssertHasFlag(FlowFlags.TrueCondition, trueCondition.Flags);
        Assert.Same(condition.Right, trueCondition.Node);

        // 它的前驱是 `a` 的 True 条件节点 —— 左操作数的真分支就是右操作数的入口。
        var leftTrueCondition = Antecedent(trueCondition);
        AssertHasFlag(FlowFlags.TrueCondition, leftTrueCondition.Flags);
        Assert.Same(condition.Left, leftTrueCondition.Node);

        // else 分支拿到 `a` 与 `b` 各一个 False 条件节点（两条前驱），于是 finishFlowLabel 交回标签本身。
        AssertHasFlag(FlowFlags.BranchLabel, elseStatement.FlowNode!.Flags);
        var falseConditions = Antecedents(elseStatement.FlowNode);
        Assert.Equal(2, falseConditions.Count);
        Assert.Same(condition.Left, falseConditions[0].Node);
        Assert.Same(condition.Right, falseConditions[1].Node);
    }

    [Fact]
    public void BindPrefixUnaryExpressionFlow_SwapsTheConditionalTargetsUnderANot()
    {
        var file = ParseAndBind("""
            if (!(a && b)) t(); else e();
            """);
        var ifStatement = Statement<IfStatement>(file, 0);
        var notExpression = Assert.IsType<PrefixUnaryExpression>(ifStatement.Expression);
        var condition = Assert.IsType<BinaryExpression>(Assert.IsType<ParenthesizedExpression>(notExpression.Operand).Expression);
        var thenStatement = Assert.IsType<ExpressionStatement>(ifStatement.ThenStatement);
        var elseStatement = Assert.IsType<ExpressionStatement>(ifStatement.ElseStatement);

        // `!` 交换 currentTrueTarget/currentFalseTarget（binder.ts:1870-1885），于是 `a && b` 的真分支接到 else、
        // 假分支接到 then：then 拿到的两个前驱都是 False 条件节点。
        AssertHasFlag(FlowFlags.BranchLabel, thenStatement.FlowNode!.Flags);
        var thenConditions = Antecedents(thenStatement.FlowNode);
        Assert.Equal(2, thenConditions.Count);
        AssertHasFlag(FlowFlags.FalseCondition, thenConditions[0].Flags);
        Assert.Same(condition.Left, thenConditions[0].Node);
        AssertHasFlag(FlowFlags.FalseCondition, thenConditions[1].Flags);
        Assert.Same(condition.Right, thenConditions[1].Node);

        // 对称地：`!(a && b)` 为假的入口只有一条链 —— `a` 真之后 `b` 也真，于是 else 的流是 `b` 的 True 条件节点，
        // 它的唯一前驱是 `a` 的 True 条件节点（正是 bindLogicalLikeExpression 把右操作数接在左操作数的真分支上）。
        var elseCondition = elseStatement.FlowNode!;
        AssertHasFlag(FlowFlags.TrueCondition, elseCondition.Flags);
        Assert.Same(condition.Right, elseCondition.Node);
        var elseLeftCondition = Antecedent(elseCondition);
        AssertHasFlag(FlowFlags.TrueCondition, elseLeftCondition.Flags);
        Assert.Same(condition.Left, elseLeftCondition.Node);
    }

    [Fact]
    public void BindBinaryExpressionFlow_TracksAnElementAccessAssignmentAsAnArrayMutation()
    {
        var file = ParseAndBind("""
            x[0] = 1;
            next;
            """);
        var assignment = Assert.IsType<BinaryExpression>(Statement<ExpressionStatement>(file, 0).Expression);

        // 赋值表达式的左值先变成 Assignment 变异节点（bindAssignmentTargetFlow，binder.ts:1818-1825），随后左值是
        // 元素访问且其对象可窄化时再叠一个 ArrayMutation 变异（binder.ts:1992-2015，走 createBindBinaryExpressionFlow
        // 的蹦床 —— 这条断言是蹦床真的跑起来的证据）。
        var next = Statement<ExpressionStatement>(file, 1);
        var arrayMutation = next.FlowNode!;
        AssertHasFlag(FlowFlags.ArrayMutation, arrayMutation.Flags);
        Assert.Same(assignment, arrayMutation.Node);

        var assignmentMutation = Antecedent(arrayMutation);
        AssertHasFlag(FlowFlags.Assignment, assignmentMutation.Flags);
        Assert.Same(assignment.Left, assignmentMutation.Node);
    }

    [Fact]
    public void BindUpdateExpressionFlow_TracksItsOperandAsAMutation()
    {
        var file = ParseAndBind("""
            i++;
            next;
            """);
        var postfix = Assert.IsType<PostfixUnaryExpression>(Statement<ExpressionStatement>(file, 0).Expression);

        // `i++` 的赋值目标流由 bindPostfixUnaryExpressionFlow 补齐（binder.ts:1887-1892）。
        var mutation = Statement<ExpressionStatement>(file, 1).FlowNode!;
        AssertHasFlag(FlowFlags.Assignment, mutation.Flags);
        Assert.Same(postfix.Operand, mutation.Node);
    }

    [Fact]
    public void BindDeleteExpressionFlow_TracksAPropertyAccessButNotAPlainOperand()
    {
        var withTarget = ParseAndBind("""
            delete a.b;
            next;
            """);
        var deleteExpression = Assert.IsType<DeleteExpression>(Statement<ExpressionStatement>(withTarget, 0).Expression);
        var mutation = Statement<ExpressionStatement>(withTarget, 1).FlowNode!;

        // 操作数是属性访问 → 一条 Assignment 变异（binder.ts:2025-2030）。
        AssertHasFlag(FlowFlags.Assignment, mutation.Flags);
        Assert.Same(deleteExpression.Expression, mutation.Node);

        var withoutTarget = ParseAndBind("""
            delete k;
            next;
            """);

        // 操作数不是属性访问 → 没有变异，下一条语句看到的仍是进入 `delete k` 时的流。
        var deleteStatement = Statement<ExpressionStatement>(withoutTarget, 0);
        var afterDelete = Statement<ExpressionStatement>(withoutTarget, 1);
        Assert.Same(deleteStatement.FlowNode, afterDelete.FlowNode);
        AssertHasFlag(FlowFlags.Start, afterDelete.FlowNode!.Flags);
    }

    [Fact]
    public void BindDestructuringAssignmentFlow_WalksEveryTargetOfThePattern()
    {
        var file = ParseAndBind("""
            [p, { q }] = r;
            next;
            """);
        var assignment = Assert.IsType<BinaryExpression>(Statement<ExpressionStatement>(file, 0).Expression);
        var elements = ((ArrayLiteralExpression)assignment.Left!).Elements!;
        var shorthand = Assert.IsType<ShorthandPropertyAssignment>(Assert.IsType<ObjectLiteralExpression>(elements[1]).Properties![0]);

        // 解构赋值对每个目标调 bindAssignmentTargetFlow（binder.ts:1894-1910, 1809-1816）：数组元素 `p` 先出一条
        // Assignment 变异，对象字面量里的 `q` 是最后一条，两者前后相邻。
        var last = Statement<ExpressionStatement>(file, 1).FlowNode!;
        AssertHasFlag(FlowFlags.Assignment, last.Flags);
        Assert.Same(shorthand.Name, last.Node);

        var previous = Antecedent(last);
        AssertHasFlag(FlowFlags.Assignment, previous.Flags);
        Assert.Same(elements[0], previous.Node);
    }

    [Fact]
    public void BindSourceFile_FlagsStatementsAfterAnAbruptCompletionAsUnreachable()
    {
        var file = ParseAndBind("""
            function f() { return 1; g(); }
            """);
        var body = Assert.IsType<Block>(Statement<FunctionDeclaration>(file, 0).Body);

        // return 之后 currentFlow 变成 unreachableFlow：后面的可执行语句既拿 NodeFlags.Unreachable 又没有 flowNode
        // （binder.ts:1096-1112, 1592-1605）。
        var unreachable = body.Statements![1];
        Assert.Equal(NodeFlags.Unreachable, unreachable.Flags & NodeFlags.Unreachable);
        Assert.Null(((IFlowContainer)unreachable).FlowNode);
    }

    [Fact]
    public void BindSwitchStatement_RecordsPossiblyExhaustiveAndTheFallthroughFlow()
    {
        var exhaustive = ParseAndBind("""
            function f() { switch (k) { case 1: return 1; } }
            """);
        var exhaustiveSwitch = Assert.IsType<SwitchStatement>(Assert.IsType<Block>(Statement<FunctionDeclaration>(exhaustive, 0).Body).Statements![0]);

        // 没有 default 且 case 的出口都不可达 → possiblyExhaustive（binder.ts:1729-1732）。
        Assert.True(exhaustiveSwitch.PossiblyExhaustive);

        var falling = ParseAndBind("""
            function f() { switch (k) { case 1: g(); } }
            """);
        var fallingSwitch = Assert.IsType<SwitchStatement>(Assert.IsType<Block>(Statement<FunctionDeclaration>(falling, 0).Body).Statements![0]);

        // case 正常结束 → postSwitchLabel 有前驱 → 不是 possiblyExhaustive。
        Assert.False(fallingSwitch.PossiblyExhaustive);

        var noFallthrough = ParseAndBind("""
            function f() { switch (k) { case 1: g(); case 2: h(); } }
            """, new FakeCompilerOptions { Target = ScriptTarget.ESNext, NoFallthroughCasesInSwitch = true });
        var clauses = Assert.IsType<SwitchStatement>(Assert.IsType<Block>(Statement<FunctionDeclaration>(noFallthrough, 0).Body).Statements![0]).CaseBlock!.Clauses!;

        // options.noFallthroughCasesInSwitch 为真时，非最后一个子句的可达出口记进 fallthroughFlowNode（binder.ts:1759-1762）。
        Assert.NotNull(Assert.IsType<CaseClause>(clauses[0]).FallthroughFlowNode);
        Assert.Null(Assert.IsType<CaseClause>(clauses[1]).FallthroughFlowNode);
    }

    [Fact]
    public void BindConditionalExpressionFlow_RecordsTheBranchesOnlyInReturnPosition()
    {
        var inReturn = ParseAndBind("""
            function f() { return p ? q : r; }
            """);
        var returned = Assert.IsType<ConditionalExpression>(
            Assert.IsType<ReturnStatement>(Assert.IsType<Block>(Statement<FunctionDeclaration>(inReturn, 0).Body).Statements![0]).Expression);

        // inReturnPosition 为真时两个分支的入口流写回节点（binder.ts:2032-2056）。
        Assert.NotNull(returned.FlowNodeWhenTrue);
        Assert.NotNull(returned.FlowNodeWhenFalse);

        var elsewhere = ParseAndBind("""
            var v = p ? q : r;
            """);
        var conditional = Assert.IsType<ConditionalExpression>(
            Statement<VariableStatement>(elsewhere, 0).DeclarationList!.Declarations![0].Initializer);

        // 不在返回位置时两个槽保持为空。
        Assert.Null(conditional.FlowNodeWhenTrue);
        Assert.Null(conditional.FlowNodeWhenFalse);
    }

    [Fact]
    public void BindOptionalChainFlow_JoinsTheChainExitAndItsNullishBranch()
    {
        var file = ParseAndBind("""
            a?.[i = 0];
            next;
            """);
        var chain = Assert.IsType<ElementAccessExpression>(Statement<ExpressionStatement>(file, 0).Expression);

        // `a?.[i = 0]` 顶层当作逻辑表达式处理：`a` 的假分支接进 postExpressionLabel，链上的赋值又让 hasFlowEffects 为真，
        // 于是 currentFlow 换成 finishFlowLabel(postExpressionLabel)；两条前驱使它保持为标签本身（binder.ts:2171-2207, 2145-2151）。
        var after = Statement<ExpressionStatement>(file, 1).FlowNode!;
        AssertHasFlag(FlowFlags.BranchLabel, after.Flags);

        var falseBranch = Antecedents(after).Find(flow => (flow.Flags & FlowFlags.FalseCondition) != 0);
        Assert.NotNull(falseBranch);
        Assert.Same(chain.Expression, falseBranch.Node);
    }

    [Fact]
    public void BindTryStatement_ReducesTheFinallyLabelForControlFlowPastIt()
    {
        var file = ParseAndBind("""
            function f() { try { g(); } finally { h(); } after(); }
            """);
        var tryStatement = Assert.IsType<TryStatement>(Assert.IsType<Block>(Statement<FunctionDeclaration>(file, 0).Body).Statements![0]);
        var after = Assert.IsType<ExpressionStatement>(Assert.IsType<Block>(Statement<FunctionDeclaration>(file, 0).Body).Statements![1]);

        // finally 之后的控制流是 ReduceLabel 节点，它带着 finally 前标签的替换前驱集（binder.ts:1691-1713）。
        AssertHasFlag(FlowFlags.ReduceLabel, after.FlowNode!.Flags);
        var reduceData = Assert.IsType<FlowReduceLabelData>(after.FlowNode.Node);
        var finallyLabel = Assert.IsType<FlowNode>(reduceData.Target);

        // finally 的入口标签把 try 的正常出口与异常出口串起来（这里没有 return，于是恰好两条前驱）—— 标签形态证明
        // concatenate 走的是合并而不是折叠成单点。
        AssertHasFlag(FlowFlags.BranchLabel, finallyLabel.Flags);
        Assert.Equal(2, Antecedents(finallyLabel).Count);
        Assert.NotNull(tryStatement.FinallyBlock);
        Assert.Single(reduceData.Antecedents!);
    }

    [Fact]
    public void BindLabeledStatement_MarksAnUnreferencedLabelAsUnreachable()
    {
        var file = ParseAndBind("""
            unused: { g(); }
            used: { break used; }
            """);
        var unused = Statement<LabeledStatement>(file, 0);
        var used = Statement<LabeledStatement>(file, 1);

        // 未被引用的标签由 bindLabeledStatement 打上 NodeFlags.Unreachable，被 break 引用的则不打（binder.ts:1790-1807）。
        Assert.Equal(NodeFlags.Unreachable, unused.Label!.Flags & NodeFlags.Unreachable);
        Assert.Equal(NodeFlags.None, used.Label!.Flags & NodeFlags.Unreachable);
    }

    [Fact]
    public void GetCombinedModifierFlags_ReachesTheModifierOnTheCarryingStatement()
    {
        var file = Parser.CreateSourceFile(
            "test.ts",
            "namespace N { export var x = 1; }",
            ScriptTarget.ESNext,
            /*setParentNodes*/ false,
            ScriptKind.TS);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = ScriptTarget.ESNext });

        var module = Statement<ModuleDeclaration>(file, 0);
        var statement = Assert.IsType<VariableStatement>(Assert.IsType<ModuleBlock>(module.Body).Statements![0]);
        var declaration = statement.DeclarationList!.Declarations![0];

        // `export` 落在 VariableStatement 上：单看清 syntax 的读法在声明上是 None，而合并的走法要沿
        // VariableDeclaration → VariableDeclarationList → VariableStatement 把修饰符收上来（utilitiesPublic.ts:644-664）。
        Assert.Equal(ModifierFlags.None, Utilities.GetSyntacticModifierFlags(declaration));
        Assert.Equal(ModifierFlags.Export, Utilities.GetCombinedModifierFlags(declaration));
        Assert.Equal(ModifierFlags.Export, Utilities.GetCombinedModifierFlags(statement));
    }

    [Fact]
    public void SymbolName_UnescapesTheLeadingUnderscoresAndReadsAPrivateElementName()
    {
        var file = ParseAndBind("""
            var __proto = 1;
            class C { #p = 2; }
            """);

        // escapedName 里 `__x` 存成 `___x`，symbolName 交回原样（utilitiesPublic.ts:852, 870-875）。
        Assert.Equal("__proto", Utilities.SymbolName(file.Locals!["___proto"]));

        // 值声明是私有类元素时交回 idText(名字) —— 上游 symbolName 的前半段（utilitiesPublic.ts:871-873）。
        var property = Assert.IsType<PropertyDeclaration>(Statement<ClassDeclaration>(file, 1).Members![0]);
        var privateIdentifier = Assert.IsType<PrivateIdentifier>(property.Name);
        var symbol = new Symbol(SymbolFlags.Property, privateIdentifier.EscapedText!) { ValueDeclaration = property };
        Assert.Equal("#p", Utilities.SymbolName(symbol));
    }

    /// <summary>
    /// 不依赖参考实现就能成立的两条流图不变量（<c>tools/BindDiff</c> 那份差分是对照真编译器跑的，这两条是端口自己
    /// 能在 <c>dotnet test</c> 里守住的）。
    /// </summary>
    private static readonly string[] InvariantSources =
    [
        """
        while (x) { if (y) continue; if (z) break; w(); }
        after;
        """,
        """
        function f(k: number) {
            var hoisted;
            if (k) { return 1; }
            switch (k) { case 1: g(); case 2: break; default: return 2; }
            try { h(); } catch (e) { i(); } finally { j(); }
            lbl: { unused(); }
            used: { break used; }
            return k;
        }
        """,
        """
        a?.[i = 0];
        p ? q : r;
        p || (q && r);
        p ??= q;
        [s, { t }] = u;
        next;
        """,
        """
        class C { static { x = 1; } m() { return this; } }
        enum E { A = 1 }
        namespace N { export var v = 1; }
        """,
        """
        function afterReturn() { return 1; g(); }
        function afterThrow() { throw new Error(); g(); }
        function afterBreak() { while (x) { break; g(); } }
        """,
    ];

    /// <summary>流节点必须带的“变体”位：<c>createFlowNode</c> 的每个调用点都至少带一个（binder.ts:1356-1421）。</summary>
    private const FlowFlags VariantFlags = FlowFlags.Unreachable | FlowFlags.Start | FlowFlags.BranchLabel | FlowFlags.LoopLabel
        | FlowFlags.Assignment | FlowFlags.TrueCondition | FlowFlags.FalseCondition | FlowFlags.SwitchClause
        | FlowFlags.ArrayMutation | FlowFlags.Call | FlowFlags.ReduceLabel;

    private static void WalkNodes(Node node, Action<Node> visit)
    {
        visit(node);
        ForEachChildTable.ForEachChild(node, child => WalkNodes(child, visit));
    }

    private static void WalkFlow(FlowNode? flow, HashSet<FlowNode> visited, Action<FlowNode> visit)
    {
        if (flow is null || !visited.Add(flow))
        {
            return;
        }

        visit(flow);
        switch (flow.Antecedent)
        {
            case null:
                break;
            case FlowNode antecedent:
                WalkFlow(antecedent, visited, visit);
                break;
            case List<FlowNode> antecedents:
                foreach (var item in antecedents)
                {
                    WalkFlow(item, visited, visit);
                }

                break;
            default:
                Assert.Fail($"FlowNode.Antecedent must be null, a FlowNode or a List<FlowNode>, was {flow.Antecedent.GetType().Name}");
                break;
        }
    }

    [Fact]
    public void BinderFlowInvariants_EveryStatementCarriesAFlowNodeExactlyWhenItIsReachable()
    {
        foreach (var source in InvariantSources)
        {
            var file = ParseAndBind(source);
            WalkNodes(file, node =>
            {
                if (node.Kind < SyntaxKind.FirstStatement || node.Kind > SyntaxKind.LastStatement)
                {
                    return;
                }

                // 语句节点必须实现 IFlowContainer —— bindChildren 对范围内每条语句都会写 flowNode（binder.ts:1129-1132），
                // 少了这个能力就是一次 InvalidCastException。
                var container = Assert.IsAssignableFrom<IFlowContainer>(node);
                var unreachable = (node.Flags & NodeFlags.Unreachable) != 0;

                // 不可达 ⇒ 没有 flowNode：bindChildren 在不可达分支里清掉它（binder.ts:1099-1104）。
                if (unreachable)
                {
                    Assert.Null(container.FlowNode);
                }

                // 有可执行语义的语句反过来也成立：拿不到 flowNode 只能是因为它不可达。
                if (Utilities.IsPotentiallyExecutableNode(node))
                {
                    Assert.Equal(unreachable, container.FlowNode is null);
                }
            });
        }
    }

    [Fact]
    public void BinderFlowInvariants_EveryFlowNodeInTheGraphIsAWellFormedVariant()
    {
        foreach (var source in InvariantSources)
        {
            var file = ParseAndBind(source);
            var visited = new HashSet<FlowNode>();
            void Visit(FlowNode flow)
            {
                // 每个流节点都带一个变体位，且前驱只可能是 null / 单节点 / 节点数组。
                Assert.NotEqual((FlowFlags)0, flow.Flags & VariantFlags);

                if ((flow.Flags & FlowFlags.Label) != 0)
                {
                    // 标签的前驱只由 addAntecedent 追加成数组（binder.ts:1373-1378），从不是单个节点。
                    Assert.IsNotType<FlowNode>(flow.Antecedent);
                }

                if ((flow.Flags & (FlowFlags.Assignment | FlowFlags.ArrayMutation | FlowFlags.Call | FlowFlags.Condition)) != 0)
                {
                    // 变异/调用/条件节点一定携带它描述的那个节点（binder.ts:1380-1420）。
                    Assert.NotNull(flow.Node);
                }

                if ((flow.Flags & FlowFlags.SwitchClause) != 0)
                {
                    var data = Assert.IsType<FlowSwitchClauseData>(flow.Node);
                    Assert.NotNull(data.SwitchStatement);
                    Assert.Equal(SyntaxKind.SwitchStatement, data.SwitchStatement!.Kind);
                    Assert.True(data.ClauseStart <= data.ClauseEnd);
                }

                if ((flow.Flags & FlowFlags.ReduceLabel) != 0)
                {
                    var data = Assert.IsType<FlowReduceLabelData>(flow.Node);
                    Assert.IsAssignableFrom<FlowNode>(data.Target);
                    Assert.NotNull(data.Antecedents);
                }
            }

            WalkNodes(file, node =>
            {
                if (node is IFlowContainer { FlowNode: { } flow })
                {
                    WalkFlow(flow, visited, Visit);
                }

                switch (node)
                {
                    case SourceFile { EndFlowNode: { } endFlow }:
                        WalkFlow(endFlow, visited, Visit);
                        break;
                    case SwitchStatement { PossiblyExhaustive: null } switchStatement:
                        // bindSwitchStatement 总会写这个槽（binder.ts:1731），除非该 switch 根本没被走到。
                        Assert.True((switchStatement.Flags & NodeFlags.Unreachable) != 0);
                        break;
                    case ConditionalExpression { FlowNodeWhenTrue: null } conditional:
                        Assert.Null(conditional.FlowNodeWhenFalse);
                        break;
                    case ConditionalExpression { FlowNodeWhenFalse: null } conditional:
                        Assert.Null(conditional.FlowNodeWhenTrue);
                        break;
                }
            });
        }
    }
}
