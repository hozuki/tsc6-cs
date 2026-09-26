using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/path.ts:1-1115
/// <summary>
/// Path manipulation. Internally paths are strings with '/' as the directory separator; hosts are
/// expected to handle that format when making system calls.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>Path</c> is <c>string &amp; { __pathBrand }</c>, a compile-time-only brand with no
/// runtime representation. The port uses plain <see cref="string"/>; where a TS signature says <c>Path</c>
/// the C# signature says <see cref="string"/>, and no call site can accidentally mix them in a way that
/// matters, because the two are the same runtime type.
/// <para>
/// PORT-NOTE: overloads that TS distinguishes by argument *type* (<c>string | readonly string[]</c>) become
/// separate overloads here, since C# has no union types.
/// </para>
/// </remarks>
internal static partial class Paths
{
    // ported from: src/compiler/path.ts:28-32
    public const string DirectorySeparator = "/";

    public const string AltDirectorySeparator = "\\";

    private const string UrlSchemeSeparator = "://";

    /// <summary>The identity of <c>Path</c> at runtime: the branded type collapses to <see cref="string"/>.</summary>
    [GeneratedRegex(@"\\")]
    private static partial Regex BackslashRegExp();

    // check path for these segments: '', '.'. '..'
    [GeneratedRegex(@"//|(?:^|/)\.\.?(?:$|/)")]
    private static partial Regex RelativePathSegmentRegExp();

    [GeneratedRegex(@"^\.\.?(?:$|[\\/])")]
    private static partial Regex PathIsRelativeRegExp();

    //// Path Tests

    // ported from: src/compiler/path.ts:41-43
    /// <summary>Determines whether a charCode corresponds to <c>/</c> or <c>\</c>.</summary>
    public static bool IsAnyDirectorySeparator(int charCode)
        => charCode == CharacterCodes.Slash || charCode == CharacterCodes.Backslash;

    // ported from: src/compiler/path.ts:50-52
    /// <summary>
    /// Determines whether a path starts with a URL scheme (e.g. starts with <c>http://</c>, <c>ftp://</c>,
    /// <c>file://</c>, etc.).
    /// </summary>
    public static bool IsUrl(string path) => GetEncodedRootLength(path) < 0;

    // ported from: src/compiler/path.ts:60-62
    /// <summary>
    /// Determines whether a path is an absolute disk path (e.g. starts with <c>/</c>, or a dos path like
    /// <c>c:</c>, <c>c:\</c> or <c>c:/</c>).
    /// </summary>
    public static bool IsRootedDiskPath(string path) => GetEncodedRootLength(path) > 0;

    // ported from: src/compiler/path.ts:69-72
    /// <summary>Determines whether a path consists only of a path root.</summary>
    public static bool IsDiskPathRoot(string path)
    {
        var rootLength = GetEncodedRootLength(path);
        return rootLength > 0 && rootLength == path.Length;
    }

    // ported from: src/compiler/path.ts:91-93
    /// <summary>
    /// Determines whether a path starts with an absolute path component (i.e. <c>/</c>, <c>c:/</c>,
    /// <c>file://</c>, etc.).
    /// </summary>
    public static bool PathIsAbsolute(string path) => GetEncodedRootLength(path) != 0;

    // ported from: src/compiler/path.ts:100-102
    /// <summary>Determines whether a path starts with a relative path component (i.e. <c>.</c> or <c>..</c>).</summary>
    public static bool PathIsRelative(string path) => PathIsRelativeRegExp().IsMatch(path);

    // ported from: src/compiler/path.ts:110-112
    /// <summary>
    /// Determines whether a path is neither relative nor absolute, e.g. "path/to/file".
    /// Also known misleadingly as "non-relative".
    /// </summary>
    public static bool PathIsBareSpecifier(string path) => !PathIsAbsolute(path) && !PathIsRelative(path);

    // ported from: src/compiler/path.ts:115-117
    public static bool HasExtension(string fileName) => GetBaseFileName(fileName).Contains('.');

    // ported from: src/compiler/path.ts:120-122
    public static bool FileExtensionIs(string path, string extension)
        => path.Length > extension.Length && Core.EndsWith(path, extension);

    // ported from: src/compiler/path.ts:125-133
    public static bool FileExtensionIsOneOf(string path, IList<string> extensions)
    {
        for (var i = 0; i < extensions.Count; i++)
        {
            if (FileExtensionIs(path, extensions[i]))
            {
                return true;
            }
        }

        return false;
    }

    // ported from: src/compiler/path.ts:140-142
    /// <summary>Determines whether a path has a trailing separator (<c>/</c> or <c>\</c>).</summary>
    public static bool HasTrailingDirectorySeparator(string path)
        => path.Length > 0 && IsAnyDirectorySeparator(path[path.Length - 1]);

    //// Path Parsing

