using System.Reflection;

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:1486-2412 (the assembly and the state block it runs)
// ported from: src/compiler/checker.ts:1611-1621 (the counts), 1631-1634 (getTypeOfSymbolAtLocation)
// ported from: src/compiler/checker.ts:1672 (getParameterType = getTypeAtPosition), 1704-1708 (getSymbolAtLocation)
// ported from: src/compiler/checker.ts:38500 (getTypeAtPosition), 44097 (createSignature), 16473 (getReturnTypeOfSignature)
/// <summary>
/// The M6-5 keystone: <c>Checker.CreateTypeChecker</c> assembles a checker whose <see cref="ITypeChecker"/> surface is
/// complete, and the assembled checker answers a declaration check, an expression check and a type/signature
/// construction over a real parsed-and-bound file.
/// </summary>
/// <remarks>
/// <para>
/// These are the milestone's end-to-end proofs. The expectations are the reference's own behaviour, not observations of
/// the port: <c>const x: number = 1</c> types <c>x</c> as the <c>numberType</c> singleton (the keyword type node
/// reaches the intrinsic slot <c>getTypeFromTypeNode</c> keeps), a parameter's type at position 0 is the declared type
/// (<c>getTypeAtPosition</c>, checker.ts:38500), <c>getSignatureFromDeclaration</c> gives <c>minArgumentCount</c> 1 for
/// one required parameter (checker.ts:44097), <c>one + two</c> over two number-typed operands is <c>number</c>
/// (<c>checkBinaryLikeExpressionWorker</c> and the arithmetic widening of <c>+</c>), and <c>getWidenedLiteralType</c> of
/// a number literal type is <c>numberType</c> (checker.ts:41440's widening).
/// </para>
/// <para>
/// PORT-NOTE: a real program is used (so <c>initializeTypeChecker</c> resolves the global types from the stub library);
/// every call goes through <see cref="ITypeChecker"/> because the class's members are explicit interface
/// implementations — that is the surface <c>Program.GetTypeChecker()</c> hands out.
/// </para>
/// </remarks>
public class CheckerCreateTypeCheckerTests
{
    /// <summary>
    /// The generated <see cref="ITypeChecker"/> members whose port cannot answer, each with the reason the class
    /// records. §15.9's "interface bigger than the class" treatment: the difference set is *named*, and this test pins
    /// it by reflection, so a generator fix that makes a member answerable turns this list empty instead of silently
    /// leaving a throwing member behind.
    /// </summary>
    /// <remarks>
    /// Empty since the generator learned to read a function type before the <c>T[]</c> shape: the one entry,
    /// <c>GetIndexInfosOfIndexSymbol</c> (types.ts:5083 as an array of functions), is now generated as the reference's
    /// function and implemented by <see cref="Checker"/>.
    /// </remarks>
    private static readonly (string Member, string Reason)[] MembersThatCannotAnswer = [];

    /// <summary>The smallest library that lets <c>initializeTypeChecker</c> resolve the globals it asks for.</summary>
    private static readonly string[] StubLib =
    [
        "interface Array<T> { }",
        "interface Object { }",
        "interface Function { }",
        "declare var undefined: undefined;",
    ];

