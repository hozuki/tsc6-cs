using System.Globalization;

// The port's own entry-point class is named Program (tools/AstDiff, tools/BindDiff, tools/TypeDiff and tools/EmitDiff
// do the same), which is also the name of the compiler's; the alias keeps the two apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// The harness's baseline text writers: <c>getErrorBaseline</c> (src/harness/harnessIO.ts:494-660) and
/// <c>doJsEmitBaseline</c> (src/harness/harnessIO.ts:910-1000), which together produce the `.errors.txt` and `.js`
/// files of <c>tests/baselines/reference</c>. Each returns <c>null</c> where the harness's generator returns
/// <c>null</c> — the value the baseline machinery compares as the literal <c>&lt;no content&gt;</c>.
/// </summary>
/// <remarks>
/// The produced text must equal the checked-in baseline byte for byte, so every newline here is the harness's
/// <c>harnessNewLine</c> of <c>\r\n</c> and every number is invariant culture. A run's own line terminators never
/// enter: the port's system reports <c>\r\n</c> and the reference's <c>IO.newLine()</c> is the same constant, so the
/// comparison does not depend on the host OS.
/// </remarks>
internal static class BaselineDiffBaselines
{
    /// <summary>src/harness/harnessIO.ts:56 — <c>harnessNewLine</c>.</summary>
    public const string HarnessNewLine = "\r\n";

    /// <summary>The literal the baseline machinery substitutes for a generator's <c>null</c>
    /// (src/harness/harnessIO.ts:1391 — <c>noContent</c>).</summary>
    public const string NoContent = "<no content>";

    /// <summary>src/harness/harnessIO.ts:494-500 — <c>minimalDiagnosticsToString</c>: the harness's own formatting
    /// host, whose canonical-file-name function is the identity.</summary>
    public static string MinimalDiagnosticsToString(List<Diagnostic> diagnostics, bool pretty)
        => (pretty
                ? PortProgram.FormatDiagnosticsWithColorAndContext(diagnostics, new BaselineFormatHost("", HarnessNewLine))
                : PortProgram.FormatDiagnostics(diagnostics, new BaselineFormatHost("", HarnessNewLine)));

    /// <summary>src/harness/harnessIO.ts:659-661 — <c>doErrorBaseline</c>: no diagnostics means no baseline at all,
    /// which the baseline machinery compares as <c>&lt;no content&gt;</c> against a file that does not exist.</summary>
    public static string? DoErrorBaseline(List<TestFile> inputFiles, List<Diagnostic> diagnostics, bool pretty)
        => diagnostics.Count == 0 ? null : GetErrorBaseline(inputFiles, diagnostics, pretty);

    /// <summary>src/harness/harnessIO.ts:507-530 — <c>getErrorBaseline</c>.</summary>
    public static string GetErrorBaseline(List<TestFile> inputFiles, List<Diagnostic> diagnostics, bool pretty)
    {
        var output = new System.Text.StringBuilder();
        foreach (var (_, content, _) in IterateErrorBaseline(inputFiles, diagnostics, pretty, null, false))
        {
            output.Append(content);
        }

        if (pretty)
        {
            // src/harness/harnessIO.ts:520-522 — the pretty form appends the error summary, whose three helpers
            // (getErrorSummaryText/getErrorCountForSummary/getFilesInErrorForSummary) the port has not taken.
            throw new BaselineModelException("`@pretty: true` needs ts.getErrorSummaryText/getErrorCountForSummary/getFilesInErrorForSummary, which the port has not ported");
        }

        return output.ToString();
    }

