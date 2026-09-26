#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/jsx.ts:92-681 — the whole JSX transform.
/// <summary>
/// ported from: src/compiler/transformers/jsx.ts:92-681 — <c>transformJsx</c>: it rewrites every JSX element, fragment
/// and <c>{…}</c> child into the call the configured runtime wants — <c>React.createElement</c> (or the configured
/// <c>jsxFactory</c>) for the classic modes, the implicit <c>jsx</c>/<c>jsxs</c>/<c>jsxDEV</c> runtime imports for the
/// automatic ones — decoding JSX text entities and re-flowing its whitespace on the way.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports the single function <c>transformJsx</c>, whose body is a table of
/// closures over the factory's locals. The port states the factory as <see cref="JsxTransform.TransformJsx"/> and the
/// closures as the members of <see cref="JsxTransformer"/> — the same "closures → class" treatment
/// <c>Transformer.cs</c> records for <c>transformNodes</c>'s context literal (docs/architecture.md §5 rule 6).
/// </para>
/// <para>
/// PORT-NOTE (the registration hook): the reference's <c>getScriptTransformers</c> pushes this factory between
/// <c>transformTypeScript</c> and <c>transformESNext</c>, gated by <c>getJSXTransformEnabled(compilerOptions)</c>
/// (transformer.ts:142-144). <c>Transformer.GetScriptTransformers</c> is the §15.15 loud blocker for that whole
/// family, so this slice ships its entry point alone; the one hook its owner adds is
/// <c>if (Utilities.GetJSXTransformEnabled(compilerOptions)) transformers.Add(JsxTransform.TransformJsx);</c> right
/// after <c>transformers.Add(TypeScriptTransform.TransformTypeScript)</c>.
/// </para>
/// <para>
/// PORT-NOTE (the JSX unions): TS's <c>JsxOpeningLikeElement</c> (<c>JsxOpeningElement | JsxSelfClosingElement</c>),
/// <c>JsxChild</c> and <c>JsxAttributeValue</c> have no C# type, so the members that take one take <see cref="Node"/>
/// (or the <c>NodeArray&lt;Node&gt;</c> the child slots hold) and narrow on the kind, with the <c>attributes</c> and
/// <c>tagName</c> reads stated once as <see cref="JsxTransformer.AttributesOf"/> and
/// <see cref="JsxTransformer.TagNameOf"/> (§12.5 — the treatment
/// <c>FactoryUtilities.CreateJsxFactoryExpression</c> already records for the same union).
/// </para>
/// <para>
/// PORT-NOTE (the implicit runtime imports): the reference keys <c>utilizedImplicitRuntimeImports</c> by what
/// <c>getJSXRuntimeImport</c> answers, a <c>string | undefined</c>, which a JS map allows; a C# dictionary's key is
/// non-nullable, so <see cref="JsxTransformer.GetImplicitImportForName"/> keys by the non-null string every call site
/// proves: it is only reached from the two JSX-mode visitors, which <c>shouldUseCreateElement</c> selects only when
/// the base specifier is present, and the <c>"createElement"</c> arm passes that specifier itself.
/// </para>
/// <para>
/// PORT-NOTE (the reference's function-valued local): <c>visitJsxElement</c>, <c>visitJsxSelfClosingElement</c> and
/// <c>visitJsxFragment</c> each pick one of two members through a local <c>tagTransform</c> and call it; C# states the
/// same selection as the conditional that calls the chosen member directly.
/// </para>
/// <para>
/// PORT-NOTE (<c>isStringDoubleQuoted</c>): the reference's <c>utilities.ts:3917</c> helper is not ported yet; its
/// body is the one line <see cref="JsxTransformer.IsStringDoubleQuoted"/> states from the ported
/// <c>Utilities.GetSourceTextOfNodeFromSourceFile</c>, which is what every JSX attribute string literal is read with.
/// Its home is <c>Utilities</c>, beside the landed <c>IsSingleOrDoubleQuote</c>; this slice's report names the move.
/// </para>
/// </remarks>
internal static class JsxTransform
{
    /// <summary>ported from: src/compiler/transformers/jsx.ts:92-681 — <c>transformJsx</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
    /// <c>Func&lt;object?, object?&gt;</c> (Transformer.cs's header records why), and the reference's own
    /// <c>chainBundle(context, transformSourceFile)</c> is <see cref="TransformationUtilities.ChainBundle"/>, whose
    /// <c>Func&lt;Node, Node&gt;</c> is adapted to that face (the shape
    /// <c>Transformer.WrapScriptTransformerFactory</c> states for the same pair).
    /// </remarks>
    public static Func<object?, object?> TransformJsx(ITransformerContext context)
    {
        var transformer = new JsxTransformer(context);
        var chained = TransformationUtilities.ChainBundle(context, transformer.TransformSourceFile);
        return node => chained((Node)node!);
    }
}

