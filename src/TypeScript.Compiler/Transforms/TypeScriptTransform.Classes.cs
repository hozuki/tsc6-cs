#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/ts.ts:856-1786 — the class, function, parameter and expression members of
// `transformTypeScript`'s body.
/// <summary>
/// The class/function/expression half of the TypeScript erase transform (src/compiler/transformers/ts.ts:856-1786):
/// <c>getClassFacts</c> through <c>visitJsxJsxOpeningElement</c>.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (<c>ClassLikeDeclaration</c>): TS's union (class declaration, class expression, interface declaration) is
/// the model's <see cref="IClassLikeDeclarationBase"/>; the reference's two readers that also admit an object literal
/// (<c>visitMethodDeclaration</c>/<c>visitGetAccessor</c>/<c>visitSetAccessor</c>/<c>injectClassElementTypeMetadata</c>)
/// take that literal, a type the union does not cover, so their <c>parent</c>/<c>container</c> slot is
/// <see cref="Node"/> and the class-only members cast where the reference's
/// <c>isClassLike</c> guard licenses it.
/// </para>
/// <para>
/// PORT-NOTE (<c>Declaration</c>): TS's <c>Declaration</c> union has no single model interface for the metadata
/// helpers, so <c>getTypeMetadata</c>/<c>shouldAdd*Metadata</c> take a <see cref="Node"/> and test its kind — the same
/// reading the reference's <c>node.kind</c> arms make.
/// </para>
/// <para>
/// PORT-NOTE (the array visit results): where the reference answers a statement array
/// (<c>visitClassDeclaration</c>'s export arms, <c>visitFunctionDeclaration</c>'s export arm), the port answers
/// <c>List&lt;Node&gt;</c> — the erased shape <c>Utilities.VisitNodes</c> accepts from a visitor
/// (<c>visited is IEnumerable&lt;Node&gt;</c>). The reference's elements are <c>Statement</c>s, which the model carries as
/// the <see cref="IStatement"/> interface, and an <c>IEnumerable&lt;IStatement&gt;</c> is not an
/// <c>IEnumerable&lt;Node&gt;</c>, so each list is built with its elements' concrete class as the type argument and the
/// statements are added unchanged.
/// </para>
/// <para>
/// PORT-NOTE (the nullable reads): TS asserts non-null with <c>!</c> where a guarded read has already run
/// (<c>node.expression!</c>, <c>node.body!</c>, <c>constructor.parameters</c>); the port states the same non-null
/// assertion with the same lack of a check.
/// </para>
/// </remarks>
internal sealed partial class TypeScriptTransformer
{
    // ---------------------------------------------------------------------------------------------
    // ts.ts:856-878 — the class facts
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:856-867 — <c>getClassFacts</c>.</summary>
    /// <remarks>PORT-NOTE: <c>some(getProperties(…))</c> is <c>Count &gt; 0</c> because
    /// <see cref="TransformationUtilities.GetProperties"/> answers <c>IReadOnlyList&lt;PropertyDeclaration&gt;</c>
    /// (whose elements are never null, which is the only input for which JS's <c>some</c> without a predicate answers
    /// differently).</remarks>
    private ClassFacts GetClassFacts(ClassDeclaration node)
    {
        var facts = ClassFacts.None;
        if (TransformationUtilities.GetProperties(node, /*requireInitializer*/ true, /*isStatic*/ true).Count > 0) facts |= ClassFacts.HasStaticInitializedProperties;
        var extendsClauseElement = Utilities.GetEffectiveBaseTypeNode(node);
        if (extendsClauseElement is not null && FactoryUtilities.SkipOuterExpressions(extendsClauseElement.Expression!).Kind != SyntaxKind.NullKeyword) facts |= ClassFacts.IsDerivedClass;
        if (Utilities.ClassOrConstructorParameterIsDecorated(_legacyDecorators, node)) facts |= ClassFacts.HasClassOrConstructorParameterDecorators;
        if (Utilities.ChildIsDecorated(_legacyDecorators, node)) facts |= ClassFacts.HasMemberDecorators;
        if (IsExportOfNamespace(node)) facts |= ClassFacts.IsExportOfNamespace;
        else if (IsDefaultExternalModuleExport(node)) facts |= ClassFacts.IsDefaultExternalExport;
        else if (IsNamedExternalModuleExport(node)) facts |= ClassFacts.IsNamedExternalExport;
        return facts;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:869-871 — <c>hasTypeScriptClassSyntax</c>.</summary>
    private static bool HasTypeScriptClassSyntax(Node node)
        => (node.TransformFlags & TransformFlags.ContainsTypeScriptClassSyntax) != 0;

    /// <summary>ported from: src/compiler/transformers/ts.ts:873-878 — <c>isClassLikeDeclarationWithTypeScriptSyntax</c>.</summary>
    private static bool IsClassLikeDeclarationWithTypeScriptSyntax(IClassLikeDeclarationBase node)
        => Utilities.HasDecorators((Node)node)
            || Core.Some(node.TypeParameters)
            || Core.Some(node.HeritageClauses, (clause, _) => HasTypeScriptClassSyntax((Node)clause))
            || Core.Some(node.Members, (member, _) => HasTypeScriptClassSyntax((Node)member));

    // ---------------------------------------------------------------------------------------------
    // ts.ts:880-1046 — the class declaration and expression
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:880-1025 — <c>visitClassDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>moveModifiers</c> is <c>boolean | number</c> (the second arm is a bit test); the port states
    /// the same question as a <c>bool</c> — <c>promoteToIIFE || facts &amp; ClassFacts.IsExportOfNamespace</c> is truthy
    /// exactly when one of the two is nonzero. <c>statement</c> is the reference's
    /// <c>VariableStatement | ClassDeclaration</c>, both of which are <see cref="IStatement"/>; the two
    /// <c>[statement, …]</c> answers are the port's <c>List&lt;IStatement&gt;</c> (the array visit result).
    /// <c>createPartiallyEmittedExpression(localName)</c> states the reference's defaulted second argument as
    /// <c>null</c>, and <c>insertStatementsAfterStandardPrologue</c> is
    /// <see cref="TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue"/>.
    /// </remarks>
    private object? VisitClassDeclaration(ClassDeclaration node)
    {
        var facts = GetClassFacts(node);
        var promoteToIIFE = _languageVersion < ScriptTarget.ES2015 &&
            (facts & ClassFacts.MayNeedImmediatelyInvokedFunctionExpression) != 0;

        if (
            !IsClassLikeDeclarationWithTypeScriptSyntax(node) &&
            !Utilities.ClassOrConstructorParameterIsDecorated(_legacyDecorators, node) &&
            !IsExportOfNamespace(node)
        )
        {
            return _factory.UpdateClassDeclaration(
                node,
                Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
                node.Name,
                /*typeParameters*/ null,
                Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause),
                Utilities.VisitNodes(node.Members, GetClassElementVisitor(node), Utilities.IsClassElement));
        }

        if (promoteToIIFE)
        {
            _context.StartLexicalEnvironment();
        }

        var moveModifiers = promoteToIIFE || (facts & ClassFacts.IsExportOfNamespace) != 0;

        // elide modifiers on the declaration if we are emitting an IIFE or the class is
        // a namespace export
        var modifiers = moveModifiers
            ? Utilities.VisitNodes(node.Modifiers, ModifierElidingVisitor, Utilities.IsModifierLike)
            : Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike);