    /// <summary>
    /// src/harness/harnessIO.ts:524-660 — <c>iterateErrorBaseline</c>: the formatted diagnostics, the global errors,
    /// then each input file's lines with their squiggles and the errors that end on them.
    /// </summary>
    public static IEnumerable<(string Name, string Content, int Count)> IterateErrorBaseline(
        List<TestFile> inputFiles,
        List<Diagnostic> diagnostics,
        bool pretty,
        string? currentDirectory,
        bool caseSensitive)
    {
        diagnostics = [.. diagnostics.OrderBy(d => d, DiagnosticComparer.Instance)];
        var outputLines = "";
        var totalErrorsReportedInNonLibraryNonTsconfigFiles = 0;
        var errorsReported = 0;
        var firstLine = true;

        string NewLine()
        {
            if (firstLine)
            {
                firstLine = false;
                return "";
            }

            return HarnessNewLine;
        }

        var formatHost = new BaselineFormatHost(currentDirectory ?? "", HarnessNewLine, caseSensitive);

        void OutputErrorText(Diagnostic error)
        {
            var message = PortProgram.FlattenDiagnosticMessageText(error.MessageText, HarnessNewLine);
            var errLines = BaselineDiffHarness.RemoveTestPathPrefixes(message)
                .Split('\n')
                .Select(s => s.Length > 0 && s[^1] == '\r' ? s[..^1] : s)
                .Where(s => s.Length > 0)
                .Select(s => "!!! " + Utilities.DiagnosticCategoryName(error) + " TS" + error.Code + ": " + s)
                .ToList();
            if (error.RelatedInformation is { } relatedInformation)
            {
                foreach (var info in relatedInformation)
                {
                    var location = info.File is not null ? " " + PortProgram.FormatLocation(info.File, info.Start ?? 0, formatHost, (value, _) => value) : "";
                    location = BaselineDiffHarness.RemoveTestPathPrefixes(location);
                    if (location.Length != 0 && info.File is not null && BaselineDiffHarness.IsDefaultLibraryFile(info.File.FileName))
                    {
                        location = System.Text.RegularExpressions.Regex.Replace(location, @"(lib.*\.d\.ts):\d+:\d+", "$1:--:--", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }

                    errLines.Add($"!!! related TS{info.Code}{location}: {PortProgram.FlattenDiagnosticMessageText(info.MessageText, HarnessNewLine)}");
                }
            }

            foreach (var line in errLines) outputLines += NewLine() + line;
            errorsReported++;
            if (error.File is null || (!BaselineDiffHarness.IsDefaultLibraryFile(error.File.FileName) && !BaselineDiffHarness.IsConfigFileName(error.File.FileName)))
            {
                totalErrorsReportedInNonLibraryNonTsconfigFiles++;
            }
        }

        var topDiagnostics = BaselineDiffHarness.RemoveTestPathPrefixes(MinimalDiagnosticsToString(diagnostics, pretty));
        topDiagnostics = System.Text.RegularExpressions.Regex.Replace(
            topDiagnostics,
            @"^(lib.*\.d\.ts)\(\d+,\d+\)",
            "$1(--,--)",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        yield return ("__diagnosticSummary", topDiagnostics + HarnessNewLine + HarnessNewLine, diagnostics.Count);

        foreach (var error in diagnostics.Where(error => error.File is null)) OutputErrorText(error);
        yield return ("__globalErrors", outputLines, errorsReported);
        outputLines = "";
        errorsReported = 0;

        var dupeCase = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var inputFile in inputFiles.Where(file => file.Content is not null))
        {
            var fileErrors = diagnostics.Where(error => error.File is not null
                && Paths.ComparePaths(
                    BaselineDiffHarness.RemoveTestPathPrefixes(error.File.FileName),
                    BaselineDiffHarness.RemoveTestPathPrefixes(inputFile.UnitName),
                    currentDirectory ?? "",
                    !caseSensitive) == Comparison.EqualTo).ToList();

            outputLines += NewLine() + "==== " + BaselineDiffHarness.RemoveTestPathPrefixes(inputFile.UnitName) + " (" + fileErrors.Count + " errors) ====";

            var markedErrorCount = 0;
            var lineStarts = ScannerUtilities.ComputeLineStarts(inputFile.Content);
            var lines = inputFile.Content.Split('\n');
            if (lines.Length == 1) lines = lines[0].Split('\r');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
                var thisLineStart = lineStarts[lineIndex];
                var nextLineStart = lineIndex == lines.Length - 1 ? inputFile.Content.Length : lineStarts[lineIndex + 1];
                outputLines += NewLine() + "    " + line;
                foreach (var error in fileErrors)
                {
                    var start = error.Start ?? 0;
                    var end = start + (error.Length ?? 0);
                    if (end < thisLineStart || (start >= nextLineStart && lineIndex != lines.Length - 1)) continue;
                    var relativeOffset = start - thisLineStart;
                    var length = (end - start) - Math.Max(0, thisLineStart - start);
                    var squiggleStart = Math.Max(0, relativeOffset);
                    // `line.substr(0, squiggleStart).replace(/\S/g, " ")` + `new Array(min(len, line.length - squiggleStart) + 1).join("~")`.
                    // The count is clamped at zero: the reference's `new Array(n + 1)` would throw on a negative length,
                    // which the corpus never reaches.
                    var prefixLength = Math.Min(squiggleStart, line.Length);
                    var tildeCount = Math.Max(0, Math.Min(length, line.Length - prefixLength));
                    outputLines += NewLine() + "    " + ReplaceNonSpace(line[..prefixLength], ' ') + new string('~', tildeCount);
                    if (lineIndex == lines.Length - 1 || nextLineStart > end)
                    {
                        OutputErrorText(error);
                        markedErrorCount++;
                    }
                }
            }

            if (markedErrorCount != fileErrors.Count)
            {
                throw new BaselineModelException($"count of errors in {inputFile.UnitName}: marked {markedErrorCount}, expected {fileErrors.Count}");
            }

            yield return (BaselineDiffHarness.CheckDuplicatedFileName(inputFile.UnitName, dupeCase), outputLines, errorsReported);
            outputLines = "";
            errorsReported = 0;
        }

        var numLibraryDiagnostics = diagnostics.Count(diagnostic => diagnostic.File is not null && BaselineDiffHarness.IsDefaultLibraryFile(diagnostic.File.FileName));
        var numTsconfigDiagnostics = diagnostics.Count(diagnostic => diagnostic.File is not null && BaselineDiffHarness.IsConfigFileName(diagnostic.File.FileName));
        if (totalErrorsReportedInNonLibraryNonTsconfigFiles + numLibraryDiagnostics + numTsconfigDiagnostics != diagnostics.Count)
        {
            throw new BaselineModelException(
                $"total number of errors: reported {totalErrorsReportedInNonLibraryNonTsconfigFiles + numLibraryDiagnostics + numTsconfigDiagnostics}, diagnostics {diagnostics.Count}");
        }
    }

