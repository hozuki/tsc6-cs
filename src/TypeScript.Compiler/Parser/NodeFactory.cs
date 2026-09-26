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
/// PORT-NOTE: it does <em>not</em> yet declare <c>INodeFactory</c> — the generated interface with one member per
/// declaration in <c>types.ts</c>. Its 503 members reach past this file: <c>createAdd</c>/<c>createBitwiseAnd</c>/
/// <c>createAssignment</c>, <c>CloneNode</c> and the prologue copiers are declared by <c>factory/utilities.ts</c>
/// (**M4d**), and <c>baseFactory</c>/<c>converters</c> belong to <c>baseNodeFactory.ts</c>, which is not ported at all
/// (§15.2). Declaring it today buys 284 errors — 272 unimplemented members and 12 signature mismatches — that say
/// nothing about the ported surface. The step belongs to M4d, once those members exist and the generator can drop the
/// two that never will.
/// </para>
/// </remarks>
internal partial class NodeFactory
{
    private readonly NodeFactoryFlags _flags;

    public NodeFactory(NodeFactoryFlags flags = NodeFactoryFlags.None) => _flags = flags;

    /// <summary>How this factory was configured (see <see cref="NodeFactoryFlags"/>).</summary>
    public NodeFactoryFlags Flags => _flags;

    /// <summary>ported from: factory/nodeFactory.ts:496 — the <c>parenthesizerRules()</c> the factory keeps lazily.</summary>
    /// <remarks>
    /// PORT-NOTE: TS memoizes this per factory and can switch it off with
    /// <see cref="NodeFactoryFlags.NoParenthesizerRules"/>. Until M4d ports <c>parenthesizerRules.ts</c> every factory
    /// answers the identity rules — see <see cref="NullParenthesizerRules"/> for what that costs.
    /// </remarks>
    public IParenthesizerRules Parenthesizer { get; } = NullParenthesizerRules.Instance;

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

        // TS's `createToken(token: TKind extends PunctuationSyntaxKind)` answers a `PunctuationToken<TKind>`; the port's
        // token classes carry the kind at run time, so the range is what picks the class (Model/Token.cs).
        _ when token >= SyntaxKind.FirstPunctuation && token <= SyntaxKind.LastPunctuation => new PunctuationToken(token),

