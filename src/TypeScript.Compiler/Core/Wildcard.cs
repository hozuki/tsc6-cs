using System.Text;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:9591-9819, src/compiler/commandLineParser.ts:4040-4078
/// <summary>
/// Turns "include"/"exclude" specs (<c>**/*.ts</c>, <c>src/**</c>) into regular expressions, plus the
/// exclude-spec matching built on top of them.
/// </summary>
/// <remarks>
/// PORT-NOTE: the wildcard family lives in <c>utilities.ts</c> and <c>matchesExclude</c> in
/// <c>commandLineParser.ts</c> — Layers 5 and 8 of the port. They are pulled forward into <c>Core</c> because
/// <c>sys.ts</c>'s directory watchers need them (<c>isIgnoredByWatchOptions</c>, <c>isIgnoredPath</c>) and they
/// carry no compiler semantics: they are pure string/path logic. The wildcard helpers nothing needs yet
/// (<c>getPatternFromSpec</c>, <c>regExpEscape</c>, <c>getFileMatcherPatterns</c>, <c>matchFiles</c>) stay behind
/// with their own layers.
/// </remarks>
internal static class Wildcard
{
    /// <summary>What a spec is being used for. TS spells this as the literal union
    /// <c>"files" | "directories" | "exclude"</c> and indexes a table with it.</summary>
    public enum Usage
    {
        Files,
        Directories,
        Exclude,
    }

    /// <summary>How one usage spells a wildcard in a regular expression.</summary>
    /// <remarks>ported from: src/compiler/utilities.ts:9611-9616 — TS: <c>WildcardMatcher</c>.</remarks>
    public sealed class Matcher
    {
        public required string SingleAsteriskRegexFragment { get; init; }

        public required string DoubleAsteriskRegexFragment { get; init; }

        /// <summary>Maps one reserved character (or <c>*</c>/<c>?</c>) to its regex spelling.</summary>
        public required Func<string, string> ReplaceWildcardCharacter { get; init; }
    }

    // ported from: src/compiler/utilities.ts:9591-9594
    /// <summary>
    /// Reserved characters, forcing escaping of any non-word (or digit), non-whitespace character. It may be
    /// inefficient (we could just match <c>(/[-[\]{}()*+?.,\\^$|#\s]/g)</c>, but this is future proof).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>\w</c> is ASCII-only while .NET's is Unicode-aware, so copying the class verbatim
    /// would let <c>é</c>-like characters through unescaped; hence the explicit ASCII range. <c>\s</c> is kept
    /// as written — the two definitions differ only on <c>\uFEFF</c> and <c>\u0085</c>, which do not occur in the
    /// paths this feeds.
    /// </remarks>
    private static readonly Regex s_reservedCharacterPattern = new(@"[^A-Za-z0-9_\s/]", RegexOptions.Compiled);

    // ported from: src/compiler/utilities.ts:9685
    /// <summary>Characters that make a path component a glob rather than a literal.</summary>
    private static readonly Regex s_globCharacterPattern = new(@"[.*?]", RegexOptions.Compiled);

    // ported from: src/compiler/utilities.ts:9607
    private static readonly string[] s_commonPackageFolders = ["node_modules", "bower_components", "jspm_packages"];

    // ported from: src/compiler/utilities.ts:9609
    private static readonly string s_implicitExcludePathRegexPattern =
        $"(?!(?:{string.Join("|", s_commonPackageFolders)})(?:/|$))";

    // ported from: src/compiler/utilities.ts:9618-9648
    /// <summary>
    /// The single-asterisk fragment of each matcher below.
    /// </summary>
    /// <remarks>
    /// TS reads the fragment back off the matcher object itself
    /// (<c>match =&gt; replaceWildcardCharacter(match, filesMatcher.singleAsteriskRegexFragment)</c>); a C#
    /// static field initialiser may not reference the field it is initialising, so the fragments are named
    /// constants instead. The values are identical.
    /// </remarks>
    private const string FilesSingleAsteriskRegexFragment = "(?:[^./]|(?:\\.(?!min\\.js$))?)*";

    /// <summary>The fragment shared by the directories and exclude matchers.</summary>
    private const string DirectoryLikeSingleAsteriskRegexFragment = "[^/]*";

    // ported from: src/compiler/utilities.ts:9618-9632
    private static readonly Matcher s_filesMatcher = new()
    {
        // Matches any single directory segment unless it is the last segment and a .min.js file:
        //   [^./]                   everything up to the first . character (excluding directory separators)
        //   (\\.(?!min\\.js$))?     . characters, but not if they are part of the .min.js file extension
        SingleAsteriskRegexFragment = FilesSingleAsteriskRegexFragment,

        // Regex for the ** wildcard. Matches any number of subdirectories. When used for including files or
        // directories, does not match subdirectories that start with a . character.
        DoubleAsteriskRegexFragment = $"(?:/{s_implicitExcludePathRegexPattern}[^/.][^/]*)*?",

        ReplaceWildcardCharacter = match => ReplaceWildcard(match, FilesSingleAsteriskRegexFragment),
    };