        // inject metadata only if the class is decorated
        if ((facts & ClassFacts.HasClassOrConstructorParameterDecorators) != 0)
        {
            modifiers = InjectClassTypeMetadata(modifiers, node);
        }

        var needsName = (moveModifiers && node.Name is null)
            || (facts & ClassFacts.HasMemberDecorators) != 0
            || (facts & ClassFacts.HasStaticInitializedProperties) != 0;

        var name = needsName
            ? node.Name ?? _factory.GetGeneratedNameForNode(node, /*flags*/ null)
            : node.Name;

        //  ${modifiers} class ${name} ${heritageClauses} {
        //      ${members}
        //  }
        var classDeclaration = _factory.UpdateClassDeclaration(
            node,
            modifiers,
            name,
            /*typeParameters*/ null,
            Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause),
            TransformClassMembers(node));

        // To better align with the old emitter, we should not emit a trailing source map
        // entry if the class has static properties.
        var emitFlags = Utilities.GetEmitFlags(node);
        if ((facts & ClassFacts.HasStaticInitializedProperties) != 0)
        {
            emitFlags |= EmitFlags.NoTrailingSourceMap;
        }

        EmitNodes.SetEmitFlags(classDeclaration, emitFlags);

        Node statement;
        if (promoteToIIFE)
        {
            // When we emit a TypeScript class down to ES5, we must wrap it in an IIFE so that the
            // 'es2015' transformer can properly nest static initializers and decorators. The result
            // looks something like:
            //
            //  var C = function () {
            //      class C {
            //      }
            //      C.static_prop = 1;
            //      return C;
            //  }();
            //
            var statements = new List<IStatement> { classDeclaration };
            var closingBraceLocation = TypeScriptTransformSupport.CreateTokenRange(
                ScannerUtilities.SkipTrivia(_currentSourceFile!.Text!, node.Members!.End),
                SyntaxKind.CloseBraceToken);
            var localName = TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null);

            // The following partially-emitted expression exists purely to align our sourcemap
            // emit with the original emitter.
            var outer = _factory.CreatePartiallyEmittedExpression(localName, /*original*/ null);
            Utilities.SetTextRangeEnd(outer, closingBraceLocation.End);
            EmitNodes.SetEmitFlags(outer, EmitFlags.NoComments);

            var returnStatement = _factory.CreateReturnStatement(outer);
            Utilities.SetTextRangePos(returnStatement, closingBraceLocation.Pos);
            EmitNodes.SetEmitFlags(returnStatement, EmitFlags.NoComments | EmitFlags.NoTokenSourceMaps);
            statements.Add(returnStatement);

            TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, _context.EndLexicalEnvironment());

            var iife = _factory.CreateImmediatelyInvokedArrowFunction(statements);
            EmitNodes.SetInternalEmitFlags(iife, InternalEmitFlags.TypeScriptClassWrapper);

            //  let C = (() => { ... })();
            var varDecl = _factory.CreateVariableDeclaration(
                TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ false),
                /*exclamationToken*/ null,
                /*type*/ null,
                iife);
            NodeFactory.SetOriginalNode(varDecl, node);

            var varStatement = _factory.CreateVariableStatement(
                /*modifiers*/ null,
                _factory.CreateVariableDeclarationList([varDecl], NodeFlags.Let));
            NodeFactory.SetOriginalNode(varStatement, node);
            EmitNodes.SetCommentRange(varStatement, node);
            EmitNodes.SetSourceMapRange(varStatement, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastDecorators(node)));
            FactoryUtilities.StartOnNewLine(varStatement);
            statement = varStatement;
        }
        else
        {
            statement = classDeclaration;
        }

        if (moveModifiers)
        {
            if ((facts & ClassFacts.IsExportOfNamespace) != 0)
            {
                return new List<Node>
                {
                    statement,
                    CreateExportMemberAssignmentStatement(node),
                };
            }
            if ((facts & ClassFacts.IsDefaultExternalExport) != 0)
            {
                return new List<Node>
                {
                    statement,
                    _factory.CreateExportDefault(TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true)),
                };
            }
            if ((facts & ClassFacts.IsNamedExternalExport) != 0)
            {
                return new List<Node>
                {
                    statement,
                    _factory.CreateExternalModuleExport(TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true)),
                };
            }
        }

        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1027-1046 — <c>visitClassExpression</c>.</summary>
    private Expression VisitClassExpression(ClassExpression node)
    {
        var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierElidingVisitor, Utilities.IsModifierLike);
        if (Utilities.ClassOrConstructorParameterIsDecorated(_legacyDecorators, node))
        {
            modifiers = InjectClassTypeMetadata(modifiers, node);
        }

        return _factory.UpdateClassExpression(
            node,
            modifiers,
            node.Name,
            /*typeParameters*/ null,
            Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause),
            TransformClassMembers(node));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1048-1080 — <c>transformClassMembers</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>filter(constructor.parameters, …)</c> answers the parameter-property
    /// declarations, which are <see cref="ParameterDeclaration"/>s whose parent is a constructor
    /// (<c>Utilities.IsParameterPropertyDeclaration</c> records the narrowing); <c>append</c>/<c>addRange</c> are
    /// <see cref="Core.Append{T}"/>/<see cref="Core.AddRange{T}"/>, whose <c>IList&lt;T&gt;?</c> answers are cast back
    /// to the <c>List&lt;IClassElement&gt;</c> they were handed (that is what the reference's own array does).</remarks>
    private NodeArray<IClassElement>? TransformClassMembers(IClassLikeDeclarationBase node)
    {
        var members = Utilities.VisitNodes(node.Members, GetClassElementVisitor(node), Utilities.IsClassElement);

        List<IClassElement>? newMembers = null;
        var constructor = Utilities.GetFirstConstructorWithBody((Node)node);
        var parametersWithPropertyAssignments = constructor is null
            ? null
            : Core.Filter(constructor.Parameters, p => Utilities.IsParameterPropertyDeclaration(p!, constructor));

        if (parametersWithPropertyAssignments is not null)
        {
            foreach (var parameter in parametersWithPropertyAssignments)
            {
                // Ignore parameter properties with destructured names; we will have errored on them earlier.
                if (!NodeTests.IsIdentifier(parameter.Name!))
                {
                    continue;
                }
                var parameterProperty = _factory.CreatePropertyDeclaration(
                    /*modifiers*/ null,
                    parameter.Name,
                    /*questionOrExclamationToken*/ null,
                    /*type*/ null,
                    /*initializer*/ null);
                NodeFactory.SetOriginalNode(parameterProperty, parameter);
                newMembers = (List<IClassElement>?)Core.Append(newMembers, parameterProperty);
            }
        }

        if (newMembers is not null)
        {
            newMembers = (List<IClassElement>?)Core.AddRange(newMembers, members);
            return Utilities.SetTextRange(_factory.CreateNodeArray(newMembers, /*hasTrailingComma*/ null), node.Members);
        }

        return members;
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:1082-1214 — the decorator type metadata
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:1082-1094 — <c>injectClassTypeMetadata</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>ModifierLike[]</c> is the port's <c>List&lt;Node&gt;</c> (the model keeps
    /// modifier-like slots as nodes), which is also what <c>getTypeMetadata</c> answers, so the four <c>addRange</c>
    /// calls need no projection.</remarks>
    private NodeArray<Node>? InjectClassTypeMetadata(NodeArray<Node>? modifiers, IClassLikeDeclarationBase node)
    {
        var metadata = GetTypeMetadata((Node)node, node);
        if (Core.Some(metadata))
        {
            var modifiersArray = new List<Node>();
            Core.AddRange(modifiersArray, Core.TakeWhile(modifiers, FactoryUtilities.IsExportOrDefaultModifier));
            Core.AddRange(modifiersArray, Core.Filter(modifiers, NodeTests.IsDecorator));
            Core.AddRange(modifiersArray, metadata);
            Core.AddRange(modifiersArray, Core.Filter(Core.SkipWhile(modifiers, FactoryUtilities.IsExportOrDefaultModifier), Utilities.IsModifier));
            modifiers = Utilities.SetTextRange(_factory.CreateNodeArray(modifiersArray, /*hasTrailingComma*/ null), modifiers);
        }

        return modifiers;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1096-1113 — <c>injectClassElementTypeMetadata</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>container</c> is <c>ClassLikeDeclaration | ObjectLiteralExpression</c>;
    /// the metadata arm runs only under <c>isClassLike(container)</c>, which is where the port casts to
    /// <see cref="IClassLikeDeclarationBase"/> (the type the serializer's container slot takes).</remarks>
    private NodeArray<Node>? InjectClassElementTypeMetadata(NodeArray<Node>? modifiers, Node node, Node container)
    {
        if (Utilities.IsClassLike(container) && Utilities.ClassElementOrClassElementParameterIsDecorated(_legacyDecorators, node, container))
        {
            var metadata = GetTypeMetadata(node, (IClassLikeDeclarationBase)container);
            if (Core.Some(metadata))
            {
                var modifiersArray = new List<Node>();
                Core.AddRange(modifiersArray, Core.Filter(modifiers, NodeTests.IsDecorator));
                Core.AddRange(modifiersArray, metadata);
                Core.AddRange(modifiersArray, Core.Filter(modifiers, Utilities.IsModifier));
                modifiers = Utilities.SetTextRange(_factory.CreateNodeArray(modifiersArray, /*hasTrailingComma*/ null), modifiers);
            }
        }
        return modifiers;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:209 — <c>USE_NEW_TYPE_METADATA_FORMAT</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's module-level <c>const</c> is <c>false</c>, so <c>getTypeMetadata</c> always
    /// takes the old-format arm; it stays a constant so the two arms and their condition read as the reference's do
    /// (TS's own bundler eliminates the other arm, this one is always false at the call).</remarks>
    private const bool UseNewTypeMetadataFormat = false;

    /// <summary>ported from: src/compiler/transformers/ts.ts:1115-1121 — <c>getTypeMetadata</c>.</summary>
    private List<Node>? GetTypeMetadata(Node node, IClassLikeDeclarationBase container)
    {
        // Decorator metadata is not yet supported for ES decorators.
        if (!_legacyDecorators) return null;
        return UseNewTypeMetadataFormat
            ? GetNewTypeMetadata(node, container)
            : GetOldTypeMetadata(node, container);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1123-1140 — <c>getOldTypeMetadata</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>Decorator[]</c> accumulation is a <c>List&lt;Node&gt;</c> for the reason
    /// <see cref="InjectClassTypeMetadata"/> records; the three serializer calls take the reference's
    /// <c>{ currentLexicalScope, currentNameScope: container }</c> object literal as
    /// <see cref="TypeSerializer.RuntimeTypeSerializerContext"/> — the type that module landed for it.</remarks>
    private List<Node>? GetOldTypeMetadata(Node node, IClassLikeDeclarationBase container)
    {
        if (_typeSerializer is not null)
        {
            List<Node>? decorators = null;
            if (ShouldAddTypeMetadata(node))
            {
                var typeMetadata = _emitHelpers.CreateMetadataHelper("design:type", _typeSerializer.SerializeTypeOfNode(
                    new TypeSerializer.RuntimeTypeSerializerContext { CurrentLexicalScope = _currentLexicalScope!, CurrentNameScope = container },
                    node,
                    container));
                decorators = (List<Node>?)Core.Append(decorators, _factory.CreateDecorator(typeMetadata));
            }
            if (ShouldAddParamTypesMetadata(node))
            {
                var paramTypesMetadata = _emitHelpers.CreateMetadataHelper("design:paramtypes", _typeSerializer.SerializeParameterTypesOfNode(
                    new TypeSerializer.RuntimeTypeSerializerContext { CurrentLexicalScope = _currentLexicalScope!, CurrentNameScope = container },
                    node,
                    container));
                decorators = (List<Node>?)Core.Append(decorators, _factory.CreateDecorator(paramTypesMetadata));
            }
            if (ShouldAddReturnTypeMetadata(node))
            {
                var returnTypeMetadata = _emitHelpers.CreateMetadataHelper("design:returntype", _typeSerializer.SerializeReturnTypeOfNode(
                    new TypeSerializer.RuntimeTypeSerializerContext { CurrentLexicalScope = _currentLexicalScope!, CurrentNameScope = container },
                    node));
                decorators = (List<Node>?)Core.Append(decorators, _factory.CreateDecorator(returnTypeMetadata));
            }
            return decorators;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1142-1169 — <c>getNewTypeMetadata</c>.</summary>
    /// <remarks>PORT-NOTE: <c>factory.createToken(SyntaxKind.EqualsGreaterThanToken)</c> is the factory's wide
    /// <c>CreateToken</c>, whose answer for a punctuation kind is a <see cref="PunctuationToken"/> — the cast names the
    /// class the <c>createArrowFunction</c> slot requires (the note <c>NodeFactory.CreateArrowFunction</c> records for
    /// the same pair). The <c>properties</c> accumulation is a <c>List&lt;Node&gt;</c> for the reason
    /// <see cref="InjectClassTypeMetadata"/> records.</remarks>
    private List<Node>? GetNewTypeMetadata(Node node, IClassLikeDeclarationBase container)
    {
        if (_typeSerializer is not null)
        {
            List<Node>? properties = null;
            if (ShouldAddTypeMetadata(node))
            {
                var typeProperty = _factory.CreatePropertyAssignment("type", _factory.CreateArrowFunction(
                    /*modifiers*/ null,
                    /*typeParameters*/ null,
                    [],
                    /*type*/ null,
                    (PunctuationToken)_factory.CreateToken(SyntaxKind.EqualsGreaterThanToken),
                    _typeSerializer.SerializeTypeOfNode(
                        new TypeSerializer.RuntimeTypeSerializerContext { CurrentLexicalScope = _currentLexicalScope!, CurrentNameScope = container },
                        node,
                        container)));
                properties = (List<Node>?)Core.Append(properties, typeProperty);
            }
            if (ShouldAddParamTypesMetadata(node))
            {
                var paramTypeProperty = _factory.CreatePropertyAssignment("paramTypes", _factory.CreateArrowFunction(
                    /*modifiers*/ null,
                    /*typeParameters*/ null,
                    [],
                    /*type*/ null,
                    (PunctuationToken)_factory.CreateToken(SyntaxKind.EqualsGreaterThanToken),
                    _typeSerializer.SerializeParameterTypesOfNode(
                        new TypeSerializer.RuntimeTypeSerializerContext { CurrentLexicalScope = _currentLexicalScope!, CurrentNameScope = container },
                        node,
                        container)));
                properties = (List<Node>?)Core.Append(properties, paramTypeProperty);
            }
            if (ShouldAddReturnTypeMetadata(node))
            {
                var returnTypeProperty = _factory.CreatePropertyAssignment("returnType", _factory.CreateArrowFunction(
                    /*modifiers*/ null,
                    /*typeParameters*/ null,
                    [],
                    /*type*/ null,
                    (PunctuationToken)_factory.CreateToken(SyntaxKind.EqualsGreaterThanToken),
                    _typeSerializer.SerializeReturnTypeOfNode(
                        new TypeSerializer.RuntimeTypeSerializerContext { CurrentLexicalScope = _currentLexicalScope!, CurrentNameScope = container },
                        node)));
                properties = (List<Node>?)Core.Append(properties, returnTypeProperty);
            }
            if (properties is not null)
            {
                var typeInfoMetadata = _emitHelpers.CreateMetadataHelper("design:typeinfo", _factory.CreateObjectLiteralExpression(properties, /*multiLine*/ true));
                return [_factory.CreateDecorator(typeInfoMetadata)];
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1171-1184 — <c>shouldAddTypeMetadata</c>.</summary>
    private static bool ShouldAddTypeMetadata(Node node)
        => node.Kind is SyntaxKind.MethodDeclaration
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor
            or SyntaxKind.PropertyDeclaration;

    /// <summary>ported from: src/compiler/transformers/ts.ts:1186-1195 — <c>shouldAddReturnTypeMetadata</c>.</summary>
    private static bool ShouldAddReturnTypeMetadata(Node node)
        => node.Kind == SyntaxKind.MethodDeclaration;

    /// <summary>ported from: src/compiler/transformers/ts.ts:1197-1214 — <c>shouldAddParamTypesMetadata</c>.</summary>
    private static bool ShouldAddParamTypesMetadata(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                return Utilities.GetFirstConstructorWithBody(node) is not null;
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:1216-1347 — property names, heritage clauses, properties, constructors
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:1216-1239 — <c>getExpressionForPropertyName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>ClassElement | EnumMember</c> share no model interface with a
    /// <c>name</c> slot other than <c>INamedDeclaration</c>, whose <c>Name</c> is a <c>Node?</c>; the reference's
    /// <c>member.name!</c> is the port's <c>!</c> on that read.</remarks>
    private Expression GetExpressionForPropertyName(Node member, bool generateNameForComputedPropertyName)
    {
        var name = ((INamedDeclaration)member).Name!;
        if (NodeTests.IsPrivateIdentifier(name))
        {
            return _factory.CreateIdentifier("", /*originalKeywordKind*/ null, /*hasExtendedUnicodeEscape*/ null);
        }
        else if (NodeTests.IsComputedPropertyName(name))
        {
            return generateNameForComputedPropertyName && !TransformationUtilities.IsSimpleInlineableExpression(((ComputedPropertyName)name).Expression!)
                ? _factory.GetGeneratedNameForNode(name, /*flags*/ null)
                : ((ComputedPropertyName)name).Expression!;
        }
        else if (NodeTests.IsIdentifier(name))
        {
            return _factory.CreateStringLiteral(Utilities.IdText((Identifier)name), /*isSingleQuote*/ null, /*hasExtendedUnicodeEscape*/ null);
        }
        else
        {
            return (Expression)_factory.CloneNode(name);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1241-1265 — <c>visitPropertyNameOfClassElement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>PropertyName</c> union is the port's <see cref="Node"/> (the slot every
    /// caller feeds). <c>skipPartiallyEmittedExpressions</c> is the <see cref="Expression"/> overload, which the
    /// <c>isExpression</c>-tested visit answers; <c>hoistVariableDeclaration</c> is the context's,
    /// <c>factory.getGeneratedNameForNode(name)</c> is the two-argument form with the reference's absent flags.</remarks>
    private Node VisitPropertyNameOfClassElement(IClassElement member)
    {
        var name = member.Name!;
        // Computed property names need to be transformed into a hoisted variable when they are used more than once.
        // The names are used more than once when the property has a decorator.
        if (_legacyDecorators && NodeTests.IsComputedPropertyName(name) && Utilities.HasDecorators((Node)member))
        {
            var expression = Utilities.VisitNode(((ComputedPropertyName)name).Expression, Visitor, Utilities.IsExpression);
            Debug.Assert(expression is not null);
            var innerExpression = Utilities.SkipPartiallyEmittedExpressions(expression!);
            if (!TransformationUtilities.IsSimpleInlineableExpression(innerExpression))
            {
                var generatedName = _factory.GetGeneratedNameForNode(name, /*flags*/ null);
                _context.HoistVariableDeclaration(generatedName);
                return _factory.UpdateComputedPropertyName((ComputedPropertyName)name, _factory.CreateAssignment(generatedName, expression));
            }
        }
        return Debug.CheckDefined(Utilities.VisitNode(name, Visitor, Utilities.IsPropertyName));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1267-1281 — <c>visitHeritageClause</c>.</summary>
    private HeritageClause? VisitHeritageClause(HeritageClause node)
    {
        if (node.Token == SyntaxKind.ImplementsKeyword)
        {
            // implements clauses are elided
            return null;
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1283-1295 — <c>visitExpressionWithTypeArguments</c>.</summary>
    private ExpressionWithTypeArguments VisitExpressionWithTypeArguments(ExpressionWithTypeArguments node)
        => _factory.UpdateExpressionWithTypeArguments(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsLeftHandSideExpression)),
            /*typeArguments*/ null);

    /// <summary>ported from: src/compiler/transformers/ts.ts:1297-1299 — <c>shouldEmitFunctionLikeDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's guard narrows <c>T["body"]</c> to its non-null type; C# cannot narrow through a
    /// predicate, so the port answers <c>bool</c> and the reference's <c>node.body!</c> reads in the callers stay
    /// <c>!</c> — the same contract <c>Utilities.IsParameterPropertyDeclaration</c> records.</remarks>
    private static bool ShouldEmitFunctionLikeDeclaration(Node node)
        => !Utilities.NodeIsMissing(((IFunctionLikeDeclarationBase)node).Body);

    /// <summary>ported from: src/compiler/transformers/ts.ts:1301-1334 — <c>visitPropertyDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>concatenate(modifiers, factory.createModifiersFromModifierFlags(…))</c> is
    /// <see cref="Core.Concatenate{T}"/>; the port's <c>CreateModifiersFromModifierFlags</c> answers
    /// <c>List&lt;ModifierToken&gt;?</c> while <c>modifiers</c> is <c>NodeArray&lt;Node&gt;?</c>, so the second half is
    /// projected to <c>Node</c> — the same widening <c>NodeFactory.ReplaceModifiers</c> records for its
    /// <c>ModifierFlags</c> parameter.</remarks>
    private PropertyDeclaration? VisitPropertyDeclaration(PropertyDeclaration node, Node parent)
    {
        var isAmbient = (node.Flags & NodeFlags.Ambient) != 0 || Utilities.HasSyntacticModifier(node, ModifierFlags.Abstract);
        if (isAmbient && !(_legacyDecorators && Utilities.HasDecorators(node)))
        {
            return null;
        }

        var modifiers = Utilities.IsClassLike(parent)
            ? !isAmbient
                ? Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike)
                : Utilities.VisitNodes(node.Modifiers, ModifierElidingVisitor, Utilities.IsModifierLike)
            : Utilities.VisitNodes(node.Modifiers, DecoratorElidingVisitor, Utilities.IsModifierLike);

        modifiers = InjectClassElementTypeMetadata(modifiers, node, parent);

        // Preserve a `declare x` property with decorators to be handled by the decorators transform
        if (isAmbient)
        {
            return _factory.UpdatePropertyDeclaration(
                node,
                Core.Concatenate<Node>(modifiers, _factory.CreateModifiersFromModifierFlags(ModifierFlags.Ambient)?.Cast<Node>().ToList()) as List<Node>,
                Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName)),
                /*questionOrExclamationToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
        }

        return _factory.UpdatePropertyDeclaration(
            node,
            modifiers,
            VisitPropertyNameOfClassElement(node),
            /*questionOrExclamationToken*/ null,
            /*type*/ null,
            Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1336-1347 — <c>visitConstructor</c>.</summary>
    private ConstructorDeclaration? VisitConstructor(ConstructorDeclaration node)
    {
        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return null;
        }

        return _factory.UpdateConstructorDeclaration(
            node,
            /*modifiers*/ null,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            TransformConstructorBody(node.Body!, node));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1349-1380 — <c>transformConstructorBodyWorker</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>readonly number[]</c>/<c>readonly Statement[]</c> slots are the port's
    /// <c>List&lt;int&gt;</c>/<c>List&lt;IStatement&gt;</c>; <c>statementsOut.push(…)</c> is <c>List.Add</c>.</remarks>
    private void TransformConstructorBodyWorker(
        List<IStatement> statementsOut,
        NodeArray<IStatement> statementsIn,
        int statementOffset,
        List<int> superPath,
        int superPathDepth,
        List<IStatement> initializerStatements)
    {
        var superStatementIndex = superPath[superPathDepth];
        var superStatement = (Node)statementsIn[superStatementIndex];
        Core.AddRange(statementsOut, Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, statementOffset, superStatementIndex - statementOffset));
        if (NodeTests.IsTryStatement(superStatement))
        {
            var tryStatement = (TryStatement)superStatement;
            var tryBlockStatements = new List<IStatement>();

            TransformConstructorBodyWorker(
                tryBlockStatements,
                tryStatement.TryBlock!.Statements!,
                /*statementOffset*/ 0,
                superPath,
                superPathDepth + 1,
                initializerStatements);

            var tryBlockStatementsArray = _factory.CreateNodeArray(tryBlockStatements, /*hasTrailingComma*/ null);
            Utilities.SetTextRange(tryBlockStatementsArray, tryStatement.TryBlock!.Statements);

            statementsOut.Add(_factory.UpdateTryStatement(
                tryStatement,
                _factory.UpdateBlock(tryStatement.TryBlock!, tryBlockStatements),
                Utilities.VisitNode(tryStatement.CatchClause, Visitor, NodeTests.IsCatchClause),
                Utilities.VisitNode(tryStatement.FinallyBlock, Visitor, NodeTests.IsBlock)));
        }
        else
        {
            Core.AddRange(statementsOut, Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, superStatementIndex, 1));
            Core.AddRange(statementsOut, initializerStatements);
        }
        Core.AddRange(statementsOut, Utilities.VisitNodes(statementsIn, Visitor, Utilities.IsStatement, superStatementIndex + 1));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1382-1428 — <c>transformConstructorBody</c>.</summary>
    /// <remarks>PORT-NOTE: <c>factory.mergeLexicalEnvironment(statements, endLexicalEnvironment())</c> is the
    /// <c>List&lt;IStatement&gt;</c> overload (the reference's first argument is a mutable array);
    /// <c>visitFunctionBody(body, visitor, context)</c> is the <see cref="Block"/> instantiation of the port's one
    /// <c>VisitFunctionBody</c>, whose answer is non-null for a non-null body.</remarks>
    private Block TransformConstructorBody(Block body, ConstructorDeclaration constructor)
    {
        var parametersWithPropertyAssignments = Core.Filter(
            constructor.Parameters,
            p => Utilities.IsParameterPropertyDeclaration(p!, constructor));
        if (!Core.Some(parametersWithPropertyAssignments))
        {
            return Utilities.VisitFunctionBody(body, Visitor, _context)!;
        }

        List<IStatement> statements = [];

        _context.ResumeLexicalEnvironment();

        var prologueStatementCount = _factory.CopyPrologue(body.Statements, statements, /*ensureUseStrict*/ false, Visitor);
        var superPath = TransformationUtilities.FindSuperStatementIndexPath(body.Statements!, prologueStatementCount);

        // Transform parameters into property assignments. Transforms this:
        //
        //  constructor (public x, public y) {
        //  }
        //
        // Into this:
        //
        //  constructor (x, y) {
        //      this.x = x;
        //      this.y = y;
        //  }
        //
        var parameterPropertyAssignments = Core.MapDefined<ParameterDeclaration, IStatement>(parametersWithPropertyAssignments, (p, _) => TransformParameterWithPropertyAssignment(p));
        if (superPath.Count > 0)
        {
            TransformConstructorBodyWorker(statements, body.Statements!, prologueStatementCount, superPath, /*superPathDepth*/ 0, parameterPropertyAssignments);
        }
        else
        {
            Core.AddRange(statements, parameterPropertyAssignments);
            Core.AddRange(statements, Utilities.VisitNodes(body.Statements, Visitor, Utilities.IsStatement, prologueStatementCount));
        }

        // End the lexical environment.
        statements = _factory.MergeLexicalEnvironment(statements, _context.EndLexicalEnvironment());
        var block = _factory.CreateBlock(Utilities.SetTextRange(_factory.CreateNodeArray(statements, /*hasTrailingComma*/ null), body.Statements), /*multiLine*/ true);
        Utilities.SetTextRange(block, body);
        NodeFactory.SetOriginalNode(block, body);
        return block;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1430-1466 — <c>transformParameterWithPropertyAssignment</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's answer is an <c>ExpressionStatement</c>; the port declares
    /// <see cref="IStatement"/> so the <c>mapDefined</c> that consumes it (whose slot is the statement array the
    /// initializers are added to) needs no projection. <c>removeAllComments</c>/<c>startOnNewLine</c> are
    /// <see cref="EmitNodes.RemoveAllComments{T}"/>/<see cref="FactoryUtilities.StartOnNewLine{T}"/>, and
    /// <c>moveRangePos(node, -1)</c> is <see cref="TypeScriptTransformSupport.MoveRangePos"/>.</remarks>
    private IStatement? TransformParameterWithPropertyAssignment(ParameterDeclaration node)
    {
        var name = node.Name;
        if (name is null || !NodeTests.IsIdentifier(name))
        {
            return null;
        }

        var identifier = (Identifier)name;

        // TODO(rbuckton): Does this need to be parented?
        var propertyName = Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode(identifier), identifier), identifier.Parent);
        EmitNodes.SetEmitFlags(propertyName!, EmitFlags.NoComments | EmitFlags.NoSourceMap);

        // TODO(rbuckton): Does this need to be parented?
        var localName = Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode(identifier), identifier), identifier.Parent);
        EmitNodes.SetEmitFlags(localName!, EmitFlags.NoComments);

        return FactoryUtilities.StartOnNewLine(
            EmitNodes.RemoveAllComments(
                Utilities.SetTextRange(
                    NodeFactory.SetOriginalNode(
                        _factory.CreateExpressionStatement(
                            _factory.CreateAssignment(
                                Utilities.SetTextRange(
                                    _factory.CreatePropertyAccessExpression(
                                        _factory.CreateThis(),
                                        propertyName),
                                    identifier),
                                localName)),
                        node),
                    TypeScriptTransformSupport.MoveRangePos(node, -1))));
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:1468-1630 — methods, accessors, functions, parameters
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:1468-1500 — <c>visitMethodDeclaration</c>.</summary>
    private MethodDeclaration? VisitMethodDeclaration(MethodDeclaration node, Node parent)
    {
        if ((node.TransformFlags & TransformFlags.ContainsTypeScript) == 0)
        {
            return node;
        }

        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return null;
        }

        var modifiers = Utilities.IsClassLike(parent)
            ? Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike)
            : Utilities.VisitNodes(node.Modifiers, DecoratorElidingVisitor, Utilities.IsModifierLike);

        modifiers = InjectClassElementTypeMetadata(modifiers, node, parent);

        return _factory.UpdateMethodDeclaration(
            node,
            modifiers,
            node.AsteriskToken,
            VisitPropertyNameOfClassElement(node),
            /*questionToken*/ null,
            /*typeParameters*/ null,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            Utilities.VisitFunctionBody(node.Body, Visitor, _context));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1502-1504 — <c>shouldEmitAccessorDeclaration</c>.</summary>
    private static bool ShouldEmitAccessorDeclaration(Node node)
        => !(Utilities.NodeIsMissing(((IFunctionLikeDeclarationBase)node).Body) && Utilities.HasSyntacticModifier(node, ModifierFlags.Abstract));

    /// <summary>ported from: src/compiler/transformers/ts.ts:1506-1529 — <c>visitGetAccessor</c>.</summary>
    private GetAccessorDeclaration? VisitGetAccessor(GetAccessorDeclaration node, Node parent)
    {
        if ((node.TransformFlags & TransformFlags.ContainsTypeScript) == 0)
        {
            return node;
        }

        if (!ShouldEmitAccessorDeclaration(node))
        {
            return null;
        }

        var modifiers = Utilities.IsClassLike(parent)
            ? Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike)
            : Utilities.VisitNodes(node.Modifiers, DecoratorElidingVisitor, Utilities.IsModifierLike);

        modifiers = InjectClassElementTypeMetadata(modifiers, node, parent);

        return _factory.UpdateGetAccessorDeclaration(
            node,
            modifiers,
            VisitPropertyNameOfClassElement(node),
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            Utilities.VisitFunctionBody(node.Body, Visitor, _context) ?? _factory.CreateBlock([], /*multiLine*/ null));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1531-1553 — <c>visitSetAccessor</c>.</summary>
    private SetAccessorDeclaration? VisitSetAccessor(SetAccessorDeclaration node, Node parent)
    {
        if ((node.TransformFlags & TransformFlags.ContainsTypeScript) == 0)
        {
            return node;
        }

        if (!ShouldEmitAccessorDeclaration(node))
        {
            return null;
        }

        var modifiers = Utilities.IsClassLike(parent)
            ? Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike)
            : Utilities.VisitNodes(node.Modifiers, DecoratorElidingVisitor, Utilities.IsModifierLike);

        modifiers = InjectClassElementTypeMetadata(modifiers, node, parent);

        return _factory.UpdateSetAccessorDeclaration(
            node,
            modifiers,
            VisitPropertyNameOfClassElement(node),
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            Utilities.VisitFunctionBody(node.Body, Visitor, _context) ?? _factory.CreateBlock([], /*multiLine*/ null));
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1555-1575 — <c>visitFunctionDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>[updated, createExportMemberAssignmentStatement(node)]</c> is the port's
    /// <c>List&lt;IStatement&gt;</c> (the array visit result).</remarks>
    private object? VisitFunctionDeclaration(FunctionDeclaration node)
    {
        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return _factory.CreateNotEmittedStatement(node);
        }
        var updated = _factory.UpdateFunctionDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
            node.AsteriskToken,
            node.Name,
            /*typeParameters*/ null,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            Utilities.VisitFunctionBody(node.Body, Visitor, _context) ?? _factory.CreateBlock([], /*multiLine*/ null));
        if (IsExportOfNamespace(node))
        {
            var statements = new List<Node> { updated };
            AddExportMemberAssignment(statements, node);
            return statements;
        }
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1577-1592 — <c>visitFunctionExpression</c>.</summary>
    private Expression VisitFunctionExpression(FunctionExpression node)
    {
        if (!ShouldEmitFunctionLikeDeclaration(node))
        {
            return _factory.CreateOmittedExpression();
        }
        var updated = _factory.UpdateFunctionExpression(
            node,
            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
            node.AsteriskToken,
            node.Name,
            /*typeParameters*/ null,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            Utilities.VisitFunctionBody(node.Body, Visitor, _context) ?? _factory.CreateBlock([], /*multiLine*/ null));
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1594-1605 — <c>visitArrowFunction</c>.</summary>
    private ArrowFunction VisitArrowFunction(ArrowFunction node)
    {
        var updated = _factory.UpdateArrowFunction(
            node,
            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
            /*typeParameters*/ null,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            node.EqualsGreaterThanToken,
            Utilities.VisitFunctionBody(node.Body, Visitor, _context));
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1607-1630 — <c>visitParameter</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's inline visitor <c>node =&gt; isDecorator(node) ? visitor(node) : undefined</c>
    /// is the port's lambda (a method group cannot carry the ternary); <c>updated !== node</c> is the reference's
    /// identity test.</remarks>
    private ParameterDeclaration? VisitParameter(ParameterDeclaration node)
    {
        if (Utilities.ParameterIsThisKeyword(node))
        {
            return null;
        }

        var updated = _factory.UpdateParameterDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, n => NodeTests.IsDecorator(n) ? Visitor(n) : null, Utilities.IsModifierLike),
            node.DotDotDotToken,
            Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsBindingName)),
            /*questionToken*/ null,
            /*type*/ null,
            Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));
        if (!ReferenceEquals(updated, node))
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitNodes.SetCommentRange(updated, node);
            Utilities.SetTextRange(updated, TypeScriptTransformSupport.MoveRangePastModifiers(node));
            EmitNodes.SetSourceMapRange(updated, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers(node)));
            EmitNodes.SetEmitFlags(updated.Name!, EmitFlags.NoTrailingSourceMap);
        }
        return updated;
    }

    // ---------------------------------------------------------------------------------------------
    // ts.ts:1632-1786 — variables and the remaining expressions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/transformers/ts.ts:1632-1652 — <c>visitVariableStatement</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getInitializedVariables</c> is
    /// <see cref="TypeScriptTransformSupport.GetInitializedVariables"/> and <c>map</c>/<c>inlineExpressions</c> are
    /// <see cref="Core.Map{T,TU}"/>/<see cref="NodeFactory.InlineExpressions"/>.</remarks>
    private object? VisitVariableStatement(VariableStatement node)
    {
        if (IsExportOfNamespace(node))
        {
            var variables = TypeScriptTransformSupport.GetInitializedVariables(node.DeclarationList!);
            if (variables.Count == 0)
            {
                // elide statement if there are no initialized variables.
                return null;
            }

            return Utilities.SetTextRange(
                _factory.CreateExpressionStatement(
                    _factory.InlineExpressions(
                        Core.Map(variables, (variable, _) => TransformInitializedVariable(variable)))),
                node);
        }
        else
        {
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1654-1675 — <c>transformInitializedVariable</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>InitializedVariableDeclaration</c> is a <see cref="VariableDeclaration"/>
    /// whose initializer is non-null (which <see cref="TypeScriptTransformSupport.GetInitializedVariables"/> filtered
    /// for); <c>flattenDestructuringAssignment</c> is <see cref="Destructuring.FlattenDestructuringAssignment"/> with
    /// the reference's <c>needsValue: false</c>, and the second arm's <c>isIdentifier</c>-narrowed name is cast to
    /// <see cref="Identifier"/>.</remarks>
    private Expression TransformInitializedVariable(VariableDeclaration node)
    {
        var name = node.Name;
        if (Utilities.IsBindingPattern(name))
        {
            return Destructuring.FlattenDestructuringAssignment(
                node,
                Visitor,
                _context,
                FlattenLevel.All,
                /*needsValue*/ false,
                CreateNamespaceExportExpression);
        }
        else
        {
            return Utilities.SetTextRange(
                _factory.CreateAssignment(
                    GetNamespaceMemberNameWithSourceMapsAndWithoutComments((Identifier)name!),
                    Debug.CheckDefined(Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression))),
                /*location*/ node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1677-1689 — <c>visitVariableDeclaration</c>.</summary>
    private VariableDeclaration VisitVariableDeclaration(VariableDeclaration node)
    {
        var updated = _factory.UpdateVariableDeclaration(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsBindingName)),
            /*exclamationToken*/ null,
            /*type*/ null,
            Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));
        if (node.Type is not null)
        {
            EmitNodes.SetTypeNode(updated.Name!, node.Type);
        }
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1691-1720 — <c>visitParenthesizedExpression</c>.</summary>
    /// <remarks>PORT-NOTE: <c>~(OuterExpressionKinds.Assertions | OuterExpressionKinds.ExpressionsWithTypeArguments)</c>
    /// is the enum complement, which is what the reference's <c>~</c> computes on the flag value;
    /// <c>factory.createPartiallyEmittedExpression(expression, node)</c> keeps the reference's two arguments.</remarks>
    private Expression VisitParenthesizedExpression(ParenthesizedExpression node)
    {
        var innerExpression = FactoryUtilities.SkipOuterExpressions(node.Expression!, ~(OuterExpressionKinds.Assertions | OuterExpressionKinds.ExpressionsWithTypeArguments));
        if (Utilities.IsAssertionExpression(innerExpression) || NodeTests.IsSatisfiesExpression(innerExpression))
        {
            // Make sure we consider all nested cast expressions, e.g.:
            // (<any><number><any>-A).x;
            var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
            Debug.Assert(expression is not null);

            // We have an expression of the form: (<Type>SubExpr). Emitting this as (SubExpr)
            // is really not desirable. We would like to emit the subexpression as-is. Omitting
            // the parentheses, however, could cause change in the semantics of the generated
            // code if the casted expression has a lower precedence than the rest of the
            // expression.
            //
            // To preserve comments, we return a "PartiallyEmittedExpression" here which will
            // preserve the position information of the original expression.
            //
            // Due to the auto-parenthesization rules used by the visitor and factory functions
            // we can safely elide the parentheses here, as a new synthetic
            // ParenthesizedExpression will be inserted if we remove parentheses too
            // aggressively.
            //
            // If there are leading comments on the expression itself, the emitter will handle ASI
            // for return, throw, and yield by re-introducing parenthesis during emit on an as-need
            // basis.
            return _factory.CreatePartiallyEmittedExpression(expression, node);
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1722-1726 — <c>visitAssertionExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>AssertionExpression</c> union (<c>TypeAssertion | AsExpression</c>) has no model
    /// interface — the two are separate classes with separately-typed <c>Expression</c> slots (one
    /// <see cref="UnaryExpression"/>?, one <see cref="Expression"/>?) — so the parameter is a <see cref="Node"/> and
    /// the slot is read from the narrowed class, as <c>NodeFactory</c> does for the same union.</remarks>
    private Expression VisitAssertionExpression(Node node)
    {
        var expression = node is AsExpression asExpression
            ? asExpression.Expression
            : ((TypeAssertion)node).Expression;
        var visited = Utilities.VisitNode(expression, Visitor, Utilities.IsExpression);
        Debug.Assert(visited is not null);
        return _factory.CreatePartiallyEmittedExpression(visited, node);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1728-1732 — <c>visitNonNullExpression</c>.</summary>
    private Expression VisitNonNullExpression(NonNullExpression node)
    {
        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsLeftHandSideExpression);
        Debug.Assert(expression is not null);
        return _factory.CreatePartiallyEmittedExpression(expression, node);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1734-1738 — <c>visitSatisfiesExpression</c>.</summary>
    private Expression VisitSatisfiesExpression(SatisfiesExpression node)
    {
        var expression = Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression);
        Debug.Assert(expression is not null);
        return _factory.CreatePartiallyEmittedExpression(expression, node);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:1740-1747 — <c>visitCallExpression</c>.</summary>
    private CallExpression VisitCallExpression(CallExpression node)
        => _factory.UpdateCallExpression(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
            /*typeArguments*/ null,
            Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/ts.ts:1749-1756 — <c>visitNewExpression</c>.</summary>
    private NewExpression VisitNewExpression(NewExpression node)
        => _factory.UpdateNewExpression(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
            /*typeArguments*/ null,
            Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/ts.ts:1758-1765 — <c>visitTaggedTemplateExpression</c>.</summary>
    private TaggedTemplateExpression VisitTaggedTemplateExpression(TaggedTemplateExpression node)
        => _factory.UpdateTaggedTemplateExpression(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Tag, Visitor, Utilities.IsExpression)),
            /*typeArguments*/ null,
            Debug.CheckDefined(Utilities.VisitNode(node.Template, Visitor, Utilities.IsTemplateLiteral)));

    /// <summary>ported from: src/compiler/transformers/ts.ts:1767-1774 — <c>visitJsxSelfClosingElement</c>.</summary>
    private JsxSelfClosingElement VisitJsxSelfClosingElement(JsxSelfClosingElement node)
        => _factory.UpdateJsxSelfClosingElement(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.TagName, Visitor, Utilities.IsJsxTagNameExpression)),
            /*typeArguments*/ null,
            Debug.CheckDefined(Utilities.VisitNode(node.Attributes, Visitor, NodeTests.IsJsxAttributes)));

    /// <summary>ported from: src/compiler/transformers/ts.ts:1776-1783 — <c>visitJsxJsxOpeningElement</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's own name for this member is <c>visitJsxJsxOpeningElement</c>; the port keeps
    /// it.</remarks>
    private JsxOpeningElement VisitJsxJsxOpeningElement(JsxOpeningElement node)
        => _factory.UpdateJsxOpeningElement(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.TagName, Visitor, Utilities.IsJsxTagNameExpression)),
            /*typeArguments*/ null,
            Debug.CheckDefined(Utilities.VisitNode(node.Attributes, Visitor, NodeTests.IsJsxAttributes)));
}
