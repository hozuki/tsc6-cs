namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/transformers/declarations.ts:221-238 (getDeclarationDiagnostics) and :259-311
// (transformDeclarations)
/// <summary>
/// Acceptance for the declaration transformer (M7-B7): the text <c>transformDeclarations</c> produces for a source file
/// is the text the reference's <c>--declaration</c> emit produces for it, byte for byte, and the diagnostics it reports
/// are the reference's.
/// </summary>
/// <remarks>
/// <para>
/// Every expected string below is measured from the reference compiler — <c>node lib/tsc.js --declaration
/// --emitDeclarationOnly --noLib --target esnext --module esnext</c> over the same source text — and never from the
/// port's own output (the oracle rule). The port side runs the reference's own declaration-emit path minus the emitter
/// wrapper: <c>transformDeclarations</c> through <c>transformNodes</c> (the same call <c>emitter.ts:913</c> makes), then
/// the declaration printer with the option set <c>emitter.ts</c>'s declaration arm builds.
/// </para>
/// <para>
/// PORT-NOTE (the print handlers): the production path hands the printer <c>Emitter</c>'s private
/// <c>EmitPrintHandlers</c>, whose only non-default hook is <c>hasGlobalName</c> (it forwards to the resolver). The test
/// cannot reach that private type, so it uses <c>PrintHandlers.Default</c> — the same hooks for a declaration
/// transform, whose factory installs no emit hooks of its own — and its <c>hasGlobalName</c> answering <c>false</c>.
/// That difference is only observable for a generated name that collides with a global, and none of the expected
/// outputs below generates one (<c>_default</c> and <c>_base</c> are the two the transform can create).
/// </para>
/// <para>
/// PORT-NOTE (<c>Program.Emit</c>): the transform and the printer are driven here, not the full emitter entry point.
/// That is a harness choice, not a blocker: <c>Transformer.GetTransformers</c> and
/// <c>Program.GetDeclarationDiagnosticsFromTransform</c> landed with the M7-B/C batches, so <c>Program.Emit</c> reaches
/// this path end to end — what the tests pin, and all they need to pin, is the text the declaration transform plus the
/// declaration printer produce for the option set above, which is the same pair of calls <c>emitter.ts:913</c> and the
/// emitter's declaration arm make. Nothing in this file replaces a production member: the resolver, the host,
/// <c>transformDeclarations</c> and the printer are the production ones.
/// </para>
/// </remarks>
public class DeclarationsTransformTests
{
    /// <summary>The one source file both halves compile, and the option set both halves are handed.</summary>
    /// <remarks>
    /// PORT-NOTE (the option set): <c>Declaration</c> is not decoration. The reference's
    /// <c>getEmitDeclarations</c> gate (<c>checker.ts:48823-48825</c>, <c>checkExportSpecifier</c>) is what makes the
    /// checker mark an <c>export { a, c }</c> list's targets visible — <c>collectLinkedAliases(…, setVisibility: true)</c>
    /// pre-sets the <c>isVisible</c> link the declaration transform then reads, and which is the only thing that makes a
    /// non-exported binding element visible at all (<c>isDeclarationVisible</c>'s <c>BindingElement</c> arm returns
    /// <c>isGlobalSourceFile(parent)</c> for a module, i.e. false). The oracle compiles with <c>--declaration</c>, so a
    /// half without it is not the same compilation: the export list's targets stay unmarked and
    /// <c>transformVariableStatement</c> (declarations.ts:1761) drops the statement the reference emits.
    /// </remarks>
    private static (Program Program, ICompilerHost Host, FakeSystem System, ICompilerOptions Options) CreateProgramOverOneFile(string text)
    {
        var options = new FakeCompilerOptions
        {
            Target = ScriptTarget.ESNext,
            Module = ModuleKind.ESNext,
            NewLine = NewLineKind.LineFeed,
            SuppressOutputPathCheck = true,
            NoLib = true,
            Declaration = true,
        };
        var system = new FakeSystem();
        system.Files["a.ts"] = text;
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ false, system);
        var program = Program.CreateProgram(["a.ts"], options, host);
        return (program, host, system, options);
    }

    /// <summary>Runs the reference's declaration-emit path over the program's one file and answers what it printed.</summary>
    private static string EmitDeclarations(Program program, ICompilerOptions options, out List<IDiagnosticWithLocation> diagnostics)
    {
        var file = program.GetSourceFile("a.ts")!;
        var resolver = program.GetTypeChecker().GetEmitResolver(file, /*cancellationToken*/ null, /*forceDts*/ null);
        var transform = Transformer.TransformNodes(
            resolver,
            program.GetEmitHost(),
            NodeFactory.Factory,
            options,
            new List<SourceFile> { file },
            new List<Func<ITransformerContext, Func<object?, object?>>> { DeclarationsTransform.TransformDeclarations },
            /*allowDtsFiles*/ false);
        diagnostics = transform.Diagnostics!;
        Assert.Single(transform.Transformed!);
        var printer = new Printer(
            new PrinterOptions
            {
                RemoveComments = options.RemoveComments,
                NewLine = options.NewLine,
                NoEmitHelpers = true,
                Module = options.Module,
                Target = options.Target,
                OnlyPrintJsDocStyle = true,
                OmitBraceSourceMapPositions = true,
            },
            PrintHandlers.Default);
        return printer.PrintFile((SourceFile)transform.Transformed![0]);
    }

    /// <summary>
    /// The declarations of a module: an inferred <c>const</c>, a function whose return type is inferred, a class whose
    /// private member keeps its modifier and whose method keeps its inferred return type, an interface and a union
    /// alias.
    /// </summary>
    /// <remarks>
    /// Measured from <c>node lib/tsc.js --declaration --emitDeclarationOnly --noLib --target esnext --module esnext</c>
    /// over exactly the source below; the file ends with the printer's own newline.
    /// </remarks>
    [Fact]
    public void EmitsTheReferencesDeclarationsForAModule()
    {
        const string source =
            "export const x = 1;\n"
            + "export function f(p: number) { return p; }\n"
            + "export class C {\n"
            + "    private p = 1;\n"
            + "    m(a: number) { return a; }\n"
            + "}\n"
            + "export interface I { a: number; }\n"
            + "export type T = I | null;\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "export declare const x = 1;\n"
            + "export declare function f(p: number): number;\n"
            + "export declare class C {\n"
            + "    private p;\n"
            + "    m(a: number): number;\n"
            + "}\n"
            + "export interface I {\n"
            + "    a: number;\n"
            + "}\n"
            + "export type T = I | null;\n",
            emitted);
    }

    /// <summary>
    /// <c>getDeclarationDiagnostics</c> answers the transform's diagnostics for a source file, and <c>null</c> for a
    /// file the emit set does not contain.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: this is the reference's exported entry point (<c>transformers/declarations.ts:221</c>), which
    /// <c>Program.GetDeclarationDiagnosticsFromTransform</c> (<c>Program/Program.Emit.cs:413</c>) must call — that
    /// member is the last named blocker of the declaration path, and until the one-line wiring lands the program-level
    /// member still reports the blocker instead of reaching here. The three steps the reference's entry point takes are
    /// all real: the emit-set filter, the transform list and the diagnostics the transform collected.
    /// </remarks>
    [Fact]
    public void GetDeclarationDiagnosticsAnswersTheTransformsDiagnostics()
    {
        var (program, _, _, _) = CreateProgramOverOneFile("export const x = 1;\n");
        var file = program.GetSourceFile("a.ts")!;
        var resolver = program.GetTypeChecker().GetEmitResolver(file, /*cancellationToken*/ null, /*forceDts*/ null);

        // A source file of the emit set: the transform runs and answers its (empty) diagnostic list.
        Assert.Empty(DeclarationsTransform.GetDeclarationDiagnostics(program.GetEmitHost(), resolver, file)!);

        // A declaration file is not in `getSourceFilesToEmit`'s answer, so the reference answers `undefined` — the
        // port's null — before any transform runs. The program above has no library at all, so this half needs one:
        // the stub the program tests lay down, whose single declaration is the file under test.
        var libOptions = new FakeCompilerOptions { SuppressOutputPathCheck = true };
        var system = new FakeSystem();
        system.Files["/a.ts"] = "const y = 1;\n";
        var host = Program.CreateCompilerHostWorker(libOptions, /*setParentNodes*/ false, system);
        system.Files[host.GetDefaultLibFileName(libOptions)] = "declare var undefined: undefined;\n";
        var libProgram = Program.CreateProgram(["/a.ts"], libOptions, host);
        var declaration = libProgram.GetSourceFiles().First(f => f.IsDeclarationFile);
        Assert.Null(DeclarationsTransform.GetDeclarationDiagnostics(
            libProgram.GetEmitHost(),
            libProgram.GetTypeChecker().GetEmitResolver(declaration, /*cancellationToken*/ null, /*forceDts*/ null),
            declaration));
    }

    /// <summary>Enum members are rewritten to their evaluated constants, and a namespace keeps its nested shape.</summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below. <c>A</c>'s auto-increment value (0), the
    /// computed <c>B + 1</c> (3) and a string-valued const enum member all come from the resolver's
    /// <c>getEnumMemberValue</c>, which is the reference's own path; the <c>declare</c> modifier on the namespace is
    /// <c>ensureModifierFlags</c>'s.
    /// </remarks>
    [Fact]
    public void RewritesEnumMembersToTheirConstants()
    {
        const string source =
            "export namespace N {\n"
            + "    export const q = 1;\n"
            + "    export function g(): void { }\n"
            + "}\n"
            + "export enum E { A, B = 2, C = B + 1 }\n"
            + "export const enum CE { X = \"x\", Y = 1 }\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "export declare namespace N {\n"
            + "    const q = 1;\n"
            + "    function g(): void;\n"
            + "}\n"
            + "export declare enum E {\n"
            + "    A = 0,\n"
            + "    B = 2,\n"
            + "    C = 3\n"
            + "}\n"
            + "export declare const enum CE {\n"
            + "    X = \"x\",\n"
            + "    Y = 1\n"
            + "}\n",
            emitted);
    }

    /// <summary>A local class a declaration refers to is emitted beside it, and the module's export marker is added.</summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below, which reports no declaration diagnostics: the
    /// local class is nameable (the reference writes its own <c>declare class P</c>), and the module gets the
    /// <c>export {};</c> scope marker <c>isExternalModule</c> plus <c>!resultHasExternalModuleIndicator</c> ask for.
    /// </remarks>
    [Fact]
    public void EmitsTheLocalTypeAnExportedDeclarationRefersTo()
    {
        const string source =
            "class P { private p = 1; }\n"
            + "export const leak = new P();\n"
            + "export function bad() { return new P(); }\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "declare class P {\n"
            + "    private p;\n"
            + "}\n"
            + "export declare const leak: P;\n"
            + "export declare function bad(): P;\n"
            + "export {};\n",
            emitted);
    }

    /// <summary>
    /// The M7-B7 slice's one diverging case: a destructuring declaration exported by name. The reference keeps the
    /// statement (flattened to one <c>declare const</c> with a declaration per bound name, each with its inferred
    /// type) because <c>collectLinkedAliases</c> marked the binding elements visible; the port dropped it until the
    /// harness handed the same option set the oracle does (<see cref="CreateProgramOverOneFile"/>'s PORT-NOTE).
    /// </summary>
    /// <remarks>
    /// Measured from <c>node lib/tsc.js --declaration --emitDeclarationOnly --noLib --target esnext --module esnext</c>
    /// over exactly the source below (and re-measured through the reference API for the same option object; the two
    /// agree byte for byte on all seven fixtures here). The nested pattern <c>b: { c }</c> is flattened by
    /// <c>recreateBindingPattern</c> (declarations.ts:1786), so the emitted statement lists <c>a</c> and <c>c</c>.
    /// </remarks>
    [Fact]
    public void EmitsTheDeclarationOfADestructuredExportList()
    {
        const string source =
            "const { a, b: { c } } = { a: 1, b: { c: \"s\" } };\n"
            + "export { a, c };\n"
            + "export default class D { constructor(private n: number) { } }\n"
            + "export const arr: number[] = [1, 2];\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "declare const a: number, c: string;\n"
            + "export { a, c };\n"
            + "export default class D {\n"
            + "    private n;\n"
            + "    constructor(n: number);\n"
            + "}\n"
            + "export declare const arr: number[];\n",
            emitted);
    }

    /// <summary>
    /// An index signature is a <c>isFunctionLike</c> node with no <c>body</c> slot, and the port threw on it.
    /// </summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below. <c>visitDeclarationSubtree</c>'s
    /// overload-implementation elision (declarations.ts:1036) asks <c>isImplementationOfOverload</c> for every
    /// <c>isFunctionLike</c> input, index signatures included; the reference reads <c>.body</c> on the erased
    /// <c>SignatureDeclaration</c> and gets <c>undefined</c>, where the port's cast to
    /// <c>IFunctionLikeDeclarationBase</c> threw (<c>Checker.Check.Source.cs</c>).
    /// </remarks>
    [Fact]
    public void EmitsAnIndexSignatureOfAnExportedClass()
    {
        const string source = "export class C { [key: string]: unknown; }\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "export declare class C {\n"
            + "    [key: string]: unknown;\n"
            + "}\n",
            emitted);
    }

    /// <summary>
    /// A type-parameter reference is one of the entity names <c>isEntityNameVisible</c> answers as accessible, and the
    /// port threw on that answer.
    /// </summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below. <c>visitDeclarationSubtree</c>'s
    /// <c>TypeReference</c> arm runs <c>checkEntityNameVisibility</c> (declarations.ts:823), whose result the reference
    /// hands straight to <c>handleSymbolAccessibilityError</c>: <c>isEntityNameVisible</c>'s first arm returns a
    /// <c>SymbolVisibilityResult</c>-shaped object whose only extra slot (<c>errorModuleName</c>) is optional, so TS's
    /// structural typing accepts it. The port's producer builds the derived <c>SymbolAccessibilityResult</c> (the
    /// treatment <c>hasVisibleDeclarations</c> records), which is what makes the transform's downcast hold.
    /// </remarks>
    [Fact]
    public void EmitsAnInterfaceWhoseMemberIsATypeParameter()
    {
        const string source = "export interface I<T> { a: T; }\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "export interface I<T> {\n"
            + "    a: T;\n"
            + "}\n",
            emitted);
    }

    /// <summary>
    /// An expando function's declaration replaces one statement with two, and the port threw on the replacement list.
    /// </summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below. <c>transformTopLevelDeclaration</c>'s
    /// function arm (declarations.ts:1487-1533) answers <c>[clean, namespaceDecl]</c> for an expando function — a
    /// statement <em>array</em> the late pass reads — so <c>transformAndReplaceLatePaintedStatements</c>' tests over
    /// that value are TS's <c>isArray(result) ? some(result, f) : f(result)</c>. The port's container is an
    /// <c>object?[]</c>, which is not an <c>IList&lt;IStatement&gt;</c> (<c>DeclarationsTransform.Members.cs</c>'s
    /// <c>AnyReplacement</c> reads it through the visitor's own array test instead).
    /// </remarks>
    [Fact]
    public void EmitsTheNamespaceOfAnExpandoFunctionBeneathIt()
    {
        const string source =
            "function f() {}\n"
            + "f.x = 1;\n"
            + "export default f;\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "declare function f(): void;\n"
            + "declare namespace f {\n"
            + "    var x: number;\n"
            + "}\n"
            + "export default f;\n",
            emitted);
    }

    /// <summary>
    /// A computed property name that is not a primitive literal falls back to the inferred type instead of throwing.
    /// </summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below (which reports one <em>checker</em>
    /// diagnostic — the computed name is not a literal — and still emits; the declaration transform adds none, so the
    /// list this test reads is empty). <c>canGetTypeFromObjectLiteral</c>
    /// (expressionToTypeNode.ts:1040) asks <c>isPrimitiveLiteralValue</c> about every computed name; the reference's
    /// <c>assertType&lt;never&gt;(node); return false</c> default is a type-only assertion, so a
    /// <c>"" || 0</c> answers <c>false</c> and the property falls back to the reported inference. Stated as
    /// <c>Debug.AssertNever</c> the arm threw (<c>SyntacticNodeBuilder.Kinds.cs</c>).
    /// </remarks>
    [Fact]
    public void FallsBackForAComputedPropertyNameThatIsNotALiteral()
    {
        const string source = "export const o = { [\"\" || 0]: 0 };\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "export declare const o: {\n"
            + "    0: number;\n"
            + "};\n",
            emitted);
    }

    /// <summary>An emptied module body keeps the shape its braces were written in.</summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below: the namespace's only member is elided, so
    /// the body is empty — and because its braces were on separate lines in the source,
    /// <c>isEmptyBlock</c> (emitter.ts:5228) is false and the body prints as <c>{</c> newline <c>}</c>, not <c>{ }</c>.
    /// The port's module-block call site used an <c>isEmptyBlock</c> without the range test
    /// (<c>Printer.Statements.cs</c>), which printed the single-line form.
    /// </remarks>
    [Fact]
    public void EmitsAnEmptyModuleBodyAcrossItsOwnLines()
    {
        const string source =
            "namespace M {\n"
            + "    const x = 1;\n"
            + "}\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "declare namespace M {\n"
            + "}\n",
            emitted);
    }

    /// <summary>
    /// A numeric name the type printer synthesizes keeps its digits.
    /// </summary>
    /// <remarks>
    /// Measured from the same <c>tsc</c> invocation over the source below. The computed name <c>[0]</c> is a primitive
    /// literal, so the object type's property name comes from the symbol's name through
    /// <c>createPropertyNameNodeForIdentifierOrLiteral</c> (utilities.ts:10946) and a <em>numeric</em> literal node is
    /// built for it; the port's factory took only an <c>int</c> for its <c>string | number</c> slot, so the
    /// <c>double</c> the numeric-name arm passes fell into the string arm and the literal printed as nothing
    /// (<c>, : number;</c> — <c>NodeFactory.CreateNumericLiteral</c>).
    /// </remarks>
    [Fact]
    public void EmitsASynthesizedNumericPropertyName()
    {
        const string source = "export const o = { [0]: 1 };\n";

        var (program, _, _, options) = CreateProgramOverOneFile(source);
        var emitted = EmitDeclarations(program, options, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            "export declare const o: {\n"
            + "    0: number;\n"
            + "};\n",
            emitted);
    }
}
