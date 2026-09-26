namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:314-319 (ModuleInstanceState)
// ported from: src/compiler/binder.ts:465-493 (ContainerFlags)
/// <summary>
/// How instantiated a module declaration's body is — TS's <c>ModuleInstanceState</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this a <c>const enum</c> with implicit values; the port writes the values out (§4.1).
/// The order matters: <c>getModuleInstanceState</c> compares two answers with <c>&gt;</c> to keep the strongest.
/// </remarks>
internal enum ModuleInstanceState
{
    /// <summary>ported from: src/compiler/binder.ts:316 — <c>ModuleInstanceState.NonInstantiated</c>.</summary>
    NonInstantiated = 0,

    /// <summary>ported from: src/compiler/binder.ts:317 — <c>ModuleInstanceState.Instantiated</c>.</summary>
    Instantiated = 1,

    /// <summary>ported from: src/compiler/binder.ts:318 — <c>ModuleInstanceState.ConstEnumOnly</c>.</summary>
    ConstEnumOnly = 2,
}

/// <summary>
/// What kind of container a node opens — TS's <c>ContainerFlags</c>, the bit set the binder tests in
/// <c>bindContainer</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this a <c>const enum</c>; the port's flag enums are <c>[Flags] enum : uint</c> (§4.5).
/// </remarks>
[Flags]
internal enum ContainerFlags : uint
{
    /// <summary>ported from: src/compiler/binder.ts:468 — <c>ContainerFlags.None</c>.</summary>
    None = 0,

    /// <summary>ported from: src/compiler/binder.ts:474 — <c>ContainerFlags.IsContainer</c>.</summary>
    IsContainer = 1 << 0,

    /// <summary>ported from: src/compiler/binder.ts:479 — <c>ContainerFlags.IsBlockScopedContainer</c>.</summary>
    IsBlockScopedContainer = 1 << 1,

    /// <summary>ported from: src/compiler/binder.ts:483 — <c>ContainerFlags.IsControlFlowContainer</c>.</summary>
    IsControlFlowContainer = 1 << 2,

    /// <summary>ported from: src/compiler/binder.ts:485 — <c>ContainerFlags.IsFunctionLike</c>.</summary>
    IsFunctionLike = 1 << 3,

    /// <summary>ported from: src/compiler/binder.ts:486 — <c>ContainerFlags.IsFunctionExpression</c>.</summary>
    IsFunctionExpression = 1 << 4,

    /// <summary>ported from: src/compiler/binder.ts:487 — <c>ContainerFlags.HasLocals</c>.</summary>
    HasLocals = 1 << 5,

    /// <summary>ported from: src/compiler/binder.ts:488 — <c>ContainerFlags.IsInterface</c>.</summary>
    IsInterface = 1 << 6,

    /// <summary>ported from: src/compiler/binder.ts:489 — <c>ContainerFlags.IsObjectLiteralOrClassExpressionMethodOrAccessor</c>.</summary>
    IsObjectLiteralOrClassExpressionMethodOrAccessor = 1 << 7,

    /// <summary>ported from: src/compiler/binder.ts:490 — <c>ContainerFlags.PropagatesThisKeyword</c>.</summary>
    PropagatesThisKeyword = 1 << 8,
}
