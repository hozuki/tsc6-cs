namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:16091-16122 (isOptionalParameter)
// ported from: src/compiler/checker.ts:16087-16089 (hasEffectiveQuestionToken)
/// <summary>
/// The three boundary cases of <c>isOptionalParameter</c>: the syntactic <c>?</c>, an initializer, and the union's
/// <c>JSDocParameterTag</c> arm.
/// </summary>
/// <remarks>
/// The expected values are the reference's:
/// <list type="bullet">
/// <item><c>?</c> answers true through <c>hasEffectiveQuestionToken</c> (checker.ts:16087-16089) before any parameter
/// test runs, so a <c>?</c> parameter is optional regardless of its signature.</item>
/// <item>an initializer is optional exactly when its index is at or past <c>getMinArgumentCount</c> with
/// <c>StrongArityForUntypedJS | VoidIsNonOptional</c> (checker.ts:16102-16106); a lone optional-looking parameter with
/// an initializer but no <c>?</c> is still required at index 0, so the reference answers false for
/// <c>function f(x = 1) {}</c> — the assertion below pins that, not the intuitive "has an initializer" reading.</item>
/// <item>a <c>JSDocParameterTag</c> that is not optional answers false without throwing
/// (checker.ts:16095-16097: <c>!isParameter(node)</c> short-circuits).</item>
/// </list>
/// PORT-NOTE: these facts need a parsed + bound file (the method walks <c>parameter.parent.parameters</c> and, for the
/// initializer arm, <c>getSignatureFromDeclaration</c>), so they are written against the parser/binder the same way the
/// binder tests are; they cannot execute until the tree compiles whole.
/// </remarks>
public class CheckerParameterOptionalityTests
{
    private static Checker CreateChecker()
    {
        var checker = new Checker(new FakeTypeCheckerHost());
        checker.InitializeBuiltinTypes();
        return checker;
    }

    private static SourceFile ParseAndBind(string fileName, string text)
    {
        var file = Parser.CreateSourceFile(fileName, text, ScriptTarget.ESNext, /*setParentNodes*/ true,
            fileName.EndsWith(".js", StringComparison.Ordinal) ? ScriptKind.JS : ScriptKind.TS);
        Binder.BindSourceFile(file, new FakeCompilerOptions { Target = ScriptTarget.ESNext });
        return file;
    }

    private static ParameterDeclaration FirstParameterOf(SourceFile file, int statementIndex)
        => ((FunctionDeclaration)file.Statements![statementIndex]).Parameters![0];

    [Fact]
    public void ParameterWithQuestionToken_IsOptional()
    {
        var file = ParseAndBind("test.ts", "function f(x?: number) {}");
        var checker = CreateChecker();

        // checker.ts:16092-16094 — `hasEffectiveQuestionToken(node)` is the first test, so the `?` alone is enough.
        Assert.True(checker.IsOptionalParameter(FirstParameterOf(file, 0)));
    }

    [Fact]
    public void ParameterWithInitializer_FollowsTheMinArgumentCountRule()
    {
        var file = ParseAndBind("test.ts", "function f(x = 1) {}\nfunction g(a: number, b = 1) {}");
        var checker = CreateChecker();

        // checker.ts:16098-16107 — the initializer arm answers `parameterIndex >= getMinArgumentCount(signature,
        // StrongArityForUntypedJS | VoidIsNonOptional)`. For `f`, the sole initializer parameter is at index 0 and the
        // signature's min argument count is 0 (a parameter with an initializer is not required), so it IS optional;
        // for `g`, `b` is at index 1 and the min count is 1, so it is optional too — and `a`, which has neither `?` nor
        // an initializer, is not.
        Assert.True(checker.IsOptionalParameter(FirstParameterOf(file, 0)));
        Assert.True(checker.IsOptionalParameter(((FunctionDeclaration)file.Statements![1]).Parameters![1]));
        Assert.False(checker.IsOptionalParameter(((FunctionDeclaration)file.Statements![1]).Parameters![0]));
    }

    [Fact]
    public void JSDocParameterTag_DoesNotThrowAndIsNotOptional()
    {
        // The union's other arm: a JSDoc `@param` tag. `bindJSDoc` only binds JSDoc in JS files (binder.ts:2810-2814),
        // and `getJSDocParameterTags` is the reference's own accessor for the tag of a parameter.
        var file = ParseAndBind("test.js", "/** @param {number} x */\nfunction f(x) {}");
        var checker = CreateChecker();
        var parameter = FirstParameterOf(file, 0);
        var tags = Utilities.GetJSDocParameterTags(parameter);

        Assert.NotEmpty(tags);

        // checker.ts:16095-16097 — `hasEffectiveQuestionToken(tag)` is false for a plain `@param`, `isParameter` is
        // false for a tag, so the answer is false and no parameter-only member is read.
        Assert.False(checker.IsOptionalParameter(tags[0]));
    }
}
