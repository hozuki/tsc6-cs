namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:6439-6455 (Type)
// ported from: src/compiler/utilities.ts:8489-8495 (the Type constructor)
/// <summary>
/// The base of every type the checker creates — TS's <c>Type</c> class (M6-0).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares <c>Type</c> as an interface and merges a constructor function of the same name
/// (<c>utilities.ts:8489</c>); the checker reaches it through <c>objectAllocator.getTypeConstructor()</c>. The port
/// has no <c>objectAllocator</c> (§15.2), so this class is both halves — the generated <see cref="IType"/> supplies
/// the slots and this class supplies the constructor the checker's <c>createType</c> calls.
/// </para>
/// <para>
/// PORT-NOTE: TS has exactly one runtime type class; every richer face (<c>ObjectType</c>, <c>UnionType</c>,
/// <c>TypeParameter</c>, …) is a structural interface the checker applies to that same object with an <c>as</c>
/// cast, which never fails. C# is nominal, so the port gives each face its own class and arranges the families in
/// inheritance chains (<c>Model/UnionOrIntersectionType.cs</c> → <c>Model/ObjectType.cs</c> →
/// <c>Model/AnonymousType.cs</c>; <c>Model/IntrinsicType.cs</c> → <c>Model/LiteralType.cs</c>;
/// <c>Model/TypeParameter.cs</c> …). Each family's factory instantiates the most derived class of its family, so a
/// cast TS performs on any member of that family still succeeds in C#. The price is that a class carries the slots
/// of the whole family — the same slots TS's object would answer <c>undefined</c> for, which is what the port's
/// <c>null</c> means.
/// </para>
/// <para>
/// PORT-NOTE: the family chains make the slots of a class a superset of its own TS interface. That is deliberate:
/// <c>emptyGenericType = createAnonymousType(…) as ObjectType as GenericType</c> (checker.ts:2154) is a cast TS
/// performs at run time, and only shared slots can express it in C#.
/// </para>
/// <para>
/// PORT-NOTE: <c>checker</c> is filled only while <c>Debug.isDebugging</c> or tracing is on
/// (<c>utilities.ts:8492</c>); nothing but the debugger reads it. The slot itself always exists — C# cannot drop a
/// field — so the condition only decides whether it is assigned.
/// </para>
/// <para>
/// PORT-NOTE: TS has one type constructor and reaches it through <c>objectAllocator.getTypeConstructor()</c>, so every
/// cast in the checker is a cast on an object built by these two arguments. The port keeps that call shape by declaring
/// the same two-argument constructor on <em>every</em> family member, each forwarding to its base: the M6-2 factory
/// picks the class from <c>TypeFlags</c> and constructs it as <c>new X(checker, flags)</c>, which is also what makes the
/// chain of <c>Model/ObjectType.cs</c> and friends the only place the class choice lives.
/// </para>
/// </remarks>
internal partial class Type : IType
{
    /// <summary>ported from: src/compiler/utilities.ts:8489-8495 — the <c>Type</c> constructor.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's constructor deliberately does not set <c>id</c>; <c>createType</c> (checker.ts:5518-5521)
    /// assigns it from <c>typeCount</c> right after construction. <c>createOriginType</c> (checker.ts:5531-5533)
    /// leaves it 0. The port keeps both behaviours by leaving the slot at its default here.
    /// </remarks>
    public Type(ITypeChecker checker, TypeFlags flags)
    {
        Flags = flags;
        if (Debug.IsDebugging || Tracing.Current is not null)
        {
            Checker = checker;
        }
    }

    /// <summary>ported from: src/compiler/types.ts:6440 — <c>Type.flags</c>.</summary>
    public TypeFlags Flags { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6441 — <c>Type.id</c>.</summary>
    public int Id { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6442 — <c>Type.checker</c>.</summary>
    public ITypeChecker? Checker { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6443 — <c>Type.symbol</c>.</summary>
    public ISymbol? Symbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6444 — <c>Type.pattern</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>DestructuringPattern</c> is <c>BindingPattern | ObjectLiteralExpression | ArrayLiteralExpression</c>;
    /// the generator widened the union to <c>Node</c> (the common base it finds), which is exactly TS's own upper bound.
    /// </remarks>
    public Node? Pattern { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6445 — <c>Type.aliasSymbol</c>.</summary>
    public ISymbol? AliasSymbol { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6446 — <c>Type.aliasTypeArguments</c>.</summary>
    public List<IType>? AliasTypeArguments { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6448 — <c>Type.permissiveInstantiation</c>.</summary>
    public IType? PermissiveInstantiation { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6450 — <c>Type.restrictiveInstantiation</c>.</summary>
    public IType? RestrictiveInstantiation { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6452 — <c>Type.immediateBaseConstraint</c>.</summary>
    public IType? ImmediateBaseConstraint { get; set; }

    /// <summary>ported from: src/compiler/types.ts:6454 — <c>Type.widened</c>.</summary>
    public IType? Widened { get; set; }
}
