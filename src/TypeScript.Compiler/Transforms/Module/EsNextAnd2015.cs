#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/esnextAnd2015.ts:67-439 — the whole ECMAScript-module transform.
/// <summary>
/// ported from: src/compiler/transformers/module/esnextAnd2015.ts:67-439 — <c>transformECMAScriptModule</c>: the
/// transformer that keeps a <see cref="SourceFile"/> an ES module (adding the external-helpers <c>import</c>, the
/// <c>createRequire</c> shim for an <c>import=</c>, the empty-exports marker when nothing else makes the file a module,
/// and the relative-import-extension rewrites under <c>--rewriteRelativeImportExtensions</c>).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformECMAScriptModule</c>, whose body is a table
/// of closures over that function's locals; the port states the factory as
/// <see cref="EsNextAnd2015.TransformECMAScriptModule"/> and the closures as the members of
/// <see cref="ECMAScriptModuleTransformer"/>.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
/// <c>Func&lt;object?, object?&gt;</c>; the returned member is the reference's own
/// <c>chainBundle(context, transformSourceFile)</c> call.
/// </para>
/// </remarks>
internal static class EsNextAnd2015
{
    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:67-88 — <c>transformECMAScriptModule</c>.</summary>
    public static Func<object?, object?> TransformECMAScriptModule(ITransformerContext context)
        => new ECMAScriptModuleTransformer(context).TransformSourceFileOrBundle;
}

/// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:67-439 — the body of
/// <c>transformECMAScriptModule</c>.</summary>
/// <remarks>
/// PORT-NOTE (the state): the reference's <c>noSubstitution</c>, <c>importsAndRequiresToRewriteOrShim</c>,
/// <c>helperNameSubstitutions</c>, <c>currentSourceFile</c> and <c>importRequireStatements</c> are the factory
/// function's locals that the closures mutate; they are this class's fields. <c>noSubstitution</c> is TS's
/// <c>Set&lt;NodeId&gt;</c>, whose C# spelling is a <see cref="HashSet{T}"/> of the ids <c>Utilities.GetNodeId</c>
/// answers.
/// </remarks>
internal sealed class ECMAScriptModuleTransformer
{
    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:207-259 — the two statements
    /// <c>createRequireCall</c> hoists.</summary>
    /// <remarks>PORT-NOTE: TS's field is the tuple type <c>[ImportDeclaration, VariableStatement]</c> read as
    /// <c>importRequireStatements[1].declarationList.declarations[0].name</c>; the port states the pair as this
    /// constructible class, the treatment <c>TransformationUtilities.ExternalModuleInfo</c> records for the same
    /// situation.</remarks>
    private sealed class ImportRequireStatements(ImportDeclaration importStatement, VariableStatement requireStatement)
    {
        /// <summary>The <c>import { createRequire as _createRequire } from "module"</c> statement.</summary>
        public ImportDeclaration ImportStatement { get; } = importStatement;

