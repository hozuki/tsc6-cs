namespace TypeScript.Compiler;

// ported from: src/compiler/binder.ts:3072-3093 (the JSDoc arms of bindWorker's switch)
/// <summary>
/// The JSDoc arms of <c>bindWorker</c>: the two predefined-tag families that become declarations and the three
/// deferred sets whose binding must wait until the enclosing scope is fully bound.
/// </summary>
/// <remarks>
/// PORT-NOTE: the arms sit in their own file because they are what ties the dispatch to the JSDoc deferral state
/// (<c>_delayedTypeAliases</c>, <c>_jsDocImports</c>) and to <c>DelayedBindJSDocTypedefTag</c>/<c>BindJSDocImports</c>,
/// which drain that state at the end of the file's bind (binder.ts:584-585). Both drains belong to M5-3.
/// <para>
/// PORT-NOTE: TS's <c>JSDocPropertyTag</c> case reads <c>propTag.typeExpression &amp;&amp; ...</c>, a truthiness test on
/// a nullable slot; the port's <c>?.</c> chain answers the same. TS's <c>JSDocParameterTag</c> case falls through to it
/// when the tag's parent is a <c>JSDocTypeLiteral</c>, which is the shared helper below.
/// </para>
/// </remarks>
internal partial class Binder
{
    /// <summary>ported from: src/compiler/binder.ts:3072-3079 — <c>bindWorker</c>'s <c>JSDocParameterTag</c> arm.</summary>
    private static void BindJSDocParameterWorker(Binder binder, Node node)
    {
        if (node.Parent?.Kind == SyntaxKind.JSDocSignature)
        {
            binder.BindParameter(node);
            return;
        }

        if (node.Parent?.Kind != SyntaxKind.JSDocTypeLiteral)
        {
            return;
        }

        // falls through to the JSDocPropertyTag arm
        BindJSDocPropertyLikeTag(binder, node);
    }

    /// <summary>ported from: src/compiler/binder.ts:3080-3085 — <c>bindWorker</c>'s <c>JSDocPropertyTag</c> arm.</summary>
    private static void BindJSDocPropertyWorker(Binder binder, Node node) => BindJSDocPropertyLikeTag(binder, node);

    /// <summary>
    /// ported from: src/compiler/binder.ts:3080-3085 — the tail shared by the <c>JSDocParameterTag</c> and
    /// <c>JSDocPropertyTag</c> arms.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>propTag</c> is the <c>JSDocPropertyLikeTag</c> union; the port's class of that name is the
    /// abstract base both tags derive from, so it is the parameter type.
    /// </remarks>
    private static void BindJSDocPropertyLikeTag(Binder binder, Node node)
    {
        var propTag = (JSDocPropertyLikeTag)node;
        var flags = propTag.IsBracketed || propTag.TypeExpression?.Type?.Kind == SyntaxKind.JSDocOptionalType
            ? SymbolFlags.Property | SymbolFlags.Optional
            : SymbolFlags.Property;
        binder.DeclareSymbolAndAddToSymbolTable(propTag, flags, SymbolFlags.PropertyExcludes);
    }

    /// <summary>
    /// ported from: src/compiler/binder.ts:3086-3089 — <c>bindWorker</c>'s <c>JSDocTypedefTag</c> /
    /// <c>JSDocCallbackTag</c> / <c>JSDocEnumTag</c> arm.
    /// </summary>
    private static void BindJSDocTypeAliasTagWorker(Binder binder, Node node)
        => (binder._delayedTypeAliases ??= []).Add((JSDocTag)node);

    /// <summary>ported from: src/compiler/binder.ts:3090-3091 — <c>bindWorker</c>'s <c>JSDocOverloadTag</c> arm.</summary>
    private static void BindJSDocOverloadTagWorker(Binder binder, Node node)
        => binder.Bind(((JSDocOverloadTag)node).TypeExpression);

    /// <summary>ported from: src/compiler/binder.ts:3092-3093 — <c>bindWorker</c>'s <c>JSDocImportTag</c> arm.</summary>
    private static void BindJSDocImportTagWorker(Binder binder, Node node)
        => (binder._jsDocImports ??= []).Add((JSDocImportTag)node);

    /// <summary>ported from: src/compiler/binder.ts:3072-3093 — the JSDoc kinds in the switch.</summary>
    private static void AddJsDocWorkerHandlers(WorkerHandler?[] handlers)
    {
        Register(handlers, SyntaxKind.JSDocParameterTag, BindJSDocParameterWorker);
        Register(handlers, SyntaxKind.JSDocPropertyTag, BindJSDocPropertyWorker);
        Register(handlers, SyntaxKind.JSDocTypedefTag, BindJSDocTypeAliasTagWorker);
        Register(handlers, SyntaxKind.JSDocCallbackTag, BindJSDocTypeAliasTagWorker);
        Register(handlers, SyntaxKind.JSDocEnumTag, BindJSDocTypeAliasTagWorker);
        Register(handlers, SyntaxKind.JSDocOverloadTag, BindJSDocOverloadTagWorker);
        Register(handlers, SyntaxKind.JSDocImportTag, BindJSDocImportTagWorker);
    }
}