    // ported from: src/compiler/path.ts:146-149
    private static bool IsVolumeCharacter(int charCode)
        => (charCode >= CharacterCodes.a && charCode <= CharacterCodes.z)
            || (charCode >= CharacterCodes.A && charCode <= CharacterCodes.Z);

    /// <summary>
    /// JS's <c>str.charCodeAt(i)</c>. For out-of-range indices JS yields <c>NaN</c>, which compares false
    /// against every real character code; C#'s indexer throws instead. Out-of-range reads therefore return
    /// -1, which (being outside the 0..0xFFFF range) compares the same way as <c>NaN</c> does here.
    /// </summary>
    private static int CharCodeAt(string s, int index)
        => index >= 0 && index < s.Length ? s[index] : -1;

    // ported from: src/compiler/path.ts:151-159
    private static int GetFileUrlVolumeSeparatorEnd(string url, int start)
    {
        var ch0 = CharCodeAt(url, start);
        if (ch0 == CharacterCodes.Colon) return start + 1;
        if (ch0 == CharacterCodes.Percent && CharCodeAt(url, start + 1) == CharacterCodes._3)
        {
            var ch2 = CharCodeAt(url, start + 2);
            if (ch2 == CharacterCodes.a || ch2 == CharacterCodes.A) return start + 3;
        }

        return -1;
    }

    // ported from: src/compiler/path.ts:165-221
    /// <summary>
    /// Returns length of the root part of a path or URL (i.e. length of "/", "x:/", "//server/share/",
    /// file:///user/files"). If the root is part of a URL, the twos-complement of the root length is returned.
    /// </summary>
    private static int GetEncodedRootLength(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        var ch0 = path[0];

        // POSIX or UNC
        if (ch0 == CharacterCodes.Slash || ch0 == CharacterCodes.Backslash)
        {
            if (CharCodeAt(path, 1) != ch0) return 1; // POSIX: "/" (or non-normalized "\")

            var p1 = path.IndexOf(
                ch0 == CharacterCodes.Slash ? DirectorySeparator : AltDirectorySeparator,
                2,
                StringComparison.Ordinal);
            if (p1 < 0) return path.Length; // UNC: "//server" or "\\server"

            return p1 + 1; // UNC: "//server/" or "\\server\"
        }

        // DOS
        if (IsVolumeCharacter(ch0) && CharCodeAt(path, 1) == CharacterCodes.Colon)
        {
            var ch2 = CharCodeAt(path, 2);
            if (ch2 == CharacterCodes.Slash || ch2 == CharacterCodes.Backslash) return 3; // DOS: "c:/" or "c:\"
            if (path.Length == 2) return 2; // DOS: "c:" (but not "c:d")
        }

        // URL
        var schemeEnd = path.IndexOf(UrlSchemeSeparator, StringComparison.Ordinal);
        if (schemeEnd != -1)
        {
            var authorityStart = schemeEnd + UrlSchemeSeparator.Length;
            var authorityEnd = path.IndexOf(DirectorySeparator, authorityStart, StringComparison.Ordinal);
            if (authorityEnd != -1)
            {
                // URL: "file:///", "file://server/", "file://server/path"
                // For local "file" URLs, include the leading DOS volume (if present).
                // Per https://www.ietf.org/rfc/rfc1738.txt, a host of "" or "localhost" is a
                // special case interpreted as "the machine from which the URL is being interpreted".
                var scheme = path[..schemeEnd];
                var authority = path[authorityStart..authorityEnd];
                if (
                    scheme == "file" && (authority == "" || authority == "localhost")
                    && IsVolumeCharacter(CharCodeAt(path, authorityEnd + 1))
                )
                {
                    var volumeSeparatorEnd = GetFileUrlVolumeSeparatorEnd(path, authorityEnd + 2);
                    if (volumeSeparatorEnd != -1)
                    {
                        if (CharCodeAt(path, volumeSeparatorEnd) == CharacterCodes.Slash)
                        {
                            // URL: "file:///c:/", "file://localhost/c:/", "file:///c%3a/", "file://localhost/c%3a/"
                            return ~(volumeSeparatorEnd + 1);
                        }

                        if (volumeSeparatorEnd == path.Length)
                        {
                            // URL: "file:///c:", "file://localhost/c:", "file:///c$3a", "file://localhost/c%3a"
                            // but not "file:///c:d" or "file:///c%3ad"
                            return ~volumeSeparatorEnd;
                        }
                    }
                }

                return ~(authorityEnd + 1); // URL: "file://server/", "http://server/"
            }

            return ~path.Length; // URL: "file://server", "http://server"
        }

        // relative
        return 0;
    }

    // ported from: src/compiler/path.ts:250-253
    /// <summary>
    /// Returns length of the root part of a path or URL (i.e. length of "/", "x:/", "//server/share/",
    /// file:///user/files").
    /// </summary>
    public static int GetRootLength(string path)
    {
        var rootLength = GetEncodedRootLength(path);
        return rootLength < 0 ? ~rootLength : rootLength;
    }

