namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:1595-1623 (Token / PunctuationToken / KeywordToken / ModifierToken)
/// <summary>
/// A leaf token. Every punctuator and keyword is an instance of this one type.
/// </summary>
/// <remarks>
/// Hand-written, and deliberately <em>not</em> one class per kind. TS declares its tokens as type aliases
/// over a generic (<c>type DotToken = PunctuationToken&lt;SyntaxKind.DotToken&gt;</c>), so all 187 of them are
/// the same runtime shape and differ only by kind. Emitting 187 empty C# classes would add nothing the
/// <see cref="SyntaxKind"/> value does not already carry.
/// <para>
/// PORT-NOTE: the generic <c>TKind</c> parameter is dropped. In TS it makes <c>DotToken</c> a distinct *type*
/// from <c>CommaToken</c>; in C# that distinction cannot exist on a class whose kind is a runtime value, and
/// every use site switches on <see cref="Node.Kind"/> anyway.
/// </para>
/// </remarks>
internal class Token : Node, IJSDocContainer
{
    private readonly SyntaxKind _kind;

    public Token(SyntaxKind kind) => _kind = kind;

    public override SyntaxKind Kind => _kind;

    // ported from: types.ts:957-960 (JSDocContainer) and types.ts:1596 (`EndOfFileToken = Token<…> & JSDocContainer`)
    /// <remarks>
    /// PORT-NOTE: TS reaches this through an intersection type — the end-of-file token is the one token that carries
    /// JSDoc, and it gets the slot structurally. The port declares the interface on the class instead, which is the same
    /// trade the keyword-type tokens got (see <c>KeywordTypeToken</c>): every token then has the slot, and the parser is
    /// what fills it (only for the end-of-file token).
    /// </remarks>
    public IJSDocArray? JsDoc { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:1602-1603 — TS: <c>PunctuationToken&lt;TKind&gt;</c>.</summary>
internal class PunctuationToken : Token
{
    public PunctuationToken(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>ported from: src/compiler/types.ts:1621-1622 — TS: <c>KeywordToken&lt;TKind&gt;</c>.</summary>
internal class KeywordToken : Token
{
    public KeywordToken(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>ported from: src/compiler/types.ts:1629-1631 — TS: <c>ModifierToken&lt;TKind&gt;</c>.</summary>
internal class ModifierToken : KeywordToken
{
    public ModifierToken(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>ported from: src/compiler/types.ts — TS: <c>KeywordTypeNode&lt;TKind&gt;</c>.</summary>
/// <remarks>
/// PORT-NOTE: a keyword that is also a type (<c>number</c>, <c>string</c>, …) is one runtime object like every other
/// token — TS only gives it a different <em>type</em>, <c>KeywordTypeNode&lt;TKind&gt;</c>, through structural
/// compatibility. C# needs a class to say so, and it has to be a class of its own: putting <c>ITypeNode</c> on
/// <c>KeywordToken</c> would turn `export` and `const` into type nodes too, and <c>ModifierToken</c> derives from it.
/// </remarks>
internal class KeywordTypeToken : Token, IKeywordTypeNode<SyntaxKind>
{
    public KeywordTypeToken(SyntaxKind kind)
        : base(kind)
    {
    }
}
