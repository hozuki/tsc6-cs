#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2015.ts:946-2415, :4190-4310, :5027-5066 — the class and constructor
// machinery, the member transforms, and the three visitors that live beside them.
/// <summary>
/// The class half of the ES2015 downlevel transform.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>T extends Node</c> unions): the reference writes the class transforms over the unions
/// <c>ClassDeclaration | ClassExpression</c>, <c>DoStatement | WhileStatement</c> and
/// <c>GetAccessorDeclaration | SetAccessorDeclaration</c>; C# has no structural union, so each takes the common base
/// the body actually reads (<see cref="Node"/> plus <see cref="IClassLikeDeclarationBase"/> for the class pair, the
/// model's <c>Node</c> for the accessor pair, whose <c>Kind</c> switch is what TS's <c>node.kind</c> test is).
/// </para>
/// <para>
/// PORT-NOTE (the nine structural types at es2015.ts:1347-1420): <c>CapturedThis</c>, <c>SyntheticSuper</c>,
/// <c>ThisCapturingVariableDeclaration</c>, <c>ThisCapturingVariableStatement</c>,
/// <c>ThisCapturingAssignmentExpression</c>, <c>TransformedSuperCall</c>, <c>TransformedSuperCallWithFallback</c>,
/// <c>ImplicitSuperCall</c>/<c>ImplicitSuperCallWithFallback</c> and the two <c>ThisCapturing*WithFallback</c> aliases
/// have no model counterpart — every generated identifier in the port is an <see cref="Identifier"/>, so
/// <c>isCapturedThis</c> answers <c>bool</c> and each caller keeps reading the slots the intersection type licensed
/// through the class the matching <c>IsX</c> predicate tested.
/// </para>
/// </remarks>
internal sealed partial class ES2015Transformer
{
    /// <summary>ported from: src/compiler/transformers/es2015.ts:952-996 — <c>visitClassDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>singleOrMany(statements)</c> is the reference's own expression here (one statement →
    /// the statement, otherwise the array), which the visitor slot's <c>object?</c> carries.</remarks>
    private object? VisitClassDeclaration(ClassDeclaration node)
    {
        // [source]
        //      class C { }
        //
        // [output]
        //      var C = (function () {
        //          function C() {
        //          }
        //          return C;
        //      }());

        var variable = _factory.CreateVariableDeclaration(
            TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ true, /*allowSourceMaps*/ null),
            /*exclamationToken*/ null,
            /*type*/ null,
            TransformClassLikeDeclarationToExpression(node));

        NodeFactory.SetOriginalNode(variable, node);

        var statements = new List<IStatement>();
        var statement = _factory.CreateVariableStatement(
            /*modifiers*/ null,
            _factory.CreateVariableDeclarationList([variable], null));

        NodeFactory.SetOriginalNode(statement, node);
        Utilities.SetTextRange(statement, node);
        FactoryUtilities.StartOnNewLine(statement);
        statements.Add(statement);