    // ported from: src/compiler/utilities.ts:9634-9642
    private static readonly Matcher s_directoriesMatcher = new()
    {
        SingleAsteriskRegexFragment = DirectoryLikeSingleAsteriskRegexFragment,
        DoubleAsteriskRegexFragment = $"(?:/{s_implicitExcludePathRegexPattern}[^/.][^/]*)*?",
        ReplaceWildcardCharacter = match => ReplaceWildcard(match, DirectoryLikeSingleAsteriskRegexFragment),
    };

    // ported from: src/compiler/utilities.ts:9644-9648
    private static readonly Matcher s_excludeMatcher = new()
    {
        SingleAsteriskRegexFragment = DirectoryLikeSingleAsteriskRegexFragment,
        DoubleAsteriskRegexFragment = "(?:/.+?)?",
        ReplaceWildcardCharacter = match => ReplaceWildcard(match, DirectoryLikeSingleAsteriskRegexFragment),
    };

    // ported from: src/compiler/utilities.ts:9650-9654
    private static Matcher MatcherFor(Usage usage) => usage switch
    {
        Usage.Files => s_filesMatcher,
        Usage.Directories => s_directoriesMatcher,
        _ => s_excludeMatcher,
    };

    // ported from: src/compiler/utilities.ts:9656-9667
    public static string? GetRegularExpressionForWildcard(IReadOnlyList<string>? specs, string basePath, Usage usage)
    {
        var patterns = GetRegularExpressionsForWildcards(specs, basePath, usage);
        if (patterns is null || patterns.Count == 0) return null;

        var pattern = string.Join("|", patterns.Select(static pattern => $"(?:{pattern})"));
        // If excluding, match "foo/bar/baz...", but if including, only allow "foo".
        var terminator = usage == Usage.Exclude ? "(?:$|/)" : "$";
        return $"^(?:{pattern}){terminator}";
    }

    // ported from: src/compiler/utilities.ts:9669-9676
    public static IReadOnlyList<string>? GetRegularExpressionsForWildcards(IReadOnlyList<string>? specs, string basePath, Usage usage)
    {
        if (specs is null || specs.Count == 0) return null;

        // TS writes `spec && getSubPatternFromSpec(…)`: an empty spec is falsy, so the alternative it produces
        // is dropped by flatMap — matching an empty spec never matches anything.
        // (Spelled with Length rather than Js.IsTruthy: the generic IsTruthy overload would answer "is not
        // null" for a string, which is the wrong question here.)
        return Core.FlatMap<string, string>(specs, (spec, _) =>
            spec.Length != 0 ? GetSubPatternFromSpec(spec, basePath, usage) : null);
    }

    // ported from: src/compiler/utilities.ts:9684-9686
    /// <summary>
    /// An "includes" path "foo" is implicitly a glob "foo/**/*" if its last component has no extension, and
    /// does not contain any glob characters itself.
    /// </summary>
    public static bool IsImplicitGlob(string lastPathComponent) => !s_globCharacterPattern.IsMatch(lastPathComponent);

    // ported from: src/compiler/utilities.ts:9694-9774
    public static string? GetSubPatternFromSpec(string spec, string basePath, Usage usage, Matcher? matcher = null)
    {
        var m = matcher ?? MatcherFor(usage);
        var subpattern = new StringBuilder();
        var hasWrittenComponent = false;
        var components = Paths.GetNormalizedPathComponents(spec, basePath);
        var lastComponent = Core.Last(components);
        if (usage != Usage.Exclude && lastComponent == "**") return null;

        // getNormalizedPathComponents includes the separator for the root component.
        // We need to remove to create our regex correctly.
        components[0] = Paths.RemoveTrailingDirectorySeparator(components[0]);

        if (IsImplicitGlob(lastComponent))
        {
            components.Add("**");
            components.Add("*");
        }

        var optionalCount = 0;
        foreach (var componentValue in components)
        {
            var component = componentValue;
            if (component == "**")
            {
                subpattern.Append(m.DoubleAsteriskRegexFragment);
            }
            else
            {
                if (usage == Usage.Directories)
                {
                    subpattern.Append("(?:");
                    optionalCount++;
                }

                if (hasWrittenComponent) subpattern.Append(Paths.DirectorySeparator);

                if (usage != Usage.Exclude)
                {
                    var componentPattern = new StringBuilder();
                    // The * and ? wildcards should not match directories or files that start with . if they
                    // appear first in a component. Dotted directories and files can be included explicitly
                    // like so: **/.*/.*
                    if (component.Length != 0 && component[0] == CharacterCodes.Asterisk)
                    {
                        componentPattern.Append("(?:[^./]").Append(m.SingleAsteriskRegexFragment).Append(")?");
                        component = component[1..];
                    }
                    else if (component.Length != 0 && component[0] == CharacterCodes.Question)
                    {
                        componentPattern.Append("[^./]");
                        component = component[1..];
                    }

                    componentPattern.Append(EscapeReservedCharacters(component, m));

                    // Patterns should not include subfolders like node_modules unless they are explicitly
                    // included as part of the path.
                    //
                    // As an optimization, if the component pattern is the same as the component, then there
                    // definitely were no wildcard characters and we do not need to add the exclusion pattern.
                    if (!componentPattern.ToString().Equals(component, StringComparison.Ordinal))
                    {
                        subpattern.Append(s_implicitExcludePathRegexPattern);
                    }

                    subpattern.Append(componentPattern);
                }
                else
                {
                    subpattern.Append(EscapeReservedCharacters(component, m));
                }
            }

            hasWrittenComponent = true;
        }

        while (optionalCount > 0)
        {
            subpattern.Append(")?");
            optionalCount--;
        }

        return subpattern.ToString();
    }

