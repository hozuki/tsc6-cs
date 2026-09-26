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

// ported from: src/compiler/types.ts:7667 — interface DiagnosticMessage's `args`, plus utilities.ts:8705-8712 (`formatMessage`)
/// <summary>
/// One argument of a diagnostic message whose text is computed on the first read of that message.
/// </summary>
/// <remarks>
/// PORT-NOTE (deviation, ruling A″ of the M6-late plan): the reference computes every argument at the call site, and the
/// four string converters (<c>typeToString</c> at checker.ts:6240-6265, <c>symbolToString</c> at 6169-6201,
/// <c>signatureToString</c> at 6202-6239, <c>typePredicateToString</c> at 11175-11187) run the printer, which is M7's
/// and therefore a loud blocker in the port. Wrapping such an argument in <see cref="Diagnostic.DeferArg"/> keeps the
/// string bit-for-bit identical while moving its computation — and the node building inside it — to the first read of
/// the message, which is the same point A′ already moved the <c>formatMessage</c> call to
/// (<see cref="Diagnostic.Deferred"/>): a reader that never asks for the text no longer trips the gate, and every
/// reader that does still does. The one ordering-observable reader is <c>compareMessageText</c>
/// (utilities.ts:8807, read from <c>compareDiagnosticsSkipRelatedInformation</c> at utilities.ts:8787-8794), whose
/// tie-break point does not move.
/// <para>
/// The value is memoised on first read, exactly like A′'s formatted text, so a message read more than once (a sort
/// followed by a dump, for instance) stringifies once.
/// </para>
/// </remarks>
internal sealed class DiagnosticArgument
{
    private Func<string?>? _factory;
    private string? _text;

    public DiagnosticArgument(Func<string?> factory) => _factory = factory;

    /// <summary>
    /// The argument's text, computed on the first call and remembered afterwards; <c>null</c> means the argument
    /// itself would have been null, which <c>formatStringFromArgs</c> renders as <c>"undefined"</c>.
    /// </summary>
    internal string? Resolve()
    {
        if (_factory is not null)
        {
            _text = _factory();
            _factory = null;
        }

        return _text;
    }

