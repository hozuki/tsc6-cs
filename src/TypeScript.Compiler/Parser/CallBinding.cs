namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:8666-8669 — interface CallBinding
/// <summary>What a call to a member has to be rewritten into: the target to call and the `this` to call it on.</summary>
/// <remarks>
/// PORT-NOTE: TS returns an object literal from <c>createCallBinding</c>; the port needs something constructible (the same
/// situation as <see cref="LineAndCharacter"/> and <see cref="TextRange"/>).
/// </remarks>
internal sealed class CallBinding : ICallBinding
{
    public CallBinding(LeftHandSideExpression target, Expression thisArg)
    {
        Target = target;
        ThisArg = thisArg;
    }

    public LeftHandSideExpression? Target { get; set; }

    public Expression? ThisArg { get; set; }
}
