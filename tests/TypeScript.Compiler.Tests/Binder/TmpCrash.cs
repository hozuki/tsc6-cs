namespace TypeScript.Compiler.Tests;

public class TmpCrash
{
    [Fact]
    public void Bind()
    {
        var text = File.ReadAllText("D:/Source/GitHub/TypeScript/tests/cases/conformance/controlFlow/controlFlowCommaOperator.ts");
        var file = Parser.CreateSourceFile("test.ts", text, ScriptTarget.ESNext, false, ScriptKind.TS);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = ScriptTarget.ESNext });
    }
}
