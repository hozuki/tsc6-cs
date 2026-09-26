using System.Text;

// The port's own entry-point class is named Program (tools/AstDiff, tools/BindDiff, tools/TypeDiff and tools/EmitDiff
// do the same), which is also the name of the compiler's; the alias keeps the two apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// The `.js` baseline: <c>doJsEmitBaseline</c> (src/harness/harnessIO.ts:910-1000) — the corpus file's own sources,
/// then every file the emitter wrote (the JavaScript and, when the case asks for declarations, the `.d.ts` beside it),
/// then the declaration re-compilation's diagnostics or the <c>noCheck</c> comparison.
/// </summary>
/// <remarks>
/// This is the baseline that carries the declaration output: the reference corpus has no standalone <c>.d.ts</c>
/// baselines (the 912 <c>.d.ts</c> files under <c>tests/baselines</c> all belong to the projects and transpile
/// runners), so a case's declaration emit is compared as part of its `.js` baseline.
/// </remarks>
internal static class BaselineDiffJsBaseline
{
    /// <summary>src/harness/harnessIO.ts:910-1005 — <c>doJsEmitBaseline</c>. <c>null</c> is the harness's "no
    /// content" (nothing was emitted and nothing was printed).</summary>
    public static string? DoJsEmitBaseline(
        string header,
        ICompilerOptions options,
        BaselineCompilation result,
        List<TestFile> tsConfigFiles,
        List<TestFile> toBeCompiled,
        List<TestFile> otherFiles,
        Dictionary<string, string> harnessSettings,
        BaselineDiffContext context)
    {
        var fullEmitPaths = harnessSettings.TryGetValue("fullEmitPaths", out var fullPaths) && fullPaths == "true";
        if (options.NoEmit != true && options.EmitDeclarationOnly != true && result.Js.Count == 0 && result.Diagnostics.Count == 0)
        {
            throw new BaselineModelException("Expected at least one js file to be emitted or at least one error to be created.");
        }

        var tsCode = new StringBuilder();
        var tsSources = otherFiles.Concat(toBeCompiled).ToList();
        tsCode.Append("//// [").Append(header).Append("] ////\r\n\r\n");
        for (var i = 0; i < tsSources.Count; i++)
        {
            tsCode.Append("//// [").Append(Paths.GetBaseFileName(tsSources[i].UnitName)).Append("]\r\n");
            tsCode.Append(tsSources[i].Content);
            if (i < tsSources.Count - 1) tsCode.Append("\r\n");
        }

        var jsCode = new StringBuilder();
        foreach (var file in result.Js.Values)
        {
            // The harness separates consecutive outputs with a blank line unless the previous one already ended in one.
            if (jsCode.Length > 0 && jsCode[^1] != '\n') jsCode.Append("\r\n");
            if (result.Diagnostics.Count == 0 && !Paths.FileExtensionIs(file.File, ".json"))
            {
                var fileParseResult = Parser.CreateSourceFile(
                    file.File,
                    file.Text,
                    Utilities.GetEmitScriptTarget(options),
                    setParentNodes: false,
                    file.File.EndsWith('x') ? ScriptKind.JSX : ScriptKind.JS);
                if (fileParseResult!.ParseDiagnostics!.Count > 0)
                {
                    jsCode.Append(BaselineDiffBaselines.GetErrorBaseline([new TestFile(file.File, file.Text)], [.. fileParseResult.ParseDiagnostics!.Cast<Diagnostic>()], pretty: false));
                    continue;
                }
            }

            jsCode.Append(BaselineDiffBaselines.FileOutput(file, fullEmitPaths));
        }

        if (result.Dts.Count > 0)
        {
            jsCode.Append("\r\n\r\n");
            foreach (var declaration in result.Dts.Values) jsCode.Append(BaselineDiffBaselines.FileOutput(declaration, fullEmitPaths));
        }

        var declFileContext = BaselineDiffHarnessModel.PrepareDeclarationCompilationContext(
            toBeCompiled,
            otherFiles,
            result,
            harnessSettings,
            options,
            null,
            context);
        var declFileCompilationResult = BaselineDiffHarnessModel.CompileDeclarationFiles(declFileContext);

        if (declFileCompilationResult is not null && declFileCompilationResult.Diagnostics.Count > 0)
        {
            jsCode.Append("\r\n\r\n//// [DtsFileErrors]\r\n");
            jsCode.Append("\r\n\r\n");
            jsCode.Append(BaselineDiffBaselines.GetErrorBaseline(
                [.. tsConfigFiles, .. declFileContext!.DeclInputFiles, .. declFileContext!.DeclOtherFiles],
                declFileCompilationResult.Diagnostics,
                pretty: false));
        }
        else if (options.NoCheck != true && options.NoEmit != true)
        {
            var withoutChecking = result.Repeat(new Dictionary<string, string>(StringComparer.Ordinal) { ["noCheck"] = "true" });
            CompareResultFileSets(withoutChecking.Dts, result.Dts);
            CompareResultFileSets(withoutChecking.Js, result.Js);
        }

        return jsCode.Length > 0 ? tsCode + "\r\n\r\n" + jsCode : null;

        void CompareResultFileSets(Dictionary<string, BaselineDocument> a, Dictionary<string, BaselineDocument> b)
        {
            foreach (var (key, document) in a)
            {
                if (!b.TryGetValue(key, out var original))
                {
                    jsCode.Append($"\r\n\r\n!!!! File {BaselineDiffHarness.RemoveTestPathPrefixes(document.File)} missing from original emit, but present in noCheck emit\r\n");
                    jsCode.Append(BaselineDiffBaselines.FileOutput(document, fullEmitPaths));
                }
                else if (original.Text != document.Text)
                {
                    // src/harness/harnessIO.ts:995-999 — the harness prints `diff`'s createTwoFilesPatch here (the
                    // reference's dependency, not the compiler's). Reproducing jsdiff's unified diff is not part of
                    // this ring, so the case is reported instead of being printed without its patch.
                    throw new BaselineModelException(
                        $"'{BaselineDiffHarness.RemoveTestPathPrefixes(document.File)}' differs from the original emit in the noCheck emit; the harness prints diff.createTwoFilesPatch here, which this tool does not implement");
                }
            }
        }
    }
}