    /// <summary>
    /// The same text, for the readers that stringify an argument themselves — <c>Convert.ToString</c> in
    /// <c>Utilities.FormatStringFromArgs</c> reaches this by falling back to <see cref="object.ToString"/>.
    /// </summary>
    public override string ToString() => Resolve() ?? "";
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
        _messageText = messageText;
        Category = category;
        Code = code;
        Start = start;
        Length = length;
        FileName = fileName;
        File = file;
    }

    /// <summary>The message of a diagnostic built by <see cref="Deferred"/> that nobody has read yet.</summary>
    private DiagnosticMessage? _deferredMessage;

    /// <summary>The arguments of a diagnostic built by <see cref="Deferred"/> that nobody has read yet.</summary>
    private object?[]? _deferredArgs;

    /// <summary>The formatted message, or whatever the constructor was handed (a <see cref="string"/>, or a chain).</summary>
    private object? _messageText;

    /// <summary>
    /// ported from: src/compiler/utilities.ts:8682-8701 (<c>createFileDiagnostic</c>), src/compiler/utilities.ts:8705-8712
    /// (<c>formatMessage</c>), src/compiler/utilities.ts:8609-8618 (<c>createDetachedDiagnostic</c>),
    /// src/compiler/utilities.ts:8716-8734 (<c>createCompilerDiagnostic</c>) — the formatting those three do up front,
    /// deferred to the first read of <see cref="MessageText"/>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE (deviation, ruling A′ of the M6-late plan): the reference formats the message *while building* the
    /// diagnostic, which calls <c>typeToString</c>/<c>symbolToString</c> for any argument that is a type or a symbol —
    /// and those are printer-gated in the port (<c>checker.ts:6240-6264</c>, M7). Formatting on first read is
    /// observationally identical (the same string, from the same <c>getLocaleSpecificMessage</c> +
    /// <c>formatStringFromArgs</c> pair) *except* that it happens later, which is the whole point: a caller that never
    /// reads the text — the differential harness's dump, which records only code/start/length/category — no longer
    /// trips the gate. The one ordering-observable reader is <c>compareMessageText</c> (utilities.ts:8807), and it
    /// reads the text only in the tie-break after file/start/length/code compared equal, exactly as the reference does
    /// (<c>compareDiagnosticsSkipRelatedInformation</c>, utilities.ts:8787-8794) — so the deferred read there happens
    /// at exactly the reference's own point of use, and ordering cannot differ.
    /// </remarks>
    internal static Diagnostic Deferred(
        DiagnosticMessage message,
        object?[] args,
        int? start = null,
        int? length = null,
        string? fileName = null,
        SourceFile? file = null)
    {
        return new Diagnostic(null, message.Category, message.Code, start, length, fileName, file)
        {
            _deferredMessage = message,
            _deferredArgs = args,
        };
    }

    /// <summary>
    /// One argument of <see cref="Deferred"/> whose text is computed on the first read of the message rather than at
    /// the call site — see <see cref="DiagnosticArgument"/> for why and for what it does not change.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the call sites are exactly the ones where the reference passes the result of a printer-gated
    /// converter (<c>typeToString</c>, <c>symbolToString</c>, <c>signatureToString</c>, <c>typePredicateToString</c>)
    /// as a message argument.
    /// </remarks>
    internal static DiagnosticArgument DeferArg(Func<string> factory) => new(factory);

    /// <summary>
    /// Hands this diagnostic's message to <paramref name="target"/> without formatting it, for the copies the
    /// reference makes by spreading a diagnostic into a new object literal.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the three copies are <c>{ ...diagnostic, category: Suggestion }</c> at checker.ts:2589
    /// (<c>addErrorOrSuggestion</c>) and checker.ts:53784 (<c>getSuggestionDiagnostics</c>), and
    /// <c>attachFileToDiagnostic</c>'s literal at utilities.ts:8627-8636. The reference's spread reads
    /// <c>messageText</c> as it stands (a formatted string); the port's may still be deferred, so the copy takes the
    /// deferred payload over instead of forcing it — the two then format the same string on first read.
    /// </remarks>
    internal void ShareMessageText(Diagnostic target)
    {
        target._messageText = _messageText;
        target._deferredMessage = _deferredMessage;
        target._deferredArgs = _deferredArgs;
    }

    /// <summary>The file this diagnostic has been attached to, once a program knows which one it is.</summary>
    public SourceFile? File { get; set; }

    /// <summary>The file name a detached diagnostic carries; the attached form reads it from <see cref="File"/>.</summary>
    public string? FileName { get; set; }

    public int? Start { get; set; }

    public int? Length { get; set; }

    /// <summary>
    /// The message, with its arguments substituted (a <see cref="string"/>, or a message chain) — formatted on the
    /// first read when the diagnostic was built by <see cref="Deferred"/>, and remembered from then on.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: see <see cref="Deferred"/> for why the formatting is deferred (ruling A′) and why that cannot change
    /// the string or any ordering that reads it.
    /// </remarks>
    public object? MessageText
    {
        get
        {
            if (_deferredMessage is null)
            {
                return _messageText;
            }

            var text = Utilities.GetLocaleSpecificMessage(_deferredMessage);
            if (Core.Some(_deferredArgs))
            {
                text = Utilities.FormatStringFromArgs(text, _deferredArgs!);
            }

            _messageText = text;
            _deferredMessage = null;
            _deferredArgs = null;
            return text;
        }

        set
        {
            _messageText = value;
            _deferredMessage = null;
            _deferredArgs = null;
        }
    }

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

