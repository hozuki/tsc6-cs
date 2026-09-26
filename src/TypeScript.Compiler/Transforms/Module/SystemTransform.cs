#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/system.ts:1-2050 — the whole SystemJS module transform.
/// <summary>
/// ported from: src/compiler/transformers/module/system.ts:137-176 — <c>transformSystemModule</c>: the transformer that
/// rewrites a <see cref="SourceFile"/>'s module syntax into a <c>System.register([&hellip;], function (exports, context)
/// { &hellip; })</c> call, hoisting the file's declarations into the module-body function, building the <c>setters</c>
/// array from the dependency groups, and substituting imported identifiers and exported assignments at emit time.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformSystemModule</c>, whose body is a table of
/// closures over that function's locals; the port states the factory as
/// <see cref="SystemTransform.TransformSystemModule"/> and the closures as the members of
/// <see cref="SystemTransformer"/> — the same "closures → classes" treatment <c>ES2015Transform</c>,
/// <c>TypeScriptTransform</c> and the sibling <c>ModuleTransform</c> record for their modules. The reference's nested
/// <c>DependencyGroup</c> interface becomes <see cref="SystemTransformer.DependencyGroup"/>, a constructible class, as
/// <c>TransformationUtilities.ExternalModuleInfo</c> did for <c>ExternalModuleInfo</c>.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
/// <c>Func&lt;object?, object?&gt;</c>; the returned member is the reference's own
/// <c>chainBundle(context, transformSourceFile)</c> call, staying a method group so no delegate is allocated per call
/// beyond the one <see cref="TransformationUtilities.ChainBundle"/> stores.
/// </para>
/// <para>
/// PORT-NOTE (<c>factory</c> and the lexical-environment members): the reference destructures <c>factory</c>,
/// <c>startLexicalEnvironment</c>, <c>endLexicalEnvironment</c> and <c>hoistVariableDeclaration</c> out of the context;
/// the port keeps the context itself and reads the same members through it (their bodies are
/// <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs), so the sequence is the same. The primary-constructor
/// spelling the assignment suggests is not available for these members (they are read as
/// <c>_context.&hellip;</c>), so the context is an ordinary field.
/// </para>
/// </remarks>
internal static class SystemTransform
{
    /// <summary>ported from: src/compiler/transformers/module/system.ts:137-176 — <c>transformSystemModule</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>chainBundle(context, transformSourceFile)</c> is built in
    /// <see cref="SystemTransformer"/>'s constructor and this member answers its bundle-aware face, so no delegate is
    /// allocated per call beyond the one <see cref="TransformationUtilities.ChainBundle"/> stores (the sibling
    /// <c>ModuleTransform.TransformModule</c> states the same).</remarks>
    public static Func<object?, object?> TransformSystemModule(ITransformerContext context)
        => new SystemTransformer(context).TransformSourceFileOrBundle;
}

