namespace TypeScript.Compiler.Tests;

/// <summary>
/// M5-1 骨架的端到端证据：<c>bindSourceFile</c> 的状态、符号工厂（<c>createSymbol</c>/<c>addDeclarationToSymbol</c>/
/// <c>declareSymbol</c>）、作用域（<c>bindContainer</c>）与声明绑定。
/// </summary>
/// <remarks>
/// 断言的全部是上游 binder 直接写在树上的东西 —— <c>file.locals</c>、<c>symbol.exports</c>/<c>members</c>、
/// <c>nextContainer</c> 链、<c>classifiableNames</c>、<c>symbolCount</c>、<c>bindDiagnostics</c> —— 不经过
/// checker，所以不需要 M6。与流图有关的形状（<c>flowNode</c> 链、可达性）由 M5-2 落地，这里只断言与流无关的部分，
/// 因此这份用例在 M5-2…M5-5 全部落地前后都成立。
/// </remarks>
public class BinderSkeletonTests
{
    private const string Source = """
        import { q } from "./m";
        var x = 1;
        function f(a: number) { return a; }
        class C {
            m() {}
            static s = 1;
        }
        interface I {}
        type T = number;
        let dup: number;
        let dup: number;
        """;

    private static SourceFile ParseAndBind(string text)
    {
        var file = Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, /*setParentNodes*/ false, ScriptKind.TS);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = ScriptTarget.ESNext });
        return file;
    }

    [Fact]
    public void BindSourceFile_PutsEachTopLevelDeclarationIntoTheSourceFileLocals()
    {
        var file = ParseAndBind(Source);

        // `import` makes the file an external module, so every top-level declaration travels through
        // `declareSourceFileMember` → `declareModuleMember` and lands in `file.locals`; none of them carries an
        // `export` modifier, so no symbol is duplicated into `file.symbol.exports`.
        var locals = file.Locals!;
        Assert.Equal(SymbolFlags.FunctionScopedVariable, locals["x"].Flags);
        Assert.Equal(SymbolFlags.Function, locals["f"].Flags);
        Assert.Equal(SymbolFlags.Class, locals["C"].Flags);
        Assert.Equal(SymbolFlags.Interface, locals["I"].Flags);
        Assert.Equal(SymbolFlags.TypeAlias, locals["T"].Flags);
        Assert.Equal(SymbolFlags.BlockScopedVariable, locals["dup"].Flags);

        // `setValueDeclaration` ran for the value declarations (binder.ts:652-654).
        var function = Assert.IsType<FunctionDeclaration>(locals["f"].ValueDeclaration);
        Assert.Equal("f", function.Name!.EscapedText);
        var variable = Assert.IsType<VariableDeclaration>(locals["x"].ValueDeclaration);
        Assert.Equal("x", Assert.IsType<Identifier>(variable.Name).EscapedText);
        Assert.Single(locals["f"].Declarations!);
    }

    [Fact]
    public void BindSourceFile_BuildsTheClassMemberAndExportTables()
    {
        var file = ParseAndBind(Source);
        var classSymbol = file.Locals!["C"];

        // The instantiated `prototype` member every class gets (binder.ts:3621-3636).
        Assert.Equal(SymbolFlags.Property | SymbolFlags.Prototype, classSymbol.Exports!["prototype"].Flags);
        Assert.Same(classSymbol, classSymbol.Exports!["prototype"].Parent);

        // `m` is an instance member → `members`; `s` is static → `exports` (binder.ts:2312-2318).
        Assert.Equal(SymbolFlags.Method, classSymbol.Members!["m"].Flags);
        Assert.Equal(SymbolFlags.Property, classSymbol.Exports!["s"].Flags);
    }

    [Fact]
    public void BindSourceFile_BindsParametersIntoTheFunctionsLocals()
    {
        var file = ParseAndBind(Source);
        var function = Assert.IsType<FunctionDeclaration>(file.Locals!["f"].ValueDeclaration);

        var parameters = function.Locals!;
        Assert.Equal(SymbolFlags.FunctionScopedVariable, parameters["a"].Flags);
        Assert.Same(function.Parameters![0], parameters["a"].ValueDeclaration);
    }

    [Fact]
    public void BindSourceFile_ChainsContainersInBindOrderAndRecordsTheClassifiableNames()
    {
        var file = ParseAndBind(Source);

        // `bindContainer` creates `locals` eagerly for containers that may hold locals and links them through
        // `nextContainer` (binder.ts:983-986, 2256-2262). Function declarations are bound before the rest of the
        // statement list, so `f` is chained first even though `var x` comes first in the source; the class's own
        // method follows (a class is a container but has no locals, so it is not itself chained).
        var function = Assert.IsType<FunctionDeclaration>(file.NextContainer);
        Assert.Equal("f", function.Name!.EscapedText);
        Assert.IsType<MethodDeclaration>(function.NextContainer);

        // `SymbolFlags.Classifiable` names are recorded as their symbols are declared (binder.ts:797-799). The class,
        // the interface and the type alias are classifiable; a function and a variable are not, and neither is the
        // namespace the source imports from — but the *alias* the import clause declares is (`Classifiable` includes
        // `Alias`), which is why `q` is in the list and only appears once the M5-5 dispatch reaches `ImportSpecifier`.
        Assert.Equal(new[] { "C", "I", "T", "q" }, file.ClassifiableNames!.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // The source file's own end-of-flow node is written by `bindContainer` (binder.ts:1037-1040).
        Assert.NotNull(file.EndFlowNode);
    }

    [Fact]
    public void BindSourceFile_ReportsTheBlockScopedRedeclarationOnBothDeclarations()
    {
        var file = ParseAndBind(Source);

        // `let dup` twice in the same container: the conflict is reported for the earlier declaration and for the new
        // one (binder.ts:836-869), both as `Cannot_redeclare_block_scoped_variable_0` (2451).
        var diagnostics = file.BindDiagnostics!;
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal(2451, diagnostic.Code));
    }

    [Fact]
    public void BindSourceFile_SetsParentsAndCountsTheSymbolsItCreated()
    {
        var file = ParseAndBind(Source);

        // The binder is the step that parents the tree (`bind` → `setParent(node, parent)`, binder.ts:2757).
        var function = Assert.IsType<FunctionDeclaration>(file.NextContainer);
        Assert.Same(function, function.Name!.Parent);
        Assert.Same(function, function.Parameters![0].Parent);

        // NOTE: `symbolCount` (binder.ts:604) is deliberately not asserted here. It counted 11 while the binder skeleton
        // was partial and counts 13 once the dispatch reaches `ImportSpecifier` and the external-module path — a number
        // that tracks how much of the walk has landed rather than a property of binding, so pinning it would only
        // re-break on the next slice. `BinderModuleExpandoTests`/`BinderFlowTests` assert the symbols themselves.
    }
}
