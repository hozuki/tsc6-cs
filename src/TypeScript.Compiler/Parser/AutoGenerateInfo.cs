namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:1717-1722 — interface AutoGenerateInfo
/// <summary>The generated-name bookkeeping an identifier or private identifier carries.</summary>
/// <remarks>
/// PORT-NOTE: TS fills the interface in with an object literal — <c>setIdentifierAutoGenerate(node, { flags, id,
/// prefix, suffix })</c> — so no type for it exists on that side; the port needs something constructible (the same
/// situation as <see cref="LineAndCharacter"/> and <see cref="TextRange"/>).
/// <para>
/// The copy constructor is the port of the object spread TS uses in <c>cloneGeneratedIdentifier</c> and
/// <c>cloneGeneratedPrivateIdentifier</c>: the id is deliberately shared, because a clone must generate the same name as
/// its original (see the comment on <c>id</c> in <c>types.ts</c>).
/// </para>
/// </remarks>
internal sealed class AutoGenerateInfo : IAutoGenerateInfo
{
    public AutoGenerateInfo(GeneratedIdentifierFlags flags, int id, object? prefix, string? suffix)
    {
        Flags = flags;
        Id = id;
        Prefix = prefix;
        Suffix = suffix;
    }

    public AutoGenerateInfo(IAutoGenerateInfo other)
        : this(other.Flags, other.Id, other.Prefix, other.Suffix)
    {
    }

    /// <summary>Whether and how to auto-generate the text for the identifier.</summary>
    public GeneratedIdentifierFlags Flags { get; set; }

    /// <summary>Ensures unique generated identifiers get unique names, but clones get the same name.</summary>
    public int Id { get; }

    public object? Prefix { get; }

    public string? Suffix { get; }
}
