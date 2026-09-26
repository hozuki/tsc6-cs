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
/// <para>
/// M4f's two files (<c>factory/emitHelpers.ts</c> and <c>expressionToTypeNode.ts</c>) added the last group: the
/// function-like predicates they narrow with, <c>findAncestor</c>, <c>forEachReturnStatement</c>,
/// <c>getFunctionFlags</c> and the <c>getEffective*tNode</c> family. Two of their bodies stop short of the JS-file
/// JSDoc fallback, because <c>getJSDocType</c> / <c>getJSDocReturnType</c> belong to the JSDoc-attribution family of M5:
/// each says so in a PORT-NOTE and throws rather than answering a type-less <c>null</c>. <c>hasDynamicName</c> is
/// <em>not</em> here for the same milestone reason — it is defined as <c>getNonAssignedNameOfDeclaration(declaration)</c>
/// followed by <c>isDynamicName</c>, and that name lookup is exactly the M5 family §15.9 records as residual.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilitiesPublic.ts:946-948 — the duck-typed <c>name</c> read
    /// <c>isNamedDeclaration</c> is defined by.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks <c>!!(node as NamedDeclaration).name</c> — a slot read, not a type test — so it answers for
    /// every declaration that carries a <c>name</c>, including the two the model cannot mark
    /// <see cref="INamedDeclaration"/>: types.ts declares their <c>name</c> without <c>extends NamedDeclaration</c>
    /// (<c>JsxAttribute</c> at types.ts:3274-3280, <c>NamedTupleMember</c>), so a plain
    /// <c>is INamedDeclaration</c> test would answer false where TS answers true. The port states the same question as
    /// this switch, which the members that need the read share — <see cref="IsNamedDeclaration"/>,
    /// <c>getNonAssignedNameOfDeclaration</c> and the binder's <c>getDisplayName</c>/<c>declareSymbol</c>.
    /// </remarks>
    public static Node? GetNamedDeclarationName(Node node) => node switch
    {
        INamedDeclaration named => named.Name,
        JsxAttribute jsxAttribute => jsxAttribute.Name,
        NamedTupleMember namedTupleMember => namedTupleMember.Name,
        _ => null,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:946 — <c>isNamedDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows to <c>NamedDeclaration &amp; { name: DeclarationName }</c>, which is what lets a caller
    /// read <c>name</c> without another check. The port answers <c>bool</c> and the caller reads the slot through
    /// <see cref="GetNamedDeclarationName"/> — the honest C# spelling of TS's duck-typed read — because a C# type test
    /// cannot ask "does this node have a name slot".
    /// </remarks>
    public static bool IsNamedDeclaration(Node node) => GetNamedDeclarationName(node) is not null;

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

    /// <summary>ported from: src/compiler/factory/utilitiesPublic.ts:14 — <c>canHaveModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows the node to <c>HasModifiers</c> so a caller can read <c>node.modifiers</c>; the port
    /// answers <c>bool</c>, because C# cannot narrow through a predicate. <c>HasModifiers</c> is an intersection alias
    /// and has no generated interface, so a caller that needs the list reads <c>Modifiers</c> off the node class it
    /// narrowed to (the same treatment as <see cref="IsNamedDeclaration"/>).
    /// </remarks>
    public static bool CanHaveModifiers(Node node) => node.Kind switch
    {
        SyntaxKind.TypeParameter
            or SyntaxKind.Parameter
            or SyntaxKind.PropertySignature
            or SyntaxKind.PropertyDeclaration
            or SyntaxKind.MethodSignature
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.Constructor
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor
            or SyntaxKind.IndexSignature
            or SyntaxKind.ConstructorType
            or SyntaxKind.FunctionExpression
            or SyntaxKind.ArrowFunction
            or SyntaxKind.ClassExpression
            or SyntaxKind.VariableStatement
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.ClassDeclaration
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.ImportDeclaration
            or SyntaxKind.ExportAssignment
            or SyntaxKind.ExportDeclaration => true,
        _ => false,
    };

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
    /// <remarks>
    /// PORT-NOTE: TS casts to the *union* <c>PropertyAccessExpression | ElementAccessExpression</c> and reads
    /// <c>.expression</c> from both arms — legal there because both declare the member, so the cast never fails. C# has
    /// no union cast: the earlier port spelled it as one cast to <c>PropertyAccessExpression</c>, which threw
    /// <c>InvalidCastException</c> the first time a real file called it with <c>super[x]</c>. The kind switch keeps TS's
    /// order (kind first, then the member).
    /// </remarks>
    public static bool IsSuperProperty(Node node) => node.Kind switch
    {
        SyntaxKind.PropertyAccessExpression => ((PropertyAccessExpression)node).Expression is { Kind: SyntaxKind.SuperKeyword },
        SyntaxKind.ElementAccessExpression => ((ElementAccessExpression)node).Expression is { Kind: SyntaxKind.SuperKeyword },
        _ => false,
    };

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

    // ---------------------------------------------------------------------------------------------
    // M4f prerequisites: `factory/emitHelpers.ts` and `expressionToTypeNode.ts` need these ahead of M5
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3804-3806 — <c>isInJSFile</c>.</summary>
    public static bool IsInJSFile(Node? node) => node is not null && (node.Flags & NodeFlags.JavaScriptFile) != 0;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1692-1704 — <c>isFunctionLikeDeclarationKind</c>.</summary>
    private static bool IsFunctionLikeDeclarationKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.FunctionDeclaration
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.Constructor
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor
            or SyntaxKind.FunctionExpression
            or SyntaxKind.ArrowFunction => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1710-1724 — <c>isFunctionLikeKind</c>.</summary>
    public static bool IsFunctionLikeKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.MethodSignature
            or SyntaxKind.CallSignature
            or SyntaxKind.JSDocSignature
            or SyntaxKind.ConstructSignature
            or SyntaxKind.IndexSignature
            or SyntaxKind.FunctionType
            or SyntaxKind.JSDocFunctionType
            or SyntaxKind.ConstructorType => true,
        _ => IsFunctionLikeDeclarationKind(kind),
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1675-1677 — <c>isFunctionLike</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows the node to <c>SignatureDeclaration</c> — a union of sixteen node types that has no
    /// C# base interface of its own (<c>JSDocSignature</c> does not even implement <c>ISignatureDeclarationBase</c>) — so
    /// the port answers <c>bool</c> and the caller narrows as it needs, the same treatment
    /// <see cref="CanHaveModifiers"/> gets.
    /// </remarks>
    public static bool IsFunctionLike(Node? node) => node is not null && IsFunctionLikeKind(node.Kind);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1685-1687 — <c>isFunctionLikeDeclaration</c>.</summary>
    public static bool IsFunctionLikeDeclaration(Node node) => IsFunctionLikeDeclarationKind(node.Kind);

    /// <summary>ported from: src/compiler/utilities.ts:2893-2918 — <c>forEachReturnStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>body: Block | Statement</c> is <see cref="IStatement"/> here — the anchor §12.9 gives that
    /// categorisation chain — and <c>Block</c> implements it, so both members of the union are accepted. The walk itself
    /// needs the <c>Node</c> half (the port's <see cref="INode"/> carries no child slots), hence the cast. <c>T</c> stays
    /// unconstrained: the walk stops at the first result the <c>forEachChild</c> family considers truthy, which the port's
    /// <see cref="ForEachChildTable.ForEachChild{T}"/> expresses the same way.
    /// </remarks>
    public static T? ForEachReturnStatement<T>(IStatement body, Func<ReturnStatement, T?> visitor)
    {
        return Traverse((Node)body);

        T? Traverse(Node node) => node.Kind switch
        {
            SyntaxKind.ReturnStatement => visitor((ReturnStatement)node),
            SyntaxKind.CaseBlock
                or SyntaxKind.Block
                or SyntaxKind.IfStatement
                or SyntaxKind.DoStatement
                or SyntaxKind.WhileStatement
                or SyntaxKind.ForStatement
                or SyntaxKind.ForInStatement
                or SyntaxKind.ForOfStatement
                or SyntaxKind.WithStatement
                or SyntaxKind.SwitchStatement
                or SyntaxKind.CaseClause
                or SyntaxKind.DefaultClause
                or SyntaxKind.LabeledStatement
                or SyntaxKind.TryStatement
                or SyntaxKind.CatchClause => ForEachChildTable.ForEachChild(node, Traverse),
            _ => default,
        };
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:786-799 — <c>findAncestor</c> (both overloads).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's second overload answers <c>boolean | "quit"</c> — a truthy value stops the walk and answers that
    /// node, <c>"quit"</c> stops it and answers <c>undefined</c>. A C# <c>bool</c>-returning delegate cannot carry a third
    /// answer, so TS's two overloads become two here: a plain predicate, which a method group such as
    /// <c>Utilities.IsFunctionLike</c> binds to directly, and the quit-capable one whose <c>null</c> is TS's <c>"quit"</c>
    /// (<c>false</c> keeps walking, <c>true</c> returns the node, <c>null</c> returns nothing). A lambda that answers
    /// <c>bool</c> binds to the first — that is the better conversion — so only a caller that wants to quit has to spell
    /// the nullable form.
    /// <para>
    /// PORT-NOTE: TS's first overload is a type guard (<c>T extends Node</c>, answering <c>T | undefined</c>). C# cannot
    /// narrow through a predicate, so the port keeps <c>Node?</c> and the caller casts — the same treatment
    /// <see cref="IsNamedDeclaration"/> gets.
    /// </para>
    /// </remarks>
    public static Node? FindAncestor(Node? node, Func<Node, bool> callback)
        => FindAncestorCore(node, n => callback(n));

    /// <summary>The quit-capable overload: an answer of <c>null</c> stops the walk without a result (TS's
    /// <c>"quit"</c>).</summary>
    public static Node? FindAncestor(Node? node, Func<Node, bool?> callback) => FindAncestorCore(node, callback);

    private static Node? FindAncestorCore(Node? node, Func<Node, bool?> callback)
    {
        while (node is not null)
        {
            var result = callback(node);
            if (result is null) return null;
            if (result.Value) return node;
            node = node.Parent;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5318-5345 — <c>getFunctionFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>SignatureDeclaration | undefined</c> union, which also admits the signature
    /// kinds without a body (call, construct, index). The port takes <c>Node</c> and narrows with
    /// <c>is IFunctionLikeDeclarationBase</c> where TS writes <c>as FunctionLikeDeclaration</c>: both answer
    /// <c>Invalid</c> for a signature that has no body, because the missing member reads <c>undefined</c> on that side.
    /// <para>
    /// PORT-NOTE: TS asks <c>hasSyntacticModifier(node, ModifierFlags.Async)</c>, which is the ported
    /// <see cref="HasSyntacticModifier"/>; the four kinds below are the ones <c>canHaveModifiers</c> admits.
    /// </para>
    /// </remarks>
    public static FunctionFlags GetFunctionFlags(Node? node)
    {
        if (node is null) return FunctionFlags.Invalid;

        var flags = FunctionFlags.Normal;
        switch (node.Kind)
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.MethodDeclaration:
                if (((IFunctionLikeDeclarationBase)node).AsteriskToken is not null)
                {
                    flags |= FunctionFlags.Generator;
                }

                goto case SyntaxKind.ArrowFunction;

            case SyntaxKind.ArrowFunction:
                if (HasSyntacticModifier(node, ModifierFlags.Async))
                {
                    flags |= FunctionFlags.Async;
                }

                break;
        }

        if (node is not IFunctionLikeDeclarationBase { Body: not null })
        {
            flags |= FunctionFlags.Invalid;
        }

        return flags;
    }

    /// <summary>ported from: src/compiler/utilities.ts:12218-12222 — <c>isNewScopeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>IntroducesNewScopeNode</c> (<c>SignatureDeclaration | JSDocSignature |
    /// MappedTypeNode</c>); C# cannot narrow through a predicate, so the port answers <c>bool</c>. The alias itself has no
    /// port counterpart — nothing names it except parameter slots of
    /// <c>SyntacticTypeNodeBuilderResolver.enterNewScope</c>, which the generated interface already spells <c>Node?</c>.
    /// </remarks>
    public static bool IsNewScopeNode(Node node)
        => IsFunctionLike(node) || NodeTests.IsJSDocSignature(node) || NodeTests.IsMappedTypeNode(node);

    /// <summary>ported from: src/compiler/utilities.ts:6793-6795 — <c>parameterIsThisKeyword</c>.</summary>
    public static bool ParameterIsThisKeyword(ParameterDeclaration parameter) => IsThisIdentifier(parameter.Name);

    /// <summary>ported from: src/compiler/utilities.ts:6764-6769 — <c>getSetAccessorValueParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS guards with <c>accessor &amp;&amp;</c> a parameter it also declares non-optional; the port's parameter
    /// is non-nullable, so only the length test remains.
    /// </remarks>
    public static ParameterDeclaration? GetSetAccessorValueParameter(SetAccessorDeclaration accessor)
    {
        if (accessor.Parameters is { Count: > 0 } parameters)
        {
            var hasThis = parameters.Count == 2 && ParameterIsThisKeyword(parameters[0]);
            return parameters[hasThis ? 1 : 0];
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6892-6898 — <c>getEffectiveTypeAnnotationNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the JSDoc fallback that TS runs for a JavaScript file is only half ported. The JSDoc-property case is
    /// here (<c>node.typeExpression.type</c> is the annotation, and both kinds are in the model), but the rest needs
    /// <c>getJSDocType</c>, which belongs to the JSDoc-attribution family that arrives with M5. Answering <c>null</c>
    /// there would silently drop every type a JS file declares through <c>@type</c> / <c>@param</c>, so the branch is a
    /// loud blocker instead — the same choice <see cref="FactoryUtilities.IsJSDocTypeAssertion"/> makes.
    /// </remarks>
    public static ITypeNode? GetEffectiveTypeAnnotationNode(Node node)
    {
        if (!IsInJSFile(node) && NodeTests.IsFunctionDeclaration(node)) return null;
        if (NodeTests.IsTypeAliasDeclaration(node)) return null; // has a .type, is not a type annotation
        var type = GetTypeAnnotationNode(node);
        if (type is not null || !IsInJSFile(node)) return type;
        if (node is JSDocPropertyLikeTag propertyLike) return propertyLike.TypeExpression?.Type;
        throw new NotImplementedException(
            "getEffectiveTypeAnnotationNode's JSDoc fallback needs getJSDocType, which arrives with the JSDoc helpers in M5");
    }

    /// <summary>ported from: src/compiler/utilities.ts:6901-6903 — <c>getTypeAnnotationNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's whole body is <c>(node as HasType).type</c>, and <c>HasType</c> is only the static spelling of
    /// that read — at runtime it is a plain property read on whatever node it is handed. The port writes the read out as
    /// a switch over every node type whose model declares a <c>type</c> slot: <c>ISignatureDeclarationBase</c> covers the
    /// whole <c>SignatureDeclaration</c> family (as <c>HasType</c>'s first member names it), and the remaining arms are
    /// <c>HasType</c>'s other members plus the node types TS's union omits but the read still reaches
    /// (<c>SatisfiesExpression</c>, <c>NamedTupleMember</c>, <c>OptionalTypeNode</c>, <c>RestTypeNode</c>,
    /// <c>TemplateLiteralTypeSpan</c>, <c>JSDocOptionalType</c>, <c>JSDocVariadicType</c>, <c>JSDocNamepathType</c>).
    /// <c>JSDocSignature</c> is deliberately absent: TS's union lists it, but its <c>type</c> is a
    /// <c>JSDocReturnTag</c> rather than a <c>TypeNode</c>, which is why <c>getEffectiveReturnTypeNode</c> handles that
    /// shape itself.
    /// </remarks>
    private static ITypeNode? GetTypeAnnotationNode(Node node) => node switch
    {
        ISignatureDeclarationBase signature => signature.Type,
        VariableDeclaration declaration => declaration.Type,
        ParameterDeclaration declaration => declaration.Type,
        PropertySignature declaration => declaration.Type,
        PropertyDeclaration declaration => declaration.Type,
        TypePredicateNode declaration => declaration.Type,
        ParenthesizedTypeNode declaration => declaration.Type,
        TypeOperatorNode declaration => declaration.Type,
        MappedTypeNode declaration => declaration.Type,
        TypeAssertion declaration => declaration.Type,
        AsExpression declaration => declaration.Type,
        SatisfiesExpression declaration => declaration.Type,
        TypeAliasDeclaration declaration => declaration.Type,
        NamedTupleMember declaration => declaration.Type,
        OptionalTypeNode declaration => declaration.Type,
        RestTypeNode declaration => declaration.Type,
        TemplateLiteralTypeSpan declaration => declaration.Type,
        JSDocTypeExpression declaration => declaration.Type,
        JSDocNonNullableType declaration => declaration.Type,
        JSDocNullableType declaration => declaration.Type,
        JSDocOptionalType declaration => declaration.Type,
        JSDocVariadicType declaration => declaration.Type,
        JSDocNamepathType declaration => declaration.Type,
        _ => null,
    };

    /// <summary>ported from: src/compiler/utilities.ts:6911-6915 — <c>getEffectiveReturnTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>SignatureDeclaration | JSDocSignature</c>, which has no C# counterpart (see
    /// <see cref="IsFunctionLike"/>), so the port takes <c>Node</c> and narrows with <c>is JSDocSignature</c> /
    /// <c>is ISignatureDeclarationBase</c> exactly where TS's union members apply.
    /// <para>
    /// PORT-NOTE: the JavaScript-file fallback (<c>getJSDocReturnType</c>) is not ported — it belongs to the
    /// JSDoc-attribution family that arrives with M5. A JS function with no return annotation is the common case there,
    /// so answering <c>null</c> would silently drop the declared return type; the branch is a loud blocker instead, the
    /// same choice <see cref="GetEffectiveTypeAnnotationNode"/> makes.
    /// </para>
    /// </remarks>
    public static ITypeNode? GetEffectiveReturnTypeNode(Node node)
    {
        if (NodeTests.IsJSDocSignature(node))
        {
            return ((JSDocSignature)node).Type?.TypeExpression?.Type;
        }

        var type = node is ISignatureDeclarationBase signature ? signature.Type : null;
        if (type is not null) return type;
        if (!IsInJSFile(node)) return null;
        throw new NotImplementedException(
            "getEffectiveReturnTypeNode's JSDoc fallback needs getJSDocReturnType, which arrives with the JSDoc helpers in M5");
    }

    /// <summary>ported from: src/compiler/utilities.ts:6933-6936 — <c>getEffectiveSetAccessorTypeAnnotationNode</c>.</summary>
    public static ITypeNode? GetEffectiveSetAccessorTypeAnnotationNode(SetAccessorDeclaration node)
    {
        var parameter = GetSetAccessorValueParameter(node);
        return parameter is not null ? GetEffectiveTypeAnnotationNode(parameter) : null;
    }
}
