#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/module.ts:1409-1958 — the helper-expression pickers, the four
// top-level import/export visitors, the function/class/variable visitors and the two destructuring callbacks.
/// <summary>
/// ported from: src/compiler/transformers/module/module.ts:1409-1958 — the top-level import/export rewrites, the
/// helper-expression pickers (<c>__importStar</c>/<c>__importDefault</c>), and the exported declaration visitors that
/// append the <c>exports.x</c> assignments.
/// </summary>
internal sealed partial class ModuleTransformer
{
    /// <summary>ported from: src/compiler/transformers/module/module.ts:1409-1417 — <c>getHelperExpressionForExport</c>.</summary>
    private Expression GetHelperExpressionForExport(ExportDeclaration node, Expression innerExpr)
    {
        if (!Utilities.GetESModuleInterop(_compilerOptions) || (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.NeverApplyImportHelper) != 0)
        {
            return innerExpr;
        }
        if (TransformationUtilities.GetExportNeedsImportStarHelper(node))
        {
            return _emitHelpers.CreateImportStarHelper(innerExpr);
        }
        return innerExpr;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1419-1430 — <c>getHelperExpressionForImport</c>.</summary>
    private Expression GetHelperExpressionForImport(ImportDeclaration node, Expression innerExpr)
    {
        if (!Utilities.GetESModuleInterop(_compilerOptions) || (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.NeverApplyImportHelper) != 0)
        {
            return innerExpr;
        }
        if (TransformationUtilities.GetImportNeedsImportStarHelper(node))
        {
            return _emitHelpers.CreateImportStarHelper(innerExpr);
        }
        if (TransformationUtilities.GetImportNeedsImportDefaultHelper(node))
        {
            return _emitHelpers.CreateImportDefaultHelper(innerExpr);
        }
        return innerExpr;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1437-1531 — <c>visitTopLevelImportDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getNamespaceDeclarationNode(node) as NamespaceImport | undefined</c> is the port's
    /// <c>as</c> over <see cref="Utilities.GetNamespaceDeclarationNode"/>, whose answer is the three-member union.</remarks>
    private object? VisitTopLevelImportDeclaration(ImportDeclaration node)
    {
        List<IStatement>? statements = null;
        var namespaceDeclaration = Utilities.GetNamespaceDeclarationNode(node) as NamespaceImport;
        if (_moduleKind != ModuleKind.AMD)
        {
            if (node.ImportClause is null)
            {
                // import "mod";
                return NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(_factory.CreateExpressionStatement(CreateRequireCall(node)), node),
                    node);
            }
            else
            {
                var variables = new List<VariableDeclaration>();
                if (namespaceDeclaration is not null && !TransformationUtilities.IsDefaultImport(node))
                {
                    // import * as n from "mod";
                    variables.Add(
                        _factory.CreateVariableDeclaration(
                            _factory.CloneNode(namespaceDeclaration.Name),
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            GetHelperExpressionForImport(node, CreateRequireCall(node))));
                }
                else
                {
                    // import d from "mod";
                    // import { x, y } from "mod";
                    // import d, { x, y } from "mod";
                    // import d, * as n from "mod";
                    variables.Add(
                        _factory.CreateVariableDeclaration(
                            _factory.GetGeneratedNameForNode(node, null),
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            GetHelperExpressionForImport(node, CreateRequireCall(node))));

                    if (namespaceDeclaration is not null && TransformationUtilities.IsDefaultImport(node))
                    {
                        variables.Add(
                            _factory.CreateVariableDeclaration(
                                _factory.CloneNode(namespaceDeclaration.Name),
                                /*exclamationToken*/ null,
                                /*type*/ null,
                                _factory.GetGeneratedNameForNode(node, null)));
                    }
                }

                statements = Append(
                    statements,
                    NodeFactory.SetOriginalNode(
                        Utilities.SetTextRange(
                            _factory.CreateVariableStatement(
                                /*modifiers*/ null,
                                _factory.CreateVariableDeclarationList(
                                    variables,
                                    _languageVersion >= ScriptTarget.ES2015 ? NodeFlags.Const : NodeFlags.None)),
                            /*location*/ node),
                        /*original*/ node));
            }
        }
        else if (namespaceDeclaration is not null && TransformationUtilities.IsDefaultImport(node))
        {
            // import d, * as n from "mod";
            statements = Append(
                statements,
                _factory.CreateVariableStatement(
                    /*modifiers*/ null,
                    _factory.CreateVariableDeclarationList(
                        [
                            NodeFactory.SetOriginalNode(
                                Utilities.SetTextRange(
                                    _factory.CreateVariableDeclaration(
                                        _factory.CloneNode(namespaceDeclaration.Name),
                                        /*exclamationToken*/ null,
                                        /*type*/ null,
                                        _factory.GetGeneratedNameForNode(node, null)),
                                    /*location*/ node),
                                /*original*/ node),
                        ],
                        _languageVersion >= ScriptTarget.ES2015 ? NodeFlags.Const : NodeFlags.None)));
        }

        statements = AppendExportsOfImportDeclaration(statements, node);
        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1538-1546 — <c>createRequireCall</c>.</summary>
    /// <remarks>PORT-NOTE: TS pushes <c>rewriteModuleSpecifier(moduleName, compilerOptions)</c> into an
    /// <c>Expression[]</c> only when the module name is present; the port's <c>!</c> is that same non-null path
    /// (<c>rewriteModuleSpecifier</c> answers the node itself when no rewrite applies, which is never null for a
    /// string literal).</remarks>
    private CallExpression CreateRequireCall(Node importNode)
    {
        var moduleName = ModuleTransformSupport.GetExternalModuleNameLiteral(
            _factory, importNode, _currentSourceFile!, _host, _resolver, _compilerOptions);
        var args = new List<Expression>();
        if (moduleName is not null)
        {
            args.Add(TransformationUtilities.RewriteModuleSpecifier(moduleName, _compilerOptions)!);
        }

        return _factory.CreateCallExpression(_factory.CreateIdentifier("require", null, null), /*typeArguments*/ null, args);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1553-1620 — <c>visitTopLevelImportEqualsDeclaration</c>.</summary>
    private object? VisitTopLevelImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        Debug.Assert(Utilities.IsExternalModuleImportEqualsDeclaration(node), "import= for internal module references should be handled in an earlier transformer.");

        List<IStatement>? statements = null;
        if (_moduleKind != ModuleKind.AMD)
        {
            if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
            {
                statements = Append(
                    statements,
                    NodeFactory.SetOriginalNode(
                        Utilities.SetTextRange(
                            _factory.CreateExpressionStatement(
                                CreateExportExpression(node.Name!, CreateRequireCall(node))),
                            node),
                        node));
            }
            else
            {
                statements = Append(
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
            }
        }
        else
        {
            if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
            {
                statements = Append(
                    statements,
                    NodeFactory.SetOriginalNode(
                        Utilities.SetTextRange(
                            _factory.CreateExpressionStatement(
                                CreateExportExpression(
                                    TypeScriptTransformSupport.GetExportName(_factory, node, null, null),
                                    TypeScriptTransformSupport.GetLocalName(_factory, node, null, null, null))),
                            node),
                        node));
            }
        }

        statements = AppendExportsOfImportEqualsDeclaration(statements, node);
        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1627-1729 — <c>visitTopLevelExportDeclaration</c>.</summary>
    private object? VisitTopLevelExportDeclaration(ExportDeclaration node)
    {
        if (node.ModuleSpecifier is null)
        {
            // Elide export declarations with no module specifier as they are handled
            // elsewhere.
            return null;
        }

        var generatedName = _factory.GetGeneratedNameForNode(node, null);

        if (node.ExportClause is not null && NodeTests.IsNamedExports(node.ExportClause))
        {
            var statements = new List<IStatement>();
            // export { x, y } from "mod";
            if (_moduleKind != ModuleKind.AMD)
            {
                statements.Add(
                    NodeFactory.SetOriginalNode(
                        Utilities.SetTextRange(
                            _factory.CreateVariableStatement(
                                /*modifiers*/ null,
                                _factory.CreateVariableDeclarationList([
                                    _factory.CreateVariableDeclaration(
                                        generatedName,
                                        /*exclamationToken*/ null,
                                        /*type*/ null,
                                        CreateRequireCall(node)),
                                ], null)),
                            /*location*/ node),
                        /* original */ node));
            }
            foreach (var specifier in ((NamedExports)node.ExportClause).Elements!)
            {
                var specifierName = (Node?)specifier.PropertyName ?? specifier.Name!;
                var exportNeedsImportDefault = Utilities.GetESModuleInterop(_compilerOptions)
                    && (ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.NeverApplyImportHelper) == 0
                    && Utilities.ModuleExportNameIsDefault(specifierName);
                var target = exportNeedsImportDefault ? _emitHelpers.CreateImportDefaultHelper(generatedName) : generatedName;
                var exportedValue = NodeTests.IsStringLiteral(specifierName)
                    ? (Expression)_factory.CreateElementAccessExpression(target, specifierName)
                    : _factory.CreatePropertyAccessExpression(target, specifierName);
                statements.Add(
                    NodeFactory.SetOriginalNode(
                        Utilities.SetTextRange(
                            _factory.CreateExpressionStatement(
                                CreateExportExpression(
                                    NodeTests.IsStringLiteral(specifier.Name!)
                                        ? _factory.CloneNode(specifier.Name)
                                        : TypeScriptTransformSupport.GetExportName(_factory, specifier, null, null),
                                    exportedValue,
                                    /*location*/ null,
                                    /*liveBinding*/ true)),
                            specifier),
                        specifier));
            }

            return ModuleTransformSupport.SingleOrMany(statements);
        }
        else if (node.ExportClause is not null)
        {
            var statements = new List<IStatement>();
            // export * as ns from "mod";
            // export * as default from "mod";
            statements.Add(
                NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(
                        _factory.CreateExpressionStatement(
                            CreateExportExpression(
                                _factory.CloneNode(((NamespaceExport)node.ExportClause).Name),
                                GetHelperExpressionForExport(
                                    node,
                                    _moduleKind != ModuleKind.AMD
                                        ? CreateRequireCall(node)
                                        : ModuleTransformSupport.IsExportNamespaceAsDefaultDeclaration(node)
                                            ? generatedName
                                            : NodeTests.IsStringLiteral(((NamespaceExport)node.ExportClause).Name!)
                                                ? generatedName
                                                : _factory.CreateIdentifier(Utilities.IdText(((NamespaceExport)node.ExportClause).Name!), null, null)))),
                        node),
                    node));

            return ModuleTransformSupport.SingleOrMany(statements);
        }
        else
        {
            // export * from "mod";
            return NodeFactory.SetOriginalNode(
                Utilities.SetTextRange(
                    _factory.CreateExpressionStatement(
                        _emitHelpers.CreateExportStarHelper(
                            _moduleKind != ModuleKind.AMD ? CreateRequireCall(node) : generatedName,
                            /*exportsExpression*/ null)),
                    node),
                node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1736-1742 — <c>visitTopLevelExportAssignment</c>.</summary>
    private object? VisitTopLevelExportAssignment(ExportAssignment node)
    {
        if (Js.IsTruthy(node.IsExportEquals))
        {
            return null;
        }

        return CreateExportStatement(
            _factory.CreateIdentifier("default", null, null),
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)!,
            /*location*/ node,
            /*allowComments*/ true);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1749-1777 — <c>visitFunctionDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference hands <c>visitEachChild(node.body, …)</c> a possibly-missing body, which TS's
    /// <c>visitEachChild</c> answers <c>undefined</c> for (<c>visitorPublic.ts:598-601</c>); the port's
    /// <see cref="VisitEachChildOrNull{TNode}"/> is that same rule, since
    /// <see cref="Utilities.VisitEachChildWorker{TNode}"/> is non-nullable.</remarks>
    private object? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        List<IStatement>? statements = null;
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            statements = Append(
                statements,
                NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(
                        _factory.CreateFunctionDeclaration(
                            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
                            node.AsteriskToken,
                            TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ true),
                            /*typeParameters*/ null,
                            Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
                            /*type*/ null,
                            VisitEachChildOrNull(node.Body, Visitor)),
                        /*location*/ node),
                    /*original*/ node));
        }
        else
        {
            statements = Append(statements, Utilities.VisitEachChildWorker(node, Visitor, _context));
        }

        // NOTE: CommonJS/AMD/UMD exports are hoisted to the top of the module body and do not need to be added here.
        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1784-1810 — <c>visitClassDeclaration</c>.</summary>
    private object? VisitClassDeclaration(ClassDeclaration node)
    {
        List<IStatement>? statements = null;
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            statements = Append(
                statements,
                NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(
                        _factory.CreateClassDeclaration(
                            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifierLike),
                            TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ true),
                            /*typeParameters*/ null,
                            Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause),
                            Utilities.VisitNodes(node.Members, Visitor, Utilities.IsClassElement)),
                        node),
                    node));
        }
        else
        {
            statements = Append(statements, Utilities.VisitEachChildWorker(node, Visitor, _context));
        }

        statements = AppendExportsOfHoistedDeclaration(statements, node);
        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1817-1903 — <c>visitVariableStatement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter type is the <c>InitializedVariableDeclaration</c> refinement
    /// (<c>VariableDeclaration &amp; { initializer: Expression }</c>) the branch's own <c>else if (variable.initializer)</c>
    /// has just established; the port's model has no such subtype, so <see cref="TransformInitializedVariable"/> takes the
    /// class and reads the initializer it already tested.</remarks>
    private object? VisitVariableStatement(VariableStatement node)
    {
        List<IStatement>? statements = null;
        List<VariableDeclaration>? variables = null;
        List<Expression>? expressions = null;

        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            List<Node>? modifiers = null;
            var removeCommentsOnExpressions = false;

            // If we're exporting these variables, then these just become assignments to 'exports.x'.
            foreach (var variable in node.DeclarationList!.Declarations!)
            {
                if (NodeTests.IsIdentifier(variable.Name!) && FactoryUtilities.IsLocalName(variable.Name!))
                {
                    // A "local name" generally means a variable declaration that *shouldn't* be
                    // converted to `exports.x = ...`, even if the declaration is exported. This
                    // usually indicates a class or function declaration that was converted into
                    // a variable declaration, as most references to the declaration will remain
                    // untransformed (i.e., `new C` rather than `new exports.C`). In these cases,
                    // an `export { x }` declaration will follow.
                    if (modifiers is null)
                    {
                        modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
                    }

                    if (variable.Initializer is not null)
                    {
                        var updatedVariable = _factory.UpdateVariableDeclaration(
                            variable,
                            variable.Name,
                            /*exclamationToken*/ null,
                            /*type*/ null,
                            CreateExportExpression(
                                (Identifier)variable.Name!,
                                Utilities.VisitNode(variable.Initializer, Visitor, Utilities.IsExpression)!));
                        variables = Append(variables, updatedVariable);
                    }
                    else
                    {
                        variables = Append(variables, variable);
                    }
                }
                else if (variable.Initializer is not null)
                {
                    if (!Utilities.IsBindingPattern(variable.Name)
                        && (NodeTests.IsArrowFunction(variable.Initializer)
                            || NodeTests.IsFunctionExpression(variable.Initializer)
                            || NodeTests.IsClassExpression(variable.Initializer)))
                    {
                        var expression = _factory.CreateAssignment(
                            Utilities.SetTextRange(
                                _factory.CreatePropertyAccessExpression(
                                    _factory.CreateIdentifier("exports", null, null),
                                    variable.Name),
                                /*location*/ variable.Name!),
                            _factory.CreateIdentifier(Utilities.GetTextOfIdentifierOrLiteral(variable.Name!), null, null));
                        var updatedVariable = _factory.CreateVariableDeclaration(
                            variable.Name,
                            variable.ExclamationToken,
                            variable.Type,
                            Utilities.VisitNode(variable.Initializer, Visitor, Utilities.IsExpression));

                        variables = Append(variables, updatedVariable);
                        expressions = Append(expressions, expression);
                        removeCommentsOnExpressions = true;
                    }
                    else
                    {
                        expressions = Append(expressions, TransformInitializedVariable(variable));
                    }
                }
            }

            if (variables is not null)
            {
                statements = Append(
                    statements,
                    _factory.UpdateVariableStatement(node, modifiers, _factory.UpdateVariableDeclarationList(node.DeclarationList!, variables)));
            }

            if (expressions is not null)
            {
                var statement = NodeFactory.SetOriginalNode(
                    Utilities.SetTextRange(
                        _factory.CreateExpressionStatement(_factory.InlineExpressions(expressions)),
                        node),
                    node);
                if (removeCommentsOnExpressions)
                {
                    EmitNodes.RemoveAllComments(statement);
                }
                statements = Append(statements, statement);
            }
        }
        else
        {
            statements = Append(statements, Utilities.VisitEachChildWorker(node, Visitor, _context));
        }

        statements = AppendExportsOfVariableStatement(statements, node);
        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1905-1924 — <c>createAllExportExpressions</c>.</summary>
    private Expression CreateAllExportExpressions(Identifier name, Expression value, ITextRange? location)
    {
        var exportedNames = GetExports(name);
        if (exportedNames is not null)
        {
            // For each additional export of the declaration, apply an export assignment.
            Expression expression = FactoryUtilities.IsExportName(name) ? value : _factory.CreateAssignment(name, value);
            foreach (var exportName in exportedNames)
            {
                // Mark the node to prevent triggering substitution.
                EmitNodes.SetEmitFlags(expression, EmitFlags.NoSubstitution);
                expression = CreateExportExpression(exportName, expression, /*location*/ location);
            }

            return expression;
        }
        return _factory.CreateAssignment(name, value);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:1926-1958 — <c>transformInitializedVariable</c>.</summary>
    private Expression TransformInitializedVariable(VariableDeclaration node)
    {
        if (Utilities.IsBindingPattern(node.Name))
        {
            return Destructuring.FlattenDestructuringAssignment(
                Utilities.VisitNode(node, Visitor, IsInitializedVariable)!,
                Visitor,
                _context,
                FlattenLevel.All,
                /*needsValue*/ false,
                CreateAllExportExpressions);
        }
        else
        {
            return _factory.CreateAssignment(
                Utilities.SetTextRange(
                    _factory.CreatePropertyAccessExpression(
                        _factory.CreateIdentifier("exports", null, null),
                        node.Name),
                    /*location*/ node.Name),
                node.Initializer is not null
                    ? Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression)
                    : _factory.CreateVoidZero());
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:8024-8026 — <c>isInitializedVariable</c>.</summary>
    /// <remarks>PORT-NOTE: <c>utilities.ts</c>'s predicate; its port home is <c>TypeScriptTransformSupport</c>, which
    /// keeps its copy private for its own <c>getInitializedVariables</c> reader, so the module transform states its own
    /// (the same "second copy" <c>GeneratorsTransform</c> records for two logical-operator predicates).</remarks>
    private static bool IsInitializedVariable(Node node) => ((VariableDeclaration)node).Initializer is not null;

    /// <summary>TS's <c>visitEachChild</c> with a possibly-missing node (src/compiler/visitorPublic.ts:598-601).</summary>
    /// <remarks>PORT-NOTE: TS returns <c>undefined</c> for an <c>undefined</c> node; the port's
    /// <see cref="Utilities.VisitEachChildWorker{TNode}"/> requires a node, so this is that early return.</remarks>
    private TNode? VisitEachChildOrNull<TNode>(TNode? node, Func<Node, object?> visitor)
        where TNode : Node
        => node is null ? null : Utilities.VisitEachChildWorker(node, visitor, _context);
}
