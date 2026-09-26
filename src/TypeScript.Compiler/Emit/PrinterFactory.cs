namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:1199-1209 — the four configured printer factories, plus the two concrete faces
// of the reference's option/handler object literals (`createPrinter`'s `printerOptions = {}` and `handlers = {}`
// defaults, emitter.ts:1211).

/// <summary>ported from: src/compiler/types.ts:9862-9879 — <c>PrinterOptions</c>'s constructible face.</summary>
/// <remarks>
/// PORT-NOTE: TS's printer options are a plain object literal (`createPrinter({ removeComments: true })`); the port's
/// generated face is the interface <see cref="IPrinterOptions"/>, so the port needs one implementation to build the
/// literals with. Every slot is nullable, exactly as the reference's optional members are, and the members are the
/// generated interface's, so this class adds no surface of its own.
/// </remarks>
internal sealed class PrinterOptions : IPrinterOptions
{
    public bool? RemoveComments { get; set; }

    public NewLineKind? NewLine { get; set; }

    public bool? OmitTrailingSemicolon { get; set; }

    public bool? NoEmitHelpers { get; set; }

    public ModuleKind? Module { get; set; }

    public ModuleResolutionKind? ModuleResolution { get; set; }

    public ScriptTarget? Target { get; set; }

    public bool? SourceMap { get; set; }

    public bool? InlineSourceMap { get; set; }

    public bool? InlineSources { get; set; }

    public bool? OmitBraceSourceMapPositions { get; set; }

    public bool? ExtendedDiagnostics { get; set; }

    public bool? OnlyPrintJsDocStyle { get; set; }

    public bool? NeverAsciiEscape { get; set; }

    public bool? StripInternal { get; set; }

    public bool? PreserveSourceNewlines { get; set; }

    public bool? TerminateUnterminatedLiterals { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:9820-9860 — <c>PrintHandlers</c>'s constructible face.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS destructures <c>handlers</c> with production defaults — <c>onEmitNode = noEmitNotification</c> and
/// <c>substituteNode = noEmitSubstitution</c> (emitter.ts:1217-1219) — while the other members stay <c>undefined</c>.
/// The generated <see cref="IPrintHandlers"/> states every member as a required method or property, so the port
/// supplies the reference's defaults here: <see cref="Default"/> is the handler set <c>createPrinter({})</c> uses, and
/// <see cref="Printer"/> recognises it by identity where the reference compares against the two module-level
/// defaults (emitter.ts:1502, 1507, 1911).
/// </para>
/// <para>
/// The two members whose reference default is “absent” are stated as follows. <c>hasGlobalName</c> defaults to a
/// function answering <c>false</c>, which is the answer <c>isFileLevelUniqueName</c> reaches when the handler is
/// missing (<c>!(hasGlobalName &amp;&amp; hasGlobalName(name))</c>, utilities.ts:1062-1064). <c>isEmitNotificationEnabled</c>
/// defaults to <c>true</c>, which is the reference's <c>!isEmitNotificationEnabled || …</c> answer when that handler is
/// missing (emitter.ts:1502).
/// </para>
/// </remarks>
internal sealed class PrintHandlers : IPrintHandlers
{
    /// <summary>ported from: src/compiler/emitter.ts:1211 — the default <c>handlers = {}</c> of <c>createPrinter</c>.</summary>
    internal static readonly PrintHandlers Default = new();

    public bool HasGlobalName(string? name) => false;

    public void OnEmitNode(EmitHint hint, Node? node, Action<EmitHint, Node>? emitCallback)
        => Printer.NoEmitNotification(hint, node, emitCallback);

    public bool IsEmitNotificationEnabled(Node? node) => true;

    public Node SubstituteNode(EmitHint hint, Node? node) => Printer.NoEmitSubstitution(hint, node);

    public Action<EmitHint, Node, Action<EmitHint, Node>>? OnEmitSourceMapOfNode { get; set; }

    public Func<Node, SyntaxKind, Action<string>, int, Func<SyntaxKind, Action<string>, int, int>, int>? OnEmitSourceMapOfToken { get; set; }

    public Action<int>? OnEmitSourceMapOfPosition { get; set; }

    public Action<SourceFile>? OnSetSourceFile { get; set; }

    public Action<Node>? OnBeforeEmitNode { get; set; }

    public Action<Node>? OnAfterEmitNode { get; set; }

    public Action<NodeArray<object?>>? OnBeforeEmitNodeArray { get; set; }

    public Action<NodeArray<object?>>? OnAfterEmitNodeArray { get; set; }

    public Action<Node>? OnBeforeEmitToken { get; set; }

    public Action<Node>? OnAfterEmitToken { get; set; }
}

/// <summary>ported from: src/compiler/emitter.ts:1199-1209 — the memoized configured printers.</summary>
/// <remarks>
/// PORT-NOTE: TS exports four <c>/* @__PURE__ */ memoize(() =&gt; createPrinter(...))</c> constants; the port states them
/// as four no-argument static methods (the checker's <c>*ToString</c> gates call them by name) over
/// <see cref="Core.Memoize{T}"/>, which is the port's <c>memoize</c>.
/// </remarks>
internal static class PrinterFactory
{
    private static readonly Func<Printer> s_printerWithDefaults = Core.Memoize(() => new Printer());

    private static readonly Func<Printer> s_printerWithRemoveComments = Core.Memoize(() => new Printer(new PrinterOptions { RemoveComments = true }));

    private static readonly Func<Printer> s_printerWithRemoveCommentsNeverAsciiEscape = Core.Memoize(() => new Printer(new PrinterOptions { RemoveComments = true, NeverAsciiEscape = true }));

    private static readonly Func<Printer> s_printerWithRemoveCommentsOmitTrailingSemicolon = Core.Memoize(() => new Printer(new PrinterOptions { RemoveComments = true, OmitTrailingSemicolon = true }));

    /// <summary>ported from: src/compiler/emitter.ts:1199 — <c>createPrinterWithDefaults</c>.</summary>
    internal static Printer CreatePrinterWithDefaults() => s_printerWithDefaults();

    /// <summary>ported from: src/compiler/emitter.ts:1202 — <c>createPrinterWithRemoveComments</c>.</summary>
    internal static Printer CreatePrinterWithRemoveComments() => s_printerWithRemoveComments();

    /// <summary>ported from: src/compiler/emitter.ts:1205 — <c>createPrinterWithRemoveCommentsNeverAsciiEscape</c>.</summary>
    internal static Printer CreatePrinterWithRemoveCommentsNeverAsciiEscape() => s_printerWithRemoveCommentsNeverAsciiEscape();

    /// <summary>ported from: src/compiler/emitter.ts:1208 — <c>createPrinterWithRemoveCommentsOmitTrailingSemicolon</c>.</summary>
    internal static Printer CreatePrinterWithRemoveCommentsOmitTrailingSemicolon() => s_printerWithRemoveCommentsOmitTrailingSemicolon();
}
