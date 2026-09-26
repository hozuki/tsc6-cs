namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:4299-4305 (isSpecialPropertyDeclaration)
// ported from: src/compiler/utilities.ts:10203-10217 (tryParsePattern)
/// <summary>
/// The two <c>utilities.ts</c> members the binder's module/export slice needs and the M5-0 forward slice did not
/// carry: the JS property-declaration predicate <c>bindWorker</c>'s access-expression arm asks for, and the ambient
/// module pattern parser <c>bindModuleDeclaration</c> needs.
/// </summary>
/// <remarks>
/// PORT-NOTE: like the JS-expando batch (Utilities.JsExpando.cs), these land on the <c>Utilities</c> partial ahead of
/// the batch that owns <c>utilities.ts</c>, with the line-cited summary naming what each is, rather than being
/// duplicated into the binder — module resolution and <c>getAmbientModule</c> are what will read them next.
/// <para>
/// PORT-NOTE: <c>isSpecialPropertyDeclaration</c> is the reason this file is M5-4's: its only caller is
/// <c>bindWorker</c>'s <c>PropertyAccessExpression</c>/<c>ElementAccessExpression</c> arm (binder.ts:2872-2874), which
/// hands it to <c>bindSpecialPropertyDeclaration</c>. Its <c>getJSDocTypeTag</c> dependency (utilitiesPublic.ts:1204)
/// is the JSDoc family's (M5-3), so this file names <see cref="Utilities.GetJSDocTypeTag"/> rather than declaring it.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:4299-4305 — <c>isSpecialPropertyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>PropertyAccessExpression | ElementAccessExpression</c> union and its result
    /// is a guard narrowing to <c>PropertyAccessExpression | LiteralLikeElementAccessExpression</c>; the port takes
    /// <see cref="MemberExpression"/> — the base class both arms of that union share, and the port's established
    /// stand-in for the union — and answers <c>bool</c>. The body reads only <c>parent</c>, the element-access kind
    /// test and the literal-argument test, all of which the base class or the kind test carries.
    /// <para>
    /// PORT-NOTE: TS's <c>&amp;&amp;</c> chain becomes early returns so that <c>expr.parent</c> is read once and the
    /// <c>getJSDocTypeTag(expr.parent)</c> call sits behind the <c>ExpressionStatement</c> test, which is the order TS
    /// evaluates it in. The <c>!!</c> is the null test.
    /// </para>
    /// </remarks>
    public static bool IsSpecialPropertyDeclaration(MemberExpression expr)
    {
        if (!IsInJSFile(expr)) return false;
        if (expr.Parent is not { } parent || parent.Kind != SyntaxKind.ExpressionStatement) return false;
        if (NodeTests.IsElementAccessExpression(expr) && !IsLiteralLikeElementAccess(expr)) return false;
        return GetJSDocTypeTag(parent) is not null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10203-10217 — <c>tryParsePattern</c>.</summary>
    /// <remarks>
    /// Returns the input if there are no stars, a pattern if there is exactly one, and answers false if there are
    /// more.
    /// <para>
    /// PORT-NOTE: TS answers <c>string | Pattern | undefined</c> — three states with no C# anchor. The port states them
    /// as a <c>bool</c> result plus an <c>out</c> parameter, the .NET shape for a parse: <c>false</c> is TS's
    /// <c>undefined</c> (two or more stars), and a <c>true</c> result with a null <paramref name="pattern"/> is TS's
    /// plain-string answer (no star at all). The string themselves are the input, so no information is lost.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>pattern.substr(0, indexOfStar)</c>/<c>pattern.substr(indexOfStar + 1)</c> are C# ranges; the
    /// second <c>indexOf("*", indexOfStar + 1)</c> is <see cref="string.IndexOf(char, int)"/>.
    /// </para>
    /// </remarks>
    public static bool TryParsePattern(string text, out Core.Pattern? pattern)
    {
        var indexOfStar = text.IndexOf('*');
        if (indexOfStar == -1)
        {
            pattern = null;
            return true;
        }

        if (text.IndexOf('*', indexOfStar + 1) != -1)
        {
            pattern = null;
            return false;
        }

        pattern = new Core.Pattern(text[..indexOfStar], text[(indexOfStar + 1)..]);
        return true;
    }
}
