namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:50854-50862 (declaredParameterTypeContainsUndefined)
// ported from: src/compiler/checker.ts:50864-50883 (requiresAddingImplicitUndefined … isOptionalUninitializedParameterProperty)
// ported from: src/compiler/checker.ts:50885-50923 (isExpandoFunctionDeclaration … getNodeCheckFlags)
// ported from: src/compiler/checker.ts:50925-51055 (hasNodeCheckFlag, calculateNodeCheckFlagWorker)
// ported from: src/compiler/checker.ts:51057-51094 (getEnumMemberValue … isFunctionType)
// ported from: src/compiler/checker.ts:51096-51223 (getTypeReferenceSerializationKind … hasGlobalName)
// ported from: src/compiler/checker.ts:51225-51316 (getReferencedValueSymbol … getReferencedValueDeclarations)
// ported from: src/compiler/checker.ts:51318-51378 (isLiteralConstDeclaration … getNonlocalEffectiveTypeAnnotationNode)
/// <summary>
/// The node-check-flag block, the constant-value queries and the literal, accessor and JSX-entity helpers that the
/// emit resolver (checker.ts:51380's <c>createResolver</c>, M6-6) and the node builder (M6-6) reach.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's <c>factory</c> (imported at checker.ts:196 from <c>factory/nodeFactory.ts</c>) is the
/// port's <see cref="_factory"/> (<c>NodeFactory.Factory</c>), the singleton the node-builder slice declares on this
/// partial class and creates every serialized node through. The checker's <c>factory</c> sites reach that same
/// singleton directly (the flow-analysis sites in <c>Checker.TypeOfSymbols.cs</c>, checker.ts:12026-12063, and
/// <c>Checker.Check.Symbols.cs</c>'s <c>createCallSignature</c>, checker.ts:44068); the checker's other factory,
/// <see cref="FactoryUtilities.ParseNodeFactory"/> (the reference's <c>parseNodeFactory</c>, parser.ts:441), is used
/// only at the three reference sites that use it (checker.ts:10209, :11694-11696, :36251). The two instances differ
/// only in the <c>NoParenthesizerRules</c>/<c>NoIndentationOnFreshPropertyAccess</c> flags, which none of the
/// <c>create*</c> members used here reads.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>LazyNodeCheckFlags</c> (types.ts:5881-5895) is a union of thirteen <c>NodeCheckFlags</c> members
/// with no C# counterpart — the generated model carries the enum's composite <c>NodeCheckFlags.LazyFlags</c>
/// (types.ts:6247) instead. The internal flag parameters are therefore typed <see cref="NodeCheckFlags"/> where TS
/// names that union, and the resolver's <c>object? flags</c> slot is unboxed at the wiring point.
/// </para>
/// <para>
/// PORT-NOTE: <c>AllAccessorDeclarations</c> (types.ts:5818-5823) is a types.ts interface filled with an object literal;
/// the generated model carries only <see cref="IAllAccessorDeclarations"/>, so <see cref="AllAccessorDeclarations"/> is
/// this file's one constructible implementation — nested, so that no sibling band can declare the same name and so that
/// it can be folded into the model when the model slice gives the interface a home.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Parameter optionality and expando containers (checker.ts:50854-50917)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:50854-50862 — <c>declaredParameterTypeContainsUndefined</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as with the three parameter helpers below, TS's union <c>ParameterDeclaration | JSDocParameterTag</c>
    /// is the class both derive from, <c>Node</c> (there is no C# union and no shared declaration interface).
    /// </remarks>
    internal bool DeclaredParameterTypeContainsUndefined(Node parameter)
    {
        var typeNode = GetNonlocalEffectiveTypeAnnotationNode(parameter);
        if (typeNode is null)
        {
            return false;
        }

        var type = GetTypeFromTypeNode(typeNode);
        // allow error type here to avoid confusing errors that the annotation has to contain undefined when it does in cases like this:
        //
        // export function fn(x?: Unresolved | undefined): void {}
        return IsErrorType(type) || ContainsUndefinedType(type);
    }

    /// <summary>ported from: src/compiler/checker.ts:50864-50866 — <c>requiresAddingImplicitUndefined</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's union <c>ParameterDeclaration | JSDocParameterTag</c> has no C# counterpart (both are node
    /// classes with no shared declaration interface), so the parameter is the class both derive from — <c>Node</c>,
    /// which is also the parameter type the generated <c>ITypeChecker.RequiresAddingImplicitUndefined</c> declares.
    /// </remarks>
    internal bool RequiresAddingImplicitUndefined(Node parameter, Node? enclosingDeclaration)
        => (IsRequiredInitializedParameter(parameter, enclosingDeclaration) || IsOptionalUninitializedParameterProperty(parameter))
            && !DeclaredParameterTypeContainsUndefined(parameter);

    /// <summary>ported from: src/compiler/checker.ts:50868-50876 — <c>isRequiredInitializedParameter</c>.</summary>
    internal bool IsRequiredInitializedParameter(Node parameter, Node? enclosingDeclaration)
    {
        if (!_strictNullChecks
            || IsOptionalParameter(parameter)
            || NodeTests.IsJSDocParameterTag(parameter)
            || ((ParameterDeclaration)parameter).Initializer is null)
        {
            return false;
        }

        if (Utilities.HasSyntacticModifier(parameter, ModifierFlags.ParameterPropertyModifier))
        {
            return enclosingDeclaration is not null && Utilities.IsFunctionLikeDeclaration(enclosingDeclaration);
        }

        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:50878-50883 — <c>isOptionalUninitializedParameterProperty</c>.</summary>
    internal bool IsOptionalUninitializedParameterProperty(Node parameter)
        => _strictNullChecks
            && IsOptionalParameter(parameter)
            && (NodeTests.IsJSDocParameterTag(parameter) || ((ParameterDeclaration)parameter).Initializer is null)
            && Utilities.HasSyntacticModifier(parameter, ModifierFlags.ParameterPropertyModifier);

    /// <summary>ported from: src/compiler/checker.ts:50885-50908 — <c>isExpandoFunctionDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the initializer's symbol with <c>getSymbolOfDeclaration</c> (checker.ts:5930), whose port
    /// member asserts the symbol is present; the reference's own <c>!symbol</c> guard on the next line shows the value
    /// may be absent, so the port reads it through the nullable <c>getSymbolOfNode</c> accessor (checker.ts:5326).
    /// </remarks>
    internal bool IsExpandoFunctionDeclaration(IDeclaration? node)
    {
        var declaration = Utilities.GetParseTreeNode(
            (Node?)node,
            n => NodeTests.IsFunctionDeclaration(n) || NodeTests.IsVariableDeclaration(n));
        if (declaration is null)
        {
            return false;
        }

        ISymbol? symbol;
        if (NodeTests.IsVariableDeclaration(declaration))
        {
            var variableDeclaration = (VariableDeclaration)declaration;
            if (variableDeclaration.Type is not null || !Utilities.IsInJSFile(declaration) && !IsVarConstLike(declaration))
            {
                return false;
            }

            var initializer = Utilities.GetDeclaredExpandoInitializer(declaration);
            if (initializer is null || !Utilities.CanHaveSymbol(initializer))
            {
                return false;
            }

            symbol = GetSymbolOfNode(initializer);
        }
        else
        {
            symbol = GetSymbolOfDeclaration((IDeclaration)declaration);
        }

        if (symbol is null || (symbol.Flags & (SymbolFlags.Function | SymbolFlags.Variable)) == 0)
        {
            return false;
        }

        return Utilities.ForEachEntry<string, ISymbol, bool>(
            GetExportsOfSymbol(symbol),
            (p, _) => (p.Flags & SymbolFlags.Value) != 0 && Utilities.IsExpandoPropertyDeclaration((IDeclaration?)p.ValueDeclaration)) is true;
    }

    /// <summary>ported from: src/compiler/checker.ts:50910-50917 — <c>getPropertiesOfContainerFunction</c>.</summary>
    internal List<ISymbol> GetPropertiesOfContainerFunction(IDeclaration? node)
    {
        var declaration = Utilities.GetParseTreeNode<FunctionDeclaration>((Node?)node, NodeTests.IsFunctionDeclaration);
        if (declaration is null)
        {
            return Core.EmptyArray<ISymbol>.Value;
        }

        var symbol = GetSymbolOfDeclaration(declaration);
        return symbol is not null ? GetPropertiesOfType(GetTypeOfSymbol(symbol)) : Core.EmptyArray<ISymbol>.Value;
    }

    // ---------------------------------------------------------------------------------------------
    // The node check flags (checker.ts:50919-51055)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:50919-50923 — <c>getNodeCheckFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.id || 0</c> — a read that does <em>not</em> allocate an id — and then bounds-checks
    /// the sparse <c>nodeLinks</c> array. The port reads the same id slot (<c>0</c> is "unassigned", see
    /// <see cref="Node.Id"/>) and asks the paged table for it, which allocates nothing and answers "absent" for an id
    /// whose page was never written: that is TS's <c>0</c> hole, reported as <see cref="NodeCheckFlags.None"/>.
    /// </remarks>
    internal NodeCheckFlags GetNodeCheckFlags(Node node)
        => _nodeLinks.TryGet(node.Id)?.Flags ?? NodeCheckFlags.None;

    /// <summary>ported from: src/compiler/checker.ts:50925-50928 — <c>hasNodeCheckFlag</c>.</summary>
    internal bool HasNodeCheckFlag(Node node, NodeCheckFlags flag)
    {
        CalculateNodeCheckFlagWorker(node, flag);
        return (GetNodeCheckFlags(node) & flag) != 0;
    }

    /// <summary>ported from: src/compiler/checker.ts:50930-51055 — <c>calculateNodeCheckFlagWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>forEachNodeRecursively&lt;T&gt;</c> is specialized to <c>object?</c> because TS
    /// instantiates <c>T</c> only with the three callbacks below, all of which answer <c>"skip" | undefined</c>. The
    /// <c>"skip"</c> string literal is the port's <see cref="ForEachChildTable.Skip"/> marker (the port's own
    /// <c>forEachChildRecursively</c>, Parser/ForEachChildTable.cs:443), and the callbacks take the parent node the
    /// walker passes even though the reference's own declarations read only the node. The nested helpers' <c>node</c>
    /// parameter, which shadows this method's <c>node</c> in the reference, is named <c>subNode</c> because C# rejects
    /// the shadowing (CS0136).
    /// </remarks>
    internal void CalculateNodeCheckFlagWorker(Node node, NodeCheckFlags flag)
    {
        if (!Js.IsTruthy(_compilerOptions.NoCheck)
            && Utilities.CanIncludeBindAndCheckDiagnostics(Utilities.GetSourceFileOfNode(node)!, _compilerOptions))
        {
            // Unless noCheck is passed, assume calculation of node check flags has been done eagerly.
            // This saves needing to mark up where in the eager traversal certain results are "done",
            // just to reconcile the eager and lazy results. This wouldn't be hard if an eager typecheck
            // was actually an in-order traversal, but it isn't - some nodes are deferred, and so don't
            // have these node check flags calculated until that deferral is completed. As an example,
            // in concept, we could consider a class that we've called `checkSourceElement` on as having had
            // these flags calculated, but since the method bodies are deferred, we actually can't set the
            // flags as having been calculated until that deferral is completed.
            // The downside to this either/or approach to eager or lazy calculation is that we can't combine
            // a partial eager traversal and lazy calculation for the missing bits, and there's a bit of
            // overlap in functionality. This isn't a huge loss for any usecases today, but would be nice
            // alongside language service partial file checking and editor-triggered emit.
            return;
        }

        var links = GetNodeLinks(node);
        if ((links.CalculatedFlags & flag) != 0)
        {
            return;
        }

        // This is only the set of `NodeCheckFlags` our emitter actually looks for, not all of them
        switch (flag)
        {
            case NodeCheckFlags.SuperInstance:
            case NodeCheckFlags.SuperStatic:
                CheckSingleSuperExpression(node);
                return;
            case NodeCheckFlags.MethodWithSuperPropertyAccessInAsync:
            case NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync:
            case NodeCheckFlags.ContainsSuperPropertyInStaticInitializer:
                CheckChildSuperExpressions(node);
                return;
            case NodeCheckFlags.CaptureArguments:
            case NodeCheckFlags.ContainsCapturedBlockScopeBinding:
            case NodeCheckFlags.NeedsLoopOutParameter:
            case NodeCheckFlags.ContainsConstructorReference:
                CheckChildIdentifiers(node);
                return;
            case NodeCheckFlags.ConstructorReference:
                CheckSingleIdentifier(node);
                return;
            case NodeCheckFlags.LoopWithCapturedBlockScopedBinding:
            case NodeCheckFlags.BlockScopedBindingInLoop:
            case NodeCheckFlags.CapturedBlockScopedBinding:
                CheckContainingBlockScopeBindingUses(node);
                return;
            default:
                Debug.AssertNever(flag, $"Unhandled node check flag calculation: {Debug.FormatNodeCheckFlags(flag)}");
                return;
        }

        object? ForEachNodeRecursively(Node root, Func<Node, Node, object?> cb)
        {
            var rootResult = cb(root, root.Parent!);
            if (ReferenceEquals(rootResult, ForEachChildTable.Skip))
            {
                return null;
            }

            if (Js.IsTruthy(rootResult))
            {
                return rootResult;
            }

            return ForEachChildTable.ForEachChildRecursively(root, cb);
        }

        object? CheckSuperExpressions(Node subNode)
        {
            var subLinks = GetNodeLinks(subNode);
            if ((subLinks.CalculatedFlags & flag) != 0)
            {
                return ForEachChildTable.Skip;
            }

            subLinks.CalculatedFlags |= NodeCheckFlags.MethodWithSuperPropertyAccessInAsync | NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync | NodeCheckFlags.ContainsSuperPropertyInStaticInitializer;
            CheckSingleSuperExpression(subNode);
            return null;
        }

        void CheckChildSuperExpressions(Node subNode)
            => ForEachNodeRecursively(subNode, (n, _) => CheckSuperExpressions(n));

        void CheckSingleSuperExpression(Node subNode)
        {
            var nodeLinks = GetNodeLinks(subNode); // This is called on sub-nodes of the original input, make sure we set `calculatedFlags` on the correct node
            nodeLinks.CalculatedFlags |= NodeCheckFlags.SuperInstance | NodeCheckFlags.SuperStatic; // Yes, we set this on non-applicable nodes, so we can entirely skip the traversal on future calls
            if (subNode.Kind == SyntaxKind.SuperKeyword)
            {
                CheckSuperExpression(subNode);
            }
        }

        object? CheckIdentifiers(Node subNode)
        {
            var subLinks = GetNodeLinks(subNode);
            if ((subLinks.CalculatedFlags & flag) != 0)
            {
                return ForEachChildTable.Skip;
            }

            subLinks.CalculatedFlags |= NodeCheckFlags.CaptureArguments | NodeCheckFlags.ContainsCapturedBlockScopeBinding | NodeCheckFlags.NeedsLoopOutParameter | NodeCheckFlags.ContainsConstructorReference;
            CheckSingleIdentifier(subNode);
            return null;
        }

        void CheckChildIdentifiers(Node subNode)
            => ForEachNodeRecursively(subNode, (n, _) => CheckIdentifiers(n));

        bool IsExpressionNodeOrShorthandPropertyAssignmentName(Identifier identifier)
        {
            // TODO(jakebailey): Just use isExpressionNode once that considers these identifiers to be expressions.
            // CHECKER-FIX: the reference's `isShorthandPropertyAssignment(node.parent)` is a *predicate*, and the second
            // operand of `||` reads `node.parent.objectAssignmentInitializer ?? node.parent.name` only when it holds;
            // the port spelled the guard as an unconditional cast, which throws on every other parent kind (a
            // ClassDeclaration reaches here through CalculateNodeCheckFlagWorker's ForEachChildRecursively walk).
            var shorthandParent = identifier.Parent as ShorthandPropertyAssignment;
            return Utilities.IsExpressionNode(identifier)
                || NodeTests.IsShorthandPropertyAssignment(identifier.Parent!)
                    && ReferenceEquals(shorthandParent!.ObjectAssignmentInitializer ?? (object?)shorthandParent.Name, identifier);
        }

        void CheckSingleIdentifier(Node subNode)
        {
            var nodeLinks = GetNodeLinks(subNode);
            nodeLinks.CalculatedFlags |= NodeCheckFlags.ConstructorReference;
            if (NodeTests.IsIdentifier(subNode))
            {
                nodeLinks.CalculatedFlags |= NodeCheckFlags.BlockScopedBindingInLoop | NodeCheckFlags.CapturedBlockScopedBinding; // Can't set on all arbitrary nodes (these nodes have this flag set by `checkSingleBlockScopeBinding` only)
                var identifier = (Identifier)subNode;
                if (IsExpressionNodeOrShorthandPropertyAssignmentName(identifier)
                    && !(NodeTests.IsPropertyAccessExpression(subNode.Parent!) && ReferenceEquals(((PropertyAccessExpression)subNode.Parent!).Name, subNode)))
                {
                    var s = GetResolvedSymbol(identifier);
                    if (s is not null && !ReferenceEquals(s, _unknownSymbol))
                    {
                        CheckIdentifierCalculateNodeCheckFlags(identifier, s);
                    }
                }
            }
        }

        object? CheckBlockScopeBindings(Node subNode)
        {
            var subLinks = GetNodeLinks(subNode);
            if ((subLinks.CalculatedFlags & flag) != 0)
            {
                return ForEachChildTable.Skip;
            }

            subLinks.CalculatedFlags |= NodeCheckFlags.LoopWithCapturedBlockScopedBinding | NodeCheckFlags.BlockScopedBindingInLoop | NodeCheckFlags.CapturedBlockScopedBinding;
            CheckSingleBlockScopeBinding(subNode);
            return null;
        }

        void CheckContainingBlockScopeBindingUses(Node subNode)
        {
            var scope = Utilities.GetEnclosingBlockScopeContainer(Utilities.IsDeclarationName(subNode) ? subNode.Parent! : subNode);
            ForEachNodeRecursively(scope, (n, _) => CheckBlockScopeBindings(n));
        }

        void CheckSingleBlockScopeBinding(Node subNode)
        {
            CheckSingleIdentifier(subNode);
            if (NodeTests.IsComputedPropertyName(subNode))
            {
                CheckComputedPropertyName((ComputedPropertyName)subNode);
            }

            if (NodeTests.IsPrivateIdentifier(subNode) && Utilities.IsClassElement(subNode.Parent!))
            {
                SetNodeLinksForPrivateIdentifierScope(subNode.Parent!);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Constant values (checker.ts:51057-51094)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51057-51060 — <c>getEnumMemberValue</c>.</summary>
    internal IEvaluatorResult<object?> GetEnumMemberValue(EnumMember node)
    {
        ComputeEnumMemberValues((EnumDeclaration)node.Parent!);
        return GetNodeLinks(node).EnumMemberValue ?? Utilities.EvaluatorResult<object?>(null);
    }

    /// <summary>ported from: src/compiler/checker.ts:51062-51070 — <c>canHaveConstantValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's type guard <c>node is EnumMember | AccessExpression</c> becomes <c>bool</c> (mapping rule (d)) so
    /// that the reference's own reader — <c>getConstantValue: nodeIn => getParseTreeNode(nodeIn, canHaveConstantValue)</c>
    /// (checker.ts:51407, M6-6) — can pass this member as its <c>Func&lt;Node, bool&gt;</c> node test.
    /// </remarks>
    internal bool CanHaveConstantValue(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.EnumMember:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
                return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:51072-51090 — <c>getConstantValue</c>.</summary>
    internal object? GetConstantValue(Node node)
    {
        if (node.Kind == SyntaxKind.EnumMember)
        {
            return GetEnumMemberValue((EnumMember)node).Value;
        }

        if (GetNodeLinks(node).ResolvedSymbol is null)
        {
            CheckExpressionCached((Expression)node); // ensure cached resolved symbol is set
        }

        var symbol = GetNodeLinks(node).ResolvedSymbol
            ?? (Utilities.IsEntityNameExpression(node) ? ResolveEntityName(node, SymbolFlags.Value, /*ignoreErrors*/ true) : null);
        if (symbol is not null && (symbol.Flags & SymbolFlags.EnumMember) != 0)
        {
            // inline property\index accesses only for const enums
            var member = (EnumMember)symbol.ValueDeclaration!;
            if (Utilities.IsEnumConst((EnumDeclaration)member.Parent!))
            {
                return GetEnumMemberValue(member).Value;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:51092-51094 — <c>isFunctionType</c>.</summary>
    internal bool IsFunctionType(IType type)
        => (type.Flags & TypeFlags.Object) != 0 && Core.Length(GetSignaturesOfType(type, SignatureKind.Call)) > 0;

    // ---------------------------------------------------------------------------------------------
    // The type serializer's reachability check and the emitted type nodes (checker.ts:51096-51219)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51096-51178 — <c>getTypeReferenceSerializationKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>typeNameIn: EntityName</c> is the <c>Identifier | QualifiedName</c> union, which the port
    /// carries as <c>Node</c> (its <c>ResolveEntityName</c> and <c>IsEntityName</c> take <c>Node</c> as well); the
    /// parameter is nullable because the generated <c>IEmitResolver</c> slot is, and both null guards the reference
    /// performs are kept.
    /// </remarks>
    internal TypeReferenceSerializationKind GetTypeReferenceSerializationKind(Node? typeNameIn, Node? location)
    {
        // ensure both `typeName` and `location` are parse tree nodes.
        var typeName = Utilities.GetParseTreeNode(typeNameIn, Utilities.IsEntityName);
        if (typeName is null)
        {
            return TypeReferenceSerializationKind.Unknown;
        }

        if (location is not null)
        {
            location = Utilities.GetParseTreeNode(location);
            if (location is null)
            {
                return TypeReferenceSerializationKind.Unknown;
            }
        }

        // Resolve the symbol as a value to ensure the type can be reached at runtime during emit.
        var isTypeOnly = false;
        if (NodeTests.IsQualifiedName(typeName))
        {
            var rootValueSymbol = ResolveEntityName(Utilities.GetFirstIdentifier(typeName), SymbolFlags.Value, /*ignoreErrors*/ true, /*dontResolveAlias*/ true, location);
            isTypeOnly = rootValueSymbol?.Declarations is not null && Core.Every(rootValueSymbol.Declarations, (d, _) => Utilities.IsTypeOnlyImportOrExportDeclaration((Node)d));
        }

        var valueSymbol = ResolveEntityName(typeName, SymbolFlags.Value, /*ignoreErrors*/ true, /*dontResolveAlias*/ true, location);
        var resolvedValueSymbol = valueSymbol is not null && (valueSymbol.Flags & SymbolFlags.Alias) != 0 ? ResolveAlias(valueSymbol) : valueSymbol;
        isTypeOnly = isTypeOnly || valueSymbol is not null && GetTypeOnlyAliasDeclaration(valueSymbol, SymbolFlags.Value) is not null;

        // Resolve the symbol as a type so that we can provide a more useful hint for the type serializer.
        var typeSymbol = ResolveEntityName(typeName, SymbolFlags.Type, /*ignoreErrors*/ true, /*dontResolveAlias*/ true, location);
        var resolvedTypeSymbol = typeSymbol is not null && (typeSymbol.Flags & SymbolFlags.Alias) != 0 ? ResolveAlias(typeSymbol) : typeSymbol;

        // In case the value symbol can't be resolved (e.g. because of missing declarations), use type symbol for reachability check.
        if (valueSymbol is null)
        {
            isTypeOnly = isTypeOnly || typeSymbol is not null && GetTypeOnlyAliasDeclaration(typeSymbol, SymbolFlags.Type) is not null;
        }

        if (resolvedValueSymbol is not null && ReferenceEquals(resolvedValueSymbol, resolvedTypeSymbol))
        {
            var globalPromiseSymbol = GetGlobalPromiseConstructorSymbol(/*reportErrors*/ false);
            if (globalPromiseSymbol is not null && ReferenceEquals(resolvedValueSymbol, globalPromiseSymbol))
            {
                return TypeReferenceSerializationKind.Promise;
            }

            var constructorType = GetTypeOfSymbol(resolvedValueSymbol);
            if (constructorType is not null && IsConstructorType(constructorType))
            {
                return isTypeOnly ? TypeReferenceSerializationKind.TypeWithCallSignature : TypeReferenceSerializationKind.TypeWithConstructSignatureAndValue;
            }
        }

        // We might not be able to resolve type symbol so use unknown type in that case (eg error case)
        if (resolvedTypeSymbol is null)
        {
            return isTypeOnly ? TypeReferenceSerializationKind.ObjectType : TypeReferenceSerializationKind.Unknown;
        }

        var type = GetDeclaredTypeOfSymbol(resolvedTypeSymbol);
        if (IsErrorType(type))
        {
            return isTypeOnly ? TypeReferenceSerializationKind.ObjectType : TypeReferenceSerializationKind.Unknown;
        }
        else if ((type.Flags & TypeFlags.AnyOrUnknown) != 0)
        {
            return TypeReferenceSerializationKind.ObjectType;
        }
        else if (IsTypeAssignableToKind(type, TypeFlags.Void | TypeFlags.Nullable | TypeFlags.Never))
        {
            return TypeReferenceSerializationKind.VoidNullableOrNeverType;
        }
        else if (IsTypeAssignableToKind(type, TypeFlags.BooleanLike))
        {
            return TypeReferenceSerializationKind.BooleanType;
        }
        else if (IsTypeAssignableToKind(type, TypeFlags.NumberLike))
        {
            return TypeReferenceSerializationKind.NumberLikeType;
        }
        else if (IsTypeAssignableToKind(type, TypeFlags.BigIntLike))
        {
            return TypeReferenceSerializationKind.BigIntLikeType;
        }
        else if (IsTypeAssignableToKind(type, TypeFlags.StringLike))
        {
            return TypeReferenceSerializationKind.StringLikeType;
        }
        else if (IsTupleType(type))
        {
            return TypeReferenceSerializationKind.ArrayLikeType;
        }
        else if (IsTypeAssignableToKind(type, TypeFlags.ESSymbolLike))
        {
            return TypeReferenceSerializationKind.ESSymbolType;
        }
        else if (IsFunctionType(type))
        {
            return TypeReferenceSerializationKind.TypeWithCallSignature;
        }
        else if (IsArrayType(type))
        {
            return TypeReferenceSerializationKind.ArrayLikeType;
        }
        else
        {
            return TypeReferenceSerializationKind.ObjectType;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:51180-51188 — <c>createTypeOfDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>HasInferredType</c> (utilities.ts:11569) is a union of declaration node types with no C#
    /// counterpart, so the parameter is <see cref="IDeclaration"/> — the interface the union's members all implement
    /// and the type the generated resolver slots take (as <c>object?</c>). The <c>KeywordTypeNode</c> cast the
    /// reference writes is the port's explicit <c>(ITypeNode)</c> cast of the token.
    /// </remarks>
    internal ITypeNode CreateTypeOfDeclaration(IDeclaration? declarationIn, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
    {
        var declaration = Utilities.GetParseTreeNode((Node?)declarationIn, Utilities.HasInferredType);
        if (declaration is null)
        {
            return (ITypeNode)_factory.CreateToken(SyntaxKind.AnyKeyword);
        }

        // Get type of the symbol if this is the valid symbol otherwise get type at location
        var symbol = GetSymbolOfDeclaration((IDeclaration)declaration);
        return SerializeTypeForDeclaration(declaration, symbol, enclosingDeclaration, flags | NodeBuilderFlags.MultilineObjectLiterals, internalFlags, tracker)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:51190-51204 — <c>getAllAccessorDeclarationsForDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>accessor: AccessorDeclaration</c> is the get/set accessor union, which the port has no type
    /// for (<c>GetAccessorDeclaration</c> and <c>SetAccessorDeclaration</c> are separate classes), so the parameter is
    /// the class they share and the reference's own <c>getParseTreeNode(accessor, isGetOrSetAccessorDeclaration)!</c>
    /// narrows it.
    /// </remarks>
    internal IAllAccessorDeclarations GetAllAccessorDeclarationsForDeclaration(Node accessor)
    {
        accessor = Utilities.GetParseTreeNode(accessor, Utilities.IsGetOrSetAccessorDeclaration)!;
        var otherKind = accessor.Kind == SyntaxKind.SetAccessor ? SyntaxKind.GetAccessor : SyntaxKind.SetAccessor;
        var otherAccessor = Utilities.GetDeclarationOfKind<Node>(GetSymbolOfDeclaration((IDeclaration)accessor), otherKind);
        var firstAccessor = otherAccessor is not null && otherAccessor.Pos < accessor.Pos ? otherAccessor : accessor;
        var secondAccessor = otherAccessor is not null && otherAccessor.Pos < accessor.Pos ? accessor : otherAccessor;
        var setAccessor = accessor.Kind == SyntaxKind.SetAccessor ? (SetAccessorDeclaration)accessor : (SetAccessorDeclaration?)otherAccessor;
        var getAccessor = accessor.Kind == SyntaxKind.GetAccessor ? (GetAccessorDeclaration)accessor : (GetAccessorDeclaration?)otherAccessor;
        return new AllAccessorDeclarations
        {
            FirstAccessor = firstAccessor,
            SecondAccessor = secondAccessor,
            SetAccessor = setAccessor,
            GetAccessor = getAccessor,
        };
    }

    /// <summary>ported from: src/compiler/types.ts:5818-5823 — <c>AllAccessorDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference fills types.ts's interface with an object literal; the generated model carries only
    /// <see cref="IAllAccessorDeclarations"/>, so this private class is the port's constructible implementation, the
    /// same treatment <c>PragmaDefinition</c> got in <c>Core/Pragmas.cs</c> for another interface filled with literals.
    /// It is nested so the name cannot collide with a sibling band's, and it can be folded into the model when the
    /// model slice gives <c>AllAccessorDeclarations</c> a home.
    /// </remarks>
    private sealed class AllAccessorDeclarations : IAllAccessorDeclarations
    {
        public Node? FirstAccessor { get; set; }
        public Node? SecondAccessor { get; set; }
        public GetAccessorDeclaration? GetAccessor { get; set; }
        public SetAccessorDeclaration? SetAccessor { get; set; }
    }

    /// <summary>ported from: src/compiler/checker.ts:51205-51211 — <c>createReturnTypeOfSignatureDeclaration</c>.</summary>
    internal ITypeNode CreateReturnTypeOfSignatureDeclaration(Node? signatureDeclarationIn, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
    {
        var signatureDeclaration = Utilities.GetParseTreeNode(signatureDeclarationIn, Utilities.IsFunctionLike);
        if (signatureDeclaration is null)
        {
            return (ITypeNode)_factory.CreateToken(SyntaxKind.AnyKeyword);
        }

        return SerializeReturnTypeForSignature(signatureDeclaration, enclosingDeclaration, flags | NodeBuilderFlags.MultilineObjectLiterals, internalFlags, tracker)!;
    }

    /// <summary>ported from: src/compiler/checker.ts:51213-51219 — <c>createTypeOfExpression</c>.</summary>
    internal ITypeNode CreateTypeOfExpression(Expression? exprIn, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker)
    {
        var expr = Utilities.GetParseTreeNode<Expression>(exprIn, Utilities.IsExpression);
        if (expr is null)
        {
            return (ITypeNode)_factory.CreateToken(SyntaxKind.AnyKeyword);
        }

        return SerializeTypeForExpression(expr, enclosingDeclaration, flags | NodeBuilderFlags.MultilineObjectLiterals, internalFlags, tracker)!;
    }

    // ---------------------------------------------------------------------------------------------
    // Referenced-value lookups (checker.ts:51221-51316)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51221-51223 — <c>hasGlobalName</c>.</summary>
    internal bool HasGlobalName(string? name)
        => _globals.ContainsKey(Utilities.EscapeLeadingUnderscores(name!));

    /// <summary>ported from: src/compiler/checker.ts:51225-51242 — <c>getReferencedValueSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS compares the reference against <c>parent.name</c> of a <c>Declaration</c>; the port reaches the
    /// same slot through <see cref="INamedDeclaration"/> (types.ts's <c>name</c> lives on <c>NamedDeclaration</c>, and
    /// <c>IDeclaration</c> carries only the symbol slots).
    /// </remarks>
    internal ISymbol? GetReferencedValueSymbol(Identifier reference, bool? startInDeclarationContainer = null)
    {
        var resolvedSymbol = GetNodeLinks(reference).ResolvedSymbol;
        if (resolvedSymbol is not null)
        {
            return resolvedSymbol;
        }

        Node location = reference;
        if (startInDeclarationContainer == true)
        {
            // When resolving the name of a declaration as a value, we need to start resolution
            // at a point outside of the declaration.
            var parent = reference.Parent!;
            if (Utilities.IsDeclaration(parent) && ReferenceEquals(reference, ((INamedDeclaration)parent).Name))
            {
                location = GetDeclarationContainer(parent);
            }
        }

        return ResolveName(location, reference.EscapedText!, SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
    }

    /// <summary>ported from: src/compiler/checker.ts:51251-51265 — <c>getReferencedValueOrAliasSymbol</c>.</summary>
    /// <remarks>
    /// Get either a value-meaning symbol or an alias symbol.
    /// Unlike <c>getReferencedValueSymbol</c>, if the cached resolved symbol is the unknown symbol,
    /// we call <c>resolveName</c> to find a symbol.
    /// This is because when caching the resolved symbol, we only consider value symbols, but here
    /// we want to also get an alias symbol if one exists.
    /// </remarks>
    internal ISymbol? GetReferencedValueOrAliasSymbol(Identifier reference)
    {
        var resolvedSymbol = GetNodeLinks(reference).ResolvedSymbol;
        if (resolvedSymbol is not null && !ReferenceEquals(resolvedSymbol, _unknownSymbol))
        {
            return resolvedSymbol;
        }

        return ResolveName(
            reference,
            reference.EscapedText!,
            SymbolFlags.Value | SymbolFlags.ExportValue | SymbolFlags.Alias,
            /*nameNotFoundMessage*/ null,
            /*isUse*/ true,
            /*excludeGlobals*/ false);
    }

    /// <summary>ported from: src/compiler/checker.ts:51267-51279 — <c>getReferencedValueDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isGeneratedIdentifier</c> reads <c>node?.flags</c> and so accepts <c>undefined</c>; the port's
    /// <c>Utilities.IsGeneratedIdentifier</c> takes a node, so the absent-reference case is the explicit null check,
    /// which lands on the same result the reference's fall-through produces.
    /// </remarks>
    internal IDeclaration? GetReferencedValueDeclaration(Identifier? referenceIn)
    {
        if (referenceIn is not null && !Utilities.IsGeneratedIdentifier(referenceIn))
        {
            var reference = Utilities.GetParseTreeNode<Identifier>(referenceIn, NodeTests.IsIdentifier);
            if (reference is not null)
            {
                var symbol = GetReferencedValueSymbol(reference);
                if (symbol is not null)
                {
                    return GetExportSymbolOfValueSymbolIfExported(symbol).ValueDeclaration;
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:51281-51316 — <c>getReferencedValueDeclarations</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="GetReferencedValueDeclaration"/> for the generated-identifier guard.</remarks>
    internal List<IDeclaration>? GetReferencedValueDeclarations(Identifier? referenceIn)
    {
        if (referenceIn is not null && !Utilities.IsGeneratedIdentifier(referenceIn))
        {
            var reference = Utilities.GetParseTreeNode<Identifier>(referenceIn, NodeTests.IsIdentifier);
            if (reference is not null)
            {
                var symbol = GetReferencedValueSymbol(reference);
                if (symbol is not null)
                {
                    return (List<IDeclaration>?)Core.Filter(GetExportSymbolOfValueSymbolIfExported(symbol).Declarations, declaration =>
                    {
                        switch (declaration.Kind)
                        {
                            case SyntaxKind.VariableDeclaration:
                            case SyntaxKind.Parameter:
                            case SyntaxKind.BindingElement:
                            case SyntaxKind.PropertyDeclaration:
                            case SyntaxKind.PropertyAssignment:
                            case SyntaxKind.ShorthandPropertyAssignment:
                            case SyntaxKind.EnumMember:
                            case SyntaxKind.ObjectLiteralExpression:
                            case SyntaxKind.FunctionDeclaration:
                            case SyntaxKind.FunctionExpression:
                            case SyntaxKind.ArrowFunction:
                            case SyntaxKind.ClassDeclaration:
                            case SyntaxKind.ClassExpression:
                            case SyntaxKind.EnumDeclaration:
                            case SyntaxKind.MethodDeclaration:
                            case SyntaxKind.GetAccessor:
                            case SyntaxKind.SetAccessor:
                            case SyntaxKind.ModuleDeclaration:
                                return true;
                        }

                        return false;
                    });
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Literal const values and the JSX entity names (checker.ts:51318-51378)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:51318-51323 — <c>isLiteralConstDeclaration</c>.</summary>
    internal bool IsLiteralConstDeclaration(Node? node)
    {
        if (Utilities.IsDeclarationReadonly((IDeclaration)node!) || NodeTests.IsVariableDeclaration(node!) && IsVarConstLike(node!))
        {
            return IsFreshLiteralType(GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)node!)));
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:51325-51334 — <c>literalTypeToNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated model widens <c>LiteralType.value</c> to <c>object?</c> (types.ts:6483), so the
    /// reference's <c>typeof literalValue === "object" | "string"</c> dispatch becomes a pattern test for
    /// <see cref="IPseudoBigInt"/> and for <c>string</c>, with the remaining case the number the flags say it is
    /// (Model/LiteralType.cs). The number is handed to the factory as its text through
    /// <see cref="Js.NumberToString"/> because the port's <c>CreateNumericLiteral</c>
    /// (Parser/NodeFactory.cs:130) accepts <c>string | int</c>, not a <c>double</c>, and TS's own
    /// <c>createNumericLiteral</c> takes the same <c>string | number</c>; the bigint's text is
    /// <c>pseudoBigIntToString(value) + "n"</c> (factory/nodeFactory.ts:1248), which the port's
    /// <c>CreateBigIntLiteral</c> takes already spelled (Parser/NodeFactory.cs:154).
    /// </remarks>
    internal Expression LiteralTypeToNode(IFreshableType type, Node enclosing, ISymbolTracker? tracker)
    {
        Expression? enumResult = null;
        if ((type.Flags & TypeFlags.EnumLike) != 0)
        {
            enumResult = SymbolToExpression(type.Symbol, SymbolFlags.Value, enclosing, /*flags*/ null, /*internalFlags*/ null, tracker);
        }
        else if (ReferenceEquals(type, _trueType))
        {
            enumResult = _factory.CreateTrue();
        }
        else if (ReferenceEquals(type, _falseType))
        {
            enumResult = _factory.CreateFalse();
        }

        if (enumResult is not null)
        {
            return enumResult;
        }

        var literalValue = ((ILiteralType)type).Value;
        if (literalValue is IPseudoBigInt pseudoBigInt)
        {
            return _factory.CreateBigIntLiteral(Utilities.PseudoBigIntToString(pseudoBigInt) + "n");
        }

        if (literalValue is string text)
        {
            return _factory.CreateStringLiteral(text, null, null);
        }

        var number = (double)literalValue!;
        return number < 0
            ? _factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, _factory.CreateNumericLiteral(Js.NumberToString(-number), null))
            : _factory.CreateNumericLiteral(Js.NumberToString(number), null);
    }

    /// <summary>ported from: src/compiler/checker.ts:51336-51339 — <c>createLiteralConstValue</c>.</summary>
    internal Expression CreateLiteralConstValue(Node? node, ISymbolTracker? tracker)
    {
        var type = GetTypeOfSymbol(GetSymbolOfDeclaration((IDeclaration)node!));
        return LiteralTypeToNode((IFreshableType)type, node!, tracker);
    }

    /// <summary>ported from: src/compiler/checker.ts:51341-51343 — <c>getJsxFactoryEntity</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comma expression discards the <c>getJsxNamespace(location)</c> call for its side
    /// effect; C# has no comma operator, so the call is its own statement.
    /// </remarks>
    internal Node? GetJsxFactoryEntity(Node? location)
    {
        if (location is null)
        {
            return _jsxFactoryEntity;
        }

        GetJsxNamespace(location);
        return Utilities.GetSourceFileOfNode(location)!.LocalJsxFactory ?? _jsxFactoryEntity;
    }

    /// <summary>ported from: src/compiler/checker.ts:51345-51364 — <c>getJsxFragmentFactoryEntity</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the pragma map's values stay <c>object?</c> because types.ts's <c>PragmaPseudoMap</c> is a mapped
    /// type with no C# counterpart (Core/Pragmas.cs), so the reference's <c>isArray</c> test and <c>[0]</c> index become
    /// a test for the <c>List&lt;PragmaPseudoMapEntry&gt;</c> the pragma processor stores for a repeated pragma
    /// (Parser/Parser.Entry.cs:456-462), and the entry's <c>arguments.factory</c> is the dictionary slot
    /// <c>PragmaPseudoMapEntry.Arguments</c>. That class is declared at Parser/Parser.Entry.cs:960 with the
    /// <c>Parser</c> class body still open, so it is nested — hence the <c>Parser.</c> qualifier. The argument's
    /// <c>object?</c> value is handed to the parser as a <c>string</c> with an empty fallback for the absent case, which
    /// the isolated parse then rejects the same way the reference's <c>undefined</c> does. The checker's
    /// <c>languageVersion</c> (checker.ts:1529) is <see cref="_languageVersion"/>.
    /// </remarks>
    internal Node? GetJsxFragmentFactoryEntity(Node? location)
    {
        if (location is not null)
        {
            var file = Utilities.GetSourceFileOfNode(location);
            if (file is not null)
            {
                if (file.LocalJsxFragmentFactory is not null)
                {
                    return file.LocalJsxFragmentFactory;
                }

                var jsxFragPragmas = file.Pragmas?.Get("jsxfrag");
                var jsxFragPragma = jsxFragPragmas is List<Parser.PragmaPseudoMapEntry> list
                    ? (list.Count > 0 ? list[0] : null)
                    : jsxFragPragmas as Parser.PragmaPseudoMapEntry;
                if (jsxFragPragma is not null)
                {
                    file.LocalJsxFragmentFactory = Parser.ParseIsolatedEntityName((string?)jsxFragPragma.Arguments["factory"] ?? "", _languageVersion);
                    return file.LocalJsxFragmentFactory;
                }
            }
        }

        if (Js.IsTruthy(_compilerOptions.JsxFragmentFactory))
        {
            return Parser.ParseIsolatedEntityName(_compilerOptions.JsxFragmentFactory!, _languageVersion);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:51366-51378 — <c>getNonlocalEffectiveTypeAnnotationNode</c>.</summary>
    internal ITypeNode? GetNonlocalEffectiveTypeAnnotationNode(Node node)
    {
        var direct = Utilities.GetEffectiveTypeAnnotationNode(node);
        if (direct is not null)
        {
            return direct;
        }

        if (node.Kind == SyntaxKind.Parameter && node.Parent!.Kind == SyntaxKind.SetAccessor)
        {
            var other = GetAllAccessorDeclarationsForDeclaration(node.Parent!).GetAccessor;
            if (other is not null)
            {
                return Utilities.GetEffectiveReturnTypeNode(other);
            }
        }

        return null;
    }
}
