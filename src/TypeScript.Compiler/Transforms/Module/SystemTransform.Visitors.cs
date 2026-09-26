#nullable enable

namespace TypeScript.Compiler;

/// <summary>ported from: src/compiler/transformers/module/system.ts:723-1756 — the visitors of
/// <c>transformSystemModule</c>: the top-level and nested source-element visitors, the declaration hoisting, the export
/// appending, and the destructuring-assignment visitors.</summary>
/// <remarks>
/// PORT-NOTE (the visitor slots): TS's <c>visitor</c>/<c>topLevelVisitor</c>/&hellip; are the <c>Visitor</c> function
/// type, which the port spells <c>Func&lt;Node, object?&gt;</c>, and <c>VisitResult&lt;T&gt;</c> is
/// <c>T | readonly Node[] | undefined</c>, which the port's slots erase to <c>object?</c> (the splittings are the same
/// ones <c>Utilities.VisitNode</c>/<c>VisitNodes</c> make).
/// </remarks>
internal sealed partial class SystemTransformer
{
    //
    // Top-level Source Element Visitors
    //

    /// <summary>ported from: src/compiler/transformers/module/system.ts:729-746 — <c>topLevelVisitor</c>.</summary>
    private object? TopLevelVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.ImportDeclaration => VisitImportDeclaration((ImportDeclaration)node),
            SyntaxKind.ImportEqualsDeclaration => VisitImportEqualsDeclaration((ImportEqualsDeclaration)node),
            SyntaxKind.ExportDeclaration => VisitExportDeclaration((ExportDeclaration)node),
            SyntaxKind.ExportAssignment => VisitExportAssignment((ExportAssignment)node),
            _ => TopLevelNestedVisitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/module/system.ts:753-759 — <c>visitImportDeclaration</c>.</summary>
    private object? VisitImportDeclaration(ImportDeclaration node)
    {
        List<IStatement>? statements = null;
        if (node.ImportClause is not null)
        {
            _context.HoistVariableDeclaration(ModuleTransformSupport.GetLocalNameForExternalImport(_factory, node, _currentSourceFile!)!); // TODO: GH#18217
        }
        return ModuleTransformSupport.SingleOrMany(AppendExportsOfImportDeclaration(statements, node));
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:761-764 — <c>visitExportDeclaration</c>.</summary>
    private object? VisitExportDeclaration(ExportDeclaration node)
    {
        Debug.AssertIsDefined(node);
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:771-777 — <c>visitImportEqualsDeclaration</c>.</summary>
    private object? VisitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        Debug.Assert(Utilities.IsExternalModuleImportEqualsDeclaration(node), "import= for internal module references should be handled in an earlier transformer.");

