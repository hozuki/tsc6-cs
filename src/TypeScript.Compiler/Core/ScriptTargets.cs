namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:9373-9375 and the `type` map of targetOptionDeclaration
//             (src/compiler/commandLineParser.ts)
/// <summary>
/// The command-line names of <see cref="ScriptTarget"/>.
/// </summary>
/// <remarks>
/// PORT-NOTE: in TS this is the key set of <c>targetOptionDeclaration.type</c>, a <c>Map</c> built from an object
/// literal, so the order is the declaration order — and several targets carry two names (<c>es6</c> and
/// <c>es2015</c>), of which the first one encountered wins. The port keeps that order rather than deriving a name
/// from the enum member, because the name ends up inside diagnostic messages.
/// <para>
/// PORT-NOTE: only the names are ported here. The option declaration itself belongs to the command line parser
/// (M8) and will supersede this table then.
/// </para>
/// </remarks>
internal static class ScriptTargets
{
    private static readonly (string Name, ScriptTarget Target)[] s_optionNames =
    [
        ("es3", ScriptTarget.ES3),
        ("es5", ScriptTarget.ES5),
        ("es6", ScriptTarget.ES2015),
        ("es2015", ScriptTarget.ES2015),
        ("es2016", ScriptTarget.ES2016),
        ("es2017", ScriptTarget.ES2017),
        ("es2018", ScriptTarget.ES2018),
        ("es2019", ScriptTarget.ES2019),
        ("es2020", ScriptTarget.ES2020),
        ("es2021", ScriptTarget.ES2021),
        ("es2022", ScriptTarget.ES2022),
        ("es2023", ScriptTarget.ES2023),
        ("es2024", ScriptTarget.ES2024),
        ("es2025", ScriptTarget.ES2025),
        ("esnext", ScriptTarget.ESNext),
    ];

    // ported from: src/compiler/utilities.ts:9373-9375
    /// <summary>The command-line name of a target, or <c>null</c> when it has none.</summary>
    public static string? GetNameOfScriptTarget(ScriptTarget scriptTarget)
    {
        foreach (var (name, target) in s_optionNames)
        {
            if (target == scriptTarget) return name;
        }

        return null;
    }
}