    /// <summary>The reference's <c>line.substr(0, squiggleStart).replace(/\S/g, " ")</c>.</summary>
    private static string ReplaceNonSpace(string text, char replacement)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsWhiteSpace(chars[i])) chars[i] = replacement;
        }

        return new string(chars);
    }

    /// <summary>src/harness/harnessIO.ts:1000-1003 — <c>fileOutput</c>: the emitted file's header and its text, with
    /// the full path only when the test asked for it.</summary>
    public static string FileOutput(BaselineDocument file, bool fullEmitPaths)
    {
        var fileName = fullEmitPaths ? BaselineDiffHarness.RemoveTestPathPrefixes(file.File) : Paths.GetBaseFileName(file.File);
        return "//// [" + fileName + "]\r\n" + file.Text;
    }
}

/// <summary>src/harness/harnessIO.ts:534-538 — the formatting host <c>iterateErrorBaseline</c> builds. Its canonical
/// file name is the identity for the summary host and the case-insensitivity setting for the location host.</summary>
internal sealed class BaselineFormatHost(string currentDirectory, string newLine, bool caseSensitive = true) : IFormatDiagnosticsHost
{
    public string GetCurrentDirectory() => currentDirectory;

    public string GetCanonicalFileName(string? fileName) => caseSensitive || fileName is null ? fileName ?? "" : fileName.ToLowerInvariant();

    public string GetNewLine() => newLine;
}

/// <summary><c>ts.compareDiagnostics</c> as a comparer (utilities.ts:8780-8810), so the sorted list is the same
/// stable order the reference's <c>toSorted</c> produces.</summary>
internal sealed class DiagnosticComparer : IComparer<Diagnostic>
{
    public static readonly DiagnosticComparer Instance = new();

    public int Compare(Diagnostic? x, Diagnostic? y) => (int)Utilities.CompareDiagnostics(x!, y!);
}
