namespace TypeScript.Compiler;

// ported from: src/compiler/utilitiesPublic.ts:820-846 (isParseTreeNode, getParseTreeNode)
// ported from: src/compiler/utilities.ts:984-989 (getSourceFileOfNode)
/// <summary>
/// Utilities that Layer 3 needs ahead of Layer 5.
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>utilities.ts</c> is a Layer 5 file (11,376 lines), but <c>factory/emitNode.ts</c> and the factory
/// itself depend on a handful of its functions. Only those are ported here; the rest arrives with M5. Keeping them in
/// a partial class named after the TS module means the later batches extend this file rather than duplicate it.
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilitiesPublic.ts:946 — <c>isNamedDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows to <c>NamedDeclaration &amp; { name: DeclarationName }</c>, which is what lets a caller
    /// read <c>name</c> without another check. The port answers with the interface, and its <c>Name</c> is
    /// <c>object?</c> — that is what <c>DeclarationName</c> maps to (§12.5) — so callers narrow once more.
    /// </remarks>
    public static bool IsNamedDeclaration(Node node) => node is INamedDeclaration { Name: not null };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1657 — <c>isPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes five <c>kind ===</c> comparisons. The port asks the same question with a kind pattern
    /// rather than an <c>is X</c> test: several of these kinds share a C# class (<c>StringLiteral</c> and
    /// <c>NumericLiteral</c> are separate types here, but a test on the class would not be the question TS asks).
    /// </remarks>
    public static bool IsPropertyName(Node node)
        => node.Kind is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier or SyntaxKind.StringLiteral
            or SyntaxKind.NumericLiteral or SyntaxKind.ComputedPropertyName;

    /// <summary>ported from: src/compiler/factory/utilitiesPublic.ts:10 — <c>setTextRange</c>.</summary>
    public static T SetTextRange<T>(T range, ITextRange? location)
        where T : ITextRange
    {
        if (location is null) return range;

        range.Pos = location.Pos;
        range.End = location.End;
        return range;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:842 — <c>escapeLeadingUnderscores</c>.</summary>
    /// <remarks>
    /// An identifier that starts with two underscores gets a third one, so it cannot collide with a magic name such as
    /// <c>__proto__</c>. PORT-NOTE: TS returns a branded <c>__String</c>; the port's <c>__String</c> is still
    /// <c>string</c> until D4's pool lands.
    /// </remarks>
    public static string EscapeLeadingUnderscores(string identifier)
        => identifier.Length >= 2 && identifier[0] == '_' && identifier[1] == '_' ? "_" + identifier : identifier;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:852 — <c>unescapeLeadingUnderscores</c>.</summary>
    public static string UnescapeLeadingUnderscores(string identifier)
        => identifier.Length >= 3 && identifier[0] == '_' && identifier[1] == '_' && identifier[2] == '_'
            ? identifier[1..]
            : identifier;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:860 — <c>idText</c>.</summary>
    public static string IdText(Node node) => UnescapeLeadingUnderscores(node switch
    {
        Identifier identifier => identifier.EscapedText ?? "",
        PrivateIdentifier privateIdentifier => privateIdentifier.EscapedText ?? "",
        _ => "",
    });

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1359 — <c>isMemberName</c>.</summary>
    public static bool IsMemberName(Node node) => node.Kind is SyntaxKind.Identifier or SyntaxKind.PrivateIdentifier;

    /// <summary>ported from: src/compiler/utilities.ts:5439 — <c>getTextOfIdentifierOrLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's remaining branch handles a JSX namespaced name, whose helper arrives with M4e. A node that is
    /// neither a member name nor one of the literal spellings therefore answers <c>null</c> instead of a name.
    /// </remarks>
    public static string? GetTextOfIdentifierOrLiteral(Node node)
    {
        if (IsMemberName(node)) return IdText(node);

        return node switch
        {
            StringLiteral literal => literal.Text,
            NumericLiteral numeric => numeric.Text,
            NoSubstitutionTemplateLiteral template => template.Text,
            _ => null,
        };
    }

    /// <summary>
    /// Walks up the parent chain to the enclosing source file.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS also declares a non-nullable overload that assumes a source file exists. The port returns the
    /// nullable one and lets callers handle the absence, because the caller that reaches the end of the chain in TS
    /// would be relying on the type system rather than a runtime guarantee.
    /// </remarks>
    public static SourceFile? GetSourceFileOfNode(Node? node)
    {
        while (node is not null && node.Kind != SyntaxKind.SourceFile)
        {
            node = node.Parent;
        }

        return node as SourceFile;
    }

    /// <summary>
    /// Whether the node came from parsing rather than from a factory or a transform — i.e. whether it is part of the
    /// parse tree that <c>nodeChildren</c> and the emit-node cleanup annotate.
    /// </summary>
    public static bool IsParseTreeNode(Node node) => (node.Flags & NodeFlags.Synthesized) == 0;

    /// <summary>
    /// The parse-tree node a node was derived from, or the node itself when it already is one.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <paramref name="nodeTest"/> is applied only to the nodes found while walking <c>original</c>
    /// links, not to the early return — TS writes it that way, and a caller passing a test for a synthesized node
    /// relies on getting that node back rather than <c>undefined</c>.
    /// </remarks>
    public static Node? GetParseTreeNode(Node? node, Func<Node, bool>? nodeTest = null)
    {
        if (node is null || IsParseTreeNode(node))
        {
            return node;
        }

        node = node.Original;
        while (node is not null)
        {
            if (IsParseTreeNode(node))
            {
                return nodeTest is null || nodeTest(node) ? node : null;
            }

            node = node.Original;
        }

        return null;
    }

    /// <summary>
    /// The parse-tree node a node was derived from, when it also satisfies <paramref name="nodeTest"/>.
    /// </summary>
    public static T? GetParseTreeNode<T>(Node? node, Func<Node, bool>? nodeTest = null)
        where T : Node => GetParseTreeNode(node, nodeTest) as T;
}
