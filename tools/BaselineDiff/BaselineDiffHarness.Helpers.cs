using System.Text.RegularExpressions;

// The port's own entry-point class is named Program (tools/AstDiff, tools/BindDiff, tools/TypeDiff and tools/EmitDiff
// do the same), which is also the name of the compiler's; the alias keeps the two apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// The case could not be modelled on the harness's own terms — an unknown `// @` directive, a directive whose option
/// has no slot in <see cref="ICompilerOptions"/>, a `tsconfig.json` unit (the port has no config-file reader yet) or a
/// `// @link:` symlink. The driver records one of these per artifact as <c>unmodelled</c> rather than letting a
/// defaulted option or a guessed path read as agreement, and never counts it as a match.
/// </summary>
internal sealed class BaselineModelException(string message) : Exception(message);

/// <summary>One unit of a `// @Filename:`-split test case (<c>TestCaseParser.TestUnitData</c>).</summary>
internal sealed record TestUnitData(
    string Name,
    string Content,
    Dictionary<string, string> FileOptions,
    string OriginalFilePath,
    List<string> References);

/// <summary>The harness's own view of a unit: the name it is compiled under and its text
/// (<c>Compiler.TestFile</c>).</summary>
internal sealed record TestFile(string UnitName, string Content, Dictionary<string, string>? FileOptions = null);

/// <summary>Everything <c>TestCaseParser.makeUnitsFromTest</c> returns for one corpus file.</summary>
internal sealed record BaselineCaseContent(
    Dictionary<string, string> Settings,
    List<TestUnitData> Units,
    string? TsConfigFileName,
    TestUnitData? TsConfigUnit,
    Dictionary<string, string>? Symlinks);

/// <summary>One (file, configuration) pair: the harness runs the file once per varying configuration
/// (<c>CompilerTest</c>), merging the configuration's entries over the file's own settings.</summary>
internal sealed record BaselineCase(
    Dictionary<string, string> Configuration,
    Dictionary<string, string> Settings,
    BaselineCaseContent Content);

/// <summary>The units a case compiles: what the harness passes as root names, what it puts on the file system
/// alongside them, and whether the case has any non-declaration input (which is what decides the `.js` baseline)
/// — <c>CompilerTest</c>'s constructor.</summary>
internal sealed record BaselineCaseUnits(
    List<TestFile> ToBeCompiled,
    List<TestFile> OtherFiles,
    bool HasNonDtsFiles,
    List<TestFile> TsConfigFiles);

/// <summary>One output the emitter wrote, in the order the host accepted it (<c>documents.TextDocument</c>).</summary>
internal sealed record BaselineDocument(string File, string Text);

/// <summary>
/// The harness utilities the baseline writers and the option model share — <c>src/harness/util.ts:8-102</c>,
/// <c>src/harness/harnessUtils.ts:24-28</c> and <c>src/harness/harnessIO.ts:1037-1053</c>, 1571-1588.
/// </summary>
internal static class BaselineDiffHarness
{
    /// <summary>src/harness/util.ts:8-10 — <c>removeTestPathPrefixes</c>. Every path a baseline prints goes through
    /// this, which is why the virtual roots are the harness's own spellings.</summary>
    private static readonly Regex TestPathPrefixRegex = new(@"(?:(file:/{3})|/)\.(?:ts|lib|src)/", RegexOptions.CultureInvariant);

    public static string RemoveTestPathPrefixes(string? text, bool retainTrailingDirectorySeparator = false)
        => TestPathPrefixRegex.Replace(
            text ?? "",
            match => match.Groups[1].Success ? match.Groups[1].Value : retainTrailingDirectorySeparator ? "/" : "");

    /// <summary>src/harness/harnessUtils.ts:24-28 — <c>splitContentByNewlines</c>: splits on <c>\r\n</c> or
    /// <c>\n</c>, never on a lone <c>\r</c>.</summary>
    private static readonly Regex NewlineRegex = new("\r?\n", RegexOptions.CultureInvariant);

    public static string[] SplitContentByNewlines(string content) => NewlineRegex.Split(content);

    /// <summary>src/harness/util.ts:73-93 — <c>getByteOrderMarkLength</c>, over the same four byte sequences the
    /// harness recognizes (the UTF-8 BOM spelled as its code points, because that is what survives a JS string).</summary>
    public static int GetByteOrderMarkLength(string text)
    {
        if (text.Length >= 1)
        {
            var ch0 = text[0];
            if (ch0 == '\uFEFF') return 1;
            if (ch0 == '\u00FE') return text.Length >= 2 && text[1] == '\u00FF' ? 2 : 0;
            if (ch0 == '\u00FF') return text.Length >= 2 && text[1] == '\u00FE' ? 2 : 0;
            if (ch0 == '\u00EF') return text.Length >= 3 && text[1] == '\u00BB' && text[2] == '\u00BF' ? 3 : 0;
        }

        return 0;
    }

    public static string RemoveByteOrderMark(string text)
    {
        var length = GetByteOrderMarkLength(text);
        return length > 0 ? text[length..] : text;
    }

    /// <summary>src/harness/util.ts:100-102 — <c>addUTF8ByteOrderMark</c>. The port's emitter asks its host with
    /// <c>writeByteOrderMark</c> rather than writing one itself, so this is the one place a run's output can gain
    /// one.</summary>
    public static string AddUTF8ByteOrderMark(string text)
        => GetByteOrderMarkLength(text) == 0 ? "\u00EF\u00BB\u00BF" + text : text;

    /// <summary>src/harness/harnessIO.ts:1051-1058 — <c>sanitizeTestFilePath</c>. The harness canonicalizes to lower
    /// case on every host (<c>Utils.canonicalizeForHarness</c>) so a baseline name never depends on the file system's
    /// case sensitivity, and <c>toPath(name, "", canonicalizeForHarness)</c> over these inputs is exactly
    /// "normalize the slashes and lower-case".</summary>
    private static readonly Regex TestFilePathRegex = new("[\\^<>:\"|?*%]", RegexOptions.CultureInvariant);

    public static string SanitizeTestFilePath(string name)
    {
        var replaced = TestFilePathRegex.Replace(name, "_").Replace("../", "__dotdot/");
        var path = Paths.NormalizeSlashes(replaced).ToLowerInvariant();
        return path.StartsWith('/') ? path[1..] : path;
    }

    /// <summary>src/harness/harnessIO.ts:1037-1049 — <c>checkDuplicatedFileName</c>.</summary>
    public static string CheckDuplicatedFileName(string resultName, Dictionary<string, int> dupeCase)
    {
        resultName = SanitizeTestFilePath(resultName);
        if (dupeCase.TryGetValue(resultName, out var count))
        {
            count++;
            dupeCase[resultName] = count;
            resultName = $"{resultName}.dupe{count}";
        }
        else
        {
            dupeCase[resultName] = 0;
        }

        return resultName;
    }

    /// <summary>src/harness/harnessIO.ts:1571-1575 — <c>isDefaultLibraryFile</c>.</summary>
    public static bool IsDefaultLibraryFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var fileName = Paths.GetBaseFileName(Paths.NormalizeSlashes(filePath));
        return fileName.StartsWith("lib.", StringComparison.Ordinal) && Paths.FileExtensionIs(fileName, ".d.ts");
    }

    /// <summary>src/harness/harnessIO.ts:1584-1587 — <c>getConfigNameFromFileName</c>.</summary>
    public static bool IsConfigFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var baseName = Paths.GetBaseFileName(fileName).ToLowerInvariant();
        return baseName is "tsconfig.json" or "jsconfig.json";
    }
}
