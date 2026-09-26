using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts (the members module resolution reads that no earlier slice landed)
//   6483-6490  hostUsesCaseSensitiveFileNames / hostGetCanonicalFileName
//   6582-6587  getPossibleOriginalInputExtensionForExtension
//   6609-6612  getPathsBasePath
//   7626-7628  tryExtractTSExtension
//   7786-7812  readJsonOrUndefined / readJson / tryParseJson / directoryProbablyExists
//   8705-8711  formatMessage
//   9025-9027  usesWildcardTypes
//   9399-9403  getCompilerOptionValue
//   10203-10334 tryParsePattern / ParsedPatterns / tryParsePatterns / matchPatternOrExact
//   10276-10278 extensionIsTS
//   11196-11202 replaceFirstStar
// ported from: src/compiler/utilitiesPublic.ts:296-301 (isExternalModuleNameRelative)
// ported from: src/compiler/program.ts:342-380 (computeCommonSourceDirectoryOfFilenames), 1138 (inferredTypesContainingFile)
// ported from: src/compiler/commandLineParser.ts:1707 (moduleResolutionOptionDeclarations, filtered subset)
/// <summary>
/// The <c>utilities.ts</c>/<c>utilitiesPublic.ts</c>/<c>program.ts</c>/<c>commandLineParser.ts</c> members module
/// resolution needs that are not already ported. Every one of them is reachable from
/// <c>moduleNameResolver.ts</c>; the ones a later slice also wants are noted in their remarks.
/// </summary>
/// <remarks>
/// PORT-NOTE: these are declared here rather than in the file named after their source file because this slice owns
/// only <c>Program/</c> (the port's convention for a name needed before its own slice lands is one shared hand-written
/// file, exactly as <c>FakeSystem</c>-style hosts and the M4f emit-helper prerequisites were handled). Each member
/// carries its own <c>ported from</c> line, so moving it to its eventual file is mechanical.
/// <para>
/// PORT-NOTE: <c>getCommonSourceDirectory</c> (<c>emitter.ts:636</c>) is deliberately <em>not</em> declared: it is M7's.
/// The one caller in <c>moduleNameResolver.ts</c> reaches only two of its three arms (it is guarded on
/// <c>rootDir || configFilePath</c> in the first, and has neither in the second), and those two arms are inlined at
/// that call site with the same trailing-separator behavior.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // Hosts
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:6483-6485 — <c>hostUsesCaseSensitiveFileNames</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks the host for a function (<c>useCaseSensitiveFileNames?(): boolean</c>); the generated
    /// <see cref="IModuleResolutionHost"/> types the slot <c>object?</c> because <c>sys.ts</c>'s host is also a plain
    /// boolean there. Both arms are accepted, which is what the interfaces in <c>moduleNameResolver.ts</c> do.
    /// </remarks>
    public static bool HostUsesCaseSensitiveFileNames(IModuleResolutionHost host)
        => host.UseCaseSensitiveFileNames switch
        {
            bool b => b,
            Func<bool> f => f(),
            _ => false,
        };

    /// <summary>ported from: src/compiler/utilities.ts:6488-6490 — <c>hostGetCanonicalFileName</c>.</summary>
    public static Core.GetCanonicalFileName HostGetCanonicalFileName(IModuleResolutionHost host)
        => Core.CreateGetCanonicalFileName(HostUsesCaseSensitiveFileNames(host));

    /// <summary>ported from: src/compiler/utilities.ts:7819-7822 — <c>directoryProbablyExists</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers "assume it exists" when the host has no <c>directoryExists</c>. The generated
    /// <see cref="IModuleResolutionHost"/> declares it non-optional (the generator does not record optionality), so the
    /// port always asks — a host that cannot answer must answer "exists" itself, which is what the reference's default
    /// <c>sys</c> host does.
    /// </remarks>
    public static bool DirectoryProbablyExists(string directoryName, IModuleResolutionHost host)
        => host.DirectoryExists(directoryName);

    /// <summary>ported from: src/compiler/utilities.ts:9042-9047 — the <c>allowImportingTsExtensions</c> entry of <c>createComputedCompilerOptions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: main's <c>Program/Utilities.Program.cs</c> holds the other computed-option readers this slice needs;
    /// this one is only read by <c>shouldAllowImportingTsExtension</c>, so it lives with the module-resolution slice for
    /// now (moving it beside the others is mechanical).
    /// </remarks>
    public static bool GetAllowImportingTsExtensions(ICompilerOptions? options)
        => Js.IsTruthy(options!.AllowImportingTsExtensions) || Js.IsTruthy(options.RewriteRelativeImportExtensions);

    /// <summary>ported from: src/compiler/utilities.ts:6609-6612 — <c>getPathsBasePath</c>.</summary>
    public static string? GetPathsBasePath(ICompilerOptions options, IModuleResolutionHost host)
        => Js.IsTruthy(options.Paths)
            ? options.BaseUrl ?? Debug.CheckDefined(
                options.PathsBasePath ?? host.GetCurrentDirectory(),
                "Encountered 'paths' without a 'baseUrl', config file, or host 'getCurrentDirectory'.")
            : null;

    // ---------------------------------------------------------------------------------------------
    // Extensions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:11196-11202 — <c>replaceFirstStar</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS calls <c>stringReplace(s, "*", replacement)</c>, i.e. <c>String.prototype.replace</c> with a string
    /// pattern, which replaces the <em>first</em> occurrence only. <see cref="string.Replace(string,string)"/> would
    /// replace all of them, so the index is taken explicitly.
    /// </remarks>
    public static string ReplaceFirstStar(string s, string replacement)
    {
        var index = s.IndexOf('*');
        return index == -1 ? s : string.Concat(s.AsSpan(0, index), replacement, s.AsSpan(index + 1));
    }

    // ---------------------------------------------------------------------------------------------
    // Module names
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:296-301 — <c>isExternalModuleNameRelative</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the parameter non-nullable; the port's callers read the name out of the generated node
    /// model, where the slot is <c>string?</c>, so it is widened and the reference's behavior on a missing name (a read
    /// of it) is kept: an absent name fails the assert here rather than answering "relative".
    /// </remarks>
    public static bool IsExternalModuleNameRelative(string? moduleName)
    {
        Debug.Assert(moduleName is not null);
        return Paths.PathIsRelative(moduleName!) || Paths.IsRootedDiskPath(moduleName!);
    }

    /// <summary>ported from: src/compiler/utilities.ts:9025-9027 — <c>usesWildcardTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is a type guard (<c>options is CompilerOptions &amp; { types: string[] }</c>); the
    /// caller's <c>options.types.map</c> is what needs the guarantee, and the port's <c>Types</c> slot is already
    /// <c>List&lt;string&gt;?</c>, so the guard has nothing left to narrow.
    /// </remarks>
    public static bool UsesWildcardTypes(ICompilerOptions options)
        => Core.Some(options.Types, t => t == "*");

    // ---------------------------------------------------------------------------------------------
    // Patterns ('paths' and 'typesVersions')
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:10217-10220 — <c>ParsedPatterns</c>.</summary>
    internal sealed class ParsedPatterns
    {
        public HashSet<string>? MatchableStringSet { get; init; }

        public List<Core.Pattern>? Patterns { get; init; }
    }

    /// <summary>
    /// ported from: src/compiler/utilities.ts:10222-10265 — <c>parsedPatternsCache</c> / <c>tryParsePatterns</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference memoizes on the identity of the <c>paths</c> object with a <c>WeakMap</c>; the same
    /// identity-keyed memo is a <see cref="ConditionalWeakTable{TKey,TValue}"/> here, so repeated lookups with the same
    /// options object do not re-parse the patterns.
    /// </remarks>
    private static readonly ConditionalWeakTable<object, ParsedPatterns> ParsedPatternsCache = new();

    public static ParsedPatterns TryParsePatterns(Dictionary<string, List<string>> paths)
    {
        if (ParsedPatternsCache.TryGetValue(paths, out var cached))
        {
            return cached;
        }

        HashSet<string>? matchableStringSet = null;
        List<Core.Pattern>? patterns = null;

        var pathList = Core.GetOwnKeys(paths);
        foreach (var path in pathList)
        {
            // PORT-NOTE: `tryParsePattern` is already ported (Utilities/Utilities.Binder.Modules.cs, same range) in the
            // .NET parse shape: false is TS's `undefined` (two or more stars), true with a null out-parameter is TS's
            // plain-string answer — which is the input itself, so nothing is lost.
            if (!Utilities.TryParsePattern(path, out var pattern))
            {
                continue;
            }

            if (pattern is null)
            {
                (matchableStringSet ??= new HashSet<string>(StringComparer.Ordinal)).Add(path);
            }
            else
            {
                (patterns ??= []).Add(pattern.Value);
            }
        }

        var result = new ParsedPatterns { MatchableStringSet = matchableStringSet, Patterns = patterns };
        ParsedPatternsCache.Add(paths, result);
        return result;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10324-10334 — <c>matchPatternOrExact</c>.</summary>
    public static object? MatchPatternOrExact(ParsedPatterns parsedPatterns, string candidate)
    {
        var (matchableStringSet, patterns) = (parsedPatterns.MatchableStringSet, parsedPatterns.Patterns);

        if (matchableStringSet?.Contains(candidate) == true)
        {
            return candidate;
        }

        if (patterns is null || patterns.Count == 0)
        {
            return null;
        }

        // PORT-NOTE: TS calls `findBestPatternMatch(patterns, _ => _, candidate)`. `Core.FindBestPatternMatch` is
        // constrained to reference types while `Core.Pattern` is a struct, so the same loop lives here; the body and the
        // "longest prefix wins" tie-break are the reference's.
        Core.Pattern? matchedValue = null;
        var longestMatchPrefixLength = -1;
        foreach (var pattern in patterns)
        {
            if (pattern.Prefix.Length > longestMatchPrefixLength && Core.IsPatternMatch(pattern, candidate))
            {
                longestMatchPrefixLength = pattern.Prefix.Length;
                matchedValue = pattern;
            }
        }

        return matchedValue;
    }

    // ---------------------------------------------------------------------------------------------
    // Compiler options
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ported from: src/compiler/commandLineParser.ts:1707 — <c>moduleResolutionOptionDeclarations</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the reference filters the full <c>optionDeclarations</c> metadata table (<c>commandLineParser.ts</c>,
    /// M8) down to the entries marked <c>affectsModuleResolution</c>. That table and its <c>CommandLineOption</c> model
    /// are M8's, so only the filtered subset is transcribed here, as the list of option names the filter selects (the
    /// only field <c>getCompilerOptionValue</c> reads beyond the flags, which no selected option sets). When M8 lands
    /// the table this list should be derived from it.
    /// </remarks>
    public static readonly List<string> ModuleResolutionOptionDeclarations =
    [
        "target",
        "module",
        "checkJs",
        "jsx",
        "moduleResolution",
        "baseUrl",
        "paths",
        "rootDirs",
        "typeRoots",
        "moduleSuffixes",
        "resolvePackageJsonExports",
        "resolvePackageJsonImports",
        "customConditions",
        "jsxImportSource",
        "resolveJsonModule",
        "noResolve",
        "forceConsistentCasingInFileNames",
        "maxNodeModuleJsDepth",
        "moduleDetection",
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9399-9403 — <c>getCompilerOptionValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>option.strictFlag</c>/<c>option.allowJsFlag</c> off a
    /// <c>CommandLineOption</c> and otherwise indexes <c>options[option.name]</c>. None of the module-resolution
    /// options sets either flag (checked against the table), so the port takes the name and returns the matching slot;
    /// an unknown name is <c>undefined</c>, as <c>options[unknown]</c> would be. See
    /// <see cref="ModuleResolutionOptionDeclarations"/> for why the metadata table itself is not here.
    /// </remarks>
    public static object? GetCompilerOptionValue(ICompilerOptions options, string optionName) => optionName switch
    {
        "target" => options.Target,
        "module" => options.Module,
        "checkJs" => options.CheckJs,
        "jsx" => options.Jsx,
        "moduleResolution" => options.ModuleResolution,
        "baseUrl" => options.BaseUrl,
        "paths" => options.Paths,
        "rootDirs" => options.RootDirs,
        "typeRoots" => options.TypeRoots,
        "moduleSuffixes" => options.ModuleSuffixes,
        "resolvePackageJsonExports" => options.ResolvePackageJsonExports,
        "resolvePackageJsonImports" => options.ResolvePackageJsonImports,
        "customConditions" => options.CustomConditions,
        "jsxImportSource" => options.JsxImportSource,
        "resolveJsonModule" => options.ResolveJsonModule,
        "noResolve" => options.NoResolve,
        "forceConsistentCasingInFileNames" => options.ForceConsistentCasingInFileNames,
        "maxNodeModuleJsDepth" => options.MaxNodeModuleJsDepth,
        "moduleDetection" => options.ModuleDetection,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // JSON (package.json)
    // ---------------------------------------------------------------------------------------------


    /// <summary>ported from: src/compiler/utilities.ts:7786-7802 — <c>readJsonOrUndefined</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS tries <c>JSON.parse</c> first and falls back to <c>parseConfigFileTextToJson</c> (the
    /// comment/trailing-comma tolerant parser in <c>commandLineParser.ts</c>, M8) for text the strict parser rejects.
    /// Only the strict half is ported: the reference's own comment says package.json files should never carry comments
    /// or trailing commas. A file that does will read as "no value" here, where the reference would read it loosely;
    /// the loose parser is M8's and belongs with tsconfig reading.
    /// <para>
    /// PORT-NOTE: parsing goes through <see cref="JsonDocument"/> in its strict configuration, whose accepted grammar is
    /// <c>JSON.parse</c>'s (no comments, no trailing commas, any top-level value) and whose member order is document
    /// order. The document is materialized into <see cref="JsObject"/>/<c>List&lt;object?&gt;</c> so nothing retains the
    /// pooled buffer.
    /// </para>
    /// </remarks>
    public static object? ReadJsonOrUndefined(string path, IModuleResolutionHost host)
        => ReadJsonOrUndefined(path, host.ReadFile(path));

    /// <inheritdoc cref="ReadJsonOrUndefined(string, IModuleResolutionHost)"/>
    public static object? ReadJsonOrUndefined(string path, string? jsonText)
    {
        if (string.IsNullOrEmpty(jsonText))
        {
            return null;
        }

        return TryParseJson(jsonText, out var result) ? result : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7804-7806 — <c>readJson</c>.</summary>
    public static JsObject ReadJson(string path, IModuleResolutionHost host)
        => ReadJsonOrUndefined(path, host) as JsObject ?? new JsObject();

    /// <summary>ported from: src/compiler/utilities.ts:7795-7802 — <c>tryParseJson</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>try/catch</c> around <c>JSON.parse</c> becomes the <c>ok</c> result; <c>null</c> is a valid
    /// parsed value (JSON's <c>null</c>), which is why the caller gets it back through <paramref name="value"/> rather
    /// than as the method's answer.
    /// </remarks>
    public static bool TryParseJson(string text, out object? value)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            value = ConvertJsonElement(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ConvertJsonObject(element),
        JsonValueKind.Array => ConvertJsonArray(element),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static JsObject ConvertJsonObject(JsonElement element)
    {
        var result = new JsObject();
        foreach (var property in element.EnumerateObject())
        {
            result.Set(property.Name, ConvertJsonElement(property.Value));
        }

        return result;
    }

    private static List<object?> ConvertJsonArray(JsonElement element)
    {
        var result = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            result.Add(ConvertJsonElement(item));
        }

        return result;
    }

    /// <summary>JS's <c>typeof</c>, for the two trace messages that print it.</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>null</c> reports <c>"object"</c>, and the one call site tests <c>value === null</c> first
    /// (matching the reference's expression), so only the remaining kinds are needed here.
    /// </remarks>
    public static string JsTypeOf(object? value) => value switch
    {
        null => "object",
        string => "string",
        bool => "boolean",
        double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort => "number",
        _ => "object",
    };

    // ---------------------------------------------------------------------------------------------
    // program.ts members module resolution reads
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1138 — <c>inferredTypesContainingFile</c>.</summary>
    public const string InferredTypesContainingFile = "__inferred type names__.ts";

    /// <summary>ported from: src/compiler/program.ts:342-380 — <c>computeCommonSourceDirectoryOfFilenames</c>.</summary>
    public static string ComputeCommonSourceDirectoryOfFilenames(
        IList<string> fileNames,
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        List<string>? commonPathComponents = null;
        // PORT-NOTE: TS's `forEach` stops at the first truthy callback result; `Core.Some` is that same short-circuit
        // for a boolean callback (Core.ForEach's generic result is constrained to a reference type).
        var failed = Core.Some(fileNames, sourceFile =>
        {
            // Each file contributes into common source file path
            var sourcePathComponents = Paths.GetNormalizedPathComponents(sourceFile, currentDirectory);
            sourcePathComponents.RemoveAt(sourcePathComponents.Count - 1); // The base file name is not part of the common directory path

            if (commonPathComponents is null)
            {
                // first file
                commonPathComponents = sourcePathComponents;
                return false;
            }

            var n = Math.Min(commonPathComponents.Count, sourcePathComponents.Count);
            for (var i = 0; i < n; i++)
            {
                if (getCanonicalFileName(commonPathComponents[i]) != getCanonicalFileName(sourcePathComponents[i]))
                {
                    if (i == 0)
                    {
                        // Failed to find any common path component
                        return true;
                    }

                    // New common path found that is 0 -> i-1
                    commonPathComponents.RemoveRange(i, commonPathComponents.Count - i);
                    break;
                }
            }

            // If the sourcePathComponents was shorter than the commonPathComponents, truncate to the sourcePathComponents
            if (sourcePathComponents.Count < commonPathComponents.Count)
            {
                commonPathComponents.RemoveRange(sourcePathComponents.Count, commonPathComponents.Count - sourcePathComponents.Count);
            }

            return false;
        });

        // A common path can not be found when paths span multiple drives on windows, for example
        if (failed)
        {
            return "";
        }

        if (commonPathComponents is null)
        {
            // Can happen when all input files are .d.ts files
            return currentDirectory;
        }

        return Paths.GetPathFromPathComponents(commonPathComponents);
    }
}

/// <summary>
/// The value <c>JSON.parse</c> produces, as far as module resolution reads it.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS reads package.json through <c>readJson</c>, which hands back an ordinary JS object; every field
/// read (<c>hasProperty</c>, <c>getOwnKeys</c>, <c>for…in</c>) therefore has an <em>insertion-ordered</em> member
/// list, which matters for <c>exports</c>/<c>imports</c> condition order and for <c>typesVersions</c>'
/// first-match-wins. <see cref="Dictionary{TKey,TValue}"/> does not promise that order (§5 rule 17), so this class
/// keeps the key list beside the map. Values are <c>string</c>, <c>double</c>, <c>bool</c>, <c>null</c>,
/// <c>List&lt;object?&gt;</c> (arrays) or another <see cref="JsObject"/>.
/// </remarks>
internal sealed class JsObject
{
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>The member names in insertion order — what <c>getOwnKeys</c> answers.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getOwnKeys</c> hands back a fresh <c>string[]</c>; the port exposes the live key list so it
    /// can be passed to the <c>Core</c> list helpers without copying, and nothing in this slice mutates it.
    /// </remarks>
    public List<string> Keys => _keys;

    public int Count => _keys.Count;

    public bool Has(string key) => _values.ContainsKey(key);

    public object? Get(string key) => _values.GetValueOrDefault(key);

    public void Set(string key, object? value)
    {
        if (!_values.ContainsKey(key))
        {
            _keys.Add(key);
        }

        _values[key] = value;
    }
}
