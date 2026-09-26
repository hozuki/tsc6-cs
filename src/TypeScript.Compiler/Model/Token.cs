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
internal class Token : Node, IJsDocContainer
{
    private readonly SyntaxKind _kind;

    public Token(SyntaxKind kind) => _kind = kind;

    public override SyntaxKind Kind => _kind;

    // ported from: types.ts:957-960 (JSDocContainer)
    public JSDocArray? JsDoc { get; set; }
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
internal sealed class ModifierToken : KeywordToken
{
    public ModifierToken(SyntaxKind kind)
        : base(kind)
    {
    }
}