        List<IStatement>? statements = null;
        _context.HoistVariableDeclaration(ModuleTransformSupport.GetLocalNameForExternalImport(_factory, node, _currentSourceFile!)!); // TODO: GH#18217
        return ModuleTransformSupport.SingleOrMany(AppendExportsOfImportEqualsDeclaration(statements, node));
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:784-792 — <c>visitExportAssignment</c>.</summary>
    private object? VisitExportAssignment(ExportAssignment node)
    {
        if (node.IsExportEquals == true)
        {
            // Elide `export=` as it is illegal in a SystemJS module.
            return null;
        }

        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
        return CreateExportStatement(_factory.CreateIdentifier("default", null, null), expression!, /*allowComments*/ true);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:799-821 — <c>visitFunctionDeclaration</c>.</summary>
    private object? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            _hoistedStatements = Append(
                _hoistedStatements,
                _factory.UpdateFunctionDeclaration(
                    node,
                    Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifierLike),
                    node.AsteriskToken,
                    TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ true),
                    /*typeParameters*/ null,
                    Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
                    /*type*/ null,
                    Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock)));
        }
        else
        {
            _hoistedStatements = Append(_hoistedStatements, Utilities.VisitEachChildWorker(node, Visitor, _context));
        }

        _hoistedStatements = AppendExportsOfHoistedDeclaration(_hoistedStatements, node);
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:828-860 — <c>visitClassDeclaration</c>.</summary>
    private object? VisitClassDeclaration(ClassDeclaration node)
    {
        List<IStatement>? statements = null;

        // Hoist the name of the class declaration to the outer module body function.
        var name = TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null);
        _context.HoistVariableDeclaration(name);

        // Rewrite the class declaration into an assignment of a class expression.
        statements = Append(
            statements,
            Utilities.SetTextRange(
                _factory.CreateExpressionStatement(
                    _factory.CreateAssignment(
                        name,
                        Utilities.SetTextRange(
                            _factory.CreateClassExpression(
                                Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifierLike),
                                node.Name,
                                /*typeParameters*/ null,
                                Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause),
                                Utilities.VisitNodes(node.Members, Visitor, Utilities.IsClassElement)),
                            node))),
                node));

        statements = AppendExportsOfHoistedDeclaration(statements, node);
        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:868-915 — <c>visitVariableStatement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's two <c>isVarUsing</c>/<c>isVarAwaitUsing</c> reads
    /// (<c>utilities.ts:2658-2675</c>) have no shared port member yet — this class's own
    /// <see cref="IsVarUsing"/>/<see cref="IsVarAwaitUsing"/> state their bodies — and
    /// <c>factory.getGeneratedNameForNode(variable.name)</c> is the port's two-argument
    /// <c>GetGeneratedNameForNode</c>.</remarks>
    private object? VisitVariableStatement(VariableStatement node)
    {
        if (!ShouldHoistVariableDeclarationList(node.DeclarationList!))
        {
            return Utilities.VisitNode(node, Visitor, Utilities.IsStatement);
        }

        List<IStatement>? statements = null;
        // `using` and `await using` declarations cannot be hoisted directly, so we will hoist the variable name
        // as a normal variable, and declare it as a temp variable that remains as a `using` to ensure the correct
        // lifetime.
        if (IsVarUsing(node.DeclarationList!) || IsVarAwaitUsing(node.DeclarationList!))
        {
            var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifierLike);
            var declarations = new List<VariableDeclaration>();
            foreach (var variable in node.DeclarationList!.Declarations!)
            {
                declarations.Add(_factory.UpdateVariableDeclaration(
                    variable,
                    _factory.GetGeneratedNameForNode(variable.Name, /*flags*/ null),
                    /*exclamationToken*/ null,
                    /*type*/ null,
                    TransformInitializedVariable(variable, /*isExportedDeclaration*/ false)));
            }

            var declarationList = _factory.UpdateVariableDeclarationList(
                node.DeclarationList,
                declarations);
            statements = Append(statements, _factory.UpdateVariableStatement(node, modifiers, declarationList));
        }
        else
        {
            List<Expression>? expressions = null;
            var isExportedDeclaration = Utilities.HasSyntacticModifier(node, ModifierFlags.Export);
            foreach (var variable in node.DeclarationList!.Declarations!)
            {
                if (variable.Initializer is not null)
                {
                    expressions = Append(expressions, TransformInitializedVariable(variable, isExportedDeclaration));
                }
                else
                {
                    HoistBindingElement(variable);
                }
            }

            if (expressions is not null)
            {
                statements = Append(statements, Utilities.SetTextRange(_factory.CreateExpressionStatement(_factory.InlineExpressions(expressions)), node));
            }
        }

        statements = AppendExportsOfVariableStatement(statements, node, /*exportSelf*/ false);
        return ModuleTransformSupport.SingleOrMany(statements);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:922-933 — <c>hoistBindingElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>VariableDeclaration | BindingElement</c>, both of which carry
    /// <c>name</c>; the port's parameter is the <see cref="Node"/> the union erases to and the <c>name</c> read is the
    /// switch below. The <c>elements</c> read of a binding pattern splits over the two pattern classes for the same
    /// reason (<see cref="GetBindingPatternElements"/>).</remarks>
    private void HoistBindingElement(Node node)
    {
        var name = node switch
        {
            VariableDeclaration declaration => declaration.Name,
            BindingElement element => element.Name,
            _ => null,
        };
        if (Utilities.IsBindingPattern(name))
        {
            foreach (var element in GetBindingPatternElements(name!))
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    HoistBindingElement(element);
                }
            }
        }
        else
        {
            _context.HoistVariableDeclaration(_factory.CloneNode((Identifier)name!));
        }
    }

    /// <summary>The <c>elements</c> read of the two binding-pattern classes, for the loops <c>hoistBindingElement</c>
    /// and <c>appendExportsOfBindingElement</c> share.</summary>
    /// <remarks>PORT-NOTE: TS's <c>isBindingPattern(node)</c> narrows to <c>ObjectBindingPattern | ArrayBindingPattern</c>
    /// and its readers use the union's <c>elements</c>; the model gives the two classes different element types
    /// (<c>BindingElement</c> and <c>Node</c>), so this reader states them as the <c>Node</c> elements both call sites
    /// walk.</remarks>
    private static IEnumerable<Node> GetBindingPatternElements(Node name)
    {
        if (name is ObjectBindingPattern objectPattern)
        {
            return (IEnumerable<Node>?)objectPattern.Elements ?? [];
        }
        if (name is ArrayBindingPattern arrayPattern)
        {
            return arrayPattern.Elements ?? [];
        }
        return [];
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:940-945 — <c>shouldHoistVariableDeclarationList</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>node.flags</c> in <c>(getEmitFlags(node) &amp; EmitFlags.NoHoisting) === 0</c> is the
    /// emit-node flags read (<see cref="Utilities.GetEmitFlags"/>) and <c>getOriginalNode(node).flags</c> is the
    /// <see cref="NodeFlags"/> of the original node (both zero-tests as written).</remarks>
    private bool ShouldHoistVariableDeclarationList(VariableDeclarationList node)
        // hoist only non-block scoped declarations or block scoped declarations parented by source file
        => (Utilities.GetEmitFlags(node) & EmitFlags.NoHoisting) == 0
            && (_enclosingBlockScopedContainer!.Kind == SyntaxKind.SourceFile
                || (Utilities.GetOriginalNode(node)!.Flags & NodeFlags.BlockScoped) == 0);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:953-965 — <c>transformInitializedVariable</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>createAssignment</c> local is one of the two callbacks; the port states the delegate
    /// type <c>flattenDestructuringAssignment</c>'s sixth parameter takes and picks the callback with the reference's
    /// own conditional. The final arm's <c>(Expression)</c> is TS's <c>BindingName</c>-to-<c>Expression</c> narrowing,
    /// which the model spells as a cast from <see cref="Node"/>.</remarks>
    private Expression TransformInitializedVariable(VariableDeclaration node, bool isExportedDeclaration)
    {
        Func<Identifier, Expression, ITextRange?, Expression> createAssignment = isExportedDeclaration
            ? CreateExportedVariableAssignment
            : CreateNonExportedVariableAssignment;
        if (Utilities.IsBindingPattern(node.Name))
        {
            return Destructuring.FlattenDestructuringAssignment(
                node,
                Visitor,
                _context,
                FlattenLevel.All,
                /*needsValue*/ false,
                createAssignment);
        }
        if (node.Initializer is not null)
        {
            return createAssignment((Identifier)node.Name!, Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression)!, /*location*/ null);
        }
        return (Expression)node.Name!;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:974-976 — <c>createExportedVariableAssignment</c>.</summary>
    private Expression CreateExportedVariableAssignment(Identifier name, Expression value, ITextRange? location)
        => CreateVariableAssignment(name, value, location, /*isExportedDeclaration*/ true);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:985-987 — <c>createNonExportedVariableAssignment</c>.</summary>
    private Expression CreateNonExportedVariableAssignment(Identifier name, Expression value, ITextRange? location)
        => CreateVariableAssignment(name, value, location, /*isExportedDeclaration*/ false);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:997-1002 — <c>createVariableAssignment</c>.</summary>
    private Expression CreateVariableAssignment(Identifier name, Expression value, ITextRange? location, bool isExportedDeclaration)
    {
        _context.HoistVariableDeclaration(_factory.CloneNode(name));
        return isExportedDeclaration
            ? CreateExportExpression(name, PreventSubstitution(Utilities.SetTextRange(_factory.CreateAssignment(name, value), location)))
            : PreventSubstitution(Utilities.SetTextRange(_factory.CreateAssignment(name, value), location));
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1013-1044 — <c>appendExportsOfImportDeclaration</c>.</summary>
    private List<IStatement>? AppendExportsOfImportDeclaration(List<IStatement>? statements, ImportDeclaration decl)
    {
        if (_moduleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        var importClause = decl.ImportClause;
        if (importClause is null)
        {
            return statements;
        }

        if (importClause.Name is not null)
        {
            statements = AppendExportsOfDeclaration(statements, importClause);
        }

        var namedBindings = importClause.NamedBindings;
        if (namedBindings is not null)
        {
            switch (namedBindings.Kind)
            {
                case SyntaxKind.NamespaceImport:
                    statements = AppendExportsOfDeclaration(statements, namedBindings);
                    break;

                case SyntaxKind.NamedImports:
                    foreach (var importBinding in ((NamedImports)namedBindings).Elements!)
                    {
                        statements = AppendExportsOfDeclaration(statements, importBinding);
                    }

                    break;
            }
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1055-1061 — <c>appendExportsOfImportEqualsDeclaration</c>.</summary>
    private List<IStatement>? AppendExportsOfImportEqualsDeclaration(List<IStatement>? statements, ImportEqualsDeclaration decl)
    {
        if (_moduleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        return AppendExportsOfDeclaration(statements, decl);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1074-1086 — <c>appendExportsOfVariableStatement</c>.</summary>
    private List<IStatement>? AppendExportsOfVariableStatement(List<IStatement>? statements, VariableStatement node, bool exportSelf)
    {
        if (_moduleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        foreach (var decl in node.DeclarationList!.Declarations!)
        {
            if (decl.Initializer is not null || exportSelf)
            {
                statements = AppendExportsOfBindingElement(statements, decl, exportSelf);
            }
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1098-1121 — <c>appendExportsOfBindingElement</c>.</summary>
    /// <remarks>PORT-NOTE: as <c>hoistBindingElement</c>, TS's union parameter <c>VariableDeclaration | BindingElement</c>
    /// erases to <see cref="Node"/> and its <c>name</c> read is the switch below.</remarks>
    private List<IStatement>? AppendExportsOfBindingElement(List<IStatement>? statements, Node decl, bool exportSelf)
    {
        if (_moduleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        var name = decl switch
        {
            VariableDeclaration declaration => declaration.Name,
            BindingElement element => element.Name,
            _ => null,
        };
        if (Utilities.IsBindingPattern(name))
        {
            foreach (var element in GetBindingPatternElements(name!))
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    statements = AppendExportsOfBindingElement(statements, element, exportSelf);
                }
            }
        }
        else if (!Utilities.IsGeneratedIdentifier(name!))
        {
            string? excludeName = null;
            if (exportSelf)
            {
                statements = AppendExportStatement(statements, name!, TypeScriptTransformSupport.GetLocalName(_factory, decl, /*allowComments*/ null, /*allowSourceMaps*/ null));
                excludeName = Utilities.IdText(name!);
            }

            statements = AppendExportsOfDeclaration(statements, decl, excludeName);
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1132-1149 — <c>appendExportsOfHoistedDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's union parameter <c>ClassDeclaration | FunctionDeclaration</c> erases to
    /// <see cref="Node"/> and its <c>decl.name</c> read is the switch below; the two <c>decl.name</c> reads share that
    /// local.</remarks>
    private List<IStatement>? AppendExportsOfHoistedDeclaration(List<IStatement>? statements, Node decl)
    {
        if (_moduleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        var name = decl switch
        {
            ClassDeclaration classDeclaration => (Node?)classDeclaration.Name,
            FunctionDeclaration functionDeclaration => functionDeclaration.Name,
            _ => null,
        };
        string? excludeName = null;
        if (Utilities.HasSyntacticModifier(decl, ModifierFlags.Export))
        {
            Node exportName = Utilities.HasSyntacticModifier(decl, ModifierFlags.Default)
                ? _factory.CreateStringLiteral("default", null, null)
                : name!;
            statements = AppendExportStatement(statements, exportName, TypeScriptTransformSupport.GetLocalName(_factory, decl, /*allowComments*/ null, /*allowSourceMaps*/ null));
            excludeName = Utilities.GetTextOfIdentifierOrLiteral(exportName);
        }

        if (name is not null)
        {
            statements = AppendExportsOfDeclaration(statements, decl, excludeName);
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1160-1175 — <c>appendExportsOfDeclaration</c>.</summary>
    private List<IStatement>? AppendExportsOfDeclaration(List<IStatement>? statements, Node decl, string? excludeName = null)
    {
        if (_moduleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        var name = TransformationUtilities.GetDeclarationName(_factory, decl);
        var exportSpecifiers = _moduleInfo.ExportSpecifiers.Get(name);
        if (exportSpecifiers is not null)
        {
            foreach (var exportSpecifier in exportSpecifiers)
            {
                if (Utilities.ModuleExportNameTextUnescaped(exportSpecifier.Name) != excludeName)
                {
                    statements = AppendExportStatement(statements, exportSpecifier.Name!, name);
                }
            }
        }
        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1188-1191 — <c>appendExportStatement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>exportName</c> is the union <c>Identifier | StringLiteral</c> (<c>ModuleExportName</c>);
    /// the port's slot is the <see cref="Node"/> the union erases to, which is the same reading
    /// <c>Utilities.ModuleExportNameTextUnescaped</c> takes.</remarks>
    private List<IStatement>? AppendExportStatement(List<IStatement>? statements, Node exportName, Expression expression, bool? allowComments = null)
    {
        statements = Append(statements, CreateExportStatement(exportName, expression, allowComments));
        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1200-1208 — <c>createExportStatement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>!allowComments</c> over an optional boolean is the port's
    /// <c>allowComments != true</c>, and <c>startOnNewLine</c> is <see cref="FactoryUtilities.StartOnNewLine{T}"/>.</remarks>
    private ExpressionStatement CreateExportStatement(Node name, Expression value, bool? allowComments)
    {
        var statement = _factory.CreateExpressionStatement(CreateExportExpression(name, value));
        FactoryUtilities.StartOnNewLine(statement);
        if (allowComments != true)
        {
            EmitNodes.SetEmitFlags(statement, EmitFlags.NoComments);
        }

        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1216-1221 — <c>createExportExpression</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>isIdentifier(name) ? createStringLiteralFromNode(name) : name</c> yields the
    /// string-ish node the call takes; the port's two arms are the same two nodes typed as the
    /// <see cref="Expression"/> the argument list needs.</remarks>
    private Expression CreateExportExpression(Node name, Expression value)
    {
        var exportName = NodeTests.IsIdentifier(name)
            ? (Expression)_factory.CreateStringLiteralFromNode(name)
            : (Expression)name;
        EmitNodes.SetEmitFlags(value, Utilities.GetEmitFlags(value) | EmitFlags.NoComments);
        return EmitNodes.SetCommentRange(
            _factory.CreateCallExpression(_exportFunction!, /*typeArguments*/ null, [exportName, value]),
            value);
    }

    //
    // Top-Level or Nested Source Element Visitors
    //

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1231-1290 — <c>topLevelNestedVisitor</c>.</summary>
    private object? TopLevelNestedVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.VariableStatement => VisitVariableStatement((VariableStatement)node),
            SyntaxKind.FunctionDeclaration => VisitFunctionDeclaration((FunctionDeclaration)node),
            SyntaxKind.ClassDeclaration => VisitClassDeclaration((ClassDeclaration)node),
            SyntaxKind.ForStatement => VisitForStatement((ForStatement)node, /*isTopLevel*/ true),
            SyntaxKind.ForInStatement => VisitForInStatement((ForInStatement)node),
            SyntaxKind.ForOfStatement => VisitForOfStatement((ForOfStatement)node),
            SyntaxKind.DoStatement => VisitDoStatement((DoStatement)node),
            SyntaxKind.WhileStatement => VisitWhileStatement((WhileStatement)node),
            SyntaxKind.LabeledStatement => VisitLabeledStatement((LabeledStatement)node),
            SyntaxKind.WithStatement => VisitWithStatement((WithStatement)node),
            SyntaxKind.IfStatement => VisitIfStatement((IfStatement)node),
            SyntaxKind.SwitchStatement => VisitSwitchStatement((SwitchStatement)node),
            SyntaxKind.CaseBlock => VisitCaseBlock((CaseBlock)node),
            SyntaxKind.CaseClause => VisitCaseClause((CaseClause)node),
            SyntaxKind.DefaultClause => VisitDefaultClause((DefaultClause)node),
            SyntaxKind.TryStatement => VisitTryStatement((TryStatement)node),
            SyntaxKind.CatchClause => VisitCatchClause((CatchClause)node),
            SyntaxKind.Block => VisitBlock((Block)node),
            _ => Visitor(node),
        };

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1297-1311 — <c>visitForStatement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>isTopLevel ? visitForInitializer : discardedValueVisitor</c> and
    /// <c>isTopLevel ? topLevelNestedVisitor : visitor</c> are the two visitor slots the loop hands to <c>visitNode</c>
    /// and <c>visitIterationBody</c>; the port's locals state those choices once.</remarks>
    private object? VisitForStatement(ForStatement node, bool isTopLevel)
    {
        var savedEnclosingBlockScopedContainer = _enclosingBlockScopedContainer;
        _enclosingBlockScopedContainer = node;

        Func<Node, object?> initializerVisitor = isTopLevel ? VisitForInitializer : DiscardedValueVisitor;
        Func<Node, object?> statementVisitor = isTopLevel ? TopLevelNestedVisitor : Visitor;
        node = _factory.UpdateForStatement(
            node,
            Utilities.VisitNode(node.Initializer, initializerVisitor, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.Incrementor, DiscardedValueVisitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, statementVisitor, _context));

        _enclosingBlockScopedContainer = savedEnclosingBlockScopedContainer;
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1318-1331 — <c>visitForInStatement</c>.</summary>
    private object? VisitForInStatement(ForInStatement node)
    {
        var savedEnclosingBlockScopedContainer = _enclosingBlockScopedContainer;
        _enclosingBlockScopedContainer = node;

        node = _factory.UpdateForInStatement(
            node,
            VisitForInitializer(node.Initializer!),
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context));

        _enclosingBlockScopedContainer = savedEnclosingBlockScopedContainer;
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1338-1352 — <c>visitForOfStatement</c>.</summary>
    private object? VisitForOfStatement(ForOfStatement node)
    {
        var savedEnclosingBlockScopedContainer = _enclosingBlockScopedContainer;
        _enclosingBlockScopedContainer = node;

        node = _factory.UpdateForOfStatement(
            node,
            node.AwaitModifier,
            VisitForInitializer(node.Initializer!),
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context));

        _enclosingBlockScopedContainer = savedEnclosingBlockScopedContainer;
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1360-1363 — <c>shouldHoistForInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: TS's guard narrows to <c>VariableDeclarationList</c> and this port's callers read the narrowed
    /// node with their own cast, so the member answers <c>bool</c> (mapping rule (d)).</remarks>
    private bool ShouldHoistForInitializer(Node node)
        => NodeTests.IsVariableDeclarationList(node)
            && ShouldHoistVariableDeclarationList((VariableDeclarationList)node);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1370-1385 — <c>visitForInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: TS's return is the <c>ForInitializer</c> union, which the port's callers pass to the
    /// <c>For*</c> updaters' <c>Node?</c> slot; the <c>visitNode</c> arm therefore keeps its nullable answer.</remarks>
    private Node? VisitForInitializer(Node node)
    {
        if (ShouldHoistForInitializer(node))
        {
            List<Expression>? expressions = null;
            foreach (var variable in ((VariableDeclarationList)node).Declarations!)
            {
                expressions = Append(expressions, TransformInitializedVariable(variable, /*isExportedDeclaration*/ false));
                if (variable.Initializer is null)
                {
                    HoistBindingElement(variable);
                }
            }

            return expressions is not null ? _factory.InlineExpressions(expressions) : _factory.CreateOmittedExpression();
        }
        else
        {
            return Utilities.VisitNode(node, DiscardedValueVisitor, Utilities.IsForInitializer);
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1392-1398 — <c>visitDoStatement</c>.</summary>
    private object? VisitDoStatement(DoStatement node)
        => _factory.UpdateDoStatement(
            node,
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context),
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1405-1411 — <c>visitWhileStatement</c>.</summary>
    private object? VisitWhileStatement(WhileStatement node)
        => _factory.UpdateWhileStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, TopLevelNestedVisitor, _context));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1418-1424 — <c>visitLabeledStatement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>?? factory.createExpressionStatement(factory.createIdentifier(""))</c> gives
    /// the <c>isStatement</c> lift's absent answer a statement; the port's <c>??</c> is that same fallback.</remarks>
    private object? VisitLabeledStatement(LabeledStatement node)
        => _factory.UpdateLabeledStatement(
            node,
            node.Label,
            Utilities.VisitNode(node.Statement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock)
                ?? _factory.CreateExpressionStatement(_factory.CreateIdentifier("", null, null)));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1431-1437 — <c>visitWithStatement</c>.</summary>
    private object? VisitWithStatement(WithStatement node)
        => _factory.UpdateWithStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Debug.CheckDefined(Utilities.VisitNode(node.Statement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock)));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1444-1451 — <c>visitIfStatement</c>.</summary>
    private object? VisitIfStatement(IfStatement node)
        => _factory.UpdateIfStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.ThenStatement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock) ?? _factory.CreateBlock([], null),
            Utilities.VisitNode(node.ElseStatement, TopLevelNestedVisitor, Utilities.IsStatement, _liftToBlock));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1458-1464 — <c>visitSwitchStatement</c>.</summary>
    private object? VisitSwitchStatement(SwitchStatement node)
        => _factory.UpdateSwitchStatement(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Debug.CheckDefined(Utilities.VisitNode(node.CaseBlock, TopLevelNestedVisitor, NodeTests.IsCaseBlock)));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1471-1482 — <c>visitCaseBlock</c>.</summary>
    private CaseBlock VisitCaseBlock(CaseBlock node)
    {
        var savedEnclosingBlockScopedContainer = _enclosingBlockScopedContainer;
        _enclosingBlockScopedContainer = node;

        node = _factory.UpdateCaseBlock(
            node,
            Utilities.VisitNodes(node.Clauses, TopLevelNestedVisitor, Utilities.IsCaseOrDefaultClause));

        _enclosingBlockScopedContainer = savedEnclosingBlockScopedContainer;
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1489-1495 — <c>visitCaseClause</c>.</summary>
    private object? VisitCaseClause(CaseClause node)
        => _factory.UpdateCaseClause(
            node,
            Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression),
            Utilities.VisitNodes(node.Statements, TopLevelNestedVisitor, Utilities.IsStatement));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1502-1504 — <c>visitDefaultClause</c>.</summary>
    private object? VisitDefaultClause(DefaultClause node)
        => Utilities.VisitEachChildWorker(node, TopLevelNestedVisitor, _context);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1511-1513 — <c>visitTryStatement</c>.</summary>
    private object? VisitTryStatement(TryStatement node)
        => Utilities.VisitEachChildWorker(node, TopLevelNestedVisitor, _context);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1520-1532 — <c>visitCatchClause</c>.</summary>
    private CatchClause VisitCatchClause(CatchClause node)
    {
        var savedEnclosingBlockScopedContainer = _enclosingBlockScopedContainer;
        _enclosingBlockScopedContainer = node;

        node = _factory.UpdateCatchClause(
            node,
            node.VariableDeclaration,
            Debug.CheckDefined(Utilities.VisitNode(node.Block, TopLevelNestedVisitor, NodeTests.IsBlock)));

        _enclosingBlockScopedContainer = savedEnclosingBlockScopedContainer;
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1539-1547 — <c>visitBlock</c>.</summary>
    private Block VisitBlock(Block node)
    {
        var savedEnclosingBlockScopedContainer = _enclosingBlockScopedContainer;
        _enclosingBlockScopedContainer = node;

        node = Utilities.VisitEachChildWorker(node, TopLevelNestedVisitor, _context);

        _enclosingBlockScopedContainer = savedEnclosingBlockScopedContainer;
        return node;
    }

    //
    // Destructuring Assignment Visitors
    //

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1558-1586 — <c>visitorWorker</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's three <c>if (…) return visit…</c> arms inside a <c>case</c>
    /// (<c>isDestructuringAssignment</c>, <c>isImportCall</c>) are the port's <c>when</c> clauses, which take the same
    /// branch and otherwise fall through to the shared <c>visitEachChild</c>.</remarks>
    private object? VisitorWorker(Node node, bool valueIsDiscarded)
    {
        if ((node.TransformFlags & (TransformFlags.ContainsDestructuringAssignment | TransformFlags.ContainsDynamicImport | TransformFlags.ContainsUpdateExpressionForIdentifier)) == 0)
        {
            return node;
        }
        switch (node.Kind)
        {
            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node, /*isTopLevel*/ false);
            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement((ExpressionStatement)node);
            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression((ParenthesizedExpression)node, valueIsDiscarded);
            case SyntaxKind.PartiallyEmittedExpression:
                return VisitPartiallyEmittedExpression((PartiallyEmittedExpression)node, valueIsDiscarded);
            case SyntaxKind.BinaryExpression when Utilities.IsDestructuringAssignment(node):
                return VisitDestructuringAssignment((BinaryExpression)node, valueIsDiscarded);
            case SyntaxKind.CallExpression when Utilities.IsImportCall(node):
                return VisitImportCallExpression((CallExpression)node);
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPrefixOrPostfixUnaryExpression(node, valueIsDiscarded);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1593-1595 — <c>visitor</c>.</summary>
    private object? Visitor(Node node) => VisitorWorker(node, /*valueIsDiscarded*/ false);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1597-1599 — <c>discardedValueVisitor</c>.</summary>
    private object? DiscardedValueVisitor(Node node) => VisitorWorker(node, /*valueIsDiscarded*/ true);

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1601-1603 — <c>visitExpressionStatement</c>.</summary>
    private object? VisitExpressionStatement(ExpressionStatement node)
        => _factory.UpdateExpressionStatement(node, Utilities.VisitNode(node.Expression, DiscardedValueVisitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1605-1607 — <c>visitParenthesizedExpression</c>.</summary>
    private object? VisitParenthesizedExpression(ParenthesizedExpression node, bool valueIsDiscarded)
        => _factory.UpdateParenthesizedExpression(node, Utilities.VisitNode(node.Expression, valueIsDiscarded ? DiscardedValueVisitor : Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1609-1611 — <c>visitPartiallyEmittedExpression</c>.</summary>
    private object? VisitPartiallyEmittedExpression(PartiallyEmittedExpression node, bool valueIsDiscarded)
        => _factory.UpdatePartiallyEmittedExpression(node, Utilities.VisitNode(node.Expression, valueIsDiscarded ? DiscardedValueVisitor : Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1613-1636 — <c>visitImportCallExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ImportCall</c> is a <c>CallExpression</c> with typed arguments, which the model spells
    /// as the <see cref="CallExpression"/> class (the <c>IImportCall</c> interface carries no slots); <c>firstOrUndefined</c>
    /// is <see cref="Core.FirstOrUndefined{T}(IList{T}?)"/>.</remarks>
    private Expression VisitImportCallExpression(CallExpression node)
    {
        // import("./blah")
        // emit as
        // System.register([], function (_export, _context) {
        //     return {
        //         setters: [],
        //         execute: () => {
        //             _context.import('./blah');
        //         }
        //     };
        // });
        var externalModuleName = ModuleTransformSupport.GetExternalModuleNameLiteral(_factory, node, _currentSourceFile!, _host, _resolver, _compilerOptions);
        var firstArgument = Utilities.VisitNode(Core.FirstOrUndefined(node.Arguments), Visitor, Utilities.IsExpression);
        // Only use the external module name if it differs from the first argument. This allows us to preserve the quote style of the argument on output.
        var argument = externalModuleName is not null
            && (firstArgument is null || !NodeTests.IsStringLiteral(firstArgument) || ((StringLiteral)firstArgument).Text != externalModuleName.Text)
            ? (Expression)externalModuleName
            : firstArgument;
        return _factory.CreateCallExpression(
            _factory.CreatePropertyAccessExpression(
                _contextObject!,
                _factory.CreateIdentifier("import", null, null)),
            /*typeArguments*/ null,
            argument is not null ? [argument] : []);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1643-1655 — <c>visitDestructuringAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>DestructuringAssignment</c> is a <c>BinaryExpression</c> with a typed <c>left</c>, which
    /// the model spells as the <see cref="BinaryExpression"/> class.</remarks>
    private object? VisitDestructuringAssignment(BinaryExpression node, bool valueIsDiscarded)
    {
        if (HasExportedReferenceInDestructuringTarget(node.Left!))
        {
            return Destructuring.FlattenDestructuringAssignment(
                node,
                Visitor,
                _context,
                FlattenLevel.All,
                !valueIsDiscarded);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1662-1688 — <c>hasExportedReferenceInDestructuringTarget</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>Expression | ObjectLiteralElementLike</c> and its
    /// <c>some(…, hasExportedReferenceInDestructuringTarget)</c> calls pass the predicate directly; the port's parameter is
    /// the <see cref="Node"/> the union erases to, which is the predicate's own type, so those method-group conversions are
    /// unchanged.</remarks>
    private bool HasExportedReferenceInDestructuringTarget(Node node)
    {
        if (Utilities.IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true))
        {
            return HasExportedReferenceInDestructuringTarget(((BinaryExpression)node).Left!);
        }
        else if (NodeTests.IsSpreadElement(node))
        {
            return HasExportedReferenceInDestructuringTarget(((SpreadElement)node).Expression!);
        }
        else if (NodeTests.IsObjectLiteralExpression(node))
        {
            return Core.Some(((ObjectLiteralExpression)node).Properties, HasExportedReferenceInDestructuringTarget);
        }
        else if (NodeTests.IsArrayLiteralExpression(node))
        {
            return Core.Some(((ArrayLiteralExpression)node).Elements, HasExportedReferenceInDestructuringTarget);
        }
        else if (NodeTests.IsShorthandPropertyAssignment(node))
        {
            return HasExportedReferenceInDestructuringTarget(((ShorthandPropertyAssignment)node).Name!);
        }
        else if (NodeTests.IsPropertyAssignment(node))
        {
            return HasExportedReferenceInDestructuringTarget(((PropertyAssignment)node).Initializer!);
        }
        else if (NodeTests.IsIdentifier(node))
        {
            var container = _resolver.GetReferencedExportContainer((Identifier)node, /*prefixLocals*/ null);
            return container is not null && container.Kind == SyntaxKind.SourceFile;
        }
        else
        {
            return false;
        }
    }

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1690-1738 — <c>visitPrefixOrPostfixUnaryExpression</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's parameter is the union <c>PrefixUnaryExpression | PostfixUnaryExpression</c>; the port takes the
    /// <see cref="Node"/> the union erases to and reads the two members through the two kind tests, which is the same
    /// discrimination the reference's <c>isPrefixUnaryExpression</c> makes.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>factory.createTempVariable(hoistVariableDeclaration)</c> passes the context's hoist member as the
    /// record callback; the port's lambda states that same callback for the <c>Action&lt;Identifier&gt;</c> slot.
    /// </para>
    /// </remarks>
    private object? VisitPrefixOrPostfixUnaryExpression(Node node, bool valueIsDiscarded)
    {
        // When we see a prefix or postfix increment expression whose operand is an exported
        // symbol, we should ensure all exports of that symbol are updated with the correct
        // value.
        //
        // - We do not transform generated identifiers for any reason.
        // - We do not transform identifiers tagged with the LocalName flag.
        // - We do not transform identifiers that were originally the name of an enum or
        //   namespace due to how they are transformed in TypeScript.
        // - We only transform identifiers that are exported at the top level.
        var isPrefix = NodeTests.IsPrefixUnaryExpression(node);
        var operatorKind = isPrefix ? ((PrefixUnaryExpression)node).Operator : ((PostfixUnaryExpression)node).Operator;
        var operand = isPrefix ? (Node)((PrefixUnaryExpression)node).Operand! : ((PostfixUnaryExpression)node).Operand!;
        if (
            (operatorKind == SyntaxKind.PlusPlusToken || operatorKind == SyntaxKind.MinusMinusToken)
            && NodeTests.IsIdentifier(operand)
            && !Utilities.IsGeneratedIdentifier(operand)
            && !FactoryUtilities.IsLocalName(operand)
            && !ModuleTransformSupport.IsDeclarationNameOfEnumOrNamespace((Identifier)operand)
        ) {
            var exportedNames = GetExports((Identifier)operand);
            if (exportedNames is not null)
            {
                Identifier? temp = null;
                Expression expression = Utilities.VisitNode((Expression)operand, Visitor, Utilities.IsExpression)!;
                if (isPrefix)
                {
                    expression = _factory.UpdatePrefixUnaryExpression((PrefixUnaryExpression)node, expression);
                }
                else
                {
                    expression = _factory.UpdatePostfixUnaryExpression((PostfixUnaryExpression)node, expression);
                    if (!valueIsDiscarded)
                    {
                        temp = _factory.CreateTempVariable(id => _context.HoistVariableDeclaration(id), /*reservedInNestedScopes*/ null);
                        expression = _factory.CreateAssignment(temp, expression);
                        Utilities.SetTextRange(expression, node);
                    }
                    expression = _factory.CreateComma(expression, _factory.CloneNode((Expression)operand));
                    Utilities.SetTextRange(expression, node);
                }

                foreach (var exportName in exportedNames)
                {
                    expression = CreateExportExpression(exportName, PreventSubstitution(expression));
                }

                if (temp is not null)
                {
                    expression = _factory.CreateComma(expression, temp);
                    Utilities.SetTextRange(expression, node);
                }

                return expression;
            }
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    //
    // Modifier Visitors
    //

    /// <summary>ported from: src/compiler/transformers/module/system.ts:1749-1756 — <c>modifierVisitor</c>.</summary>
    private object? ModifierVisitor(Node node)
        => node.Kind switch
        {
            SyntaxKind.ExportKeyword or SyntaxKind.DefaultKeyword => null,
            _ => node,
        };
}
