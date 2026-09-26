using System.Globalization;

// `Flags` is the instance property below (TS's `factory.flags`), and it shadows the `Core/Flags.cs` helper class
// inside this type — the same collision the port accepted for the property name.
using CoreFlags = TypeScript.Compiler.Flags;

namespace TypeScript.Compiler;

// ported from: src/compiler/factory/nodeFactory.ts (createNodeFactory)
/// <summary>
/// The node factory: the single entry point for creating and updating nodes.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS builds its factory as an object literal returned from <c>createNodeFactory(flags, baseFactory)</c>
/// and takes the members from that closure, so a call site destructures them (<c>const { createIdentifier } =
/// factory</c>). C# has no free-standing functions over shared state, so the same surface is an instance class and
/// call sites reach it through the instance.
/// <para>
/// PORT-NOTE: <c>baseNodeFactory.ts</c> is not ported. It existed so every compiler instance could get its own set
/// of <c>Node</c> constructors — which is what makes <c>instanceof</c> correct across realms in TS. C# has one
/// <c>Node</c> type per assembly, so <c>new Identifier()</c> is already the right thing (see §15.2).
/// </para>
/// <para>
/// This file grows in batches: the infrastructure first, then the <c>create*</c>/<c>update*</c> families. It picks up
/// <c>INodeFactory</c> only once every member the generated interface declares is present — until then the compiler
/// cannot tell a missing method from an unimplemented one, so the batches are checked by their own tests.
/// </para>
/// </remarks>
internal partial class NodeFactory
{
    private readonly NodeFactoryFlags _flags;

    public NodeFactory(NodeFactoryFlags flags = NodeFactoryFlags.None) => _flags = flags;

    /// <summary>How this factory was configured (see <see cref="NodeFactoryFlags"/>).</summary>
    public NodeFactoryFlags Flags => _flags;

    //
    // Node creation
    //

