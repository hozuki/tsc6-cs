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
/// <para>
/// M4d widened that slice: the parenthesizer decides where parentheses go from the operator-precedence tables, and
/// <c>factory/utilities.ts</c> asks the same kind of questions about statements and emit flags. So this file now also
/// carries the expression classification and precedence helpers, <c>getLeftmostExpression</c>, the <c>skip*</c> family,
/// the statement predicates and the two identifiers' bookkeeping helpers (<c>getEmitFlags</c>, <c>getNodeId</c>) — each
/// with its own <c>ported from</c> line naming the file and range it came from. <c>checker.ts</c> is the one that is not
/// from Layer 5 at all: the factory needs <c>getNodeId</c> to spell a generated name for a non-member node, and the
/// checker reads back the same id.
/// </para>
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

    /// <summary>ported from: src/compiler/utilities.ts:6798-6825 — <c>isThisIdentifier</c>.</summary>
    public static bool IsThisIdentifier(Node? node)
        => node is Identifier identifier && IdentifierIsThisKeyword(identifier);

    /// <summary>ported from: src/compiler/utilities.ts:6822 — <c>identifierIsThisKeyword</c>.</summary>
    public static bool IdentifierIsThisKeyword(Identifier id) => id.EscapedText == "this";

    /// <summary>ported from: src/compiler/utilities.ts:7353 — <c>modifiersToFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>utilities.ts</c> is a Layer 5 file, and this is the part the factory needs ahead of it — the same
    /// arrangement as <see cref="IsParseTreeNode"/>. TS takes <c>readonly ModifierLike[]</c>, which is an
    /// <c>IEnumerable&lt;Node&gt;</c> here: <c>ModifierLike</c> collapses to <c>Node</c> (§12.5), and enumerating is what
    /// lets a <c>NodeArray&lt;ModifierToken&gt;</c> be passed where TS relies on array covariance.
    /// </remarks>
    public static ModifierFlags ModifiersToFlags(IEnumerable<Node>? modifiers)
    {
        var flags = ModifierFlags.None;
        if (modifiers is not null)
        {
            foreach (var modifier in modifiers) flags |= ModifierToFlag(modifier.Kind);
        }

        return flags;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7364 — <c>modifierToFlag</c>.</summary>
    public static ModifierFlags ModifierToFlag(SyntaxKind token) => token switch
    {
        SyntaxKind.StaticKeyword => ModifierFlags.Static,
        SyntaxKind.PublicKeyword => ModifierFlags.Public,
        SyntaxKind.ProtectedKeyword => ModifierFlags.Protected,
        SyntaxKind.PrivateKeyword => ModifierFlags.Private,
        SyntaxKind.AbstractKeyword => ModifierFlags.Abstract,
        SyntaxKind.AccessorKeyword => ModifierFlags.Accessor,
        SyntaxKind.ExportKeyword => ModifierFlags.Export,
        SyntaxKind.DeclareKeyword => ModifierFlags.Ambient,
        SyntaxKind.ConstKeyword => ModifierFlags.Const,
        SyntaxKind.DefaultKeyword => ModifierFlags.Default,
        SyntaxKind.AsyncKeyword => ModifierFlags.Async,
        SyntaxKind.ReadonlyKeyword => ModifierFlags.Readonly,
        SyntaxKind.OverrideKeyword => ModifierFlags.Override,
        SyntaxKind.InKeyword => ModifierFlags.In,
        SyntaxKind.OutKeyword => ModifierFlags.Out,
        SyntaxKind.Decorator => ModifierFlags.Decorator,
        _ => ModifierFlags.None,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1581-1583 — <c>isGeneratedIdentifier</c>.</summary>
    public static bool IsGeneratedIdentifier(Node node)
        => NodeTests.IsIdentifier(node) && EmitNodes.GetIdentifierAutoGenerate(node) is not null;

    /// <summary>ported from: src/compiler/utilities.ts:6144-6152 — <c>hasInvalidEscape</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the parameter as <c>TemplateLiteral</c> — a union of <c>TemplateExpression</c> and
    /// <c>NoSubstitutionTemplateLiteral</c> — and dispatches on which it is; the port takes the wider <c>Node</c> and
    /// narrows, because a union has no C# type (§12.5).
    /// </remarks>
    public static bool HasInvalidEscape(Node? template)
    {
        if (template is null) return false;

        if (NodeTests.IsNoSubstitutionTemplateLiteral(template))
        {
            return ContainsInvalidEscapeFlag(template);
        }

        if (template is not TemplateExpression expression) return false;

        return ContainsInvalidEscapeFlag(expression.Head!)
            || Core.Some(expression.TemplateSpans, span => ContainsInvalidEscapeFlag(span.Literal!));
    }

    /// <summary>ported from: src/compiler/utilities.ts:6144 — <c>containsInvalidEscapeFlag</c>.</summary>
    private static bool ContainsInvalidEscapeFlag(Node token)
        => ((((ITemplateLiteralLikeNode)token).TemplateFlags ?? TokenFlags.None) & TokenFlags.ContainsInvalidEscape) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:5311 — <c>isPropertyAccessChain</c>.</summary>
    public static bool IsPropertyAccessChain(Node node)
        => NodeTests.IsPropertyAccessExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:5315 — <c>isElementAccessChain</c>.</summary>
    public static bool IsElementAccessChain(Node node)
        => NodeTests.IsElementAccessExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:5319 — <c>isCallChain</c>.</summary>
    public static bool IsCallChain(Node node)
        => NodeTests.IsCallExpression(node) && (node.Flags & NodeFlags.OptionalChain) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:5343 — <c>isSuperProperty</c>.</summary>
    public static bool IsSuperProperty(Node node)
        => node.Kind is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression
            && ((PropertyAccessExpression)node).Expression?.Kind == SyntaxKind.SuperKeyword;

    /// <summary>ported from: src/compiler/utilities.ts:2274-2287 — <c>nodeIsSynthesized</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks <c>!(pos &gt;= 0)</c>, which also counts a <c>NaN</c> position as synthesized. A C# position is
    /// an <c>int</c> and never is, so the same question is <c>pos &lt; 0</c>.
    /// </remarks>
    public static bool NodeIsSynthesized(ITextRange range) => range.Pos < 0 || range.End < 0;

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

    //
    // Expression classification and precedence (M4d)
    //
    // `parenthesizerRules.ts` and `nodeConverters.ts` ask these questions about an expression before deciding
    // whether to wrap it in parentheses, and the factory's binary/assignment builders ask them about what they were
    // handed. They belong to `utilities.ts`/`utilitiesPublic.ts` (Layer 5) and are ported here for the same reason as
    // the predicates above: the batch that needs them cannot wait for the batch that owns the file.
    //

    /// <summary>ported from: src/compiler/utilities.ts:10699-10708 — <c>setParent</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares three overloads so a caller keeps the static type it passed in. One generic method
    /// that returns <c>T?</c> does the same, and the null cases collapse: TS's second overload only skips the
    /// assignment when the child is null.
    /// </remarks>
    public static T? SetParent<T>(T? child, Node? parent)
        where T : Node
    {
        if (child is not null && parent is not null) child.Parent = parent;
        return child;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2729 — <c>isPrologueDirective</c>.</summary>
    public static bool IsPrologueDirective(Node node)
        => node.Kind == SyntaxKind.ExpressionStatement
            && ((ExpressionStatement)node).Expression!.Kind == SyntaxKind.StringLiteral;

    /// <summary>ported from: src/compiler/utilities.ts:1360 — <c>getEmitFlags</c>.</summary>
    public static EmitFlags GetEmitFlags(Node node) => node.EmitNode?.Flags ?? EmitFlags.None;

    /// <summary>ported from: src/compiler/utilities.ts:2735 — <c>isCustomPrologue</c>.</summary>
    public static bool IsCustomPrologue(Node node) => (GetEmitFlags(node) & EmitFlags.CustomPrologue) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:2740 — <c>isHoistedFunction</c>.</summary>
    public static bool IsHoistedFunction(Node node) => IsCustomPrologue(node) && NodeTests.IsFunctionDeclaration(node);

    /// <summary>ported from: src/compiler/utilities.ts:2745 — <c>isHoistedVariable</c>.</summary>
    private static bool IsHoistedVariable(VariableDeclaration node)
        => node.Name is Identifier && node.Initializer is null;

    /// <summary>ported from: src/compiler/utilities.ts:2751 — <c>isHoistedVariableStatement</c>.</summary>
    public static bool IsHoistedVariableStatement(Node node)
        => IsCustomPrologue(node)
            && NodeTests.IsVariableStatement(node)
            && Core.Every(((VariableStatement)node).DeclarationList!.Declarations, (declaration, _) => IsHoistedVariable(declaration));

    /// <summary>ported from: src/compiler/utilities.ts:7413 — <c>isLogicalOrCoalescingAssignmentOperator</c>.</summary>
    public static bool IsLogicalOrCoalescingAssignmentOperator(SyntaxKind token)
        => token is SyntaxKind.BarBarEqualsToken
            or SyntaxKind.AmpersandAmpersandEqualsToken
            or SyntaxKind.QuestionQuestionEqualsToken;

    /// <summary>ported from: src/compiler/utilities.ts:7435 — <c>isAssignmentOperator</c>.</summary>
    public static bool IsAssignmentOperator(SyntaxKind token)
        => token >= SyntaxKind.FirstAssignment && token <= SyntaxKind.LastAssignment;

    /// <summary>ported from: src/compiler/utilities.ts:7474 — <c>isAssignmentExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads so a literal <c>true</c> for <paramref name="excludeCompoundAssignment"/>
    /// narrows the result to <c>AssignmentExpression&lt;EqualsToken&gt;</c>. One method with a defaulted parameter
    /// answers the same question; callers that need the narrower type cast.
    /// </remarks>
    public static bool IsAssignmentExpression(Node node, bool excludeCompoundAssignment = false)
        => NodeTests.IsBinaryExpression(node)
            && (excludeCompoundAssignment
                ? ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.EqualsToken
                : IsAssignmentOperator(((BinaryExpression)node).OperatorToken!.Kind))
            && IsLeftHandSideExpression(((BinaryExpression)node).Left!);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:762-781 — <c>getOriginalNode</c>.</summary>
    public static Node? GetOriginalNode(Node? node, Func<Node, bool>? nodeTest = null)
    {
        while (node is not null && node.Original is not null)
        {
            node = node.Original;
        }

        if (node is null || nodeTest is null) return node;
        return nodeTest(node) ? node : null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:763 — the generic <c>getOriginalNode</c> overload.</summary>
    public static T? GetOriginalNode<T>(Node? node, Func<Node, bool>? nodeTest = null)
        where T : Node => GetOriginalNode(node, nodeTest) as T;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2442 — <c>isStatementOrBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: This is the same question as `isStatement` minus the parent-pointer walk TS notes in the source.
    /// </remarks>
    public static bool IsStatementOrBlock(Node node)
        => IsStatementKindButNotDeclarationKind(node.Kind)
            || IsDeclarationStatementKind(node.Kind)
            || node.Kind == SyntaxKind.Block;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2360 — <c>isDeclarationStatementKind</c>.</summary>
    private static bool IsDeclarationStatementKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.FunctionDeclaration
            or SyntaxKind.MissingDeclaration
            or SyntaxKind.ClassDeclaration
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.ImportDeclaration
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.ExportDeclaration
            or SyntaxKind.ExportAssignment
            or SyntaxKind.NamespaceExportDeclaration => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2375 — <c>isStatementKindButNotDeclarationKind</c>.</summary>
    private static bool IsStatementKindButNotDeclarationKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.BreakStatement
            or SyntaxKind.ContinueStatement
            or SyntaxKind.DebuggerStatement
            or SyntaxKind.DoStatement
            or SyntaxKind.ExpressionStatement
            or SyntaxKind.EmptyStatement
            or SyntaxKind.ForInStatement
            or SyntaxKind.ForOfStatement
            or SyntaxKind.ForStatement
            or SyntaxKind.IfStatement
            or SyntaxKind.LabeledStatement
            or SyntaxKind.ReturnStatement
            or SyntaxKind.SwitchStatement
            or SyntaxKind.ThrowStatement
            or SyntaxKind.TryStatement
            or SyntaxKind.VariableStatement
            or SyntaxKind.WhileStatement
            or SyntaxKind.WithStatement
            or SyntaxKind.NotEmittedStatement => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1586 — <c>isGeneratedPrivateIdentifier</c>.</summary>
    public static bool IsGeneratedPrivateIdentifier(Node node)
        => NodeTests.IsPrivateIdentifier(node) && EmitNodes.GetIdentifierAutoGenerate(node) is not null;

    /// <summary>ported from: src/compiler/checker.ts:1460 — <c>getNodeId</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this in <c>checker.ts</c> with a module-level counter. The factory needs it earlier (a
    /// generated name for a node that is not a member name is spelled <c>generated@&lt;id&gt;</c>), so it lives here
    /// with the same lazily-assigned id the checker will read back. The port's <see cref="Node.Id"/> is <c>int?</c>,
    /// which is what <c>0</c> means in TS — "not assigned yet".
    /// </remarks>
    public static int GetNodeId(Node node)
    {
        if (node.Id is null)
        {
            node.Id = s_nextNodeId;
            s_nextNodeId++;
        }

        return node.Id.Value;
    }

    private static int s_nextNodeId = 1;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1431 — <c>skipPartiallyEmittedExpressions</c>.</summary>
    /// <remarks>
    /// TS declares two overloads so the caller keeps its static type; the port does the same, because the
    /// parenthesizer hands the result straight back into a position that requires an <c>Expression</c>.
    /// </remarks>
    public static Expression SkipPartiallyEmittedExpressions(Expression node)
        => (Expression)SkipPartiallyEmittedExpressions((Node)node);

    public static Node SkipPartiallyEmittedExpressions(Node node)
        => FactoryUtilities.SkipOuterExpressions(node, OuterExpressionKinds.PartiallyEmittedExpressions);

    /// <summary>ported from: src/compiler/utilities.ts:5045 — <c>skipParentheses</c>.</summary>
    public static Expression SkipParentheses(Expression node, bool excludeJSDocTypeAssertions = false)
        => (Expression)SkipParentheses((Node)node, excludeJSDocTypeAssertions);

    public static Node SkipParentheses(Node node, bool excludeJSDocTypeAssertions = false)
    {
        var kinds = excludeJSDocTypeAssertions
            ? OuterExpressionKinds.Parentheses | OuterExpressionKinds.ExcludeJSDocTypeAssertion
            : OuterExpressionKinds.Parentheses;

        return FactoryUtilities.SkipOuterExpressions(node, kinds);
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1380 — <c>isOptionalChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS names the four kinds through their chain interfaces (<c>PropertyAccessChain</c> and friends);
    /// the port answers with the kind comparison TS ultimately performs, as the kind is what marks the chain.
    /// </remarks>
    public static bool IsOptionalChain(Node node)
        => (node.Flags & NodeFlags.OptionalChain) != 0
            && node.Kind is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression
                or SyntaxKind.CallExpression or SyntaxKind.NonNullExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1487 — <c>isNodeArray</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS duck-types the question (<c>hasProperty(array, "pos") &amp;&amp; hasProperty(array, "end")</c>)
    /// because its arrays are plain objects. In the port a list can only carry a text range by being a
    /// <see cref="NodeArray{T}"/>, so that is the same test.
    /// </remarks>
    public static bool IsNodeArray<T>(IList<T>? array) => array is NodeArray<T>;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1494 — <c>isLiteralKind</c>.</summary>
    public static bool IsLiteralKind(SyntaxKind kind)
        => kind >= SyntaxKind.FirstLiteralToken && kind <= SyntaxKind.LastLiteralToken;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1498 — <c>isLiteralExpression</c>.</summary>
    public static bool IsLiteralExpression(Node node) => IsLiteralKind(node.Kind);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2625 — <c>isStringLiteralLike</c>.</summary>
    public static bool IsStringLiteralLike(Node node)
        => node.Kind is SyntaxKind.StringLiteral or SyntaxKind.NoSubstitutionTemplateLiteral;

    /// <summary>ported from: src/compiler/utilities.ts:5362 — <c>isStringOrNumericLiteralLike</c>.</summary>
    public static bool IsStringOrNumericLiteralLike(Node node)
        => IsStringLiteralLike(node) || NodeTests.IsNumericLiteral(node);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1832 — <c>isBindingPattern</c>.</summary>
    public static bool IsBindingPattern(Node? node)
        => node is not null && node.Kind is SyntaxKind.ArrayBindingPattern or SyntaxKind.ObjectBindingPattern;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1853 — <c>isAssignmentPattern</c>.</summary>
    public static bool IsAssignmentPattern(Node node)
        => node.Kind is SyntaxKind.ArrayLiteralExpression or SyntaxKind.ObjectLiteralExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1860 — <c>isDeclarationBindingElement</c>.</summary>
    public static bool IsDeclarationBindingElement(Node node)
        => node.Kind is SyntaxKind.VariableDeclaration or SyntaxKind.Parameter or SyntaxKind.BindingElement;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1798 — <c>isObjectLiteralElementLike</c>.</summary>
    public static bool IsObjectLiteralElementLike(Node node)
        => node.Kind is SyntaxKind.PropertyAssignment or SyntaxKind.ShorthandPropertyAssignment
            or SyntaxKind.SpreadAssignment or SyntaxKind.MethodDeclaration
            or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1995 — <c>isLeftHandSideExpression</c>.</summary>
    public static bool IsLeftHandSideExpression(Node node)
        => IsLeftHandSideExpressionKind(SkipPartiallyEmittedExpressions(node).Kind);

    private static bool IsLeftHandSideExpressionKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PropertyAccessExpression
            or SyntaxKind.ElementAccessExpression
            or SyntaxKind.NewExpression
            or SyntaxKind.CallExpression
            or SyntaxKind.JsxElement
            or SyntaxKind.JsxSelfClosingElement
            or SyntaxKind.JsxFragment
            or SyntaxKind.TaggedTemplateExpression
            or SyntaxKind.ArrayLiteralExpression
            or SyntaxKind.ParenthesizedExpression
            or SyntaxKind.ObjectLiteralExpression
            or SyntaxKind.ClassExpression
            or SyntaxKind.FunctionExpression
            or SyntaxKind.Identifier
            // technically this is only an Expression if it's in a `#field in expr` BinaryExpression
            or SyntaxKind.PrivateIdentifier
            or SyntaxKind.RegularExpressionLiteral
            or SyntaxKind.NumericLiteral
            or SyntaxKind.BigIntLiteral
            or SyntaxKind.StringLiteral
            or SyntaxKind.NoSubstitutionTemplateLiteral
            or SyntaxKind.TemplateExpression
            or SyntaxKind.FalseKeyword
            or SyntaxKind.NullKeyword
            or SyntaxKind.ThisKeyword
            or SyntaxKind.TrueKeyword
            or SyntaxKind.SuperKeyword
            or SyntaxKind.NonNullExpression
            or SyntaxKind.ExpressionWithTypeArguments
            or SyntaxKind.MetaProperty
            // technically this is only an Expression if it's in a CallExpression
            or SyntaxKind.ImportKeyword
            or SyntaxKind.MissingDeclaration => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2039 — <c>isUnaryExpression</c>.</summary>
    public static bool IsUnaryExpression(Node node)
        => IsUnaryExpressionKind(SkipPartiallyEmittedExpressions(node).Kind);

    private static bool IsUnaryExpressionKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PrefixUnaryExpression
            or SyntaxKind.PostfixUnaryExpression
            or SyntaxKind.DeleteExpression
            or SyntaxKind.TypeOfExpression
            or SyntaxKind.VoidExpression
            or SyntaxKind.AwaitExpression
            or SyntaxKind.TypeAssertionExpression => true,
        _ => IsLeftHandSideExpressionKind(kind),
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2086 — <c>isExpression</c>.</summary>
    public static bool IsExpression(Node node)
        => IsExpressionKind(SkipPartiallyEmittedExpressions(node).Kind);

    private static bool IsExpressionKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.ConditionalExpression
            or SyntaxKind.YieldExpression
            or SyntaxKind.ArrowFunction
            or SyntaxKind.BinaryExpression
            or SyntaxKind.SpreadElement
            or SyntaxKind.AsExpression
            or SyntaxKind.OmittedExpression
            or SyntaxKind.CommaListExpression
            or SyntaxKind.PartiallyEmittedExpression
            or SyntaxKind.SatisfiesExpression => true,
        _ => IsUnaryExpressionKind(kind),
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1819 — <c>isFunctionOrConstructorTypeNode</c>.</summary>
    public static bool IsFunctionOrConstructorTypeNode(Node node)
        => node.Kind is SyntaxKind.FunctionType or SyntaxKind.ConstructorType;

    /// <summary>ported from: src/compiler/utilities.ts:5637 — <c>getExpressionAssociativity</c>.</summary>
    public static Associativity GetExpressionAssociativity(Expression expression)
    {
        var @operator = GetOperator(expression);
        var hasArguments = expression.Kind == SyntaxKind.NewExpression && ((NewExpression)expression).Arguments is not null;
        return GetOperatorAssociativity(expression.Kind, @operator, hasArguments);
    }

    /// <summary>ported from: src/compiler/utilities.ts:5644 — <c>getOperatorAssociativity</c>.</summary>
    public static Associativity GetOperatorAssociativity(SyntaxKind kind, SyntaxKind @operator, bool? hasArguments = null)
        => kind switch
        {
            SyntaxKind.NewExpression => hasArguments == true ? Associativity.Left : Associativity.Right,
            SyntaxKind.PrefixUnaryExpression
                or SyntaxKind.TypeOfExpression
                or SyntaxKind.VoidExpression
                or SyntaxKind.DeleteExpression
                or SyntaxKind.AwaitExpression
                or SyntaxKind.ConditionalExpression
                or SyntaxKind.YieldExpression => Associativity.Right,
            SyntaxKind.BinaryExpression => @operator switch
            {
                SyntaxKind.AsteriskAsteriskToken
                    or SyntaxKind.EqualsToken
                    or SyntaxKind.PlusEqualsToken
                    or SyntaxKind.MinusEqualsToken
                    or SyntaxKind.AsteriskAsteriskEqualsToken
                    or SyntaxKind.AsteriskEqualsToken
                    or SyntaxKind.SlashEqualsToken
                    or SyntaxKind.PercentEqualsToken
                    or SyntaxKind.LessThanLessThanEqualsToken
                    or SyntaxKind.GreaterThanGreaterThanEqualsToken
                    or SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken
                    or SyntaxKind.AmpersandEqualsToken
                    or SyntaxKind.CaretEqualsToken
                    or SyntaxKind.BarEqualsToken
                    or SyntaxKind.BarBarEqualsToken
                    or SyntaxKind.AmpersandAmpersandEqualsToken
                    or SyntaxKind.QuestionQuestionEqualsToken => Associativity.Right,
                _ => Associativity.Left,
            },
            _ => Associativity.Left,
        };

    /// <summary>ported from: src/compiler/utilities.ts:5684 — <c>getExpressionPrecedence</c>.</summary>
    public static OperatorPrecedence GetExpressionPrecedence(Expression expression)
    {
        var @operator = GetOperator(expression);
        var hasArguments = expression.Kind == SyntaxKind.NewExpression && ((NewExpression)expression).Arguments is not null;
        return GetOperatorPrecedence(expression.Kind, @operator, hasArguments);
    }

    /// <summary>ported from: src/compiler/utilities.ts:5690 — <c>getOperator</c>.</summary>
    private static SyntaxKind GetOperator(Expression expression) => expression.Kind switch
    {
        SyntaxKind.BinaryExpression => ((BinaryExpression)expression).OperatorToken!.Kind,
        SyntaxKind.PrefixUnaryExpression => ((PrefixUnaryExpression)expression).Operator,
        SyntaxKind.PostfixUnaryExpression => ((PostfixUnaryExpression)expression).Operator,
        _ => expression.Kind,
    };

    /// <summary>ported from: src/compiler/utilities.ts:5896 — <c>getOperatorPrecedence</c>.</summary>
    public static OperatorPrecedence GetOperatorPrecedence(SyntaxKind nodeKind, SyntaxKind operatorKind, bool? hasArguments = null)
        => nodeKind switch
        {
            SyntaxKind.CommaListExpression => OperatorPrecedence.Comma,
            SyntaxKind.SpreadElement => OperatorPrecedence.Spread,
            SyntaxKind.YieldExpression => OperatorPrecedence.Yield,
            SyntaxKind.ConditionalExpression => OperatorPrecedence.Conditional,
            SyntaxKind.BinaryExpression => operatorKind switch
            {
                SyntaxKind.CommaToken => OperatorPrecedence.Comma,
                SyntaxKind.EqualsToken
                    or SyntaxKind.PlusEqualsToken
                    or SyntaxKind.MinusEqualsToken
                    or SyntaxKind.AsteriskAsteriskEqualsToken
                    or SyntaxKind.AsteriskEqualsToken
                    or SyntaxKind.SlashEqualsToken
                    or SyntaxKind.PercentEqualsToken
                    or SyntaxKind.LessThanLessThanEqualsToken
                    or SyntaxKind.GreaterThanGreaterThanEqualsToken
                    or SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken
                    or SyntaxKind.AmpersandEqualsToken
                    or SyntaxKind.CaretEqualsToken
                    or SyntaxKind.BarEqualsToken
                    or SyntaxKind.BarBarEqualsToken
                    or SyntaxKind.AmpersandAmpersandEqualsToken
                    or SyntaxKind.QuestionQuestionEqualsToken => OperatorPrecedence.Assignment,
                _ => GetBinaryOperatorPrecedence(operatorKind),
            },
            // TODO: Should prefix `++` and `--` be moved to the `Update` precedence?
            SyntaxKind.TypeAssertionExpression
                or SyntaxKind.NonNullExpression
                or SyntaxKind.PrefixUnaryExpression
                or SyntaxKind.TypeOfExpression
                or SyntaxKind.VoidExpression
                or SyntaxKind.DeleteExpression
                or SyntaxKind.AwaitExpression => OperatorPrecedence.Unary,
            SyntaxKind.PostfixUnaryExpression => OperatorPrecedence.Update,
            SyntaxKind.CallExpression => OperatorPrecedence.LeftHandSide,
            SyntaxKind.NewExpression => hasArguments == true ? OperatorPrecedence.Member : OperatorPrecedence.LeftHandSide,
            SyntaxKind.TaggedTemplateExpression
                or SyntaxKind.PropertyAccessExpression
                or SyntaxKind.ElementAccessExpression
                or SyntaxKind.MetaProperty => OperatorPrecedence.Member,
            SyntaxKind.AsExpression
                or SyntaxKind.SatisfiesExpression => OperatorPrecedence.Relational,
            SyntaxKind.ThisKeyword
                or SyntaxKind.SuperKeyword
                or SyntaxKind.Identifier
                or SyntaxKind.PrivateIdentifier
                or SyntaxKind.NullKeyword
                or SyntaxKind.TrueKeyword
                or SyntaxKind.FalseKeyword
                or SyntaxKind.NumericLiteral
                or SyntaxKind.BigIntLiteral
                or SyntaxKind.StringLiteral
                or SyntaxKind.ArrayLiteralExpression
                or SyntaxKind.ObjectLiteralExpression
                or SyntaxKind.FunctionExpression
                or SyntaxKind.ArrowFunction
                or SyntaxKind.ClassExpression
                or SyntaxKind.RegularExpressionLiteral
                or SyntaxKind.NoSubstitutionTemplateLiteral
                or SyntaxKind.TemplateExpression
                or SyntaxKind.ParenthesizedExpression
                or SyntaxKind.OmittedExpression
                or SyntaxKind.JsxElement
                or SyntaxKind.JsxSelfClosingElement
                or SyntaxKind.JsxFragment => OperatorPrecedence.Primary,
            _ => OperatorPrecedence.Invalid,
        };

    /// <summary>ported from: src/compiler/utilities.ts:5997 — <c>getBinaryOperatorPrecedence</c>.</summary>
    public static OperatorPrecedence GetBinaryOperatorPrecedence(SyntaxKind kind) => kind switch
    {
        SyntaxKind.QuestionQuestionToken => OperatorPrecedence.Coalesce,
        SyntaxKind.BarBarToken => OperatorPrecedence.LogicalOR,
        SyntaxKind.AmpersandAmpersandToken => OperatorPrecedence.LogicalAND,
        SyntaxKind.BarToken => OperatorPrecedence.BitwiseOR,
        SyntaxKind.CaretToken => OperatorPrecedence.BitwiseXOR,
        SyntaxKind.AmpersandToken => OperatorPrecedence.BitwiseAND,
        SyntaxKind.EqualsEqualsToken
            or SyntaxKind.ExclamationEqualsToken
            or SyntaxKind.EqualsEqualsEqualsToken
            or SyntaxKind.ExclamationEqualsEqualsToken => OperatorPrecedence.Equality,
        SyntaxKind.LessThanToken
            or SyntaxKind.GreaterThanToken
            or SyntaxKind.LessThanEqualsToken
            or SyntaxKind.GreaterThanEqualsToken
            or SyntaxKind.InstanceOfKeyword
            or SyntaxKind.InKeyword
            or SyntaxKind.AsKeyword
            or SyntaxKind.SatisfiesKeyword => OperatorPrecedence.Relational,
        SyntaxKind.LessThanLessThanToken
            or SyntaxKind.GreaterThanGreaterThanToken
            or SyntaxKind.GreaterThanGreaterThanGreaterThanToken => OperatorPrecedence.Shift,
        SyntaxKind.PlusToken
            or SyntaxKind.MinusToken => OperatorPrecedence.Additive,
        SyntaxKind.AsteriskToken
            or SyntaxKind.SlashToken
            or SyntaxKind.PercentToken => OperatorPrecedence.Multiplicative,
        SyntaxKind.AsteriskAsteriskToken => OperatorPrecedence.Exponentiation,
        // -1 is lower than all other precedences. Returning it will cause binary expression parsing to stop.
        _ => OperatorPrecedence.Invalid,
    };

    /// <summary>ported from: src/compiler/utilities.ts:8420 — <c>getLeftmostExpression</c>.</summary>
    /// <remarks>
    /// TS falls through from `CallExpression` into the plain-`expression` group (and relies on `<c>AsExpression</c>`s
    /// and the like having an <c>expression</c> member); the port keeps the two groups separate so the member it
    /// reads is the one the type has.
    /// </remarks>
    public static Expression GetLeftmostExpression(Expression node, bool stopAtCallExpressions)
    {
        while (true)
        {
            switch (node.Kind)
            {
                case SyntaxKind.PostfixUnaryExpression:
                    node = ((PostfixUnaryExpression)node).Operand!;
                    continue;

                case SyntaxKind.BinaryExpression:
                    node = ((BinaryExpression)node).Left!;
                    continue;

                case SyntaxKind.ConditionalExpression:
                    node = ((ConditionalExpression)node).Condition!;
                    continue;

                case SyntaxKind.TaggedTemplateExpression:
                    node = ((TaggedTemplateExpression)node).Tag!;
                    continue;

                case SyntaxKind.CallExpression:
                    if (stopAtCallExpressions) return node;
                    node = ((CallExpression)node).Expression!;
                    continue;

                case SyntaxKind.AsExpression:
                case SyntaxKind.SatisfiesExpression:
                    node = ((AsExpression)node).Expression!;
                    continue;

                case SyntaxKind.ElementAccessExpression:
                    node = ((ElementAccessExpression)node).Expression!;
                    continue;

                case SyntaxKind.PropertyAccessExpression:
                    node = ((PropertyAccessExpression)node).Expression!;
                    continue;

                case SyntaxKind.NonNullExpression:
                    node = ((NonNullExpression)node).Expression!;
                    continue;

                case SyntaxKind.PartiallyEmittedExpression:
                    node = ((PartiallyEmittedExpression)node).Expression!;
                    continue;
            }

            return node;
        }
    }
}