/// <summary>ported from: src/compiler/transformers/module/system.ts:137-2050 — the body of <c>transformSystemModule</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the state): the reference's <c>currentSourceFile</c>, <c>moduleInfo</c>, <c>exportFunction</c>,
/// <c>contextObject</c>, <c>hoistedStatements</c>, <c>enclosingBlockScopedContainer</c> and <c>noSubstitution</c> are
/// the factory function's locals that the closures mutate; they are this class's fields.
/// </para>
/// <para>
/// PORT-NOTE (the four per-file maps): TS's <c>moduleInfoMap</c>, <c>exportFunctionsMap</c>,
/// <c>noSubstitutionMap</c> and <c>contextObjectMap</c> are <em>sparse</em> hole-filled arrays indexed by
/// <c>getOriginalNodeId(file)</c>; the port's four <see cref="Dictionary{TKey,TValue}"/>s keep the same key with no
/// holes, which is the treatment the sibling <c>ModuleTransformer</c> gives its own <c>moduleInfoMap</c> (its
/// <c>ExternalModuleInfo[]</c> becomes <c>Dictionary&lt;int, IExternalModuleInfo&gt;</c>). The reads of a missing entry
/// (<c>moduleInfoMap[id]</c> in <c>onEmitNode</c>) answer <c>default</c>, which is the reference's <c>undefined</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>noSubstitutionMap</c>'s element and <c>noSubstitution</c>): TS types them <c>boolean[]</c> and reads
/// them as <c>noSubstitution &amp;&amp; node.id &amp;&amp; noSubstitution[node.id]</c> — a set membership test with a
/// zero-id guard; the port's <see cref="HashSet{T}"/> of ids is that same test (a node id is never 0,
/// <c>utilities.ts:getNodeId</c>), and the per-file map holds the same set under the file's original node id.
/// </para>
/// </remarks>
internal sealed partial class SystemTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitResolver _resolver;
    private readonly IEmitHost _host;
    private readonly ICompilerOptions _compilerOptions;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:176 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1426 / 1444 / 1458 / 1431 — <c>factory.liftToBlock</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <c>visitNode</c> lift slot is <c>Func&lt;IReadOnlyList&lt;Node&gt;, Node?&gt;</c>
    /// while <c>NodeFactory.LiftToBlock</c> takes a <c>List&lt;Node&gt;</c> and answers an <c>IStatement</c>; this is that
    /// adapter, built once (the same one <c>ES2015Transformer</c> and <c>ModuleTransformer</c> keep).</remarks>
    private readonly Func<IReadOnlyList<Node>, Node?> _liftToBlock;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:163 — <c>moduleInfoMap</c>.</summary>
    private readonly Dictionary<int, TransformationUtilities.IExternalModuleInfo> _moduleInfoMap = [];

    /// <summary>ported from: src/compiler/transformers/module/system.ts:164 — <c>exportFunctionsMap</c>.</summary>
    private readonly Dictionary<int, Identifier> _exportFunctionsMap = [];

    /// <summary>ported from: src/compiler/transformers/module/system.ts:165 — <c>noSubstitutionMap</c>.</summary>
    private readonly Dictionary<int, HashSet<int>> _noSubstitutionMap = [];

    /// <summary>ported from: src/compiler/transformers/module/system.ts:166 — <c>contextObjectMap</c>.</summary>
    private readonly Dictionary<int, Identifier> _contextObjectMap = [];

    /// <summary>ported from: src/compiler/transformers/module/system.ts:168 — <c>currentSourceFile</c>.</summary>
    private SourceFile? _currentSourceFile;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:169 — <c>moduleInfo</c>.</summary>
    private TransformationUtilities.IExternalModuleInfo? _moduleInfo;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:170 — <c>exportFunction</c>.</summary>
    private Identifier? _exportFunction;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:171 — <c>contextObject</c>.</summary>
    private Identifier? _contextObject;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:172 — <c>hoistedStatements</c>.</summary>
    private List<IStatement>? _hoistedStatements;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:173 — <c>enclosingBlockScopedContainer</c>.</summary>
    private Node? _enclosingBlockScopedContainer;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:174 — <c>noSubstitution</c>.</summary>
    private HashSet<int>? _noSubstitution;

    /// <summary>ported from: src/compiler/transformers/module/system.ts:143-161 — the factory's prologue.</summary>
    public SystemTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _resolver = context.GetEmitResolver();
        _host = context.GetEmitHost();
        _compilerOptions = context.GetCompilerOptions();
        _liftToBlock = nodes => (Node)_factory.LiftToBlock(nodes as List<Node> ?? [.. nodes]);
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);

        var previousOnSubstituteNode = context.OnSubstituteNode;
        var previousOnEmitNode = context.OnEmitNode;
        _previousOnSubstituteNode = previousOnSubstituteNode!;
        _previousOnEmitNode = previousOnEmitNode!;
        context.OnSubstituteNode = OnSubstituteNode;
        context.OnEmitNode = OnEmitNode;

        context.EnableSubstitution(SyntaxKind.Identifier); // Substitutes expression identifiers for imported symbols.
        context.EnableSubstitution(SyntaxKind.ShorthandPropertyAssignment); // Substitutes expression identifiers for imported symbols
        context.EnableSubstitution(SyntaxKind.BinaryExpression); // Substitutes assignments to exported symbols.
        context.EnableSubstitution(SyntaxKind.MetaProperty); // Substitutes 'import.meta'
        context.EnableEmitNotification(SyntaxKind.SourceFile); // Restore state when substituting nodes in a file.
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:176 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    // ported from: src/compiler/transformers/module/system.ts:138-141 — the nested `DependencyGroup` interface.
    /// <summary>ported from: src/compiler/transformers/module/system.ts:138-141 — <c>DependencyGroup</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares an interface and fills it in with an object literal in
    /// <c>collectDependencyGroups</c>; the port follows <c>TransformationUtilities.ExternalModuleInfo</c> and states the
    /// constructible class. The reference nests it inside <c>transformSystemModule</c>, which is what the port's nesting
    /// in this class — the closures' home — is.</remarks>
    private sealed class DependencyGroup(StringLiteral name, List<Node> externalImports)
    {
        /// <summary>The name of the dependency group.</summary>
        public StringLiteral Name { get; } = name;

        /// <summary>The imports the group aggregates.</summary>
        public List<Node> ExternalImports { get; } = externalImports;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:183-272 — <c>transformSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.transformFlags &amp; TransformFlags.ContainsDynamicImport</c> is truthiness of a number,
    /// so the port states the <c>!= 0</c> test; <c>compilerOptions.outFile</c> is the nullable slot's truthiness.
    /// </remarks>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (
            node.IsDeclarationFile
            || !(Utilities.IsEffectiveExternalModule(node, _compilerOptions)
                || (node.TransformFlags & TransformFlags.ContainsDynamicImport) != 0)
        )
        {
            return node;
        }

        var id = TransformationUtilities.GetOriginalNodeId(node);
        _currentSourceFile = node;
        _enclosingBlockScopedContainer = node;

        // System modules have the following shape:
        //
        //     System.register(['dep-1', ... 'dep-n'], function(exports) {/* module body function */})
        //
        // The parameter 'exports' here is a callback '<T>(name: string, value: T) => T' that
        // is used to publish exported values. 'exports' returns its 'value' argument so in
        // most cases expressions that mutate exported values can be rewritten as:
        //
        //     expr -> exports('name', expr)
        //
        // The only exception in this rule is postfix unary operators,
        // see comment to 'substitutePostfixUnaryExpression' for more details

        // Collect information about the external module and dependency groups.
        _moduleInfo = _moduleInfoMap[id] = TransformationUtilities.CollectExternalModuleInfo(_context, node);

        // Make sure that the name of the 'exports' function does not conflict with
        // existing identifiers.
        _exportFunction = _exportFunctionsMap[id] = _factory.CreateUniqueName("exports", null);
        _contextObject = _contextObjectMap[id] = _factory.CreateUniqueName("context", null);

        // Add the body of the module.
        var dependencyGroups = CollectDependencyGroups(_moduleInfo.ExternalImports);
        var moduleBodyBlock = CreateSystemModuleBody(node, dependencyGroups);
        var moduleBodyFunction = _factory.CreateFunctionExpression(
            /*modifiers*/ null,
            /*asteriskToken*/ null,
            /*name*/ null,
            /*typeParameters*/ null,
            [
                _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, _exportFunction, null, null, null),
                _factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, _contextObject, null, null, null),
            ],
            /*type*/ null,
            moduleBodyBlock);

        // Write the call to `System.register`
        // Clear the emit-helpers flag for later passes since we'll have already used it in the module body
        // So the helper will be emit at the correct position instead of at the top of the source-file
        var moduleName = ModuleTransformSupport.TryGetModuleNameFromFile(_factory, node, _host, _compilerOptions);
        var dependencies = _factory.CreateArrayLiteralExpression(
            Core.Map(dependencyGroups, (dependencyGroup, _) => (Expression)dependencyGroup.Name),
            /*multiLine*/ null);
        var updated = EmitNodes.SetEmitFlags(
            _factory.UpdateSourceFile(
                node,
                Utilities.SetTextRange(
                    _factory.CreateNodeArray<IStatement>(
                        [
                            _factory.CreateExpressionStatement(
                                _factory.CreateCallExpression(
                                    _factory.CreatePropertyAccessExpression(
                                        _factory.CreateIdentifier("System", null, null),
                                        "register"),
                                    /*typeArguments*/ null,
                                    moduleName is not null
                                        ? [moduleName, dependencies, moduleBodyFunction]
                                        : [dependencies, moduleBodyFunction])),
                        ],
                        /*hasTrailingComma*/ null),
                    node.Statements),
                null,
                null,
                null,
                null,
                null),
            EmitFlags.NoTrailingComments);

        if (!Js.IsTruthy(_compilerOptions.OutFile))
        {
            EmitNodes.MoveEmitHelpers(updated, moduleBodyBlock, helper => !((IEmitHelperBase)helper).Scoped);
        }

        if (_noSubstitution is not null)
        {
            _noSubstitutionMap[id] = _noSubstitution;
            _noSubstitution = null;
        }

        _currentSourceFile = null;
        _moduleInfo = null;
        _exportFunction = null;
        _contextObject = null;
        _hoistedStatements = null;
        _enclosingBlockScopedContainer = null;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:279-302 — <c>collectDependencyGroups</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>groupIndices</c> is a <c>Map&lt;string, number&gt;</c> whose <c>get</c> answers
    /// <c>number | undefined</c>; the port's <c>TryGetValue</c> is that test. A <c>StringLiteral</c>'s
    /// <see cref="StringLiteral.Text"/> is nullable on this model while a <c>Map</c> key may be <c>undefined</c> in TS
    /// (a key lookup can never be null), so the dictionary key is the reference's <c>text</c> with <c>?? ""</c> stated.
    /// </remarks>
    private List<DependencyGroup> CollectDependencyGroups(List<Node> externalImports)
    {
        var groupIndices = new Dictionary<string, int>();
        var dependencyGroups = new List<DependencyGroup>();
        foreach (var externalImport in externalImports)
        {
            var externalModuleName = ModuleTransformSupport.GetExternalModuleNameLiteral(
                _factory, externalImport, _currentSourceFile!, _host, _resolver, _compilerOptions);
            if (externalModuleName is not null)
            {
                var text = externalModuleName.Text ?? "";
                if (groupIndices.TryGetValue(text, out var groupIndex))
                {
                    // deduplicate/group entries in dependency list by the dependency name
                    dependencyGroups[groupIndex].ExternalImports.Add(externalImport);
                }
                else
                {
                    groupIndices[text] = dependencyGroups.Count;
                    dependencyGroups.Add(new DependencyGroup(externalModuleName, [externalImport]));
                }
            }
        }

        return dependencyGroups;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:310-421 — <c>createSystemModuleBody</c>.</summary>
    private Block CreateSystemModuleBody(SourceFile node, List<DependencyGroup> dependencyGroups)
    {
        // Shape of the body in system modules:
        //
        //  function (exports) {
        //      <list of local aliases for imports>
        //      <hoisted variable declarations>
        //      <hoisted function declarations>
        //      return {
        //          setters: [
        //              <list of setter function for imports>
        //          ],
        //          execute: function() {
        //              <module statements>
        //          }
        //      }
        //      <temp declarations>
        //  }
        //
        // i.e:
        //
        //   import {x} from 'file1'
        //   var y = 1;
        //   export function foo() { return y + x(); }
        //   console.log(y);
        //
        // Will be transformed to:
        //
        //  function(exports) {
        //      function foo() { return y + file_1.x(); }
        //      exports("foo", foo);
        //      var file_1, y;
        //      return {
        //          setters: [
        //              function(v) { file_1 = v }
        //          ],
        //          execute(): function() {
        //              y = 1;
        //              console.log(y);
        //          }
        //      };
        //  }

        var statements = new List<IStatement>();

        // We start a new lexical environment in this function body, but *not* in the
        // body of the execute function. This allows us to emit temporary declarations
        // only in the outer module body and not in the inner one.
        _context.StartLexicalEnvironment();

        // Add any prologue directives.
        // PORT-NOTE: TS's <c>isExternalModule</c> (<c>utilitiesPublic.ts:1591</c> family) is <see cref="Parser.IsExternalModule"/>
        // in the port — <c>utilities.ts</c>/<c>Utilities</c> carries no copy — which is the same
        // <c>externalModuleIndicator</c>-non-null test.
        var ensureUseStrict = Utilities.GetAlwaysStrict(_compilerOptions) || Parser.IsExternalModule(_currentSourceFile!);
        var statementOffset = _factory.CopyPrologue(node.Statements, statements, ensureUseStrict, TopLevelVisitor);

        // var __moduleName = context_1 && context_1.id;
        statements.Add(
            _factory.CreateVariableStatement(
                /*modifiers*/ null,
                _factory.CreateVariableDeclarationList([
                    _factory.CreateVariableDeclaration(
                        "__moduleName",
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        _factory.CreateLogicalAnd(
                            _contextObject,
                            _factory.CreatePropertyAccessExpression(_contextObject, "id"))),
                ], null)));

        // Visit the synthetic external helpers import declaration if present
        Utilities.VisitNode(_moduleInfo!.ExternalHelpersImportDeclaration, TopLevelVisitor, Utilities.IsStatement);

        // Visit the statements of the source file, emitting any transformations into
        // the `executeStatements` array. We do this *before* we fill the `setters` array
        // as we both emit transformations as well as aggregate some data used when creating
        // setters. This allows us to reduce the number of times we need to loop through the
        // statements of the source file.
        var executeStatements = Utilities.VisitNodes(node.Statements, TopLevelVisitor, Utilities.IsStatement, statementOffset);

        // Emit early exports for function declarations.
        Core.AddRange(statements, _hoistedStatements);

        // We emit hoisted variables early to align roughly with our previous emit output.
        // Two key differences in this approach are:
        // - Temporary variables will appear at the top rather than at the bottom of the file
        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());

        var exportStarFunction = AddExportStarIfNeeded(statements)!; // TODO: GH#18217
        var modifiers = (node.TransformFlags & TransformFlags.ContainsAwait) != 0
            ? _factory.CreateModifiersFromModifierFlags(ModifierFlags.Async)
            : null;
        var moduleObject = _factory.CreateObjectLiteralExpression([
            _factory.CreatePropertyAssignment("setters", CreateSettersArray(exportStarFunction, dependencyGroups)),
            _factory.CreatePropertyAssignment(
                "execute",
                _factory.CreateFunctionExpression(
                    modifiers,
                    /*asteriskToken*/ null,
                    /*name*/ null,
                    /*typeParameters*/ null,
                    /*parameters*/ [],
                    /*type*/ null,
                    _factory.CreateBlock(executeStatements, /*multiLine*/ true))),
        ], /*multiLine*/ true);

        statements.Add(_factory.CreateReturnStatement(moduleObject));
        return _factory.CreateBlock(statements, /*multiLine*/ true);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:428-508 — <c>addExportStarIfNeeded</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>some(moduleInfo.exportedNames)</c> is <see cref="Core.Some{T}(IList{T}?,Func{T,bool}?)"/>
    /// with the absent predicate, and <c>exportedNames: ObjectLiteralElementLike[]</c> is the port's
    /// <c>List&lt;Node&gt;</c> (the object-literal element union has no C# spelling; every element pushed is a
    /// <c>PropertyAssignment</c>).</remarks>
    private Identifier? AddExportStarIfNeeded(List<IStatement> statements)
    {
        if (!_moduleInfo!.HasExportStarsToExportValues)
        {
            return null;
        }

        // when resolving exports local exported entries/indirect exported entries in the module
        // should always win over entries with similar names that were added via star exports
        // to support this we store names of local/indirect exported entries in a set.
        // this set is used to filter names brought by star expors.

        // local names set should only be added if we have anything exported
        if (!Core.Some(_moduleInfo.ExportedNames) && _moduleInfo.ExportedFunctions.Count == 0 && _moduleInfo.ExportSpecifiers.Count == 0)
        {
            // no exported declarations (export var ...) or export specifiers (export {x})
            // check if we have any non star export declarations.
            var hasExportDeclarationWithExportClause = false;
            foreach (var externalImport in _moduleInfo.ExternalImports)
            {
                if (externalImport.Kind == SyntaxKind.ExportDeclaration && ((ExportDeclaration)externalImport).ExportClause is not null)
                {
                    hasExportDeclarationWithExportClause = true;
                    break;
                }
            }

            if (!hasExportDeclarationWithExportClause)
            {
                // we still need to emit exportStar helper
                var exportStarFunction = CreateExportStarFunction(/*localNames*/ null);
                statements.Add(exportStarFunction);
                return exportStarFunction.Name;
            }
        }

        var exportedNames = new List<Node>();
        if (_moduleInfo.ExportedNames is not null)
        {
            foreach (var exportedLocalName in _moduleInfo.ExportedNames)
            {
                if (Utilities.ModuleExportNameIsDefault(exportedLocalName))
                {
                    continue;
                }

                // write name of exported declaration, i.e 'export var x...'
                exportedNames.Add(
                    _factory.CreatePropertyAssignment(
                        _factory.CreateStringLiteralFromNode(exportedLocalName),
                        _factory.CreateTrue()));
            }
        }

        foreach (var f in _moduleInfo.ExportedFunctions)
        {
            if (Utilities.HasSyntacticModifier(f, ModifierFlags.Default))
            {
                continue;
            }
            Debug.Assert(f.Name is not null);

            // write name of exported declaration, i.e 'export var x...'
            exportedNames.Add(
                _factory.CreatePropertyAssignment(
                    _factory.CreateStringLiteralFromNode(f.Name),
                    _factory.CreateTrue()));
        }

        var exportedNamesStorageRef = _factory.CreateUniqueName("exportedNames", null);
        statements.Add(
            _factory.CreateVariableStatement(
                /*modifiers*/ null,
                _factory.CreateVariableDeclarationList([
                    _factory.CreateVariableDeclaration(
                        exportedNamesStorageRef,
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        _factory.CreateObjectLiteralExpression(exportedNames, /*multiLine*/ true)),
                ], null)));

        var exportStarFunction2 = CreateExportStarFunction(exportedNamesStorageRef);
        statements.Add(exportStarFunction2);
        return exportStarFunction2.Name;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:517-584 — <c>createExportStarFunction</c>.</summary>
    private FunctionDeclaration CreateExportStarFunction(Identifier? localNames)
    {
        var exportStarFunction = _factory.CreateUniqueName("exportStar", null);
        var m = _factory.CreateIdentifier("m", null, null);
        var n = _factory.CreateIdentifier("n", null, null);
        var exports = _factory.CreateIdentifier("exports", null, null);
        Expression condition = _factory.CreateStrictInequality(n, _factory.CreateStringLiteral("default", null, null));
        if (localNames is not null)
        {
            condition = _factory.CreateLogicalAnd(
                condition,
                _factory.CreateLogicalNot(
                    _factory.CreateCallExpression(
                        _factory.CreatePropertyAccessExpression(localNames, "hasOwnProperty"),
                        /*typeArguments*/ null,
                        [n])));
        }

        return _factory.CreateFunctionDeclaration(
            /*modifiers*/ null,
            /*asteriskToken*/ null,
            exportStarFunction,
            /*typeParameters*/ null,
            [_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, m, null, null, null)],
            /*type*/ null,
            _factory.CreateBlock([
                _factory.CreateVariableStatement(
                    /*modifiers*/ null,
                    _factory.CreateVariableDeclarationList([
                        _factory.CreateVariableDeclaration(
                            exports,
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            _factory.CreateObjectLiteralExpression([], null)),
                    ], null)),
                _factory.CreateForInStatement(
                    _factory.CreateVariableDeclarationList([
                        _factory.CreateVariableDeclaration(n, null, null, null),
                    ], null),
                    m,
                    _factory.CreateBlock([
                        EmitNodes.SetEmitFlags(
                            _factory.CreateIfStatement(
                                condition,
                                _factory.CreateExpressionStatement(
                                    _factory.CreateAssignment(
                                        _factory.CreateElementAccessExpression(exports, n),
                                        _factory.CreateElementAccessExpression(m, n))),
                                /*elseStatement*/ null),
                            EmitFlags.SingleLine),
                    ], null)),
                _factory.CreateExpressionStatement(
                    _factory.CreateCallExpression(
                        _exportFunction!,
                        /*typeArguments*/ null,
                        [exports])),
            ], /*multiLine*/ true));
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:592-718 — <c>createSettersArray</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the fall-through): the reference's <c>case ImportDeclaration</c> ends with
    /// <c>if (!entry.importClause) break; // falls through</c> into <c>case ImportEqualsDeclaration</c>; the port's two
    /// labels share one section with that guard stated first, which is the same control flow.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>e.propertyName || e.name</c>): the two slots have different model types
    /// (<c>MemberExpression?</c> and <c>Identifier?</c>), so the port states the reference's node-or-fallback as
    /// <c>(Node?)e.PropertyName ?? e.Name</c> — a node is never falsy, so the reference's test is the "is there one" test.
    /// </para>
    /// </remarks>
    private ArrayLiteralExpression CreateSettersArray(Identifier exportStarFunction, List<DependencyGroup> dependencyGroups)
    {
        var setters = new List<Expression>();
        foreach (var group in dependencyGroups)
        {
            // derive a unique name for parameter from the first named entry in the group
            var localName = Core.ForEach(
                group.ExternalImports,
                (i, _) => ModuleTransformSupport.GetLocalNameForExternalImport(_factory, i, _currentSourceFile!));
            var parameterName = localName is not null ? _factory.GetGeneratedNameForNode(localName, null) : _factory.CreateUniqueName("", null);
            var statements = new List<IStatement>();
            foreach (var entry in group.ExternalImports)
            {
                var importVariableName = ModuleTransformSupport.GetLocalNameForExternalImport(_factory, entry, _currentSourceFile!)!; // TODO: GH#18217
                switch (entry.Kind)
                {
                    case SyntaxKind.ImportDeclaration:
                    case SyntaxKind.ImportEqualsDeclaration:
                        if (entry.Kind == SyntaxKind.ImportDeclaration && ((ImportDeclaration)entry).ImportClause is null)
                        {
                            // 'import "..."' case
                            // module is imported only for side-effects, no emit required
                            break;
                        }

                        Debug.Assert(importVariableName is not null);
                        // save import into the local
                        statements.Add(
                            _factory.CreateExpressionStatement(
                                _factory.CreateAssignment(importVariableName, parameterName)));
                        if (Utilities.HasSyntacticModifier(entry, ModifierFlags.Export))
                        {
                            statements.Add(
                                _factory.CreateExpressionStatement(
                                    _factory.CreateCallExpression(
                                        _exportFunction!,
                                        /*typeArguments*/ null,
                                        [
                                            _factory.CreateStringLiteral(Utilities.IdText(importVariableName), null, null),
                                            parameterName,
                                        ])));
                        }
                        break;

                    case SyntaxKind.ExportDeclaration:
                        Debug.Assert(importVariableName is not null);
                        if (((ExportDeclaration)entry).ExportClause is { } exportClause)
                        {
                            if (NodeTests.IsNamedExports(exportClause))
                            {
                                //  export {a, b as c} from 'foo'
                                //
                                // emit as:
                                //
                                //  exports_({
                                //     "a": _["a"],
                                //     "c": _["b"]
                                //  });
                                var properties = new List<Node>();
                                foreach (var e in ((NamedExports)exportClause).Elements!)
                                {
                                    properties.Add(
                                        _factory.CreatePropertyAssignment(
                                            _factory.CreateStringLiteral(Utilities.ModuleExportNameTextUnescaped(e.Name), null, null),
                                            _factory.CreateElementAccessExpression(
                                                parameterName,
                                                _factory.CreateStringLiteral(
                                                    Utilities.ModuleExportNameTextUnescaped((Node?)e.PropertyName ?? e.Name),
                                                    null,
                                                    null))));
                                }

                                statements.Add(
                                    _factory.CreateExpressionStatement(
                                        _factory.CreateCallExpression(
                                            _exportFunction!,
                                            /*typeArguments*/ null,
                                            [_factory.CreateObjectLiteralExpression(properties, /*multiLine*/ true)])));
                            }
                            else
                            {
                                statements.Add(
                                    _factory.CreateExpressionStatement(
                                        _factory.CreateCallExpression(
                                            _exportFunction!,
                                            /*typeArguments*/ null,
                                            [
                                                _factory.CreateStringLiteral(
                                                    Utilities.ModuleExportNameTextUnescaped(((NamespaceExport)exportClause).Name),
                                                    null,
                                                    null),
                                                parameterName,
                                            ])));
                            }
                        }
                        else
                        {
                            //  export * from 'foo'
                            //
                            // emit as:
                            //
                            //  exportStar(foo_1_1);
                            statements.Add(
                                _factory.CreateExpressionStatement(
                                    _factory.CreateCallExpression(
                                        exportStarFunction,
                                        /*typeArguments*/ null,
                                        [parameterName])));
                        }
                        break;
                }
            }

            setters.Add(
                _factory.CreateFunctionExpression(
                    /*modifiers*/ null,
                    /*asteriskToken*/ null,
                    /*name*/ null,
                    /*typeParameters*/ null,
                    [_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, parameterName, null, null, null)],
                    /*type*/ null,
                    _factory.CreateBlock(statements, /*multiLine*/ true)));
        }

        return _factory.CreateArrayLiteralExpression(setters, /*multiLine*/ true);
    }

    /// <summary>TS's <c>append</c> (<c>core.ts:920</c>): adds when the value is present, answers the list either way.
    /// </summary>
    /// <remarks>PORT-NOTE: TS's <c>append</c> is <see cref="Core.Append{T}"/>; this shim exists only to keep the
    /// reference's <c>statements = append(statements, x)</c> assignment shape without the port's
    /// <c>IList&lt;T&gt;?</c> answer widening the local's type (the same shim <c>ModuleTransformer</c> keeps).</remarks>
    private static List<T>? Append<T>(List<T>? list, T? value)
        where T : class
        => (List<T>?)Core.Append(list, value);

    /// <summary>TS's <c>addRange</c> (<c>core.ts:941</c>): appends the second list's elements when it is present, and
    /// answers the first list either way.</summary>
    /// <remarks>PORT-NOTE: <c>Core.AddRange</c> is generic in one invariant element type, while the reference's
    /// <c>addRange(exportedNames, moduleInfo?.exportedBindings[id])</c> appends an <c>Identifier</c> array to the
    /// <c>Identifier | StringLiteral</c> array its own inference settles on (system.ts:1996-1998 and its shadowed inner
    /// arm). The port's covariant reader (<c>IEnumerable&lt;Node&gt;</c>) needs this shim to state the same append against
    /// the <c>List&lt;Node&gt;</c> those slots erase to.</remarks>
    private static List<T>? AddRange<T>(List<T>? to, IEnumerable<T>? from)
        where T : class
    {
        if (from is null)
        {
            return to;
        }
        to ??= [];
        to.AddRange(from);
        return to;
    }
}
