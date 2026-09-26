namespace TypeScript.Compiler.Tests;

/// <summary>
/// <c>Utilities.GetEmitScriptTarget</c>, <c>Utilities.GetAlwaysStrict</c> and <c>Utilities.GetStrictOptionValue</c>
/// (<c>Utilities/Utilities.Options.cs</c>).
/// </summary>
/// <remarks>
/// PORT-NOTE: every expected value in this file was read off the reference rather than derived from the port —
/// <c>lib/typescript.js</c> exports all three readers, and the rows below are a transcript of what they answered:
/// <code>
/// getEmitScriptTarget({}) = 12, ({target:0}) = 12, ({target:1}) = 1, ({target:2}) = 2, ({target:9}) = 9,
///     ({target:99}) = 99, ({target:100}) = 100
/// getAlwaysStrict({}) = true, ({alwaysStrict:false}) = false, ({alwaysStrict:true}) = true, ({strict:false}) = true
/// getStrictOptionValue({}, k) = true and ({strict:false}, k) = false, for every one of the eight keys k;
///     ({strict:true, strictNullChecks:false}, "strictNullChecks") = false,
///     ({strict:false, strictNullChecks:true}, "strictNullChecks") = true, ({strict:true}, "bogus") = true
/// </code>
/// That is why some cases look redundant at a glance: they pin the reference's defaults, not the port's shape.
/// </remarks>
public class CompilerOptionsTests
{
    /// <summary>The eight members of TS's <c>StrictOptionName</c> (utilities.ts:9358-9366) in its declared order —
    /// exactly the keys the reference ever passes to <c>getStrictOptionValue</c> (the 23 call sites the batch's grep
    /// found; the one dynamic caller is guarded by the eight <c>strictFlag: true</c> option declarations).</summary>
    private static readonly string[] s_strictOptionNames =
    [
        "noImplicitAny",
        "noImplicitThis",
        "strictNullChecks",
        "strictFunctionTypes",
        "strictBindCallApply",
        "strictPropertyInitialization",
        "strictBuiltinIteratorReturn",
        "useUnknownInCatchVariables",
    ];

    /// <summary>The slot each key names. Writing the value through this table is what makes the per-key theory below
    /// a check that the arm reads <em>its</em> slot rather than a neighbour's.</summary>
    private static readonly Dictionary<string, Action<FakeCompilerOptions, bool?>> s_strictSlots = new()
    {
        ["noImplicitAny"] = (options, value) => options.NoImplicitAny = value,
        ["noImplicitThis"] = (options, value) => options.NoImplicitThis = value,
        ["strictNullChecks"] = (options, value) => options.StrictNullChecks = value,
        ["strictFunctionTypes"] = (options, value) => options.StrictFunctionTypes = value,
        ["strictBindCallApply"] = (options, value) => options.StrictBindCallApply = value,
        ["strictPropertyInitialization"] = (options, value) => options.StrictPropertyInitialization = value,
        ["strictBuiltinIteratorReturn"] = (options, value) => options.StrictBuiltinIteratorReturn = value,
        ["useUnknownInCatchVariables"] = (options, value) => options.UseUnknownInCatchVariables = value,
    };

    public static TheoryData<string> StrictOptionNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in s_strictOptionNames) data.Add(name);
            return data;
        }
    }

    // The enum is internal, so the theory's parameters are its numbers and the test casts, the way the other test
    // classes here do (a public test method cannot take an internal type).
    [Theory]
    [InlineData(null, (int)ScriptTarget.LatestStandard)]
    // ES3 is deprecated and is read as "no target at all" (utilities.ts:9051).
    [InlineData((int)ScriptTarget.ES3, (int)ScriptTarget.LatestStandard)]
    [InlineData((int)ScriptTarget.ES5, (int)ScriptTarget.ES5)]
    [InlineData((int)ScriptTarget.ES2015, (int)ScriptTarget.ES2015)]
    [InlineData((int)ScriptTarget.ES2022, (int)ScriptTarget.ES2022)]
    [InlineData((int)ScriptTarget.ESNext, (int)ScriptTarget.ESNext)]
    [InlineData((int)ScriptTarget.JSON, (int)ScriptTarget.JSON)]
    public void GetEmitScriptTarget_AnswersLatestStandardForAnUnsetOrEs3Target(int? target, int expected)
    {
        Assert.Equal(
            (ScriptTarget)expected,
            Utilities.GetEmitScriptTarget(new FakeCompilerOptions { Target = (ScriptTarget?)target }));
    }

    [Fact]
    public void GetAlwaysStrict_IsOnUnlessExplicitlyDisabled()
    {
        Assert.True(Utilities.GetAlwaysStrict(new FakeCompilerOptions()));
        Assert.True(Utilities.GetAlwaysStrict(new FakeCompilerOptions { AlwaysStrict = true }));
        Assert.False(Utilities.GetAlwaysStrict(new FakeCompilerOptions { AlwaysStrict = false }));

        // It is no longer a strict-family flag, so `strict` does not reach it (the comment above the entry,
        // utilities.ts:9270).
        Assert.True(Utilities.GetAlwaysStrict(new FakeCompilerOptions { Strict = false }));
    }

    [Theory]
    [MemberData(nameof(StrictOptionNames))]
    public void GetStrictOptionValue_ReadsTheSlotTheKeyNames(string option)
    {
        var set = s_strictSlots[option];

        // An explicit value wins over `strict`, in both directions.
        var on = new FakeCompilerOptions { Strict = true };
        set(on, false);
        Assert.False(Utilities.GetStrictOptionValue(on, option));

        var off = new FakeCompilerOptions { Strict = false };
        set(off, true);
        Assert.True(Utilities.GetStrictOptionValue(off, option));

        // With no value of its own the answer is `strict !== false`, so an absent `strict` counts as on.
        Assert.True(Utilities.GetStrictOptionValue(new FakeCompilerOptions { Strict = true }, option));
        Assert.False(Utilities.GetStrictOptionValue(new FakeCompilerOptions { Strict = false }, option));
        Assert.True(Utilities.GetStrictOptionValue(new FakeCompilerOptions(), option));
    }

    [Fact]
    public void GetStrictOptionValue_AnswersTheStrictFallbackForAKeyOutsideTheUnion()
    {
        // PORT-NOTE: this batch's contract asked for a `default: false` arm. TS reads
        // `compilerOptions[flag] === undefined ? (compilerOptions.strict !== false) : !!compilerOptions[flag]`, so a
        // key the union does not name gets the `strict` fallback rather than `false` — in lib/typescript.js,
        // `getStrictOptionValue({strict:true}, "bogus")` is `true` and `{strict:false}` is `false`. The port follows
        // the reference, and this pins that choice; the arm is unreachable for the eight names above.
        Assert.True(Utilities.GetStrictOptionValue(new FakeCompilerOptions { Strict = true }, "bogus"));
        Assert.False(Utilities.GetStrictOptionValue(new FakeCompilerOptions { Strict = false }, "bogus"));

        // The key is TS's own spelling: the PascalCase member name is not one of the union's keys, so it takes the
        // same fallback instead of reading `StrictNullChecks`.
        Assert.True(Utilities.GetStrictOptionValue(
            new FakeCompilerOptions { Strict = true, StrictNullChecks = false },
            "StrictNullChecks"));
    }
}
