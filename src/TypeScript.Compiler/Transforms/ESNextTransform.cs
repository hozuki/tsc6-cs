#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/esnext.ts:1-800 — the whole ESNext downlevel transform
/// <summary>
/// ported from: src/compiler/transformers/esnext.ts:71-790 — <c>transformESNext</c>: the transformer that rewrites the
/// one ESNext syntax a <see cref="SourceFile"/> may contain — the explicit resource management declarations
/// (<c>using</c> and <c>await using</c>) — into the <c>try</c>/<c>catch</c>/<c>finally</c> environment block the
/// <c>__addDisposableResource</c>/<c>__disposeResources</c> helpers drive.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformESNext</c>, from
/// <c>transformers/esnext.ts</c>; its body is a table of closures over the factory's locals, so the port states the
/// factory as <see cref="ESNextTransform.TransformESNext"/> and the closures as the members of
/// <see cref="ESNextTransformer"/> — the same "closures → classes" treatment <c>ES2015Transform</c> records.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is
/// <c>(node: SourceFile | Bundle) =&gt; SourceFile | Bundle</c>; the port's transformer list spells its slots
/// <c>Func&lt;object?, object?&gt;</c>, which is the erased shape the returned <c>chainBundle</c> call is stored as.
/// </para>
/// <para>
/// PORT-NOTE (<c>transformNamedEvaluation</c>/<c>isNamedEvaluation</c>): the reference imports both from
/// <c>namedEvaluation.ts</c>/<c>utilities.ts</c>; the port's landings are <c>NamedEvaluation.TransformNamedEvaluation</c>
/// and <c>NamedEvaluation.IsNamedEvaluation</c>, which this file calls qualified.
/// </para>
/// </remarks>
internal static class ESNextTransform
{
    /// <summary>ported from: src/compiler/transformers/esnext.ts:71-90 — <c>transformESNext</c>.</summary>
    /// <remarks>PORT-NOTE: the returned member is the reference's own <c>chainBundle(context, transformSourceFile)</c>
    /// call, staying a method group so no delegate is allocated per call beyond the one
    /// <see cref="TransformationUtilities.ChainBundle"/> stores.</remarks>
    public static Func<object?, object?> TransformESNext(ITransformerContext context)
        => new ESNextTransformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/esnext.ts:64-68 — the const enum `UsingKind`.
/// <summary>ported from: src/compiler/transformers/esnext.ts:64-68 — <c>UsingKind</c>.</summary>
/// <remarks>PORT-NOTE: the reference declares this const enum just above <c>transformESNext</c>; the port states it
/// beside the transform with the same members in the same order, and spells the three implicit values out because
/// <see cref="ESNextTransformer.GetUsingKindOfStatements"/> orders two of them numerically (C# does not order enum
/// values with <c>&gt;</c>).</remarks>
internal enum UsingKind
{
    /// <summary><c>UsingKind.None</c>.</summary>
    None = 0,

    /// <summary><c>UsingKind.Sync</c>.</summary>
    Sync = 1,

