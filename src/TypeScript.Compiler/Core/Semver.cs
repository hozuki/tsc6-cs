using System.Globalization;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/semver.ts:1-451
/// <summary>
/// A precise semantic version number, https://semver.org.
/// </summary>
/// <remarks>
/// PORT-NOTE: this type shadows <see cref="System.Version"/> inside this namespace. TS calls it
/// <c>Version</c> and the compiler never needs the BCL type, so the name is kept for call-site parity;
/// spell out <c>System.Version</c> if a future need arises.
/// </remarks>
internal sealed class Version
{
    // https://semver.org/#spec-item-2
    // > A normal version number MUST take the form X.Y.Z where X, Y, and Z are non-negative
    // > integers, and MUST NOT contain leading zeroes. [...]
    //
    // NOTE: We differ here in that we allow X and X.Y, with missing parts having the default value of `0`.
    private static readonly Regex s_versionRegExp = new(
        @"^(0|[1-9]\d*)(?:\.(0|[1-9]\d*)(?:\.(0|[1-9]\d*)(?:-([a-z0-9-.]+))?(?:\+([a-z0-9-.]+))?)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // https://semver.org/#spec-item-9
    private static readonly Regex s_prereleaseRegExp = new(
        @"^(?:0|[1-9]\d*|[a-z-][a-z0-9-]*)(?:\.(?:0|[1-9]\d*|[a-z-][a-z0-9-]*))*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_prereleasePartRegExp = new(
        @"^(?:0|[1-9]\d*|[a-z-][a-z0-9-]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // https://semver.org/#spec-item-10
    private static readonly Regex s_buildRegExp = new(
        @"^[a-z0-9-]+(?:\.[a-z0-9-]+)*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_buildPartRegExp = new(
        @"^[a-z0-9-]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // https://semver.org/#spec-item-9
    // > Numeric identifiers MUST NOT include leading zeroes.
    private static readonly Regex s_numericIdentifierRegExp = new(@"^(?:0|[1-9]\d*)$", RegexOptions.Compiled);

    public static readonly Version Zero = new(0, 0, 0, "0", null);

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> Prerelease { get; }

    public IReadOnlyList<string> Build { get; }

    public Version(string text)
        : this(Debug.CheckDefined(TryParseComponents(text), "Invalid version"))
    {
    }

    private Version(VersionComponents components)
        : this(components.Major, components.Minor, components.Patch, components.Prerelease, components.Build)
    {
    }

    public Version(int major, int minor = 0, int patch = 0, IReadOnlyList<string>? prerelease = null, IReadOnlyList<string>? build = null)
    {
        Debug.Assert(major >= 0, "Invalid argument: major");
        Debug.Assert(minor >= 0, "Invalid argument: minor");
        Debug.Assert(patch >= 0, "Invalid argument: patch");

        var prereleaseArray = prerelease is { Count: > 0 } ? prerelease : [];
        var buildArray = build is { Count: > 0 } ? build : [];

        Debug.Assert(Core.Every(prereleaseArray, static (s, _) => s_prereleasePartRegExp.IsMatch(s)), "Invalid argument: prerelease");
        Debug.Assert(Core.Every(buildArray, static (s, _) => s_buildPartRegExp.IsMatch(s)), "Invalid argument: build");

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prereleaseArray;
        Build = buildArray;
    }

    /// <summary>TS's <c>prerelease: string | readonly string[]</c> overload, taking a dot-separated string.</summary>
    public Version(int major, int minor, int patch, string? prerelease, string? build)
        : this(major, minor, patch, SplitOrEmpty(prerelease), SplitOrEmpty(build))
    {
    }

    private static IReadOnlyList<string> SplitOrEmpty(string? parts)
        => string.IsNullOrEmpty(parts) ? [] : parts.Split('.');

    public static Version? TryParse(string text)
    {
        var result = TryParseComponents(text);
        if (result is null) return null;

        var c = result.Value;
        return new Version(c.Major, c.Minor, c.Patch, c.Prerelease, c.Build);
    }

    // https://semver.org/#spec-item-11
    // > Precedence is determined by the first difference when comparing each of these identifiers from
    // > left to right as follows: Major, minor, and patch versions are always compared numerically.
    // > Precedence for two pre-release versions with the same major, minor, and patch version MUST be
    // > determined by comparing each dot separated identifier from left to right until a difference is
    // > found [...]
    // > Build metadata does not figure into precedence
    public Comparison CompareTo(Version? other)
    {
        if (ReferenceEquals(this, other)) return Comparison.EqualTo;
        if (other is null) return Comparison.GreaterThan;

        var major = Core.CompareValues(Major, other.Major);
        if (major != Comparison.EqualTo) return major;
        var minor = Core.CompareValues(Minor, other.Minor);
        if (minor != Comparison.EqualTo) return minor;
        var patch = Core.CompareValues(Patch, other.Patch);
        if (patch != Comparison.EqualTo) return patch;
        return ComparePrereleaseIdentifiers(Prerelease, other.Prerelease);
    }

    public Version Increment(VersionField field) => field switch
    {
        VersionField.Major => new Version(Major + 1, 0, 0),
        VersionField.Minor => new Version(Major, Minor + 1, 0),
        VersionField.Patch => new Version(Major, Minor, Patch + 1),
        _ => Debug.Fail<Version>($"Illegal value: {field}"),
    };

    public Version With(int? major = null, int? minor = null, int? patch = null, string? prerelease = null, string? build = null)
        => new(
            major ?? Major,
            minor ?? Minor,
            patch ?? Patch,
            prerelease ?? string.Join(".", Prerelease),
            build ?? string.Join(".", Build));

    public override string ToString()
    {
        var result = $"{Major}.{Minor}.{Patch}";
        if (Core.Some(Prerelease)) result += $"-{string.Join(".", Prerelease)}";
        if (Core.Some(Build)) result += $"+{string.Join(".", Build)}";
        return result;
    }

    private static VersionComponents? TryParseComponents(string text)
    {
        var match = s_versionRegExp.Match(text);
        if (!match.Success) return null;

        var major = match.Groups[1].Value;
        var minor = match.Groups[2].Success ? match.Groups[2].Value : "0";
        var patch = match.Groups[3].Success ? match.Groups[3].Value : "0";
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : "";
        var build = match.Groups[5].Success ? match.Groups[5].Value : "";

        if (prerelease.Length != 0 && !s_prereleaseRegExp.IsMatch(prerelease)) return null;
        if (build.Length != 0 && !s_buildRegExp.IsMatch(build)) return null;

        return new VersionComponents(
            int.Parse(major, CultureInfo.InvariantCulture),
            int.Parse(minor, CultureInfo.InvariantCulture),
            int.Parse(patch, CultureInfo.InvariantCulture),
            prerelease,
            build);
    }

    // https://semver.org/#spec-item-11
    // > When major, minor, and patch are equal, a pre-release version has lower precedence than a
    // > normal version.
    private static Comparison ComparePrereleaseIdentifiers(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (ReferenceEquals(left, right)) return Comparison.EqualTo;
        if (left.Count == 0) return right.Count == 0 ? Comparison.EqualTo : Comparison.GreaterThan;
        if (right.Count == 0) return Comparison.LessThan;

        // > Precedence for two pre-release versions with the same major, minor, and patch version MUST
        // > be determined by comparing each dot separated identifier from left to right until a
        // > difference is found [...]
        var length = Math.Min(left.Count, right.Count);
        for (var i = 0; i < length; i++)
        {
            var leftIdentifier = left[i];
            var rightIdentifier = right[i];
            if (leftIdentifier == rightIdentifier) continue;

            var leftIsNumeric = s_numericIdentifierRegExp.IsMatch(leftIdentifier);
            var rightIsNumeric = s_numericIdentifierRegExp.IsMatch(rightIdentifier);
            if (leftIsNumeric || rightIsNumeric)
            {
                // > Numeric identifiers always have lower precedence than non-numeric identifiers.
                if (leftIsNumeric != rightIsNumeric) return leftIsNumeric ? Comparison.LessThan : Comparison.GreaterThan;

                // > identifiers consisting of only digits are compared numerically
                var numericResult = Core.CompareValues(
                    int.Parse(leftIdentifier, CultureInfo.InvariantCulture),
                    int.Parse(rightIdentifier, CultureInfo.InvariantCulture));
                if (numericResult != Comparison.EqualTo) return numericResult;
            }
            else
            {
                // > identifiers with letters or hyphens are compared lexically in ASCII sort order.
                var lexicalResult = Core.CompareStringsCaseSensitive(leftIdentifier, rightIdentifier);
                if (lexicalResult != Comparison.EqualTo) return lexicalResult;
            }
        }

        // > A larger set of pre-release fields has a higher precedence than a smaller set, if all of
        // > the preceding identifiers are equal.
        return Core.CompareValues(left.Count, right.Count);
    }

    private readonly record struct VersionComponents(int Major, int Minor, int Patch, string Prerelease, string Build);
}

/// <summary>
/// The field <see cref="Version.Increment"/> advances, corresponding to TS's <c>"major" | "minor" | "patch"</c>.
/// </summary>
internal enum VersionField
{
    Major,
    Minor,
    Patch,
}

/// <summary>
/// A semantic version range, per https://github.com/npm/node-semver#ranges.
/// </summary>
internal sealed class VersionRange
{
    // https://github.com/npm/node-semver#range-grammar
    // range-set    ::= range ( logical-or range ) *
    private static readonly Regex s_logicalOrRegExp = new(@"\|\|", RegexOptions.Compiled);
    private static readonly Regex s_whitespaceRegExp = new(@"\s+", RegexOptions.Compiled);

    // partial      ::= xr ( '.' xr ( '.' xr qualifier ? )? )?
    // xr           ::= 'x' | 'X' | '*' | nr
    // nr           ::= '0' | ['1'-'9'] ( ['0'-'9'] ) *
    // qualifier    ::= ( '-' pre )? ( '+' build )?
    // pre          ::= parts
    // build        ::= parts
    // parts        ::= part ( '.' part ) *
    // part         ::= nr | [-0-9A-Za-z]+
    private static readonly Regex s_partialRegExp = new(
        @"^([x*0]|[1-9]\d*)(?:\.([x*0]|[1-9]\d*)(?:\.([x*0]|[1-9]\d*)(?:-([a-z0-9-.]+))?(?:\+([a-z0-9-.]+))?)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // hyphen       ::= partial ' - ' partial
    private static readonly Regex s_hyphenRegExp = new(
        @"^\s*([a-z0-9-+.*]+)\s+-\s+([a-z0-9-+.*]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // simple       ::= primitive | partial | tilde | caret
    // primitive    ::= ( '<' | '>' | '>=' | '<=' | '=' ) partial
    // tilde        ::= '~' partial
    // caret        ::= '^' partial
    private static readonly Regex s_rangeRegExp = new(
        @"^([~^<>=]|<=|>=)?\s*([a-z0-9-+.*]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private IReadOnlyList<IReadOnlyList<Comparator>> _alternatives;

    public VersionRange(string spec)
    {
        _alternatives = spec.Length != 0 ? Debug.CheckDefined(ParseRange(spec), "Invalid range spec.") : [];
    }

    public static VersionRange? TryParse(string text)
    {
        var sets = ParseRange(text);
        if (sets is null) return null;

        var range = new VersionRange("");
        range._alternatives = sets;
        return range;
    }

    /// <summary>
    /// Tests whether a version matches the range. This is equivalent to
    /// <c>satisfies(version, range, { includePrerelease: true })</c> in <c>node-semver</c>.
    /// </summary>
    public bool Test(Version version) => TestDisjunction(version, _alternatives);

    public bool Test(string version) => Test(new Version(version));

    public override string ToString() => FormatDisjunction(_alternatives);

    private static List<List<Comparator>>? ParseRange(string text)
    {
        var alternatives = new List<List<Comparator>>();
        foreach (var rawRange in s_logicalOrRegExp.Split(text.Trim()))
        {
            var range = rawRange.Trim();
            if (range.Length == 0) continue;

            var comparators = new List<Comparator>();
            var hyphenMatch = s_hyphenRegExp.Match(range);
            if (hyphenMatch.Success)
            {
                if (!ParseHyphen(hyphenMatch.Groups[1].Value, hyphenMatch.Groups[2].Value, comparators)) return null;
            }
            else
            {
                foreach (var rawSimple in s_whitespaceRegExp.Split(range))
                {
                    var simple = rawSimple.Trim();
                    var match = s_rangeRegExp.Match(simple);
                    if (!match.Success) return null;
                    var op = match.Groups[1].Success ? match.Groups[1].Value : null;
                    if (!ParseComparator(op, match.Groups[2].Value, comparators)) return null;
                }
            }

            alternatives.Add(comparators);
        }

        return alternatives;
    }

    private static Partial? ParsePartial(string text)
    {
        var match = s_partialRegExp.Match(text);
        if (!match.Success) return null;

        var major = match.Groups[1].Value;
        var minor = match.Groups[2].Success ? match.Groups[2].Value : "*";
        var patch = match.Groups[3].Success ? match.Groups[3].Value : "*";
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        var build = match.Groups[5].Success ? match.Groups[5].Value : null;

        var version = new Version(
            IsWildcard(major) ? 0 : int.Parse(major, CultureInfo.InvariantCulture),
            IsWildcard(major) || IsWildcard(minor) ? 0 : int.Parse(minor, CultureInfo.InvariantCulture),
            IsWildcard(major) || IsWildcard(minor) || IsWildcard(patch) ? 0 : int.Parse(patch, CultureInfo.InvariantCulture),
            prerelease,
            build);

        return new Partial(version, major, minor, patch);
    }

    private static bool ParseHyphen(string left, string right, List<Comparator> comparators)
    {
        var leftResult = ParsePartial(left);
        if (leftResult is null) return false;

        var rightResult = ParsePartial(right);
        if (rightResult is null) return false;

        if (!IsWildcard(leftResult.Value.Major))
        {
            comparators.Add(new Comparator(">=", leftResult.Value.Version));
        }

        if (!IsWildcard(rightResult.Value.Major))
        {
            var r = rightResult.Value;
            comparators.Add(
                IsWildcard(r.Minor) ? new Comparator("<", r.Version.Increment(VersionField.Major))
                : IsWildcard(r.Patch) ? new Comparator("<", r.Version.Increment(VersionField.Minor))
                : new Comparator("<=", r.Version));
        }

        return true;
    }

    private static bool ParseComparator(string? @operator, string text, List<Comparator> comparators)
    {
        var result = ParsePartial(text);
        if (result is null) return false;

        var (version, major, minor, patch) = result.Value;
        if (!IsWildcard(major))
        {
            switch (@operator)
            {
                case "~":
                    comparators.Add(new Comparator(">=", version));
                    comparators.Add(new Comparator("<", version.Increment(IsWildcard(minor) ? VersionField.Major : VersionField.Minor)));
                    break;
                case "^":
                    comparators.Add(new Comparator(">=", version));
                    comparators.Add(new Comparator(
                        "<",
                        version.Increment(
                            version.Major > 0 || IsWildcard(minor) ? VersionField.Major
                            : version.Minor > 0 || IsWildcard(patch) ? VersionField.Minor
                            : VersionField.Patch)));
                    break;
                case "<":
                case ">=":
                    comparators.Add(
                        IsWildcard(minor) || IsWildcard(patch)
                            ? new Comparator(@operator, version.With(prerelease: "0"))
                            : new Comparator(@operator, version));
                    break;
                case "<=":
                case ">":
                    comparators.Add(
                        IsWildcard(minor) ? new Comparator(@operator == "<=" ? "<" : ">=", version.Increment(VersionField.Major).With(prerelease: "0"))
                        : IsWildcard(patch) ? new Comparator(@operator == "<=" ? "<" : ">=", version.Increment(VersionField.Minor).With(prerelease: "0"))
                        : new Comparator(@operator, version));
                    break;
                case "=":
                case null:
                    if (IsWildcard(minor) || IsWildcard(patch))
                    {
                        comparators.Add(new Comparator(">=", version.With(prerelease: "0")));
                        comparators.Add(new Comparator(
                            "<",
                            version.Increment(IsWildcard(minor) ? VersionField.Major : VersionField.Minor).With(prerelease: "0")));
                    }
                    else
                    {
                        comparators.Add(new Comparator("=", version));
                    }

                    break;
                default:
                    // unrecognized
                    return false;
            }
        }
        else if (@operator is "<" or ">")
        {
            comparators.Add(new Comparator("<", Version.Zero));
        }

        return true;
    }

    private static bool IsWildcard(string part) => part is "*" or "x" or "X";

    private static bool TestDisjunction(Version version, IReadOnlyList<IReadOnlyList<Comparator>> alternatives)
    {
        // an empty disjunction is treated as "*" (all versions)
        if (alternatives.Count == 0) return true;
        for (var i = 0; i < alternatives.Count; i++)
        {
            if (TestAlternative(version, alternatives[i])) return true;
        }

        return false;
    }

    private static bool TestAlternative(Version version, IReadOnlyList<Comparator> comparators)
    {
        for (var i = 0; i < comparators.Count; i++)
        {
            var comparator = comparators[i];
            if (!TestComparator(version, comparator.Operator, comparator.Operand)) return false;
        }

        return true;
    }

    private static bool TestComparator(Version version, string @operator, Version operand)
    {
        var cmp = (int)version.CompareTo(operand);
        return @operator switch
        {
            "<" => cmp < 0,
            "<=" => cmp <= 0,
            ">" => cmp > 0,
            ">=" => cmp >= 0,
            "=" => cmp == 0,
            _ => Debug.Fail<bool>($"Illegal value: {@operator}"),
        };
    }

    private static string FormatDisjunction(IReadOnlyList<IReadOnlyList<Comparator>> alternatives)
    {
        var result = string.Join(" || ", alternatives.Select(FormatAlternative));
        return result.Length != 0 ? result : "*";
    }

    private static string FormatAlternative(IReadOnlyList<Comparator> comparators)
        => string.Join(" ", comparators.Select(static c => $"{c.Operator}{c.Operand}"));

    private readonly record struct Comparator(string Operator, Version Operand);

    private readonly record struct Partial(Version Version, string Major, string Minor, string Patch);
}
