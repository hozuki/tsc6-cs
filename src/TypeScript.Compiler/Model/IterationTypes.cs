namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6822-6826 (IterationTypes)
// ported from: src/compiler/checker.ts:45842-45865 (createIterationTypes)
/// <summary>
/// The <c>yield</c>/<c>return</c>/<c>next</c> triple of an iterable or iterator type — TS's <c>IterationTypes</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS builds it as the object literal <c>{ yieldType, returnType, nextType }</c> (checker.ts:45859, :45864) and
/// declares all three members <c>readonly</c> (types.ts:6823-6825), which the generator kept as get-only. C# cannot
/// initialize a get-only auto-property from an object initializer, so the port's class takes the three values in its
/// constructor — the literal and the constructor call are the same three arguments.
/// </para>
/// <para>
/// PORT-NOTE: <c>noIterationTypes</c> (checker.ts:2186-2196) is TS's sentinel object literal whose three getters call
/// <c>Debug.fail</c>. It is only ever compared by identity before any member is read
/// (checker.ts:45879, :45937, :45956, :45969, :45983, :45987), so the port keeps it as a checker-level sentinel built
/// with default values — the throwing getters exist to catch a read that is impossible by construction.
/// </para>
/// </remarks>
internal partial class IterationTypes : IIterationTypes
{
    /// <summary>ported from: src/compiler/checker.ts:45859 — the <c>IterationTypes</c> object literal.</summary>
    public IterationTypes(IType? yieldType, IType? returnType, IType? nextType)
    {
        YieldType = yieldType;
        ReturnType = returnType;
        NextType = nextType;
    }

    /// <summary>ported from: src/compiler/types.ts:6823 — <c>IterationTypes.yieldType</c>.</summary>
    public IType? YieldType { get; }

    /// <summary>ported from: src/compiler/types.ts:6824 — <c>IterationTypes.returnType</c>.</summary>
    public IType? ReturnType { get; }

    /// <summary>ported from: src/compiler/types.ts:6825 — <c>IterationTypes.nextType</c>.</summary>
    public IType? NextType { get; }
}