        // Add an `export default` statement for default exports (for `--target es5 --module es6`)
        if (Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            IStatement exportStatement = Utilities.HasSyntacticModifier(node, ModifierFlags.Default)
                ? _factory.CreateExportDefault(TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null))
                : _factory.CreateExternalModuleExport(TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null));

            NodeFactory.SetOriginalNode((Node)exportStatement, statement);
            statements.Add(exportStatement);
        }

        return statements.Count == 1 ? (object)statements[0] : statements;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:998-1004 — <c>visitClassExpression</c>.</summary>
    private Expression VisitClassExpression(ClassExpression node)
    {
        // [source]
        //      C = class { }
        //
        // [output]
        //      C = (function () {
        //          function class_1() {
        //          }
        //          return class_1;
        //      }())
        return TransformClassLikeDeclarationToExpression(node);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1017-1085 — <c>transformClassLikeDeclarationToExpression</c>.</summary>
    /// <remarks>PORT-NOTE: <c>factory.getLocalName</c>/<c>getInternalName</c> live on
    /// <see cref="TypeScriptTransformSupport"/> (that file records the M5 residue), and <c>skipTrivia(currentText,
    /// node.pos)</c> is <see cref="ScannerUtilities.SkipTrivia"/>.</remarks>
    private Expression TransformClassLikeDeclarationToExpression(Node node)
    {
        if (((INamedDeclaration)node).Name is not null)
        {
            EnableSubstitutionsForBlockScopedBindings();
        }

        var extendsClauseElement = Utilities.GetClassExtendsHeritageElement(node);
        var classFunction = _factory.CreateFunctionExpression(
            /*modifiers*/ null,
            /*asteriskToken*/ null,
            /*name*/ null,
            /*typeParameters*/ null,
            extendsClauseElement is not null ? [_factory.CreateParameterDeclaration(/*modifiers*/ null, /*dotDotDotToken*/ null, CreateSyntheticSuper(), /*questionToken*/ null, /*type*/ null, /*initializer*/ null)] : [],
            /*type*/ null,
            TransformClassBody(node, extendsClauseElement));

        // To preserve the behavior of the old emitter, we explicitly indent
        // the body of the function here if it was requested in an earlier
        // transformation.
        EmitNodes.SetEmitFlags(classFunction, (Utilities.GetEmitFlags(node) & EmitFlags.Indented) | EmitFlags.ReuseTempVariableScope);

        // "inner" and "outer" below are added purely to preserve source map locations from
        // the old emitter
        var inner = _factory.CreatePartiallyEmittedExpression(classFunction, null);
        Utilities.SetTextRangeEnd(inner, node.End);
        EmitNodes.SetEmitFlags(inner, EmitFlags.NoComments);

        var outer = _factory.CreatePartiallyEmittedExpression(inner, null);
        Utilities.SetTextRangeEnd(outer, ScannerUtilities.SkipTrivia(_currentText!, node.Pos));
        EmitNodes.SetEmitFlags(outer, EmitFlags.NoComments);

        var result = _factory.CreateParenthesizedExpression(
            _factory.CreateCallExpression(
                outer,
                /*typeArguments*/ null,
                extendsClauseElement is not null
                    ? [Debug.CheckDefined(Utilities.VisitNode(extendsClauseElement.Expression, Visitor, Utilities.IsExpression))]
                    : []));
        EmitNodes.AddSyntheticLeadingComment(result, SyntaxKind.MultiLineCommentTrivia, "* @class ");
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1090-1125 — <c>transformClassBody</c>.</summary>
    private Block TransformClassBody(Node node, ExpressionWithTypeArguments? extendsClauseElement)
    {
        var statements = new List<IStatement>();
        var name = TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null);
        var constructorLikeName = ES2015TransformSupport.IsIdentifierANonContextualKeyword(name)
            ? _factory.GetGeneratedNameForNode(name, /*flags*/ null)
            : name;
        _context.StartLexicalEnvironment();
        AddExtendsHelperIfNeeded(statements, node, extendsClauseElement);
        AddConstructor(statements, node, constructorLikeName, extendsClauseElement);
        AddClassMembers(statements, node);

        // Create a synthetic text range for the return statement.
        var closingBraceLocation = TypeScriptTransformSupport.CreateTokenRange(
            ScannerUtilities.SkipTrivia(_currentText!, ((IClassLikeDeclarationBase)node).Members!.End),
            SyntaxKind.CloseBraceToken);

        // The following partially-emitted expression exists purely to align our sourcemap
        // emit with the original emitter.
        var outer = _factory.CreatePartiallyEmittedExpression(constructorLikeName, null);
        Utilities.SetTextRangeEnd(outer, closingBraceLocation.End);
        EmitNodes.SetEmitFlags(outer, EmitFlags.NoComments);

        var statement = _factory.CreateReturnStatement(outer);
        Utilities.SetTextRangePos(statement, closingBraceLocation.Pos);
        EmitNodes.SetEmitFlags(statement, EmitFlags.NoComments | EmitFlags.NoTokenSourceMaps);
        statements.Add(statement);

        TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());

        var block = _factory.CreateBlock(
            Utilities.SetTextRange(_factory.CreateNodeArray(statements, null), ((IClassLikeDeclarationBase)node).Members),
            /*multiLine*/ true);
        EmitNodes.SetEmitFlags(block, EmitFlags.NoComments);
        return block;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1127-1145 — <c>addExtendsHelperIfNeeded</c>.</summary>
    private void AddExtendsHelperIfNeeded(List<IStatement> statements, Node node, ExpressionWithTypeArguments? extendsClauseElement)
    {
        if (extendsClauseElement is not null)
        {
            statements.Add(
                Utilities.SetTextRange(
                    _factory.CreateExpressionStatement(
                        _emitHelpers.CreateExtendsHelper(TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null))),
                    /*location*/ extendsClauseElement));
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1147-1178 — <c>addConstructor</c>.</summary>
    private void AddConstructor(List<IStatement> statements, Node node, Identifier name, ExpressionWithTypeArguments? extendsClauseElement)
    {
        var savedConvertedLoopState = _convertedLoopState;
        _convertedLoopState = null;
        var ancestorFacts = EnterSubtree(HierarchyFacts.ConstructorExcludes, HierarchyFacts.ConstructorIncludes);
        var constructor = Utilities.GetFirstConstructorWithBody(node);
        var hasSynthesizedSuper = HasSynthesizedDefaultSuperCall(constructor, extendsClauseElement is not null);
        var constructorFunction = _factory.CreateFunctionDeclaration(
            /*modifiers*/ null,
            /*asteriskToken*/ null,
            name,
            /*typeParameters*/ null,
            TransformConstructorParameters(constructor, hasSynthesizedSuper),
            /*type*/ null,
            TransformConstructorBody(constructor, node, extendsClauseElement, hasSynthesizedSuper));

        Utilities.SetTextRange(constructorFunction, (ITextRange?)constructor ?? node);
        if (extendsClauseElement is not null)
        {
            EmitNodes.SetEmitFlags(constructorFunction, EmitFlags.CapturesThis);
        }

        statements.Add(constructorFunction);
        ExitSubtree(ancestorFacts, HierarchyFacts.FunctionSubtreeExcludes, HierarchyFacts.None);
        _convertedLoopState = savedConvertedLoopState;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1180-1188 — <c>transformConstructorParameters</c>.</summary>
    private NodeArray<ParameterDeclaration> TransformConstructorParameters(ConstructorDeclaration? constructor, bool hasSynthesizedSuper)
        => Utilities.VisitParameterList(
                constructor is not null && !hasSynthesizedSuper ? constructor.Parameters : null,
                Visitor,
                _context)
            ?? [];

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1190-1210 — <c>createDefaultConstructorBody</c>.</summary>
    private Block CreateDefaultConstructorBody(Node node, bool isDerivedClass)
    {
        // We must be here because the user didn't write a constructor
        // but we needed to call 'super(...args)' anyway as per 14.5.14 of the ES2016 spec.
        // If that's the case we can just immediately return the result of a 'super()' call.
        var statements = new List<IStatement>();
        _context.ResumeLexicalEnvironment();
        _factory.MergeLexicalEnvironment(statements, _context.EndLexicalEnvironment());

        if (isDerivedClass)
        {
            // return _super !== null && _super.apply(this, arguments) || this;
            statements.Add(_factory.CreateReturnStatement(CreateDefaultSuperCallOrThis()));
        }

        var statementsArray = _factory.CreateNodeArray(statements, null);
        Utilities.SetTextRange(statementsArray, ((IClassLikeDeclarationBase)node).Members);

        var block = _factory.CreateBlock(statementsArray, /*multiLine*/ true);
        Utilities.SetTextRange(block, node);
        EmitNodes.SetEmitFlags(block, EmitFlags.NoComments);
        return block;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1212-1214 — <c>isUninitializedVariableStatement</c>.</summary>
    private static bool IsUninitializedVariableStatement(Node node)
        => NodeTests.IsVariableStatement(node)
            && Core.Every(
                ((VariableStatement)node).DeclarationList!.Declarations,
                (decl, _) => NodeTests.IsIdentifier(decl.Name!) && decl.Initializer is null);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1216-1254 — <c>containsSuperCall</c>.</summary>
    /// <remarks>PORT-NOTE: <c>forEachChild(node, containsSuperCall)</c> stops at the first <em>truthy</em> answer
    /// (Parser/ForEachChildTable.cs's header records the same reading), so the <c>!!</c> is
    /// <c>?? false</c> over the <c>bool?</c> the traversal answers.</remarks>
    private bool ContainsSuperCall(Node node)
    {
        if (Utilities.IsSuperCall(node))
        {
            return true;
        }
        if ((node.TransformFlags & TransformFlags.ContainsLexicalSuper) == 0)
        {
            return false;
        }
        switch (node.Kind)
        {
            // stop at function boundaries
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return false;

            // only step into computed property names for class and object literal elements
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.PropertyDeclaration:
            {
                var named = (INamedDeclaration)node;
                if (named.Name is not null && NodeTests.IsComputedPropertyName(named.Name))
                {
                    return ForEachChildTable.ForEachChild<bool?>(named.Name!, n => ContainsSuperCall(n)) ?? false;
                }
                return false;
            }
        }
        return ForEachChildTable.ForEachChild<bool?>(node, n => ContainsSuperCall(n)) ?? false;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1256-1345 — <c>transformConstructorBody</c>.</summary>
    /// <remarks>PORT-NOTE: <c>mayReplaceThis</c> is TS's <c>boolean | HierarchyFacts</c> (a number) read as a
    /// condition; the port tests the flag and keeps the boolean.</remarks>
    private Block TransformConstructorBody(
        ConstructorDeclaration? constructor,
        Node node,
        ExpressionWithTypeArguments? extendsClauseElement,
        bool hasSynthesizedSuper)
    {
        // determine whether the class is known syntactically to be a derived class (e.g. a
        // class that extends a value that is not syntactically known to be `null`).
        var isDerivedClass = extendsClauseElement is not null
            && FactoryUtilities.SkipOuterExpressions(extendsClauseElement.Expression!).Kind != SyntaxKind.NullKeyword;

        // When the subclass does not have a constructor, we synthesize a *default* constructor using the following
        // representation:
        //
        // ```
        // // es2015 (source)
        // class C extends Base { }
        //
        // // es5 (transformed)
        // var C = (function (_super) {
        //     function C() {
        //         return _super.apply(this, arguments) || this;
        //     }
        //     return C;
        // })(Base);
        // ```
        if (constructor is null) return CreateDefaultConstructorBody(node, isDerivedClass);

        // The prologue will contain all leading standard and custom prologue statements added by this transform
        var prologue = new List<IStatement>();
        var statements = new List<IStatement>();
        _context.ResumeLexicalEnvironment();

        // In derived classes, there may be code before the necessary super() call
        // We'll remove pre-super statements to be tacked on after the rest of the body
        var body = constructor.Body!;
        var standardPrologueEnd = _factory.CopyStandardPrologue(body.Statements, prologue, /*statementOffset*/ 0, /*ensureUseStrict*/ null);
        if (hasSynthesizedSuper || ContainsSuperCall(body))
        {
            _hierarchyFacts |= HierarchyFacts.ConstructorWithSuperCall;
        }

        Core.AddRange(statements, Utilities.VisitNodes(body.Statements, Visitor, Utilities.IsStatement, standardPrologueEnd));

        var mayReplaceThis = isDerivedClass || (_hierarchyFacts & HierarchyFacts.ConstructorWithSuperCall) != 0;

        // Add parameter defaults at the beginning of the output, with prologue statements
        AddDefaultValueAssignmentsIfNeeded(prologue, constructor);
        AddRestParameterIfNeeded(prologue, constructor, hasSynthesizedSuper);
        InsertCaptureNewTargetIfNeeded(prologue, constructor);
        if (mayReplaceThis)
        {
            InsertCaptureThisForNode(prologue, constructor, CreateActualThis());
        }
        else
        {
            InsertCaptureThisForNodeIfNeeded(prologue, constructor);
        }

        _factory.MergeLexicalEnvironment(prologue, _context.EndLexicalEnvironment());

        if (mayReplaceThis && !IsSufficientlyCoveredByReturnStatements(body))
        {
            statements.Add(_factory.CreateReturnStatement(CreateCapturedThis()));
        }

        var newBody = _factory.CreateBlock(
            Utilities.SetTextRange(
                _factory.CreateNodeArray([.. prologue, .. statements], null),
                /*location*/ body.Statements),
            /*multiLine*/ true);

        Utilities.SetTextRange(newBody, body);
        return SimplifyConstructor(newBody, body, hasSynthesizedSuper);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1348-1350 — <c>isCapturedThis</c>.</summary>
    private static bool IsCapturedThis(Node node)
        => Utilities.IsGeneratedIdentifier(node) && Utilities.IdText(node) == "_this";

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1353-1356 — <c>isSyntheticSuper</c>.</summary>
    private static bool IsSyntheticSuper(Node node)
        => Utilities.IsGeneratedIdentifier(node) && Utilities.IdText(node) == "_super";

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1359-1362 — <c>isThisCapturingVariableStatement</c>.</summary>
    private static bool IsThisCapturingVariableStatement(Node node)
        => NodeTests.IsVariableStatement(node)
            && ((VariableStatement)node).DeclarationList!.Declarations!.Count == 1
            && IsThisCapturingVariableDeclaration(((VariableStatement)node).DeclarationList!.Declarations![0]);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1365-1367 — <c>isThisCapturingVariableDeclaration</c>.</summary>
    private static bool IsThisCapturingVariableDeclaration(Node node)
        => NodeTests.IsVariableDeclaration(node) && IsCapturedThis(((VariableDeclaration)node).Name!) && ((VariableDeclaration)node).Initializer is not null;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1370-1372 — <c>isThisCapturingAssignment</c>.</summary>
    private static bool IsThisCapturingAssignment(Node node)
        => Utilities.IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true) && IsCapturedThis(((BinaryExpression)node).Left!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1375-1383 — <c>isTransformedSuperCall</c>.</summary>
    private static bool IsTransformedSuperCall(Node node)
        => NodeTests.IsCallExpression(node)
            && NodeTests.IsPropertyAccessExpression(((CallExpression)node).Expression!)
            && IsSyntheticSuper(((PropertyAccessExpression)((CallExpression)node).Expression!).Expression!)
            && ((PropertyAccessExpression)((CallExpression)node).Expression!).Name is Identifier name
            && (Utilities.IdText(name) == "call" || Utilities.IdText(name) == "apply")
            && ((CallExpression)node).Arguments!.Count >= 1
            && ((CallExpression)node).Arguments![0].Kind == SyntaxKind.ThisKeyword;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1386-1390 — <c>isTransformedSuperCallWithFallback</c>.</summary>
    private static bool IsTransformedSuperCallWithFallback(Node node)
        => NodeTests.IsBinaryExpression(node)
            && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.BarBarToken
            && ((BinaryExpression)node).Right!.Kind == SyntaxKind.ThisKeyword
            && IsTransformedSuperCall(((BinaryExpression)node).Left!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1393-1400 — <c>isImplicitSuperCall</c>.</summary>
    private static bool IsImplicitSuperCall(Node node)
        => NodeTests.IsBinaryExpression(node)
            && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.AmpersandAmpersandToken
            && NodeTests.IsBinaryExpression(((BinaryExpression)node).Left!)
            && ((BinaryExpression)((BinaryExpression)node).Left!).OperatorToken!.Kind == SyntaxKind.ExclamationEqualsEqualsToken
            && IsSyntheticSuper(((BinaryExpression)((BinaryExpression)node).Left!).Left!)
            && ((BinaryExpression)((BinaryExpression)node).Left!).Right!.Kind == SyntaxKind.NullKeyword
            && IsTransformedSuperCall(((BinaryExpression)node).Right!)
            && Utilities.IdText(((PropertyAccessExpression)((CallExpression)((BinaryExpression)node).Right!).Expression!).Name!) == "apply";

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1403-1407 — <c>isImplicitSuperCallWithFallback</c>.</summary>
    private static bool IsImplicitSuperCallWithFallback(Node node)
        => NodeTests.IsBinaryExpression(node)
            && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.BarBarToken
            && ((BinaryExpression)node).Right!.Kind == SyntaxKind.ThisKeyword
            && IsImplicitSuperCall(((BinaryExpression)node).Left!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1410-1412 — <c>isThisCapturingTransformedSuperCallWithFallback</c>.</summary>
    private static bool IsThisCapturingTransformedSuperCallWithFallback(Node node)
        => IsThisCapturingAssignment(node) && IsTransformedSuperCallWithFallback(((BinaryExpression)node).Right!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1415-1417 — <c>isThisCapturingImplicitSuperCallWithFallback</c>.</summary>
    private static bool IsThisCapturingImplicitSuperCallWithFallback(Node node)
        => IsThisCapturingAssignment(node) && IsImplicitSuperCallWithFallback(((BinaryExpression)node).Right!);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1419-1427 — <c>isTransformedSuperCallLike</c>.</summary>
    private static bool IsTransformedSuperCallLike(Node node)
        => IsTransformedSuperCall(node)
            || IsTransformedSuperCallWithFallback(node)
            || IsThisCapturingTransformedSuperCallWithFallback(node)
            || IsImplicitSuperCall(node)
            || IsImplicitSuperCallWithFallback(node)
            || IsThisCapturingImplicitSuperCallWithFallback(node);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1433-1531 — <c>simplifyConstructorInlineSuperInThisCaptureVariable</c>.</summary>
    /// <remarks>PORT-NOTE: the four array slices are <see cref="List{T}.GetRange"/> reads over the body's own
    /// statement array; the reference's <c>[...a, x, ...b]</c> is the same statement order in a fresh list.</remarks>
    private Block SimplifyConstructorInlineSuperInThisCaptureVariable(Block body)
    {
        var statements = body.Statements!;
        for (var i = 0; i < statements.Count - 1; i++)
        {
            var stmt = statements[i];
            // walk forward until we find `var _this = ...;`
            if (!IsThisCapturingVariableStatement((Node)stmt))
            {
                continue;
            }

            // Only perform inlining for `var _this = this;`
            var varDecl = ((VariableStatement)stmt).DeclarationList!.Declarations![0];
            if (varDecl.Initializer!.Kind != SyntaxKind.ThisKeyword)
            {
                continue;
            }

            // It's possible that temporary variables may have been injected between `var _this` and `_this = ...`,
            // so we'll track any intervening statements to stitch them back in to the result.
            var thisCaptureStatementIndex = i;
            var superCallIndex = i + 1;
            while (superCallIndex < statements.Count)
            {
                var inner = statements[superCallIndex];
                // stop on the next `_this = _super.call(...) || this` statement
                if (NodeTests.IsExpressionStatement((Node)inner))
                {
                    if (IsTransformedSuperCallLike(FactoryUtilities.SkipOuterExpressions(((ExpressionStatement)inner).Expression!)))
                    {
                        break;
                    }
                }

                // skip over hoisted temporary variables
                if (IsUninitializedVariableStatement((Node)inner))
                {
                    superCallIndex++;
                    continue;
                }

                // if there are any other statements following `var _this = ...` then we cannot simplify.
                return body;
            }

            var following = (ExpressionStatement)statements[superCallIndex];

            // If the expression that follows is `_this = ...`, strip off the assignment to `_this` since that will
            // be handled by the variable initializer.
            var expression = following.Expression!;
            if (IsThisCapturingAssignment(expression))
            {
                expression = ((BinaryExpression)expression).Right!;
            }

            var newVarDecl = _factory.UpdateVariableDeclaration(
                varDecl,
                varDecl.Name,
                /*exclamationToken*/ null,
                /*type*/ null,
                expression);

            var newDeclList = _factory.UpdateVariableDeclarationList(((VariableStatement)stmt).DeclarationList!, [newVarDecl]);
            var newVarStatement = _factory.CreateVariableStatement(
                ((VariableStatement)stmt).Modifiers?.Cast<Node>().ToList(),
                newDeclList);
            NodeFactory.SetOriginalNode(newVarStatement, following);
            Utilities.SetTextRange(newVarStatement, following);
            var newStatements = new List<IStatement>();
            newStatements.AddRange(statements.GetRange(0, thisCaptureStatementIndex)); // copy statements preceding to `var _this`
            newStatements.AddRange(statements.GetRange(thisCaptureStatementIndex + 1, superCallIndex - thisCaptureStatementIndex - 1)); // copy intervening temp variables
            newStatements.Add(newVarStatement);
            newStatements.AddRange(statements.GetRange(superCallIndex + 1, statements.Count - superCallIndex - 1)); // copy statements following `super.call(this, ...)`
            var newStatementsArray = _factory.CreateNodeArray(newStatements, null);
            Utilities.SetTextRange(newStatementsArray, statements);
            return _factory.UpdateBlock(body, newStatementsArray);
        }

        return body;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1533-1615 — <c>simplifyConstructorInlineSuperReturn</c>.</summary>
    private Block SimplifyConstructorInlineSuperReturn(Block body, Block original)
    {
        // bail if there is a non-top-level `super()` in the original body
        foreach (var statement in original.Statements!)
        {
            if ((statement.TransformFlags & TransformFlags.ContainsLexicalSuper) != 0
                && TransformationUtilities.GetSuperCallFromStatement((Node)statement) is null)
            {
                return body;
            }
        }

        // If the original node contained a lexical `this` that must be preserved, or if we recorded a captured `this`
        // or `super`, then we cannot simplify `var _this = ...;`, but may still be able to inline standalone `_this = ...`
        // assignment statements.
        var canElideThisCapturingVariable = (original.TransformFlags & TransformFlags.ContainsLexicalThis) == 0
            && (_hierarchyFacts & HierarchyFacts.LexicalThis) == 0
            && (_hierarchyFacts & HierarchyFacts.CapturedLexicalThis) == 0;

        // find the return statement and the preceding transformed `super()` call
        var statements = body.Statements!;
        for (var i = statements.Count - 1; i > 0; i--)
        {
            // walk backwards until we find a `return _this;`
            var statement = statements[i];
            if (NodeTests.IsReturnStatement((Node)statement)
                && ((ReturnStatement)statement).Expression is not null
                && IsCapturedThis(((ReturnStatement)statement).Expression!))
            {
                var preceding = statements[i - 1];
                Expression? expression = null;
                if (NodeTests.IsExpressionStatement((Node)preceding)
                    && IsThisCapturingTransformedSuperCallWithFallback(FactoryUtilities.SkipOuterExpressions(((ExpressionStatement)preceding).Expression!)))
                {
                    // The preceding statement is `_this = _super.call(this, ...) || this`, so we'll inline the
                    // expression.
                    expression = ((ExpressionStatement)preceding).Expression;
                }
                else if (canElideThisCapturingVariable && IsThisCapturingVariableStatement((Node)preceding))
                {
                    var varDecl = ((VariableStatement)preceding).DeclarationList!.Declarations![0];
                    if (IsTransformedSuperCallLike(FactoryUtilities.SkipOuterExpressions(varDecl.Initializer!)))
                    {
                        // The preceding statement is `var _this = _super.call(this, ...) || this`, so we'll inline the
                        // initializer as a `_this` assignment, though it may be removed in a later pass.
                        expression = _factory.CreateAssignment(
                            CreateCapturedThis(),
                            varDecl.Initializer);
                    }
                }

                if (expression is null)
                {
                    break;
                }

                var newReturnStatement = _factory.CreateReturnStatement(expression);
                NodeFactory.SetOriginalNode(newReturnStatement, (Node)preceding);
                Utilities.SetTextRange(newReturnStatement, (Node)preceding);
                var newStatements = new List<IStatement>();
                newStatements.AddRange(statements.GetRange(0, i - 1)); // copy all statements preceding `_super.call(this, ...)`
                newStatements.Add(newReturnStatement);
                newStatements.AddRange(statements.GetRange(i + 1, statements.Count - i - 1)); // copy all statements following `return _this;`
                var newStatementsArray = _factory.CreateNodeArray(newStatements, null);
                Utilities.SetTextRange(newStatementsArray, statements);
                return _factory.UpdateBlock(body, newStatementsArray);
            }
        }

        return body;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1618-1657 — <c>elideUnusedThisCaptureWorker</c>.</summary>
    private object? ElideUnusedThisCaptureWorker(Node node)
    {
        if (IsThisCapturingVariableStatement(node))
        {
            var varDecl = ((VariableStatement)node).DeclarationList!.Declarations![0];
            if (varDecl.Initializer!.Kind == SyntaxKind.ThisKeyword)
            {
                return null; // elide `var _this = this;`
            }
        }
        else if (IsThisCapturingAssignment(node))
        {
            return _factory.CreatePartiallyEmittedExpression(((BinaryExpression)node).Right, node); // elide `_this =`
        }

        switch (node.Kind)
        {
            // stop at function boundaries
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return node;

            // only step into computed property names for class and object literal elements
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.PropertyDeclaration:
            {
                var named = (INamedDeclaration)node;
                if (named.Name is not null && NodeTests.IsComputedPropertyName(named.Name))
                {
                    return _factory.ReplacePropertyName(node, Utilities.VisitEachChildWorker(named.Name, ElideUnusedThisCaptureWorker));
                }
                return node;
            }
        }
        return Utilities.VisitEachChildWorker(node, ElideUnusedThisCaptureWorker);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1659-1698 — <c>simplifyConstructorElideUnusedThisCapture</c>.</summary>
    private Block SimplifyConstructorElideUnusedThisCapture(Block body, Block original)
    {
        // If the original node contained a lexical `this` that must be preserved, or if we recorded a captured `this`,
        // then there is nothing we can simplify.
        if ((original.TransformFlags & TransformFlags.ContainsLexicalThis) != 0
            || (_hierarchyFacts & HierarchyFacts.LexicalThis) != 0
            || (_hierarchyFacts & HierarchyFacts.CapturedLexicalThis) != 0)
        {
            return body;
        }

        // bail if there is a non-top-level `super()` in the original body
        foreach (var statement in original.Statements!)
        {
            if ((statement.TransformFlags & TransformFlags.ContainsLexicalSuper) != 0
                && TransformationUtilities.GetSuperCallFromStatement((Node)statement) is null)
            {
                return body;
            }
        }

        return _factory.UpdateBlock(body, Utilities.VisitNodes(body.Statements, ElideUnusedThisCaptureWorker, Utilities.IsStatement));
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1701-1745 — <c>injectSuperPresenceCheckWorker</c>.</summary>
    private object? InjectSuperPresenceCheckWorker(Node node)
    {
        if (IsTransformedSuperCall(node)
            && ((CallExpression)node).Arguments!.Count == 2
            && NodeTests.IsIdentifier(((CallExpression)node).Arguments![1])
            && Utilities.IdText(((CallExpression)node).Arguments![1]) == "arguments")
        {
            return _factory.CreateLogicalAnd(
                _factory.CreateStrictInequality(
                    CreateSyntheticSuper(),
                    _factory.CreateNull()),
                (CallExpression)node);
        }

        switch (node.Kind)
        {
            // stop at function boundaries
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.Constructor:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return node;

            // only step into computed property names for class and object literal elements
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.PropertyDeclaration:
            {
                var named = (INamedDeclaration)node;
                if (named.Name is not null && NodeTests.IsComputedPropertyName(named.Name))
                {
                    return _factory.ReplacePropertyName(node, Utilities.VisitEachChildWorker(named.Name, InjectSuperPresenceCheckWorker));
                }
                return node;
            }
        }
        return Utilities.VisitEachChildWorker(node, InjectSuperPresenceCheckWorker);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1747-1775 — <c>complicateConstructorInjectSuperPresenceCheck</c>.</summary>
    private Block ComplicateConstructorInjectSuperPresenceCheck(Block body)
        => _factory.UpdateBlock(body, Utilities.VisitNodes(body.Statements, InjectSuperPresenceCheckWorker, Utilities.IsStatement));

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1777-1794 — <c>simplifyConstructor</c>.</summary>
    private Block SimplifyConstructor(Block body, Block original, bool hasSynthesizedSuper)
    {
        var inputBody = body;
        body = SimplifyConstructorInlineSuperInThisCaptureVariable(body);
        body = SimplifyConstructorInlineSuperReturn(body, original);
        if (!ReferenceEquals(body, inputBody))
        {
            // Only perform `var _this = this` elision if some other simplification succeeded.
            body = SimplifyConstructorElideUnusedThisCapture(body, original);
        }
        if (hasSynthesizedSuper)
        {
            body = ComplicateConstructorInjectSuperPresenceCheck(body);
        }
        return body;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1796-1818 — <c>isSufficientlyCoveredByReturnStatements</c>.</summary>
    private static bool IsSufficientlyCoveredByReturnStatements(Node statement)
    {
        // A return statement is considered covered.
        if (statement.Kind == SyntaxKind.ReturnStatement)
        {
            return true;
        }
        // An if-statement with two covered branches is covered.
        else if (statement.Kind == SyntaxKind.IfStatement)
        {
            var ifStatement = (IfStatement)statement;
            if (ifStatement.ElseStatement is not null)
            {
                return IsSufficientlyCoveredByReturnStatements((Node)ifStatement.ThenStatement!)
                    && IsSufficientlyCoveredByReturnStatements((Node)ifStatement.ElseStatement);
            }
        }
        // A block is covered if it has a last statement which is covered.
        else if (statement.Kind == SyntaxKind.Block)
        {
            var lastStatement = Core.LastOrUndefined(((Block)statement).Statements);
            if (lastStatement is not null && IsSufficientlyCoveredByReturnStatements((Node)lastStatement))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1820-1822 — <c>createActualThis</c>.</summary>
    private Expression CreateActualThis()
        => EmitNodes.SetEmitFlags(_factory.CreateThis(), EmitFlags.NoSubstitution);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:1824-1844 — <c>createDefaultSuperCallOrThis</c>.</summary>
    private Expression CreateDefaultSuperCallOrThis()
        => _factory.CreateLogicalOr(
            _factory.CreateLogicalAnd(
                _factory.CreateStrictInequality(
                    CreateSyntheticSuper(),
                    _factory.CreateNull()),
                _factory.CreateFunctionApplyCall(
                    CreateSyntheticSuper(),
                    CreateActualThis(),
                    _factory.CreateIdentifier("arguments", null, null))),
            CreateActualThis());

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2254-2290 — <c>addClassMembers</c>.</summary>
    /// <remarks>PORT-NOTE: the two members of the <c>GetAccessor | SetAccessor</c> arm share the model's
    /// <c>IFunctionLikeDeclarationBase</c> and <c>IClassElement</c>; the <c>getAllAccessorDeclarations</c> helper takes
    /// that common shape (see <see cref="ES2015TransformSupport.GetAllAccessorDeclarations"/>).</remarks>
    private void AddClassMembers(List<IStatement> statements, Node node)
    {
        foreach (var member in ((IClassLikeDeclarationBase)node).Members!)
        {
            switch (member.Kind)
            {
                case SyntaxKind.SemicolonClassElement:
                    statements.Add(TransformSemicolonClassElementToStatement((SemicolonClassElement)member));
                    break;

                case SyntaxKind.MethodDeclaration:
                    statements.Add(TransformClassMethodDeclarationToStatement(
                        GetClassMemberPrefix(node, (Node)member),
                        (MethodDeclaration)member,
                        node));
                    break;

                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    var accessors = ES2015TransformSupport.GetAllAccessorDeclarations(
                        ((IClassLikeDeclarationBase)node).Members,
                        (IFunctionLikeDeclarationBase)member);
                    if (ReferenceEquals(member, accessors.FirstAccessor))
                    {
                        statements.Add(TransformAccessorsToStatement(GetClassMemberPrefix(node, (Node)member), accessors, node));
                    }

                    break;

                case SyntaxKind.Constructor:
                case SyntaxKind.ClassStaticBlockDeclaration:
                    // Constructors are handled in visitClassExpression/visitClassDeclaration
                    break;

                default:
                    Debug.FailBadSyntaxKind((Node)member, _currentSourceFile is not null ? _currentSourceFile.FileName : null);
                    break;
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2291-2299 — <c>transformSemicolonClassElementToStatement</c>.</summary>
    private IStatement TransformSemicolonClassElementToStatement(SemicolonClassElement member)
        => Utilities.SetTextRange(_factory.CreateEmptyStatement(), member);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2301-2336 — <c>transformClassMethodDeclarationToStatement</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getSourceMapRange</c>/<c>setSourceMapRange</c> are <see cref="EmitNodes"/>, whose setter
    /// takes an <see cref="ISourceMapRange"/>; the node the reference passes is projected by
    /// <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/> (the same adapter <c>ts.ts</c> needed).</remarks>
    private IStatement TransformClassMethodDeclarationToStatement(LeftHandSideExpression receiver, MethodDeclaration member, Node container)
    {
        var commentRange = EmitNodes.GetCommentRange(member);
        var sourceMapRange = EmitNodes.GetSourceMapRange(member);
        var memberFunction = TransformFunctionLikeToExpression(member, /*location*/ member, /*name*/ null, container);
        var propertyName = Utilities.VisitNode(member.Name, Visitor, Utilities.IsPropertyName);
        Debug.Assert(propertyName is not null);
        Expression e;
        if (!NodeTests.IsPrivateIdentifier(propertyName!) && Utilities.GetUseDefineForClassFields(_compilerOptions))
        {
            var name = NodeTests.IsComputedPropertyName(propertyName!) ? ((ComputedPropertyName)propertyName!).Expression
                : NodeTests.IsIdentifier(propertyName!) ? _factory.CreateStringLiteral(Utilities.UnescapeLeadingUnderscores(((Identifier)propertyName!).EscapedText!), null, null)
                : propertyName!;
            e = _factory.CreateObjectDefinePropertyCall(
                receiver,
                name,
                _factory.CreatePropertyDescriptor(
                    new PropertyDescriptorAttributes { Value = memberFunction, Enumerable = false, Writable = true, Configurable = true },
                    /*singleLine*/ null));
        }
        else
        {
            var memberName = FactoryUtilities.CreateMemberAccessForPropertyName(_factory, receiver, propertyName!, /*location*/ member.Name);
            e = _factory.CreateAssignment(memberName, memberFunction);
        }
        EmitNodes.SetEmitFlags(memberFunction, EmitFlags.NoComments);
        EmitNodes.SetSourceMapRange(memberFunction, TypeScriptTransformSupport.SourceMapRangeOf(sourceMapRange));
        var statement = Utilities.SetTextRange(_factory.CreateExpressionStatement(e), /*location*/ member);

        NodeFactory.SetOriginalNode(statement, member);
        EmitNodes.SetCommentRange(statement, commentRange);

        // The location for the statement is used to emit comments only.
        // No source map should be emitted for this statement to align with the
        // old emitter.
        EmitNodes.SetEmitFlags(statement, EmitFlags.NoSourceMap);
        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2338-2352 — <c>transformAccessorsToStatement</c>.</summary>
    private IStatement TransformAccessorsToStatement(
        LeftHandSideExpression receiver,
        (Node? FirstAccessor, Node? SecondAccessor, GetAccessorDeclaration? GetAccessor, SetAccessorDeclaration? SetAccessor) accessors,
        Node container)
    {
        var statement = _factory.CreateExpressionStatement(TransformAccessorsToExpression(receiver, accessors, container, /*startsOnNewLine*/ false));
        // The location for the statement is used to emit source maps only.
        // No comments should be emitted for this statement to align with the
        // old emitter.
        EmitNodes.SetEmitFlags(statement, EmitFlags.NoComments);
        EmitNodes.SetSourceMapRange(statement, TypeScriptTransformSupport.SourceMapRangeOf(EmitNodes.GetSourceMapRange(accessors.FirstAccessor!)));
        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:2354-2414 — <c>transformAccessorsToExpression</c>.</summary>
    private Expression TransformAccessorsToExpression(
        LeftHandSideExpression receiver,
        (Node? FirstAccessor, Node? SecondAccessor, GetAccessorDeclaration? GetAccessor, SetAccessorDeclaration? SetAccessor) accessors,
        Node container,
        bool startsOnNewLine)
    {
        var (firstAccessor, _, getAccessor, setAccessor) = accessors;

        // To align with source maps in the old emitter, the receiver and property name
        // arguments are both mapped contiguously to the accessor name.
        // TODO(rbuckton): Does this need to be parented?
        var target = Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode(receiver), receiver), receiver.Parent);
        EmitNodes.SetEmitFlags(target!, EmitFlags.NoComments | EmitFlags.NoTrailingSourceMap);
        EmitNodes.SetSourceMapRange(target!, TypeScriptTransformSupport.SourceMapRangeOf(((INamedDeclaration)firstAccessor!).Name!));

        var visitedAccessorName = Utilities.VisitNode(((INamedDeclaration)firstAccessor!).Name, Visitor, Utilities.IsPropertyName);
        Debug.Assert(visitedAccessorName is not null);
        if (NodeTests.IsPrivateIdentifier(visitedAccessorName!))
        {
            return Debug.Fail<Expression>($"Encountered unhandled private identifier while transforming ES2015.\r\nNode {Debug.FormatSyntaxKind(visitedAccessorName!.Kind)} was unexpected.");
        }
        var propertyName = FactoryUtilities.CreateExpressionForPropertyName(_factory, visitedAccessorName!);
        EmitNodes.SetEmitFlags(propertyName, EmitFlags.NoComments | EmitFlags.NoLeadingSourceMap);
        EmitNodes.SetSourceMapRange(propertyName, TypeScriptTransformSupport.SourceMapRangeOf(((INamedDeclaration)firstAccessor!).Name!));

        var properties = new List<Node>();
        if (getAccessor is not null)
        {
            var getterFunction = TransformFunctionLikeToExpression(getAccessor, /*location*/ null, /*name*/ null, container);
            EmitNodes.SetSourceMapRange(getterFunction, TypeScriptTransformSupport.SourceMapRangeOf(EmitNodes.GetSourceMapRange(getAccessor)));
            EmitNodes.SetEmitFlags(getterFunction, EmitFlags.NoLeadingComments);
            var getter = _factory.CreatePropertyAssignment("get", getterFunction);
            EmitNodes.SetCommentRange(getter, EmitNodes.GetCommentRange(getAccessor));
            properties.Add(getter);
        }

        if (setAccessor is not null)
        {
            var setterFunction = TransformFunctionLikeToExpression(setAccessor, /*location*/ null, /*name*/ null, container);
            EmitNodes.SetSourceMapRange(setterFunction, TypeScriptTransformSupport.SourceMapRangeOf(EmitNodes.GetSourceMapRange(setAccessor)));
            EmitNodes.SetEmitFlags(setterFunction, EmitFlags.NoLeadingComments);
            var setter = _factory.CreatePropertyAssignment("set", setterFunction);
            EmitNodes.SetCommentRange(setter, EmitNodes.GetCommentRange(setAccessor));
            properties.Add(setter);
        }

        properties.Add(
            _factory.CreatePropertyAssignment("enumerable", getAccessor is not null || setAccessor is not null ? _factory.CreateFalse() : _factory.CreateTrue()));
        properties.Add(
            _factory.CreatePropertyAssignment("configurable", _factory.CreateTrue()));

        var call = _factory.CreateCallExpression(
            _factory.CreatePropertyAccessExpression(_factory.CreateIdentifier("Object", null, null), "defineProperty"),
            /*typeArguments*/ null,
            [
                target!,
                propertyName,
                _factory.CreateObjectLiteralExpression(properties, /*multiLine*/ true),
            ]);
        if (startsOnNewLine)
        {
            FactoryUtilities.StartOnNewLine(call);
        }

        return call;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4195-4221 — <c>visitCatchClause</c>.</summary>
    private CatchClause VisitCatchClause(CatchClause node)
    {
        var ancestorFacts = EnterSubtree(HierarchyFacts.BlockScopeExcludes, HierarchyFacts.BlockScopeIncludes);
        CatchClause updated;
        Debug.Assert(node.VariableDeclaration is not null, "Catch clause variable should always be present when downleveling ES2015.");
        if (Utilities.IsBindingPattern(node.VariableDeclaration!.Name))
        {
            var temp = _factory.CreateTempVariable(/*recordTempVariable*/ null, null);
            var newVariableDeclaration = _factory.CreateVariableDeclaration(temp, null, null, null);
            Utilities.SetTextRange(newVariableDeclaration, node.VariableDeclaration);
            var vars = Destructuring.FlattenDestructuringBinding(
                node.VariableDeclaration,
                Visitor,
                _context,
                FlattenLevel.All,
                temp);
            var list = _factory.CreateVariableDeclarationList(vars, null);
            Utilities.SetTextRange(list, node.VariableDeclaration);
            var destructure = _factory.CreateVariableStatement(/*modifiers*/ null, list);
            updated = _factory.UpdateCatchClause(node, newVariableDeclaration, AddStatementToStartOfBlock(node.Block!, destructure));
        }
        else
        {
            updated = Utilities.VisitEachChildWorker(node, Visitor, _context);
        }

        ExitSubtree(ancestorFacts, HierarchyFacts.None, HierarchyFacts.None);
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4223-4226 — <c>addStatementToStartOfBlock</c>.</summary>
    private Block AddStatementToStartOfBlock(Block block, IStatement statement)
    {
        var transformedStatements = Utilities.VisitNodes(block.Statements, Visitor, Utilities.IsStatement);
        return _factory.UpdateBlock(block, [statement, .. transformedStatements!]);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4234-4253 — <c>visitMethodDeclaration</c>.</summary>
    private Node VisitMethodDeclaration(MethodDeclaration node)
    {
        // We should only get here for methods on an object literal with regular identifier names.
        // Methods on classes are handled in visitClassDeclaration/visitClassExpression.
        // Methods with computed property names are handled in visitObjectLiteralExpression.
        Debug.Assert(!NodeTests.IsComputedPropertyName(node.Name!));
        var functionExpression = TransformFunctionLikeToExpression(node, /*location*/ (ITextRange?)TypeScriptTransformSupport.MoveRangePos(node, -1), /*name*/ null, /*container*/ null);
        EmitNodes.SetEmitFlags(functionExpression, EmitFlags.NoLeadingComments | Utilities.GetEmitFlags(functionExpression));
        return Utilities.SetTextRange(
            _factory.CreatePropertyAssignment(
                node.Name,
                functionExpression),
            /*location*/ node);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4255-4277 — <c>visitAccessorDeclaration</c>.</summary>
    private Node VisitAccessorDeclaration(Node node)
    {
        Debug.Assert(!NodeTests.IsComputedPropertyName(((INamedDeclaration)node).Name!));
        var savedConvertedLoopState = _convertedLoopState;
        _convertedLoopState = null;
        var ancestorFacts = EnterSubtree(HierarchyFacts.FunctionExcludes, HierarchyFacts.FunctionIncludes);
        Node updated;
        var parameters = Utilities.VisitParameterList(((IFunctionLikeDeclarationBase)node).Parameters, Visitor, _context);
        var body = TransformFunctionBody(node);
        if (node.Kind == SyntaxKind.GetAccessor)
        {
            var getAccessor = (GetAccessorDeclaration)node;
            updated = _factory.UpdateGetAccessorDeclaration(getAccessor, getAccessor.Modifiers, getAccessor.Name, parameters, getAccessor.Type, body);
        }
        else
        {
            var setAccessor = (SetAccessorDeclaration)node;
            updated = _factory.UpdateSetAccessorDeclaration(setAccessor, setAccessor.Modifiers, setAccessor.Name, parameters, body);
        }
        ExitSubtree(ancestorFacts, HierarchyFacts.FunctionSubtreeExcludes, HierarchyFacts.None);
        _convertedLoopState = savedConvertedLoopState;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4279-4287 — <c>visitShorthandPropertyAssignment</c>.</summary>
    private Node VisitShorthandPropertyAssignment(ShorthandPropertyAssignment node)
        => Utilities.SetTextRange(
            _factory.CreatePropertyAssignment(
                node.Name,
                VisitIdentifier((Identifier)_factory.CloneNode(node.Name!))),
            /*location*/ node);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4289-4296 — <c>visitComputedPropertyName</c>.</summary>
    private ComputedPropertyName VisitComputedPropertyName(ComputedPropertyName node)
        => Utilities.VisitEachChildWorker(node, Visitor, _context);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4298-4306 — <c>visitYieldExpression</c>.</summary>
    private Expression VisitYieldExpression(YieldExpression node)
    {
        // `yield` expressions are transformed using the generators transformer.
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:5027-5031 — <c>getClassMemberPrefix</c>.</summary>
    private LeftHandSideExpression GetClassMemberPrefix(Node node, Node member)
        => Utilities.IsStatic(member)
            ? TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null)
            : _factory.CreatePropertyAccessExpression(
                TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null),
                "prototype");

    /// <summary>ported from: src/compiler/transformers/es2015.ts:5033-5064 — <c>hasSynthesizedDefaultSuperCall</c>.</summary>
    private static bool HasSynthesizedDefaultSuperCall(ConstructorDeclaration? constructor, bool hasExtendsClause)
    {
        if (constructor is null || !hasExtendsClause)
        {
            return false;
        }

        if (Core.Some(constructor.Parameters))
        {
            return false;
        }

        var statement = Core.FirstOrUndefined<IStatement>(constructor.Body!.Statements);
        if (statement is null || !Utilities.NodeIsSynthesized((Node)statement) || statement.Kind != SyntaxKind.ExpressionStatement)
        {
            return false;
        }

        var statementExpression = ((ExpressionStatement)statement).Expression!;
        if (!Utilities.NodeIsSynthesized(statementExpression) || statementExpression.Kind != SyntaxKind.CallExpression)
        {
            return false;
        }

        var callTarget = ((CallExpression)statementExpression).Expression!;
        if (!Utilities.NodeIsSynthesized(callTarget) || callTarget.Kind != SyntaxKind.SuperKeyword)
        {
            return false;
        }

        var callArgument = Core.SingleOrUndefined(((CallExpression)statementExpression).Arguments);
        if (callArgument is null || !Utilities.NodeIsSynthesized(callArgument) || callArgument.Kind != SyntaxKind.SpreadElement)
        {
            return false;
        }

        var expression = ((SpreadElement)callArgument).Expression!;
        return NodeTests.IsIdentifier(expression) && ((Identifier)expression).EscapedText == "arguments";
    }
}
