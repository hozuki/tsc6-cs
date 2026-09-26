#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/module/module.ts:1960-2257 — the export-appending family, the two export
// builders and the modifier visitor.
/// <summary>
/// ported from: src/compiler/transformers/module/module.ts:1960-2257 — the helpers that append a declaration's
/// exports (the <c>exports.x = …</c> / <c>Object.defineProperty(exports, …)</c> statements), the
/// <c>__esModule</c> marker, and the visitor that elides the module-specific modifiers.
/// </summary>
/// <remarks>
/// PORT-NOTE (<c>ModuleExportName</c>): TS's <c>ModuleExportName</c> is the <c>Identifier | StringLiteral</c> union, so
/// every slot of that type in this family is the port's <see cref="Node"/> and its two readers are the kind tests
/// (<see cref="NodeTests.IsStringLiteral"/>, <see cref="Utilities.IdText"/>).
/// </remarks>
internal sealed partial class ModuleTransformer
{
    /// <summary>ported from: src/compiler/transformers/module/module.ts:1960-2001 — <c>appendExportsOfImportDeclaration</c>.</summary>
    private List<IStatement>? AppendExportsOfImportDeclaration(List<IStatement>? statements, ImportDeclaration decl)
    {
        if (_currentModuleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        var importClause = decl.ImportClause;
        if (importClause is null)
        {
            return statements;
        }

        var seen = new TransformationUtilities.IdentifierNameMap<bool>();
        if (importClause.Name is not null)
        {
            statements = AppendExportsOfDeclaration(statements, seen, importClause);
        }

        var namedBindings = importClause.NamedBindings;
        if (namedBindings is not null)
        {
            switch (namedBindings.Kind)
            {
                case SyntaxKind.NamespaceImport:
                    statements = AppendExportsOfDeclaration(statements, seen, namedBindings);
                    break;

                case SyntaxKind.NamedImports:
                    foreach (var importBinding in ((NamedImports)namedBindings).Elements!)
                    {
                        statements = AppendExportsOfDeclaration(statements, seen, importBinding, /*liveBinding*/ true);
                    }

                    break;
            }
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2003-2018 — <c>appendExportsOfImportEqualsDeclaration</c>.</summary>
    private List<IStatement>? AppendExportsOfImportEqualsDeclaration(List<IStatement>? statements, ImportEqualsDeclaration decl)
    {
        if (_currentModuleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        return AppendExportsOfDeclaration(statements, new TransformationUtilities.IdentifierNameMap<bool>(), decl);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2020-2031 — <c>appendExportsOfVariableStatement</c>.</summary>
    private List<IStatement>? AppendExportsOfVariableStatement(List<IStatement>? statements, VariableStatement node)
        => AppendExportsOfVariableDeclarationList(statements, node.DeclarationList!, /*isForInOrOfInitializer*/ false);

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2033-2052 — <c>appendExportsOfVariableDeclarationList</c>.</summary>
    private List<IStatement>? AppendExportsOfVariableDeclarationList(List<IStatement>? statements, VariableDeclarationList node, bool isForInOrOfInitializer)
    {
        if (_currentModuleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        foreach (var decl in node.Declarations!)
        {
            statements = AppendExportsOfBindingElement(statements, decl, isForInOrOfInitializer);
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2054-2080 — <c>appendExportsOfBindingElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>VariableDeclaration | BindingElement</c> union, which the port takes
    /// as <see cref="Node"/> (the two classes share no base beyond it); its <c>decl.name.elements</c> read is the
    /// <c>BindingPattern</c> arm's own slot, cast where the reference's narrowing has already established it.</remarks>
    private List<IStatement>? AppendExportsOfBindingElement(List<IStatement>? statements, Node decl, bool isForInOrOfInitializer)
    {
        if (_currentModuleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        var name = decl switch
        {
            VariableDeclaration variable => variable.Name,
            _ => ((BindingElement)decl).Name,
        };
        if (Utilities.IsBindingPattern(name))
        {
            IEnumerable<Node> elements = name!.Kind == SyntaxKind.ObjectBindingPattern
                ? (IEnumerable<Node>)((ObjectBindingPattern)name).Elements!
                : ((ArrayBindingPattern)name).Elements!;
            foreach (var element in elements)
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    statements = AppendExportsOfBindingElement(statements, element, isForInOrOfInitializer);
                }
            }
        }
        else if (!Utilities.IsGeneratedIdentifier(name!)
            && (!NodeTests.IsVariableDeclaration(decl) || ((VariableDeclaration)decl).Initializer is not null || isForInOrOfInitializer))
        {
            statements = AppendExportsOfDeclaration(statements, new TransformationUtilities.IdentifierNameMap<bool>(), decl);
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2082-2106 — <c>appendExportsOfHoistedDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the <c>ClassDeclaration | FunctionDeclaration</c> union; the port takes the
    /// class's <c>NamedDeclaration</c> face (<c>IDeclaration</c>), which is what the reference reads from it
    /// (<c>name</c> and the two <c>hasSyntacticModifier</c> tests).</remarks>
    private List<IStatement>? AppendExportsOfHoistedDeclaration(List<IStatement>? statements, IDeclaration decl)
    {
        if (_currentModuleInfo!.ExportEquals is not null)
        {
            return statements;
        }

        var seen = new TransformationUtilities.IdentifierNameMap<bool>();
        if (Utilities.HasSyntacticModifier((Node)decl, ModifierFlags.Export))
        {
            var exportName = Utilities.HasSyntacticModifier((Node)decl, ModifierFlags.Default)
                ? _factory.CreateIdentifier("default", null, null)
                : TypeScriptTransformSupport.GetDeclarationName(_factory, (Node)decl, null, null);
            statements = AppendExportStatement(
                statements,
                seen,
                exportName,
                TypeScriptTransformSupport.GetLocalName(_factory, (Node)decl, null, null, null),
                /*location*/ (Node)decl,
                /*allowComments*/ null,
                /*liveBinding*/ null);
        }

        if (Utilities.GetNameOfDeclaration((Node)decl) is not null)
        {
            statements = AppendExportsOfDeclaration(statements, seen, (Node)decl);
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2108-2129 — <c>appendExportsOfDeclaration</c>.</summary>
    private List<IStatement>? AppendExportsOfDeclaration(List<IStatement>? statements, TransformationUtilities.IdentifierNameMap<bool> seen, Node decl, bool? liveBinding = null)
    {
        var name = TypeScriptTransformSupport.GetDeclarationName(_factory, decl, null, null);
        var exportSpecifiers = _currentModuleInfo!.ExportSpecifiers.Get(name);
        if (exportSpecifiers is not null)
        {
            foreach (var exportSpecifier in exportSpecifiers)
            {
                statements = AppendExportStatement(
                    statements,
                    seen,
                    exportSpecifier.Name!,
                    name,
                    /*location*/ exportSpecifier.Name,
                    /*allowComments*/ null,
                    liveBinding);
            }
        }
        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2131-2140 — <c>appendExportStatement</c>.</summary>
    private List<IStatement>? AppendExportStatement(
        List<IStatement>? statements,
        TransformationUtilities.IdentifierNameMap<bool> seen,
        Node exportName,
        Expression expression,
        ITextRange? location = null,
        bool? allowComments = null,
        bool? liveBinding = null)
    {
        if (!NodeTests.IsStringLiteral(exportName))
        {
            if (seen.Has(exportName))
            {
                return statements;
            }
            seen.Set(exportName, true);
        }
        statements = Append(statements, CreateExportStatement(exportName, expression, location, allowComments, liveBinding));
        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2142-2166 — <c>createUnderscoreUnderscoreESModule</c>.</summary>
    private ExpressionStatement CreateUnderscoreUnderscoreESModule()
    {
        var statement = _factory.CreateExpressionStatement(
            _factory.CreateCallExpression(
                _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Object", null, null), "defineProperty"),
                /*typeArguments*/ null,
                [
                    _factory.CreateIdentifier("exports", null, null),
                    _factory.CreateStringLiteral("__esModule", null, null),
                    _factory.CreateObjectLiteralExpression(
                        [
                            _factory.CreatePropertyAssignment("value", _factory.CreateTrue()),
                        ],
                        /*multiLine*/ null),
                ]));
        EmitNodes.SetEmitFlags(statement, EmitFlags.CustomPrologue);
        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2168-2183 — <c>createExportStatement</c>.</summary>
    private ExpressionStatement CreateExportStatement(
        Node name,
        Expression value,
        ITextRange? location = null,
        bool? allowComments = null,
        bool? liveBinding = null)
    {
        var statement = Utilities.SetTextRange(
            _factory.CreateExpressionStatement(CreateExportExpression(name, value, /*location*/ null, liveBinding)),
            location);
        FactoryUtilities.StartOnNewLine(statement);
        if (!Js.IsTruthy(allowComments))
        {
            EmitNodes.SetEmitFlags(statement, EmitFlags.NoComments);
        }

        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2185-2235 — <c>createExportExpression</c>.</summary>
    private Expression CreateExportExpression(
        Node name,
        Expression value,
        ITextRange? location = null,
        bool? liveBinding = null)
    {
        Expression expression = Js.IsTruthy(liveBinding)
                ? _factory.CreateCallExpression(
                    _factory.CreatePropertyAccessExpression(
                        _factory.CreateIdentifier("Object", null, null),
                        "defineProperty"),
                    /*typeArguments*/ null,
                    [
                        _factory.CreateIdentifier("exports", null, null),
                        _factory.CreateStringLiteralFromNode(name, null),
                        _factory.CreateObjectLiteralExpression(
                            [
                                _factory.CreatePropertyAssignment("enumerable", _factory.CreateTrue()),
                                _factory.CreatePropertyAssignment(
                                    "get",
                                    _factory.CreateFunctionExpression(
                                        /*modifiers*/ null,
                                        /*asteriskToken*/ null,
                                        /*name*/ null,
                                        /*typeParameters*/ null,
                                        /*parameters*/ [],
                                        /*type*/ null,
                                        _factory.CreateBlock([_factory.CreateReturnStatement(value)], null))),
                            ],
                            /*multiLine*/ null),
                    ])
                : _factory.CreateAssignment(
                    NodeTests.IsStringLiteral(name)
                        ? _factory.CreateElementAccessExpression(
                            _factory.CreateIdentifier("exports", null, null),
                            _factory.CloneNode(name))
                        : _factory.CreatePropertyAccessExpression(
                            _factory.CreateIdentifier("exports", null, null),
                            _factory.CloneNode(name)),
                    value);
        return Utilities.SetTextRange(expression, location);
    }

    /// <summary>ported from: src/compiler/transformers/module/module.ts:2237-2257 — <c>modifierVisitor</c>.</summary>
    private object? ModifierVisitor(Node node)
        // Elide module-specific modifiers.
        => node.Kind switch
        {
            SyntaxKind.ExportKeyword or SyntaxKind.DefaultKeyword => null,
            _ => node,
        };
}