/// <summary>ported from: src/compiler/transformers/jsx.ts:93-681 — the closures of <c>transformJsx</c>.</summary>
internal sealed partial class JsxTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly ICompilerOptions _compilerOptions;

    /// <summary>ported from: src/compiler/transformers/jsx.ts:107 — <c>currentSourceFile</c>.</summary>
    private SourceFile _currentSourceFile = null!;

    /// <summary>ported from: src/compiler/transformers/jsx.ts:108 — <c>currentFileState</c>.</summary>
    /// <remarks>PORT-NOTE: the reference clears it with <c>= undefined!</c> when a file is done; that is the port's
    /// <c>null!</c> (a later read fails the same way), not an empty state.</remarks>
    private PerFileState _currentFileState = null!;

    public JsxTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _compilerOptions = context.GetCompilerOptions();
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:93-106 — the <c>PerFileState</c> interface.</summary>
    private sealed class PerFileState
    {
        /// <summary>ported from: jsx.ts:94 — <c>importSpecifier</c>.</summary>
        /// <remarks>PORT-NOTE: the name is the reference's own; the slot holds the <em>implicit import base</em>
        /// <c>getJSXImplicitImportBase</c> answers, not an import specifier.</remarks>
        public string? ImportSpecifier { get; set; }

        /// <summary>ported from: jsx.ts:95 — <c>filenameDeclaration</c>.</summary>
        /// <remarks>PORT-NOTE: the reference's type is the intersection
        /// <c>VariableDeclaration &amp; { name: Identifier }</c>, which only refines the <c>name</c> slot; the port keeps
        /// the declaration and casts the slot where both readers read it (<c>CreateVariableDeclaration</c> is called
        /// with the unique identifier this very member built).</remarks>
        public VariableDeclaration? FilenameDeclaration { get; set; }

        /// <summary>ported from: jsx.ts:96 — <c>utilizedImplicitRuntimeImports</c>.</summary>
        /// <remarks>PORT-NOTE: a JS map keyed by <c>string | undefined</c>; see the class header's note on the key.</remarks>
        public Dictionary<string, Dictionary<string, ImportSpecifier>>? UtilizedImplicitRuntimeImports { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:109-157 — the implicit imports
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:109-117 — <c>getCurrentFileNameExpression</c>.</summary>
    private Identifier GetCurrentFileNameExpression()
    {
        if (_currentFileState.FilenameDeclaration is not null)
        {
            return (Identifier)_currentFileState.FilenameDeclaration.Name!;
        }

        var declaration = _factory.CreateVariableDeclaration(
            _factory.CreateUniqueName("_jsxFileName", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel),
            /*exclamationToken*/ null,
            /*type*/ null,
            _factory.CreateStringLiteral(_currentSourceFile.FileName));
        _currentFileState.FilenameDeclaration = declaration;
        return (Identifier)declaration.Name!;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:118-120 — <c>getJsxFactoryCalleePrimitive</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's return is the literal union <c>"jsx" | "jsxs" | "jsxDEV"</c>; the port keeps
    /// the strings and types the member <c>string</c>.</remarks>
    private string GetJsxFactoryCalleePrimitive(bool isStaticChildren)
        => _compilerOptions.Jsx == JsxEmit.ReactJSXDev ? "jsxDEV" : isStaticChildren ? "jsxs" : "jsx";

    /// <summary>ported from: src/compiler/transformers/jsx.ts:122-125 — <c>getJsxFactoryCallee</c>.</summary>
    private Identifier GetJsxFactoryCallee(bool isStaticChildren)
        => GetImplicitImportForName(GetJsxFactoryCalleePrimitive(isStaticChildren));

    /// <summary>ported from: src/compiler/transformers/jsx.ts:127-129 — <c>getImplicitJsxFragmentReference</c>.</summary>
    private Identifier GetImplicitJsxFragmentReference() => GetImplicitImportForName("Fragment");

    /// <summary>ported from: src/compiler/transformers/jsx.ts:131-157 — <c>getImplicitImportForName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference returns <c>existing.name</c> off the remembered <c>ImportSpecifier</c>; the port reads
    /// the same slot (the generated identifier <c>createUniqueName</c> built), which is why the returned type is
    /// <see cref="Identifier"/>.
    /// </remarks>
    private Identifier GetImplicitImportForName(string name)
    {
        var importSource = name == "createElement"
            ? _currentFileState.ImportSpecifier!
            : Utilities.GetJsxRuntimeImport(_currentFileState.ImportSpecifier, _compilerOptions)!;
        var existing = _currentFileState.UtilizedImplicitRuntimeImports?.GetValueOrDefault(importSource)?.GetValueOrDefault(name);
        if (existing is not null)
        {
            return existing.Name!;
        }

        if (_currentFileState.UtilizedImplicitRuntimeImports is null)
        {
            _currentFileState.UtilizedImplicitRuntimeImports = [];
        }

        if (!_currentFileState.UtilizedImplicitRuntimeImports.TryGetValue(importSource, out var specifierSourceImports))
        {
            specifierSourceImports = [];
            _currentFileState.UtilizedImplicitRuntimeImports[importSource] = specifierSourceImports;
        }

        var generatedName = _factory.CreateUniqueName(
            $"_{name}",
            GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.AllowNameSubstitution);
        var specifier = _factory.CreateImportSpecifier(/*isTypeOnly*/ false, _factory.CreateIdentifier(name), generatedName);
        EmitNodes.SetIdentifierGeneratedImportReference(generatedName, specifier);
        specifierSourceImports[name] = specifier;
        return generatedName;
    }

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:159-207 — the source-file transform
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:159-207 — <c>transformSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (<c>insertStatementAfterCustomPrologue</c>): the reference's is the <c>utilities.ts:1171</c> member,
    /// whose port home is <see cref="ES2015TransformSupport"/> (that class's header already records that its members
    /// belong to files other slices own); this slice calls it rather than stating a second copy.
    /// <para>
    /// PORT-NOTE (the statement list): TS starts from <c>visited.statements</c> itself and copies only with
    /// <c>.slice()</c> at each insert, which is what makes the trailing <c>statements !== visited.statements</c> test
    /// work; the port keeps that identity — <c>List&lt;IStatement&gt;</c> holds the same <c>NodeArray&lt;IStatement&gt;</c>
    /// until an insert copies it — so <see cref="object.ReferenceEquals"/> answers the reference's <c>!==</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>arrayFrom(map.entries())</c>): the port iterates a copy of the dictionary's pairs because the
    /// reference snapshots them with <c>arrayFrom</c>; the JSX transform never writes the map again, so the copy exists
    /// only to keep the loop's shape.
    /// </para>
    /// </remarks>
    public SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        _currentSourceFile = node;
        _currentFileState = new PerFileState
        {
            ImportSpecifier = Utilities.GetJsxImplicitImportBase(_compilerOptions, node),
        };
        var visited = Utilities.VisitEachChildWorker(node, Visit, _context);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());
        List<IStatement> statements = visited.Statements!;
        var filenameDeclaration = _currentFileState.FilenameDeclaration;
        if (filenameDeclaration is not null)
        {
            statements = ES2015TransformSupport.InsertStatementAfterCustomPrologue(
                [.. statements],
                _factory.CreateVariableStatement(
                    /*modifiers*/ null,
                    _factory.CreateVariableDeclarationList([filenameDeclaration], NodeFlags.Const)));
        }

        if (_currentFileState.UtilizedImplicitRuntimeImports is not null)
        {
            foreach (var (importSource, importSpecifiersMap) in new List<KeyValuePair<string, Dictionary<string, ImportSpecifier>>>(_currentFileState.UtilizedImplicitRuntimeImports))
            {
                if (Parser.IsExternalModule(node))
                {
                    // Add `import` statement
                    var importStatement = _factory.CreateImportDeclaration(
                        /*modifiers*/ null,
                        _factory.CreateImportClause(
                            /*phaseModifier*/ null,
                            /*name*/ null,
                            _factory.CreateNamedImports([.. importSpecifiersMap.Values])),
                        _factory.CreateStringLiteral(importSource),
                        /*attributes*/ null);
                    Utilities.SetParentRecursive(importStatement, /*incremental*/ false);
                    statements = ES2015TransformSupport.InsertStatementAfterCustomPrologue([.. statements], importStatement);
                }
                else if (Utilities.IsExternalOrCommonJsModule(node))
                {
                    // Add `require` statement
                    var requireStatement = _factory.CreateVariableStatement(
                        /*modifiers*/ null,
                        _factory.CreateVariableDeclarationList(
                            [
                                _factory.CreateVariableDeclaration(
                                    _factory.CreateObjectBindingPattern(
                                        [
                                            .. Core.Map(
                                                new List<ImportSpecifier>(importSpecifiersMap.Values),
                                                (s, _) => _factory.CreateBindingElement(/*dotDotDotToken*/ null, s.PropertyName, s.Name, /*initializer*/ null))!,
                                        ]),
                                    /*exclamationToken*/ null,
                                    /*type*/ null,
                                    _factory.CreateCallExpression(
                                        _factory.CreateIdentifier("require"),
                                        /*typeArguments*/ null,
                                        [_factory.CreateStringLiteral(importSource)])),
                            ],
                            NodeFlags.Const));
                    Utilities.SetParentRecursive(requireStatement, /*incremental*/ false);
                    statements = ES2015TransformSupport.InsertStatementAfterCustomPrologue([.. statements], requireStatement);
                }
                else
                {
                    // Do nothing (script file) - consider an error in the checker?
                }
            }
        }

        if (!ReferenceEquals(statements, visited.Statements))
        {
            visited = _factory.UpdateSourceFile(visited, statements, null, null, null, null, null);
        }

        _currentFileState = null!;
        return visited;
    }

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:209-257 — the visitors
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:209-216 — <c>visitor</c>.</summary>
    private Node? Visit(Node node)
        => (node.TransformFlags & TransformFlags.ContainsJsx) != 0
            ? VisitWorker(node)
            : node;

    /// <summary>ported from: src/compiler/transformers/jsx.ts:218-235 — <c>visitorWorker</c>.</summary>
    private Node? VisitWorker(Node node) => node.Kind switch
    {
        SyntaxKind.JsxElement => VisitJsxElement((JsxElement)node, /*isChild*/ false),
        SyntaxKind.JsxSelfClosingElement => VisitJsxSelfClosingElement((JsxSelfClosingElement)node, /*isChild*/ false),
        SyntaxKind.JsxFragment => VisitJsxFragment((JsxFragment)node, /*isChild*/ false),
        SyntaxKind.JsxExpression => VisitJsxExpression((JsxExpression)node),
        _ => Utilities.VisitEachChildWorker(node, Visit, _context),
    };

    /// <summary>ported from: src/compiler/transformers/jsx.ts:237-257 — <c>transformJsxChildToExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>JsxChild</c> is a union (<c>JsxText | JsxExpression | JsxElement |
    /// JsxSelfClosingElement | JsxFragment</c>) with no C# type, so the parameter is a <see cref="Node"/> and the switch
    /// is the same kind dispatch; the <c>Debug.failBadSyntaxKind</c> arm is
    /// <see cref="FailBadSyntaxKind{T}"/>.</remarks>
    private Expression? TransformJsxChildToExpression(Node node) => node.Kind switch
    {
        SyntaxKind.JsxText => VisitJsxText((JsxText)node),
        SyntaxKind.JsxExpression => VisitJsxExpression((JsxExpression)node),
        SyntaxKind.JsxElement => VisitJsxElement((JsxElement)node, /*isChild*/ true),
        SyntaxKind.JsxSelfClosingElement => VisitJsxSelfClosingElement((JsxSelfClosingElement)node, /*isChild*/ true),
        SyntaxKind.JsxFragment => VisitJsxFragment((JsxFragment)node, /*isChild*/ true),
        _ => FailBadSyntaxKind<Expression?>(node),
    };

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:259-299 — the guards and the three top-level visits
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:259-267 — <c>hasProto</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>some</c> predicate is <c>isPropertyAssignment(p) &amp;&amp; ((isIdentifier(p.name)
    /// &amp;&amp; idText(p.name) === "__proto__") || (isStringLiteral(p.name) &amp;&amp; p.name.text === "__proto__"))</c>; the
    /// loop below is that same test with the <c>&amp;&amp;</c>/<c>||</c> precedence preserved (a non-property-assignment
    /// element answers false).</remarks>
    private static bool HasProto(ObjectLiteralExpression obj)
    {
        foreach (var property in obj.Properties!)
        {
            if (!NodeTests.IsPropertyAssignment(property))
            {
                continue;
            }

            var name = ((PropertyAssignment)property).Name;
            if (NodeTests.IsIdentifier(name!)
                ? Utilities.IdText(name!) == "__proto__"
                : NodeTests.IsStringLiteral(name!) && ((StringLiteral)name!).Text == "__proto__")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:269-280 — <c>hasKeyAfterPropsSpread</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>JsxOpeningLikeElement</c> union, whose only member this body reads
    /// is <c>attributes.properties</c> — the port takes the <see cref="Node"/> and reads it through
    /// <see cref="AttributesOf"/>. The reference's <c>elem.expression</c> reads are narrowed by
    /// <c>isJsxSpreadAttribute</c>, which the port states as the cast inside the same first arm.</remarks>
    private static bool HasKeyAfterPropsSpread(Node node)
    {
        var spread = false;
        foreach (var element in AttributesOf(node).Properties!)
        {
            if (NodeTests.IsJsxSpreadAttribute(element)
                && (!NodeTests.IsObjectLiteralExpression(((JsxSpreadAttribute)element).Expression!)
                    || Core.Some(((ObjectLiteralExpression)((JsxSpreadAttribute)element).Expression!).Properties, NodeTests.IsSpreadAssignment)))
            {
                spread = true;
            }
            else if (spread
                && NodeTests.IsJsxAttribute(element)
                && NodeTests.IsIdentifier(((JsxAttribute)element).Name!)
                && ((Identifier)((JsxAttribute)element).Name!).EscapedText == "key")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:282-284 — <c>shouldUseCreateElement</c>.</summary>
    private bool ShouldUseCreateElement(Node node)
        => _currentFileState.ImportSpecifier is null || HasKeyAfterPropsSpread(node);

    /// <summary>ported from: src/compiler/transformers/jsx.ts:286-290 — <c>visitJsxElement</c>.</summary>
    private Expression VisitJsxElement(JsxElement node, bool isChild)
    {
        var location = TypeScriptTransformSupport.CreateRange(
            ScannerUtilities.SkipTrivia(_currentSourceFile.Text!, node.Pos),
            node.End);
        return ShouldUseCreateElement(node.OpeningElement!)
            ? VisitJsxOpeningLikeElementCreateElement(node.OpeningElement!, node.Children, isChild, location)
            : VisitJsxOpeningLikeElementJSX(node.OpeningElement!, node.Children, isChild, location);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:291-295 — <c>visitJsxSelfClosingElement</c>.</summary>
    private Expression VisitJsxSelfClosingElement(JsxSelfClosingElement node, bool isChild)
    {
        var location = TypeScriptTransformSupport.CreateRange(
            ScannerUtilities.SkipTrivia(_currentSourceFile.Text!, node.Pos),
            node.End);
        return ShouldUseCreateElement(node)
            ? VisitJsxOpeningLikeElementCreateElement(node, /*children*/ null, isChild, location)
            : VisitJsxOpeningLikeElementJSX(node, /*children*/ null, isChild, location);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:296-299 — <c>visitJsxFragment</c>.</summary>
    private Expression VisitJsxFragment(JsxFragment node, bool isChild)
    {
        var location = TypeScriptTransformSupport.CreateRange(
            ScannerUtilities.SkipTrivia(_currentSourceFile.Text!, node.Pos),
            node.End);
        return _currentFileState.ImportSpecifier is null
            ? VisitJsxOpeningFragmentCreateElement(node.OpeningFragment!, node.Children, isChild, location)
            : VisitJsxOpeningFragmentJSX(node.OpeningFragment!, node.Children, isChild, location);
    }

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:301-331 — the children prop, the JSX-mode opening elements
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:301-304 — <c>convertJsxChildrenToChildrenPropObject</c>.</summary>
    private ObjectLiteralExpression? ConvertJsxChildrenToChildrenPropObject(NodeArray<Node> children)
    {
        var prop = ConvertJsxChildrenToChildrenPropAssignment(children);
        return prop is not null ? _factory.CreateObjectLiteralExpression([prop], /*multiLine*/ null) : null;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:306-314 — <c>convertJsxChildrenToChildrenPropAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>(nonWhitespaceChildren[0] as JsxExpression).dotDotDotToken</c> is a plain assertion,
    /// so for a single child that is not a <c>JsxExpression</c> it reads <c>undefined</c> off an object that has no such
    /// slot — the port's <c>is JsxExpression { DotDotDotToken: not null }</c> answers that same test.</remarks>
    private PropertyAssignment? ConvertJsxChildrenToChildrenPropAssignment(NodeArray<Node> children)
    {
        var nonWhitespaceChildren = Utilities.GetSemanticJsxChildren(children);
        if (nonWhitespaceChildren.Count == 1
            && !(nonWhitespaceChildren[0] is JsxExpression { DotDotDotToken: not null }))
        {
            var result = TransformJsxChildToExpression(nonWhitespaceChildren[0]);
            return result is not null ? _factory.CreatePropertyAssignment("children", result) : null;
        }

        var mapped = Core.MapDefined<Node, Expression>(children, (child, _) => TransformJsxChildToExpression(child));
        return Core.Length(mapped) > 0
            ? _factory.CreatePropertyAssignment("children", _factory.CreateArrayLiteralExpression(mapped, /*multiLine*/ null))
            : null;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:316-331 — <c>visitJsxOpeningLikeElementJSX</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>find(properties, p =&gt; !!p.name &amp;&amp; isIdentifier(p.name) &amp;&amp;
    /// p.name.escapedText === "key")</c> reads <c>name</c> off the attribute union, where a
    /// <c>JsxSpreadAttribute</c> declares it absent (<c>ObjectLiteralElement.name?: undefined</c>); the port's slot
    /// holds every element as the <see cref="Node"/> class and its <c>JsxAttribute</c> is <em>not</em> an
    /// <c>INamedDeclaration</c> (types.ts:3274 makes it a <c>Declaration</c>), so the same read is the two-kind switch
    /// below, whose <c>null</c> arms are that same absent <c>name</c>.</remarks>
    private Expression VisitJsxOpeningLikeElementJSX(Node node, NodeArray<Node>? children, bool isChild, TextRange location)
    {
        var tagName = GetTagName(node);
        var childrenProp = children is not null && children.Count > 0
            ? ConvertJsxChildrenToChildrenPropAssignment(children)
            : null;
        var keyAttr = Core.Find(AttributesOf(node).Properties, (p, _) =>
        {
            var name = p switch
            {
                JsxAttribute attribute => attribute.Name,
                JsxSpreadAttribute spread => spread.Name,
                _ => null,
            };
            return name is not null && NodeTests.IsIdentifier(name) && ((Identifier)name).EscapedText == "key";
        }) as JsxAttribute;
        IList<Node> attrs = keyAttr is not null
            ? Core.Filter(AttributesOf(node).Properties, p => !ReferenceEquals(p, keyAttr))!
            : AttributesOf(node).Properties!;
        var objectProperties = Core.Length(attrs) > 0
            // When there are no attributes, React wants {}
            ? TransformJsxAttributesToObjectProps(attrs, childrenProp)
            : (Expression)_factory.CreateObjectLiteralExpression(childrenProp is not null ? [childrenProp] : [], /*multiLine*/ null);
        return VisitJsxOpeningLikeElementOrFragmentJSX(
            tagName,
            objectProperties,
            keyAttr,
            children ?? new NodeArray<Node>(),
            isChild,
            location);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:333-380 — <c>visitJsxOpeningLikeElementOrFragmentJSX</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>!!(nonWhitespaceChildren[0] as JsxExpression)?.dotDotDotToken</c> uses
    /// optional chaining, so an empty child list answers false rather than reading index 0; the port's
    /// <c>Count &gt; 0</c> guard is that same reading.
    /// <para>
    /// PORT-NOTE (<c>getLineAndCharacterOfPosition</c>): <c>scanner.ts:548-550</c>'s member is the memoized
    /// <c>sourceFile.lineMap ??= getLineStarts(sourceFile)</c> plus <c>computeLineAndCharacterOfPosition</c>, and the
    /// port's <c>ScannerUtilities.GetLineAndCharacterOfPosition</c> takes an <c>ISourceFileLike</c>, which
    /// <c>SourceFile</c> is not — so the two steps are inlined here with the same <c>LineMap</c> memo, the shape
    /// <c>Checker.Check.Grammar.cs:1152</c>, <c>Program/Program.DiagnosticsAggregation.cs:430</c> and
    /// <c>Emit/Printer.SourceMaps.cs:120</c> already use.
    /// </para>
    /// </remarks>
    private Expression VisitJsxOpeningLikeElementOrFragmentJSX(
        Expression tagName,
        Expression objectProperties,
        JsxAttribute? keyAttr,
        NodeArray<Node> children,
        bool isChild,
        TextRange location)
    {
        var nonWhitespaceChildren = Utilities.GetSemanticJsxChildren(children);
        var isStaticChildren = nonWhitespaceChildren.Count > 1
            || (nonWhitespaceChildren.Count > 0 && nonWhitespaceChildren[0] is JsxExpression { DotDotDotToken: not null });
        var args = new List<Expression> { tagName, objectProperties };
        // function jsx(type, config, maybeKey) {}
        // "maybeKey" is optional. It is acceptable to use "_jsx" without a third argument
        if (keyAttr is not null)
        {
            args.Add(TransformJsxAttributeInitializer(keyAttr.Initializer));
        }

        if (_compilerOptions.Jsx == JsxEmit.ReactJSXDev)
        {
            var originalFile = Utilities.GetOriginalNode(_currentSourceFile);
            if (originalFile is not null && NodeTests.IsSourceFile(originalFile))
            {
                var sourceFile = (SourceFile)originalFile;
                // "maybeKey" has to be replaced with "void 0" to not break the jsxDEV signature
                if (keyAttr is null)
                {
                    args.Add(_factory.CreateVoidZero());
                }

                // isStaticChildren development flag
                args.Add(isStaticChildren ? _factory.CreateTrue() : _factory.CreateFalse());

                // __source development flag
                var lineStarts = sourceFile.LineMap ?? (sourceFile.LineMap = ScannerUtilities.ComputeLineStarts(sourceFile.Text ?? ""));
                var lineCol = ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, location.Pos);
                args.Add(_factory.CreateObjectLiteralExpression(
                    [
                        _factory.CreatePropertyAssignment("fileName", GetCurrentFileNameExpression()),
                        _factory.CreatePropertyAssignment("lineNumber", _factory.CreateNumericLiteral(lineCol.Line + 1)),
                        _factory.CreatePropertyAssignment("columnNumber", _factory.CreateNumericLiteral(lineCol.Character + 1)),
                    ],
                    /*multiLine*/ null));

                // __self development flag
                args.Add(_factory.CreateThis());
            }
        }

        var element = Utilities.SetTextRange(
            _factory.CreateCallExpression(GetJsxFactoryCallee(isStaticChildren), /*typeArguments*/ null, args),
            location);

        if (isChild)
        {
            FactoryUtilities.StartOnNewLine(element);
        }

        return element;
    }

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:382-447 — the classic-mode opening elements and fragments
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:382-411 — <c>visitJsxOpeningLikeElementCreateElement</c>.</summary>
    private Expression VisitJsxOpeningLikeElementCreateElement(Node node, NodeArray<Node>? children, bool isChild, TextRange location)
    {
        var tagName = GetTagName(node);
        var attrs = AttributesOf(node).Properties;
        var objectProperties = Core.Length(attrs) > 0
            // When there are no attributes, React wants "null"
            ? TransformJsxAttributesToObjectProps(attrs!)
            : (Expression)_factory.CreateNull();

        var callee = _currentFileState.ImportSpecifier is null
            ? FactoryUtilities.CreateJsxFactoryExpression(
                _factory,
                _context.GetEmitResolver().GetJsxFactoryEntity(_currentSourceFile),
                _compilerOptions.ReactNamespace!,
                node)
            : GetImplicitImportForName("createElement");

        var element = FactoryUtilities.CreateExpressionForJsxElement(
            _factory,
            callee,
            tagName,
            objectProperties,
            Core.MapDefined<Node, Expression>(children, (child, _) => TransformJsxChildToExpression(child)),
            location);

        if (isChild)
        {
            FactoryUtilities.StartOnNewLine(element);
        }

        return element;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:413-429 — <c>visitJsxOpeningFragmentJSX</c>.</summary>
    /// <remarks>PORT-NOTE: the reference passes its shared <c>emptyArray</c> where the child list is absent; the port
    /// builds the empty <c>NodeArray&lt;Node&gt;</c> the same child-list slot holds, which that member only reads.</remarks>
    private Expression VisitJsxOpeningFragmentJSX(JsxOpeningFragment _node, NodeArray<Node>? children, bool isChild, TextRange location)
    {
        Expression? childrenProps = null;
        if (children is not null && children.Count > 0)
        {
            var result = ConvertJsxChildrenToChildrenPropObject(children);
            if (result is not null)
            {
                childrenProps = result;
            }
        }

        return VisitJsxOpeningLikeElementOrFragmentJSX(
            GetImplicitJsxFragmentReference(),
            childrenProps ?? _factory.CreateObjectLiteralExpression([], /*multiLine*/ null),
            /*keyAttr*/ null,
            children ?? new NodeArray<Node>(),
            isChild,
            location);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:431-447 — <c>visitJsxOpeningFragmentCreateElement</c>.</summary>
    private Expression VisitJsxOpeningFragmentCreateElement(JsxOpeningFragment node, NodeArray<Node>? children, bool isChild, TextRange location)
    {
        var element = FactoryUtilities.CreateExpressionForJsxFragment(
            _factory,
            _context.GetEmitResolver().GetJsxFactoryEntity(_currentSourceFile),
            _context.GetEmitResolver().GetJsxFragmentFactoryEntity(_currentSourceFile),
            _compilerOptions.ReactNamespace!,
            Core.MapDefined<Node, Expression>(children, (child, _) => TransformJsxChildToExpression(child)),
            node,
            location);

        if (isChild)
        {
            FactoryUtilities.StartOnNewLine(element);
        }

        return element;
    }

    // ---------------------------------------------------------------------------------------------
    // jsx.ts:449-524 — the attributes
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/jsx.ts:449-454 — <c>transformJsxSpreadAttributeToProps</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the two answer shapes): the reference answers either the spread object literal's (visited)
    /// property list or a single <c>SpreadAssignment</c>, and its only caller flattens one level with
    /// <c>flatten</c>, which spreads an array and appends a node. The port answers an <c>IList&lt;Node&gt;</c> in both
    /// cases — the single assignment is a one-element list — so the same one-level flatten below produces the same
    /// property list.
    /// </remarks>
    private IList<Node> TransformJsxSpreadAttributeToProps(JsxSpreadAttribute node)
    {
        if (NodeTests.IsObjectLiteralExpression(node.Expression!) && !HasProto((ObjectLiteralExpression)node.Expression!))
        {
            var expression = (ObjectLiteralExpression)node.Expression!;
            return Core.SameMap(
                expression.Properties,
                (p, _) => Debug.CheckDefined(Utilities.VisitNode(p, Visit, Utilities.IsObjectLiteralElementLike)))!;
        }

        return [_factory.CreateSpreadAssignment(Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visit, Utilities.IsExpression)))];
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:456-460 — <c>transformJsxAttributesToObjectProps</c>.</summary>
    /// <remarks>PORT-NOTE: TS writes <c>target &amp;&amp; target &gt;= ScriptTarget.ES2018</c>; the only falsy target is
    /// <c>ES3</c> (0, Model/ScriptTarget.cs:14), which fails the comparison anyway, so the port states the comparison
    /// alone.</remarks>
    private Expression TransformJsxAttributesToObjectProps(IList<Node> attrs, PropertyAssignment? children = null)
    {
        var target = Utilities.GetEmitScriptTarget(_compilerOptions);
        return target >= ScriptTarget.ES2018
            ? _factory.CreateObjectLiteralExpression(TransformJsxAttributesToProps(attrs, children), /*multiLine*/ null)
            : TransformJsxAttributesToExpression(attrs, children);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:462-468 — <c>transformJsxAttributesToProps</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>spanMap(…, (attrs, isSpread) =&gt; flatten(map(attrs, …)))</c> shadows the
    /// outer <c>attrs</c>; the span a <c>spanMap</c> callback receives is its first argument here too. The outer
    /// <c>flatten</c> then spreads both the per-span lists and the one-element lists
    /// <see cref="TransformJsxSpreadAttributeToProps"/> answers.</remarks>
    private List<Node> TransformJsxAttributesToProps(IList<Node> attrs, PropertyAssignment? children = null)
    {
        var spans = Core.SpanMap<Node, bool, object?>(
            attrs,
            (attr, _) => NodeTests.IsJsxSpreadAttribute(attr),
            (span, isSpread, _, _) => Core.Flatten<Node>(Core.Map(span, (attr, _) => (object?)(isSpread
                ? (object)TransformJsxSpreadAttributeToProps((JsxSpreadAttribute)attr)
                : TransformJsxAttributeToObjectLiteralElement((JsxAttribute)attr)))!));
        var props = new List<Node>(Core.Flatten<Node>(spans!));
        if (children is not null)
        {
            props.Add(children);
        }

        return props;
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:470-517 — <c>transformJsxAttributesToExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (the local <c>finishObjectLiteralIfNeeded</c>): the reference's closure over <c>expressions</c> and
    /// <c>properties</c> is the port's local function over the same two lists (it reassigns <c>properties</c>, which a
    /// local function captures by reference).
    /// <para>
    /// PORT-NOTE (<c>singleOrUndefined(expressions) || …</c>): an <c>Expression</c> node is never falsy in TS, so the
    /// logical <c>||</c> that falls through to the assign helper is the port's <c>??</c>.
    /// </para>
    /// </remarks>
    private Expression TransformJsxAttributesToExpression(IList<Node> attrs, PropertyAssignment? children = null)
    {
        var expressions = new List<Expression>();
        var properties = new List<Node>();

        foreach (var attr in attrs)
        {
            if (NodeTests.IsJsxSpreadAttribute(attr))
            {
                var spread = (JsxSpreadAttribute)attr;
                // as an optimization we try to flatten the first level of spread inline object
                // as if its props would be passed as JSX attributes
                if (NodeTests.IsObjectLiteralExpression(spread.Expression!) && !HasProto((ObjectLiteralExpression)spread.Expression!))
                {
                    foreach (var prop in ((ObjectLiteralExpression)spread.Expression!).Properties!)
                    {
                        if (NodeTests.IsSpreadAssignment(prop))
                        {
                            FinishObjectLiteralIfNeeded();
                            expressions.Add(Debug.CheckDefined(Utilities.VisitNode(((SpreadAssignment)prop).Expression, Visit, Utilities.IsExpression)));
                            continue;
                        }

                        properties.Add(Debug.CheckDefined(Utilities.VisitNode(prop, Visit)));
                    }

                    continue;
                }

                FinishObjectLiteralIfNeeded();
                expressions.Add(Debug.CheckDefined(Utilities.VisitNode(spread.Expression, Visit, Utilities.IsExpression)));
                continue;
            }

            properties.Add(TransformJsxAttributeToObjectLiteralElement((JsxAttribute)attr));
        }

        if (children is not null)
        {
            properties.Add(children);
        }

        FinishObjectLiteralIfNeeded();

        if (expressions.Count > 0 && !NodeTests.IsObjectLiteralExpression(expressions[0]))
        {
            // We must always emit at least one object literal before a spread attribute
            // as the JSX always factory expects a fresh object, so we need to make a copy here
            // we also avoid mutating an external reference by doing this (first expression is used as assign's target)
            expressions.Insert(0, _factory.CreateObjectLiteralExpression(/*properties*/ null, /*multiLine*/ null));
        }

        return Core.SingleOrUndefined(expressions) ?? _emitHelpers.CreateAssignHelper(expressions);

        void FinishObjectLiteralIfNeeded()
        {
            if (properties.Count > 0)
            {
                expressions.Add(_factory.CreateObjectLiteralExpression(properties, /*multiLine*/ null));
                properties = [];
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:519-523 — <c>transformJsxAttributeToObjectLiteralElement</c>.</summary>
    private PropertyAssignment TransformJsxAttributeToObjectLiteralElement(JsxAttribute node)
    {
        var name = GetAttributeName(node);
        var expression = TransformJsxAttributeInitializer(node.Initializer);
        return _factory.CreatePropertyAssignment(name, expression);
    }

    /// <summary>ported from: src/compiler/transformers/jsx.ts:525-552 — <c>transformJsxAttributeInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>JsxAttributeValue | undefined</c> (<c>StringLiteral | JsxExpression |
    /// JsxElement | JsxSelfClosingElement | JsxFragment</c>); four of those five are <c>Expression</c> subclasses in the
    /// model, so the port takes <see cref="Expression"/> and keeps the reference's kind dispatch.</remarks>
    private Expression TransformJsxAttributeInitializer(Expression? node)
    {
        if (node is null)
        {
            return _factory.CreateTrue();
        }

        if (node.Kind == SyntaxKind.StringLiteral)
        {
            var literal = (StringLiteral)node;
            // Always recreate the literal to escape any escape sequences or newlines which may be in the original jsx string and which
            // Need to be escaped to be handled correctly in a normal string
            var singleQuote = literal.SingleQuote ?? !Utilities.IsStringDoubleQuoted(literal, _currentSourceFile);
            var text = TryDecodeEntities(literal.Text!) ?? literal.Text;
            return Utilities.SetTextRange(_factory.CreateStringLiteral(text, singleQuote), literal);
        }

        if (node.Kind == SyntaxKind.JsxExpression)
        {
            var jsxExpression = (JsxExpression)node;
            if (jsxExpression.Expression is null)
            {
                return _factory.CreateTrue();
            }

            return Debug.CheckDefined(Utilities.VisitNode(jsxExpression.Expression, Visit, Utilities.IsExpression));
        }

        if (NodeTests.IsJsxElement(node))
        {
            return VisitJsxElement((JsxElement)node, /*isChild*/ false);
        }

        if (NodeTests.IsJsxSelfClosingElement(node))
        {
            return VisitJsxSelfClosingElement((JsxSelfClosingElement)node, /*isChild*/ false);
        }

        if (NodeTests.IsJsxFragment(node))
        {
            return VisitJsxFragment((JsxFragment)node, /*isChild*/ false);
        }

        return FailBadSyntaxKind<Expression>(node);
    }

    // ---------------------------------------------------------------------------------------------
    // The JSX unions, read once
    // ---------------------------------------------------------------------------------------------

    /// <summary>The <c>attributes</c> slot of the <c>JsxOpeningLikeElement</c> union.</summary>
    /// <remarks>PORT-NOTE: TS reads <c>node.attributes</c> straight off the union, whose two members both declare it;
    /// C# narrows on the kind (the <c>JsxOpeningLikeElement</c> spelling of §12.5).</remarks>
    private static JsxAttributes AttributesOf(Node node) => node switch
    {
        JsxOpeningElement openingElement => openingElement.Attributes!,
        JsxSelfClosingElement selfClosingElement => selfClosingElement.Attributes!,
        _ => FailBadSyntaxKind<JsxAttributes>(node),
    };

    /// <summary>The <c>tagName</c> slot of the <c>JsxOpeningLikeElement</c> union.</summary>
    /// <remarks>PORT-NOTE: the same narrowing as <see cref="AttributesOf"/>.</remarks>
    private static Node TagNameOf(Node node) => node switch
    {
        JsxOpeningElement openingElement => openingElement.TagName!,
        JsxSelfClosingElement selfClosingElement => selfClosingElement.TagName!,
        _ => FailBadSyntaxKind<Node>(node),
    };

    /// <summary>The value-producing form of <c>Debug.failBadSyntaxKind</c>.</summary>
    /// <remarks>PORT-NOTE: <c>debug.ts</c>'s helper is <c>void</c> with <c>[DoesNotReturn]</c> in the port
    /// (Core/Debug.Model.cs:42), so the arms that must answer a value use <see cref="Debug.Fail{T}"/> with the same
    /// message the helper builds — the shape <c>TransformTypeScript</c> and <c>Checker.Check.Symbols</c> record for the
    /// same situation.</remarks>
    private static T FailBadSyntaxKind<T>(Node node)
        => Debug.Fail<T>($"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(node.Kind)} was unexpected.");
}
