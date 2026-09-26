namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:7665-7675 — interface DiagnosticMessage
/// <summary>
/// One entry of the <see cref="Diagnostics"/> table.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares <c>DiagnosticMessage</c> as an interface and writes the table as object literals, so the
/// port needs a constructible type for the generated table to instantiate.
/// <para>
/// PORT-NOTE: the three optional markers are booleans here. TS types them as <c>{}</c> (an object that only ever
/// exists to be truthy) and every use site tests them for truthiness, so a boolean carries the same information
/// without an allocation.
/// </para>
/// </remarks>
internal sealed class DiagnosticMessage : IDiagnosticMessage
{
    public DiagnosticMessage(
        int code,
        DiagnosticCategory category,
        string key,
        string message,
        bool reportsUnnecessary = false,
        bool elidedInCompatabilityPyramid = false,
        bool reportsDeprecated = false)
    {
        Code = code;
        Category = category;
        Key = key;
        Message = message;
        ReportsUnnecessary = reportsUnnecessary ? true : null;
        ElidedInCompatabilityPyramid = elidedInCompatabilityPyramid ? true : null;
        ReportsDeprecated = reportsDeprecated ? true : null;
    }

    /// <summary>The localization key: the mangled member name (capped at 100 characters) plus the code.</summary>
    public string Key { get; set; }

    public DiagnosticCategory Category { get; set; }

    public int Code { get; set; }

    /// <summary>The English message text, with <c>{0}</c>-style placeholders for arguments.</summary>
    public string Message { get; set; }

    public object? ReportsUnnecessary { get; set; }

    public object? ReportsDeprecated { get; set; }

    public bool? ElidedInCompatabilityPyramid { get; set; }

    // The generated interface annotates `key`/`message` as nullable, because it makes every reference member of
    // every interface nullable (the parser and factory fill them in). This class keeps the strict type — the
    // constructor never takes null — and bridges the interface explicitly, the same way generated classes bridge
    // narrowed members (porting decision D1, docs/architecture.md §12.2).
    string? IDiagnosticMessage.Key
    {
        get => Key;
        set => Key = value!;
    }

    string? IDiagnosticMessage.Message
    {
        get => Message;
        set => Message = value!;
    }
}