        // A keyword that is a type is a `KeywordTypeNode<TKind>` in TS — see KeywordTypeToken for why it needs a class.
        _ when IsKeywordTypeKind(token) => new KeywordTypeToken(token),
        _ => new Token(token),
    };

    /// <summary>TS's <c>KeywordTypeSyntaxKind</c>: the keywords that are also types.</summary>
    private static bool IsKeywordTypeKind(SyntaxKind kind) => kind is
        SyntaxKind.AnyKeyword or SyntaxKind.BigIntKeyword or SyntaxKind.BooleanKeyword
        or SyntaxKind.IntrinsicKeyword or SyntaxKind.NeverKeyword or SyntaxKind.NumberKeyword
        or SyntaxKind.ObjectKeyword or SyntaxKind.StringKeyword or SyntaxKind.SymbolKeyword
        or SyntaxKind.UndefinedKeyword or SyntaxKind.UnknownKeyword or SyntaxKind.VoidKeyword;

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

    /// <summary>ported from: factory/nodeFactory.ts:7153 — <c>asInitializer</c>.</summary>
    private Expression? AsInitializer(Expression? node)
        => node is null ? null : Parenthesizer.ParenthesizeExpressionForDisallowedComma(node);

    //
    // Transform flags of a name
    //

    /// <summary>ported from: factory/nodeFactory.ts:7305 — <c>propagateChildrenFlags</c>.</summary>
    /// <remarks>
    /// The element type is <see cref="INode"/> rather than <see cref="Node"/>: a categorisation step such as
    /// <c>TypeNode</c> is an interface in the port, and a <c>NodeArray</c> of it is a real thing the factory builds
    /// (docs/architecture.md §12.9).
    /// </remarks>
    private static TransformFlags PropagateChildrenFlags<T>(NodeArray<T>? children)
        where T : INode => children is null ? TransformFlags.None : children.TransformFlags;

    /// <summary>ported from: factory/nodeFactory.ts:7290 — <c>propagateIdentifierNameFlags</c>.</summary>
    /// <remarks>
    /// An identifier name is allowed to be <c>await</c>, so it does not make its subtree contain one.
    /// PORT-NOTE: TS declares the parameter non-null; a generated call site can pass a nullable member
    /// (<c>QualifiedName.right</c>), so an absent node answers "no flags" rather than throwing.
    /// </remarks>
    private static TransformFlags PropagateIdentifierNameFlags(INode? node)
        => node is null
            ? TransformFlags.None
            : CoreFlags.Clear(PropagateChildFlags(node), TransformFlags.ContainsPossibleTopLevelAwait);

    /// <summary>ported from: factory/nodeFactory.ts:7286 — <c>propagateNameFlags</c>.</summary>
    private static TransformFlags PropagateNameFlags(INode? node)
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
    public NodeArray<T> CreateNodeArray<T>(IList<T>? elements, bool? hasTrailingComma)
        where T : INode
    {
        if (elements is null)
        {
            elements = new List<T>();
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
        where T : INode => array is null ? null : CreateNodeArray(array, null);

    //
    // Transform flags
    //

    /// <summary>ported from: factory/nodeFactory.ts:7309 — <c>aggregateChildrenFlags</c>.</summary>
    private static void AggregateChildrenFlags<T>(NodeArray<T> children)
        where T : INode
    {
        var subtreeFlags = TransformFlags.None;
        foreach (var child in children)
        {
            subtreeFlags |= PropagateChildFlags(child);
        }

        children.TransformFlags = subtreeFlags;
    }

    /// <summary>ported from: factory/nodeFactory.ts:7299 — <c>propagateChildFlags</c>.</summary>
    private static TransformFlags PropagateChildFlags(INode? child)
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
    private static TransformFlags PropagatePropertyNameFlagsOfChild(INode node, TransformFlags transformFlags)
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

    //
    // Literals (the hand-written half of the factory)
    //

    /// <summary>ported from: factory/nodeFactory.ts:1281 — <c>createLiteralLikeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the first parameter as <c>LiteralToken["kind"] | SyntaxKind.JsxTextAllWhiteSpaces</c> and
    /// returns <c>LiteralToken</c>. The port takes a <c>SyntaxKind</c> and answers the widest node type, because the
    /// kinds it can be are exactly the switch arms below.
    /// </remarks>
    public Node CreateLiteralLikeNode(SyntaxKind kind, string text) => kind switch
    {
        SyntaxKind.NumericLiteral => CreateNumericLiteral(text, null),
        SyntaxKind.BigIntLiteral => CreateBigIntLiteral(text),
        SyntaxKind.StringLiteral => CreateStringLiteral(text, null, null),
        SyntaxKind.JsxText => CreateJsxText(text, false),
        SyntaxKind.JsxTextAllWhiteSpaces => CreateJsxText(text, true),
        SyntaxKind.RegularExpressionLiteral => CreateRegularExpressionLiteral(text),
        SyntaxKind.NoSubstitutionTemplateLiteral => CreateTemplateLiteralLikeNode(kind, text, null, null),
        _ => throw new InvalidOperationException($"invalid kind: {kind}"),
    };

    /// <summary>ported from: factory/nodeFactory.ts:3550 — <c>getTransformFlagsOfTemplateLiteralLike</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the parameter non-optional, while the generated call sites pass the node's
    /// <c>TemplateFlags</c>, which is nullable — an absent value means the same as <c>None</c>.
    /// </remarks>
    private static TransformFlags GetTransformFlagsOfTemplateLiteralLike(TokenFlags? templateFlags)
    {
        var transformFlags = TransformFlags.ContainsES2015;
        if (templateFlags is not null && templateFlags != TokenFlags.None)
        {
            transformFlags |= TransformFlags.ContainsES2018;
        }

        return transformFlags;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3561 — <c>createTemplateLiteralLikeToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds one <c>TemplateLiteralLikeNode</c> object and lets the static type say which of the three
    /// template tokens it is; the port has a class per kind, so the kind picks the class (the same treatment
    /// <see cref="CreateTokenNode"/> gives the token family).
    /// </remarks>
    private Node CreateTemplateLiteralLikeToken(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
    {
        var node = CreateTemplateLiteralTokenNode(kind);
        var literalLike = (ITemplateLiteralLikeNode)node;
        literalLike.Text = text;
        literalLike.RawText = rawText;
        literalLike.TemplateFlags = (templateFlags ?? TokenFlags.None) & TokenFlags.TemplateLiteralLikeFlags;
        node.TransformFlags = GetTransformFlagsOfTemplateLiteralLike(literalLike.TemplateFlags ?? TokenFlags.None);
        return node;
    }

    private static Node CreateTemplateLiteralTokenNode(SyntaxKind kind) => kind switch
    {
        SyntaxKind.TemplateHead => CreateBaseNode<TemplateHead>(kind),
        SyntaxKind.TemplateMiddle => CreateBaseNode<TemplateMiddle>(kind),
        SyntaxKind.TemplateTail => CreateBaseNode<TemplateTail>(kind),
        _ => throw new InvalidOperationException($"invalid template token kind: {kind}"),
    };

    /// <summary>ported from: factory/nodeFactory.ts:3580 — <c>createTemplateLiteralLikeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS picks between the two fixed bodies by kind and leaves the object's class to the caller's cast. The
    /// port dispatches to the generated <c>CreateTemplateLiteralLikeDeclaration</c> — whose parameter is the
    /// <c>TypedEnum</c> member the kind names (§16) — and to its own token path for the other three kinds.
    /// </remarks>
    public Node CreateTemplateLiteralLikeNode(SyntaxKind kind, string text, string? rawText, TokenFlags? templateFlags)
        => kind == SyntaxKind.NoSubstitutionTemplateLiteral
            ? CreateTemplateLiteralLikeDeclaration(
                TypedEnum.NoSubstitutionTemplateLiteral.Instance,
                text,
                rawText,
                templateFlags)
            : CreateTemplateLiteralLikeToken(kind, text, rawText, templateFlags);

    /// <summary>ported from: factory/nodeFactory.ts:3612 — <c>createYieldExpression</c>.</summary>
    public YieldExpression CreateYieldExpression(PunctuationToken? asteriskToken, Expression? expression)
    {
        Debug.Assert(
            asteriskToken is null || expression is not null,
            "A `YieldExpression` with an asteriskToken must have an expression.");

        var node = CreateBaseNode<YieldExpression>(SyntaxKind.YieldExpression);
        node.Expression = AsInitializer(expression);
        node.AsteriskToken = asteriskToken;
        node.TransformFlags |= PropagateChildFlags(node.Expression)
            | PropagateChildFlags(node.AsteriskToken)
            | TransformFlags.ContainsES2015
            | TransformFlags.ContainsES2018
            | TransformFlags.ContainsYield;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3695 — <c>createOmittedExpression</c>.</summary>
    public OmittedExpression CreateOmittedExpression()
        => CreateBaseNode<OmittedExpression>(SyntaxKind.OmittedExpression);

    //
    // Identifiers (the generated-name half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:1388 — <c>createBasePrivateIdentifier</c>.</summary>
    private static PrivateIdentifier CreateBasePrivateIdentifier(string escapedText)
    {
        var node = CreateBaseNode<PrivateIdentifier>(SyntaxKind.PrivateIdentifier);
        node.EscapedText = escapedText;
        node.TransformFlags |= TransformFlags.ContainsClassFields;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1396 — <c>createPrivateIdentifier</c>.</summary>
    public PrivateIdentifier CreatePrivateIdentifier(string? text)
    {
        if (text is null || !text.StartsWith('#'))
        {
            Debug.Fail($"First character of private identifier must be #: {text}");
        }

        return CreateBasePrivateIdentifier(Utilities.EscapeLeadingUnderscores(text!));
    }

    //
    // Punctuation and keywords (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:3794 — <c>createMetaProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the parameter as <c>MetaProperty["keywordToken"]</c>, whose two members are the switch arms
    /// and which maps to the enum itself.
    /// </remarks>
    public MetaProperty CreateMetaProperty(SyntaxKind keywordToken, Identifier? name)
    {
        var node = CreateBaseNode<MetaProperty>(SyntaxKind.MetaProperty);
        node.KeywordToken = keywordToken;
        node.Name = name;
        node.TransformFlags |= PropagateChildFlags(node.Name);

        switch (keywordToken)
        {
            case SyntaxKind.NewKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2015;
                break;
            case SyntaxKind.ImportKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2020;
                break;
            default:
                Debug.AssertNever(keywordToken);
                break;
        }

        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3747 — <c>updateNonNullExpression</c>.</summary>
    public NonNullExpression UpdateNonNullExpression(NonNullExpression? node, Expression? expression)
    {
        // A `NonNullChain` is the same class with the `OptionalChain` flag, and it has an update of its own.
        if ((node!.Flags & NodeFlags.OptionalChain) != 0) return UpdateNonNullChain(node, expression);

        return node!.Expression != expression
            ? Update(CreateNonNullExpression(expression), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3786 — <c>updateNonNullChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the parameter as <c>NonNullChain</c>, a view of <c>NonNullExpression</c> that only exists
    /// in its type system; the two are one class here (§12.9).
    /// </remarks>
    public NonNullExpression UpdateNonNullChain(NonNullExpression? node, Expression? expression)
    {
        Debug.Assert(
            node is not null && (node.Flags & NodeFlags.OptionalChain) != 0,
            "Cannot update a NonNullExpression using updateNonNullChain. Use updateNonNullExpression instead.");

        return node.Expression != expression
            ? Update(CreateNonNullChain(expression), node)
            : node;
    }

    //
    // Helpers TS keeps private to the factory (hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:7161 — <c>asEmbeddedStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS is generic over the statement so the result is <c>T | EmptyStatement</c>. A statement is an
    /// interface in the port rather than a class (§12.9), and every statement — the replacement included — satisfies it,
    /// so the interface is what the signature names.
    /// </remarks>
    private IStatement? AsEmbeddedStatement(IStatement? statement)
    {
        if (statement is not Node node || !NodeTests.IsNotEmittedStatement(node)) return statement;

        var empty = CreateEmptyStatement();
        SetOriginal(empty, node);
        return Utilities.SetTextRange(empty, statement as ITextRange);
    }

    /// <summary>ported from: factory/nodeFactory.ts:7167 — <c>asVariableDeclaration</c>.</summary>
    private VariableDeclaration? AsVariableDeclaration(object? variableDeclaration)
    {
        if (variableDeclaration is string
            || (variableDeclaration is Node node && !NodeTests.IsVariableDeclaration(node)))
        {
            return CreateVariableDeclaration(variableDeclaration, null, null, null);
        }

        return variableDeclaration as VariableDeclaration;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6281 — <c>flattenCommaElements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns <c>Expression | readonly Expression[]</c>; the port answers <c>object</c>, which is what
    /// <see cref="Core.SameFlatMap{T}"/> was given for exactly this caller.
    /// </remarks>
    private static object FlattenCommaElements(Expression node, int index) => FlattenCommaElements(node);

    /// <remarks>
    /// PORT-NOTE: the two-argument overload above exists because TS passes this helper to <c>sameFlatMap</c> as a
    /// callback, and TS lets a callback take fewer parameters than the signature it satisfies — C# does not.
    /// </remarks>
    private static object FlattenCommaElements(Expression node)
    {
        if (Utilities.NodeIsSynthesized(node)
            && !Utilities.IsParseTreeNode(node)
            && node.Original is null
            && node.EmitNode is null
            && node.Id is null)
        {
            if (NodeTests.IsCommaListExpression(node)) return ((CommaListExpression)node).Elements!;

            if (NodeTests.IsBinaryExpression(node)
                && ((BinaryExpression)node).OperatorToken is { } operatorToken
                && NodeTests.IsCommaToken(operatorToken))
            {
                var binary = (BinaryExpression)node;
                return new List<Expression> { binary.Left!, binary.Right! };
            }
        }

        return node;
    }

    //
    // Emit helpers TS builds out of the factory
    //

    /// <summary>ported from: factory/nodeFactory.ts:6277 — <c>createNotEmittedTypeElement</c>.</summary>
    public NotEmittedTypeElement CreateNotEmittedTypeElement()
        => CreateBaseNode<NotEmittedTypeElement>(SyntaxKind.NotEmittedTypeElement);

    /// <summary>ported from: factory/nodeFactory.ts:6446 — <c>createVoidZero</c>.</summary>
    public VoidExpression CreateVoidZero() => CreateVoidExpression(CreateNumericLiteral("0", null));

    /// <summary>ported from: factory/nodeFactory.ts:6450 — <c>createExportDefault</c>.</summary>
    public ExportAssignment CreateExportDefault(Expression? expression)
        => CreateExportAssignment(null, false, expression);

    /// <summary>ported from: factory/nodeFactory.ts:6458 — <c>createExternalModuleExport</c>.</summary>
    public ExportDeclaration CreateExternalModuleExport(Identifier? exportName)
        => CreateExportDeclaration(
            null,
            false,
            CreateNamedExports([CreateExportSpecifier(false, null, exportName)]),
            null,
            null);

    //
    // Signature elements and type elements (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:1645 — <c>createParameterDeclaration</c>.</summary>
    public ParameterDeclaration CreateParameterDeclaration(
        List<Node>? modifiers,
        PunctuationToken? dotDotDotToken,
        object? name,
        PunctuationToken? questionToken,
        ITypeNode? type,
        Expression? initializer)
    {
        var node = CreateBaseDeclaration<ParameterDeclaration>(SyntaxKind.Parameter);
        node.Modifiers = AsNodeArray(modifiers);
        node.DotDotDotToken = dotDotDotToken;
        node.Name = AsName(name);
        node.QuestionToken = questionToken;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);

        if (Utilities.IsThisIdentifier(node.Name))
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            // PORT-NOTE: `x ?? y ? … : …` is a truthiness test on a node in TS; an absent node is the only falsy one,
            // so the port asks whether either is present.
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
                | PropagateChildFlags(node.DotDotDotToken)
                | PropagateNameFlags(node.Name)
                | PropagateChildFlags(node.QuestionToken)
                | PropagateChildFlags(node.Initializer)
                | (node.QuestionToken is not null || node.Type is not null
                    ? TransformFlags.ContainsTypeScript
                    : TransformFlags.None)
                | (node.DotDotDotToken is not null || node.Initializer is not null
                    ? TransformFlags.ContainsES2015
                    : TransformFlags.None)
                | ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.ParameterPropertyModifier) != 0
                    ? TransformFlags.ContainsTypeScriptClassSyntax
                    : TransformFlags.None);
        }

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1741 — <c>updatePropertySignature</c>.</summary>
    public PropertySignature UpdatePropertySignature(
        PropertySignature? node,
        List<ModifierToken>? modifiers,
        Node? name,
        PunctuationToken? questionToken,
        ITypeNode? type)
    {
        return node!.Modifiers != modifiers
            || node!.Name != name
            || node!.QuestionToken != questionToken
            || node!.Type != type
            ? FinishUpdatePropertySignature(CreatePropertySignature(modifiers, name, questionToken, type), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1757 — <c>finishUpdatePropertySignature</c>.</summary>
    private PropertySignature FinishUpdatePropertySignature(PropertySignature updated, PropertySignature original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the child TS keeps only for error reporting.
            updated.Initializer = original.Initializer;
        }

        return Update(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:1765 — <c>createPropertyDeclaration</c>.</summary>
    public PropertyDeclaration CreatePropertyDeclaration(
        List<Node>? modifiers,
        object? name,
        PunctuationToken? questionOrExclamationToken,
        ITypeNode? type,
        Expression? initializer)
    {
        var node = CreateBaseDeclaration<PropertyDeclaration>(SyntaxKind.PropertyDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.QuestionToken = questionOrExclamationToken is not null && NodeTests.IsQuestionToken(questionOrExclamationToken)
            ? questionOrExclamationToken
            : null;
        node.ExclamationToken = questionOrExclamationToken is not null && NodeTests.IsExclamationToken(questionOrExclamationToken)
            ? questionOrExclamationToken
            : null;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);

        var isAmbient = (node.Flags & NodeFlags.Ambient) != 0
            || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0;

        node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
            | PropagateNameFlags(node.Name)
            | PropagateChildFlags(node.Initializer)
            | (isAmbient || node.QuestionToken is not null || node.ExclamationToken is not null || node.Type is not null
                ? TransformFlags.ContainsTypeScript
                : TransformFlags.None)
            | (NodeTests.IsComputedPropertyName(node.Name!)
                || ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Static) != 0 && node.Initializer is not null)
                    ? TransformFlags.ContainsTypeScriptClassSyntax
                    : TransformFlags.None)
            | TransformFlags.ContainsClassFields;

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1822 — <c>updateMethodSignature</c>.</summary>
    public MethodSignature UpdateMethodSignature(
        MethodSignature? node,
        List<ModifierToken>? modifiers,
        Node? name,
        PunctuationToken? questionToken,
        NodeArray<TypeParameterDeclaration>? typeParameters,
        NodeArray<ParameterDeclaration>? parameters,
        ITypeNode? type)
    {
        return node!.Modifiers != modifiers
            || node!.Name != name
            || node!.QuestionToken != questionToken
            || node!.TypeParameters != typeParameters
            || node!.Parameters != parameters
            || node!.Type != type
            ? FinishUpdateBaseSignatureDeclaration(
                CreateMethodSignature(modifiers, name, questionToken, typeParameters, parameters, type),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1852 — <c>finishUpdateBaseSignatureDeclaration</c>.</summary>
    private T FinishUpdateBaseSignatureDeclaration<T>(T updated, T? original)
        where T : Node, ISignatureDeclarationBase
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the child TS keeps for quick info.
            updated.TypeArguments = original!.TypeArguments;
        }

        return Update(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:1846 — <c>createMethodDeclaration</c>.</summary>
    public MethodDeclaration CreateMethodDeclaration(
        List<Node>? modifiers,
        PunctuationToken? asteriskToken,
        object? name,
        PunctuationToken? questionToken,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        var node = CreateBaseDeclaration<MethodDeclaration>(SyntaxKind.MethodDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name);
        node.QuestionToken = questionToken;
        node.ExclamationToken = null; // initialized by parser for grammar errors
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters, null);
        node.Type = type;
        node.Body = body;

        if (node.Body is null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;
            var isGenerator = node.AsteriskToken is not null;
            var isAsyncGenerator = isAsync && isGenerator;

            node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
                | PropagateChildFlags(node.AsteriskToken)
                | PropagateNameFlags(node.Name)
                | PropagateChildFlags(node.QuestionToken)
                | PropagateChildrenFlags(node.TypeParameters)
                | PropagateChildrenFlags(node.Parameters)
                | PropagateChildFlags(node.Type)
                | (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait)
                | (isAsyncGenerator ? TransformFlags.ContainsES2018
                    : isAsync ? TransformFlags.ContainsES2017
                    : isGenerator ? TransformFlags.ContainsGenerator
                    : TransformFlags.None)
                | (node.QuestionToken is not null || node.TypeParameters is not null || node.Type is not null
                    ? TransformFlags.ContainsTypeScript
                    : TransformFlags.None)
                | TransformFlags.ContainsES2015;
        }

        node.TypeArguments = null; // used in quick info
        node.JsDoc = null;        // initialized by parser (JsDocContainer)
        node.Locals = null;       // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null;     // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1914 — <c>updateMethodDeclaration</c>.</summary>
    public MethodDeclaration UpdateMethodDeclaration(
        MethodDeclaration? node,
        List<Node>? modifiers,
        PunctuationToken? asteriskToken,
        Node? name,
        PunctuationToken? questionToken,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.AsteriskToken != asteriskToken
            || node!.Name != name
            || node!.QuestionToken != questionToken
            || !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            || node!.Body != body
            ? FinishUpdateMethodDeclaration(
                CreateMethodDeclaration(modifiers, asteriskToken, name, questionToken, typeParameters, parameters, type, body),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1938 — <c>finishUpdateMethodDeclaration</c>.</summary>
    private MethodDeclaration FinishUpdateMethodDeclaration(MethodDeclaration updated, MethodDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the child TS keeps only for error reporting.
            updated.ExclamationToken = original.ExclamationToken;
        }

        return Update(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:1963 — <c>updateClassStaticBlockDeclaration</c>.</summary>
    public ClassStaticBlockDeclaration UpdateClassStaticBlockDeclaration(ClassStaticBlockDeclaration? node, Block? body)
    {
        return node!.Body != body
            ? FinishUpdateClassStaticBlockDeclaration(CreateClassStaticBlockDeclaration(body), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:1972 — <c>finishUpdateClassStaticBlockDeclaration</c>.</summary>
    private ClassStaticBlockDeclaration FinishUpdateClassStaticBlockDeclaration(
        ClassStaticBlockDeclaration updated,
        ClassStaticBlockDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the child TS keeps only for error reporting.
            updated.Modifiers = original.Modifiers;
        }

        return Update(updated, original);
    }

    //
    // Template literals (the hand-written half)
    //

    /// <summary>TS's module-level <c>rawTextScanner</c>: the scanner <c>getCookedText</c> reuses.</summary>
    private static Scanner? _rawTextScanner;

    /// <summary>TS's <c>invalidValueSentinel</c> — a value no cooked text can be.</summary>
    private static readonly object s_invalidValueSentinel = new();

    /// <summary>ported from: factory/nodeFactory.ts:7238 — <c>getCookedText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns <c>string | object</c> and asks <c>typeof cooked === "object"</c> whether it failed; the
    /// port answers <c>object</c> and lets the caller find out with a cast.
    /// </remarks>
    private static object GetCookedText(SyntaxKind kind, string rawText)
    {
        _rawTextScanner ??= Scanner.Create(ScriptTarget.Latest, skipTrivia: false, LanguageVariant.Standard);

        switch (kind)
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                _rawTextScanner.SetText("`" + rawText + "`", null, null);
                break;
            case SyntaxKind.TemplateHead:
                _rawTextScanner.SetText("`" + rawText + "${", null, null);
                break;
            case SyntaxKind.TemplateMiddle:
                _rawTextScanner.SetText("}" + rawText + "${", null, null);
                break;
            case SyntaxKind.TemplateTail:
                _rawTextScanner.SetText("}" + rawText + "`", null, null);
                break;
        }

        var token = _rawTextScanner.Scan();
        if (token == SyntaxKind.CloseBraceToken)
        {
            token = _rawTextScanner.ReScanTemplateToken(isTaggedTemplate: false);
        }

        if (_rawTextScanner.IsUnterminated())
        {
            _rawTextScanner.SetText(null, null, null);
            return s_invalidValueSentinel;
        }

        string? tokenValue = null;
        switch (token)
        {
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.TemplateHead:
            case SyntaxKind.TemplateMiddle:
            case SyntaxKind.TemplateTail:
                tokenValue = _rawTextScanner.GetTokenValue();
                break;
        }

        if (tokenValue is null || _rawTextScanner.Scan() != SyntaxKind.EndOfFileToken)
        {
            _rawTextScanner.SetText(null, null, null);
            return s_invalidValueSentinel;
        }

        _rawTextScanner.SetText(null, null, null);
        return tokenValue;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3527 — <c>checkTemplateLiteralLikeNode</c>.</summary>
    private static string? CheckTemplateLiteralLikeNode(SyntaxKind kind, string? text, string? rawText, TokenFlags? templateFlags)
    {
        var flags = templateFlags ?? TokenFlags.None;
        Debug.Assert(
            (flags & CoreFlags.Not(TokenFlags.TemplateLiteralLikeFlags)) == TokenFlags.None,
            "Unsupported template flags.");

        string? cooked = null;
        if (rawText is not null && rawText != text)
        {
            cooked = GetCookedText(kind, rawText) as string;
            if (cooked is null) return Debug.Fail<string?>("Invalid raw text");
        }

        if (text is null)
        {
            if (cooked is null) return Debug.Fail<string?>("Arguments 'text' and 'rawText' may not both be undefined.");
            text = cooked;
        }
        else if (cooked is not null)
        {
            Debug.Assert(
                text == cooked,
                "Expected argument 'text' to be the normalized (i.e. 'cooked') version of argument 'rawText'.");
        }

        return text;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3588 — <c>createTemplateHead</c>.</summary>
    public TemplateHead CreateTemplateHead(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags);
        return (TemplateHead)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text!, rawText, templateFlags);
    }

    /// <summary>ported from: factory/nodeFactory.ts:3594 — <c>createTemplateMiddle</c>.</summary>
    public TemplateMiddle CreateTemplateMiddle(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags);
        return (TemplateMiddle)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateMiddle, text!, rawText, templateFlags);
    }

    /// <summary>ported from: factory/nodeFactory.ts:3600 — <c>createTemplateTail</c>.</summary>
    public TemplateTail CreateTemplateTail(string? text, string? rawText = null, TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags);
        return (TemplateTail)CreateTemplateLiteralLikeNode(SyntaxKind.TemplateTail, text!, rawText, templateFlags);
    }

    /// <summary>ported from: factory/nodeFactory.ts:3606 — <c>createNoSubstitutionTemplateLiteral</c>.</summary>
    /// <remarks>
    /// TS passes <c>SyntaxKind.TemplateHead</c> to <c>checkTemplateLiteralLikeNode</c> here rather than the literal's own
    /// kind; the port keeps that, since the check only wraps the text in the delimiters it needs.
    /// </remarks>
    public NoSubstitutionTemplateLiteral CreateNoSubstitutionTemplateLiteral(
        string? text,
        string? rawText = null,
        TokenFlags? templateFlags = null)
    {
        text = CheckTemplateLiteralLikeNode(SyntaxKind.TemplateHead, text, rawText, templateFlags);
        return (NoSubstitutionTemplateLiteral)CreateTemplateLiteralLikeNode(
            SyntaxKind.NoSubstitutionTemplateLiteral,
            text!,
            rawText,
            templateFlags);
    }

    //
    // Expressions (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:2778 — <c>createObjectBindingPattern</c>.</summary>
    public ObjectBindingPattern CreateObjectBindingPattern(List<BindingElement>? elements)
    {
        var node = CreateBaseNode<ObjectBindingPattern>(SyntaxKind.ObjectBindingPattern);
        node.Elements = CreateNodeArray(elements, null);
        node.TransformFlags |= PropagateChildrenFlags(node.Elements)
            | TransformFlags.ContainsES2015
            | TransformFlags.ContainsBindingPattern;

        if ((node.TransformFlags & TransformFlags.ContainsRestOrSpread) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2018 | TransformFlags.ContainsObjectRestOrSpread;
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2790 — <c>updateObjectBindingPattern</c>.</summary>
    public ObjectBindingPattern UpdateObjectBindingPattern(ObjectBindingPattern? node, List<BindingElement>? elements)
    {
        return !ReferenceEquals(node!.Elements, elements)
            ? Update(CreateObjectBindingPattern(elements), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2848 — <c>createArrayLiteralExpression</c>.</summary>
    public ArrayLiteralExpression CreateArrayLiteralExpression(List<Expression>? elements, bool? multiLine)
    {
        var node = CreateBaseNode<ArrayLiteralExpression>(SyntaxKind.ArrayLiteralExpression);

        // A trailing omitted element needs an explicit trailing comma: `[1, 2, ,]` is an omitted element, while
        // `[1, 2, ]` is a trailing comma.
        var lastElement = elements is not null ? Core.LastOrUndefined(elements) : null;
        var elementsArray = CreateNodeArray(
            elements,
            lastElement is not null && NodeTests.IsOmittedExpression(lastElement) ? true : null);

        node.Elements = Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(elementsArray);
        node.MultiLine = multiLine;
        node.TransformFlags |= PropagateChildrenFlags(node.Elements);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2862 — <c>updateArrayLiteralExpression</c>.</summary>
    public ArrayLiteralExpression UpdateArrayLiteralExpression(ArrayLiteralExpression? node, List<Expression>? elements)
    {
        return !ReferenceEquals(node!.Elements, elements)
            ? Update(CreateArrayLiteralExpression(elements, node!.MultiLine), node)
            : node!;
    }

    /// <summary>TS's <c>asName</c> where the name may also be a private identifier.</summary>
    /// <remarks>
    /// The port's <see cref="AsName"/> answers an <c>Identifier</c> because that is what every other caller expects;
    /// a member name is a <c>MemberExpression</c> — an <c>Identifier</c> or a <c>PrivateIdentifier</c> — and TS's
    /// generic <c>asName</c> hands back whatever it was given.
    /// </remarks>
    private MemberExpression? AsMemberName(object? name)
        => name is string text ? CreateIdentifier(text) : name as MemberExpression;

    /// <summary>ported from: factory/nodeFactory.ts:2903 — <c>createPropertyAccessExpression</c>.</summary>
    public PropertyAccessExpression CreatePropertyAccessExpression(Expression? expression, object? name)
    {
        var node = CreateBasePropertyAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, false),
            null,
            AsMemberName(name));

        if (NodeTests.IsSuperKeyword(expression!))
        {
            // A `super` method call needs a lexical `this`, and it needs `super` hoisting in ES2017/ES2018 functions.
            node.TransformFlags |= TransformFlags.ContainsES2017 | TransformFlags.ContainsES2018;
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2919 — <c>updatePropertyAccessExpression</c>.</summary>
    public PropertyAccessExpression UpdatePropertyAccessExpression(
        PropertyAccessExpression? node,
        Expression? expression,
        Node? name)
    {
        if (Utilities.IsPropertyAccessChain(node!))
        {
            return UpdatePropertyAccessChain(node, expression, node!.QuestionDotToken, name as Identifier);
        }

        return node!.Expression != expression || node!.Name != name
            ? Update(CreatePropertyAccessExpression(expression, name), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2930 — <c>createPropertyAccessChain</c>.</summary>
    public PropertyAccessExpression CreatePropertyAccessChain(
        Expression? expression,
        PunctuationToken? questionDotToken,
        object? name)
    {
        var node = CreateBasePropertyAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, true),
            questionDotToken,
            AsMemberName(name));

        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2942 — <c>updatePropertyAccessChain</c>.</summary>
    public PropertyAccessExpression UpdatePropertyAccessChain(
        PropertyAccessExpression? node,
        Expression? expression,
        PunctuationToken? questionDotToken,
        Node? name)
    {
        Debug.Assert(
            node is not null && (node.Flags & NodeFlags.OptionalChain) != 0,
            "Cannot update a PropertyAccessExpression using updatePropertyAccessChain. Use updatePropertyAccess instead.");

        // Updating an existing chain keeps its emit flags rather than taking the ones `createPropertyAccess` would set.
        return node.Expression != expression
            || node.QuestionDotToken != questionDotToken
            || node.Name != name
            ? Update(CreatePropertyAccessChain(expression, questionDotToken, name), node)
            : node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2968 — <c>createElementAccessExpression</c>.</summary>
    public ElementAccessExpression CreateElementAccessExpression(Expression? expression, object? index)
    {
        var node = CreateBaseElementAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, false),
            null,
            AsExpression(index));

        if (NodeTests.IsSuperKeyword(expression!))
        {
            node.TransformFlags |= TransformFlags.ContainsES2017 | TransformFlags.ContainsES2018;
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2984 — <c>updateElementAccessExpression</c>.</summary>
    public ElementAccessExpression UpdateElementAccessExpression(
        ElementAccessExpression? node,
        Expression? expression,
        Expression? argumentExpression)
    {
        if (Utilities.IsElementAccessChain(node!))
        {
            return UpdateElementAccessChain(node, expression, node!.QuestionDotToken, argumentExpression);
        }

        return node!.Expression != expression || node!.ArgumentExpression != argumentExpression
            ? Update(CreateElementAccessExpression(expression, argumentExpression), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2995 — <c>createElementAccessChain</c>.</summary>
    public ElementAccessExpression CreateElementAccessChain(
        Expression? expression,
        PunctuationToken? questionDotToken,
        object? index)
    {
        var node = CreateBaseElementAccessExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, true),
            questionDotToken,
            AsExpression(index));

        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3007 — <c>updateElementAccessChain</c>.</summary>
    public ElementAccessExpression UpdateElementAccessChain(
        ElementAccessExpression? node,
        Expression? expression,
        PunctuationToken? questionDotToken,
        Expression? argumentExpression)
    {
        Debug.Assert(
            node is not null && (node.Flags & NodeFlags.OptionalChain) != 0,
            "Cannot update a ElementAccessExpression using updateElementAccessChain. Use updateElementAccess instead.");

        return node.Expression != expression
            || node.QuestionDotToken != questionDotToken
            || node.ArgumentExpression != argumentExpression
            ? Update(CreateElementAccessChain(expression, questionDotToken, argumentExpression), node)
            : node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3018 — <c>createBaseCallExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares no return type, so tools/Gen can only see it through the `createBase*` call — and it takes
    /// the type arguments as an already-built array rather than building them, which puts it outside the fixed shape.
    /// </remarks>
    private CallExpression CreateBaseCallExpression(
        LeftHandSideExpression expression,
        PunctuationToken? questionDotToken,
        NodeArray<ITypeNode>? typeArguments,
        NodeArray<Expression>? argumentsArray)
    {
        var node = CreateBaseDeclaration<CallExpression>(SyntaxKind.CallExpression);
        node.Expression = expression;
        node.QuestionDotToken = questionDotToken;
        node.TypeArguments = typeArguments;
        node.Arguments = argumentsArray;
        node.TransformFlags |= PropagateChildFlags(node.Expression)
            | PropagateChildFlags(node.QuestionDotToken)
            | PropagateChildrenFlags(node.TypeArguments)
            | PropagateChildrenFlags(node.Arguments);

        if (node.TypeArguments is not null) node.TransformFlags |= TransformFlags.ContainsTypeScript;
        if (Utilities.IsSuperProperty(node.Expression!)) node.TransformFlags |= TransformFlags.ContainsLexicalThis;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3038 — <c>createCallExpression</c>.</summary>
    public CallExpression CreateCallExpression(
        Expression? expression,
        List<ITypeNode>? typeArguments,
        List<Expression>? argumentsArray)
    {
        var node = CreateBaseCallExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, false),
            null,
            AsNodeArray(typeArguments),
            Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(argumentsArray, null)));

        if (NodeTests.IsImportKeyword(node.Expression!)) node.TransformFlags |= TransformFlags.ContainsDynamicImport;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3052 — <c>updateCallExpression</c>.</summary>
    public CallExpression UpdateCallExpression(
        CallExpression? node,
        Expression? expression,
        List<ITypeNode>? typeArguments,
        List<Expression>? argumentsArray)
    {
        if (Utilities.IsCallChain(node!))
        {
            return UpdateCallChain(node, expression, node!.QuestionDotToken, typeArguments, argumentsArray);
        }

        return node!.Expression != expression
            || !ReferenceEquals(node!.TypeArguments, typeArguments)
            || !ReferenceEquals(node!.Arguments, argumentsArray)
            ? Update(CreateCallExpression(expression, typeArguments, argumentsArray), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3064 — <c>createCallChain</c>.</summary>
    public CallExpression CreateCallChain(
        Expression? expression,
        PunctuationToken? questionDotToken,
        List<ITypeNode>? typeArguments,
        List<Expression>? argumentsArray)
    {
        var node = CreateBaseCallExpression(
            Parenthesizer.ParenthesizeLeftSideOfAccess(expression, true),
            questionDotToken,
            AsNodeArray(typeArguments),
            Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(CreateNodeArray(argumentsArray, null)));

        node.Flags |= NodeFlags.OptionalChain;
        node.TransformFlags |= TransformFlags.ContainsES2020;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3077 — <c>updateCallChain</c>.</summary>
    public CallExpression UpdateCallChain(
        CallExpression? node,
        Expression? expression,
        PunctuationToken? questionDotToken,
        List<ITypeNode>? typeArguments,
        List<Expression>? argumentsArray)
    {
        Debug.Assert(
            node is not null && (node.Flags & NodeFlags.OptionalChain) != 0,
            "Cannot update a CallExpression using updateCallChain. Use updateCall instead.");

        return node.Expression != expression
            || node.QuestionDotToken != questionDotToken
            || !ReferenceEquals(node.TypeArguments, typeArguments)
            || !ReferenceEquals(node.Arguments, argumentsArray)
            ? Update(CreateCallChain(expression, questionDotToken, typeArguments, argumentsArray), node)
            : node;
    }

    //
    // Statements and declarations (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:4276 — <c>createVariableDeclarationList</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS leaves <c>flags</c> unannotated, so its type is <c>any</c>; the port takes it as <c>object?</c>
    /// and narrows it to the enum, with an absent value meaning <c>None</c> — which is what TS's default is.
    /// </remarks>
    public VariableDeclarationList CreateVariableDeclarationList(
        IList<VariableDeclaration>? declarations,
        object? flags = null)
    {
        var nodeFlags = flags is NodeFlags value ? value : NodeFlags.None;
        var node = CreateBaseNode<VariableDeclarationList>(SyntaxKind.VariableDeclarationList);
        node.Flags |= nodeFlags & NodeFlags.BlockScoped;
        node.Declarations = CreateNodeArray(declarations, null);
        node.TransformFlags |= PropagateChildrenFlags(node.Declarations)
            | TransformFlags.ContainsHoistedDeclarationOrCompletion;

        if ((nodeFlags & NodeFlags.BlockScoped) != 0)
        {
            node.TransformFlags |= TransformFlags.ContainsES2015 | TransformFlags.ContainsBlockScopedBinding;
        }

        if ((nodeFlags & NodeFlags.Using) != 0) node.TransformFlags |= TransformFlags.ContainsESNext;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4292 — <c>updateVariableDeclarationList</c>.</summary>
    public VariableDeclarationList UpdateVariableDeclarationList(
        VariableDeclarationList? node,
        List<VariableDeclaration>? declarations)
    {
        return !ReferenceEquals(node!.Declarations, declarations)
            ? Update(CreateVariableDeclarationList(declarations, node!.Flags), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3876 — <c>createVariableStatement</c>.</summary>
    public VariableStatement CreateVariableStatement(List<Node>? modifiers, object? declarationList)
    {
        var node = CreateBaseNode<VariableStatement>(SyntaxKind.VariableStatement);
        node.Modifiers = AsNodeArray(modifiers);

        // TS accepts either a declaration list or a plain array of declarations.
        node.DeclarationList = declarationList is IList<VariableDeclaration> declarations
            ? CreateVariableDeclarationList(declarations, null)
            : declarationList as VariableDeclarationList;

        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers)
            | PropagateChildFlags(node.DeclarationList);

        if ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }

        node.JsDoc = null;    // initialized by parser (JsDocContainer)
        node.FlowNode = null; // initialized by binder (FlowContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3893 — <c>updateVariableStatement</c>.</summary>
    public VariableStatement UpdateVariableStatement(
        VariableStatement? node,
        List<Node>? modifiers,
        VariableDeclarationList? declarationList)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers) || node!.DeclarationList != declarationList
            ? Update(CreateVariableStatement(modifiers, declarationList), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4649 — <c>createImportEqualsDeclaration</c>.</summary>
    public ImportEqualsDeclaration CreateImportEqualsDeclaration(
        List<Node>? modifiers,
        bool isTypeOnly,
        object? name,
        Node? moduleReference)
    {
        var node = CreateBaseDeclaration<ImportEqualsDeclaration>(SyntaxKind.ImportEqualsDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.IsTypeOnly = isTypeOnly;
        node.ModuleReference = moduleReference;
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers)
            | PropagateIdentifierNameFlags(node.Name)
            | PropagateChildFlags(node.ModuleReference);

        // An `import x = require(…)` is TypeScript syntax unless the reference is an external module one.
        if (moduleReference is null || moduleReference.Kind != SyntaxKind.ExternalModuleReference)
        {
            node.TransformFlags |= TransformFlags.ContainsTypeScript;
        }

        // An `import =` declaration is always parsed in an await context.
        node.TransformFlags = CoreFlags.Clear(node.TransformFlags, TransformFlags.ContainsPossibleTopLevelAwait);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4676 — <c>updateImportEqualsDeclaration</c>.</summary>
    public ImportEqualsDeclaration UpdateImportEqualsDeclaration(
        ImportEqualsDeclaration? node,
        List<Node>? modifiers,
        bool isTypeOnly,
        Identifier? name,
        Node? moduleReference)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.IsTypeOnly != isTypeOnly
            || node!.Name != name
            || node!.ModuleReference != moduleReference
            ? Update(CreateImportEqualsDeclaration(modifiers, isTypeOnly, name, moduleReference), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4691 — <c>createImportDeclaration</c>.</summary>
    public ImportDeclaration CreateImportDeclaration(
        List<Node>? modifiers,
        ImportClause? importClause,
        Expression? moduleSpecifier,
        ImportAttributes? attributes)
    {
        var node = CreateBaseNode<ImportDeclaration>(SyntaxKind.ImportDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.ImportClause = importClause;
        node.ModuleSpecifier = moduleSpecifier;

        // `attributes` and `assertClause` are the two spellings of the same slot.
        node.Attributes = attributes;
        node.AssertClause = attributes;

        node.TransformFlags |= PropagateChildFlags(node.ImportClause)
            | PropagateChildFlags(node.ModuleSpecifier);

        // An import declaration is always parsed in an await context.
        node.TransformFlags = CoreFlags.Clear(node.TransformFlags, TransformFlags.ContainsPossibleTopLevelAwait);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4708 — <c>updateImportDeclaration</c>.</summary>
    public ImportDeclaration UpdateImportDeclaration(
        ImportDeclaration? node,
        List<Node>? modifiers,
        ImportClause? importClause,
        Expression? moduleSpecifier,
        ImportAttributes? attributes)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.ImportClause != importClause
            || node!.ModuleSpecifier != moduleSpecifier
            || node!.Attributes != attributes
            ? Update(CreateImportDeclaration(modifiers, importClause, moduleSpecifier, attributes), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4727 — <c>createImportClause</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the first parameter as <c>ImportPhaseModifierSyntaxKind | boolean | undefined</c> and answers
    /// the older boolean spelling inside the body; the union of an enum and a <c>boolean</c> has no single C# type, so
    /// it arrives as <c>object?</c> and the narrowing happens here.
    /// </remarks>
    public ImportClause CreateImportClause(object? phaseModifier, Identifier? name, Node? namedBindings)
    {
        var node = CreateBaseDeclaration<ImportClause>(SyntaxKind.ImportClause);

        var phase = phaseModifier switch
        {
            bool isTypeOnly => isTypeOnly ? SyntaxKind.TypeKeyword : (SyntaxKind?)null,
            SyntaxKind kind => kind,
            _ => null,
        };

        node.IsTypeOnly = phase == SyntaxKind.TypeKeyword;
        node.PhaseModifier = phase;
        node.Name = name;
        node.NamedBindings = namedBindings;
        node.TransformFlags |= PropagateChildFlags(node.Name) | PropagateChildFlags(node.NamedBindings);
        if (phase == SyntaxKind.TypeKeyword) node.TransformFlags |= TransformFlags.ContainsTypeScript;

        // An import clause is always parsed in an await context.
        node.TransformFlags = CoreFlags.Clear(node.TransformFlags, TransformFlags.ContainsPossibleTopLevelAwait);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4746 — <c>updateImportClause</c>.</summary>
    public ImportClause UpdateImportClause(
        ImportClause? node,
        object? phaseModifier,
        Identifier? name,
        Node? namedBindings)
    {
        var phase = phaseModifier switch
        {
            bool isTypeOnly => isTypeOnly ? SyntaxKind.TypeKeyword : (SyntaxKind?)null,
            SyntaxKind kind => kind,
            _ => null,
        };

        return node!.PhaseModifier != phase
            || node!.Name != name
            || node!.NamedBindings != namedBindings
            ? Update(CreateImportClause(phase, name, namedBindings), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4946 — <c>createExportDeclaration</c>.</summary>
    public ExportDeclaration CreateExportDeclaration(
        List<Node>? modifiers,
        bool isTypeOnly,
        Node? exportClause,
        Expression? moduleSpecifier,
        ImportAttributes? attributes)
    {
        var node = CreateBaseDeclaration<ExportDeclaration>(SyntaxKind.ExportDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.IsTypeOnly = isTypeOnly;
        node.ExportClause = exportClause;
        node.ModuleSpecifier = moduleSpecifier;
        node.Attributes = attributes;
        node.AssertClause = attributes;
        node.TransformFlags |= PropagateChildrenFlags(node.Modifiers)
            | PropagateChildFlags(node.ExportClause)
            | PropagateChildFlags(node.ModuleSpecifier);

        // An export declaration is always parsed in an await context.
        node.TransformFlags = CoreFlags.Clear(node.TransformFlags, TransformFlags.ContainsPossibleTopLevelAwait);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4970 — <c>updateExportDeclaration</c>.</summary>
    public ExportDeclaration UpdateExportDeclaration(
        ExportDeclaration? node,
        List<Node>? modifiers,
        bool isTypeOnly,
        Node? exportClause,
        Expression? moduleSpecifier,
        ImportAttributes? attributes)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.IsTypeOnly != isTypeOnly
            || node!.ExportClause != exportClause
            || node!.ModuleSpecifier != moduleSpecifier
            || node!.Attributes != attributes
            ? FinishUpdateExportDeclaration(
                CreateExportDeclaration(modifiers, isTypeOnly, exportClause, moduleSpecifier, attributes),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4987 — <c>finishUpdateExportDeclaration</c>.</summary>
    private ExportDeclaration FinishUpdateExportDeclaration(ExportDeclaration updated, ExportDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the children TS keeps only for error reporting.
            if (ReferenceEquals(updated.Modifiers, original.Modifiers))
            {
                updated.Modifiers = original.Modifiers;
            }
        }

        return Update(updated, original);
    }

    //
    // Type nodes (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:2280 — <c>createKeywordTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS constrains its type parameter to <c>KeywordTypeSyntaxKind</c> and answers
    /// <c>KeywordTypeNode&lt;TKind&gt;</c>. A C# type parameter cannot be constrained to an enum, so the kind arrives as
    /// the enum itself (§16) and the answer is the type interface — which the token class implements (see
    /// <see cref="KeywordTypeToken"/>).
    /// </remarks>
    public ITypeNode CreateKeywordTypeNode(SyntaxKind kind) => (ITypeNode)CreateToken(kind);

    /// <summary>ported from: factory/nodeFactory.ts:2546 — <c>createUnionOrIntersectionTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds one <c>UnionTypeNode | IntersectionTypeNode</c> object and lets the kind say which it is;
    /// the port has a class per kind, so the kind picks it. The parenthesizer the two public entry points pass is the
    /// only thing that differs between them.
    /// </remarks>
    private Node CreateUnionOrIntersectionTypeNode(
        SyntaxKind kind,
        List<ITypeNode>? types,
        Func<List<ITypeNode>?, NodeArray<ITypeNode>> parenthesize)
    {
        if (kind == SyntaxKind.UnionType)
        {
            var union = CreateBaseNode<UnionTypeNode>(kind);
            union.Types = CreateNodeArray(parenthesize(types), null);
            union.TransformFlags = TransformFlags.ContainsTypeScript;
            return union;
        }

        var intersection = CreateBaseNode<IntersectionTypeNode>(kind);
        intersection.Types = CreateNodeArray(parenthesize(types), null);
        intersection.TransformFlags = TransformFlags.ContainsTypeScript;
        return intersection;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2559 — <c>createUnionTypeNode</c>.</summary>
    public UnionTypeNode CreateUnionTypeNode(List<ITypeNode>? types)
        => (UnionTypeNode)CreateUnionOrIntersectionTypeNode(
            SyntaxKind.UnionType,
            types,
            Parenthesizer.ParenthesizeConstituentTypesOfUnionType);

    /// <summary>ported from: factory/nodeFactory.ts:2569 — <c>createIntersectionTypeNode</c>.</summary>
    public IntersectionTypeNode CreateIntersectionTypeNode(List<ITypeNode>? types)
        => (IntersectionTypeNode)CreateUnionOrIntersectionTypeNode(
            SyntaxKind.IntersectionType,
            types,
            Parenthesizer.ParenthesizeConstituentTypesOfIntersectionType);

    /// <summary>ported from: factory/nodeFactory.ts:2550 — <c>updateUnionOrIntersectionTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS is generic over <c>T extends UnionOrIntersectionTypeNode</c> so one body can serve both kinds. The
    /// two classes are unrelated in C# (they share only <c>Node</c>), so the port spells the two cases out — the same
    /// treatment a TS union gets anywhere else (§12.5).
    /// </remarks>
    private UnionTypeNode UpdateUnionOrIntersectionTypeNode(
        UnionTypeNode node,
        NodeArray<ITypeNode> types,
        Func<List<ITypeNode>?, NodeArray<ITypeNode>> parenthesize)
    {
        return !ReferenceEquals(node.Types, types)
            ? Update((UnionTypeNode)CreateUnionOrIntersectionTypeNode(node.Kind, types, parenthesize), node)
            : node;
    }

    /// <inheritdoc cref="UpdateUnionOrIntersectionTypeNode(UnionTypeNode, NodeArray{ITypeNode}, Func{IList{ITypeNode}?, NodeArray{ITypeNode}})"/>
    private IntersectionTypeNode UpdateUnionOrIntersectionTypeNode(
        IntersectionTypeNode node,
        NodeArray<ITypeNode> types,
        Func<List<ITypeNode>?, NodeArray<ITypeNode>> parenthesize)
    {
        return !ReferenceEquals(node.Types, types)
            ? Update((IntersectionTypeNode)CreateUnionOrIntersectionTypeNode(node.Kind, types, parenthesize), node)
            : node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2564 — <c>updateUnionTypeNode</c>.</summary>
    public UnionTypeNode UpdateUnionTypeNode(UnionTypeNode? node, NodeArray<ITypeNode>? types)
        => UpdateUnionOrIntersectionTypeNode(node!, types!, Parenthesizer.ParenthesizeConstituentTypesOfUnionType);

    /// <summary>ported from: factory/nodeFactory.ts:2574 — <c>updateIntersectionTypeNode</c>.</summary>
    public IntersectionTypeNode UpdateIntersectionTypeNode(IntersectionTypeNode? node, NodeArray<ITypeNode>? types)
        => UpdateUnionOrIntersectionTypeNode(node!, types!, Parenthesizer.ParenthesizeConstituentTypesOfIntersectionType);

    /// <summary>ported from: factory/nodeFactory.ts:2478 — <c>createTupleTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns <c>NodeArray&lt;TypeNode&gt;</c> into <c>NodeArray&lt;TypeNode | NamedTupleMember&gt;</c> by
    /// array covariance. C# generics are invariant, so the elements are re-listed as the wider element type — the
    /// element objects are the same ones.
    /// </remarks>
    public TupleTypeNode CreateTupleTypeNode(List<Node>? elements)
    {
        var node = CreateBaseNode<TupleTypeNode>(SyntaxKind.TupleType);
        node.Elements = CreateNodeArray(
            Parenthesizer.ParenthesizeElementTypesOfTupleType(elements) is { } parenthesized
                ? parenthesized.Cast<Node>().ToList()
                : null,
            null);
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2485 — <c>updateTupleTypeNode</c>.</summary>
    public TupleTypeNode UpdateTupleTypeNode(TupleTypeNode? node, List<Node>? elements)
    {
        return !ReferenceEquals(node!.Elements, elements)
            ? Update(CreateTupleTypeNode(elements), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2341 — <c>updateFunctionTypeNode</c>.</summary>
    public FunctionTypeNode UpdateFunctionTypeNode(
        FunctionTypeNode? node,
        NodeArray<TypeParameterDeclaration>? typeParameters,
        NodeArray<ParameterDeclaration>? parameters,
        ITypeNode? type)
    {
        return !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            ? FinishUpdateFunctionTypeNode(CreateFunctionTypeNode(typeParameters, parameters, type), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2353 — <c>finishUpdateFunctionTypeNode</c>.</summary>
    private FunctionTypeNode FinishUpdateFunctionTypeNode(FunctionTypeNode updated, FunctionTypeNode original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the child TS keeps only for error reporting.
            updated.Modifiers = original.Modifiers;
        }

        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:2363 — <c>createConstructorTypeNode1</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS exports a <c>createConstructorTypeNode</c> that dispatches on its argument count to this and
    /// <c>createConstructorTypeNode2</c>. In C# the same call site is served by two overloads of that one name.
    /// </remarks>
    public ConstructorTypeNode CreateConstructorTypeNode(
        List<ModifierToken>? modifiers,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type)
    {
        var node = CreateBaseDeclaration<ConstructorTypeNode>(SyntaxKind.ConstructorType);
        node.Modifiers = AsNodeArray(modifiers);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = AsNodeArray(parameters);
        node.Type = type;
        node.TransformFlags = TransformFlags.ContainsTypeScript;

        node.JsDoc = null;         // initialized by parser (JsDocContainer)
        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.TypeArguments = null; // used in quick info
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2388 — <c>createConstructorTypeNode2</c>.</summary>
    public ConstructorTypeNode CreateConstructorTypeNode(
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type)
        => CreateConstructorTypeNode(null, typeParameters, parameters, type);

    /// <summary>ported from: factory/nodeFactory.ts:2399 — <c>updateConstructorTypeNode1</c>.</summary>
    public ConstructorTypeNode UpdateConstructorTypeNode(
        ConstructorTypeNode? node,
        List<ModifierToken>? modifiers,
        NodeArray<TypeParameterDeclaration>? typeParameters,
        NodeArray<ParameterDeclaration>? parameters,
        ITypeNode? type)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            ? Update(CreateConstructorTypeNode(modifiers, typeParameters, parameters, type), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2430 — <c>updateConstructorTypeNode2</c>.</summary>
    public ConstructorTypeNode UpdateConstructorTypeNode(
        ConstructorTypeNode? node,
        NodeArray<TypeParameterDeclaration>? typeParameters,
        NodeArray<ParameterDeclaration>? parameters,
        ITypeNode? type)
        => UpdateConstructorTypeNode(node, node!.Modifiers, typeParameters, parameters, type);

    /// <summary>ported from: factory/nodeFactory.ts:2635 — <c>createImportTypeNode</c>.</summary>
    public ImportTypeNode CreateImportTypeNode(
        ITypeNode? argument,
        ImportAttributes? attributes,
        Node? qualifier,
        List<ITypeNode>? typeArguments,
        object? isTypeOf = null)
    {
        var node = CreateBaseNode<ImportTypeNode>(SyntaxKind.ImportType);
        node.Argument = argument;
        node.Attributes = attributes;

        // An `import(…)` type can carry its attributes through the deprecated `assertions` container as well.
        if (node.Assertions is not null && node.Assertions.AssertClause is not null && node.Attributes is not null)
        {
            node.Assertions.AssertClause = node.Attributes;
        }

        node.Qualifier = qualifier;
        node.TypeArguments = typeArguments is not null
            ? Parenthesizer.ParenthesizeTypeArguments(typeArguments)
            : null;
        node.IsTypeOf = isTypeOf is bool typeOf && typeOf;
        node.TransformFlags = TransformFlags.ContainsTypeScript;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2656 — <c>updateImportTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS defaults <c>isTypeOf</c> to the node's own value — a default that depends on another parameter. C#
    /// has no such thing, so the parameter is required and the one caller that relies on the default spells it.
    /// </remarks>
    public ImportTypeNode UpdateImportTypeNode(
        ImportTypeNode? node,
        ITypeNode? argument,
        ImportAttributes? attributes,
        Node? qualifier,
        List<ITypeNode>? typeArguments,
        bool isTypeOf)
    {
        return node!.Argument != argument
            || node!.Attributes != attributes
            || node!.Qualifier != qualifier
            || !ReferenceEquals(node!.TypeArguments, typeArguments)
            || node!.IsTypeOf != isTypeOf
            ? Update(CreateImportTypeNode(argument, attributes, qualifier, typeArguments, isTypeOf), node)
            : node!;
    }

    //
    // Expressions (the second hand-written batch)
    //

    /// <summary>ported from: factory/nodeFactory.ts:3088 — <c>createNewExpression</c>.</summary>
    public NewExpression CreateNewExpression(
        Expression? expression,
        List<ITypeNode>? typeArguments,
        List<Expression>? argumentsArray)
    {
        var node = CreateBaseDeclaration<NewExpression>(SyntaxKind.NewExpression);
        node.Expression = Parenthesizer.ParenthesizeExpressionOfNew(expression);
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Arguments = argumentsArray is not null
            ? Parenthesizer.ParenthesizeExpressionsOfCommaDelimitedList(argumentsArray)
            : null;
        node.TransformFlags |= PropagateChildFlags(node.Expression)
            | PropagateChildrenFlags(node.TypeArguments)
            | PropagateChildrenFlags(node.Arguments)
            | TransformFlags.ContainsES2020;

        if (node.TypeArguments is not null) node.TransformFlags |= TransformFlags.ContainsTypeScript;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3104 — <c>updateNewExpression</c>.</summary>
    public NewExpression UpdateNewExpression(
        NewExpression? node,
        Expression? expression,
        List<ITypeNode>? typeArguments,
        List<Expression>? argumentsArray)
    {
        return node!.Expression != expression
            || !ReferenceEquals(node!.TypeArguments, typeArguments)
            || !ReferenceEquals(node!.Arguments, argumentsArray)
            ? Update(CreateNewExpression(expression, typeArguments, argumentsArray), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3113 — <c>createTaggedTemplateExpression</c>.</summary>
    public TaggedTemplateExpression CreateTaggedTemplateExpression(
        Expression? tag,
        List<ITypeNode>? typeArguments,
        MemberExpression? template)
    {
        var node = CreateBaseNode<TaggedTemplateExpression>(SyntaxKind.TaggedTemplateExpression);
        node.Tag = Parenthesizer.ParenthesizeLeftSideOfAccess(tag, false);
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Template = template;
        node.TransformFlags |= PropagateChildFlags(node.Tag)
            | PropagateChildrenFlags(node.TypeArguments)
            | PropagateChildFlags(node.Template)
            | TransformFlags.ContainsES2015;

        if (node.TypeArguments is not null) node.TransformFlags |= TransformFlags.ContainsTypeScript;
        if (Utilities.HasInvalidEscape(node.Template)) node.TransformFlags |= TransformFlags.ContainsES2018;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3132 — <c>updateTaggedTemplateExpression</c>.</summary>
    public TaggedTemplateExpression UpdateTaggedTemplateExpression(
        TaggedTemplateExpression? node,
        Expression? tag,
        List<ITypeNode>? typeArguments,
        MemberExpression? template)
    {
        return node!.Tag != tag
            || !ReferenceEquals(node!.TypeArguments, typeArguments)
            || node!.Template != template
            ? Update(CreateTaggedTemplateExpression(tag, typeArguments, template), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3368 — <c>createPrefixUnaryExpression</c>.</summary>
    public PrefixUnaryExpression CreatePrefixUnaryExpression(SyntaxKind @operator, Expression? operand)
    {
        var node = CreateBaseNode<PrefixUnaryExpression>(SyntaxKind.PrefixUnaryExpression);
        node.Operator = @operator;
        node.Operand = Parenthesizer.ParenthesizeOperandOfPrefixUnary(operand);
        node.TransformFlags |= PropagateChildFlags(node.Operand);

        // Only a non-generated, non-"local" name is the subject of an update expression — see the comment in
        // `visitPreOrPostfixUnaryExpression` in module.ts.
        if ((@operator == SyntaxKind.PlusPlusToken || @operator == SyntaxKind.MinusMinusToken)
            && NodeTests.IsIdentifier(node.Operand!)
            && !Utilities.IsGeneratedIdentifier(node.Operand!)
            && !Utilities.IsLocalName(node.Operand!))
        {
            node.TransformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3387 — <c>updatePrefixUnaryExpression</c>.</summary>
    public PrefixUnaryExpression UpdatePrefixUnaryExpression(PrefixUnaryExpression? node, Expression? operand)
    {
        return node!.Operand != operand
            ? Update(CreatePrefixUnaryExpression(node!.Operator, operand), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3394 — <c>createPostfixUnaryExpression</c>.</summary>
    public PostfixUnaryExpression CreatePostfixUnaryExpression(Expression? operand, SyntaxKind @operator)
    {
        var node = CreateBaseNode<PostfixUnaryExpression>(SyntaxKind.PostfixUnaryExpression);
        node.Operator = @operator;
        node.Operand = Parenthesizer.ParenthesizeOperandOfPostfixUnary(operand);
        node.TransformFlags |= PropagateChildFlags(node.Operand);

        if (NodeTests.IsIdentifier(node.Operand!)
            && !Utilities.IsGeneratedIdentifier(node.Operand!)
            && !Utilities.IsLocalName(node.Operand!))
        {
            node.TransformFlags |= TransformFlags.ContainsUpdateExpressionForIdentifier;
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3412 — <c>updatePostfixUnaryExpression</c>.</summary>
    public PostfixUnaryExpression UpdatePostfixUnaryExpression(PostfixUnaryExpression? node, Expression? operand)
    {
        return node!.Operand != operand
            ? Update(CreatePostfixUnaryExpression(operand, node!.Operator), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3473 — <c>createConditionalExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes `questionToken ?? createToken(SyntaxKind.QuestionToken)`, which resolves to
    /// `PunctuationToken&lt;SyntaxKind.QuestionToken&gt;`. The port's wide `CreateToken` answers `Node` (§16), so the token
    /// class is named directly — a punctuation token carries no transform flags of its own, which is why that is the
    /// same object either way.
    /// </remarks>
    public ConditionalExpression CreateConditionalExpression(
        Expression? condition,
        PunctuationToken? questionToken,
        Expression? whenTrue,
        PunctuationToken? colonToken,
        Expression? whenFalse)
    {
        var node = CreateBaseNode<ConditionalExpression>(SyntaxKind.ConditionalExpression);
        node.Condition = Parenthesizer.ParenthesizeConditionOfConditionalExpression(condition);
        node.QuestionToken = questionToken ?? new PunctuationToken(SyntaxKind.QuestionToken);
        node.WhenTrue = Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenTrue);
        node.ColonToken = colonToken ?? new PunctuationToken(SyntaxKind.ColonToken);
        node.WhenFalse = Parenthesizer.ParenthesizeBranchOfConditionalExpression(whenFalse);
        node.TransformFlags |= PropagateChildFlags(node.Condition)
            | PropagateChildFlags(node.QuestionToken)
            | PropagateChildFlags(node.WhenTrue)
            | PropagateChildFlags(node.ColonToken)
            | PropagateChildFlags(node.WhenFalse);
        node.FlowNodeWhenFalse = null;
        node.FlowNodeWhenTrue = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3491 — <c>updateConditionalExpression</c>.</summary>
    public ConditionalExpression UpdateConditionalExpression(
        ConditionalExpression? node,
        Expression? condition,
        Token? questionToken,
        Expression? whenTrue,
        Token? colonToken,
        Expression? whenFalse)
    {
        return node!.Condition != condition
            || node!.QuestionToken != questionToken
            || node!.WhenTrue != whenTrue
            || node!.ColonToken != colonToken
            || node!.WhenFalse != whenFalse
            ? Update(
                CreateConditionalExpression(
                    condition,
                    questionToken as PunctuationToken,
                    whenTrue,
                    colonToken as PunctuationToken,
                    whenFalse),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4251 — <c>createVariableDeclaration</c>.</summary>
    public VariableDeclaration CreateVariableDeclaration(
        object? name,
        PunctuationToken? exclamationToken,
        ITypeNode? type,
        Expression? initializer)
    {
        var node = CreateBaseDeclaration<VariableDeclaration>(SyntaxKind.VariableDeclaration);
        node.Name = AsName(name);
        node.ExclamationToken = exclamationToken;
        node.Type = type;
        node.Initializer = AsInitializer(initializer);
        node.TransformFlags |= PropagateNameFlags(node.Name)
            | PropagateChildFlags(node.Initializer)
            | (node.ExclamationToken is not null || node.Type is not null
                ? TransformFlags.ContainsTypeScript
                : TransformFlags.None);

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4266 — <c>updateVariableDeclaration</c>.</summary>
    public VariableDeclaration UpdateVariableDeclaration(
        VariableDeclaration? node,
        Node? name,
        PunctuationToken? exclamationToken,
        ITypeNode? type,
        Expression? initializer)
    {
        return node!.Name != name
            || node!.Type != type
            || node!.ExclamationToken != exclamationToken
            || node!.Initializer != initializer
            ? Update(CreateVariableDeclaration(name, exclamationToken, type, initializer), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6218 — <c>createSyntheticExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the first parameter as <c>Type</c> and the third as
    /// <c>ParameterDeclaration | NamedTupleMember</c>; the first is the checker's type object, which the port models
    /// separately, and the third collapses to <c>Node</c> (§12.5).
    /// </remarks>
    public SyntheticExpression CreateSyntheticExpression(
        IType? type,
        object? isSpread = null,
        Node? tupleNameSource = null)
    {
        var node = CreateBaseNode<SyntheticExpression>(SyntaxKind.SyntheticExpression);
        node.Type = type;
        node.IsSpread = isSpread is bool spread && spread;
        node.TupleNameSource = tupleNameSource;
        return node;
    }

    //
    // Class element declarations (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:1981 — <c>createConstructorDeclaration</c>.</summary>
    public ConstructorDeclaration CreateConstructorDeclaration(
        List<Node>? modifiers,
        List<ParameterDeclaration>? parameters,
        Block? body)
    {
        var node = CreateBaseDeclaration<ConstructorDeclaration>(SyntaxKind.Constructor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Parameters = CreateNodeArray(parameters, null);
        node.Body = body;

        if (node.Body is null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
                | PropagateChildrenFlags(node.Parameters)
                | (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait)
                | TransformFlags.ContainsES2015;
        }

        node.TypeParameters = null; // initialized by parser for grammar errors
        node.Type = null;           // initialized by parser for grammar errors
        node.TypeArguments = null;  // used in quick info
        node.JsDoc = null;          // initialized by parser (JsDocContainer)
        node.Locals = null;         // initialized by binder (LocalsContainer)
        node.NextContainer = null;  // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2013 — <c>updateConstructorDeclaration</c>.</summary>
    public ConstructorDeclaration UpdateConstructorDeclaration(
        ConstructorDeclaration? node,
        List<Node>? modifiers,
        List<ParameterDeclaration>? parameters,
        Block? body)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Body != body
            ? FinishUpdateConstructorDeclaration(CreateConstructorDeclaration(modifiers, parameters, body), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2024 — <c>finishUpdateConstructorDeclaration</c>.</summary>
    private ConstructorDeclaration FinishUpdateConstructorDeclaration(
        ConstructorDeclaration updated,
        ConstructorDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the children TS keeps only for error reporting.
            updated.TypeParameters = original.TypeParameters;
            updated.Type = original.Type;
        }

        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:2035 — <c>createGetAccessorDeclaration</c>.</summary>
    public GetAccessorDeclaration CreateGetAccessorDeclaration(
        List<Node>? modifiers,
        object? name,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        var node = CreateBaseDeclaration<GetAccessorDeclaration>(SyntaxKind.GetAccessor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.Parameters = CreateNodeArray(parameters, null);
        node.Type = type;
        node.Body = body;

        if (node.Body is null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
                | PropagateNameFlags(node.Name)
                | PropagateChildrenFlags(node.Parameters)
                | PropagateChildFlags(node.Type)
                | (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait)
                | (node.Type is not null ? TransformFlags.ContainsTypeScript : TransformFlags.None);
        }

        node.TypeArguments = null;  // used in quick info
        node.TypeParameters = null; // initialized by parser for grammar errors
        node.JsDoc = null;          // initialized by parser (JsDocContainer)
        node.Locals = null;         // initialized by binder (LocalsContainer)
        node.NextContainer = null;  // initialized by binder (LocalsContainer)
        node.FlowNode = null;       // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2073 — <c>updateGetAccessorDeclaration</c>.</summary>
    public GetAccessorDeclaration UpdateGetAccessorDeclaration(
        GetAccessorDeclaration? node,
        List<Node>? modifiers,
        Node? name,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.Name != name
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            || node!.Body != body
            ? FinishUpdateGetAccessorDeclaration(
                CreateGetAccessorDeclaration(modifiers, name, parameters, type, body),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2087 — <c>finishUpdateGetAccessorDeclaration</c>.</summary>
    private GetAccessorDeclaration FinishUpdateGetAccessorDeclaration(
        GetAccessorDeclaration updated,
        GetAccessorDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the child TS keeps only for error reporting.
            updated.TypeParameters = original.TypeParameters;
        }

        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:2099 — <c>createSetAccessorDeclaration</c>.</summary>
    public SetAccessorDeclaration CreateSetAccessorDeclaration(
        List<Node>? modifiers,
        object? name,
        List<ParameterDeclaration>? parameters,
        Block? body)
    {
        var node = CreateBaseDeclaration<SetAccessorDeclaration>(SyntaxKind.SetAccessor);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.Parameters = CreateNodeArray(parameters, null);
        node.Body = body;

        if (node.Body is null)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
                | PropagateNameFlags(node.Name)
                | PropagateChildrenFlags(node.Parameters)
                | (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait)
                | (node.Type is not null ? TransformFlags.ContainsTypeScript : TransformFlags.None);
        }

        node.TypeArguments = null;  // used in quick info
        node.TypeParameters = null; // initialized by parser for grammar errors
        node.Type = null;           // initialized by parser for grammar errors
        node.JsDoc = null;          // initialized by parser (JsDocContainer)
        node.Locals = null;         // initialized by binder (LocalsContainer)
        node.NextContainer = null;  // initialized by binder (LocalsContainer)
        node.FlowNode = null;       // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2135 — <c>updateSetAccessorDeclaration</c>.</summary>
    public SetAccessorDeclaration UpdateSetAccessorDeclaration(
        SetAccessorDeclaration? node,
        List<Node>? modifiers,
        Node? name,
        List<ParameterDeclaration>? parameters,
        Block? body)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.Name != name
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Body != body
            ? FinishUpdateSetAccessorDeclaration(
                CreateSetAccessorDeclaration(modifiers, name, parameters, body),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2149 — <c>finishUpdateSetAccessorDeclaration</c>.</summary>
    private SetAccessorDeclaration FinishUpdateSetAccessorDeclaration(
        SetAccessorDeclaration updated,
        SetAccessorDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the children TS keeps only for error reporting.
            updated.TypeParameters = original.TypeParameters;
            updated.Type = original.Type;
        }

        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:2179 — <c>updateCallSignature</c>.</summary>
    public CallSignatureDeclaration UpdateCallSignature(
        CallSignatureDeclaration? node,
        NodeArray<TypeParameterDeclaration>? typeParameters,
        NodeArray<ParameterDeclaration>? parameters,
        ITypeNode? type)
    {
        return !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            ? FinishUpdateBaseSignatureDeclaration(
                CreateCallSignature(typeParameters, parameters, type),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2212 — <c>updateConstructSignature</c>.</summary>
    public ConstructSignatureDeclaration UpdateConstructSignature(
        ConstructSignatureDeclaration? node,
        NodeArray<TypeParameterDeclaration>? typeParameters,
        NodeArray<ParameterDeclaration>? parameters,
        ITypeNode? type)
    {
        return !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            ? FinishUpdateBaseSignatureDeclaration(
                CreateConstructSignature(typeParameters, parameters, type),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:2245 — <c>updateIndexSignature</c>.</summary>
    public IndexSignatureDeclaration UpdateIndexSignature(
        IndexSignatureDeclaration? node,
        List<Node>? modifiers,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type)
    {
        return !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            || !ReferenceEquals(node!.Modifiers, modifiers)
            ? FinishUpdateBaseSignatureDeclaration(
                CreateIndexSignature(modifiers, parameters, type),
                node)
            : node!;
    }

    //
    // Functions and classes (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:3177 — <c>createFunctionExpression</c>.</summary>
    public FunctionExpression CreateFunctionExpression(
        List<ModifierToken>? modifiers,
        PunctuationToken? asteriskToken,
        object? name,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        var node = CreateBaseDeclaration<FunctionExpression>(SyntaxKind.FunctionExpression);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters, null);
        node.Type = type;
        node.Body = body;

        var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;
        var isGenerator = node.AsteriskToken is not null;
        var isAsyncGenerator = isAsync && isGenerator;

        node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
            | PropagateChildFlags(node.AsteriskToken)
            | PropagateNameFlags(node.Name)
            | PropagateChildrenFlags(node.TypeParameters)
            | PropagateChildrenFlags(node.Parameters)
            | PropagateChildFlags(node.Type)
            | (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait)
            | (isAsyncGenerator ? TransformFlags.ContainsES2018
                : isAsync ? TransformFlags.ContainsES2017
                : isGenerator ? TransformFlags.ContainsGenerator
                : TransformFlags.None)
            | (node.TypeParameters is not null || node.Type is not null
                ? TransformFlags.ContainsTypeScript
                : TransformFlags.None)
            | TransformFlags.ContainsHoistedDeclarationOrCompletion;

        node.TypeArguments = null; // used in quick info
        node.JsDoc = null;         // initialized by parser (JsDocContainer)
        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null;      // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3224 — <c>updateFunctionExpression</c>.</summary>
    public FunctionExpression UpdateFunctionExpression(
        FunctionExpression? node,
        List<ModifierToken>? modifiers,
        PunctuationToken? asteriskToken,
        Identifier? name,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        return node!.Name != name
            || !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.AsteriskToken != asteriskToken
            || !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            || node!.Body != body
            ? FinishUpdateBaseSignatureDeclaration(
                CreateFunctionExpression(modifiers, asteriskToken, name, typeParameters, parameters, type, body),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3246 — <c>createArrowFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes `equalsGreaterThanToken ?? createToken(SyntaxKind.EqualsGreaterThanToken)`, which resolves to
    /// a `PunctuationToken`; the port's wide `CreateToken` answers `Node` (§16), so the class is named directly — a
    /// punctuation token carries no transform flags of its own.
    /// </remarks>
    public ArrowFunction CreateArrowFunction(
        List<ModifierToken>? modifiers,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        PunctuationToken? equalsGreaterThanToken,
        Node? body)
    {
        var node = CreateBaseDeclaration<ArrowFunction>(SyntaxKind.ArrowFunction);
        node.Modifiers = AsNodeArray(modifiers);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters, null);
        node.Type = type;
        node.EqualsGreaterThanToken = equalsGreaterThanToken ?? new PunctuationToken(SyntaxKind.EqualsGreaterThanToken);
        node.Body = Parenthesizer.ParenthesizeConciseBodyOfArrowFunction(body);

        var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;

        node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
            | PropagateChildrenFlags(node.TypeParameters)
            | PropagateChildrenFlags(node.Parameters)
            | PropagateChildFlags(node.Type)
            | PropagateChildFlags(node.EqualsGreaterThanToken)
            | (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait)
            | (node.TypeParameters is not null || node.Type is not null
                ? TransformFlags.ContainsTypeScript
                : TransformFlags.None)
            | (isAsync ? TransformFlags.ContainsES2017 | TransformFlags.ContainsLexicalThis : TransformFlags.None)
            | TransformFlags.ContainsES2015;

        node.TypeArguments = null; // used in quick info
        node.JsDoc = null;         // initialized by parser (JsDocContainer)
        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.FlowNode = null;      // initialized by binder (FlowContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3285 — <c>updateArrowFunction</c>.</summary>
    public ArrowFunction UpdateArrowFunction(
        ArrowFunction? node,
        List<ModifierToken>? modifiers,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        PunctuationToken? equalsGreaterThanToken,
        Node? body)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            || node!.EqualsGreaterThanToken != equalsGreaterThanToken
            || node!.Body != body
            ? FinishUpdateBaseSignatureDeclaration(
                CreateArrowFunction(modifiers, typeParameters, parameters, type, equalsGreaterThanToken, body),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4300 — <c>createFunctionDeclaration</c>.</summary>
    public FunctionDeclaration CreateFunctionDeclaration(
        List<Node>? modifiers,
        PunctuationToken? asteriskToken,
        object? name,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        var node = CreateBaseDeclaration<FunctionDeclaration>(SyntaxKind.FunctionDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.AsteriskToken = asteriskToken;
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.Parameters = CreateNodeArray(parameters, null);
        node.Type = type;
        node.Body = body;

        if (node.Body is null || (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            var isAsync = (Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Async) != 0;
            var isGenerator = node.AsteriskToken is not null;
            var isAsyncGenerator = isAsync && isGenerator;

            node.TransformFlags = PropagateChildrenFlags(node.Modifiers)
                | PropagateChildFlags(node.AsteriskToken)
                | PropagateNameFlags(node.Name)
                | PropagateChildrenFlags(node.TypeParameters)
                | PropagateChildrenFlags(node.Parameters)
                | PropagateChildFlags(node.Type)
                | (PropagateChildFlags(node.Body) & ~TransformFlags.ContainsPossibleTopLevelAwait)
                | (isAsyncGenerator ? TransformFlags.ContainsES2018
                    : isAsync ? TransformFlags.ContainsES2017
                    : isGenerator ? TransformFlags.ContainsGenerator
                    : TransformFlags.None)
                | (node.TypeParameters is not null || node.Type is not null
                    ? TransformFlags.ContainsTypeScript
                    : TransformFlags.None)
                | TransformFlags.ContainsHoistedDeclarationOrCompletion;
        }

        node.TypeArguments = null; // used in quick info
        node.JsDoc = null;         // initialized by parser (JsDocContainer)
        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;
        node.ReturnFlowNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4351 — <c>updateFunctionDeclaration</c>.</summary>
    public FunctionDeclaration UpdateFunctionDeclaration(
        FunctionDeclaration? node,
        List<Node>? modifiers,
        PunctuationToken? asteriskToken,
        Identifier? name,
        List<TypeParameterDeclaration>? typeParameters,
        List<ParameterDeclaration>? parameters,
        ITypeNode? type,
        Block? body)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.AsteriskToken != asteriskToken
            || node!.Name != name
            || !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.Parameters, parameters)
            || node!.Type != type
            || node!.Body != body
            ? FinishUpdateFunctionDeclaration(
                CreateFunctionDeclaration(modifiers, asteriskToken, name, typeParameters, parameters, type, body),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4373 — <c>finishUpdateFunctionDeclaration</c>.</summary>
    private FunctionDeclaration FinishUpdateFunctionDeclaration(
        FunctionDeclaration updated,
        FunctionDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the children TS keeps only for error reporting.
            if (ReferenceEquals(updated.Modifiers, original.Modifiers))
            {
                updated.Modifiers = original.Modifiers;
            }
        }

        return FinishUpdateBaseSignatureDeclaration(updated, original);
    }

    //
    // Classes (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:4383 — <c>createClassDeclaration</c>.</summary>
    public ClassDeclaration CreateClassDeclaration(
        List<Node>? modifiers,
        object? name,
        List<TypeParameterDeclaration>? typeParameters,
        List<HeritageClause>? heritageClauses,
        List<IClassElement>? members)
    {
        var node = CreateBaseDeclaration<ClassDeclaration>(SyntaxKind.ClassDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Name = AsName(name);
        node.TypeParameters = AsNodeArray(typeParameters);
        node.HeritageClauses = AsNodeArray(heritageClauses);
        node.Members = CreateNodeArray(members, null);

        if ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags |= PropagateChildrenFlags(node.Modifiers)
                | PropagateNameFlags(node.Name)
                | PropagateChildrenFlags(node.TypeParameters)
                | PropagateChildrenFlags(node.HeritageClauses)
                | PropagateChildrenFlags(node.Members)
                | (node.TypeParameters is not null ? TransformFlags.ContainsTypeScript : TransformFlags.None)
                | TransformFlags.ContainsES2015;

            // TypeScript-only class syntax anywhere in the class makes the whole declaration TypeScript.
            if ((node.TransformFlags & TransformFlags.ContainsTypeScriptClassSyntax) != 0)
            {
                node.TransformFlags |= TransformFlags.ContainsTypeScript;
            }
        }

        node.JsDoc = null; // initialized by parser (JsDocContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4419 — <c>updateClassDeclaration</c>.</summary>
    public ClassDeclaration UpdateClassDeclaration(
        ClassDeclaration? node,
        List<Node>? modifiers,
        Identifier? name,
        List<TypeParameterDeclaration>? typeParameters,
        List<HeritageClause>? heritageClauses,
        List<IClassElement>? members)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers)
            || node!.Name != name
            || !ReferenceEquals(node!.TypeParameters, typeParameters)
            || !ReferenceEquals(node!.HeritageClauses, heritageClauses)
            || !ReferenceEquals(node!.Members, members)
            ? Update(CreateClassDeclaration(modifiers, name, typeParameters, heritageClauses, members), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5870 — <c>createHeritageClause</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the first parameter as <c>HeritageClause["token"]</c> — an indexed access whose two members
    /// are the switch arms — and it maps to the enum itself.
    /// </remarks>
    public HeritageClause CreateHeritageClause(SyntaxKind token, List<ExpressionWithTypeArguments>? types)
    {
        var node = CreateBaseNode<HeritageClause>(SyntaxKind.HeritageClause);
        node.Token = token;
        node.Types = CreateNodeArray(types, null);
        node.TransformFlags |= PropagateChildrenFlags(node.Types);

        switch (token)
        {
            case SyntaxKind.ExtendsKeyword:
                node.TransformFlags |= TransformFlags.ContainsES2015;
                break;
            case SyntaxKind.ImplementsKeyword:
                node.TransformFlags |= TransformFlags.ContainsTypeScript;
                break;
            default:
                Debug.AssertNever(token);
                break;
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5888 — <c>updateHeritageClause</c>.</summary>
    public HeritageClause UpdateHeritageClause(HeritageClause? node, List<ExpressionWithTypeArguments>? types)
    {
        return !ReferenceEquals(node!.Types, types)
            ? Update(CreateHeritageClause(node!.Token, types), node)
            : node!;
    }

    //
    // Modules (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:4544 — <c>createModuleDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS leaves <c>flags</c> unannotated — its type is <c>any</c> — and defaults it to <c>NodeFlags.None</c>.
    /// The port takes it as <c>object?</c> and narrows to the enum, an absent value meaning <c>None</c>.
    /// </remarks>
    public ModuleDeclaration CreateModuleDeclaration(
        List<Node>? modifiers,
        MemberExpression? name,
        Node? body,
        object? flags = null)
    {
        var nodeFlags = flags is NodeFlags value ? value : NodeFlags.None;
        var node = CreateBaseDeclaration<ModuleDeclaration>(SyntaxKind.ModuleDeclaration);
        node.Modifiers = AsNodeArray(modifiers);
        node.Flags |= nodeFlags & (NodeFlags.Namespace | NodeFlags.NestedNamespace | NodeFlags.GlobalAugmentation);
        node.Name = name;
        node.Body = body;

        if ((Utilities.ModifiersToFlags(node.Modifiers) & ModifierFlags.Ambient) != 0)
        {
            node.TransformFlags = TransformFlags.ContainsTypeScript;
        }
        else
        {
            node.TransformFlags |= PropagateChildrenFlags(node.Modifiers)
                | PropagateChildFlags(node.Name)
                | PropagateChildFlags(node.Body)
                | TransformFlags.ContainsTypeScript;
        }

        // A module declaration cannot contain `await`.
        node.TransformFlags = CoreFlags.Clear(node.TransformFlags, TransformFlags.ContainsPossibleTopLevelAwait);

        node.JsDoc = null;         // initialized by parser (JsDocContainer)
        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4578 — <c>updateModuleDeclaration</c>.</summary>
    public ModuleDeclaration UpdateModuleDeclaration(
        ModuleDeclaration? node,
        List<Node>? modifiers,
        MemberExpression? name,
        Node? body)
    {
        return !ReferenceEquals(node!.Modifiers, modifiers) || node!.Name != name || node!.Body != body
            ? Update(CreateModuleDeclaration(modifiers, name, body, node!.Flags), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4634 — <c>updateNamespaceExportDeclaration</c>.</summary>
    public NamespaceExportDeclaration UpdateNamespaceExportDeclaration(
        NamespaceExportDeclaration? node,
        Identifier? name)
    {
        return node!.Name != name
            ? FinishUpdateNamespaceExportDeclaration(CreateNamespaceExportDeclaration(name), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:4640 — <c>finishUpdateNamespaceExportDeclaration</c>.</summary>
    private NamespaceExportDeclaration FinishUpdateNamespaceExportDeclaration(
        NamespaceExportDeclaration updated,
        NamespaceExportDeclaration original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the child TS keeps only for error reporting.
            updated.Modifiers = original.Modifiers;
        }

        return Update(updated, original);
    }

    //
    // Top-level nodes (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:6041 — <c>createSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the body is one long list of initial values — TS clears every field the parser or the binder fills in.
    /// It reads differently in C# only because the fields are properties here; the set is the same.
    /// </remarks>
    public SourceFile CreateSourceFile(List<IStatement>? statements, Token? endOfFileToken, NodeFlags flags)
    {
        var node = CreateBaseNode<SourceFile>(SyntaxKind.SourceFile);
        node.Statements = CreateNodeArray(statements, null);
        node.EndOfFileToken = endOfFileToken;
        node.Flags |= flags;
        node.Text = "";
        node.FileName = "";
        node.Path = "";
        node.ResolvedPath = "";
        node.OriginalFileName = "";
        node.LanguageVersion = ScriptTarget.LatestStandard;
        node.LanguageVariant = LanguageVariant.Standard;
        node.ScriptKind = ScriptKind.Unknown;
        node.IsDeclarationFile = false;
        node.HasNoDefaultLib = false;

        node.TransformFlags |= PropagateChildrenFlags(node.Statements) | PropagateChildFlags(node.EndOfFileToken);

        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        node.EndFlowNode = null;

        node.NodeCount = 0;
        node.IdentifierCount = 0;
        node.SymbolCount = 0;
        node.ParseDiagnostics = null!;
        node.BindDiagnostics = null!;
        node.BindSuggestionDiagnostics = null;
        node.LineMap = null!;
        node.ExternalModuleIndicator = null;
        node.SetExternalModuleIndicator = null;
        node.Pragmas = null!;
        node.CheckJsDirective = null;
        node.ReferencedFiles = null!;
        node.TypeReferenceDirectives = null!;
        node.LibReferenceDirectives = null!;
        node.AmdDependencies = null!;
        node.CommentDirectives = null;
        node.Identifiers = null!;
        node.PackageJsonLocations = null;
        node.PackageJsonScope = null;
        node.Imports = null!;
        node.ModuleAugmentations = null!;
        node.AmbientModuleNames = null!;
        node.ClassifiableNames = null;
        node.ImpliedNodeFormat = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6095 — <c>createRedirectedSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds an object whose prototype *is* the redirect target, so every property but <c>id</c> and
    /// <c>symbol</c> reads through to it, and those two forward both ways. C# cannot forward per-object property access,
    /// so the port hands back the target with the redirect info attached — a caller then reads the target's own members,
    /// which is what the forwarding would have produced. The difference is visible only where something asks a source
    /// file that is *not* redirected for its <c>RedirectInfo</c>.
    /// </remarks>
    public SourceFile CreateRedirectedSourceFile(IRedirectInfo? redirectInfo)
    {
        var target = redirectInfo!.RedirectTarget!;
        target.RedirectInfo = redirectInfo;
        return target;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6118 — <c>cloneRedirectedSourceFile</c>.</summary>
    private SourceFile CloneRedirectedSourceFile(SourceFile source)
    {
        var node = CreateRedirectedSourceFile(source.RedirectInfo);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;
        node.FileName = source.FileName;
        node.Path = source.Path;
        node.ResolvedPath = source.ResolvedPath;
        node.OriginalFileName = source.OriginalFileName;
        node.PackageJsonLocations = source.PackageJsonLocations;
        node.PackageJsonScope = source.PackageJsonScope;
        node.EmitNode = null;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6131 — <c>cloneSourceFileWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS copies every own enumerable property of <c>source</c> onto a fresh file, skipping the ones the fresh
    /// node already declares and dropping <c>emitNode</c>. A C# object always has all of its declared properties, so the
    /// equivalent is to copy every writable public property — which is the explicit-assignment rewrite the TODO in the TS
    /// source asks for anyway, done by reflection rather than by hand.
    /// </remarks>
    private static SourceFile CloneSourceFileWorker(SourceFile source)
    {
        var node = CreateBaseNode<SourceFile>(SyntaxKind.SourceFile);
        node.Flags |= source.Flags & ~NodeFlags.Synthesized;

        foreach (var property in typeof(SourceFile).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.Name == nameof(Node.EmitNode)) continue;

            property.SetValue(node, property.GetValue(source));
        }

        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6143 — <c>cloneSourceFile</c>.</summary>
    private SourceFile CloneSourceFile(SourceFile source)
    {
        var node = source.RedirectInfo is not null
            ? CloneRedirectedSourceFile(source)
            : CloneSourceFileWorker(source);
        SetOriginal(node, source);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6156 — <c>cloneSourceFileWithChanges</c>.</summary>
    private SourceFile CloneSourceFileWithChanges(
        SourceFile source,
        List<IStatement>? statements,
        bool isDeclarationFile,
        List<IFileReference>? referencedFiles,
        List<IFileReference>? typeReferences,
        bool hasNoDefaultLib,
        List<IFileReference>? libReferences)
    {
        var node = CloneSourceFile(source);
        node.Statements = CreateNodeArray(statements, null);
        node.IsDeclarationFile = isDeclarationFile;
        node.ReferencedFiles = referencedFiles;
        node.TypeReferenceDirectives = typeReferences;
        node.HasNoDefaultLib = hasNoDefaultLib;
        node.LibReferenceDirectives = libReferences;
        node.TransformFlags = PropagateChildrenFlags(node.Statements) | PropagateChildFlags(node.EndOfFileToken);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6173 — <c>updateSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS leaves every parameter after <c>statements</c> unannotated and defaults it to the corresponding
    /// member of <c>node</c> — a default that reads another parameter, which C# has no way to express. The caller passes
    /// what it means, and <c>null</c> means "keep the node's value".
    /// </remarks>
    public SourceFile UpdateSourceFile(
        SourceFile? node,
        List<IStatement>? statements,
        object? isDeclarationFile,
        object? referencedFiles,
        object? typeReferenceDirectives,
        object? _hasNoDefaultLib,
        object? libReferenceDirectives)
    {
        var declarationFile = isDeclarationFile is bool declared ? declared : node!.IsDeclarationFile;
        var referenced = referencedFiles as List<IFileReference> ?? node!.ReferencedFiles;
        var typeReferences = typeReferenceDirectives as List<IFileReference> ?? node!.TypeReferenceDirectives;
        var libReferences = libReferenceDirectives as List<IFileReference> ?? node!.LibReferenceDirectives;
        var hasNoDefaultLib = _hasNoDefaultLib is bool noDefaultLib && noDefaultLib;

        return !ReferenceEquals(node!.Statements, statements)
            || node!.IsDeclarationFile != declarationFile
            || !ReferenceEquals(node!.ReferencedFiles, referenced)
            || !ReferenceEquals(node!.TypeReferenceDirectives, typeReferences)
            || !ReferenceEquals(node!.LibReferenceDirectives, libReferences)
            ? Update(
                CloneSourceFileWithChanges(
                    node,
                    statements,
                    declarationFile,
                    referenced,
                    typeReferences,
                    hasNoDefaultLib,
                    libReferences),
                node)
            : node!;
    }

    //
    // Emit helpers (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:6411 — <c>createImmediatelyInvokedFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads — with and without a parameter — and one body. The C# signature takes the
    /// optional parameter and value directly, which serves both call shapes.
    /// </remarks>
    public CallExpression CreateImmediatelyInvokedFunctionExpression(
        List<IStatement>? statements,
        ParameterDeclaration? param = null,
        Expression? paramValue = null)
        => CreateCallExpression(
            CreateFunctionExpression(
                null,           // modifiers
                null,           // asteriskToken
                null,           // name
                null,           // typeParameters
                param is not null ? [param] : [],
                null,           // type
                CreateBlock(statements, true)),
            null,               // typeArguments
            paramValue is not null ? [paramValue] : []);

    /// <summary>ported from: factory/nodeFactory.ts:6429 — <c>createImmediatelyInvokedArrowFunction</c>.</summary>
    public CallExpression CreateImmediatelyInvokedArrowFunction(
        List<IStatement>? statements,
        ParameterDeclaration? param = null,
        Expression? paramValue = null)
        => CreateCallExpression(
            CreateArrowFunction(
                null,           // modifiers
                null,           // typeParameters
                param is not null ? [param] : [],
                null,           // type
                null,           // equalsGreaterThanToken
                CreateBlock(statements, true)),
            null,               // typeArguments
            paramValue is not null ? [paramValue] : []);

    /// <summary>ported from: factory/nodeFactory.ts:6484 — <c>createMethodCall</c>.</summary>
    public CallExpression CreateMethodCall(Expression? @object, object? methodName, List<Expression>? argumentsList)
    {
        // Preserve the optionality of `object`.
        if (Utilities.IsCallChain(@object!))
        {
            return CreateCallChain(
                CreatePropertyAccessChain(@object, null, methodName),
                null, // questionDotToken
                null, // typeArguments
                argumentsList);
        }

        return CreateCallExpression(
            CreatePropertyAccessExpression(@object, methodName),
            null, // typeArguments
            argumentsList);
    }

    /// <summary>ported from: factory/nodeFactory.ts:6501 — <c>createFunctionBindCall</c>.</summary>
    public CallExpression CreateFunctionBindCall(Expression? target, Expression? thisArg, List<Expression>? argumentsList)
    {
        var arguments = new List<Expression> { thisArg! };
        if (argumentsList is not null) arguments.AddRange(argumentsList);
        return CreateMethodCall(target, "bind", arguments);
    }

    /// <summary>ported from: factory/nodeFactory.ts:6505 — <c>createFunctionCallCall</c>.</summary>
    public CallExpression CreateFunctionCallCall(Expression? target, Expression? thisArg, List<Expression>? argumentsList)
    {
        var arguments = new List<Expression> { thisArg! };
        if (argumentsList is not null) arguments.AddRange(argumentsList);
        return CreateMethodCall(target, "call", arguments);
    }

    /// <summary>ported from: factory/nodeFactory.ts:6509 — <c>createFunctionApplyCall</c>.</summary>
    public CallExpression CreateFunctionApplyCall(
        Expression? target,
        Expression? thisArg,
        Expression? argumentsExpression)
        => CreateMethodCall(target, "apply", [thisArg!, argumentsExpression!]);

    /// <summary>ported from: factory/nodeFactory.ts:6513 — <c>createGlobalMethodCall</c>.</summary>
    public CallExpression CreateGlobalMethodCall(
        string? globalObjectName,
        string? methodName,
        List<Expression>? argumentsList)
        => CreateMethodCall(CreateIdentifier(globalObjectName, null, null), methodName, argumentsList);

    /// <summary>ported from: factory/nodeFactory.ts:6517 — <c>createArraySliceCall</c>.</summary>
    public CallExpression CreateArraySliceCall(Expression? array, object? start)
        => CreateMethodCall(array, "slice", start is null ? [] : [AsExpression(start)!]);

    /// <summary>ported from: factory/nodeFactory.ts:6521 — <c>createArrayConcatCall</c>.</summary>
    public CallExpression CreateArrayConcatCall(Expression? array, List<Expression>? argumentsList)
        => CreateMethodCall(array, "concat", argumentsList);

    /// <summary>ported from: factory/nodeFactory.ts:6525 — <c>createObjectDefinePropertyCall</c>.</summary>
    public CallExpression CreateObjectDefinePropertyCall(
        Expression? target,
        object? propertyName,
        Expression? attributes)
        => CreateGlobalMethodCall("Object", "defineProperty", [target!, AsExpression(propertyName)!, attributes!]);

    /// <summary>ported from: factory/nodeFactory.ts:6529 — <c>createObjectGetOwnPropertyDescriptorCall</c>.</summary>
    public CallExpression CreateObjectGetOwnPropertyDescriptorCall(Expression? target, object? propertyName)
        => CreateGlobalMethodCall("Object", "getOwnPropertyDescriptor", [target!, AsExpression(propertyName)!]);

    /// <summary>ported from: factory/nodeFactory.ts:6533 — <c>createReflectGetCall</c>.</summary>
    public CallExpression CreateReflectGetCall(
        Expression? target,
        Expression? propertyKey,
        Expression? receiver)
        => CreateGlobalMethodCall(
            "Reflect",
            "get",
            receiver is not null ? [target!, propertyKey!, receiver] : [target!, propertyKey!]);

    /// <summary>ported from: factory/nodeFactory.ts:6537 — <c>createReflectSetCall</c>.</summary>
    public CallExpression CreateReflectSetCall(
        Expression? target,
        Expression? propertyKey,
        Expression? value,
        Expression? receiver)
        => CreateGlobalMethodCall(
            "Reflect",
            "set",
            receiver is not null
                ? [target!, propertyKey!, value!, receiver]
                : [target!, propertyKey!, value!]);

    /// <summary>ported from: factory/nodeFactory.ts:6543 — <c>tryAddPropertyAssignment</c>.</summary>
    private bool TryAddPropertyAssignment(
        List<PropertyAssignment> properties,
        string propertyName,
        Expression? expression)
    {
        if (expression is null) return false;

        properties.Add(CreatePropertyAssignment(propertyName, expression));
        return true;
    }

    /// <summary>ported from: factory/nodeFactory.ts:6549 — <c>createPropertyDescriptor</c>.</summary>
    public ObjectLiteralExpression CreatePropertyDescriptor(
        IPropertyDescriptorAttributes? attributes,
        bool? singleLine)
    {
        var properties = new List<PropertyAssignment>();
        TryAddPropertyAssignment(properties, "enumerable", AsExpression(attributes!.Enumerable));
        TryAddPropertyAssignment(properties, "configurable", AsExpression(attributes.Configurable));

        var isData = TryAddPropertyAssignment(properties, "writable", AsExpression(attributes.Writable));
        isData = TryAddPropertyAssignment(properties, "value", attributes.Value) || isData;

        var isAccessor = TryAddPropertyAssignment(properties, "get", attributes.Get);
        isAccessor = TryAddPropertyAssignment(properties, "set", attributes.Set) || isAccessor;

        Debug.Assert(
            !(isData && isAccessor),
            "A PropertyDescriptor may not be both an accessor descriptor and a data descriptor.");

        return CreateObjectLiteralExpression(properties.Cast<Node>().ToList(), singleLine != true);
    }

    //
    // JSX and property assignments (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:5624 — <c>createJsxSelfClosingElement</c>.</summary>
    public JsxSelfClosingElement CreateJsxSelfClosingElement(
        Node? tagName,
        List<ITypeNode>? typeArguments,
        JsxAttributes? attributes)
    {
        var node = CreateBaseNode<JsxSelfClosingElement>(SyntaxKind.JsxSelfClosingElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName)
            | PropagateChildrenFlags(node.TypeArguments)
            | PropagateChildFlags(node.Attributes)
            | TransformFlags.ContainsJsx;

        if (node.TypeArguments is not null) node.TransformFlags |= TransformFlags.ContainsTypeScript;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5641 — <c>updateJsxSelfClosingElement</c>.</summary>
    public JsxSelfClosingElement UpdateJsxSelfClosingElement(
        JsxSelfClosingElement? node,
        Node? tagName,
        List<ITypeNode>? typeArguments,
        JsxAttributes? attributes)
    {
        return node!.TagName != tagName
            || !ReferenceEquals(node!.TypeArguments, typeArguments)
            || node!.Attributes != attributes
            ? Update(CreateJsxSelfClosingElement(tagName, typeArguments, attributes), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5649 — <c>createJsxOpeningElement</c>.</summary>
    public JsxOpeningElement CreateJsxOpeningElement(
        Node? tagName,
        List<ITypeNode>? typeArguments,
        JsxAttributes? attributes)
    {
        var node = CreateBaseNode<JsxOpeningElement>(SyntaxKind.JsxOpeningElement);
        node.TagName = tagName;
        node.TypeArguments = AsNodeArray(typeArguments);
        node.Attributes = attributes;
        node.TransformFlags |= PropagateChildFlags(node.TagName)
            | PropagateChildrenFlags(node.TypeArguments)
            | PropagateChildFlags(node.Attributes)
            | TransformFlags.ContainsJsx;

        if (typeArguments is not null) node.TransformFlags |= TransformFlags.ContainsTypeScript;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5666 — <c>updateJsxOpeningElement</c>.</summary>
    public JsxOpeningElement UpdateJsxOpeningElement(
        JsxOpeningElement? node,
        Node? tagName,
        List<ITypeNode>? typeArguments,
        JsxAttributes? attributes)
    {
        return node!.TagName != tagName
            || !ReferenceEquals(node!.TypeArguments, typeArguments)
            || node!.Attributes != attributes
            ? Update(CreateJsxOpeningElement(tagName, typeArguments, attributes), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:3780 — <c>updateJsxExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>JsxExpression.dotDotDotToken</c> is typed <c>Token</c> while the create side takes a
    /// <c>PunctuationToken</c>. The cast is safe because the factory builds a punctuation token for that kind (see
    /// <see cref="CreateTokenNode"/>), and a <c>null</c> token stays <c>null</c>.
    /// </remarks>
    public JsxExpression UpdateJsxExpression(JsxExpression? node, Expression? expression)
    {
        return node!.Expression != expression
            ? Update(CreateJsxExpression(node.DotDotDotToken as PunctuationToken, expression), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5938 — <c>updatePropertyAssignment</c>.</summary>
    public PropertyAssignment UpdatePropertyAssignment(
        PropertyAssignment? node,
        Node? name,
        Expression? initializer)
    {
        return node!.Name != name || node!.Initializer != initializer
            ? FinishUpdatePropertyAssignment(CreatePropertyAssignment(name, initializer), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5948 — <c>finishUpdatePropertyAssignment</c>.</summary>
    private PropertyAssignment FinishUpdatePropertyAssignment(
        PropertyAssignment updated,
        PropertyAssignment original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the children TS keeps only for error reporting.
            updated.Modifiers = original.Modifiers;
            updated.QuestionToken = original.QuestionToken;
            updated.ExclamationToken = original.ExclamationToken;
        }

        return Update(updated, original);
    }

    /// <summary>ported from: factory/nodeFactory.ts:5974 — <c>updateShorthandPropertyAssignment</c>.</summary>
    public ShorthandPropertyAssignment UpdateShorthandPropertyAssignment(
        ShorthandPropertyAssignment? node,
        Identifier? name,
        Expression? objectAssignmentInitializer)
    {
        return node!.Name != name || node!.ObjectAssignmentInitializer != objectAssignmentInitializer
            ? FinishUpdateShorthandPropertyAssignment(
                CreateShorthandPropertyAssignment(name, objectAssignmentInitializer),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5984 — <c>finishUpdateShorthandPropertyAssignment</c>.</summary>
    private ShorthandPropertyAssignment FinishUpdateShorthandPropertyAssignment(
        ShorthandPropertyAssignment updated,
        ShorthandPropertyAssignment original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // Copy the children TS keeps only for error reporting.
            updated.Modifiers = original.Modifiers;
            updated.QuestionToken = original.QuestionToken;
            updated.ExclamationToken = original.ExclamationToken;
            updated.EqualsToken = original.EqualsToken;
        }

        return Update(updated, original);
    }

    //
    // JSDoc (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:7188 — <c>getDefaultTagNameForKind</c>.</summary>
    private static string GetDefaultTagNameForKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.JSDocTypeTag => "type",
        SyntaxKind.JSDocReturnTag => "returns",
        SyntaxKind.JSDocThisTag => "this",
        SyntaxKind.JSDocEnumTag => "enum",
        SyntaxKind.JSDocAuthorTag => "author",
        SyntaxKind.JSDocClassTag => "class",
        SyntaxKind.JSDocPublicTag => "public",
        SyntaxKind.JSDocPrivateTag => "private",
        SyntaxKind.JSDocProtectedTag => "protected",
        SyntaxKind.JSDocReadonlyTag => "readonly",
        SyntaxKind.JSDocOverrideTag => "override",
        SyntaxKind.JSDocTemplateTag => "template",
        SyntaxKind.JSDocTypedefTag => "typedef",
        SyntaxKind.JSDocParameterTag => "param",
        SyntaxKind.JSDocPropertyTag => "prop",
        SyntaxKind.JSDocCallbackTag => "callback",
        SyntaxKind.JSDocOverloadTag => "overload",
        SyntaxKind.JSDocAugmentsTag => "augments",
        SyntaxKind.JSDocImplementsTag => "implements",
        SyntaxKind.JSDocImportTag => "import",
        _ => Debug.Fail<string>($"Unsupported kind: {kind}"),
    };

    /// <summary>ported from: factory/nodeFactory.ts:5192 — <c>getDefaultTagName</c>.</summary>
    private Identifier GetDefaultTagName(JSDocTag node)
    {
        var defaultTagName = GetDefaultTagNameForKind(node.Kind);

        return node.TagName is { } tagName && tagName.EscapedText == Utilities.EscapeLeadingUnderscores(defaultTagName)
            ? tagName
            : CreateIdentifier(defaultTagName, null, null);
    }

    /// <summary>ported from: src/compiler/utilities.ts — <c>getJSDocTypeAliasName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>fullName.body.name</c> when the full name is a namespace declaration; the port reads the
    /// <c>Body</c> member, whose type is already the identifier TS is testing for.
    /// </remarks>
    private static Identifier? GetJSDocTypeAliasName(Node? fullName)
        => fullName is Identifier identifier ? identifier
            : fullName is ModuleDeclaration { Body: Identifier body } ? body
            : null;

    /// <summary>Sets the <c>type</c> member the three unary JSDoc type nodes share.</summary>
    /// <remarks>
    /// PORT-NOTE: TS constrains its type parameter to <c>JSDocType &amp; { readonly type: TypeNode | undefined }</c>, which
    /// is a structural constraint C# cannot write — the three classes have no common interface to hang <c>Type</c> on. The
    /// switch is what replaces it.
    /// </remarks>
    private static void SetJSDocType(Node node, ITypeNode? type)
    {
        switch (node)
        {
            case JSDocOptionalType optional: optional.Type = type; break;
            case JSDocVariadicType variadic: variadic.Type = type; break;
            case JSDocNamepathType namepath: namepath.Type = type; break;
        }
    }

    /// <summary>Sets the <c>type</c> and <c>postfix</c> members the two nullable JSDoc type nodes share.</summary>
    private static void SetJSDocTypeAndPostfix(Node node, ITypeNode? type, bool postfix)
    {
        switch (node)
        {
            case JSDocNullableType nullable:
                nullable.Type = type;
                nullable.Postfix = postfix;
                break;
            case JSDocNonNullableType nonNullable:
                nonNullable.Type = type;
                nonNullable.Postfix = postfix;
                break;
        }
    }

    /// <summary>ported from: factory/nodeFactory.ts:5198 — <c>createBaseJSDocTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS constrains <c>T</c> to <c>JSDocTag</c> and lets the caller name the class. C# needs the constructor
    /// constraint for that, which is the only difference.
    /// </remarks>
    private T CreateBaseJSDocTag<T>(SyntaxKind kind, Identifier? tagName, object? comment)
        where T : JSDocTag, new()
    {
        var node = CreateBaseNode<T>(kind);
        node.TagName = tagName;
        node.Comment = comment;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5206 — <c>createBaseJSDocTagDeclaration</c>.</summary>
    private T CreateBaseJSDocTagDeclaration<T>(SyntaxKind kind, Identifier? tagName, object? comment)
        where T : JSDocTag, IDeclaration, new()
    {
        var node = CreateBaseDeclaration<T>(kind);
        node.TagName = tagName;
        node.Comment = comment;
        return node;
    }

    /// <summary>Sets the <c>typeExpression</c> member the type-like JSDoc tags share.</summary>
    /// <remarks>
    /// PORT-NOTE: the structural constraint TS writes (<c>JSDocTag &amp; { typeExpression?: … }</c>) has no C# spelling —
    /// <c>JSDocTag</c> is a class here and carries no such member. The switch is what replaces it; the tags whose member is
    /// a <c>JSDocSignature</c> rather than a <c>JSDocTypeExpression</c> (overload, typedef, callback) are not in it.
    /// </remarks>
    private static void SetJSDocTypeExpression(Node node, JSDocTypeExpression? typeExpression)
    {
        switch (node)
        {
            case JSDocTypeTag type: type.TypeExpression = typeExpression; break;
            case JSDocReturnTag returns: returns.TypeExpression = typeExpression; break;
            case JSDocThisTag thisTag: thisTag.TypeExpression = typeExpression; break;
            case JSDocSatisfiesTag satisfies: satisfies.TypeExpression = typeExpression; break;
        }
    }

    /// <summary>ported from: factory/nodeFactory.ts:5462 — <c>createJSDocSimpleTagWorker</c>.</summary>
    public T CreateJSDocSimpleTagWorker<T>(SyntaxKind kind, Identifier? tagName, object? comment)
        where T : JSDocTag, new()
        => CreateBaseJSDocTag<T>(
            kind,
            tagName ?? CreateIdentifier(GetDefaultTagNameForKind(kind), null, null),
            comment);

    /// <summary>ported from: factory/nodeFactory.ts:5476 — <c>updateJSDocSimpleTagWorker</c>.</summary>
    public T UpdateJSDocSimpleTagWorker<T>(SyntaxKind kind, T? node, Identifier? tagName, object? comment)
        where T : JSDocTag, new()
    {
        // TS defaults `tagName` to the node's default tag name, which is what a caller that passes nothing means.
        var name = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != name || node!.Comment != comment
            ? Update(CreateJSDocSimpleTagWorker<T>(kind, name, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5492 — <c>createJSDocTypeLikeTagWorker</c>.</summary>
    public T CreateJSDocTypeLikeTagWorker<T>(
        SyntaxKind kind,
        Identifier? tagName,
        JSDocTypeExpression? typeExpression,
        object? comment)
        where T : JSDocTag, new()
    {
        var node = CreateBaseJSDocTag<T>(
            kind,
            tagName ?? CreateIdentifier(GetDefaultTagNameForKind(kind), null, null),
            comment);
        SetJSDocTypeExpression(node, typeExpression);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5506 — <c>updateJSDocTypeLikeTagWorker</c>.</summary>
    public T UpdateJSDocTypeLikeTagWorker<T>(
        SyntaxKind kind,
        T? node,
        Identifier? tagName,
        JSDocTypeExpression? typeExpression,
        object? comment)
        where T : JSDocTag, new()
    {
        var name = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != name || node!.Comment != comment
            ? Update(CreateJSDocTypeLikeTagWorker<T>(kind, name, typeExpression, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5513 — <c>createJSDocUnknownTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the kind is <c>SyntaxKind.JSDocTag</c> rather than <c>JSDocUnknownTag</c> — that is what TS passes, and
    /// what the class pins.
    /// </remarks>
    public JSDocUnknownTag CreateJSDocUnknownTag(Identifier? tagName, object? comment = null)
        => CreateBaseJSDocTag<JSDocUnknownTag>(SyntaxKind.JSDocTag, tagName, comment);

    /// <summary>ported from: factory/nodeFactory.ts:5521 — <c>updateJSDocUnknownTag</c>.</summary>
    public JSDocUnknownTag UpdateJSDocUnknownTag(JSDocUnknownTag? node, Identifier? tagName, object? comment)
    {
        return node!.TagName != tagName || node!.Comment != comment
            ? Update(CreateJSDocUnknownTag(tagName, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5529 — <c>createJSDocEnumTag</c>.</summary>
    public JSDocEnumTag CreateJSDocEnumTag(
        Identifier? tagName,
        JSDocTypeExpression? typeExpression,
        object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocEnumTag>(
            SyntaxKind.JSDocEnumTag,
            tagName ?? CreateIdentifier(GetDefaultTagNameForKind(SyntaxKind.JSDocEnumTag), null, null),
            comment);
        node.TypeExpression = typeExpression;

        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5539 — <c>updateJSDocEnumTag</c>.</summary>
    public JSDocEnumTag UpdateJSDocEnumTag(
        JSDocEnumTag? node,
        Identifier? tagName,
        JSDocTypeExpression? typeExpression,
        object? comment)
    {
        var name = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != name || node!.TypeExpression != typeExpression || node!.Comment != comment
            ? Update(CreateJSDocEnumTag(name, typeExpression, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5548 — <c>createJSDocImportTag</c>.</summary>
    public JSDocImportTag CreateJSDocImportTag(
        Identifier? tagName,
        ImportClause? importClause,
        Expression? moduleSpecifier,
        ImportAttributes? attributes,
        object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocImportTag>(
            SyntaxKind.JSDocImportTag,
            tagName ?? CreateIdentifier("import", null, null),
            comment);
        node.ImportClause = importClause;
        node.ModuleSpecifier = moduleSpecifier;
        node.Attributes = attributes;
        node.Comment = comment;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5557 — <c>updateJSDocImportTag</c>.</summary>
    public JSDocImportTag UpdateJSDocImportTag(
        JSDocImportTag? node,
        Identifier? tagName,
        ImportClause? importClause,
        Expression? moduleSpecifier,
        ImportAttributes? attributes,
        object? comment)
    {
        return node!.TagName != tagName
            || node!.Comment != comment
            || node!.ImportClause != importClause
            || node!.ModuleSpecifier != moduleSpecifier
            || node!.Attributes != attributes
            ? Update(CreateJSDocImportTag(tagName, importClause, moduleSpecifier, attributes, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5216 — <c>createJSDocTemplateTag</c>.</summary>
    public JSDocTemplateTag CreateJSDocTemplateTag(
        Identifier? tagName,
        JSDocTypeExpression? constraint,
        List<TypeParameterDeclaration>? typeParameters,
        object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocTemplateTag>(
            SyntaxKind.JSDocTemplateTag,
            tagName ?? CreateIdentifier("template", null, null),
            comment);
        node.Constraint = constraint;
        node.TypeParameters = CreateNodeArray(typeParameters, null);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5225 — <c>updateJSDocTemplateTag</c>.</summary>
    public JSDocTemplateTag UpdateJSDocTemplateTag(
        JSDocTemplateTag? node,
        Identifier? tagName,
        JSDocTypeExpression? constraint,
        List<TypeParameterDeclaration>? typeParameters,
        object? comment)
    {
        var name = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != name
            || node!.Constraint != constraint
            || !ReferenceEquals(node!.TypeParameters, typeParameters)
            || node!.Comment != comment
            ? Update(CreateJSDocTemplateTag(name, constraint, typeParameters, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5237 — <c>createJSDocTypedefTag</c>.</summary>
    public JSDocTypedefTag CreateJSDocTypedefTag(
        Identifier? tagName,
        JSDocTypeExpression? typeExpression,
        Node? fullName,
        object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocTypedefTag>(
            SyntaxKind.JSDocTypedefTag,
            tagName ?? CreateIdentifier("typedef", null, null),
            comment);
        node.TypeExpression = typeExpression;
        node.FullName = fullName;
        node.Name = GetJSDocTypeAliasName(fullName);

        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5253 — <c>updateJSDocTypedefTag</c>.</summary>
    public JSDocTypedefTag UpdateJSDocTypedefTag(
        JSDocTypedefTag? node,
        Identifier? tagName,
        JSDocTypeExpression? typeExpression,
        Node? fullName,
        object? comment)
    {
        var name = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != name
            || node!.TypeExpression != typeExpression
            || node!.FullName != fullName
            || node!.Comment != comment
            ? Update(CreateJSDocTypedefTag(name, typeExpression, fullName, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5268 — <c>createJSDocParameterTag</c>.</summary>
    public JSDocParameterTag CreateJSDocParameterTag(
        Identifier? tagName,
        Node? name,
        bool isBracketed,
        JSDocTypeExpression? typeExpression = null,
        bool? isNameFirst = null,
        object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocParameterTag>(
            SyntaxKind.JSDocParameterTag,
            tagName ?? CreateIdentifier("param", null, null),
            comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst == true;
        node.IsBracketed = isBracketed;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5283 — <c>updateJSDocParameterTag</c>.</summary>
    public JSDocParameterTag UpdateJSDocParameterTag(
        JSDocParameterTag? node,
        Identifier? tagName,
        Node? name,
        bool isBracketed,
        JSDocTypeExpression? typeExpression,
        bool isNameFirst,
        object? comment)
    {
        var defaultName = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != defaultName
            || node!.Name != name
            || node!.IsBracketed != isBracketed
            || node!.TypeExpression != typeExpression
            || node!.IsNameFirst != isNameFirst
            || node!.Comment != comment
            ? Update(
                CreateJSDocParameterTag(defaultName, name, isBracketed, typeExpression, isNameFirst, comment),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5296 — <c>createJSDocPropertyTag</c>.</summary>
    public JSDocPropertyTag CreateJSDocPropertyTag(
        Identifier? tagName,
        Node? name,
        bool isBracketed,
        JSDocTypeExpression? typeExpression = null,
        bool? isNameFirst = null,
        object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocPropertyTag>(
            SyntaxKind.JSDocPropertyTag,
            tagName ?? CreateIdentifier("prop", null, null),
            comment);
        node.TypeExpression = typeExpression;
        node.Name = name;
        node.IsNameFirst = isNameFirst == true;
        node.IsBracketed = isBracketed;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5311 — <c>updateJSDocPropertyTag</c>.</summary>
    public JSDocPropertyTag UpdateJSDocPropertyTag(
        JSDocPropertyTag? node,
        Identifier? tagName,
        Node? name,
        bool isBracketed,
        JSDocTypeExpression? typeExpression,
        bool isNameFirst,
        object? comment)
    {
        var defaultName = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != defaultName
            || node!.Name != name
            || node!.IsBracketed != isBracketed
            || node!.TypeExpression != typeExpression
            || node!.IsNameFirst != isNameFirst
            || node!.Comment != comment
            ? Update(
                CreateJSDocPropertyTag(defaultName, name, isBracketed, typeExpression, isNameFirst, comment),
                node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5324 — <c>createJSDocCallbackTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the type expression of a callback tag is a <c>JSDocSignature</c>, not a <c>JSDocTypeExpression</c> — the
    /// one place in this family where the member's type differs.
    /// </remarks>
    public JSDocCallbackTag CreateJSDocCallbackTag(
        Identifier? tagName,
        JSDocSignature? typeExpression,
        Node? fullName = null,
        object? comment = null)
    {
        var node = CreateBaseJSDocTagDeclaration<JSDocCallbackTag>(
            SyntaxKind.JSDocCallbackTag,
            tagName ?? CreateIdentifier("callback", null, null),
            comment);
        node.TypeExpression = typeExpression;
        node.FullName = fullName;
        node.Name = GetJSDocTypeAliasName(fullName);

        node.Locals = null;        // initialized by binder (LocalsContainer)
        node.NextContainer = null; // initialized by binder (LocalsContainer)
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5340 — <c>updateJSDocCallbackTag</c>.</summary>
    public JSDocCallbackTag UpdateJSDocCallbackTag(
        JSDocCallbackTag? node,
        Identifier? tagName,
        JSDocSignature? typeExpression,
        Node? fullName,
        object? comment)
    {
        var defaultName = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != defaultName
            || node!.TypeExpression != typeExpression
            || node!.FullName != fullName
            || node!.Comment != comment
            ? Update(CreateJSDocCallbackTag(defaultName, typeExpression, fullName, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5351 — <c>createJSDocOverloadTag</c>.</summary>
    public JSDocOverloadTag CreateJSDocOverloadTag(
        Identifier? tagName,
        JSDocSignature? typeExpression,
        object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocOverloadTag>(
            SyntaxKind.JSDocOverloadTag,
            tagName ?? CreateIdentifier("overload", null, null),
            comment);
        node.TypeExpression = typeExpression;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5360 — <c>updateJSDocOverloadTag</c>.</summary>
    public JSDocOverloadTag UpdateJSDocOverloadTag(
        JSDocOverloadTag? node,
        Identifier? tagName,
        JSDocSignature? typeExpression,
        object? comment)
    {
        var defaultName = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != defaultName
            || node!.TypeExpression != typeExpression
            || node!.Comment != comment
            ? Update(CreateJSDocOverloadTag(defaultName, typeExpression, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5368 — <c>createJSDocAugmentsTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the class expression as an indexed access (<c>JSDocAugmentsTag["class"]</c>), which maps to the
    /// class expression node.
    /// </remarks>
    public JSDocAugmentsTag CreateJSDocAugmentsTag(
        Identifier? tagName,
        ExpressionWithTypeArguments? className,
        object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocAugmentsTag>(
            SyntaxKind.JSDocAugmentsTag,
            tagName ?? CreateIdentifier("augments", null, null),
            comment);
        node.Class = className;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5376 — <c>updateJSDocAugmentsTag</c>.</summary>
    public JSDocAugmentsTag UpdateJSDocAugmentsTag(
        JSDocAugmentsTag? node,
        Identifier? tagName,
        ExpressionWithTypeArguments? className,
        object? comment)
    {
        var defaultName = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != defaultName || node!.Class != className || node!.Comment != comment
            ? Update(CreateJSDocAugmentsTag(defaultName, className, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5384 — <c>createJSDocImplementsTag</c>.</summary>
    public JSDocImplementsTag CreateJSDocImplementsTag(
        Identifier? tagName,
        ExpressionWithTypeArguments? className,
        object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocImplementsTag>(
            SyntaxKind.JSDocImplementsTag,
            tagName ?? CreateIdentifier("implements", null, null),
            comment);
        node.Class = className;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5392 — <c>updateJSDocImplementsTag</c>.</summary>
    public JSDocImplementsTag UpdateJSDocImplementsTag(
        JSDocImplementsTag? node,
        Identifier? tagName,
        ExpressionWithTypeArguments? className,
        object? comment)
    {
        var defaultName = tagName ?? GetDefaultTagName(node!);
        return node!.TagName != defaultName || node!.Class != className || node!.Comment != comment
            ? Update(CreateJSDocImplementsTag(defaultName, className, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5400 — <c>createJSDocSeeTag</c>.</summary>
    public JSDocSeeTag CreateJSDocSeeTag(
        Identifier? tagName,
        JSDocNameReference? name,
        object? comment = null)
    {
        var node = CreateBaseJSDocTag<JSDocSeeTag>(
            SyntaxKind.JSDocSeeTag,
            tagName ?? CreateIdentifier("see", null, null),
            comment);
        node.Name = name;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5408 — <c>updateJSDocSeeTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS leaves this tag name undefaulted (the caller may pass <c>undefined</c> deliberately), so the port does
    /// the same rather than falling back to the default tag name.
    /// </remarks>
    public JSDocSeeTag UpdateJSDocSeeTag(
        JSDocSeeTag? node,
        Identifier? tagName,
        JSDocNameReference? name,
        object? comment)
    {
        return node!.TagName != tagName || node!.Name != name || node!.Comment != comment
            ? Update(CreateJSDocSeeTag(tagName, name, comment), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5071 — <c>createJSDocPrimaryTypeWorker</c>.</summary>
    public T CreateJSDocPrimaryTypeWorker<T>(SyntaxKind kind)
        where T : Node, new()
        => CreateBaseNode<T>(kind);

    /// <summary>ported from: factory/nodeFactory.ts:5094 — <c>createJSDocUnaryTypeWorker</c>.</summary>
    public T CreateJSDocUnaryTypeWorker<T>(SyntaxKind kind, ITypeNode? type)
        where T : Node, new()
    {
        var node = CreateBaseNode<T>(kind);
        SetJSDocType(node, type);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5084 — <c>updateJSDocUnaryTypeWorker</c>.</summary>
    public T UpdateJSDocUnaryTypeWorker<T>(SyntaxKind kind, T? node, ITypeNode? type)
        where T : Node, new()
    {
        return !ReferenceEquals(TypeOf(node!), type)
            ? Update(CreateJSDocUnaryTypeWorker<T>(kind, type), node)
            : node!;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5076 — <c>createJSDocPrePostfixUnaryTypeWorker</c>.</summary>
    public T CreateJSDocPrePostfixUnaryTypeWorker<T>(SyntaxKind kind, ITypeNode? type, bool? postfix = null)
        where T : Node, new()
    {
        var isPostfix = postfix ?? false;
        var node = CreateJSDocUnaryTypeWorker<T>(
            kind,
            isPostfix && type is not null
                ? Parenthesizer.ParenthesizeNonArrayTypeOfPostfixType(type)
                : type);
        SetJSDocTypeAndPostfix(node, TypeOf(node), isPostfix);
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5088 — <c>updateJSDocPrePostfixUnaryTypeWorker</c>.</summary>
    public T UpdateJSDocPrePostfixUnaryTypeWorker<T>(SyntaxKind kind, T? node, ITypeNode? type)
        where T : Node, new()
    {
        return !ReferenceEquals(TypeOf(node!), type)
            ? Update(CreateJSDocPrePostfixUnaryTypeWorker<T>(kind, type, PostfixOf(node!)), node)
            : node!;
    }

    /// <summary>The <c>type</c> member of a unary JSDoc type node.</summary>
    private static ITypeNode? TypeOf(Node node) => node switch
    {
        JSDocOptionalType optional => optional.Type,
        JSDocVariadicType variadic => variadic.Type,
        JSDocNamepathType namepath => namepath.Type,
        JSDocNullableType nullable => nullable.Type,
        JSDocNonNullableType nonNullable => nonNullable.Type,
        _ => null,
    };

    /// <summary>The <c>postfix</c> member of a nullable JSDoc type node.</summary>
    private static bool PostfixOf(Node node) => node switch
    {
        JSDocNullableType nullable => nullable.Postfix,
        JSDocNonNullableType nonNullable => nonNullable.Postfix,
        _ => false,
    };

    /// <summary>ported from: factory/nodeFactory.ts:5136 — <c>createJSDocTypeLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS leaves <c>isArrayType</c> unannotated and defaults it to <c>false</c>; the port takes it as
    /// <c>object?</c> and narrows, an absent value meaning <c>false</c>.
    /// </remarks>
    public JSDocTypeLiteral CreateJSDocTypeLiteral(List<JSDocPropertyLikeTag>? propertyTags, object? isArrayType = null)
    {
        var node = CreateBaseDeclaration<JSDocTypeLiteral>(SyntaxKind.JSDocTypeLiteral);
        node.JsDocPropertyTags = AsNodeArray(propertyTags);
        node.IsArrayType = isArrayType is bool arrayType && arrayType;
        return node;
    }

    /// <summary>ported from: factory/nodeFactory.ts:5145 — <c>updateJSDocTypeLiteral</c>.</summary>
    public JSDocTypeLiteral UpdateJSDocTypeLiteral(
        JSDocTypeLiteral? node,
        List<JSDocPropertyLikeTag>? propertyTags,
        bool isArrayType)
    {
        return !ReferenceEquals(node!.JsDocPropertyTags, propertyTags) || node!.IsArrayType != isArrayType
            ? Update(CreateJSDocTypeLiteral(propertyTags, isArrayType), node)
            : node!;
    }

    //
    // Outer expressions and emit prologues (the hand-written half)
    //

    /// <summary>ported from: factory/nodeFactory.ts:6564 — <c>updateOuterExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types the parameter as <c>OuterExpression</c>, a union of the seven kinds below, and its switch has no
    /// default — a caller that passes something else gets <c>undefined</c>. The port throws instead, because returning null
    /// from a method typed <c>Expression</c> would be a lie.
    /// </remarks>
    public Expression UpdateOuterExpression(Expression? outerExpression, Expression? expression)
        => outerExpression!.Kind switch
        {
            SyntaxKind.ParenthesizedExpression => UpdateParenthesizedExpression(
                (ParenthesizedExpression)outerExpression,
                expression),
            SyntaxKind.TypeAssertionExpression => UpdateTypeAssertion(
                (TypeAssertion)outerExpression,
                ((TypeAssertion)outerExpression).Type,
                expression),
            SyntaxKind.AsExpression => UpdateAsExpression(
                (AsExpression)outerExpression,
                expression,
                ((AsExpression)outerExpression).Type),
            SyntaxKind.SatisfiesExpression => UpdateSatisfiesExpression(
                (SatisfiesExpression)outerExpression,
                expression,
                ((SatisfiesExpression)outerExpression).Type),
            SyntaxKind.NonNullExpression => UpdateNonNullExpression((NonNullExpression)outerExpression, expression),
            SyntaxKind.ExpressionWithTypeArguments => UpdateExpressionWithTypeArguments(
                (ExpressionWithTypeArguments)outerExpression,
                expression,
                ((ExpressionWithTypeArguments)outerExpression).TypeArguments),
            SyntaxKind.PartiallyEmittedExpression => UpdatePartiallyEmittedExpression(
                (PartiallyEmittedExpression)outerExpression,
                expression),
            _ => throw new InvalidOperationException($"invalid kind: {outerExpression.Kind}"),
        };

    /// <summary>ported from: factory/nodeFactory.ts:6725 — <c>createAssignmentTargetWrapper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the explicit parentheses are part of the output, not a parsing accident — TS notes they are required
    /// because of a V8 regression (https://bugs.chromium.org/p/v8/issues/detail?id=9560), and the port keeps them.
    /// </remarks>
    public PropertyAccessExpression CreateAssignmentTargetWrapper(Identifier? paramName, Expression? expression)
        => CreatePropertyAccessExpression(
            CreateParenthesizedExpression(
                CreateObjectLiteralExpression(
                    [
                        CreateSetAccessorDeclaration(
                            null,       // modifiers
                            "value",
                            [
                                CreateParameterDeclaration(
                                    null,   // modifiers
                                    null,   // dotDotDotToken
                                    paramName,
                                    null,   // questionToken
                                    null,   // type
                                    null),  // initializer
                            ],
                            CreateBlock([CreateExpressionStatement(expression)], true)),
                    ],
                    null)),
            "value");

    /// <summary>ported from: factory/nodeFactory.ts:6880 — <c>createUseStrictPrologue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS wraps the node with `startOnNewLine`, which is `setStartsOnNewLine(node, true)` — the emit-node slot
    /// the port already has (Parser/EmitNodes.cs).
    /// </remarks>
    public ExpressionStatement CreateUseStrictPrologue()
        => EmitNodes.SetStartsOnNewLine(
            CreateExpressionStatement(CreateStringLiteral("use strict", null, null)),
            true);
}
