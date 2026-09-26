namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:8428 — type EmitHelperUniqueNameCallback
/// <summary>
/// Answers the name under which a helper will be emitted in this file, given the helper's plain name, so that one
/// helper's text can refer to another (<c>__awaiter</c> inside <c>__asyncGenerator</c>, and the like).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this as a function type alias and passes it to a helper's text function — <c>text:
/// (uniqueName: EmitHelperUniqueNameCallback) =&gt; string</c>, which is <c>Func&lt;EmitHelperUniqueNameCallback,
/// string&gt;</c> here. The generated <c>IEmitHelperBase.Text</c> is <c>object?</c> because TS's <c>text</c> is
/// <c>string | ((node: EmitHelperUniqueNameCallback) =&gt; string)</c>.
/// </remarks>
internal delegate string EmitHelperUniqueNameCallback(string name);

// ported from: src/compiler/types.ts:8406-8426 — EmitHelperBase / ScopedEmitHelper / UnscopedEmitHelper / EmitHelper
/// <summary>
/// One emit helper the emitter writes into the output: its name, whether it must be emitted inside the current scope,
/// and the text to write (or a callback that builds that text).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS fills the <c>EmitHelper</c> interfaces in with object literals — there is no class on that side — so the
/// port needs a constructible type, the same situation as <see cref="AutoGenerateInfo"/> and <see cref="CallBinding"/>.
/// It is one class for all three interfaces because C# cannot express the discriminated union
/// (<c>ScopedEmitHelper | UnscopedEmitHelper</c>): the generated <c>IScopedEmitHelper</c> adds nothing, and
/// <c>IUnscopedEmitHelper</c> adds only <c>ImportName</c>, so the class provides <c>ImportName</c> and leaves it null for a
/// scoped helper. The one place TS's union narrows — an unscoped helper's text is always a <c>string</c> — is not
/// enforced here, exactly as the generated interfaces do not carry it either.
/// <para>
/// PORT-NOTE: the descriptor derives from <see cref="Node"/>, which TS's <c>EmitHelper</c> does not. The port's model has
/// no slot for a bare descriptor: <c>IEmitNode.Helpers</c> and <c>IEmitHelperBase.Dependencies</c> are generated as
/// <c>List&lt;Node&gt;?</c> (the union's members share only interfaces, and <c>tools/Gen</c>'s common-base search walks
/// class chains, so it falls back to <c>Node</c>), and the entry points that take a helper
/// (<c>EmitNodes.AddEmitHelper</c>, <c>TransformationContext.requestEmitHelper</c>) are typed <c>Node</c> for the same
/// reason. Deriving from <see cref="Node"/> is what lets one object fit every one of those slots without changing the
/// generated model; <see cref="Kind"/> is <see cref="SyntaxKind.Unknown"/>, which no dispatch table handles. A helper is
/// never a child of anything, so the only code that has to tell a descriptor apart is the emitter, and it asks
/// <c>is EmitHelper</c>.
/// </para>
/// </remarks>
internal sealed class EmitHelper : Node, IEmitHelperBase, IScopedEmitHelper, IUnscopedEmitHelper
{
    /// <summary>Builds a helper whose text is written as it is (TS's <c>text: string</c>).</summary>
    public EmitHelper(
        string name,
        bool scoped,
        string text,
        int? priority = null,
        List<Node>? dependencies = null,
        string? importName = null)
        : this(name, scoped, (object)text, priority, dependencies, importName)
    {
    }

    /// <summary>Builds a helper whose text is built from the file-level unique-name generator (TS's
    /// <c>text: (uniqueName) =&gt; string</c>, produced there by <c>helperString</c>).</summary>
    public EmitHelper(
        string name,
        bool scoped,
        Func<EmitHelperUniqueNameCallback, string> text,
        int? priority = null,
        List<Node>? dependencies = null,
        string? importName = null)
        : this(name, scoped, (object)text, priority, dependencies, importName)
    {
    }

    private EmitHelper(
        string name,
        bool scoped,
        object text,
        int? priority,
        List<Node>? dependencies,
        string? importName)
    {
        Name = name;
        Scoped = scoped;
        Text = text;
        Priority = priority;
        Dependencies = dependencies;
        ImportName = importName;
    }

    /// <summary>A helper descriptor is not a syntax form; see the note on the class.</summary>
    public override SyntaxKind Kind => SyntaxKind.Unknown;

    /// <summary>A unique name for this helper.</summary>
    public string? Name { get; }

    /// <summary>Whether the helper MUST be emitted in the current scope.</summary>
    public bool Scoped { get; }

    /// <summary>ES3-compatible raw script text, or a <c>Func&lt;EmitHelperUniqueNameCallback, string&gt;</c> that yields
    /// such a string (see <see cref="EmitHelperUniqueNameCallback"/>).</summary>
    public object? Text { get; }

    /// <summary>Helpers with a higher priority are emitted earlier than other helpers on a node.</summary>
    public int? Priority { get; }

    /// <summary>Helpers whose text must be written before this one's (TS: <c>dependencies: EmitHelper[]</c>).</summary>
    public List<Node>? Dependencies { get; }

    /// <summary>The name of the helper to use when importing it (<c>--importHelpers</c>); unscoped helpers only.</summary>
    public string? ImportName { get; }
}