// ported from: src/compiler/types.ts:4426-4433 — interface DiagnosticMessageChain
/// <summary>
/// A diagnostic whose message is a chain (a head message plus elaborations) — TS's <c>DiagnosticMessageChain</c>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this as an interface and <c>chainDiagnosticMessages</c> (utilities.ts:8751-8765) builds it
/// as an object literal, so the port needs the constructible class — the same treatment <c>DiagnosticMessage</c> got.
/// <see cref="IDiagnosticMessageChain.RepopulateInfo"/> exists for the localization path (M8) and is left null.
/// </remarks>
internal sealed class DiagnosticMessageChain : IDiagnosticMessageChain
{
    /// <summary>ported from: src/compiler/utilities.ts:8751-8765 — the <c>chainDiagnosticMessages</c> literal.</summary>
    public DiagnosticMessageChain(string? messageText, DiagnosticCategory category, int code, List<IDiagnosticMessageChain>? next = null)
    {
        _messageText = messageText;
        Category = category;
        Code = code;
        Next = next;
    }

    /// <summary>The message of a chain built by <see cref="Deferred"/> that nobody has read yet.</summary>
    private DiagnosticMessage? _deferredMessage;

    /// <summary>The arguments of a chain built by <see cref="Deferred"/> that nobody has read yet.</summary>
    private object?[]? _deferredArgs;

    /// <summary>The formatted message, or whatever the constructor was handed.</summary>
    private string? _messageText;

    /// <summary>
    /// Builds a chain whose text is formatted on the first read of <see cref="MessageText"/>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE (deviation, ruling A″ of the M6-late plan): the reference formats the message while building the
    /// chain (<c>chainDiagnosticMessages</c>, utilities.ts:8759-8761), and its arguments are evaluated at the call site
    /// before that. Deferring the same pair to the first read is what lets a caller pass
    /// <see cref="Diagnostic.DeferArg"/> for the printer-gated converters; see <see cref="DiagnosticArgument"/>. The
    /// string is identical and <c>compareMessageText</c> (utilities.ts:8807) reads it at the reference's own tie-break
    /// point, so no ordering can differ.
    /// </remarks>
    internal static DiagnosticMessageChain Deferred(
        DiagnosticMessage message,
        object?[] args,
        List<IDiagnosticMessageChain>? next = null)
        => new(null, message.Category, message.Code, next)
        {
            _deferredMessage = message,
            _deferredArgs = args,
        };

    /// <summary>ported from: src/compiler/types.ts:4427 — <c>DiagnosticMessageChain.messageText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: formatted on the first read when the chain was built by <see cref="Deferred"/>, and remembered from
    /// then on — the same treatment <see cref="Diagnostic.MessageText"/> gives a diagnostic (ruling A′).
    /// </remarks>
    public string? MessageText
    {
        get
        {
            if (_deferredMessage is null)
            {
                return _messageText;
            }

            var text = Utilities.GetLocaleSpecificMessage(_deferredMessage);
            if (Core.Some(_deferredArgs))
            {
                text = Utilities.FormatStringFromArgs(text, _deferredArgs!);
            }

            _messageText = text;
            _deferredMessage = null;
            _deferredArgs = null;
            return text;
        }

        set
        {
            _messageText = value;
            _deferredMessage = null;
            _deferredArgs = null;
        }
    }

    /// <summary>ported from: src/compiler/types.ts:4428 — <c>DiagnosticMessageChain.category</c>.</summary>
    public DiagnosticCategory Category { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4429 — <c>DiagnosticMessageChain.code</c>.</summary>
    public int Code { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4430 — <c>DiagnosticMessageChain.next</c>.</summary>
    public List<IDiagnosticMessageChain>? Next { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4431 — <c>DiagnosticMessageChain.repopulateInfo</c>.</summary>
    public Func<object?>? RepopulateInfo { get; set; }

    /// <summary>ported from: src/compiler/types.ts:4432 — <c>DiagnosticMessageChain.canonicalHead</c>.</summary>
    public ICanonicalDiagnostic? CanonicalHead { get; set; }
}