    // ported from: src/compiler/path.ts:312-323
    /// <summary>
    /// Returns the path except for its basename. Semantics align with NodeJS's <c>path.dirname</c>
    /// except that we support URLs as well.
    /// </summary>
    public static string GetDirectoryPath(string path)
    {
        path = NormalizeSlashes(path);

        // If the path provided is itself the root, then return it.
        var rootLength = GetRootLength(path);
        if (rootLength == path.Length) return path;

        // return the leading portion of the path up to the last (non-terminal) directory separator
        // but not including any trailing directory separator.
        path = RemoveTrailingDirectorySeparator(path);
        return path[..Math.Max(rootLength, path.LastIndexOf('/'))];
    }

    // ported from: src/compiler/path.ts:373-386
    /// <summary>
    /// Gets the portion of a path following the last (non-terminal) separator (<c>/</c>).
    /// Semantics align with NodeJS's <c>path.basename</c> except that we support URLs as well.
    /// If the base name has any one of the provided extensions, it is removed.
    /// </summary>
    public static string GetBaseFileName(string path) => GetBaseFileNameWorker(path, null, null);

    public static string GetBaseFileName(string path, string extension, bool ignoreCase)
        => GetBaseFileNameWorker(path, [extension], ignoreCase);

    public static string GetBaseFileName(string path, IList<string>? extensions, bool? ignoreCase)
        => GetBaseFileNameWorker(path, extensions, ignoreCase);

    private static string GetBaseFileNameWorker(string path, IList<string>? extensions, bool? ignoreCase)
    {
        path = NormalizeSlashes(path);

        // if the path provided is itself the root, then it has not file name.
        var rootLength = GetRootLength(path);
        if (rootLength == path.Length) return "";

        // return the trailing portion of the path starting after the last (non-terminal) directory
        // separator but not including any trailing directory separator.
        path = RemoveTrailingDirectorySeparator(path);
        var name = path[Math.Max(GetRootLength(path), path.LastIndexOf('/') + 1)..];
        var extension = extensions is not null && ignoreCase is not null
            ? GetAnyExtensionFromPath(name, extensions, ignoreCase.Value)
            : null;
        return !string.IsNullOrEmpty(extension) ? name[..(name.Length - extension.Length)] : name;
    }

    // ported from: src/compiler/path.ts:388-396
    private static string? TryGetExtensionFromPath(string path, string extension, EqualityComparer<string?> stringEqualityComparer)
    {
        if (!Core.StartsWith(extension, ".")) extension = "." + extension;
        if (path.Length >= extension.Length && path[path.Length - extension.Length] == CharacterCodes.Dot)
        {
            var pathExtension = path[(path.Length - extension.Length)..];
            if (stringEqualityComparer(pathExtension, extension))
            {
                return pathExtension;
            }
        }

        return null;
    }

    // ported from: src/compiler/path.ts:398-407
    private static string GetAnyExtensionFromPathWorker(string path, IList<string> extensions, EqualityComparer<string?> stringEqualityComparer)
    {
        for (var i = 0; i < extensions.Count; i++)
        {
            var result = TryGetExtensionFromPath(path, extensions[i], stringEqualityComparer);
            if (result is not null) return result;
        }

        return "";
    }

    // ported from: src/compiler/path.ts:435-447
    /// <summary>
    /// Gets the file extension for a path.
    /// <code>
    /// getAnyExtensionFromPath("/path/to/file.ext") == ".ext"
    /// getAnyExtensionFromPath("/path/to/file.ext/") == ".ext"
    /// getAnyExtensionFromPath("/path/to/file") == ""
    /// getAnyExtensionFromPath("/path/to.ext/file") == ""
    /// </code>
    /// </summary>
    public static string GetAnyExtensionFromPath(string path)
    {
        // Retrieves any string from the final "." onwards from a base file name.
        // Unlike extensionFromPath, which throws an exception on unrecognized extensions.
        var baseFileName = GetBaseFileName(path);
        var extensionIndex = baseFileName.LastIndexOf('.');
        return extensionIndex >= 0 ? baseFileName[extensionIndex..] : "";
    }

    /// <summary>
    /// Gets the file extension for a path, provided it is one of the provided extensions.
    /// </summary>
    public static string GetAnyExtensionFromPath(string path, string extension, bool ignoreCase)
        => GetAnyExtensionFromPath(path, [extension], ignoreCase);

    public static string GetAnyExtensionFromPath(string path, IList<string> extensions, bool ignoreCase)
        => GetAnyExtensionFromPathWorker(
            RemoveTrailingDirectorySeparator(path),
            extensions,
            ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive);

