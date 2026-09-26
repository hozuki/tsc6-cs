#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/module.ts:174-2504 — the whole CommonJS/AMD/UMD module transform.
/// <summary>
/// ported from: src/compiler/transformers/module/module.ts:174-224 — <c>transformModule</c>: the transformer that
/// rewrites a <see cref="SourceFile"/>'s module syntax into <c>require</c>/<c>exports</c> (CommonJS), <c>define</c>
/// (AMD) or a UMD wrapper, hoists the exports of the file's declarations, and substitutes imported/exported
/// identifiers at emit time.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformModule</c>, whose body is a table of
/// closures over that function's locals; the port states the factory as <see cref="ModuleTransform.TransformModule"/>
/// and the closures as the members of <see cref="ModuleTransformer"/> — the same "closures → classes" treatment
/// <c>ES2015Transform</c> and <c>TypeScriptTransform</c> record for their modules.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
/// <c>Func&lt;object?, object?&gt;</c>; the returned member is the reference's own
/// <c>chainBundle(context, transformSourceFile)</c> call.
/// </para>
/// </remarks>
internal static class ModuleTransform
{
    /// <summary>ported from: src/compiler/transformers/module/module.ts:174-224 — <c>transformModule</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>chainBundle(context, transformSourceFile)</c> is built in
    /// <see cref="ModuleTransformer"/>'s constructor and this member answers its bundle-aware face, so no delegate is
    /// allocated per call beyond the one <see cref="TransformationUtilities.ChainBundle"/> stores.</remarks>
    public static Func<object?, object?> TransformModule(ITransformerContext context)
        => new ModuleTransformer(context).TransformSourceFileOrBundle;
}

/// <summary>ported from: src/compiler/transformers/module/module.ts:174-2504 — the body of <c>transformModule</c>.</summary>
/// <remarks>
/// PORT-NOTE (the state): the reference's <c>currentSourceFile</c>, <c>currentModuleInfo</c>,
/// <c>importsAndRequiresToRewriteOrShim</c>, <c>noSubstitution</c> and <c>needUMDDynamicImportHelper</c> are the
/// factory function's locals that the closures mutate; they are this class's fields.
/// <para>
/// PORT-NOTE (<c>moduleInfoMap</c>): TS's <c>ExternalModuleInfo[]</c> is a <em>sparse</em> array indexed by
/// <c>getOriginalNodeId(file)</c>; the port's <see cref="Dictionary{TKey,TValue}"/> keeps the same key with no holes,
/// which is the treatment <c>SystemTransform</c> gives its four maps too.
/// </para>
/// <para>
/// PORT-NOTE (<c>noSubstitution</c>): TS's <c>boolean[]</c> is written by node id and read as
/// <c>node.id &amp;&amp; noSubstitution[node.id]</c> — a set membership test with a zero-id guard; the port's
/// <see cref="HashSet{T}"/> of ids is that same test (a node id is never 0, <c>utilities.ts:getNodeId</c>).
/// </para>
/// </remarks>
internal sealed partial class ModuleTransformer
{
// ported from: src/compiler/transformers/module/module.ts:175-178 — the `AsynchronousDependencies` interface.
/// <summary>ported from: src/compiler/transformers/module/module.ts:175-178 — <c>AsynchronousDependencies</c>.</summary>
/// <remarks>PORT-NOTE: TS fills the interface in with an object literal at the end of
/// <c>collectAsynchronousDependencies</c>; the port follows <c>TransformationUtilities.ExternalModuleInfo</c> and
/// states the constructible class.</remarks>
internal sealed class AsynchronousDependencies(
    List<Expression> aliasedModuleNames,
    List<Expression> unaliasedModuleNames,
    List<ParameterDeclaration> importAliasNames)
{
    /// <summary>Names of modules with a corresponding parameter in the factory function.</summary>
    public List<Expression> AliasedModuleNames { get; } = aliasedModuleNames;

    /// <summary>Names of modules with no corresponding parameter in the factory function.</summary>
    public List<Expression> UnaliasedModuleNames { get; } = unaliasedModuleNames;

    /// <summary>Names of the parameters in the factory function.</summary>
    public List<ParameterDeclaration> ImportAliasNames { get; } = importAliasNames;
}

    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly IEmitHost _host;
    private readonly ICompilerOptions _compilerOptions;
    private readonly ScriptTarget _languageVersion;
    private readonly ModuleKind _moduleKind;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/module/module.ts:224 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1010 / 1039 — <c>factory.liftToBlock</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <c>visitNode</c> lift slot is <c>Func&lt;IReadOnlyList&lt;Node&gt;, Node?&gt;</c>
    /// while <c>NodeFactory.LiftToBlock</c> takes a <c>List&lt;Node&gt;</c>; this is that adapter, built once (the same
    /// one <c>ES2015Transformer</c> keeps).</remarks>
    private readonly Func<IReadOnlyList<Node>, Node?> _liftToBlock;

