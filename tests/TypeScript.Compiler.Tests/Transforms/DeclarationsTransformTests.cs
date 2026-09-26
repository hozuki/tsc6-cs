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
/// PORT-NOTE (<c>Program.Emit</c>): the full emitter entry point still needs <c>Transformer.GetTransformers</c> (the
/// module-transform batch's list, M7-B1), which is why the transform and the printer are driven here rather than through
/// <c>Program.Emit</c>. Everything this file exercises — the resolver, the host, <c>transformDeclarations</c>, the
/// declaration printer — is the production path.
/// </para>
/// </remarks>
public class DeclarationsTransformTests
{
    /// <summary>The one source file both halves compile, and the option set both halves are handed.</summary>
    private static (Program Program, ICompilerHost Host, FakeSystem System, ICompilerOptions Options) CreateProgramOverOneFile(string text)
    {
        var options = new FakeCompilerOptions
        {
            Target = ScriptTarget.ESNext,
            Module = ModuleKind.ESNext,
            NewLine = NewLineKind.LineFeed,
            SuppressOutputPathCheck = true,
            NoLib = true,
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
}