    /// <summary>ported from: factory/nodeFactory.ts:1209 — <c>createBaseNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this is where the port diverges most sharply from TS, and the divergence is what makes D1 work.
    /// TS's <c>baseFactory.createBaseNode(kind)</c> instantiates the <em>base</em> <c>Node</c> class with the given
    /// kind and lets the caller hang fields on it — the concrete type exists only in the type system. The port has one
    /// C# class per kind, and the kind is pinned by the class itself (<c>public override SyntaxKind Kind =&gt;
    /// …</c>), so the node is simply constructed and the parameter is only checked.
    /// <para>
    /// The token family is the exception and is why <c>createToken</c> stays hand-written: <c>Token</c> carries its
    /// kind as <em>state</em> (there is one class for every punctuation token), so it cannot be built from a type
    /// parameter.
    /// </para>
    /// </remarks>
    private static T CreateBaseNode<T>(SyntaxKind kind)
        where T : Node, new()
    {
        var node = new T();
        Debug.Assert(node.Kind == kind, $"factory kind mismatch for {typeof(T).Name}: {node.Kind} instead of {kind}");
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1213 — <c>createBaseDeclaration</c>.</summary>
    /// <remarks>
    /// The two fields are cleared because the binder fills them in later; TS spells the same thing with
    /// <c>undefined</c>.
    /// </remarks>
    private static T CreateBaseDeclaration<T>(SyntaxKind kind)
        where T : Node, IDeclaration, new()
    {
        var node = CreateBaseNode<T>(kind);
        node.Symbol = null;
        node.LocalSymbol = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1436 — <c>createBaseToken</c>.</summary>
    private static T CreateBaseToken<T>(SyntaxKind kind)
        where T : Node, new() => CreateBaseNode<T>(kind);

    //
    // Literals
    //

    /// <summary>ported from: factory/nodeFactory.ts:1233 — <c>createNumericLiteral</c>.</summary>
    /// <remarks>
    /// The parameter is <c>object?</c> because TS takes <c>string | number</c>; the generated interface carries the
    /// same shape, with `int` and `string` overloads next to it (§12.5).
    /// </remarks>
    public NumericLiteral CreateNumericLiteral(object? value, TokenFlags? numericLiteralFlags = null)
    {
        var text = value is int number ? number.ToString(CultureInfo.InvariantCulture) : value as string;
        Debug.Assert(
            text is null || text.Length == 0 || text[0] != CharacterCodes.Minus,
            "Negative numbers should be created in combination with createPrefixUnaryExpression");

        var node = CreateBaseDeclaration<NumericLiteral>(SyntaxKind.NumericLiteral);
        node.Text = text;
        node.NumericLiteralFlags = numericLiteralFlags ?? TokenFlags.None;
        if ((node.NumericLiteralFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015;
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1244 — <c>createBigIntLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS takes <c>string | PseudoBigInt</c>. The port's <c>PseudoBigInt</c> is not a value type — only the
    /// digit-parsing half is ported (<c>Core/PseudoBigInt.cs</c>) — so this takes the literal text, and a caller
    /// holding a pseudo-BigInt spells it <c>PseudoBigInt.Parse(value) + "n"</c>.
    /// </remarks>
    public BigIntLiteral CreateBigIntLiteral(string? value)
    {
        var node = CreateBaseToken<BigIntLiteral>(SyntaxKind.BigIntLiteral);
        node.Text = value;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    private StringLiteral CreateBaseStringLiteral(string? text, bool? isSingleQuote)
    {
        var node = CreateBaseDeclaration<StringLiteral>(SyntaxKind.StringLiteral);
        node.Text = text;
        node.SingleQuote = isSingleQuote;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1259 — <c>createStringLiteral</c>.</summary>
    public StringLiteral CreateStringLiteral(string? text, bool? isSingleQuote = null, bool? hasExtendedUnicodeEscape = null)
    {
        var node = CreateBaseStringLiteral(text, isSingleQuote);
        node.HasExtendedUnicodeEscape = hasExtendedUnicodeEscape;
        if (hasExtendedUnicodeEscape == true) node.TransformFlags |= TransformFlags.ContainsES2015;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1267 — <c>createStringLiteralFromNode</c>.</summary>
    public StringLiteral CreateStringLiteralFromNode(Node? sourceNode, bool? isSingleQuote = null)
    {
        var node = CreateBaseStringLiteral(Utilities.GetTextOfIdentifierOrLiteral(sourceNode!), isSingleQuote);
        node.TextSourceNode = sourceNode;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1274 — <c>createRegularExpressionLiteral</c>.</summary>
    public RegularExpressionLiteral CreateRegularExpressionLiteral(string? text)
    {
        var node = CreateBaseToken<RegularExpressionLiteral>(SyntaxKind.RegularExpressionLiteral);
        node.Text = text;
        return node;
    }

    //
    // Identifiers
    //

    private static Identifier CreateBaseIdentifier(string escapedText)
    {
        var node = CreateBaseNode<Identifier>(SyntaxKind.Identifier);
        node.EscapedText = escapedText;
        node.JsDoc = null;    // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        node.Symbol = null;   // initialized by checker
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1326 — <c>createIdentifier</c>.</summary>
    public Identifier CreateIdentifier(string? text, SyntaxKind? originalKeywordKind = null, bool? hasExtendedUnicodeEscape = null)
    {
        text ??= "";

        if (originalKeywordKind is null && text.Length != 0) originalKeywordKind = ScannerUtilities.StringToToken(text);
        if (originalKeywordKind == SyntaxKind.Identifier) originalKeywordKind = null;

        var node = CreateBaseIdentifier(Utilities.EscapeLeadingUnderscores(text));
        if (hasExtendedUnicodeEscape == true) node.Flags |= NodeFlags.IdentifierHasExtendedUnicodeEscape;

        // NOTE: the transform flags of type arguments are not included: an identifier's do not contribute to
        // transformations.
        if (node.EscapedText == "await") node.TransformFlags |= TransformFlags.ContainsPossibleTopLevelAwait;
        if ((node.Flags & NodeFlags.IdentifierHasExtendedUnicodeEscape) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015;
        }

        return node;
    }

    //
    // Punctuation and keywords
    //

    /// <summary>ported from: factory/nodeFactory.ts:1453 — <c>createToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: this is the other place, after <c>createBaseNode</c>, where having one class per kind shows (§16).
    /// TS builds a single <c>Token</c> instance for every kind and lets the static type say what it is, so
    /// <c>createToken(token: SyntaxKind.SuperKeyword)</c> is <em>typed</em> <c>SuperExpression</c> while the object
    /// stays a plain token. C# cannot pretend, so the five kinds whose TS type is a node of their own get that node;
    /// every other kind is a <c>Token</c> carrying the runtime kind.
    /// </remarks>
    public Node CreateToken(SyntaxKind token)
    {
        Debug.Assert(token >= SyntaxKind.FirstToken && token <= SyntaxKind.LastToken, "Invalid token");
        Debug.Assert(
            token <= SyntaxKind.FirstTemplateToken || token >= SyntaxKind.LastTemplateToken,
            "Invalid token. Use 'createTemplateLiteralLikeNode' to create template literals.");
        Debug.Assert(
            token <= SyntaxKind.FirstLiteralToken || token >= SyntaxKind.LastLiteralToken,
            "Invalid token. Use 'createLiteralLikeNode' to create literals.");
        Debug.Assert(token != SyntaxKind.Identifier, "Invalid token. Use 'createIdentifier' to create identifiers");

        var node = CreateTokenNode(token);

        // `super` and `this` can be reached through the flow graph, so their flow node starts out cleared here; for
        // everything else the binder is the one that initializes it.
        switch (node)
        {
            case SuperExpression super: super.FlowNode = null; break;
            case ThisExpression thisExpression: thisExpression.FlowNode = null; break;
        }

        var transformFlags = TokenTransformFlagsOf(token);
        if (transformFlags != TransformFlags.None) node.TransformFlags |= transformFlags;
        return node;
    }

    private static Node CreateTokenNode(SyntaxKind token) => token switch
    {
        SyntaxKind.SuperKeyword => CreateBaseNode<SuperExpression>(token),
        SyntaxKind.ThisKeyword => CreateBaseNode<ThisExpression>(token),
        SyntaxKind.NullKeyword => CreateBaseNode<NullLiteral>(token),
        SyntaxKind.TrueKeyword => CreateBaseNode<TrueLiteral>(token),
        SyntaxKind.FalseKeyword => CreateBaseNode<FalseLiteral>(token),
        _ => new Token(token),
    };

    /// <summary>ported from: factory/nodeFactory.ts:1459 — the <c>switch (token)</c> that sets transform flags.</summary>
    private static TransformFlags TokenTransformFlagsOf(SyntaxKind token) => token switch
    {
        // 'async' modifier is ES2017 (async functions) or ES2018 (async generators)
        SyntaxKind.AsyncKeyword => TransformFlags.ContainsES2017 | TransformFlags.ContainsES2018,
        SyntaxKind.UsingKeyword => TransformFlags.ContainsESNext,
        SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword
            or SyntaxKind.ReadonlyKeyword or SyntaxKind.AbstractKeyword or SyntaxKind.DeclareKeyword
            or SyntaxKind.ConstKeyword or SyntaxKind.AnyKeyword or SyntaxKind.NumberKeyword
            or SyntaxKind.BigIntKeyword or SyntaxKind.NeverKeyword or SyntaxKind.ObjectKeyword
            or SyntaxKind.InKeyword or SyntaxKind.OutKeyword or SyntaxKind.OverrideKeyword
            or SyntaxKind.StringKeyword or SyntaxKind.BooleanKeyword or SyntaxKind.SymbolKeyword
            or SyntaxKind.VoidKeyword or SyntaxKind.UnknownKeyword
            // `undefined` is an Identifier in the expression case.
            or SyntaxKind.UndefinedKeyword
            => TransformFlags.ContainsTypeScript,
        SyntaxKind.SuperKeyword => TransformFlags.ContainsES2015 | TransformFlags.ContainsLexicalSuper,
        SyntaxKind.StaticKeyword => TransformFlags.ContainsES2015,
        SyntaxKind.AccessorKeyword => TransformFlags.ContainsClassFields,
        SyntaxKind.ThisKeyword => TransformFlags.ContainsLexicalThis,
        _ => TransformFlags.None,
    };

    /// <summary>ported from: factory/nodeFactory.ts:1481 — <c>createSuper</c>.</summary>
    /// <remarks>
    /// The cast is safe by construction: <see cref="CreateToken"/> builds a <c>SuperExpression</c> for this kind (see
    /// its remarks).
    /// </remarks>
    public SuperExpression CreateSuper() => (SuperExpression)CreateToken(SyntaxKind.SuperKeyword);

    /// <summary>ported from: factory/nodeFactory.ts:1486 — <c>createThis</c>.</summary>
    public ThisExpression CreateThis() => (ThisExpression)CreateToken(SyntaxKind.ThisKeyword);

    /// <summary>ported from: factory/nodeFactory.ts:1491 — <c>createNull</c>.</summary>
    public NullLiteral CreateNull() => (NullLiteral)CreateToken(SyntaxKind.NullKeyword);

    /// <summary>ported from: factory/nodeFactory.ts:1496 — <c>createTrue</c>.</summary>
    public TrueLiteral CreateTrue() => (TrueLiteral)CreateToken(SyntaxKind.TrueKeyword);

    /// <summary>ported from: factory/nodeFactory.ts:1501 — <c>createFalse</c>.</summary>
    public FalseLiteral CreateFalse() => (FalseLiteral)CreateToken(SyntaxKind.FalseKeyword);

    //
    // Modifiers
    //

    /// <summary>ported from: factory/nodeFactory.ts:1543 — <c>createModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS forwards to <c>createToken</c>, whose object is a plain token. The port's <c>ModifierToken</c> is
    /// a class of its own, so the token is built here — with the transform flags <c>createToken</c> would have given
    /// it, which is why that switch is a function of its own.
    /// </remarks>
    public ModifierToken CreateModifier(SyntaxKind kind)
    {
        var node = new ModifierToken(kind);
        node.TransformFlags |= TokenTransformFlagsOf(kind);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1555 — <c>createModifiersFromModifierFlags</c>.</summary>
    /// <remarks>
    /// The order matters: it is the order the emitted modifiers appear in, and TS writes them in this sequence.
    /// </remarks>
    public List<ModifierToken>? CreateModifiersFromModifierFlags(ModifierFlags flags)
    {
        var result = new List<ModifierToken>();
        if ((flags & ModifierFlags.Export) != 0) result.Add(CreateModifier(SyntaxKind.ExportKeyword));
        if ((flags & ModifierFlags.Ambient) != 0) result.Add(CreateModifier(SyntaxKind.DeclareKeyword));
        if ((flags & ModifierFlags.Default) != 0) result.Add(CreateModifier(SyntaxKind.DefaultKeyword));
        if ((flags & ModifierFlags.Const) != 0) result.Add(CreateModifier(SyntaxKind.ConstKeyword));
        if ((flags & ModifierFlags.Public) != 0) result.Add(CreateModifier(SyntaxKind.PublicKeyword));
        if ((flags & ModifierFlags.Private) != 0) result.Add(CreateModifier(SyntaxKind.PrivateKeyword));
        if ((flags & ModifierFlags.Protected) != 0) result.Add(CreateModifier(SyntaxKind.ProtectedKeyword));
        if ((flags & ModifierFlags.Abstract) != 0) result.Add(CreateModifier(SyntaxKind.AbstractKeyword));
        if ((flags & ModifierFlags.Static) != 0) result.Add(CreateModifier(SyntaxKind.StaticKeyword));
        if ((flags & ModifierFlags.Override) != 0) result.Add(CreateModifier(SyntaxKind.OverrideKeyword));
        if ((flags & ModifierFlags.Readonly) != 0) result.Add(CreateModifier(SyntaxKind.ReadonlyKeyword));
        if ((flags & ModifierFlags.Accessor) != 0) result.Add(CreateModifier(SyntaxKind.AccessorKeyword));
        if ((flags & ModifierFlags.Async) != 0) result.Add(CreateModifier(SyntaxKind.AsyncKeyword));
        if ((flags & ModifierFlags.In) != 0) result.Add(CreateModifier(SyntaxKind.InKeyword));
        if ((flags & ModifierFlags.Out) != 0) result.Add(CreateModifier(SyntaxKind.OutKeyword));

        return result.Count != 0 ? result : null;
    }

    //
    // Helpers TS keeps private to the factory
    //

    /// <summary>ported from: factory/nodeFactory.ts:7141 — <c>asName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's signature is generic over <c>string | T</c> and returns <c>T | Identifier</c>. The port answers
    /// with the node it was handed when it is not a string, which is what every call site relies on.
    /// </remarks>
    private Identifier? AsName(object? name) => name is string text ? CreateIdentifier(text) : name as Identifier;

    /// <summary>ported from: factory/nodeFactory.ts:7146 — <c>asExpression</c>.</summary>
    private Expression? AsExpression(object? value) => value switch
    {
        null => null,
        string text => CreateStringLiteral(text, null, null),
        int number => CreateNumericLiteral(number, null),
        bool boolean => boolean ? CreateTrue() : CreateFalse(),
        _ => value as Expression,
    };

    /// <summary>ported from: factory/nodeFactory.ts:7157 — <c>asToken</c>.</summary>
    private Node? AsToken(object? value) => value is SyntaxKind kind ? CreateToken(kind) : value as Node;

    //
    // Transform flags of a name
    //

    /// <summary>ported from: factory/nodeFactory.ts:7305 — <c>propagateChildrenFlags</c>.</summary>
    private static TransformFlags PropagateChildrenFlags<T>(NodeArray<T>? children)
        where T : Node => children is null ? TransformFlags.None : children.TransformFlags;

    /// <summary>ported from: factory/nodeFactory.ts:7290 — <c>propagateIdentifierNameFlags</c>.</summary>
    /// <remarks>An identifier name is allowed to be <c>await</c>, so it does not make its subtree contain one.</remarks>
    private static TransformFlags PropagateIdentifierNameFlags(Node node)
        => CoreFlags.Clear(PropagateChildFlags(node), TransformFlags.ContainsPossibleTopLevelAwait);

    /// <summary>ported from: factory/nodeFactory.ts:7286 — <c>propagateNameFlags</c>.</summary>
    private static TransformFlags PropagateNameFlags(Node? node)
        => node is Identifier identifier ? PropagateIdentifierNameFlags(identifier) : PropagateChildFlags(node);

    //
    // Node arrays
    //

    /// <summary>ported from: factory/nodeFactory.ts:1169 — <c>createNodeArray</c>.</summary>
    /// <remarks>
    /// Two behaviours matter to callers and are covered by tests: an array that is already a
    /// <see cref="NodeArray{T}"/> with the same <paramref name="hasTrailingComma"/> comes back as the <em>same
    /// instance</em>, and every array that leaves here has its <c>TransformFlags</c> aggregated over its children.
    /// <para>
    /// PORT-NOTE: TS has two memory-layout tricks that have no C# counterpart. For one to four elements it calls
    /// <c>slice()</c> so the VM can pick a compact representation; for five or more it uses the caller's array
    /// <em>as</em> the <see cref="NodeArray{T}"/> — legal in JS, where properties can be hung on an array. A C#
    /// <see cref="List{T}"/> cannot become a <see cref="NodeArray{T}"/>, so this always allocates and copies.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS also treats <c>core.ts</c>'s shared <c>emptyArray</c> as "no elements" and attaches debug info
    /// to the result in debug builds (<c>Debug.attachNodeArrayDebugInfo</c>). Neither is ported: a C# call site has
    /// no shared empty array to compare against, and the debug info only decorates the object for inspection.
    /// </para>
    /// </remarks>
    public NodeArray<T> CreateNodeArray<T>(List<T>? elements, bool? hasTrailingComma)
        where T : Node
    {
        if (elements is null)
        {
            elements = [];
        }
        else if (elements is NodeArray<T> existing)
        {
            if (hasTrailingComma is null || existing.HasTrailingComma == hasTrailingComma.Value)
            {
                // Ensure the transform flags have been aggregated for this NodeArray.
                if (existing.TransformFlags == TransformFlags.None) AggregateChildrenFlags(existing);
                return existing;
            }

            // This *was* a NodeArray, but the hasTrailingComma option differs: recreate it with the same elements,
            // text range and transform flags, but the updated value.
            return new NodeArray<T>(existing)
            {
                Pos = existing.Pos,
                End = existing.End,
                HasTrailingComma = hasTrailingComma.Value,
                TransformFlags = existing.TransformFlags,
            };
        }

        var array = new NodeArray<T>(elements)
        {
            Pos = -1,
            End = -1,
            HasTrailingComma = hasTrailingComma ?? false,
        };

        AggregateChildrenFlags(array);
        return array;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7137 — <c>asNodeArray</c>.</summary>
    private NodeArray<T>? AsNodeArray<T>(List<T>? array)
        where T : Node => array is null ? null : CreateNodeArray(array, null);

    //
    // Transform flags
    //

    /// <summary>ported from: factory/nodeFactory.ts:7309 — <c>aggregateChildrenFlags</c>.</summary>
    private static void AggregateChildrenFlags<T>(NodeArray<T> children)
        where T : Node
    {
        var subtreeFlags = TransformFlags.None;
        foreach (var child in children)
        {
            subtreeFlags |= PropagateChildFlags(child);
        }

        children.TransformFlags = subtreeFlags;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7299 — <c>propagateChildFlags</c>.</summary>
    private static TransformFlags PropagateChildFlags(Node? child)
    {
        if (child is null) return TransformFlags.None;

        var childFlags = CoreFlags.Clear(child.TransformFlags, GetTransformFlagsSubtreeExclusions(child.Kind));

        // A property name contributes more than its own flags: a computed name makes the whole subtree "contains a
        // computed property name", which is what a property declaration's exclusions are about.
        return child is INamedDeclaration { Name: { } name } && Utilities.IsPropertyName(name)
            ? PropagatePropertyNameFlagsOfChild(name, childFlags)
            : childFlags;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7295 — <c>propagatePropertyNameFlagsOfChild</c>.</summary>
    private static TransformFlags PropagatePropertyNameFlagsOfChild(Node node, TransformFlags transformFlags)
        => transformFlags | (node.TransformFlags & TransformFlags.PropertyNamePropagatingFlags);

    /// <summary>ported from: factory/nodeFactory.ts:7320 — <c>getTransformFlagsSubtreeExclusions</c>.</summary>
    /// <remarks>
    /// The flags returned here are the ones a child <em>stops</em> propagating: a nested function body, for instance,
    /// does not make the enclosing declaration contain a `yield`.
    /// </remarks>
    private static TransformFlags GetTransformFlagsSubtreeExclusions(SyntaxKind kind)
    {
        if (kind >= SyntaxKind.FirstTypeNode && kind <= SyntaxKind.LastTypeNode)
        {
            return TransformFlags.TypeExcludes;
        }

        return kind switch
        {
            SyntaxKind.CallExpression or SyntaxKind.NewExpression or SyntaxKind.ArrayLiteralExpression
                => TransformFlags.ArrayLiteralOrCallOrNewExcludes,
            SyntaxKind.ModuleDeclaration => TransformFlags.ModuleExcludes,
            SyntaxKind.Parameter => TransformFlags.ParameterExcludes,
            SyntaxKind.ArrowFunction => TransformFlags.ArrowFunctionExcludes,
            SyntaxKind.FunctionExpression or SyntaxKind.FunctionDeclaration => TransformFlags.FunctionExcludes,
            SyntaxKind.VariableDeclarationList => TransformFlags.VariableDeclarationListExcludes,
            SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression => TransformFlags.ClassExcludes,
            SyntaxKind.Constructor => TransformFlags.ConstructorExcludes,
            SyntaxKind.PropertyDeclaration => TransformFlags.PropertyExcludes,
            SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor
                => TransformFlags.MethodOrAccessorExcludes,
            SyntaxKind.AnyKeyword or SyntaxKind.NumberKeyword or SyntaxKind.BigIntKeyword or SyntaxKind.NeverKeyword
                or SyntaxKind.StringKeyword or SyntaxKind.ObjectKeyword or SyntaxKind.BooleanKeyword
                or SyntaxKind.SymbolKeyword or SyntaxKind.VoidKeyword or SyntaxKind.TypeParameter
                or SyntaxKind.PropertySignature or SyntaxKind.MethodSignature or SyntaxKind.CallSignature
                or SyntaxKind.ConstructSignature or SyntaxKind.IndexSignature or SyntaxKind.InterfaceDeclaration
                or SyntaxKind.TypeAliasDeclaration
                => TransformFlags.TypeExcludes,
            SyntaxKind.ObjectLiteralExpression => TransformFlags.ObjectLiteralExcludes,
            SyntaxKind.CatchClause => TransformFlags.CatchClauseExcludes,
            SyntaxKind.ObjectBindingPattern or SyntaxKind.ArrayBindingPattern => TransformFlags.BindingPatternExcludes,
            SyntaxKind.TypeAssertionExpression or SyntaxKind.SatisfiesExpression or SyntaxKind.AsExpression
                or SyntaxKind.PartiallyEmittedExpression or SyntaxKind.ParenthesizedExpression or SyntaxKind.SuperKeyword
                => TransformFlags.OuterExpressionExcludes,
            SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression
                => TransformFlags.PropertyAccessExcludes,
            _ => TransformFlags.NodeExcludes,
        };
    }

    //
    // Updating
    //

    /// <summary>ported from: factory/nodeFactory.ts:7179 — <c>update</c>.</summary>
    /// <remarks>
    /// Every <c>update*</c> member ends in this: when something changed, the new node inherits the old node's original
    /// pointer and text range; when nothing changed, the old node is returned untouched — which is why
    /// <see cref="CreateNodeArray{T}"/> hands back the same array and why callers compare members with reference
    /// equality.
    /// </remarks>
    private T Update<T>(T updated, T? original)
        where T : Node
    {
        if (!ReferenceEquals(updated, original))
        {
            SetOriginal(updated, original);
            Utilities.SetTextRange(updated, original);
        }

        return updated;
    }

    /// <summary>ported from: factory/nodeFactory.ts:493 — the <c>setOriginal</c> binding.</summary>
    /// <remarks>
    /// TS binds this once, to <c>identity</c> or to <c>setOriginalNode</c> according to
    /// <see cref="NodeFactoryFlags.NoOriginalNode"/>. C# cannot substitute a method, so the flag is read at the call
    /// site instead.
    /// </remarks>
    private void SetOriginal(Node node, Node? original)
    {
        if ((_flags & NodeFactoryFlags.NoOriginalNode) == 0) SetOriginalNode(node, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:7420 — <c>setOriginalNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this as a module-level export rather than a member of the factory object, but it belongs
    /// to the same file and is only reached through <see cref="Update{T}"/>, so it lives here.
    /// </remarks>
    public static T SetOriginalNode<T>(T node, Node? original)
        where T : Node
    {
        if (!ReferenceEquals(node.Original, original))
        {
            node.Original = original;

            // An original that carries emit information passes it on: the transform that produced this node did not
            // necessarily re-derive the comments, source maps or helpers that were attached to the node it replaced.
            if (original?.EmitNode is { } emitNode)
            {
                node.EmitNode = MergeEmitNode(emitNode, node.EmitNode);
            }
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7434 — <c>mergeEmitNode</c>.</summary>
    /// <remarks>
    /// TS lists one line per property of <c>EmitNode</c>, including the ones that deliberately do not merge, and the
    /// port keeps that shape: a property that is quietly forgotten here changes emit output in ways that are hard to
    /// trace back.
    /// </remarks>
    private static IEmitNode MergeEmitNode(IEmitNode source, IEmitNode? dest)
    {
        dest ??= new EmitNode();

        // `flags` overwrites the destination.
        if (source.Flags != EmitFlags.None) dest.Flags = source.Flags;

        // `internalFlags` overwrites the destination; the source's immutability is not copied over.
        if (source.InternalFlags != InternalEmitFlags.None)
        {
            dest.InternalFlags = CoreFlags.Clear(source.InternalFlags, InternalEmitFlags.Immutable);
        }

        // `annotatedNodes` are not merged: they belong to the parse tree node of a `SourceFile`.

        // `leadingComments` / `trailingComments` are concatenated — the source's copy first, then the destination's.
        if (source.LeadingComments is { } leading)
        {
            var merged = new List<ISynthesizedComment>(leading);
            if (dest.LeadingComments is { } existingLeading) merged.AddRange(existingLeading);
            dest.LeadingComments = merged;
        }

        if (source.TrailingComments is { } trailing)
        {
            var merged = new List<ISynthesizedComment>(trailing);
            if (dest.TrailingComments is { } existingTrailing) merged.AddRange(existingTrailing);
            dest.TrailingComments = merged;
        }

        // `commentRange` and `sourceMapRange` overwrite the destination.
        if (source.CommentRange is { } commentRange) dest.CommentRange = commentRange;
        if (source.SourceMapRange is { } sourceMapRange) dest.SourceMapRange = sourceMapRange;

        // `tokenSourceMapRanges` are merged with the destination.
        if (source.TokenSourceMapRanges is { } tokenSourceMapRanges)
        {
            dest.TokenSourceMapRanges = MergeTokenSourceMapRanges(tokenSourceMapRanges, dest.TokenSourceMapRanges);
        }

        // `constantValue` overwrites the destination.
        if (source.ConstantValue is not null) dest.ConstantValue = source.ConstantValue;

        // `externalHelpersModuleName` and `externalHelpers` are not merged.

        // `helpers` are merged into the destination, without duplicates.
        if (source.Helpers is { } helpers)
        {
            foreach (var helper in helpers)
            {
                if (dest.Helpers?.Contains(helper) != true)
                {
                    (dest.Helpers ??= []).Add(helper);
                }
            }
        }

        // `startsOnNewLine`, `snippetElement`, `classThis` and `assignedName` overwrite the destination.
        if (source.StartsOnNewLine is { } startsOnNewLine) dest.StartsOnNewLine = startsOnNewLine;
        if (source.SnippetElement is { } snippetElement) dest.SnippetElement = snippetElement;

        // `typeNode` is not merged: it only applies to comment emit for a variable declaration.
        // PORT-NOTE: TS carries a TODO here ("typeNode should overwrite the destination") and so does the port, so the
        // behaviour stays comparable.

        if (source.ClassThis is { } classThis) dest.ClassThis = classThis;
        if (source.AssignedName is { } assignedName) dest.AssignedName = assignedName;

        // `identifierTypeArguments`, `autoGenerate` and `generatedImportReference` are not merged: they only apply to
        // one specific generated identifier.

        return dest;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7542 — <c>mergeTokenSourceMapRanges</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns by index into a sparse array, which grows on demand; <see cref="List{T}"/> throws instead,
    /// so the gaps are filled in first. The same "null hole" treatment appears in <c>SysWatch</c> (§13.4).
    /// </remarks>
    private static List<ISourceMapRange>? MergeTokenSourceMapRanges(List<ISourceMapRange> source, List<ISourceMapRange>? dest)
    {
        dest ??= [];

        for (var i = 0; i < source.Count; i++)
        {
            while (dest.Count <= i) dest.Add(null!);
            dest[i] = source[i];
        }

        return dest;
    }
}
