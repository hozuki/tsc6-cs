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

// ported from: src/compiler/types.ts:7697-7705 — interfaces Diagnostic, DiagnosticWithDetachedLocation, DiagnosticWithLocation
/// <summary>
/// One diagnostic: what went wrong, where, and which message it is.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares three interfaces — <c>Diagnostic</c>, <c>DiagnosticWithDetachedLocation</c> (a diagnostic that
/// knows its file name but has not been attached to a <c>SourceFile</c> yet, which is what the parser produces) and
/// <c>DiagnosticWithLocation</c> (the attached form) — and builds them all as object literals. The port has one
/// constructible class implementing all three, because the difference between them is one field
/// (<see cref="File"/> versus <see cref="FileName"/>) and <c>attachFileToDiagnostics</c> turns one into the other by
/// copying. That mirrors what the Go port ended up doing with a single <c>Diagnostic</c> plus a nullable file.
/// <para>
/// PORT-NOTE: TS types <c>messageText</c> as <c>string | DiagnosticMessageChain</c> and the port keeps it as
/// <c>object?</c>, so it holds a <see cref="string"/> (the normal case — <c>createDetachedDiagnostic</c> formats the
/// message up front) or an <see cref="IDiagnosticMessageChain"/>.
/// </para>
/// </remarks>
internal sealed class Diagnostic : IDiagnosticWithDetachedLocation, IDiagnosticWithLocation
{
    public Diagnostic(
        object? messageText,
        DiagnosticCategory category,
        int code,
        int? start = null,
        int? length = null,
        string? fileName = null,
        SourceFile? file = null)
    {
        MessageText = messageText;
        Category = category;
        Code = code;
        Start = start;
        Length = length;
        FileName = fileName;
        File = file;
    }

    /// <summary>The file this diagnostic has been attached to, once a program knows which one it is.</summary>
    public SourceFile? File { get; set; }

    /// <summary>The file name a detached diagnostic carries; the attached form reads it from <see cref="File"/>.</summary>
    public string? FileName { get; set; }

    public int? Start { get; set; }

    public int? Length { get; set; }

    /// <summary>The message, with its arguments already substituted (a <see cref="string"/>, or a message chain).</summary>
    public object? MessageText { get; set; }

    public DiagnosticCategory Category { get; set; }

    public int Code { get; set; }

    /// <summary>A custom prefix shown instead of <c>TS</c> before the code, when the diagnostic did not come from here.</summary>
    public string? Source { get; set; }

    public List<IDiagnosticRelatedInformation>? RelatedInformation { get; set; }

    public object? ReportsUnnecessary { get; set; }

    public object? ReportsDeprecated { get; set; }

    public object? SkippedOn { get; set; }

    public ICanonicalDiagnostic? CanonicalHead { get; set; }
}