    // ported from: src/compiler/path.ts:449-454
    private static List<string> PathComponents(string path, int rootLength)
    {
        var root = path[..rootLength];
        var rest = path[rootLength..].Split('/');
        if (rest.Length != 0 && string.IsNullOrEmpty(rest[^1])) rest = rest[..^1];
        var result = new List<string>(rest.Length + 1) { root };
        result.AddRange(rest);
        return result;
    }

    // ported from: src/compiler/path.ts:495-498
    /// <summary>
    /// Parse a path into an array containing a root component (at index 0) and zero or more path
    /// components (at indices &gt; 0). The result is not normalized. If the path is relative, the root
    /// component is <c>""</c>. If the path is absolute, the root component includes the first path
    /// separator (<c>/</c>).
    /// </summary>
    public static List<string> GetPathComponents(string path, string currentDirectory = "")
    {
        path = CombinePaths(currentDirectory, path);
        return PathComponents(path, GetRootLength(path));
    }

    // ported from: src/compiler/path.ts:512-517
    /// <summary>
    /// Formats a parsed path consisting of a root component (at index 0) and zero or more path segments
    /// (at indices &gt; 0).
    /// </summary>
    public static string GetPathFromPathComponents(IList<string> pathComponents, int? length = null)
    {
        if (pathComponents.Count == 0) return "";

        var root = !string.IsNullOrEmpty(pathComponents[0]) ? EnsureTrailingDirectorySeparator(pathComponents[0]) : "";
        var end = length ?? pathComponents.Count;
        var rest = new List<string>(Math.Max(0, end - 1));
        for (var i = 1; i < end; i++)
        {
            rest.Add(pathComponents[i]);
        }

        return root + string.Join(DirectorySeparator, rest);
    }

    //// Path Normalization

    // ported from: src/compiler/path.ts:526-530
    /// <summary>Normalize path separators, converting <c>\</c> into <c>/</c>.</summary>
    public static string NormalizeSlashes(string path)
        => path.Contains('\\') ? BackslashRegExp().Replace(path, DirectorySeparator) : path;