    // ported from: src/compiler/utilities.ts:9776-9778
    private static string ReplaceWildcard(string match, string singleAsteriskRegexFragment)
        => match == "*" ? singleAsteriskRegexFragment : match == "?" ? "[^/]" : "\\" + match;

    /// <summary>Applies the matcher's replacement to every reserved character of <paramref name="component"/>.</summary>
    private static string EscapeReservedCharacters(string component, Matcher matcher)
        => s_reservedCharacterPattern.Replace(component, match => matcher.ReplaceWildcardCharacter(match.Value));

    // ported from: src/compiler/commandLineParser.ts:4055-4067
    /// <summary>Whether <paramref name="pathToCheck"/> is matched by one of the exclude specs.</summary>
    public static bool MatchesExclude(string pathToCheck, IReadOnlyList<string>? excludeSpecs, bool useCaseSensitiveFileNames, string currentDirectory)
        => MatchesExcludeWorker(
            pathToCheck,
            Core.Filter(excludeSpecs, static spec => !InvalidDotDotAfterRecursiveWildcard(spec)),
            useCaseSensitiveFileNames,
            currentDirectory);

    // ported from: src/compiler/commandLineParser.ts:4070-4078
    public static bool MatchesExcludeWorker(
        string pathToCheck,
        IReadOnlyList<string>? excludeSpecs,
        bool useCaseSensitiveFileNames,
        string currentDirectory,
        string? basePath = null)
    {
        var excludePattern = GetRegularExpressionForWildcard(
            excludeSpecs,
            Paths.CombinePaths(Paths.NormalizePath(currentDirectory), basePath),
            Usage.Exclude);
        var excludeRegex = excludePattern is not null ? GetRegexFromPattern(excludePattern, useCaseSensitiveFileNames) : null;
        if (excludeRegex is null) return false;
        if (excludeRegex.IsMatch(pathToCheck)) return true;
        return !Paths.HasExtension(pathToCheck) && excludeRegex.IsMatch(Paths.EnsureTrailingDirectorySeparator(pathToCheck));
    }

    // ported from: src/compiler/utilities.ts:9816-9819
    /// <summary>Compiles a pattern the way TS's <c>new RegExp(pattern, flags)</c> does.</summary>
    /// <remarks>
    /// PORT-NOTE: JS regular expressions ignore case without consulting the ambient culture, so
    /// <see cref="RegexOptions.CultureInvariant"/> is added to match. The remaining gap is the one already
    /// logged for case folding in general (see docs/architecture.md §10.2): JS folds cases the way the Unicode
    /// simple-fold table does, .NET uses the invariant ones, and they disagree on a few characters such as ß.
    /// </remarks>
    public static Regex GetRegexFromPattern(string pattern, bool useCaseSensitiveFileNames)
        => new(pattern, RegexOptions.CultureInvariant |
            (useCaseSensitiveFileNames ? RegexOptions.None : RegexOptions.IgnoreCase));

    // ported from: src/compiler/commandLineParser.ts:4040-4051
    /// <summary>
    /// Whether a spec contains a <c>/..</c> segment after a <c>**/</c> one.
    /// </summary>
    /// <remarks>
    /// We used to use the regex <c>/(^|\/)\*\*\/(.*\/)?\.\.($|\/)/</c> to check for this case, but in v8 that
    /// has polynomial performance because the recursive wildcard match — <c>**/</c> — can be matched in many
    /// arbitrary positions when multiple are present, resulting in bad backtracking (and we don't care which is
    /// matched — just that some <c>/..</c> segment comes after some <c>**/</c> segment).
    /// </remarks>
    public static bool InvalidDotDotAfterRecursiveWildcard(string s)
    {
        var wildcardIndex = Core.StartsWith(s, "**/") ? 0 : s.IndexOf("/**/", StringComparison.Ordinal);
        if (wildcardIndex == -1) return false;
        var lastDotIndex = Core.EndsWith(s, "/..") ? s.Length : s.LastIndexOf("/../", StringComparison.Ordinal);
        return lastDotIndex > wildcardIndex;
    }
}
