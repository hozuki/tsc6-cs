using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

// The port's own entry-point class is named Program (tools/AstDiff, tools/BindDiff, tools/TypeDiff and tools/EmitDiff
// do the same), which is also the name of the compiler's; the alias keeps the two apart.
using PortProgram = TypeScript.Compiler.Program;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// The harness's case model: the `// @` directive parser, the file splitter, the option model and the compilation
/// wrapper — <c>TestCaseParser</c> (src/harness/harnessIO.ts:1205-1390), <c>Compiler.compileFiles</c>
/// (src/harness/harnessIO.ts:333-380), <c>CompilerTest</c> (src/testRunner/compilerRunner.ts:129-350) and
/// <c>compilerImpl.compileFiles</c> (src/harness/compilerImpl.ts:265-300).
/// </summary>
/// <remarks>
/// <para>
/// The option model is the reference's own, read out of <c>lib/typescript.js</c>'s <c>optionDeclarations</c> into
/// <see cref="BaselineDiffOptions.Declarations"/> rather than restated by hand: <c>optionValue</c>
/// (harnessIO.ts:302-325) decides a directive's value from the option's declared type, and <c>list</c>/custom-type
/// options go through the reference's own <c>parseListTypeOption</c>/<c>parseCustomTypeOption</c>
/// (commandLineParser.ts:1879-1911). A directive the table does not know, or one whose option has no slot in
/// <see cref="ICompilerOptions"/>, is a <see cref="BaselineModelException"/> — reported, never defaulted.
/// </para>
/// <para>
/// PORT-NOTE (<c>tsconfig.json</c>): the harness hands <c>parseJsonSourceFileConfigFileContent</c> a config unit's
/// text and compiles the file names it finds; the port has no config-file reader (M8), so a case that declares one is
/// reported as unmodelled instead of being compiled with the config's own defaults guessed.
/// </para>
/// <para>
/// PORT-NOTE (<c>// @link:</c>): the harness applies the declared symlinks with <c>fs.apply(symlinks)</c>; the corpus
/// declares none, so a case that does is reported rather than half-modelled.
/// </para>
/// </remarks>
internal static partial class BaselineDiffHarnessModel
{
    /// <summary>src/harness/harnessIO.ts:1219 — <c>optionRegex</c>. <c>\w</c> is spelled out as
    /// <c>[A-Za-z0-9_]</c>: a .NET <c>\w</c> also matches Unicode marks, which would make a directive name that JS
    /// does not see into one.</summary>
    private static readonly Regex OptionRegex = new(@"^//\s*@([A-Za-z0-9_]+)\s*:\s*([^\r\n]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>src/harness/harnessIO.ts:1220 — <c>linkRegex</c>.</summary>
    private static readonly Regex LinkRegex = new(@"^//\s*@link\s*:\s*([^\r\n]*)\s*->\s*([^\r\n]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------------------------------------
    // TestCaseParser.
    // ---------------------------------------------------------------------------------------------

    /// <summary>src/harness/harnessIO.ts:1232-1241 — <c>extractCompilerSettings</c>: every `// @Name: value` line,
    /// the value trimmed, the name kept as written (the option lookup is case-insensitive, but the
    /// <c>varyBy</c> check is not).</summary>
    public static Dictionary<string, string> ExtractCompilerSettings(string content)
    {
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in OptionRegex.Matches(content))
        {
            opts[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }

        return opts;
    }

    /// <summary>src/harness/harnessIO.ts:1252-1386 — <c>makeUnitsFromTest</c>. The config-file branch stops at the
    /// unit's name: the reference parses its text with <c>parseJsonSourceFileConfigFileContent</c>, which the port has
    /// not taken, so the name is recorded and the caller reports the case.</summary>
    public static BaselineCaseContent MakeUnitsFromTest(string code, string fileName, Dictionary<string, string>? settings = null)
    {
        settings ??= ExtractCompilerSettings(code);
        var units = new List<TestUnitData>();
        var symlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        var currentFileContent = (string?)null;
        var currentFileOptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var currentFileName = (string?)null;
        var references = new List<string>();

        foreach (var line in BaselineDiffHarness.SplitContentByNewlines(code))
        {
            var linkMatch = LinkRegex.Match(line);
            if (linkMatch.Success)
            {
                // src/harness/harnessIO.ts:1222-1230 — `symlinks[target] = link`.
                symlinks[Paths.GetNormalizedAbsolutePath(linkMatch.Groups[2].Value.Trim(), BaselineDiffVirtualFileSystem.SrcFolder)]
                    = Paths.GetNormalizedAbsolutePath(linkMatch.Groups[1].Value.Trim(), BaselineDiffVirtualFileSystem.SrcFolder);
                continue;
            }

            var match = OptionRegex.Match(line);
            if (match.Success)
            {
                var metaDataName = match.Groups[1].Value.ToLowerInvariant();
                currentFileOptions[match.Groups[1].Value] = match.Groups[2].Value.Trim();
                if (metaDataName != "filename") continue;

                if (currentFileName is not null)
                {
                    units.Add(new TestUnitData(currentFileName, currentFileContent!, currentFileOptions, fileName, references));
                    currentFileContent = null;
                    currentFileOptions = new Dictionary<string, string>(StringComparer.Ordinal);
                    currentFileName = match.Groups[2].Value.Trim();
                    references = [];
                }
                else
                {
                    currentFileName = match.Groups[2].Value.Trim();
                    if (currentFileContent is not null
                        && ScannerUtilities.SkipTrivia(currentFileContent, 0, stopAfterLineBreak: false, stopAtComments: false) != currentFileContent.Length)
                    {
                        throw new BaselineModelException("Non-comment test content appears before the first '// @Filename' directive");
                    }

                    currentFileContent = "";
                }
            }
            else
            {
                if (currentFileContent is null) currentFileContent = "";
                else if (currentFileContent.Length != 0) currentFileContent += "\n";
                currentFileContent += line;
            }
        }

        currentFileName = units.Count > 0 || currentFileName is not null ? currentFileName : Paths.GetBaseFileName(fileName);
        units.Add(new TestUnitData(currentFileName!, currentFileContent ?? "", currentFileOptions, fileName, references));

        string? tsConfigFileName = null;
        TestUnitData? tsConfigUnit = null;
        for (var i = 0; i < units.Count; i++)
        {
            if (!BaselineDiffHarness.IsConfigFileName(units[i].Name)) continue;
            tsConfigFileName = Paths.GetNormalizedAbsolutePath(units[i].Name, BaselineDiffVirtualFileSystem.SrcFolder);
            tsConfigUnit = units[i];
            units.RemoveAt(i);
            break;
        }

        return new BaselineCaseContent(settings, units, tsConfigFileName, tsConfigUnit, symlinks.Count == 0 ? null : symlinks);
    }

    // ---------------------------------------------------------------------------------------------
    // The option model (src/harness/harnessIO.ts:255-420).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The option slots, taken from the concrete <see cref="BaselineDiffCompilerOptions"/> rather than from
    /// <see cref="ICompilerOptions"/>: an interface's property carries no setter, so <c>SetValue</c> on it throws, and
    /// the harness writes every setting into the object it then hands the program.</summary>
    private static readonly PropertyInfo[] Slots = typeof(BaselineDiffCompilerOptions).GetProperties();

    private static readonly Dictionary<string, PropertyInfo> SlotsByName = Slots.ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>src/harness/harnessIO.ts:257-273 — the options the harness declares itself. They are settings the
    /// *harness* reads (<c>noTypesAndSymbols</c>, <c>fullEmitPaths</c>, <c>currentDirectory</c>,
    /// <c>noImplicitReferences</c>, <c>useCaseSensitiveFileNames</c>, <c>noCheck</c>, …), not members of the compiler's
    /// options type, so a directive that names one is kept in the settings dictionary and not written into the options
    /// object — which is exactly what the reference's `options[name] = value` on a plain object amounts to.</summary>
    private static readonly HashSet<string> HarnessOnlyOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "allowNonTsExtensions",
        "useCaseSensitiveFileNames",
        "baselineFile",
        "fileName",
        "noErrorTruncation",
        "suppressOutputPathCheck",
        "noImplicitReferences",
        "currentDirectory",
        "symlink",
        "link",
        "noTypesAndSymbols",
        "fullEmitPaths",
        "noCheck",
        "reportDiagnostics",
        "captureSuggestions",
    };

    /// <summary>src/harness/harnessIO.ts:266-274 — <c>getCommandLineOption</c>.</summary>
    public static HarnessOption? GetCommandLineOption(string name)
        => BaselineDiffOptions.Declarations.GetValueOrDefault(name);

    /// <summary>src/harness/harnessIO.ts:302-325 — <c>optionValue</c>.</summary>
    private static object? OptionValue(HarnessOption option, string value, List<string> errors)
    {
        switch (option.Kind)
        {
            case HarnessOptionKind.Boolean:
                return value.ToLowerInvariant() == "true";
            case HarnessOptionKind.String:
                return value;
            case HarnessOptionKind.Number:
                var number = JsParseInt(value);
                if (number is null) throw new BaselineModelException($"Value must be a number, got: {JsonEscape(value)}");
                return number;
            case HarnessOptionKind.List:
            case HarnessOptionKind.ListOrElement:
                return ParseListTypeOption(option, value, errors);
            default:
                return ParseCustomTypeOption(option, value, errors);
        }
    }

    /// <summary>commandLineParser.ts:1884-1909 — <c>parseListTypeOption</c>. A value starting with <c>-</c> answers
    /// <c>undefined</c> (the harness then assigns <c>undefined</c> to the slot, which is "unset"), an empty value
    /// answers the empty list, and each element is converted by its own declaration.</summary>
    private static object? ParseListTypeOption(HarnessOption option, string value, List<string> errors)
    {
        value = value.Trim();
        if (value.StartsWith('-')) return null;
        if (option.Kind == HarnessOptionKind.ListOrElement && !value.Contains(',')) return ParseCustomTypeOption(option, value, errors) ?? value;
        if (value.Length == 0) return new List<object>();
        var element = option.Element ?? throw new BaselineModelException($"option '{option.Name}' is a list with no element declaration");
        var result = new List<object>();
        foreach (var part in value.Split(','))
        {
            object? converted = element.Kind switch
            {
                HarnessOptionKind.Number => JsParseInt(part),
                HarnessOptionKind.String => part.Length == 0 ? "" : part,
                HarnessOptionKind.Boolean or HarnessOptionKind.Object => throw new BaselineModelException($"List of {element.Kind} is not yet supported."),
                _ => ParseCustomTypeOption(option.Name, element.Kind, element.Map, part, errors),
            };
            if (converted is not null) result.Add(converted);
        }

        return result;
    }

    /// <summary>commandLineParser.ts:1879-1882 + 3860-3876 — <c>parseCustomTypeOption</c> over one custom-type table:
    /// the value lower-cased is looked up, and an unknown one reports rather than answering a default.</summary>
    private static object? ParseCustomTypeOption(HarnessOption option, string value, List<string> errors)
        => ParseCustomTypeOption(option.Name, option.Kind, option.Map, value, errors);

    private static object? ParseCustomTypeOption(string name, HarnessOptionKind kind, IReadOnlyDictionary<string, object>? map, string value, List<string> errors)
    {
        if (map is null || kind != HarnessOptionKind.Map)
        {
            throw new BaselineModelException($"option '{name}' is not a custom type, so its value '{value}' cannot be looked up");
        }

        var key = value.Trim().ToLowerInvariant();
        if (map.TryGetValue(key, out var found)) return found;
        errors.Add($"Unknown value '{value}' for compiler option '{name}'.");
        return null;
    }

    /// <summary>src/harness/harnessIO.ts:290-300 — the numeric parse <c>parseInt(value, 10)</c> performs: leading
    /// whitespace and an optional sign, then the digits that follow, or nothing at all.</summary>
    private static int? JsParseInt(string value)
    {
        var match = Regex.Match(value, @"^\s*([+-]?\d+)", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    /// <summary>src/harness/harnessIO.ts:277-300 — <c>setCompilerOptionsFromHarnessSetting</c>: every setting is
    /// converted by its option's declared type and written into the slot of the same name. The write is by reflection
    /// because the port's model is the generated <see cref="ICompilerOptions"/> interface, whose slots a JS object
    /// literal would fill in by name.</summary>
    public static void SetCompilerOptionsFromHarnessSetting(Dictionary<string, string> settings, ICompilerOptions options)
    {
        foreach (var (name, value) in settings)
        {
            if (name == "typeScriptVersion") continue;
            var option = GetCommandLineOption(name) ?? throw new BaselineModelException($"Unknown compiler option '{name}'.");
            var errors = new List<string>();
            var converted = OptionValue(option, value, errors);
            if (errors.Count > 0) throw new BaselineModelException(errors[0]);
            var property = FindSlot(option.Name);
            if (property is null)
            {
                // A harness-only setting (harnessIO.ts:257-273) has no compiler-options slot by design; a compiler
                // option that has none is a gap in the model and is reported.
                if (HarnessOnlyOptions.Contains(option.Name)) continue;
                throw new BaselineModelException($"the reference declares option '{option.Name}' but ICompilerOptions has no '{Capitalize(option.Name)}' slot");
            }

            property.SetValue(options, ConvertOptionValue(converted, property.PropertyType, option));
        }
    }

    /// <summary>The option name's slot in the generated model: the reference's own naming rule, capitalizing the first
    /// character and keeping the rest (so <c>strictNullChecks</c> is <c>StrictNullChecks</c>).</summary>
    private static string Capitalize(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static string LowerFirst(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static PropertyInfo? FindSlot(string optionName) => SlotsByName.GetValueOrDefault(Capitalize(optionName));

    /// <summary>The option declaration a slot is named after, or <c>null</c> for a slot the reference does not declare
    /// as an option (<c>configFile</c>, <c>configFilePath</c>, <c>build</c>, …).</summary>
    private static HarnessOption? OptionOfSlot(string slotName) => GetCommandLineOption(LowerFirst(slotName));

    private static object? ConvertOptionValue(object? value, System.Type targetType, HarnessOption option)
    {
        if (value is null) return null;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        switch (value)
        {
            case bool or string:
                return value;
            case int number when type.IsEnum:
                return Enum.ToObject(type, number);
            case int number:
                return number;
            case List<object> list when type == typeof(List<string>):
                return list.Cast<string>().ToList();
            case List<object>:
                throw new BaselineModelException($"option '{option.Name}' is a list of {type.Name}, which this tool cannot build");
            default:
                throw new BaselineModelException($"option '{option.Name}' has value type {value.GetType().Name}, which this tool cannot place");
        }
    }

    /// <summary>commandLineParser.ts:2971-3000 — <c>convertToOptionsWithAbsolutePaths</c>: every option declared as
    /// naming a path (or a list of them) has its value resolved against the current directory. The reference walks the
    /// options object's own keys; the port's model is an interface, so the walk is over its slots and the declaration
    /// is found by the slot's name.</summary>
    public static void ConvertToOptionsWithAbsolutePaths(ICompilerOptions options, Func<string, string> toAbsolutePath)
    {
        foreach (var property in Slots)
        {
            var option = OptionOfSlot(property.Name);
            if (option is null) continue;
            var value = property.GetValue(options);
            switch (value)
            {
                case string path when option.IsFilePath:
                    property.SetValue(options, toAbsolutePath(path));
                    break;
                case List<string> paths when option.Kind == HarnessOptionKind.List && option.Element?.IsFilePath == true:
                    property.SetValue(options, paths.Select(toAbsolutePath).ToList());
                    break;
            }
        }

        if (options.ConfigFilePath is { } configFilePath) options.ConfigFilePath = toAbsolutePath(configFilePath);
    }

    /// <summary>commandLineParser.ts:2916 — <c>cloneCompilerOptions</c>: a shallow copy of every slot.</summary>
    public static BaselineDiffCompilerOptions CloneOptions(ICompilerOptions source)
    {
        var clone = new BaselineDiffCompilerOptions();
        foreach (var property in Slots) property.SetValue(clone, property.GetValue(source));
        return clone;
    }

    // ---------------------------------------------------------------------------------------------
    // The configurations (src/testRunner/compilerRunner.ts:130-150 + src/harness/harnessIO.ts:960-1140).
    // ---------------------------------------------------------------------------------------------

    /// <summary>src/testRunner/compilerRunner.ts:130-150 — <c>CompilerTest.varyBy</c>: every option that is a boolean
    /// or a custom type and takes part in a program, plus the two the runner adds explicitly.</summary>
    public static List<string> ComputeVaryBy()
    {
        var names = BaselineDiffOptions.Declarations.Values
            .Where(option => !option.IsCommandLineOnly
                && option.Kind is HarnessOptionKind.Boolean or HarnessOptionKind.Map
                && option.Affects != OptionAffects.None)
            .Select(option => option.Name)
            .ToList();
        names.Add("noEmit");
        names.Add("isolatedModules");
        return names;
    }

    /// <summary>src/harness/harnessIO.ts:1121-1139 — <c>getVaryByStarSettingValues</c>: a boolean option's <c>*</c>
    /// values are <c>true</c>/<c>false</c>, a custom type's are its own table.</summary>
    private static IReadOnlyDictionary<string, object>? GetVaryByStarSettingValues(string varyBy)
    {
        var option = GetCommandLineOption(varyBy);
        return option?.Kind switch
        {
            HarnessOptionKind.Map => option.Map,
            HarnessOptionKind.Boolean => new Dictionary<string, object>(StringComparer.Ordinal) { ["true"] = 1, ["false"] = 0 },
            _ => null,
        };
    }

    /// <summary>src/harness/harnessIO.ts:1078-1119 — <c>splitVaryBySettingValue</c>.</summary>
    public static List<string>? SplitVaryBySettingValue(string text, string varyBy)
    {
        if (text.Length == 0) return null;
        var star = false;
        var includes = new List<string>();
        var excludes = new List<string>();
        foreach (var raw in text.Split(','))
        {
            var s = raw.Trim().ToLowerInvariant();
            if (s.Length == 0) continue;
            if (s == "*") star = true;
            else if (s.StartsWith('-') || s.StartsWith('!')) excludes.Add(s[1..]);
            else includes.Add(s);
        }

        if (includes.Count <= 1 && !star && excludes.Count == 0) return null;

        var variations = new List<(string Key, object? Value)>();
        var values = GetVaryByStarSettingValues(varyBy);
        void Add(string key, object? value)
        {
            if (!variations.Any(v => v.Key == key || (value is not null && Equals(v.Value, value)))) variations.Add((key, value));
        }

        foreach (var include in includes) Add(include, values is null ? null : values.GetValueOrDefault(include));
        if (star && values is not null)
        {
            foreach (var (key, value) in values) Add(key, value);
        }

        foreach (var exclude in excludes)
        {
            var value = values is null ? null : values.GetValueOrDefault(exclude);
            variations.RemoveAll(v => v.Key == exclude || (value is not null && Equals(v.Value, value)));
        }

        if (variations.Count == 0) throw new BaselineModelException($"Variations in test option '@{varyBy}' resulted in an empty set.");
        return variations.Select(v => v.Key).ToList();
    }

    /// <summary>src/harness/harnessIO.ts:1144-1175 — <c>getFileBasedTestConfigurations</c>: the cartesian product of
    /// every setting that has variations, capped at 25 as the runner caps it.</summary>
    public static List<Dictionary<string, string>>? GetFileBasedTestConfigurations(Dictionary<string, string> settings, List<string> varyBy)
    {
        var varyByEntries = new List<(string Key, List<string> Entries)>();
        var variationCount = 1;
        foreach (var varyByKey in varyBy)
        {
            if (!settings.TryGetValue(varyByKey, out var text)) continue;
            var entries = SplitVaryBySettingValue(text, varyByKey);
            if (entries is null) continue;
            variationCount *= entries.Count;
            if (variationCount > 25) throw new BaselineModelException("Provided test options exceeded the maximum number of variations: 25");
            varyByEntries.Add((varyByKey, entries));
        }

        if (varyByEntries.Count == 0) return null;

        var configurations = new List<Dictionary<string, string>>();
        Compute(new Dictionary<string, string>(StringComparer.Ordinal), 0);
        return configurations;

        void Compute(Dictionary<string, string> state, int offset)
        {
            if (offset >= varyByEntries.Count)
            {
                configurations.Add(new Dictionary<string, string>(state, StringComparer.Ordinal));
                return;
            }

            var (key, entries) = varyByEntries[offset];
            foreach (var entry in entries)
            {
                state[key] = entry;
                Compute(state, offset + 1);
            }
        }
    }

    /// <summary>src/testRunner/compilerRunner.ts:175-190 — the configured name: the file's base name with the sorted
    /// configuration entries appended as <c>(key=value,…)</c>, both lower-cased.</summary>
    public static string ConfiguredName(string justName, Dictionary<string, string>? configuration)
    {
        if (configuration is null || configuration.Count == 0) return justName;
        var entries = new List<string>();
        foreach (var key in configuration.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            entries.Add($"{key.ToLowerInvariant()}={configuration[key].ToLowerInvariant()}");
        }

        if (entries.Count == 0) return justName;
        var extension = PathExtension(justName);
        var baseName = justName[..^extension.Length];
        return $"{baseName}({string.Join(",", entries)}){extension}";
    }

    /// <summary><c>path.posix.extname</c>: the last dot's suffix of the last component, and nothing when the name is
    /// a dot-file or has no dot at all.</summary>
    private static string PathExtension(string name)
    {
        var slash = name.LastIndexOf('/');
        var dot = name.LastIndexOf('.');
        return dot > slash + 1 ? name[dot..] : "";
    }

    /// <summary>src/testRunner/compilerRunner.ts:191-250 — <c>CompilerTest</c>'s constructor: which units become root
    /// names and which are only on the file system. A config unit, or a declared symlink, is reported.</summary>
    public static BaselineCaseUnits AssembleUnits(BaselineCaseContent content, Dictionary<string, string> settings)
    {
        if (content.TsConfigFileName is not null)
        {
            throw new BaselineModelException($"the test declares a config file ('{content.TsConfigFileName}'); the port has no config-file reader (M8)");
        }

        if (content.Symlinks is not null)
        {
            throw new BaselineModelException($"the test declares symlinks ({string.Join(", ", content.Symlinks.Keys)}); the harness applies them with fs.apply, which this tool does not model");
        }

        var units = content.Units;
        var toBeCompiled = new List<TestFile>();
        var otherFiles = new List<TestFile>();
        var hasNonDtsFiles = units.Any(unit => !Paths.FileExtensionIs(unit.Name, ".d.ts"));
        TestFile AsTestFile(TestUnitData unit) => new(unit.Name, unit.Content, unit.FileOptions);

        if (settings.TryGetValue("baseUrl", out var baseUrl) && !Paths.IsRootedDiskPath(baseUrl))
        {
            settings["baseUrl"] = Paths.GetNormalizedAbsolutePath(baseUrl, BaselineDiffVirtualFileSystem.SrcFolder);
        }

        var lastUnit = units[^1];
        if ((settings.TryGetValue("noImplicitReferences", out var noImplicitReferences) && noImplicitReferences.Length != 0)
            || Regex.IsMatch(lastUnit.Content, "require\\(", RegexOptions.CultureInvariant)
            || Regex.IsMatch(lastUnit.Content, "reference\\spath", RegexOptions.CultureInvariant))
        {
            toBeCompiled.Add(AsTestFile(lastUnit));
            foreach (var unit in units)
            {
                if (unit.Name != lastUnit.Name) otherFiles.Add(AsTestFile(unit));
            }
        }
        else
        {
            foreach (var unit in units) toBeCompiled.Add(AsTestFile(unit));
        }

        return new BaselineCaseUnits(toBeCompiled, otherFiles, hasNonDtsFiles, []);
    }

    /// <summary>src/testRunner/compilerRunner.ts:180-260 + src/harness/harnessIO.ts:1288-1390 — one corpus file to
    /// its cases: the settings from the whole file's text, the units, and one entry per derived configuration.</summary>
    public static List<BaselineCase> DeriveCases(string fileName, string content)
    {
        var settings = ExtractCompilerSettings(content);
        var caseContent = MakeUnitsFromTest(content, fileName, settings);
        var configurations = GetFileBasedTestConfigurations(settings, ComputeVaryBy());
        var result = new List<BaselineCase>();
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var configuration in configurations ?? [empty])
        {
            var merged = new Dictionary<string, string>(settings, StringComparer.Ordinal);
            foreach (var (key, value) in configuration) merged[key] = value;
            result.Add(new BaselineCase(configuration, merged, caseContent));
        }

        return result;
    }
}