    // ported from: src/compiler/path.ts:538-557
    /// <summary>
    /// Reduce an array of path components to a more simplified path by navigating any <c>"."</c> or
    /// <c>".."</c> entries in the path.
    /// </summary>
    public static List<string> ReducePathComponents(IList<string> components)
    {
        if (!Core.Some(components)) return [];
        var reduced = new List<string> { components[0] };
        for (var i = 1; i < components.Count; i++)
        {
            var component = components[i];
            if (string.IsNullOrEmpty(component)) continue;
            if (component == ".") continue;
            if (component == "..")
            {
                if (reduced.Count > 1)
                {
                    if (reduced[^1] != "..")
                    {
                        reduced.RemoveAt(reduced.Count - 1);
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(reduced[0]))
                {
                    continue;
                }
            }

            reduced.Add(component);
        }

        return reduced;
    }

    // ported from: src/compiler/path.ts:579-592
    /// <summary>
    /// Combines paths. If a path is absolute, it replaces any previous path. Relative paths are not
    /// simplified.
    /// </summary>
    public static string CombinePaths(string path, params string?[] paths)
    {
        if (!string.IsNullOrEmpty(path)) path = NormalizeSlashes(path);
        for (var i = 0; i < paths.Length; i++)
        {
            var relativePath = paths[i];
            if (string.IsNullOrEmpty(relativePath)) continue;
            relativePath = NormalizeSlashes(relativePath);
            if (string.IsNullOrEmpty(path) || GetRootLength(relativePath) != 0)
            {
                path = relativePath;
            }
            else
            {
                path = EnsureTrailingDirectorySeparator(path) + relativePath;
            }
        }

        return path;
    }

    // ported from: src/compiler/path.ts:606-608
    /// <summary>
    /// Combines and resolves paths. If a path is absolute, it replaces any previous path. Any <c>.</c>
    /// and <c>..</c> path components are resolved. Trailing directory separators are preserved.
    /// </summary>
    public static string ResolvePath(string path, params string?[] paths)
        => NormalizePath(Core.Some(paths) ? CombinePaths(path, paths) : NormalizeSlashes(path));

    // ported from: src/compiler/path.ts:622-624
    /// <summary>
    /// Parse a path into an array containing a root component (at index 0) and zero or more path
    /// components (at indices &gt; 0). The result is normalized.
    /// </summary>
    public static List<string> GetNormalizedPathComponents(string path, string? currentDirectory)
        => ReducePathComponents(GetPathComponents(path, currentDirectory ?? ""));

    // ported from: src/compiler/path.ts:627-719
    public static string GetNormalizedAbsolutePath(string path, string? currentDirectory)
    {
        var rootLength = GetRootLength(path);
        if (rootLength == 0 && !string.IsNullOrEmpty(currentDirectory))
        {
            path = CombinePaths(currentDirectory, path);
            rootLength = GetRootLength(path);
        }
        else
        {
            // combinePaths normalizes slashes, so not necessary in the other branch
            path = NormalizeSlashes(path);
        }

        var simpleNormalized = SimpleNormalizePath(path);
        if (simpleNormalized is not null)
        {
            return simpleNormalized.Length > rootLength ? RemoveTrailingDirectorySeparator(simpleNormalized) : simpleNormalized;
        }

        var length = path.Length;
        var root = path[..rootLength];
        // `normalized` is only initialized once `path` is determined to be non-normalized
        string? normalized = null;
        var index = rootLength;
        var segmentStart = index;
        var normalizedUpTo = index;
        var seenNonDotDotSegment = rootLength != 0;
        while (index < length)
        {
            // At beginning of segment
            segmentStart = index;
            var ch = path[index];
            while (ch == CharacterCodes.Slash && index + 1 < length)
            {
                index++;
                ch = path[index];
            }

            if (index > segmentStart)
            {
                // Seen superfluous separator
                normalized ??= path[..(segmentStart - 1)];
                segmentStart = index;
            }

            // Past any superfluous separators
            var segmentEnd = path.IndexOf('/', index + 1);
            if (segmentEnd == -1)
            {
                segmentEnd = length;
            }

            var segmentLength = segmentEnd - segmentStart;
            if (segmentLength == 1 && path[index] == CharacterCodes.Dot)
            {
                // "." segment (skip)
                normalized ??= path[..normalizedUpTo];
            }
            else if (segmentLength == 2 && path[index] == CharacterCodes.Dot && path[index + 1] == CharacterCodes.Dot)
            {
                // ".." segment
                if (!seenNonDotDotSegment)
                {
                    if (normalized is not null)
                    {
                        normalized += normalized.Length == rootLength ? ".." : "/..";
                    }
                    else
                    {
                        normalizedUpTo = index + 2;
                    }
                }
                else if (normalized is null)
                {
                    if (normalizedUpTo - 2 >= 0)
                    {
                        normalized = path[..Math.Max(rootLength, path.LastIndexOf('/', normalizedUpTo - 2))];
                    }
                    else
                    {
                        normalized = path[..normalizedUpTo];
                    }
                }
                else
                {
                    var lastSlash = normalized.LastIndexOf('/');
                    normalized = lastSlash != -1
                        ? normalized[..Math.Max(rootLength, lastSlash)]
                        : root;
                    if (normalized.Length == rootLength)
                    {
                        seenNonDotDotSegment = rootLength != 0;
                    }
                }
            }
            else if (normalized is not null)
            {
                if (normalized.Length != rootLength)
                {
                    normalized += DirectorySeparator;
                }

                seenNonDotDotSegment = true;
                normalized += path[segmentStart..segmentEnd];
            }
            else
            {
                seenNonDotDotSegment = true;
                normalizedUpTo = segmentEnd;
            }

            index = segmentEnd + 1;
        }

        return normalized ?? (length > rootLength ? RemoveTrailingDirectorySeparator(path) : path);
    }

    // ported from: src/compiler/path.ts:722-730
    public static string NormalizePath(string path)
    {
        path = NormalizeSlashes(path);
        var normalized = SimpleNormalizePath(path);
        if (normalized is not null)
        {
            return normalized;
        }

        normalized = GetNormalizedAbsolutePath(path, "");
        return normalized.Length != 0 && HasTrailingDirectorySeparator(path) ? EnsureTrailingDirectorySeparator(normalized) : normalized;
    }

    // ported from: src/compiler/path.ts:732-749
    private static string? SimpleNormalizePath(string path)
    {
        // Most paths don't require normalization
        if (!RelativePathSegmentRegExp().IsMatch(path))
        {
            return path;
        }

        // Some paths only require cleanup of `/./` or leading `./`
        var simplified = path.Replace("/./", "/");
        if (simplified.StartsWith("./", StringComparison.Ordinal))
        {
            simplified = simplified[2..];
        }

        if (simplified != path)
        {
            path = simplified;
            if (!RelativePathSegmentRegExp().IsMatch(path))
            {
                return path;
            }
        }

        return null;
    }

    // ported from: src/compiler/path.ts:751-754
    private static string GetPathWithoutRoot(IList<string> pathComponents)
        => pathComponents.Count == 0 ? "" : string.Join(DirectorySeparator, pathComponents.Skip(1));

    // ported from: src/compiler/path.ts:757-759
    public static string GetNormalizedAbsolutePathWithoutRoot(string fileName, string? currentDirectory)
        => GetPathWithoutRoot(GetNormalizedPathComponents(fileName, currentDirectory));

    // ported from: src/compiler/path.ts:762-767
    public static string ToPath(string fileName, string? basePath, Core.GetCanonicalFileName getCanonicalFileName)
    {
        var nonCanonicalizedPath = IsRootedDiskPath(fileName)
            ? NormalizePath(fileName)
            : GetNormalizedAbsolutePath(fileName, basePath);
        return getCanonicalFileName(nonCanonicalizedPath);
    }

    //// Path Mutation

    // ported from: src/compiler/path.ts:785-791
    /// <summary>Removes a trailing directory separator from a path, if it does not already have one.</summary>
    public static string RemoveTrailingDirectorySeparator(string path)
        => HasTrailingDirectorySeparator(path) ? path[..(path.Length - 1)] : path;

    // ported from: src/compiler/path.ts:807-813
    /// <summary>Adds a trailing directory separator to a path, if it does not already have one.</summary>
    public static string EnsureTrailingDirectorySeparator(string path)
        => !HasTrailingDirectorySeparator(path) ? path + DirectorySeparator : path;

    // ported from: src/compiler/path.ts:828-830
    /// <summary>
    /// Ensures a path is either absolute (prefixed with <c>/</c> or <c>c:</c>) or dot-relative (prefixed
    /// with <c>./</c> or <c>../</c>) so as not to be confused with an unprefixed module name.
    /// </summary>
    public static string EnsurePathIsNonModuleName(string path)
        => !PathIsAbsolute(path) && !PathIsRelative(path) ? "./" + path : path;

    // ported from: src/compiler/path.ts:855-858
    /// <summary>Changes the extension of a path to the provided extension.</summary>
    public static string ChangeAnyExtension(string path, string ext)
    {
        var pathext = GetAnyExtensionFromPath(path);
        return pathext.Length != 0
            ? path[..(path.Length - pathext.Length)] + (Core.StartsWith(ext, ".") ? ext : "." + ext)
            : path;
    }

    /// <summary>
    /// Changes the extension of a path to the provided extension if it has one of the provided extensions.
    /// </summary>
    public static string ChangeAnyExtension(string path, string ext, string extension, bool ignoreCase)
        => ChangeAnyExtension(path, ext, [extension], ignoreCase);

    public static string ChangeAnyExtension(string path, string ext, IList<string> extensions, bool ignoreCase)
    {
        var pathext = GetAnyExtensionFromPath(path, extensions, ignoreCase);
        return pathext.Length != 0
            ? path[..(path.Length - pathext.Length)] + (Core.StartsWith(ext, ".") ? ext : "." + ext)
            : path;
    }

    // ported from: src/compiler/path.ts:870-877
    /// <summary>
    /// Like <see cref="ChangeAnyExtension(string, string)"/>, but declaration file extensions are
    /// recognized and replaced starting from the <c>.d</c>.
    /// <code>
    /// changeAnyExtension("file.d.ts", ".js") == "file.d.js"
    /// changeFullExtension("file.d.ts", ".js") == "file.js"
    /// </code>
    /// </summary>
    public static string ChangeFullExtension(string path, string newExtension)
    {
        var declarationExtension = GetDeclarationFileExtension(path);
        if (!string.IsNullOrEmpty(declarationExtension))
        {
            return path[..(path.Length - declarationExtension.Length)]
                + (Core.StartsWith(newExtension, ".") ? newExtension : "." + newExtension);
        }

        return ChangeAnyExtension(path, newExtension);
    }

    //// Path Comparisons

    // ported from: src/compiler/path.ts:884-918
    private static Comparison ComparePathsWorker(string a, string b, Comparer<string?> componentComparer)
    {
        if (a == b) return Comparison.EqualTo;
        if (a is null) return Comparison.LessThan;
        if (b is null) return Comparison.GreaterThan;

        // NOTE: Performance optimization - shortcut if the root segments differ as there would be no
        //       need to perform path reduction.
        var aRoot = a[..GetRootLength(a)];
        var bRoot = b[..GetRootLength(b)];
        var result = Core.CompareStringsCaseInsensitive(aRoot, bRoot);
        if (result != Comparison.EqualTo)
        {
            return result;
        }

        // NOTE: Performance optimization - shortcut if there are no relative path segments in
        //       the non-root portion of the path
        var aRest = a[aRoot.Length..];
        var bRest = b[bRoot.Length..];
        if (!RelativePathSegmentRegExp().IsMatch(aRest) && !RelativePathSegmentRegExp().IsMatch(bRest))
        {
            return componentComparer(aRest, bRest);
        }

        // The path contains a relative path segment. Normalize the paths and perform a slower component
        // by component comparison.
        var aComponents = ReducePathComponents(GetPathComponents(a));
        var bComponents = ReducePathComponents(GetPathComponents(b));
        var sharedLength = Math.Min(aComponents.Count, bComponents.Count);
        for (var i = 1; i < sharedLength; i++)
        {
            var componentResult = componentComparer(aComponents[i], bComponents[i]);
            if (componentResult != Comparison.EqualTo)
            {
                return componentResult;
            }
        }

        return Core.CompareValues(aComponents.Count, bComponents.Count);
    }

    // ported from: src/compiler/path.ts:925-927
    /// <summary>
    /// Performs a case-sensitive comparison of two paths. Path roots are always compared case-insensitively.
    /// </summary>
    public static Comparison ComparePathsCaseSensitive(string a, string b)
        => ComparePathsWorker(a, b, Core.CompareStringsCaseSensitive);

    // ported from: src/compiler/path.ts:934-936
    /// <summary>Performs a case-insensitive comparison of two paths.</summary>
    public static Comparison ComparePathsCaseInsensitive(string a, string b)
        => ComparePathsWorker(a, b, Core.CompareStringsCaseInsensitive);

    // ported from: src/compiler/path.ts:947-956
    /// <summary>Compare two paths using the provided case sensitivity.</summary>
    public static Comparison ComparePaths(string a, string b, bool ignoreCase = false)
        => ComparePathsWorker(a, b, Core.GetStringComparer(ignoreCase));

    public static Comparison ComparePaths(string a, string b, string? currentDirectory, bool ignoreCase = false)
    {
        if (currentDirectory is not null)
        {
            a = CombinePaths(currentDirectory, a);
            b = CombinePaths(currentDirectory, b);
        }

        return ComparePathsWorker(a, b, Core.GetStringComparer(ignoreCase));
    }

    // ported from: src/compiler/path.ts:967-992
    /// <summary>
    /// Determines whether a <paramref name="parent"/> path contains a <paramref name="child"/> path using
    /// the provided case sensitivity.
    /// </summary>
    public static bool ContainsPath(string parent, string child, bool ignoreCase = false)
    {
        if (parent is null || child is null) return false;
        if (parent == child) return true;
        var parentComponents = ReducePathComponents(GetPathComponents(parent));
        var childComponents = ReducePathComponents(GetPathComponents(child));
        if (childComponents.Count < parentComponents.Count)
        {
            return false;
        }

        EqualityComparer<string?> componentEqualityComparer = ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive;
        for (var i = 0; i < parentComponents.Count; i++)
        {
            EqualityComparer<string?> equalityComparer = i == 0 ? Core.EquateStringsCaseInsensitive : componentEqualityComparer;
            if (!equalityComparer(parentComponents[i], childComponents[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool ContainsPath(string parent, string child, string? currentDirectory, bool ignoreCase = false)
    {
        if (currentDirectory is not null)
        {
            parent = CombinePaths(currentDirectory, parent);
            child = CombinePaths(currentDirectory, child);
        }

        return ContainsPath(parent, child, ignoreCase);
    }

    // ported from: src/compiler/path.ts:1002-1006
    /// <summary>
    /// Determines whether <paramref name="fileName"/> starts with the specified
    /// <paramref name="directoryName"/> using the provided path canonicalization callback. Comparison is
    /// case-sensitive between the canonical paths.
    /// </summary>
    public static bool StartsWithDirectory(string fileName, string directoryName, Core.GetCanonicalFileName getCanonicalFileName)
    {
        var canonicalFileName = getCanonicalFileName(fileName);
        var canonicalDirectoryName = getCanonicalFileName(directoryName);
        return Core.StartsWith(canonicalFileName, canonicalDirectoryName + "/")
            || Core.StartsWith(canonicalFileName, canonicalDirectoryName + "\\");
    }

    //// Relative Paths

    // ported from: src/compiler/path.ts:1010-1032
    private static List<string> GetPathComponentsRelativeTo(
        string from,
        string to,
        EqualityComparer<string?> stringEqualityComparer,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        var fromComponents = ReducePathComponents(GetPathComponents(from));
        var toComponents = ReducePathComponents(GetPathComponents(to));

        int start;
        for (start = 0; start < fromComponents.Count && start < toComponents.Count; start++)
        {
            var fromComponent = getCanonicalFileName(fromComponents[start]);
            var toComponent = getCanonicalFileName(toComponents[start]);
            EqualityComparer<string?> comparer = start == 0 ? Core.EquateStringsCaseInsensitive : stringEqualityComparer;
            if (!comparer(fromComponent, toComponent)) break;
        }

        if (start == 0)
        {
            return toComponents;
        }

        var components = toComponents.GetRange(start, toComponents.Count - start);
        var relative = new List<string>();
        for (; start < fromComponents.Count; start++)
        {
            relative.Add("..");
        }

        var result = new List<string> { "" };
        result.AddRange(relative);
        result.AddRange(components);
        return result;
    }

    // ported from: src/compiler/path.ts:1047-1053
    /// <summary>Gets a relative path that can be used to traverse between <c>from</c> and <c>to</c>.</summary>
    public static string GetRelativePathFromDirectory(string from, string to, bool ignoreCase)
        => GetRelativePathFromDirectory(from, to, ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive, Core.IdentityFileName);

    public static string GetRelativePathFromDirectory(string fromDirectory, string to, Core.GetCanonicalFileName getCanonicalFileName)
        => GetRelativePathFromDirectory(fromDirectory, to, Core.EquateStringsCaseSensitive, getCanonicalFileName);

    private static string GetRelativePathFromDirectory(
        string fromDirectory,
        string to,
        EqualityComparer<string?> stringEqualityComparer,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        Debug.Assert((GetRootLength(fromDirectory) > 0) == (GetRootLength(to) > 0), "Paths must either both be absolute or both be relative");
        var pathComponents = GetPathComponentsRelativeTo(fromDirectory, to, stringEqualityComparer, getCanonicalFileName);
        return GetPathFromPathComponents(pathComponents);
    }

    // ported from: src/compiler/path.ts:1056-1060
    public static string ConvertToRelativePath(string absoluteOrRelativePath, string basePath, Core.GetCanonicalFileName getCanonicalFileName)
        => !IsRootedDiskPath(absoluteOrRelativePath)
            ? absoluteOrRelativePath
            : GetRelativePathToDirectoryOrUrl(basePath, absoluteOrRelativePath, basePath, getCanonicalFileName, /*isAbsolutePathAnUrl*/ false);

    // ported from: src/compiler/path.ts:1063-1065
    public static string GetRelativePathFromFile(string from, string to, Core.GetCanonicalFileName getCanonicalFileName)
        => EnsurePathIsNonModuleName(GetRelativePathFromDirectory(GetDirectoryPath(from), to, getCanonicalFileName));

    // ported from: src/compiler/path.ts:1068-1083
    public static string GetRelativePathToDirectoryOrUrl(
        string directoryPathOrUrl,
        string relativeOrAbsolutePath,
        string currentDirectory,
        Core.GetCanonicalFileName getCanonicalFileName,
        bool isAbsolutePathAnUrl)
    {
        var pathComponents = GetPathComponentsRelativeTo(
            ResolvePath(currentDirectory, directoryPathOrUrl),
            ResolvePath(currentDirectory, relativeOrAbsolutePath),
            Core.EquateStringsCaseSensitive,
            getCanonicalFileName);

        var firstComponent = pathComponents[0];
        if (isAbsolutePathAnUrl && IsRootedDiskPath(firstComponent))
        {
            var prefix = firstComponent[0] == CharacterCodes.Slash ? "file://" : "file:///";
            pathComponents[0] = prefix + firstComponent;
        }

        return GetPathFromPathComponents(pathComponents);
    }

    //// Path Traversal

    // ported from: src/compiler/path.ts:1096-1110
    /// <summary>
    /// Calls <paramref name="callback"/> on <paramref name="directory"/> and every ancestor directory it
    /// has, returning the first defined result.
    /// </summary>
    public static T? ForEachAncestorDirectory<T>(string directory, Func<string, T?> callback)
        where T : class
    {
        while (true)
        {
            var result = callback(directory);
            if (result is not null)
            {
                return result;
            }

            var parentPath = GetDirectoryPath(directory);
            if (parentPath == directory)
            {
                return null;
            }

            directory = parentPath;
        }
    }

    // ported from: src/compiler/path.ts:1113-1115
    public static bool IsNodeModulesDirectory(string dirPath) => Core.EndsWith(dirPath, "/node_modules");

    //// Declaration files

    // ported from: src/compiler/utilities.ts:9982 (supportedDeclarationExtensions)
    // and src/compiler/parser.ts:10561-10574 (getDeclarationFileExtension).
    // PORT-NOTE: TS declares these above Layer 0 (`utilities.ts` and `parser.ts`). They are pure string
    // helpers over `Extension`, and `path.ts` (Layer 0) already imports `getDeclarationFileExtension`, so
    // the port lands them here rather than introducing an upward dependency or a stub.
    // PORT-NOTE: TS types these as `readonly Extension[]` and passes them where `readonly string[]` is
    // expected, which works because a string-enum member is assignable to `string`. The port's Extension
    // is a struct, so the values are unwrapped explicitly here.
    private static readonly string[] s_supportedDeclarationExtensions =
        [Extension.Dts.Value, Extension.Dcts.Value, Extension.Dmts.Value];

    public static string? GetDeclarationFileExtension(string fileName)
    {
        var standardExtension = GetAnyExtensionFromPath(fileName, s_supportedDeclarationExtensions, /*ignoreCase*/ false);
        if (standardExtension.Length != 0)
        {
            return standardExtension;
        }

        if (FileExtensionIs(fileName, Extension.Ts.Value))
        {
            var baseName = GetBaseFileName(fileName);
            var index = baseName.LastIndexOf(".d.", StringComparison.Ordinal);
            if (index >= 0)
            {
                return baseName[index..];
            }
        }

        return null;
    }
}
