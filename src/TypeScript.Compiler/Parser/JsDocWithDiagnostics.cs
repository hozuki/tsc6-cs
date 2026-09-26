namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:1409-1415 — interface JsDocWithDiagnostics
/// <summary>A JSDoc comment parsed on its own, with whatever the parse made of it.</summary>
/// <remarks>
/// PORT-NOTE: TS declares this interface in parser.ts and its two users build it as an object literal; the port needs a
/// constructible type (the same treatment <c>CallBinding</c> and <c>CreateSourceFileOptions</c> got).
/// </remarks>
internal sealed class JsDocWithDiagnostics
{
    public JsDocWithDiagnostics(JSDoc jsDoc, List<Diagnostic> diagnostics)
    {
        JsDoc = jsDoc;
        Diagnostics = diagnostics;
    }

    public JSDoc JsDoc { get; set; }

    public List<Diagnostic> Diagnostics { get; set; }
}
