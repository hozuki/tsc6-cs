namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:8720-8727 — interface GeneratedNamePart
/// <summary>A generated name's prefix or suffix, with the node its text is taken from.</summary>
/// <remarks>
/// PORT-NOTE: TS fills this in with an object literal wherever a transformer needs a name like
/// <c>_a_b</c> (see <c>GeneratedNamePart</c> in <c>types.ts</c>); the port needs something constructible, the same
/// situation as <see cref="AutoGenerateInfo"/> and <see cref="CallBinding"/>. Only <see cref="Prefix"/> and
/// <see cref="Suffix"/> are optional — the node is what the name is built from.
/// </remarks>
internal sealed class GeneratedNamePart : IGeneratedNamePart
{
    public GeneratedNamePart(MemberExpression node, string? prefix = null, string? suffix = null)
    {
        Node = node;
        Prefix = prefix;
        Suffix = suffix;
    }

    /// <summary>An additional prefix to insert before the text sourced from <see cref="Node"/>.</summary>
    public string? Prefix { get; set; }

    /// <summary>The node the generated name is derived from.</summary>
    public MemberExpression? Node { get; set; }

    /// <summary>An additional suffix to insert after the text sourced from <see cref="Node"/>.</summary>
    public string? Suffix { get; set; }
}
