namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts — the literal-type overloads of NodeFactory.createToken
/// <summary>
/// The runtime projection of a <c>SyntaxKind</c> member: what a <see cref="SyntaxKind"/> value can be asked about
/// once the compile-time type is gone.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS lets an enum member be used as a *type* (`createToken(token: SyntaxKind.EndOfFileToken)`), which is
/// how the factory picks a return type from an argument. A C# enum member is a constant, not a type, so the port
/// maps each member to a nested type instead — see <c>TypedEnum</c> (docs/architecture.md §16).
/// <para>
/// This non-generic interface is the <b>dynamic</b> half. When the kind is not known at compile time — a value from
/// the scanner, from configuration, from another kind variable — this is what the code can still ask "which member
/// is this?". <see cref="ITypedEnum{TSelf}"/> is the static half.
/// </para>
/// </remarks>
internal interface ITypedEnum
{
    SyntaxKind SyntaxKind { get; }
}

/// <summary>
/// The static half: lets a member type stand in for itself in generic and overloaded signatures.
/// </summary>
/// <typeparam name="TSelf">The member type itself, as in the curiously-recurring pattern.</typeparam>
internal interface ITypedEnum<TSelf> : ITypedEnum
    where TSelf : ITypedEnum<TSelf>
{
    /// <summary>The member's kind, without needing an instance.</summary>
    static abstract SyntaxKind StaticSyntaxKind { get; }

    /// <summary>The single instance, used where a value has to be passed (e.g. as a factory argument).</summary>
    static abstract TSelf Instance { get; }
}

/// <summary>
/// Base of every generated member type, so the member types share a class family as well as the interfaces.
/// </summary>
/// <remarks>
/// PORT-NOTE: the constructor is <c>private protected</c> — only the generated member types (which are nested in
/// <c>TypedEnum</c> and in this assembly) can derive. Each member then seals itself and takes a private constructor
/// so <c>Instance</c> is genuinely the only instance.
/// </remarks>
internal abstract class TypedEnum<T>
    where T : TypedEnum<T>, ITypedEnum<T>
{
    private protected TypedEnum()
    {
    }
}
