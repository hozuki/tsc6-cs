#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/ts.ts:1790-2252 — the enum and module/namespace members of
// `transformTypeScript`'s body.
/// <summary>
/// The enum/namespace half of the TypeScript erase transform (src/compiler/transformers/ts.ts:1790-2252):
/// <c>shouldEmitEnumDeclaration</c> through <c>getInnerMostModuleDeclarationFromDottedModule</c>, plus the
/// emitted-declaration bookkeeping the two declaration rewrites share.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>Map&lt;__String, Node&gt;</c>): <c>currentScopeFirstDeclarationsOfName</c> is the port's
/// <c>Dictionary&lt;string, Node&gt;</c> (<c>__String</c> is erased to <c>string</c>); the reference's
/// <c>has</c>/<c>get</c>/<c>set</c> are <c>ContainsKey</c>/<c>TryGetValue</c>/the indexer, and its
/// <c>map.get(name) === node</c> is the port's <c>ReferenceEquals</c>.
/// </para>
/// <para>
/// PORT-NOTE (<c>Statement</c>): TS's <c>VisitResult&lt;Statement&gt;</c> is the port's <c>object?</c>, which is also
/// how the erased visit result carries the reference's statement array.
/// </para>
/// <para>
/// PORT-NOTE (the two <c>createParameterDeclaration</c>/<c>createVariableDeclaration</c> calls): the reference's
/// one- and three-argument forms are the port's six- and four-argument forms with the absent arguments stated
/// (<c>undefined</c> → <c>null</c>), as the factory's own members record.
/// </para>
/// </remarks>
internal sealed partial class TypeScriptTransformer
{
    // ---------------------------------------------------------------------------------------------
    // ts.ts:1790-1972 — enums
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:1790-1792 — <c>shouldEmitEnumDeclaration</c>.</summary>
    private bool ShouldEmitEnumDeclaration(EnumDeclaration node)
        => !Utilities.IsEnumConst(node) || Utilities.ShouldPreserveConstEnums(_compilerOptions);

    /// <summary>ported from: src/compiler/transformers/ts.ts:1802-1889 — <c>visitEnumDeclaration</c>.</summary>
    private object? VisitEnumDeclaration(EnumDeclaration node)
    {
        if (!ShouldEmitEnumDeclaration(node))
        {
            return _factory.CreateNotEmittedStatement(node);
        }

        var statements = new List<Node>();

        // We request to be advised when the printer is about to print this node. This allows
        // us to set up the correct state for later substitutions.
        var emitFlags = EmitFlags.AdviseOnEmitNode;

        // If needed, we should emit a variable declaration for the enum. If we emit
        // a leading variable declaration, we should not emit leading comments for the
        // enum body.
        var varAdded = AddVarForEnumOrModuleDeclaration(statements, node);
        if (varAdded)
        {
            // We should still emit the comments if we are emitting a system module.
            if (_moduleKind != ModuleKind.System || !ReferenceEquals(_currentLexicalScope, _currentSourceFile))
            {
                emitFlags |= EmitFlags.NoLeadingComments;
            }
        }

        // `parameterName` is the declaration name used inside of the enum.
        var parameterName = GetNamespaceParameterName(node);

        // `containerName` is the expression used inside of the enum for assignments.
        var containerName = GetNamespaceContainerName(node);

        // `exportName` is the expression used within this node's container for any exported references.
        Expression exportName = IsExportOfNamespace(node)
            ? TypeScriptTransformSupport.GetExternalModuleOrNamespaceExportName(_factory, _currentNamespaceContainerName, node, /*allowComments*/ false, /*allowSourceMaps*/ true)
            : TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true);

        //  x || (x = {})
        //  exports.x || (exports.x = {})
        Expression moduleArg = _factory.CreateLogicalOr(
            exportName,
            _factory.CreateAssignment(
                exportName,
                _factory.CreateObjectLiteralExpression(/*properties*/ null, /*multiLine*/ null)));

