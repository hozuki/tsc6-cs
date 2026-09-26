namespace TypeScript.Compiler;

// ported from: src/compiler/core.ts:1921-1926
/// <summary>
/// Level of debug assertions that are enabled. Equivalent to TS's <c>AssertionLevel</c> const enum.
/// </summary>
internal enum AssertionLevel
{
    None = 0,
    Normal = 1,
    Aggressive = 2,
    VeryAggressive = 3,
}
