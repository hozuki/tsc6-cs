namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:5309-5315 — const enum FunctionFlags
/// <summary>What kind of function a signature declares, as reported by <c>Utilities.GetFunctionFlags</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS declares this <c>const enum</c> in <c>utilities.ts</c> rather than in <c>types.ts</c>, so it is not part
/// of the extracted type model and <c>tools/Gen</c> never sees it — the two other <c>utilities.ts</c> enums the port has,
/// <c>Associativity</c> and <c>OperatorPrecedence</c>, are named one by one in the generator's <c>enumSources</c>. It is
/// written by hand here, under the file name the generator would use if it ever gains the name, so a re-run cannot
/// produce a second copy.
/// </remarks>
[Flags]
internal enum FunctionFlags : int
{
    /// <summary>A normal function.</summary>
    Normal = 0,

    /// <summary>A generator function or async generator function.</summary>
    Generator = 1 << 0,

    /// <summary>An async function or an async generator function.</summary>
    Async = 1 << 1,

    /// <summary>A signature or overload, which has no body.</summary>
    Invalid = 1 << 2,

    /// <summary>An async generator function.</summary>
    AsyncGenerator = Async | Generator,
}
