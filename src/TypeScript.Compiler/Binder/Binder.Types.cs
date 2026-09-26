namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:321-328 (ActiveLabel)
/// <summary>
/// One entry of the binder's stack of labels that a <c>break</c>/<c>continue</c> can target — TS's
/// <c>ActiveLabel</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this an interface (<c>interface ActiveLabel</c>) but every use is a mutable object it pushes
/// and pops as a linked list, so the port declares the class it actually is (§12.3: an interface nothing implements is
/// not a type, and a mutable shape is a class).
/// <para>
/// PORT-NOTE: TS's <c>name</c> is <c>__String</c> and <c>breakTarget</c> is a non-optional <c>FlowLabel</c>; the port's
/// <c>Name</c> is nullable (the label is read off <c>Identifier.EscapedText</c>, a <c>string?</c>) and <c>BreakTarget</c>
/// is nullable because the literal that builds an entry sets it in the same statement that creates the object
/// (binder.ts:1808-1816, M5-2).
/// </para>
/// </remarks>
internal sealed class ActiveLabel
{
    /// <summary>ported from: src/compiler/binder.ts:322 — <c>ActiveLabel.next</c>.</summary>
    public ActiveLabel? Next { get; set; }

    /// <summary>ported from: src/compiler/binder.ts:323 — <c>ActiveLabel.name</c>.</summary>
    public string? Name { get; set; }

    /// <summary>ported from: src/compiler/binder.ts:324 — <c>ActiveLabel.breakTarget</c>.</summary>
    /// <remarks>PORT-NOTE: TS types this <c>FlowLabel</c>; the port has one flow-node class (Model/FlowNode.cs).</remarks>
    public FlowNode? BreakTarget { get; set; }

    /// <summary>ported from: src/compiler/binder.ts:325 — <c>ActiveLabel.continueTarget</c>.</summary>
    public FlowNode? ContinueTarget { get; set; }

    /// <summary>ported from: src/compiler/binder.ts:326 — <c>ActiveLabel.referenced</c>.</summary>
    public bool Referenced { get; set; }
}