    /// <summary>ported from: src/compiler/transformers/module/module.ts:216 — <c>moduleInfoMap</c>.</summary>
    private readonly Dictionary<int, TransformationUtilities.IExternalModuleInfo> _moduleInfoMap = [];

    /// <summary>ported from: src/compiler/transformers/module/module.ts:221 — <c>noSubstitution</c>.</summary>
    private readonly HashSet<int> _noSubstitution = [];

    /// <summary>ported from: src/compiler/transformers/module/module.ts:218 — <c>currentSourceFile</c>.</summary>
    private SourceFile? _currentSourceFile;

    /// <summary>ported from: src/compiler/transformers/module/module.ts:219 — <c>currentModuleInfo</c>.</summary>
    private TransformationUtilities.IExternalModuleInfo? _currentModuleInfo;

    /// <summary>ported from: src/compiler/transformers/module/module.ts:220 — <c>importsAndRequiresToRewriteOrShim</c>.</summary>
    private List<CallExpression>? _importsAndRequiresToRewriteOrShim;

    /// <summary>ported from: src/compiler/transformers/module/module.ts:222 — <c>needUMDDynamicImportHelper</c>.</summary>
    private bool _needUMDDynamicImportHelper;

    /// <summary>ported from: src/compiler/transformers/module/module.ts:176-224 — the factory's prologue.</summary>
    public ModuleTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _host = context.GetEmitHost();
        _compilerOptions = context.GetCompilerOptions();
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
        _moduleKind = Utilities.GetEmitModuleKind(_compilerOptions);
        _liftToBlock = nodes => (Node)_factory.LiftToBlock(nodes as List<Node> ?? [.. nodes]);
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);

        var previousOnSubstituteNode = context.OnSubstituteNode;
        var previousOnEmitNode = context.OnEmitNode;
        _previousOnSubstituteNode = previousOnSubstituteNode!;
        _previousOnEmitNode = previousOnEmitNode!;
        context.OnSubstituteNode = OnSubstituteNode;
        context.OnEmitNode = OnEmitNode;

        context.EnableSubstitution(SyntaxKind.CallExpression); // Substitute calls to imported/exported symbols to avoid incorrect `this`.
        context.EnableSubstitution(SyntaxKind.TaggedTemplateExpression); // Substitute calls to imported/exported symbols to avoid incorrect `this`.
        context.EnableSubstitution(SyntaxKind.Identifier); // Substitutes expression identifiers with imported/exported symbols.
        context.EnableSubstitution(SyntaxKind.BinaryExpression); // Substitutes assignments to exported symbols.
        context.EnableSubstitution(SyntaxKind.ShorthandPropertyAssignment); // Substitutes shorthand property assignments for imported/exported symbols.
        context.EnableEmitNotification(SyntaxKind.SourceFile); // Restore state when substituting nodes in a file.
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:231-259 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:181-190 — <c>getTransformModuleDelegate</c>.</summary>
    /// <remarks>PORT-NOTE: TS's switch answers three distinct function values; the port's switch expression is the same
    /// three, and the default arm's two module kinds (CommonJS and everything that reaches this transformer) share
    /// <see cref="TransformCommonJSModule"/>.</remarks>
    private Func<SourceFile, SourceFile> GetTransformModuleDelegate(ModuleKind moduleKind)
        => moduleKind switch
        {
            ModuleKind.AMD => TransformAMDModule,
            ModuleKind.UMD => TransformUMDModule,
            _ => TransformCommonJSModule,
        };

    /// <summary>ported from: src/compiler/transformers/module/module.ts:231-259 — <c>transformSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.transformFlags &amp; TransformFlags.ContainsDynamicImport</c> is truthiness of a number,
    /// so the port states the <c>!= 0</c> test; <c>compilerOptions.outFile</c> is the nullable slot's truthiness.
    /// </remarks>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (
            node.IsDeclarationFile
            || !(Utilities.IsEffectiveExternalModule(node, _compilerOptions)
                || (node.TransformFlags & TransformFlags.ContainsDynamicImport) != 0
                || (Utilities.IsJsonSourceFile(node)
                    && Utilities.HasJsonModuleEmitEnabled(_compilerOptions)
                    && Js.IsTruthy(_compilerOptions.OutFile)))
        )
        {
            return node;
        }

        _currentSourceFile = node;
        _currentModuleInfo = TransformationUtilities.CollectExternalModuleInfo(_context, node);
        _moduleInfoMap[TransformationUtilities.GetOriginalNodeId(node)] = _currentModuleInfo;
        if (Js.IsTruthy(_compilerOptions.RewriteRelativeImportExtensions))
        {
            Utilities.ForEachDynamicImportOrRequireCall(
                node,
                /*includeTypeSpaceImports*/ false,
                /*requireStringLiteralLikeArgument*/ false,
                (call, _) =>
                {
                    var argument = ((CallExpression)call).Arguments![0];
                    if (!Utilities.IsStringLiteralLike(argument) || Utilities.ShouldRewriteModuleSpecifier(((StringLiteral)argument).Text!, _compilerOptions))
                    {
                        _importsAndRequiresToRewriteOrShim = (List<CallExpression>?)Append(_importsAndRequiresToRewriteOrShim, (CallExpression)call);
                    }
                });
        }

        // Perform the transformation.
        var transformModule = GetTransformModuleDelegate(_moduleKind);
        var updated = transformModule(node);
        _currentSourceFile = null;
        _currentModuleInfo = null;
        _needUMDDynamicImportHelper = false;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:261-269 — <c>shouldEmitUnderscoreUnderscoreESModule</c>.</summary>
    private bool ShouldEmitUnderscoreUnderscoreESModule()
    {
        var currentSourceFile = _currentSourceFile!;
        if (ModuleTransformSupport.HasJSFileExtension(currentSourceFile.FileName!)
            && currentSourceFile.CommonJsModuleIndicator is not null
            && (currentSourceFile.ExternalModuleIndicator is null || currentSourceFile.ExternalModuleIndicator is true))
        {
            return false;
        }
        if (_currentModuleInfo!.ExportEquals is null && Parser.IsExternalModule(currentSourceFile))
        {
            return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:276-316 — <c>transformCommonJSModule</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>exportedNames.slice(i, i + chunkSize)</c> is <see cref="Core.Slice{T}"/>, whose
    /// range clamps the way <c>Array.prototype.slice</c> does, and its <c>reduceLeft</c> is
    /// <see cref="Core.ReduceLeft{T,TU}(IList{T}?,Func{TU,T,int,TU},TU,int?,int?)"/> (the callback takes the index the
    /// reference's ignores).</remarks>
    private SourceFile TransformCommonJSModule(SourceFile node)
    {
        _context.StartLexicalEnvironment();

        var statements = new List<IStatement>();
        var ensureUseStrict = Utilities.GetAlwaysStrict(_compilerOptions) || Parser.IsExternalModule(_currentSourceFile!);
        var statementOffset = _factory.CopyPrologue(
            node.Statements,
            statements,
            ensureUseStrict && !Utilities.IsJsonSourceFile(node),
            TopLevelVisitor);

        if (ShouldEmitUnderscoreUnderscoreESModule())
        {
            statements.Add(CreateUnderscoreUnderscoreESModule());
        }
        if (Core.Some(_currentModuleInfo!.ExportedNames))
        {
            const int chunkSize = 50;
            var exportedNames = _currentModuleInfo.ExportedNames!;
            for (var i = 0; i < exportedNames.Count; i += chunkSize)
            {
                statements.Add(
                    _factory.CreateExpressionStatement(
                        Core.ReduceLeft(
                            Core.Slice(exportedNames, i, i + chunkSize),
                            (prev, nextId, _) =>
                                nextId.Kind == SyntaxKind.StringLiteral
                                    ? _factory.CreateAssignment(
                                        _factory.CreateElementAccessExpression(
                                            _factory.CreateIdentifier("exports", null, null),
                                            _factory.CreateStringLiteral(((StringLiteral)nextId).Text, null, null)),
                                        prev)
                                    : _factory.CreateAssignment(
                                        _factory.CreatePropertyAccessExpression(
                                            _factory.CreateIdentifier("exports", null, null),
                                            _factory.CreateIdentifier(Utilities.IdText(nextId), null, null)),
                                        prev),
                            (Expression)_factory.CreateVoidZero())));
            }
        }
        foreach (var f in _currentModuleInfo.ExportedFunctions)
        {
            AppendExportsOfHoistedDeclaration(statements, f);
        }

        Append(statements, (IStatement?)Utilities.VisitNode(_currentModuleInfo.ExternalHelpersImportDeclaration, TopLevelVisitor, Utilities.IsStatement));
        Core.AddRange(statements, Utilities.VisitNodes(node.Statements, TopLevelVisitor, Utilities.IsStatement, statementOffset));
        AddExportEqualsIfNeeded(statements, /*emitAsReturn*/ false);
        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());

        var updated = _factory.UpdateSourceFile(node, statements, null, null, null, null, null);
        EmitNodes.AddEmitHelpers(updated, _context.ReadEmitHelpers());
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:323-406 — <c>transformAMDModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isJsonSourceFile(node) &amp;&amp; node</c> is a <c>SourceFile | false | undefined</c> value used
    /// both as a test and as the file; the port's nullable local is that value. The nested ternary that picks the
    /// module-body argument reads <c>jsonSourceFile.statements.length ? statements[0].expression : …</c> with the JSON
    /// file's own expressions, which the port states as the two nested conditionals it is.
    /// </remarks>
    private SourceFile TransformAMDModule(SourceFile node)
    {
        var define = _factory.CreateIdentifier("define", null, null);
        var moduleName = ModuleTransformSupport.TryGetModuleNameFromFile(_factory, node, _host, _compilerOptions);
        var jsonSourceFile = Utilities.IsJsonSourceFile(node) ? node : null;

        // An AMD define function has the following shape:
        //
        //     define(id?, dependencies?, factory);
        //
        // This has the shape of the following:
        //
        //     define(name, ["module1", "module2"], function (module1Alias) { ... }
        //
        // The location of the alias in the parameter list in the factory function needs to
        // match the position of the module name in the dependency list.
        //
        // To ensure this is true in cases of modules with no aliases, e.g.:
        //
        //     import "module"
        //
        // or
        //
        //     /// <amd-dependency path= "a.css" />
        //
        // we need to add modules without alias names to the end of the dependencies list

        var dependencies = CollectAsynchronousDependencies(node, /*includeNonAmdDependencies*/ true);

        // Create an updated SourceFile:
        //
        //     define("module1", ["module2"], function ...
        var updated = _factory.UpdateSourceFile(
            node,
            [
                _factory.CreateExpressionStatement(
                    _factory.CreateCallExpression(
                        define,
                        /*typeArguments*/ null,
                        [
                            // Add the module name (if provided).
                            .. (moduleName is not null ? new List<Expression> { moduleName } : new List<Expression>()),

                            // Add the dependency array argument:
                            //
                            //     ["require", "exports", module1", "module2", ...]
                            _factory.CreateArrayLiteralExpression(
                                jsonSourceFile is not null
                                    ? new List<Expression>()
                                    : [
                                        _factory.CreateStringLiteral("require", null, null),
                                        _factory.CreateStringLiteral("exports", null, null),
                                        .. dependencies.AliasedModuleNames,
                                        .. dependencies.UnaliasedModuleNames,
                                    ],
                                /*multiLine*/ null),

                            // Add the module body function argument:
                            //
                            //     function (require, exports, module1, module2) ...
                            jsonSourceFile is not null
                                ? jsonSourceFile.Statements!.Count > 0
                                    ? (Expression)((ExpressionStatement)jsonSourceFile.Statements![0]).Expression!
                                    : _factory.CreateObjectLiteralExpression(null, null)
                                : _factory.CreateFunctionExpression(
                                    /*modifiers*/ null,
                                    /*asteriskToken*/ null,
                                    /*name*/ null,
                                    /*typeParameters*/ null,
                                    [
                                        _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, "require", null, null, null),
                                        _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, "exports", null, null, null),
                                        .. dependencies.ImportAliasNames,
                                    ],
                                    /*type*/ null,
                                    TransformAsynchronousModuleBody(node)),
                        ])),
            ],
            null,
            null,
            null,
            null,
            null);

        EmitNodes.AddEmitHelpers(updated, _context.ReadEmitHelpers());
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:413-546 — <c>transformUMDModule</c>.</summary>
    private SourceFile TransformUMDModule(SourceFile node)
    {
        var dependencies = CollectAsynchronousDependencies(node, /*includeNonAmdDependencies*/ false);
        var moduleName = ModuleTransformSupport.TryGetModuleNameFromFile(_factory, node, _host, _compilerOptions);
        var umdHeader = _factory.CreateFunctionExpression(
            /*modifiers*/ null,
            /*asteriskToken*/ null,
            /*name*/ null,
            /*typeParameters*/ null,
            [_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, "factory", null, null, null)],
            /*type*/ null,
            Utilities.SetTextRange(
                _factory.CreateBlock(
                    [
                        _factory.CreateIfStatement(
                            _factory.CreateLogicalAnd(
                                _factory.CreateTypeCheck(_factory.CreateIdentifier("module", null, null), "object"),
                                _factory.CreateTypeCheck(
                                    _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("module", null, null), "exports"),
                                    "object")),
                            _factory.CreateBlock(
                                [
                                    _factory.CreateVariableStatement(
                                        /*modifiers*/ null,
                                        new List<VariableDeclaration>
                                        {
                                            _factory.CreateVariableDeclaration(
                                                "v",
                                                /*exclamationToken*/ null,
                                                /*type*/ null,
                                                _factory.CreateCallExpression(
                                                    _factory.CreateIdentifier("factory", null, null),
                                                    /*typeArguments*/ null,
                                                    [
                                                        _factory.CreateIdentifier("require", null, null),
                                                        _factory.CreateIdentifier("exports", null, null),
                                                    ])),
                                        }),
                                    EmitNodes.SetEmitFlags(
                                        _factory.CreateIfStatement(
                                            _factory.CreateStrictInequality(
                                                _factory.CreateIdentifier("v", null, null),
                                                _factory.CreateIdentifier("undefined", null, null)),
                                            _factory.CreateExpressionStatement(
                                                _factory.CreateAssignment(
                                                    _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("module", null, null), "exports"),
                                                    _factory.CreateIdentifier("v", null, null))), /*elseStatement*/ null),
                                        EmitFlags.SingleLine),
                                ],
                                /*multiLine*/ null),
                            _factory.CreateIfStatement(
                                _factory.CreateLogicalAnd(
                                    _factory.CreateTypeCheck(_factory.CreateIdentifier("define", null, null), "function"),
                                    _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("define", null, null), "amd")),
                                _factory.CreateBlock(
                                    [
                                        _factory.CreateExpressionStatement(
                                            _factory.CreateCallExpression(
                                                _factory.CreateIdentifier("define", null, null),
                                                /*typeArguments*/ null,
                                                [
                                                    // Add the module name (if provided).
                                                    .. (moduleName is not null ? new List<Expression> { moduleName } : new List<Expression>()),
                                                    _factory.CreateArrayLiteralExpression(
                                                        [
                                                            _factory.CreateStringLiteral("require", null, null),
                                                            _factory.CreateStringLiteral("exports", null, null),
                                                            .. dependencies.AliasedModuleNames,
                                                            .. dependencies.UnaliasedModuleNames,
                                                        ],
                                                        /*multiLine*/ null),
                                                    _factory.CreateIdentifier("factory", null, null),
                                                ])),
                                    ],
                                    /*multiLine*/ null), /*elseStatement*/ null)),
                    ],
                    /*multiLine*/ true),
                /*location*/ null));

        // Create an updated SourceFile:
        //
        //  (function (factory) {
        //      if (typeof module === "object" && typeof module.exports === "object") {
        //          var v = factory(require, exports);
        //          if (v !== undefined) module.exports = v;
        //      }
        //      else if (typeof define === 'function' && define.amd) {
        //          define(["require", "exports"], factory);
        //      }
        //  })(function ...)

        var updated = _factory.UpdateSourceFile(
            node,
            [
                _factory.CreateExpressionStatement(
                    _factory.CreateCallExpression(
                        umdHeader,
                        /*typeArguments*/ null,
                        [
                            // Add the module body function argument:
                            //
                            //     function (require, exports) ...
                            _factory.CreateFunctionExpression(
                                /*modifiers*/ null,
                                /*asteriskToken*/ null,
                                /*name*/ null,
                                /*typeParameters*/ null,
                                [
                                    _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, "require", null, null, null),
                                    _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, "exports", null, null, null),
                                    .. dependencies.ImportAliasNames,
                                ],
                                /*type*/ null,
                                TransformAsynchronousModuleBody(node)),
                        ])),
            ],
            null,
            null,
            null,
            null,
            null);

        EmitNodes.AddEmitHelpers(updated, _context.ReadEmitHelpers());
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:554-601 — <c>collectAsynchronousDependencies</c>.</summary>
    /// <remarks>PORT-NOTE: <c>node.amdDependencies</c> is <see cref="SourceFile.AmdDependencies"/>; the reference's
    /// <c>setEmitFlags(importAliasName, EmitFlags.NoSubstitution)</c> is <see cref="EmitNodes.SetEmitFlags{T}"/>.</remarks>
    private AsynchronousDependencies CollectAsynchronousDependencies(SourceFile node, bool includeNonAmdDependencies)
    {
        // names of modules with corresponding parameter in the factory function
        var aliasedModuleNames = new List<Expression>();

        // names of modules with no corresponding parameters in factory function
        var unaliasedModuleNames = new List<Expression>();

        // names of the parameters in the factory function; these
        // parameters need to match the indexes of the corresponding
        // module names in aliasedModuleNames.
        var importAliasNames = new List<ParameterDeclaration>();

        // Fill in amd-dependency tags
        foreach (var amdDependency in node.AmdDependencies ?? [])
        {
            if (Js.IsTruthy(amdDependency.Name))
            {
                aliasedModuleNames.Add(_factory.CreateStringLiteral(amdDependency.Path, null, null));
                importAliasNames.Add(_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, amdDependency.Name, null, null, null));
            }
            else
            {
                unaliasedModuleNames.Add(_factory.CreateStringLiteral(amdDependency.Path, null, null));
            }
        }

        foreach (var importNode in _currentModuleInfo!.ExternalImports)
        {
            // Find the name of the external module
            var externalModuleName = ModuleTransformSupport.GetExternalModuleNameLiteral(
                _factory, importNode, _currentSourceFile!, _host, _resolver, _compilerOptions);

            // Find the name of the module alias, if there is one
            var importAliasName = ModuleTransformSupport.GetLocalNameForExternalImport(_factory, importNode, _currentSourceFile!);
            // It is possible that externalModuleName is undefined if it is not string literal.
            // This can happen in the invalid import syntax.
            // E.g : "import * from alias from 'someLib';"
            if (externalModuleName is not null)
            {
                if (includeNonAmdDependencies && importAliasName is not null)
                {
                    // Set emitFlags on the name of the classDeclaration
                    // This is so that when printer will not substitute the identifier
                    EmitNodes.SetEmitFlags(importAliasName, EmitFlags.NoSubstitution);
                    aliasedModuleNames.Add(externalModuleName);
                    importAliasNames.Add(_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, importAliasName, null, null, null));
                }
                else
                {
                    unaliasedModuleNames.Add(externalModuleName);
                }
            }
        }

        return new AsynchronousDependencies(aliasedModuleNames, unaliasedModuleNames, importAliasNames);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:603-613 — <c>getAMDImportExpressionForImport</c>.</summary>
    private ExpressionStatement? GetAMDImportExpressionForImport(Node node)
    {
        if (NodeTests.IsImportEqualsDeclaration(node)
            || NodeTests.IsExportDeclaration(node)
            || ModuleTransformSupport.GetExternalModuleNameLiteral(_factory, node, _currentSourceFile!, _host, _resolver, _compilerOptions) is null)
        {
            return null;
        }
        var name = ModuleTransformSupport.GetLocalNameForExternalImport(_factory, node, _currentSourceFile!)!; // TODO: GH#18217
        var expr = GetHelperExpressionForImport((ImportDeclaration)node, name);
        if (ReferenceEquals(expr, name))
        {
            return null;
        }
        return _factory.CreateExpressionStatement(_factory.CreateAssignment(name, expr));
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:620-662 — <c>transformAsynchronousModuleBody</c>.</summary>
    private Block TransformAsynchronousModuleBody(SourceFile node)
    {
        _context.StartLexicalEnvironment();

        var statements = new List<IStatement>();
        var statementOffset = _factory.CopyPrologue(node.Statements, statements, /*ensureUseStrict*/ true, TopLevelVisitor);

        if (ShouldEmitUnderscoreUnderscoreESModule())
        {
            statements.Add(CreateUnderscoreUnderscoreESModule());
        }
        if (Core.Some(_currentModuleInfo!.ExportedNames))
        {
            statements.Add(
                _factory.CreateExpressionStatement(
                    Core.ReduceLeft(
                        _currentModuleInfo.ExportedNames,
                        (prev, nextId, _) =>
                            nextId.Kind == SyntaxKind.StringLiteral
                                ? _factory.CreateAssignment(
                                    _factory.CreateElementAccessExpression(
                                        _factory.CreateIdentifier("exports", null, null),
                                        _factory.CreateStringLiteral(((StringLiteral)nextId).Text, null, null)),
                                    prev)
                                : _factory.CreateAssignment(
                                    _factory.CreatePropertyAccessExpression(
                                        _factory.CreateIdentifier("exports", null, null),
                                        _factory.CreateIdentifier(Utilities.IdText(nextId), null, null)),
                                    prev),
                        (Expression)_factory.CreateVoidZero())));
        }
        foreach (var f in _currentModuleInfo.ExportedFunctions)
        {
            AppendExportsOfHoistedDeclaration(statements, f);
        }

        // Visit each statement of the module body.
        Append(statements, (IStatement?)Utilities.VisitNode(_currentModuleInfo.ExternalHelpersImportDeclaration, TopLevelVisitor, Utilities.IsStatement));
        if (_moduleKind == ModuleKind.AMD)
        {
            Core.AddRange(statements, Core.MapDefined(_currentModuleInfo.ExternalImports, (importNode, _) => (IStatement?)GetAMDImportExpressionForImport(importNode)));
        }
        Core.AddRange(statements, Utilities.VisitNodes(node.Statements, TopLevelVisitor, Utilities.IsStatement, statementOffset));

        // Append the 'export =' statement if provided.
        AddExportEqualsIfNeeded(statements, /*emitAsReturn*/ true);

        // End the lexical environment for the module body
        // and merge any new lexical declarations.
        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());

        var body = _factory.CreateBlock(statements, /*multiLine*/ true);
        if (_needUMDDynamicImportHelper)
        {
            EmitNodes.AddEmitHelper(body, DynamicImportUMDHelper);
        }

        return body;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:672-699 — <c>addExportEqualsIfNeeded</c>.</summary>
    private void AddExportEqualsIfNeeded(List<IStatement> statements, bool emitAsReturn)
    {
        var exportEquals = _currentModuleInfo!.ExportEquals;
        if (exportEquals is not null)
        {
            var expressionResult = Utilities.VisitNode(exportEquals.Expression, Visitor, Utilities.IsExpression);
            if (expressionResult is not null)
            {
                if (emitAsReturn)
                {
                    var statement = _factory.CreateReturnStatement(expressionResult);
                    Utilities.SetTextRange(statement, exportEquals);
                    EmitNodes.SetEmitFlags(statement, EmitFlags.NoTokenSourceMaps | EmitFlags.NoComments);
                    statements.Add(statement);
                }
                else
                {
                    var statement = _factory.CreateExpressionStatement(
                        _factory.CreateAssignment(
                            _factory.CreatePropertyAccessExpression(
                                _factory.CreateIdentifier("module", null, null),
                                "exports"),
                            expressionResult));

                    Utilities.SetTextRange(statement, exportEquals);
                    EmitNodes.SetEmitFlags(statement, EmitFlags.NoComments);
                    statements.Add(statement);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2499-2504 — <c>dynamicImportUMDHelper</c>.</summary>
    /// <remarks>PORT-NOTE: TS's object literal is the port's <see cref="EmitHelper"/> constructible type; the text is the
    /// reference's template literal, leading newline and indentation included (the emitter writes it as it stands).</remarks>
    private static readonly EmitHelper DynamicImportUMDHelper = new(
        "typescript:dynamicimport-sync-require",
        /*scoped*/ true,
        @"
            var __syncRequire = typeof module === ""object"" && typeof module.exports === ""object"";");

    /// <summary>TS's <c>append</c> (<c>core.ts:920</c>): adds when the value is present, answers the list either way.
    /// </summary>
    /// <remarks>PORT-NOTE: TS's <c>append</c> is <see cref="Core.Append{T}"/>; this shim exists only to keep the
    /// reference's <c>statements = append(statements, x)</c> assignment shape without the port's
    /// <c>IList&lt;T&gt;?</c> answer widening the local's type.</remarks>
    private static List<T>? Append<T>(List<T>? list, T? value)
        where T : class
        => (List<T>?)Core.Append(list, value);
}