        /// <summary>The <c>const __require = _createRequire(import.meta.url)</c> statement.</summary>
        public VariableStatement RequireStatement { get; } = requireStatement;
    }

    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitHost _host;
    private readonly IEmitResolver _resolver;
    private readonly ICompilerOptions _compilerOptions;
    private readonly ScriptTarget _languageVersion;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:88 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:83 — <c>noSubstitution</c>.</summary>
    private readonly HashSet<int> _noSubstitution = [];

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:84 — <c>importsAndRequiresToRewriteOrShim</c>.</summary>
    private List<CallExpression>? _importsAndRequiresToRewriteOrShim;

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:85 — <c>helperNameSubstitutions</c>.</summary>
    private Dictionary<string, Identifier>? _helperNameSubstitutions;

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:86 — <c>currentSourceFile</c>.</summary>
    private SourceFile? _currentSourceFile;

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:87 — <c>importRequireStatements</c>.</summary>
    private ImportRequireStatements? _importRequireStatements;

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:68-88 — the factory's prologue.</summary>
    public ECMAScriptModuleTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _host = context.GetEmitHost();
        _resolver = context.GetEmitResolver();
        _compilerOptions = context.GetCompilerOptions();
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);

        var previousOnEmitNode = context.OnEmitNode;
        var previousOnSubstituteNode = context.OnSubstituteNode;
        _previousOnEmitNode = previousOnEmitNode!;
        _previousOnSubstituteNode = previousOnSubstituteNode!;
        context.OnEmitNode = OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;
        context.EnableEmitNotification(SyntaxKind.SourceFile);
        context.EnableSubstitution(SyntaxKind.Identifier);
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:90-124 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:90-124 — <c>transformSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: <c>currentSourceFile.flags &amp; NodeFlags.PossiblyContainsDynamicImport</c> is truthiness of a
    /// flag, so the port states the <c>!= 0</c> test; <c>factory.createNodeArray(insertStatementsAfterCustomPrologue(
    /// result.statements.slice(), importRequireStatements))</c> is the port's copy-then-insert pair
    /// (<see cref="List{T}.GetRange"/> does not exist for the whole list, so the copy is the list's own spread).</remarks>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        if (Parser.IsExternalModule(node) || Utilities.GetIsolatedModules(_compilerOptions))
        {
            _currentSourceFile = node;
            _importRequireStatements = null;
            if (Js.IsTruthy(_compilerOptions.RewriteRelativeImportExtensions)
                && (node.Flags & NodeFlags.PossiblyContainsDynamicImport) != 0 || Utilities.IsInJSFile(node))
            {
                Utilities.ForEachDynamicImportOrRequireCall(
                    node,
                    /*includeTypeSpaceImports*/ false,
                    /*requireStringLiteralLikeArgument*/ false,
                    (call, _) =>
                    {
                        var argument = ((CallExpression)call).Arguments![0];
                        if (!Utilities.IsStringLiteralLike(argument)
                            || Utilities.ShouldRewriteModuleSpecifier(((StringLiteral)argument).Text!, _compilerOptions))
                        {
                            _importsAndRequiresToRewriteOrShim = (List<CallExpression>?)Core.Append(
                                _importsAndRequiresToRewriteOrShim,
                                (CallExpression)call);
                        }
                    });
            }
            var result = UpdateExternalModule(node);
            EmitNodes.AddEmitHelpers(result, _context.ReadEmitHelpers());
            _currentSourceFile = null;
            if (_importRequireStatements is not null)
            {
                result = _factory.UpdateSourceFile(
                    result,
                    Utilities.SetTextRange(
                        _factory.CreateNodeArray(
                            ES2015TransformSupport.InsertStatementsAfterCustomPrologue(
                                [.. result.Statements!],
                                [_importRequireStatements.ImportStatement, _importRequireStatements.RequireStatement]),
                            null),
                        result.Statements),
                    null,
                    null,
                    null,
                    null,
                    null);
            }
            if (!Parser.IsExternalModule(node)
                || Utilities.GetEmitModuleKind(_compilerOptions) == ModuleKind.Preserve
                || Core.Some(result.Statements, (statement, _) => Utilities.IsExternalModuleIndicator(statement)))
            {
                return result;
            }
            return _factory.UpdateSourceFile(
                result,
                Utilities.SetTextRange(
                    _factory.CreateNodeArray([.. result.Statements!, FactoryUtilities.CreateEmptyExports(_factory)], null),
                    result.Statements),
                null,
                null,
                null,
                null,
                null);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:126-141 — <c>updateExternalModule</c>.</summary>
    private SourceFile UpdateExternalModule(SourceFile node)
    {
        var externalHelpersImportDeclaration = TransformationUtilities.CreateExternalHelpersImportDeclarationIfNeeded(
            _factory, _emitHelpers, node, _compilerOptions, null, null, null);
        if (externalHelpersImportDeclaration is not null)
        {
            var statements = new List<IStatement>();
            var statementOffset = _factory.CopyPrologue(node.Statements, statements, /*ensureUseStrict*/ null, /*visitor*/ null);
            var helperStatements = Utilities.VisitArray(
                _factory.CreateNodeArray([externalHelpersImportDeclaration], null),
                Visitor,
                Utilities.IsStatement);
            if (helperStatements is not null)
            {
                foreach (var helperStatement in helperStatements)
                {
                    statements.Add((IStatement)helperStatement);
                }
            }
            Core.AddRange(statements, Utilities.VisitNodes(node.Statements, Visitor, Utilities.IsStatement, statementOffset));
            return _factory.UpdateSourceFile(
                node,
                Utilities.SetTextRange(_factory.CreateNodeArray(statements, null), node.Statements),
                null,
                null,
                null,
                null,
                null);
        }
        else
        {
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:143-169 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ExportDeclaration</c> arm declares <c>const exportDecl</c> before its <c>return</c> and
    /// falls through to the <c>CallExpression</c> arm's shared prefix (the reference's <c>// fallthrough</c>); the port's
    /// two separate cases state each arm's own body.</remarks>
    private object? Visitor(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ImportEqualsDeclaration:
                // Though an error in es2020 modules, in node-flavor es2020 modules, we can helpfully transform this to a synthetic `require` call
                // To give easy access to a synchronous `require` in node-flavor esm. We do the transform even in scenarios where we error, but `import.meta.url`
                // is available, just because the output is reasonable for a node-like runtime.
                return Utilities.GetEmitModuleKind(_compilerOptions) >= ModuleKind.Node16
                    ? VisitImportEqualsDeclaration((ImportEqualsDeclaration)node)
                    : null;
            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment((ExportAssignment)node);
            case SyntaxKind.ExportDeclaration:
                return VisitExportDeclaration((ExportDeclaration)node);
            case SyntaxKind.ImportDeclaration:
                return VisitImportDeclaration((ImportDeclaration)node);
            case SyntaxKind.CallExpression:
                if (ReferenceEquals(node, Core.FirstOrUndefined(_importsAndRequiresToRewriteOrShim)))
                {
                    var first = _importsAndRequiresToRewriteOrShim![0];
                    _importsAndRequiresToRewriteOrShim.RemoveAt(0);
                    return VisitImportOrRequireCall(first);
                }
                // fallthrough
                goto default;
            default:
                if (Core.Some(_importsAndRequiresToRewriteOrShim)
                    && ModuleTransformSupport.RangeContainsRange(node, _importsAndRequiresToRewriteOrShim![0]))
                {
                    return Utilities.VisitEachChildWorker(node, Visitor, _context);
                }
                break;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:171-186 — <c>visitImportDeclaration</c>.</summary>
    private object? VisitImportDeclaration(ImportDeclaration node)
    {
        if (!Js.IsTruthy(_compilerOptions.RewriteRelativeImportExtensions))
        {
            return node;
        }
        var updatedModuleSpecifier = TransformationUtilities.RewriteModuleSpecifier(node.ModuleSpecifier, _compilerOptions);
        if (ReferenceEquals(updatedModuleSpecifier, node.ModuleSpecifier))
        {
            return node;
        }
        return _factory.UpdateImportDeclaration(
            node,
            node.Modifiers,
            node.ImportClause,
            updatedModuleSpecifier,
            node.Attributes);
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:188-205 — <c>visitImportOrRequireCall</c>.</summary>
    private object? VisitImportOrRequireCall(CallExpression node)
        => _factory.UpdateCallExpression(
            node,
            node.Expression,
            node.TypeArguments,
            [
                Utilities.IsStringLiteralLike(node.Arguments![0])
                    ? TransformationUtilities.RewriteModuleSpecifier((Expression)node.Arguments![0], _compilerOptions)!
                    : _emitHelpers.CreateRewriteRelativeImportExtensionsHelper((Expression)node.Arguments![0]),
                .. node.Arguments!.GetRange(1, node.Arguments!.Count - 1),
            ]);

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:207-259 — <c>createRequireCall</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>Debug.checkDefined(currentSourceFile)</c> is the port's <c>!</c> on the
    /// nullable field; <c>Debug.assertNode(name, isIdentifier)</c> has no port member, so the identifier is the typed
    /// cast the reference's assertion establishes.</remarks>
    private CallExpression CreateRequireCall(Node importNode)
    {
        var moduleName = ModuleTransformSupport.GetExternalModuleNameLiteral(
            _factory, importNode, Debug.CheckDefined(_currentSourceFile), _host, _resolver, _compilerOptions);
        var args = new List<Expression>();
        if (moduleName is not null)
        {
            args.Add(TransformationUtilities.RewriteModuleSpecifier(moduleName, _compilerOptions)!);
        }
        if (Utilities.GetEmitModuleKind(_compilerOptions) == ModuleKind.Preserve)
        {
            return _factory.CreateCallExpression(_factory.CreateIdentifier("require", null, null), /*typeArguments*/ null, args);
        }

        if (_importRequireStatements is null)
        {
            var createRequireName = _factory.CreateUniqueName("_createRequire", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
            var importStatement = _factory.CreateImportDeclaration(
                /*modifiers*/ null,
                _factory.CreateImportClause(
                    /*phaseModifier*/ null,
                    /*name*/ null,
                    _factory.CreateNamedImports([
                        _factory.CreateImportSpecifier(/*isTypeOnly*/ false, _factory.CreateIdentifier("createRequire", null, null), createRequireName),
                    ])),
                _factory.CreateStringLiteral("module", null, null),
                /*attributes*/ null);
            var requireHelperName = _factory.CreateUniqueName("__require", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
            var requireStatement = _factory.CreateVariableStatement(
                /*modifiers*/ null,
                _factory.CreateVariableDeclarationList(
                    [
                        _factory.CreateVariableDeclaration(
                            requireHelperName,
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            _factory.CreateCallExpression(_factory.CloneNode(createRequireName), /*typeArguments*/ null, [
                                _factory.CreatePropertyAccessExpression(
                                    _factory.CreateMetaProperty(SyntaxKind.ImportKeyword, _factory.CreateIdentifier("meta", null, null)),
                                    _factory.CreateIdentifier("url", null, null)),
                            ])),
                    ],
                    /*flags*/ _languageVersion >= ScriptTarget.ES2015 ? NodeFlags.Const : NodeFlags.None));
            _importRequireStatements = new ImportRequireStatements(importStatement, requireStatement);
        }

        var name = _importRequireStatements.RequireStatement.DeclarationList!.Declarations![0].Name;
        Debug.Assert(NodeTests.IsIdentifier(name!), "Expected an identifier");
        return _factory.CreateCallExpression(_factory.CloneNode((Identifier)name!), /*typeArguments*/ null, args);
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:261-292 — <c>visitImportEqualsDeclaration</c>.</summary>
    private object? VisitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        Debug.Assert(Utilities.IsExternalModuleImportEqualsDeclaration(node), "import= for internal module references should be handled in an earlier transformer.");

        List<IStatement>? statements = null;
        statements = (List<IStatement>?)Core.Append(
            statements,
            NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateVariableStatement(
                        /*modifiers*/ null,
                        _factory.CreateVariableDeclarationList(
                            [
                                _factory.CreateVariableDeclaration(
                                    _factory.CloneNode(node.Name),
                                    /*exclamationToken*/ null,
                                    /*type*/ null,
                                    CreateRequireCall(node)),
                            ],
                            /*flags*/ _languageVersion >= ScriptTarget.ES2015 ? NodeFlags.Const : NodeFlags.None)),
                    node),
                node));

        statements = AppendExportsOfImportEqualsDeclaration(statements, node);

        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:294-306 — <c>appendExportsOfImportEqualsDeclaration</c>.</summary>
    private List<IStatement>? AppendExportsOfImportEqualsDeclaration(List<IStatement>? statements, ImportEqualsDeclaration node)
    {
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            statements = (List<IStatement>?)Core.Append(
                statements,
                _factory.CreateExportDeclaration(
                    /*modifiers*/ null,
                    node.IsTypeOnly,
                    _factory.CreateNamedExports([
                        _factory.CreateExportSpecifier(/*isTypeOnly*/ false, /*propertyName*/ null, Utilities.IdText(node.Name!)),
                    ]),
                    null,
                    null));
        }
        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:308-329 — <c>visitExportAssignment</c>.</summary>
    private object? VisitExportAssignment(ExportAssignment node)
    {
        if (Js.IsTruthy(node.IsExportEquals))
        {
            if (Utilities.GetEmitModuleKind(_compilerOptions) == ModuleKind.Preserve)
            {
                var statement = NodeFactory.SetOriginalNode(
                    _factory.CreateExpressionStatement(
                        _factory.CreateAssignment(
                            _factory.CreatePropertyAccessExpression(
                                _factory.CreateIdentifier("module", null, null),
                                "exports"),
                            node.Expression)),
                    node);
                return statement;
            }
            // Elide `export=` as it is not legal with --module ES6
            return null;
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:331-384 — <c>visitExportDeclaration</c>.</summary>
    private object? VisitExportDeclaration(ExportDeclaration node)
    {
        var updatedModuleSpecifier = TransformationUtilities.RewriteModuleSpecifier(node.ModuleSpecifier, _compilerOptions);
        if (
            (_compilerOptions.Module is not null && _compilerOptions.Module > ModuleKind.ES2015)
            || node.ExportClause is null
            || !NodeTests.IsNamespaceExport(node.ExportClause)
            || node.ModuleSpecifier is null
        )
        {
            // Either ill-formed or don't need to be tranformed.
            return node.ModuleSpecifier is null || ReferenceEquals(updatedModuleSpecifier, node.ModuleSpecifier)
                ? node
                : _factory.UpdateExportDeclaration(
                    node,
                    node.Modifiers,
                    node.IsTypeOnly,
                    node.ExportClause,
                    updatedModuleSpecifier,
                    node.Attributes);
        }

        var oldIdentifier = ((NamespaceExport)node.ExportClause).Name;
        var synthName = _factory.GetGeneratedNameForNode(oldIdentifier, null);
        var importDecl = _factory.CreateImportDeclaration(
            /*modifiers*/ null,
            _factory.CreateImportClause(
                /*phaseModifier*/ null,
                /*name*/ null,
                _factory.CreateNamespaceImport(synthName)),
            updatedModuleSpecifier!,
            node.Attributes);
        NodeFactory.SetOriginalNode(importDecl, node.ExportClause);

        var exportDecl = ModuleTransformSupport.IsExportNamespaceAsDefaultDeclaration(node)
            ? (Node)_factory.CreateExportDefault(synthName)
            : _factory.CreateExportDeclaration(
                /*modifiers*/ null,
                /*isTypeOnly*/ false,
                _factory.CreateNamedExports([
                    _factory.CreateExportSpecifier(/*isTypeOnly*/ false, synthName, oldIdentifier),
                ]),
                null,
                null);
        NodeFactory.SetOriginalNode(exportDecl, node);

        return new List<Node> { importDecl, exportDecl };
    }

    //
    // Emit Notification
    //

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:386-409 — <c>onEmitNode</c>.</summary>
    private void OnEmitNode(EmitHint hint, Node node, Action<EmitHint, Node> emitCallback)
    {
        if (NodeTests.IsSourceFile(node))
        {
            if ((Parser.IsExternalModule((SourceFile)node) || Utilities.GetIsolatedModules(_compilerOptions)) && Js.IsTruthy(_compilerOptions.ImportHelpers))
            {
                _helperNameSubstitutions = [];
            }
            _currentSourceFile = (SourceFile)node;
            _previousOnEmitNode(hint, node, emitCallback);
            _currentSourceFile = null;
            _helperNameSubstitutions = null;
        }
        else
        {
            _previousOnEmitNode(hint, node, emitCallback);
        }
    }

    //
    // Substitutions
    //

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:411-421 — <c>onSubstituteNode</c>.</summary>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node) ?? node;
        if (node.Id is int nodeId && nodeId != 0 && _noSubstitution.Contains(nodeId))
        {
            return node;
        }
        if (NodeTests.IsIdentifier(node) && (Utilities.GetEmitFlags(node) & EmitFlags.HelperName) != 0)
        {
            return SubstituteHelperName((Identifier)node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/esnextAnd2015.ts:423-439 — <c>substituteHelperName</c>.</summary>
    private Expression SubstituteHelperName(Identifier node)
    {
        var externalHelpersModuleName = _currentSourceFile is not null
            ? FactoryUtilities.GetExternalHelpersModuleName(_currentSourceFile)
            : null;
        if (externalHelpersModuleName is not null)
        {
            _noSubstitution.Add(Utilities.GetNodeId(node));
            return _factory.CreatePropertyAccessExpression(externalHelpersModuleName, node);
        }
        if (_helperNameSubstitutions is not null)
        {
            var name = Utilities.IdText(node);
            if (!_helperNameSubstitutions.TryGetValue(name, out var substitution))
            {
                substitution = _factory.CreateUniqueName(name, GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
                _helperNameSubstitutions.Add(name, substitution);
            }
            return substitution;
        }
        return node;
    }
}