    /// <summary><c>UsingKind.Async</c>.</summary>
    Async = 2,
}

// ported from: src/compiler/transformers/esnext.ts:71-790 — the `transformESNext` factory's closures
/// <summary>
/// ported from: src/compiler/transformers/esnext.ts:71-790 — the closures <c>transformESNext</c> builds: its
/// <c>transformSourceFile</c>, <c>visitor</c>, the four statement rewriters (<c>visitSourceFile</c>,
/// <c>visitBlock</c>, <c>visitForStatement</c>, <c>visitForOfStatement</c>), the two hoisting helpers that walk a
/// <c>using</c> body (<c>transformUsingDeclarations</c>, <c>createDownlevelUsingStatements</c>) and the six
/// <c>hoist*</c> members that lift a top-level import/export/declaration out of the generated <c>try</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the state): the reference's <c>exportBindings</c>, <c>exportVars</c>, <c>defaultExportBinding</c> and
/// <c>exportEqualsBinding</c> are the factory function's locals that the closures mutate; they are this class's fields.
/// </para>
/// <para>
/// PORT-NOTE (<c>factory</c>/<c>emitHelpers</c> and the three lexical-environment members): the reference destructures
/// them out of the context; the port keeps the context and reads the same members through it (their bodies are
/// <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs), so the sequence is the same.
/// </para>
/// <para>
/// PORT-NOTE (the visitor slot): TS's <c>visitor</c> is the <c>Visitor</c> function type, which the port spells
/// <c>Func&lt;Node, object?&gt;</c> (Utilities/Utilities.Visitor.cs), and <c>VisitResult&lt;T&gt;</c> is
/// <c>T | readonly Node[] | undefined</c>, which <see cref="IsArray"/> discriminates the way
/// <c>Utilities.Visitor.cs</c>'s own private test does.
/// </para>
/// <para>
/// PORT-NOTE (<c>Debug.assertNode(statement, isVariableStatement)</c>): the reference's assertion also narrows the
/// statement for the reads below it; the port states the cast the assertion established (§12.5).
/// </para>
/// </remarks>
internal sealed class ESNextTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:71-90 — the factory's prologue.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:84 — <c>exportBindings</c>.</summary>
    private TransformationUtilities.IdentifierNameMap<ExportSpecifier>? _exportBindings;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:85 — <c>exportVars</c>.</summary>
    private List<VariableDeclaration>? _exportVars;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:86 — <c>defaultExportBinding</c>.</summary>
    private Identifier? _defaultExportBinding;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:87 — <c>exportEqualsBinding</c>.</summary>
    private Identifier? _exportEqualsBinding;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:71-90 — the factory's prologue.</summary>
    public ESNextTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:92-103 — <c>transformSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>double</c>: the reference chains this through <c>chainBundle</c>; this member is the
    /// <c>transformSourceFile</c> arm it wraps, and <see cref="TransformSourceFileOrBundle"/> is the chain's face.
    /// The reference's three resets do not include <c>exportEqualsBinding</c>, and neither does the port.
    /// </remarks>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        var visited = Utilities.VisitNode(node, Visitor, NodeTests.IsSourceFile)!;
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());
        _exportVars = null;
        _exportBindings = null;
        _defaultExportBinding = null;
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:90 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/esnext.ts:105-126 — <c>visitor</c>.</summary>
    public object? Visitor(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsESNext) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case SyntaxKind.SourceFile:
                return VisitSourceFile((SourceFile)node);

            case SyntaxKind.Block:
                return VisitBlock((Block)node);

            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node);

            case SyntaxKind.ForOfStatement:
                return VisitForOfStatement((ForOfStatement)node);

            default:
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:128-234 — <c>visitSourceFile</c>.</summary>
    /// <remarks>
    /// <para>
    /// The transform's central case: a source file with a top-level <c>using</c> is split into a hoisted statement
    /// list (imports, exports, functions and the bindings the <c>using</c> body assigns) followed by the
    /// <c>try</c>/<c>catch</c>/<c>finally</c> environment block, with everything before the first <c>using</c> left in
    /// place.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>visitArray</c>): TS's <c>visitArray(node.statements, …)</c> and <c>visitNodes(…)</c> are the same
    /// worker (<c>visitorPublic.ts</c>), differing only in the answer's declared type — <c>visitArray</c> answers
    /// <c>readonly T[]</c> so that its caller's <c>addRange</c> cannot see a <c>NodeArray</c>. The port states both
    /// calls as <see cref="Utilities.VisitNodes{TNode}"/>, whose <c>NodeArray&lt;T&gt;?</c> is the <c>IList</c> the
    /// port's <see cref="Core.AddRange{T}"/> takes; the array itself and every element are the same ones
    /// <c>visitArray</c> builds.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>arrayFrom(exportBindings.values())</c>): the reference's <c>arrayFrom</c> is the port's
    /// <c>List&lt;T&gt;</c> constructor over the same enumeration, which is what
    /// <c>NodeFactory.CreateNamedExports</c> takes.
    /// </para>
    /// </remarks>
    private SourceFile VisitSourceFile(SourceFile node)
    {
        var usingKind = GetUsingKindOfStatements(node.Statements);
        if (usingKind != UsingKind.None)
        {
            _context.StartLexicalEnvironment();

            var exportBindings = new TransformationUtilities.IdentifierNameMap<ExportSpecifier>();
            var exportVars = new List<VariableDeclaration>();
            _exportBindings = exportBindings;
            _exportVars = exportVars;

            var prologueCount = CountPrologueStatements(node.Statements);
            var topLevelStatements = new List<IStatement>();
            Core.AddRange(topLevelStatements, Utilities.VisitNodes(node.Statements, Visitor, Utilities.IsStatement, 0, prologueCount));

            // Collect and transform any leading statements up to the first `using` or `await using`. This preserves
            // the original statement order much as is possible.
            var pos = prologueCount;
            while (pos < node.Statements!.Count)
            {
                var statement = node.Statements[pos];
                if (GetUsingKind(statement) != UsingKind.None)
                {
                    if (pos > prologueCount)
                    {
                        Core.AddRange(
                            topLevelStatements,
                            Utilities.VisitNodes(node.Statements, Visitor, Utilities.IsStatement, prologueCount, pos - prologueCount));
                    }
                    break;
                }
                pos++;
            }

            Debug.Assert(pos < node.Statements!.Count, "Should have encountered at least one 'using' statement.");

            // transform the rest of the body
            var envBinding = CreateEnvBinding();
            var bodyStatements = TransformUsingDeclarations(
                node.Statements!,
                pos,
                node.Statements!.Count,
                envBinding,
                topLevelStatements);

            // add `export {}` declarations for any hoisted bindings.
            if (exportBindings.Count > 0)
            {
                topLevelStatements.Add(_factory.CreateExportDeclaration(
                    /*modifiers*/ null,
                    /*isTypeOnly*/ false,
                    _factory.CreateNamedExports(new List<ExportSpecifier>(exportBindings.Values())),
                    /*moduleSpecifier*/ null,
                    /*attributes*/ null));
            }

            Core.AddRange(topLevelStatements, _context.EndLexicalEnvironment());
            if (exportVars.Count > 0)
            {
                topLevelStatements.Add(_factory.CreateVariableStatement(
                    _factory.CreateModifiersFromModifierFlags(ModifierFlags.Export)?.Cast<Node>().ToList(),
                    _factory.CreateVariableDeclarationList(exportVars, NodeFlags.Let)));
            }
            Core.AddRange(topLevelStatements, CreateDownlevelUsingStatements(bodyStatements, envBinding, usingKind == UsingKind.Async));

            if (_exportEqualsBinding is not null)
            {
                topLevelStatements.Add(_factory.CreateExportAssignment(
                    /*modifiers*/ null,
                    /*isExportEquals*/ true,
                    _exportEqualsBinding));
            }

            return _factory.UpdateSourceFile(node, topLevelStatements, null, null, null, null, null);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:236-254 — <c>visitBlock</c>.</summary>
    /// <remarks>PORT-NOTE: the reference builds the block's statement list with two spreads
    /// (<c>[...visitArray(…), ...createDownlevelUsingStatements(…)]</c>); the port fills the same fresh
    /// <c>List&lt;IStatement&gt;</c> in the same order, and the first spread's <c>visitArray</c> is the
    /// <c>visitNodes</c> substitution <see cref="VisitSourceFile"/> records.</remarks>
    private object? VisitBlock(Block node)
    {
        var usingKind = GetUsingKindOfStatements(node.Statements);
        if (usingKind != UsingKind.None)
        {
            var prologueCount = CountPrologueStatements(node.Statements);
            var envBinding = CreateEnvBinding();
            var statements = new List<IStatement>();
            Core.AddRange(statements, Utilities.VisitNodes(node.Statements, Visitor, Utilities.IsStatement, 0, prologueCount));
            Core.AddRange(statements, CreateDownlevelUsingStatements(
                TransformUsingDeclarations(
                    node.Statements!,
                    prologueCount,
                    node.Statements!.Count,
                    envBinding,
                    /*topLevelStatements*/ null),
                envBinding,
                usingKind == UsingKind.Async));
            return _factory.UpdateBlock(node, statements);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:256-287 — <c>visitForStatement</c>.</summary>
    /// <remarks>
    /// Given <c>for (using x = expr; cond; incr) { … }</c>, produces a shallow transformation to
    /// <c>{ using x = expr; for (; cond; incr) { … } }</c> before handing the shallow transformation back to the
    /// visitor for an in-depth transformation.
    /// </remarks>
    private object? VisitForStatement(ForStatement node)
    {
        if (node.Initializer is not null && IsUsingVariableDeclarationList(node.Initializer))
        {
            return Utilities.VisitNode(
                _factory.CreateBlock(
                    [
                        _factory.CreateVariableStatement(/*modifiers*/ null, node.Initializer),
                        _factory.UpdateForStatement(
                            node,
                            /*initializer*/ null,
                            node.Condition,
                            node.Incrementor,
                            node.Statement),
                    ],
                    /*multiLine*/ null),
                Visitor,
                Utilities.IsStatement);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:289-337 — <c>visitForOfStatement</c>.</summary>
    /// <remarks>
    /// <para>
    /// Given <c>for (using x of y) { … }</c>, produces a shallow transformation to
    /// <c>for (const x_1 of y) { using x = x; … }</c> before handing the shallow transformation back to the visitor
    /// for an in-depth transformation.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>[usingVarStatement, ...node.statement.statements]</c>): the reference spreads the block's
    /// statements into a fresh array; the port fills the same fresh <c>List&lt;IStatement&gt;</c> in the same order.
    /// </para>
    /// </remarks>
    private object? VisitForOfStatement(ForOfStatement node)
    {
        if (IsUsingVariableDeclarationList(node.Initializer!))
        {
            var forInitializer = (VariableDeclarationList)node.Initializer!;
            var forDecl = Core.FirstOrUndefined(forInitializer.Declarations)
                ?? _factory.CreateVariableDeclaration(
                    _factory.CreateTempVariable(/*recordTempVariable*/ null, null),
                    /*exclamationToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null);

            var isAwaitUsing = GetUsingKindOfVariableDeclarationList(forInitializer) == UsingKind.Async;
            var temp = _factory.GetGeneratedNameForNode(forDecl.Name, /*flags*/ null);
            var usingVar = _factory.UpdateVariableDeclaration(
                forDecl,
                forDecl.Name,
                /*exclamationToken*/ null,
                /*type*/ null,
                temp);
            var usingVarList = _factory.CreateVariableDeclarationList([usingVar], isAwaitUsing ? NodeFlags.AwaitUsing : NodeFlags.Using);
            var usingVarStatement = _factory.CreateVariableStatement(/*modifiers*/ null, usingVarList);

            IStatement statement;
            if (NodeTests.IsBlock((Node)node.Statement!))
            {
                var block = (Block)node.Statement!;
                var blockStatements = new List<IStatement> { usingVarStatement };
                Core.AddRange(blockStatements, block.Statements);
                statement = _factory.UpdateBlock(block, blockStatements);
            }
            else
            {
                statement = _factory.CreateBlock([usingVarStatement, node.Statement!], /*multiLine*/ true);
            }

            return Utilities.VisitNode(
                _factory.UpdateForOfStatement(
                    node,
                    node.AwaitModifier,
                    _factory.CreateVariableDeclarationList(
                        [_factory.CreateVariableDeclaration(temp, /*exclamationToken*/ null, /*type*/ null, /*initializer*/ null)],
                        NodeFlags.Const),
                    node.Expression,
                    statement),
                Visitor,
                Utilities.IsStatement);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:339-393 — <c>transformUsingDeclarations</c>.</summary>
    /// <remarks>
    /// <para>
    /// Transforms the <c>using</c> declarations in a statement list: each valid declaration becomes an assignment of
    /// <c>__addDisposableResource(env, initializer, async)</c> (hoisted to the file's statement list when a
    /// <paramref name="topLevelStatements"/> is given), and every other statement is visited and hoisted or appended by
    /// <see cref="Hoist"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>declaration</c>): the reference's loop variable is a reassignable <c>let</c> binding; a C#
    /// <c>foreach</c> iteration variable cannot be assigned, so the port takes the same value in a local, which is the
    /// only use of the reassignment.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>declarations.length = 0</c>): the reference truncates the array in place to signal "this is not a
    /// <c>using</c> after all"; <see cref="List{T}.Clear"/> is that truncation for the array that is empty at this
    /// point.
    /// </para>
    /// </remarks>
    private List<IStatement> TransformUsingDeclarations(
        IList<IStatement> statementsIn,
        int start,
        int end,
        Identifier envBinding,
        List<IStatement>? topLevelStatements)
    {
        var statements = new List<IStatement>();

        for (var i = start; i < end; i++)
        {
            var statement = statementsIn[i];
            var usingKind = GetUsingKind(statement);
            if (usingKind != UsingKind.None)
            {
                Debug.AssertNode((Node)statement, NodeTests.IsVariableStatement);
                var variableStatement = (VariableStatement)statement;
                var declarations = new List<VariableDeclaration>();
                foreach (var declarationIn in variableStatement.DeclarationList!.Declarations!)
                {
                    var declaration = declarationIn;
                    if (!NodeTests.IsIdentifier(declaration.Name!))
                    {
                        // Since binding patterns are a grammar error, we reset `declarations` so we don't process this
                        // as a `using`.
                        declarations.Clear();
                        break;
                    }

                    // perform a shallow transform for any named evaluation
                    if (NamedEvaluation.IsNamedEvaluation(declaration))
                    {
                        declaration = (VariableDeclaration)NamedEvaluation.TransformNamedEvaluation(_context, declaration);
                    }

                    var initializer = Utilities.VisitNode(declaration.Initializer, Visitor, Utilities.IsExpression) ?? _factory.CreateVoidZero();
                    declarations.Add(_factory.UpdateVariableDeclaration(
                        declaration,
                        declaration.Name,
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        _emitHelpers.CreateAddDisposableResourceHelper(
                            envBinding,
                            initializer,
                            usingKind == UsingKind.Async)));
                }

                // Only replace the statement if it was valid.
                if (declarations.Count > 0)
                {
                    var varList = _factory.CreateVariableDeclarationList(declarations, NodeFlags.Const);
                    NodeFactory.SetOriginalNode(varList, variableStatement.DeclarationList);
                    Utilities.SetTextRange(varList, variableStatement.DeclarationList);
                    HoistOrAppendNode(
                        statements,
                        topLevelStatements,
                        _factory.UpdateVariableStatement(variableStatement, /*modifiers*/ null, varList));
                    continue;
                }
            }

            var result = Visitor((Node)statement);
            if (IsArray(result))
            {
                foreach (var node in (System.Collections.IEnumerable)result!)
                {
                    HoistOrAppendNode(statements, topLevelStatements, (Node)node);
                }
            }
            else if (result is not null)
            {
                HoistOrAppendNode(statements, topLevelStatements, (Node)result);
            }
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:395-398 — <c>hoistOrAppendNode</c>.</summary>
    /// <remarks>PORT-NOTE: the reference writes <c>append(statements, hoist(node))</c>, whose own guard skips an
    /// absent value (<c>Core.Append</c> keeps that guard: a null value is not added); the port states the same test as
    /// the <c>if</c> whose body is the append, since <see cref="Hoist"/> is where the absent value comes from.</remarks>
    private void HoistOrAppendNode(List<IStatement> statements, List<IStatement>? topLevelStatements, Node node)
    {
        Debug.AssertNode(node, Utilities.IsStatement);
        var hoisted = Hoist(topLevelStatements, (IStatement)node);
        if (hoisted is not null)
        {
            statements.Add(hoisted);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:400-419 — <c>hoist</c>.</summary>
    /// <remarks>PORT-NOTE (the answer): the reference's declared answer is <c>Statement</c> while its
    /// <c>SyntaxKind.ImportDeclaration</c> arm returns <c>undefined</c> (the node went to the enclosing statement list),
    /// so the port states <c>IStatement?</c> — the same answer <see cref="HoistOrAppendNode"/> reads.</remarks>
    private IStatement? Hoist(List<IStatement>? topLevelStatements, IStatement node)
    {
        if (topLevelStatements is null) return node;

        switch (node.Kind)
        {
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.FunctionDeclaration:
                return HoistImportOrExportOrHoistedDeclaration(node, topLevelStatements);
            case SyntaxKind.ExportAssignment:
                return HoistExportAssignment((ExportAssignment)node);
            case SyntaxKind.ClassDeclaration:
                return HoistClassDeclaration((ClassDeclaration)node);
            case SyntaxKind.VariableStatement:
                return HoistVariableStatement((VariableStatement)node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:421-425 —
    /// <c>hoistImportOrExportOrHoistedDeclaration</c>.</summary>
    private static IStatement? HoistImportOrExportOrHoistedDeclaration(IStatement node, List<IStatement> topLevelStatements)
    {
        // NOTE: `node` has already been visited
        topLevelStatements.Add(node);
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:427-429 — <c>hoistExportAssignment</c>.</summary>
    private IStatement HoistExportAssignment(ExportAssignment node)
        => node.IsExportEquals == true ? HoistExportEquals(node) : HoistExportDefault(node);

    /// <summary>ported from: src/compiler/transformers/esnext.ts:431-464 — <c>hoistExportDefault</c>.</summary>
    /// <remarks>
    /// Given <c>export default expr;</c> at the top level of a file with a top-level <c>using</c>, produces a
    /// file-level <c>var default_1;</c> plus <c>export { default_1 as default }</c> and the body statement
    /// <c>default_1 = expr;</c>.
    /// </remarks>
    private IStatement HoistExportDefault(ExportAssignment node)
    {
        // NOTE: `node` has already been visited
        if (_defaultExportBinding is not null)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        _defaultExportBinding = _factory.CreateUniqueName(
            "_default",
            GeneratedIdentifierFlags.ReservedInNestedScopes | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic);
        HoistBindingIdentifier(_defaultExportBinding, /*isExport*/ true, "default", node);

        // give a class or function expression an assigned name, if needed.
        var expression = node.Expression!;
        var innerExpression = (Expression)FactoryUtilities.SkipOuterExpressions(expression);
        if (NamedEvaluation.IsNamedEvaluation(innerExpression))
        {
            innerExpression = (Expression)NamedEvaluation.TransformNamedEvaluation(
                _context,
                innerExpression,
                /*ignoreEmptyStringLiteral*/ false,
                "default");
            expression = _factory.RestoreOuterExpressions(expression, innerExpression, /*kinds*/ null);
        }

        var assignment = _factory.CreateAssignment(_defaultExportBinding, expression);
        return _factory.CreateExpressionStatement(assignment);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:466-496 — <c>hoistExportEquals</c>.</summary>
    /// <remarks>
    /// Given <c>export = expr;</c> at the top level of a file with a top-level <c>using</c>, produces a file-level
    /// <c>var default_1;</c>, the body statement <c>default_1 = expr;</c> and the file-level suffix
    /// <c>export = default_1;</c>.
    /// </remarks>
    private IStatement HoistExportEquals(ExportAssignment node)
    {
        // NOTE: `node` has already been visited
        if (_exportEqualsBinding is not null)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        _exportEqualsBinding = _factory.CreateUniqueName(
            "_default",
            GeneratedIdentifierFlags.ReservedInNestedScopes | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic);
        _context.HoistVariableDeclaration(_exportEqualsBinding);

        // give a class or function expression an assigned name, if needed.
        var assignment = _factory.CreateAssignment(_exportEqualsBinding, node.Expression);
        return _factory.CreateExpressionStatement(assignment);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:498-581 — <c>hoistClassDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// When hoisting a class declaration at the top level of a file containing a top-level <c>using</c> statement, the
    /// class is first converted to a class expression so that its binding can be hoisted outside of the
    /// <c>try</c>, and the expression statement <c>C = class C { … }</c> takes its place in the
    /// <c>try</c> body. A default-exported class additionally assigns the generated default binding.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>factory.getLocalName</c>/<c>factory.getDeclarationName</c>): the <c>getName</c> family lives on
    /// <see cref="TypeScriptTransformSupport"/> (the M5 residue that file records), so those calls carry the factory as
    /// their first argument.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>setSourceMapRange(expression, node)</c>): the reference hands the <em>node</em> to the range slot,
    /// which is legal there because <c>SourceMapRange</c> is an interface a node satisfies structurally; no port type
    /// satisfies <see cref="ISourceMapRange"/> structurally, so <c>TypeScriptTransformSupport.SourceMapRangeOf</c> — the
    /// adapter every landed caller uses — states the same <c>Pos</c>/<c>End</c> pair.
    /// </para>
    /// </remarks>
    private IStatement HoistClassDeclaration(ClassDeclaration node)
    {
        // NOTE: `node` has already been visited
        if (node.Name is null && _defaultExportBinding is not null)
        {
            // invalid case of multiple `export default` declarations. Don't assert here, just pass it through
            return node;
        }

        var isExported = Utilities.HasSyntacticModifier(node, ModifierFlags.Export);
        var isDefault = Utilities.HasSyntacticModifier(node, ModifierFlags.Default);

        // When hoisting a class declaration at the top level of a file containing a top-level `using` statement, we
        // must first convert it to a class expression so that we can hoist the binding outside of the `try`.
        Expression expression = _factory.Converters.ConvertToClassExpression(node);
        if (node.Name is not null)
        {
            HoistBindingIdentifier(
                TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null),
                isExported && !isDefault,
                /*exportAlias*/ null,
                node);
            expression = _factory.CreateAssignment(
                TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null),
                expression);
            if (NamedEvaluation.IsNamedEvaluation(expression))
            {
                expression = (Expression)NamedEvaluation.TransformNamedEvaluation(
                    _context,
                    expression,
                    /*ignoreEmptyStringLiteral*/ false);
            }
            NodeFactory.SetOriginalNode(expression, node);
            EmitNodes.SetSourceMapRange(expression, TypeScriptTransformSupport.SourceMapRangeOf(node));
            EmitNodes.SetCommentRange(expression, node);
        }

        if (isDefault && _defaultExportBinding is null)
        {
            // In the case of a default export, we create a temporary variable that we export as the default and then
            // assign to that variable.
            _defaultExportBinding = _factory.CreateUniqueName(
                "_default",
                GeneratedIdentifierFlags.ReservedInNestedScopes | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic);
            HoistBindingIdentifier(_defaultExportBinding, /*isExport*/ true, "default", node);
            expression = _factory.CreateAssignment(_defaultExportBinding, expression);
            if (NamedEvaluation.IsNamedEvaluation(expression))
            {
                expression = (Expression)NamedEvaluation.TransformNamedEvaluation(
                    _context,
                    expression,
                    /*ignoreEmptyStringLiteral*/ false,
                    "default");
            }
            NodeFactory.SetOriginalNode(expression, node);
        }

        return _factory.CreateExpressionStatement(expression);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:583-600 — <c>hoistVariableStatement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>append(expressions, …)</c> skips an absent value and answers the array;
    /// the port states the same call <see cref="Core.Append{T}"/> and keeps its <c>IList&lt;T&gt;?</c> answer, which is
    /// what <c>NodeFactory.InlineExpressions</c> takes after the null test below.</remarks>
    private IStatement? HoistVariableStatement(VariableStatement node)
    {
        // NOTE: `node` has already been visited
        List<Expression>? expressions = null;
        var isExported = Utilities.HasSyntacticModifier(node, ModifierFlags.Export);
        foreach (var variable in node.DeclarationList!.Declarations!)
        {
            HoistBindingElement(variable, isExported, variable);
            if (variable.Initializer is not null)
            {
                expressions = (List<Expression>?)Core.Append(expressions, HoistInitializedVariable(variable));
            }
        }
        if (expressions is not null)
        {
            var statement = _factory.CreateExpressionStatement(_factory.InlineExpressions(expressions));
            NodeFactory.SetOriginalNode(statement, node);
            EmitNodes.SetCommentRange(statement, node);
            EmitNodes.SetSourceMapRange(statement, TypeScriptTransformSupport.SourceMapRangeOf(node));
            return statement;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:603-620 — <c>hoistInitializedVariable</c>.</summary>
    /// <remarks>PORT-NOTE (<c>factory.cloneNode(node.name)</c>): the reference clears the <c>LocalName</c>/
    /// <c>ExportName</c>/<c>InternalName</c> emit flags on the copy so the hoisted assignment's target prints as a
    /// plain reference; the port states the same mask with <see cref="Utilities.GetEmitFlags"/> and
    /// <c>EmitNodes.SetEmitFlags</c>.</remarks>
    private Expression HoistInitializedVariable(VariableDeclaration node)
    {
        // NOTE: `node` has already been visited
        Debug.AssertIsDefined(node.Initializer);
        Expression target;
        if (NodeTests.IsIdentifier(node.Name!))
        {
            target = _factory.CloneNode((Identifier)node.Name!);
            EmitNodes.SetEmitFlags(
                target,
                Utilities.GetEmitFlags(target) & ~(EmitFlags.LocalName | EmitFlags.ExportName | EmitFlags.InternalName));
        }
        else
        {
            target = _factory.Converters.ConvertToAssignmentPattern(node.Name);
        }

        var assignment = _factory.CreateAssignment(target, node.Initializer);
        NodeFactory.SetOriginalNode(assignment, node);
        EmitNodes.SetCommentRange(assignment, node);
        EmitNodes.SetSourceMapRange(assignment, TypeScriptTransformSupport.SourceMapRangeOf(node));
        return assignment;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:622-634 — <c>hoistBindingElement</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (<c>VariableDeclaration | BindingElement</c>): the union's common face is the model's
    /// <see cref="INamedDeclaration"/> (<c>name</c> is the slot both carry), which is what the two hoist callers pass.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>node.name.elements</c>): TS reads <c>elements</c> off the <c>BindingPattern</c> union, whose two
    /// members declare it (<c>NodeArray&lt;BindingElement&gt;</c> and
    /// <c>NodeArray&lt;BindingElement | OmittedExpression&gt;</c>); the model states it on each pattern class, so the
    /// port reads it per kind — the treatment <c>TransformationUtilities.CollectExportedVariableInfo</c> records for
    /// the same read.
    /// </para>
    /// </remarks>
    private void HoistBindingElement(INamedDeclaration node, bool isExportedDeclaration, Node? original)
    {
        // NOTE: `node` has already been visited
        var name = node.Name;
        if (Utilities.IsBindingPattern(name))
        {
            IEnumerable<Node>? elements = name switch
            {
                ObjectBindingPattern pattern => pattern.Elements,
                ArrayBindingPattern pattern => pattern.Elements,
                _ => null,
            };
            if (elements is not null)
            {
                foreach (var element in elements)
                {
                    if (!NodeTests.IsOmittedExpression(element))
                    {
                        HoistBindingElement((INamedDeclaration)element, isExportedDeclaration, original);
                    }
                }
            }
        }
        else
        {
            HoistBindingIdentifier(
                (Identifier)name!,
                isExportedDeclaration,
                /*exportAlias*/ null,
                original);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:636-658 — <c>hoistBindingIdentifier</c>.</summary>
    /// <remarks>
    /// Hoists a binding as a plain <c>var</c> declaration; when the original was exported, the name is either recorded
    /// for an <c>export { … }</c> clause (through <see cref="_exportBindings"/>) or pushed as a member of the
    /// <c>export let …</c> statement <see cref="_exportVars"/> collects.
    /// </remarks>
    private void HoistBindingIdentifier(Identifier node, bool isExport, object? exportAlias, Node? original)
    {
        // NOTE: `node` has already been visited
        var name = Utilities.IsGeneratedIdentifier(node) ? node : _factory.CloneNode(node);
        if (isExport)
        {
            if (exportAlias is null && !FactoryUtilities.IsLocalName(name))
            {
                var varDecl = _factory.CreateVariableDeclaration(name, /*exclamationToken*/ null, /*type*/ null, /*initializer*/ null);
                if (original is not null)
                {
                    NodeFactory.SetOriginalNode(varDecl, original);
                }
                _exportVars!.Add(varDecl);
                return;
            }

            var localName = exportAlias is not null ? name : null;
            var exportName = exportAlias ?? name;
            var specifier = _factory.CreateExportSpecifier(/*isTypeOnly*/ false, localName, exportName);
            if (original is not null)
            {
                NodeFactory.SetOriginalNode(specifier, original);
            }
            _exportBindings!.Set(name, specifier);
        }
        _context.HoistVariableDeclaration(name);
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:660-662 — <c>createEnvBinding</c>.</summary>
    private Identifier CreateEnvBinding() => _factory.CreateUniqueName("env", /*flags*/ null);

    /// <summary>ported from: src/compiler/transformers/esnext.ts:664-789 — <c>createDownlevelUsingStatements</c>.</summary>
    /// <remarks>
    /// <para>
    /// Produces the environment object, the <c>try</c> with the visitor's body statements, the <c>catch</c> that
    /// records the error in it, and the <c>finally</c> that disposes it — awaiting the disposal's promise for an
    /// <c>await using</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>readonly Statement[]</c>): the two callers hand this member the fresh list
    /// <see cref="TransformUsingDeclarations"/> built, which nothing else aliases, so the port states it as
    /// <c>List&lt;IStatement&gt;</c> — the same list <c>NodeFactory.CreateBlock</c> takes.
    /// </para>
    /// </remarks>
    private List<IStatement> CreateDownlevelUsingStatements(List<IStatement> bodyStatements, Identifier envBinding, bool async)
    {
        var statements = new List<IStatement>();

        // produces:
        //
        //  const env_1 = { stack: [], error: void 0, hasError: false };
        //
        var envObject = _factory.CreateObjectLiteralExpression(
            [
                _factory.CreatePropertyAssignment("stack", _factory.CreateArrayLiteralExpression(/*elements*/ null, /*multiLine*/ null)),
                _factory.CreatePropertyAssignment("error", _factory.CreateVoidZero()),
                _factory.CreatePropertyAssignment("hasError", _factory.CreateFalse()),
            ],
            /*multiLine*/ null);
        var envVar = _factory.CreateVariableDeclaration(
            envBinding,
            /*exclamationToken*/ null,
            /*type*/ null,
            envObject);
        var envVarList = _factory.CreateVariableDeclarationList([envVar], NodeFlags.Const);
        var envVarStatement = _factory.CreateVariableStatement(/*modifiers*/ null, envVarList);
        statements.Add(envVarStatement);

        // Unfortunately, it is necessary to use two properties to indicate an error because `throw undefined` is legal
        // JavaScript.
        var tryBlock = _factory.CreateBlock(bodyStatements, /*multiLine*/ true);
        var bodyCatchBinding = _factory.CreateUniqueName("e", /*flags*/ null);
        var catchClause = _factory.CreateCatchClause(
            bodyCatchBinding,
            _factory.CreateBlock(
                [
                    _factory.CreateExpressionStatement(
                        _factory.CreateAssignment(
                            _factory.CreatePropertyAccessExpression(envBinding, "error"),
                            bodyCatchBinding)),
                    _factory.CreateExpressionStatement(
                        _factory.CreateAssignment(
                            _factory.CreatePropertyAccessExpression(envBinding, "hasError"),
                            _factory.CreateTrue())),
                ],
                /*multiLine*/ true));

        Block finallyBlock;
        if (async)
        {
            var result = _factory.CreateUniqueName("result", /*flags*/ null);
            finallyBlock = _factory.CreateBlock(
                [
                    _factory.CreateVariableStatement(
                        /*modifiers*/ null,
                        _factory.CreateVariableDeclarationList(
                            [
                                _factory.CreateVariableDeclaration(
                                    result,
                                    /*exclamationToken*/ null,
                                    /*type*/ null,
                                    _emitHelpers.CreateDisposeResourcesHelper(envBinding)),
                            ],
                            NodeFlags.Const)),
                    _factory.CreateIfStatement(
                        result,
                        _factory.CreateExpressionStatement(_factory.CreateAwaitExpression(result)),
                        /*elseStatement*/ null),
                ],
                /*multiLine*/ true);
        }
        else
        {
            finallyBlock = _factory.CreateBlock(
                [
                    _factory.CreateExpressionStatement(
                        _emitHelpers.CreateDisposeResourcesHelper(envBinding)),
                ],
                /*multiLine*/ true);
        }

        var tryStatement = _factory.CreateTryStatement(tryBlock, catchClause, finallyBlock);
        statements.Add(tryStatement);

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:764-772 — <c>countPrologueStatements</c>.</summary>
    /// <remarks>PORT-NOTE: the reference answers <c>0</c> when every statement is a prologue (its loop falls out
    /// without a <c>return</c>), which the port keeps.</remarks>
    private static int CountPrologueStatements(IList<IStatement>? statements)
    {
        for (var i = 0; i < (statements?.Count ?? 0); i++)
        {
            if (!Utilities.IsPrologueDirective((Node)statements![i]) && !Utilities.IsCustomPrologue((Node)statements![i]))
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>ported from: src/compiler/transformers/esnext.ts:774-776 — <c>isUsingVariableDeclarationList</c>.</summary>
    /// <remarks>PORT-NOTE: TS's answer is the type guard
    /// <c>VariableDeclarationList &amp; { _usingBrand: any }</c>; the port answers <c>bool</c> and its callers cast, as
    /// every narrowing predicate of the port does.</remarks>
    private static bool IsUsingVariableDeclarationList(Node node)
        => NodeTests.IsVariableDeclarationList(node)
            && GetUsingKindOfVariableDeclarationList((VariableDeclarationList)node) != UsingKind.None;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:778-782 —
    /// <c>getUsingKindOfVariableDeclarationList</c>.</summary>
    private static UsingKind GetUsingKindOfVariableDeclarationList(VariableDeclarationList node)
        => (node.Flags & NodeFlags.BlockScoped) == NodeFlags.AwaitUsing ? UsingKind.Async
            : (node.Flags & NodeFlags.BlockScoped) == NodeFlags.Using ? UsingKind.Sync
            : UsingKind.None;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:784-786 — <c>getUsingKindOfVariableStatement</c>.</summary>
    private static UsingKind GetUsingKindOfVariableStatement(VariableStatement node)
        => GetUsingKindOfVariableDeclarationList(node.DeclarationList!);

    /// <summary>ported from: src/compiler/transformers/esnext.ts:788-790 — <c>getUsingKind</c>.</summary>
    private static UsingKind GetUsingKind(IStatement statement)
        => NodeTests.IsVariableStatement((Node)statement)
            ? GetUsingKindOfVariableStatement((VariableStatement)statement)
            : UsingKind.None;

    /// <summary>ported from: src/compiler/transformers/esnext.ts:792-800 — <c>getUsingKindOfStatements</c>.</summary>
    /// <remarks>PORT-NOTE (<c>usingKind &gt; result</c>): TS orders two const-enum members numerically, which C# does
    /// not state for enum values; <see cref="UsingKind"/> keeps the reference's values (None = 0, Sync = 1, Async = 2),
    /// so the same comparison is stated over those numbers.</remarks>
    private static UsingKind GetUsingKindOfStatements(IList<IStatement>? statements)
    {
        var result = UsingKind.None;
        if (statements is not null)
        {
            foreach (var statement in statements)
            {
                var usingKind = GetUsingKind(statement);
                if (usingKind == UsingKind.Async) return UsingKind.Async;
                if ((int)usingKind > (int)result) result = usingKind;
            }
        }
        return result;
    }

    /// <summary>The node-array arm of TS's <c>isArray(result)</c> over a visitor's answer.</summary>
    /// <remarks>
    /// PORT-NOTE: the visitor slot is <c>Func&lt;Node, object?&gt;</c>, so <c>isArray</c>'s runtime array test is this
    /// file's own — the same test <c>Utilities.Visitor.cs</c> makes in its private <c>IsNodeArray</c>: a
    /// <c>NodeArray&lt;IStatement&gt;</c> is not an <c>IEnumerable&lt;Node&gt;</c> (generics are invariant), while the
    /// non-generic <c>IEnumerable</c> test covers every node array there is, and <see cref="Node"/> is excluded because
    /// a node is never the array.
    /// </remarks>
    private static bool IsArray(object? value) => value is not Node and System.Collections.IEnumerable;
}