    private static (Program Program, ITypeChecker Checker, SourceFile Root) CreateProgramOver(string text)
    {
        // PORT-NOTE: `verifyCompilerOptions`'s emit-path check calls `getEmitHost` (program.ts:4393) and
        // `forEachEmittedFile` (program.ts:4395), both M7's loud blockers (`Program/Program.EmitBoundary.cs`), and the
        // reference reaches that block whenever `!options.noEmit && !options.suppressOutputPathCheck` (program.ts:4392).
        // `suppressOutputPathCheck` is the option that skips exactly that check — the flag the reference's own build
        // mode sets — so the M6 path takes it, exactly as `ProgramTests.CreateProgramOverOneFile` records; nothing else
        // about the file graph, the options the checker reads, or the binding below changes.
        var options = new FakeCompilerOptions { SuppressOutputPathCheck = true };
        var system = new FakeSystem();
        system.Files["/a.ts"] = text;
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ true, system);
        // PORT-NOTE: the host asks for `combinePaths(getDefaultLibLocation(), getDefaultLibFileName(options))`
        // (program.ts:475) and `getDefaultLibLocation()` is `getDirectoryPath(normalizePath(system.getExecutingFilePath()))`
        // (program.ts:467) — with the fake system's empty executing path that is the *relative* library name, so the
        // stub has to be laid down under the host's own answer; a file at `"/" + name` is never asked for, and
        // `initializeTypeChecker` then cannot resolve the globals this stub declares.
        system.Files[host.GetDefaultLibFileName(options)] = string.Join("\n", StubLib) + "\n";
        var program = Program.CreateProgram(["/a.ts"], options, host);
        var root = program.GetSourceFiles().Single(file => (file.FileName ?? "").EndsWith("a.ts", StringComparison.Ordinal));
        return (program, program.GetTypeChecker(), root);
    }

    [Fact]
    public void CreateTypeChecker_ImplementsEveryRequiredMemberOfTheInterface()
    {
        // GetInterfaceMap throws if Checker does not implement ITypeChecker at all; from there, an abstract interface
        // member whose mapped target is declared on the interface itself is one the class left to a default
        // implementation. There must be none: the port implements all of them explicitly.
        var map = typeof(Checker).GetInterfaceMap(typeof(ITypeChecker));

        var unimplemented = new List<string>();
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (!map.InterfaceMethods[i].IsAbstract) continue;
            if (map.TargetMethods[i].DeclaringType == typeof(ITypeChecker))
            {
                unimplemented.Add(map.InterfaceMethods[i].Name);
            }
        }

        Assert.Equal("", string.Join(", ", unimplemented));
    }

    [Fact]
    public void CreateTypeChecker_EveryGeneratedMemberOfTheInterfaceIsAnswerable()
    {
        // The difference set that used to hold GetIndexInfosOfIndexSymbol is empty: the generator emits the
        // reference's function type for it (types.ts:5083), and the class forwards the interface slot to the ported
        // function (checker.ts:16693 through the literal's assignment at checker.ts:1658).
        Assert.Empty(MembersThatCannotAnswer);

        var checker = (ITypeChecker)new Checker(new FakeTypeCheckerHost());
        var indexInfosOfIndexSymbol = checker.GetIndexInfosOfIndexSymbol;
        Assert.NotNull(indexInfosOfIndexSymbol);
        Assert.IsType<Func<ISymbol, List<ISymbol>, List<IIndexInfo>>>(indexInfosOfIndexSymbol);
    }

    [Fact]
    public void CreateTypeChecker_DeclarationCheck_TypesTheAnnotatedVariableAndTheFunctionSignature()
    {
        var (_, checker, sourceFile) = CreateProgramOver("const x: number = 1;\nfunction f(p: string): number { return 1; }\n");

        // checkExpression reaches checkTypeReferenceNode for the annotation, and getTypeFromTypeNode answers the
        // `numberType` slot for the `number` keyword.
        var variableStatement = (VariableStatement)sourceFile.Statements![0];
        var declaration = variableStatement.DeclarationList!.Declarations![0];
        Assert.Same(checker.GetNumberType(), checker.GetTypeAtLocation(declaration.Name));

        // checker.ts:44097 — one required parameter; getParameterType (checker.ts:1672's getTypeAtPosition) answers the
        // declared `stringType`, and getReturnTypeOfSignature answers the `number` return annotation.
        var function = (FunctionDeclaration)sourceFile.Statements[1];
        var signature = Assert.IsAssignableFrom<ISignature>(checker.GetSignatureFromDeclaration(function));
        Assert.Equal(1, signature.MinArgumentCount);
        Assert.Same(checker.GetStringType(), checker.GetParameterType(signature, 0));
        Assert.Same(checker.GetNumberType(), checker.GetReturnTypeOfSignature(signature));
    }

    [Fact]
    public void CreateTypeChecker_ExpressionCheck_TypesTheInitializerAndTheBinarySum()
    {
        var (_, checker, sourceFile) = CreateProgramOver("const one = 1;\nconst two = 2;\nconst sum = one + two;\n");

        // `1` is a number literal type, and the widening of a number literal type is `numberType` (checker.ts:41440).
        var one = (VariableDeclaration)((VariableStatement)sourceFile.Statements![0]).DeclarationList!.Declarations![0];
        var oneType = checker.GetTypeAtLocation(one.Name);
        Assert.NotEqual(0, (int)(oneType.Flags & TypeFlags.NumberLiteral));
        Assert.Same(checker.GetNumberType(), checker.GetWidenedLiteralType(oneType));

        // `one + two` is `number`: `+` over two number-typed operands widens (checkBinaryLikeExpressionWorker).
        var sum = (VariableDeclaration)((VariableStatement)sourceFile.Statements![2]).DeclarationList!.Declarations![0];
        var initializer = (BinaryExpression)sum.Initializer!;
        Assert.Same(checker.GetNumberType(), checker.GetTypeAtLocation(initializer));
        Assert.Same(checker.GetNumberType(), checker.GetTypeAtLocation(sum.Name));
    }

    [Fact]
    public void CreateTypeChecker_ConstructsATypeAndASignatureEndToEnd()
    {
        var (_, checker, _) = CreateProgramOver("export {};\n");

        // createArrayType needs the global Array interface the stub library declares, and getElementTypeOfArrayType
        // reads it back through the type reference's arguments.
        var array = checker.CreateArrayType(checker.GetNumberType());
        Assert.Same(checker.GetNumberType(), checker.GetElementTypeOfArrayType(array));

        // createAnonymousType with no members is an empty object type; getPropertiesOfType reads its table back.
        var members = Utilities.CreateSymbolTable();
        var anonymous = checker.CreateAnonymousType(null, members, [], [], []);
        Assert.Empty(checker.GetPropertiesOfType(anonymous));

        // createSignature with a resolvedReturnType answers getReturnTypeOfSignature without resolving (checker.ts:16473).
        var signature = checker.CreateSignature(
            /*declaration*/ null,
            /*typeParameters*/ null,
            /*thisParameter*/ null,
            /*parameters*/ [],
            /*resolvedReturnType*/ checker.GetStringType(),
            /*typePredicate*/ null,
            /*minArgumentCount*/ 0,
            /*flags*/ SignatureFlags.None);
        Assert.Equal(0, signature.MinArgumentCount);
        Assert.Same(checker.GetStringType(), checker.GetReturnTypeOfSignature(signature));

        // The assembled state answers the counts, and the intrinsic accessors are the same instances the queries use.
        Assert.Equal(0, checker.GetTypeCount() - checker.GetTypeCount());
        // checker.ts:25661-25669 — `getWidenedLiteralType` widens a literal only while it is *fresh*
        // (`isFreshLiteralType`, checker.ts:20301-20303, is `freshType === type`). The canonical
        // `getNumberLiteralType(0)` is its own regular type and never fresh, so the number arm does not fire here and
        // the accessor answers the type unchanged; `getBaseTypeOfLiteralType` (checker.ts:20327-20329) is the accessor
        // that answers `numberType` for it. Verified against `lib/typescript.js`: `getWidenedLiteralType(
        // getNumberLiteralType(0))` is the literal, `getBaseTypeOfLiteralType` of it is the number type.
        var zero = checker.GetNumberLiteralType(0);
        Assert.Same(zero, checker.GetWidenedLiteralType(zero));
        Assert.Same(checker.GetNumberType(), checker.GetBaseTypeOfLiteralType(zero));
        Assert.False(checker.IsLibType(checker.GetNumberType()));
    }
}