        if (IsExportOfNamespace(node))
        {
            // `localName` is the expression used within this node's containing scope for any local references.
            var localName = TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true);

            //  x = (exports.x || (exports.x = {}))
            moduleArg = _factory.CreateAssignment(localName, moduleArg);
        }

        //  (function (x) {
        //      x[x["y"] = 0] = "y";
        //      ...
        //  })(x || (x = {}));
        var enumStatement = _factory.CreateExpressionStatement(
            _factory.CreateCallExpression(
                _factory.CreateFunctionExpression(
                    /*modifiers*/ null,
                    /*asteriskToken*/ null,
                    /*name*/ null,
                    /*typeParameters*/ null,
                    [_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, parameterName, /*questionToken*/ null, /*type*/ null, /*initializer*/ null)],
                    /*type*/ null,
                    TransformEnumBody(node, containerName)),
                /*typeArguments*/ null,
                [moduleArg]));

        NodeFactory.SetOriginalNode(enumStatement, node);
        if (varAdded)
        {
            // If a variable was added, synthetic comments are emitted on it, not on the moduleStatement.
            EmitNodes.SetSyntheticLeadingComments(enumStatement, null);
            EmitNodes.SetSyntheticTrailingComments(enumStatement, null);
        }
        Utilities.SetTextRange(enumStatement, node);
        EmitNodes.AddEmitFlags(enumStatement, emitFlags);
        statements.Add(enumStatement);

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1891-1911 — <c>transformEnumBody</c>.</summary>
    private Block TransformEnumBody(EnumDeclaration node, Identifier localName)
    {
        var savedCurrentNamespaceLocalName = _currentNamespaceContainerName;
        _currentNamespaceContainerName = localName;

        var statements = new List<IStatement>();
        _context.StartLexicalEnvironment();
        var members = Core.Map(node.Members, (member, _) => TransformEnumMember(member));
        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());
        Core.AddRange(statements, members);

        _currentNamespaceContainerName = savedCurrentNamespaceLocalName;
        return _factory.CreateBlock(
            Utilities.SetTextRange(_factory.CreateNodeArray(statements, /*hasTrailingComma*/ null), node.Members),
            /*multiLine*/ true);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1913-1950 — <c>transformEnumMember</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>getEnumMemberValue</c> answers an evaluation result whose <c>value</c> is
    /// <c>string | number | undefined</c>; the port's <c>Value</c> is <c>object?</c> and its
    /// <c>isSyntacticallyString</c> a <c>bool</c> (read through <c>Js.IsTruthy</c>, as the reference's
    /// <c>evaluated?.isSyntacticallyString</c> is a truthiness test).</remarks>
    private IStatement TransformEnumMember(EnumMember member)
    {
        // enums don't support computed properties
        // we pass false as 'generateNameForComputedPropertyName' for a backward compatibility purposes
        // old emitter always generate 'expression' part of the name as-is.
        var name = GetExpressionForPropertyName(member, /*generateNameForComputedPropertyName*/ false);
        var evaluated = _resolver.GetEnumMemberValue(member);
        var valueExpression = TransformEnumMemberDeclarationValue(member, evaluated?.Value);
        var innerAssignment = _factory.CreateAssignment(
            _factory.CreateElementAccessExpression(
                _currentNamespaceContainerName!,
                name),
            valueExpression);
        var outerAssignment = evaluated?.Value is string || Js.IsTruthy(evaluated?.IsSyntacticallyString)
            ? innerAssignment
            : _factory.CreateAssignment(
                _factory.CreateElementAccessExpression(
                    _currentNamespaceContainerName!,
                    innerAssignment),
                name);
        return Utilities.SetTextRange(
            _factory.CreateExpressionStatement(
                Utilities.SetTextRange(
                    outerAssignment,
                    member)),
            member);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1952-1972 — <c>transformEnumMemberDeclarationValue</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>constantValue: string | number | undefined</c> is the port's
    /// <c>object?</c> with <c>null</c> for the absent arm; the two numeric arms test <c>is double</c> and then compare,
    /// which is the reference's <c>typeof constantValue === "string"</c> / <c>constantValue &lt; 0</c> pair over the
    /// domain <c>getEnumMemberValue</c> actually produces (a string or a number).
    /// <para>
    /// PORT-NOTE (the numeric literal): TS passes the number to <c>createNumericLiteral</c>, whose JS body renders it
    /// with <c>String(value)</c>; the port's factory renders only the <c>int</c> and <c>string</c> forms of its
    /// <c>object?</c> slot (Parser/NodeFactory.cs:155), and the checker hands this code a <c>double</c>, so the value
    /// goes through <see cref="Js.NumberToString"/> — the port's ECMA-262 number formatter, and the same call shape the
    /// ported checker slices use for their own <c>createNumericLiteral(number)</c> sites
    /// (Checker.Check.Flags.cs:842-843, Checker.NodeBuilder.Types.cs:184-185). The node, its text and the printed
    /// JavaScript are identical to the reference's.
    /// </para>
    /// </remarks>
    private Expression TransformEnumMemberDeclarationValue(EnumMember member, object? constantValue)
    {
        if (constantValue is not null)
        {
            return constantValue is string text
                ? _factory.CreateStringLiteral(text, /*isSingleQuote*/ null, /*hasExtendedUnicodeEscape*/ null)
                : constantValue is double number && number < 0
                    ? _factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, _factory.CreateNumericLiteral(Js.NumberToString(-number), /*numericLiteralFlags*/ null))
                    : _factory.CreateNumericLiteral(Js.NumberToString((double)constantValue!), /*numericLiteralFlags*/ null);
        }
        else
        {
            EnableSubstitutionForNonQualifiedEnumMembers();
            if (member.Initializer is not null)
            {
                return Debug.CheckDefined(Utilities.VisitNode(member.Initializer, Visitor, Utilities.IsExpression));
            }
            else
            {
                return _factory.CreateVoidZero();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:1974-2100 — module declarations
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:1974-1985 — <c>shouldEmitModuleDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>isInstantiatedModule</c> is
    /// <see cref="TypeScriptTransformSupport.IsInstantiatedModule"/> — the reference's <c>isModuleDeclaration</c> test
    /// is the type argument of the port's <c>GetParseTreeNode</c> overload.</remarks>
    private bool ShouldEmitModuleDeclaration(ModuleDeclaration nodeIn)
    {
        var node = Utilities.GetParseTreeNode<ModuleDeclaration>(nodeIn, NodeTests.IsModuleDeclaration);
        if (node is null)
        {
            // If we can't find a parse tree node, assume the node is instantiated.
            return true;
        }
        return TypeScriptTransformSupport.IsInstantiatedModule(node, Utilities.ShouldPreserveConstEnums(_compilerOptions));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1987-2000 — <c>recordEmittedDeclarationInScope</c>.</summary>
    private void RecordEmittedDeclarationInScope(Node node)
    {
        _currentScopeFirstDeclarationsOfName ??= [];

        var name = DeclaredNameInScope(node);
        if (!_currentScopeFirstDeclarationsOfName.ContainsKey(name))
        {
            _currentScopeFirstDeclarationsOfName[name] = node;
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2002-2008 — <c>isFirstEmittedDeclarationInScope</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>get(name) === node</c> is a node-identity test; a missing entry answers
    /// <c>false</c>, which is what <c>undefined === node</c> does.</remarks>
    private bool IsFirstEmittedDeclarationInScope(Node node)
    {
        if (_currentScopeFirstDeclarationsOfName is not null)
        {
            var name = DeclaredNameInScope(node);
            return _currentScopeFirstDeclarationsOfName.TryGetValue(name, out var first) && ReferenceEquals(first, node);
        }
        return true;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2010-2013 — <c>declaredNameInScope</c>.</summary>
    /// <remarks>PORT-NOTE: <c>escapedText</c> is <c>__String</c> upstream and a <c>string?</c> here, which the erased
    /// <c>__String</c> makes the same key; the reference's <c>Debug.assertNode(node.name, isIdentifier)</c> is the
    /// port's assertion (a cast cannot follow it, so the narrowed read casts to <see cref="Identifier"/>).</remarks>
    private static string DeclaredNameInScope(Node node)
    {
        var name = ((INamedDeclaration)node).Name!;
        Debug.AssertNode(name, NodeTests.IsIdentifier);
        return ((Identifier)name).EscapedText!;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2018-2079 — <c>addVarForEnumOrModuleDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>node.modifiers</c> is read off the two concrete declaration kinds (the union TS allows has
    /// no model interface carrying the slot, and the factory's <c>ModifiersOf</c> widens to an enumeration the visitor
    /// does not take); the two <c>setSourceMapRange</c> calls take the declaration node as the range, which
    /// <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/> adapts.</remarks>
    private bool AddVarForEnumOrModuleDeclaration(List<Node> statements, Node node)
    {
        // Emit a variable statement for the module. We emit top-level enums as a `var`
        // declaration to avoid static errors in global scripts scripts due to redeclaration.
        // enums in any other scope are emitted as a `let` declaration.
        var varDecl = _factory.CreateVariableDeclaration(
            TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true),
            /*exclamationToken*/ null,
            /*type*/ null,
            /*initializer*/ null);
        var varFlags = _currentLexicalScope!.Kind == SyntaxKind.SourceFile ? NodeFlags.None : NodeFlags.Let;
        var modifiers = node is EnumDeclaration enumDeclaration ? enumDeclaration.Modifiers : ((ModuleDeclaration)node).Modifiers;
        var statement = _factory.CreateVariableStatement(
            Utilities.VisitNodes(modifiers, ModifierVisitor, Utilities.IsModifier),
            _factory.CreateVariableDeclarationList([varDecl], varFlags));

        NodeFactory.SetOriginalNode(varDecl, node);
        EmitNodes.SetSyntheticLeadingComments(varDecl, null);
        EmitNodes.SetSyntheticTrailingComments(varDecl, null);

        NodeFactory.SetOriginalNode(statement, node);

        RecordEmittedDeclarationInScope(node);
        if (IsFirstEmittedDeclarationInScope(node))
        {
            // Adjust the source map emit to match the old emitter.
            if (node.Kind == SyntaxKind.EnumDeclaration)
            {
                EmitNodes.SetSourceMapRange(statement.DeclarationList!, TypeScriptTransformSupport.SourceMapRangeOf(node));
            }
            else
            {
                EmitNodes.SetSourceMapRange(statement, TypeScriptTransformSupport.SourceMapRangeOf(node));
            }

            // Trailing comments for module declaration should be emitted after the function closure
            // instead of the variable statement:
            //
            //     /** Module comment*/
            //     module m1 {
            //         function foo4Export() {
            //         }
            //     } // trailing comment module
            //
            // Should emit:
            //
            //     /** Module comment*/
            //     var m1;
            //     (function (m1) {
            //         function foo4Export() {
            //         }
            //     })(m1 || (m1 = {})); // trailing comment module
            //
            EmitNodes.SetCommentRange(statement, node);
            EmitNodes.AddEmitFlags(statement, EmitFlags.NoTrailingComments);
            statements.Add(statement);
            return true;
        }

        // For an EnumDeclaration or ModuleDeclaration that merges with a preceeding
        // declaration we do not emit a leading variable declaration.
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2081-2170 — <c>visitModuleDeclaration</c>.</summary>
    private object? VisitModuleDeclaration(ModuleDeclaration node)
    {
        if (!ShouldEmitModuleDeclaration(node))
        {
            return _factory.CreateNotEmittedStatement(node);
        }

        Debug.AssertNode(node.Name, NodeTests.IsIdentifier, "A TypeScript namespace should have an Identifier name.");
        EnableSubstitutionForNamespaceExports();

        var statements = new List<Node>();

        // We request to be advised when the printer is about to print this node. This allows
        // us to set up the correct state for later substitutions.
        var emitFlags = EmitFlags.AdviseOnEmitNode;

        // If needed, we should emit a variable declaration for the module. If we emit
        // a leading variable declaration, we should not emit leading comments for the
        // module body.
        var varAdded = AddVarForEnumOrModuleDeclaration(statements, node);
        if (varAdded)
        {
            // We should still emit the comments if we are emitting a system module.
            if (_moduleKind != ModuleKind.System || !ReferenceEquals(_currentLexicalScope, _currentSourceFile))
            {
                emitFlags |= EmitFlags.NoLeadingComments;
            }
        }

        // `parameterName` is the declaration name used inside of the namespace.
        var parameterName = GetNamespaceParameterName(node);

        // `containerName` is the expression used inside of the namespace for exports.
        var containerName = GetNamespaceContainerName(node);

        // `exportName` is the expression used within this node's container for any exported references.
        Expression exportName = IsExportOfNamespace(node)
            ? TypeScriptTransformSupport.GetExternalModuleOrNamespaceExportName(_factory, _currentNamespaceContainerName, node, /*allowComments*/ false, /*allowSourceMaps*/ true)
            : TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true);

        //  x || (x = {})
        //  exports.x || (exports.x = {})
        Expression moduleArg = _factory.CreateLogicalOr(
            exportName,
            _factory.CreateAssignment(
                exportName,
                _factory.CreateObjectLiteralExpression(/*properties*/ null, /*multiLine*/ null)));

        if (IsExportOfNamespace(node))
        {
            // `localName` is the expression used within this node's containing scope for any local references.
            var localName = TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true);

            //  x = (exports.x || (exports.x = {}))
            moduleArg = _factory.CreateAssignment(localName, moduleArg);
        }

        //  (function (x_1) {
        //      x_1.y = ...;
        //  })(x || (x = {}));
        var moduleStatement = _factory.CreateExpressionStatement(
            _factory.CreateCallExpression(
                _factory.CreateFunctionExpression(
                    /*modifiers*/ null,
                    /*asteriskToken*/ null,
                    /*name*/ null,
                    /*typeParameters*/ null,
                    [_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, parameterName, /*questionToken*/ null, /*type*/ null, /*initializer*/ null)],
                    /*type*/ null,
                    TransformModuleBody(node, containerName)),
                /*typeArguments*/ null,
                [moduleArg]));

        NodeFactory.SetOriginalNode(moduleStatement, node);
        if (varAdded)
        {
            // If a variable was added, synthetic comments are emitted on it, not on the moduleStatement.
            EmitNodes.SetSyntheticLeadingComments(moduleStatement, null);
            EmitNodes.SetSyntheticTrailingComments(moduleStatement, null);
        }
        Utilities.SetTextRange(moduleStatement, node);
        EmitNodes.AddEmitFlags(moduleStatement, emitFlags);
        statements.Add(moduleStatement);

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2172-2245 — <c>transformModuleBody</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>isArray(result)</c> test is the port's test for the list arm of the
    /// visit result (<c>visitModuleDeclaration</c> answers a <c>List&lt;IStatement&gt;</c> or a single statement);
    /// <c>statementsLocation</c>/<c>blockLocation</c> are the port's <c>ITextRange?</c>, the common shape
    /// <c>setTextRange</c> takes (<c>node.body.statements</c> and the block are both ranges);
    /// <c>getInnerMost…</c>'s <c>body as ModuleBlock</c> is the port's cast on the same guarded read.</remarks>
    private Block TransformModuleBody(ModuleDeclaration node, Identifier namespaceLocalName)
    {
        var savedCurrentNamespaceContainerName = _currentNamespaceContainerName;
        var savedCurrentNamespace = _currentNamespace;
        var savedCurrentScopeFirstDeclarationsOfName = _currentScopeFirstDeclarationsOfName;
        _currentNamespaceContainerName = namespaceLocalName;
        _currentNamespace = node;
        _currentScopeFirstDeclarationsOfName = null;

        var statements = new List<IStatement>();
        _context.StartLexicalEnvironment();

        ITextRange? statementsLocation = null;
        ITextRange? blockLocation = null;
        if (node.Body is not null)
        {
            if (node.Body.Kind == SyntaxKind.ModuleBlock)
            {
                var moduleBlock = (ModuleBlock)node.Body;
                SaveStateAndInvoke(moduleBlock, body => Core.AddRange(statements, Utilities.VisitNodes(body.Statements, NamespaceElementVisitor, Utilities.IsStatement)));
                statementsLocation = moduleBlock.Statements;
                blockLocation = moduleBlock;
            }
            else
            {
                var result = VisitModuleDeclaration((ModuleDeclaration)node.Body);
                if (result is not null)
                {
                    if (result is List<Node> resultStatements)
                    {
                        foreach (var statement in resultStatements)
                        {
                            statements.Add((IStatement)statement);
                        }
                    }
                    else
                    {
                        statements.Add((IStatement)result);
                    }
                }

                var innermostModuleBlock = (ModuleBlock)GetInnerMostModuleDeclarationFromDottedModule(node)!.Body!;
                statementsLocation = TypeScriptTransformSupport.MoveRangePos(innermostModuleBlock.Statements!, -1);
            }
        }

        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());
        _currentNamespaceContainerName = savedCurrentNamespaceContainerName;
        _currentNamespace = savedCurrentNamespace;
        _currentScopeFirstDeclarationsOfName = savedCurrentScopeFirstDeclarationsOfName;

        var block = _factory.CreateBlock(
            Utilities.SetTextRange(
                _factory.CreateNodeArray(statements, /*hasTrailingComma*/ null),
                /*location*/ statementsLocation),
            /*multiLine*/ true);
        Utilities.SetTextRange(block, blockLocation);

        // namespace hello.hi.world {
        //      function foo() {}
        //
        //      // TODO, blah
        // }
        //
        // should be emitted as
        //
        // var hello;
        // (function (hello) {
        //     var hi;
        //     (function (hi) {
        //         var world;
        //         (function (world) {
        //             function foo() { }
        //             // TODO, blah
        //         })(world = hi.world || (hi.world = {}));
        //     })(hi = hello.hi || (hello.hi = {}));
        // })(hello || (hello = {}));
        // We only want to emit comment on the namespace which contains block body itself, not the containing namespaces.
        if (node.Body is null || node.Body.Kind != SyntaxKind.ModuleBlock)
        {
            EmitNodes.SetEmitFlags(block, Utilities.GetEmitFlags(block) | EmitFlags.NoComments);
        }
        return block;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:2247-2252 — <c>getInnerMostModuleDeclarationFromDottedModule</c>.</summary>
    /// <remarks>PORT-NOTE: TS's implicit <c>undefined</c> answer is the port's <c>null</c>; the recursive answer's
    /// truthiness test is the reference's <c>recursiveInnerModule || node.body</c> pair.</remarks>
    private static ModuleDeclaration? GetInnerMostModuleDeclarationFromDottedModule(ModuleDeclaration moduleDeclaration)
    {
        if (moduleDeclaration.Body!.Kind == SyntaxKind.ModuleDeclaration)
        {
            var recursiveInnerModule = GetInnerMostModuleDeclarationFromDottedModule((ModuleDeclaration)moduleDeclaration.Body);
            return recursiveInnerModule ?? (ModuleDeclaration)moduleDeclaration.Body;
        }
        return null;
    }
}
