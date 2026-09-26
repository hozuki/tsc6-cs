namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts (the members the checker slices call that no earlier slice landed)
//    997-999    isPlainJsFile
//  2677-2679    isVarConst
//  2724-2726    isLiteralImportTypeNode
//  2788-2882    isPartOfTypeNode
//  2884-2888    isPartOfTypeExpressionWithTypeArguments
//  3014-3017    isVariableDeclarationInVariableStatement
//  3027-3030    isCommonJsExportPropertyAssignment
//  3033-3037    isValidESSymbolDeclaration
//  3068-3070    isFunctionBlock
//  3127-3129    getContainingFunction
//  3142-3149    getContainingClassStaticBlock
//  3824-3830    isJSDocIndexSignature
//  4041-4051    getNameOfExpando
//  4487-4489    isTypeAlias
//  4900-4918    getAssignmentTargetKind
//  5001-5006    walkUp (file-local upstream)
//  5009-5011    walkUpParenthesizedTypes
//  5035-5038    skipTypeParentheses
//  5054-5060    isDeleteTarget
//  5063-5069    isNodeDescendantOf
//  5397-5424    getPropertyNameForPropertyNameNode
//  5454-5456    isKnownSymbol
//  7190-7192    hasEffectiveModifier
//  7231-7233    hasEffectiveReadonlyModifier
//  7444-7447    tryGetClassExtendingExpressionWithTypeArguments
//  7455-7468    tryGetClassImplementingOrExtendingExpressionWithTypeArguments
//  7495-7497    isExpressionWithTypeArgumentsInClassExtendsClause
//  7505-7522    getFirstIdentifier
//  10529-10531  pseudoBigIntToString
//  10910-10912  isFunctionExpressionOrArrowFunction
//  10920-10943  isNumericLiteralName
//  11025-11045  isTypeDeclaration
//  11162-11164  isTypeUsableAsPropertyName
//  11170-11178  getPropertyNameFromType
// ported from: src/compiler/utilitiesPublic.ts (the same, for the module's public half)
//  1170-1172    getJSDocDeprecatedTag
//  1426-1429    isConstTypeReference
//  1600-1602    isPrivateIdentifierClassElementDeclaration
//  1968-1983    isCallLikeExpression
//  1985-1987    isCallOrNewExpression
//  2108-2112    isAssertionExpression
//  2419-2424    isStatement
//  2426-2434    isBlockStatement (file-local upstream)
//  2597-2599    isTypeReferenceType
/// <summary>
/// The <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members the checker slices call that no earlier slice landed.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: these are declared here rather than in the file named after their source file because this is the slice
/// that needs them: the checker reaches them through <c>Utilities.*</c>, and the two the landed checker slices already
/// state as <c>Checker</c> members (<c>IsPartOfTypeNode</c> and its two helpers, <c>Checker.Support.cs</c>) are the
/// transitional copies these replace at cutover. Each member carries its own <c>ported from</c> line, so moving a group
/// to <c>Utilities/</c> when the M5 slice lands is mechanical — the same treatment
/// <c>Program/Utilities.ModuleResolution.cs</c> documents for the program slice.
/// </para>
/// <para>
/// PORT-NOTE: a member whose upstream body needs another member of this file has no extra dependency: the twelve
/// helpers this file adds beside the task's list (<c>IsVarConst</c>, <c>HasEffectiveModifier</c>,
/// <c>HasEffectiveReadonlyModifier</c>, <c>IsVariableDeclarationInVariableStatement</c>, <c>IsFunctionBlock</c>,
/// <c>IsBlockStatement</c>, <c>WalkUp</c>, <c>IsPartOfTypeExpressionWithTypeArguments</c>,
/// <c>TryGetClassExtendingExpressionWithTypeArguments</c>,
/// <c>TryGetClassImplementingOrExtendingExpressionWithTypeArguments</c>,
/// <c>IsExpressionWithTypeArgumentsInClassExtendsClause</c>) are all <c>utilities.ts</c>/<c>utilitiesPublic.ts</c>
/// members too, so they are ported in place rather than called by a name nobody declares. Everything else the bodies
/// call already exists elsewhere in the port and is named in the member's own remarks.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // JS files, expando declarations, literal import types (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:997-999 — <c>isPlainJsFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!!file</c> narrows for the two slot reads; the port's <c>file is not null</c> is the same
    /// guard. TS's <c>checkJs === undefined</c> is the port's <c>checkJs is null</c> — the generated
    /// <c>CompilerOptions.checkJs</c> slot is <c>bool?</c>, so an absent option is the same question.
    /// </remarks>
    public static bool IsPlainJsFile(SourceFile? file, bool? checkJs)
        => file is not null
            && file.ScriptKind is ScriptKind.JS or ScriptKind.JSX
            && file.CheckJsDirective is null
            && checkJs is null;

    /// <summary>ported from: src/compiler/utilities.ts:2677-2679 — <c>isVarConst</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>VariableDeclaration | VariableDeclarationList</c> union; the port takes the
    /// upper bound both arms share, because <c>getCombinedNodeFlags</c> reads any node and the union has no C# name.
    /// </remarks>
    public static bool IsVarConst(Node node)
        => (GetCombinedNodeFlags(node) & NodeFlags.BlockScoped) == NodeFlags.Const;

    /// <summary>ported from: src/compiler/utilities.ts:2724-2726 — <c>isLiteralImportTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows <c>n</c> to <c>LiteralImportTypeNode</c>; C# cannot narrow through a predicate, so
    /// the port answers <c>bool</c> and reads the argument through the casts the guard licenses — the model types
    /// <c>ImportTypeNode.argument</c> as the interface <see cref="ITypeNode"/> and
    /// <c>LiteralTypeNode.literal</c> as the interface <see cref="UpdateExpression"/>.
    /// </remarks>
    public static bool IsLiteralImportTypeNode(Node n)
        => NodeTests.IsImportTypeNode(n)
            && ((ImportTypeNode)n).Argument is { } argument
            && NodeTests.IsLiteralTypeNode((Node)argument)
            && NodeTests.IsStringLiteral((Node)((LiteralTypeNode)argument).Literal!);

    // ---------------------------------------------------------------------------------------------
    // Type-node structure and the declarations named by types (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2788-2882 — <c>isPartOfTypeNode</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference's identifier arm falls through to the qualified-name arm; the port's <c>goto case</c> is
    /// that fall-through, with the reference's <c>Debug.assert</c> kept.
    /// </para>
    /// <para>
    /// PORT-NOTE: the three structural unions the arms read have no C# name, so each is selected by the kind already
    /// matched: <c>HasType</c> is the pattern switch over the four declarations, <c>FunctionLikeDeclaration</c> and
    /// <c>SignatureDeclaration</c> are both <see cref="ISignatureDeclarationBase"/>, and
    /// <c>CallExpression | TaggedTemplateExpression</c> is the type-argument switch. The reference's
    /// <c>contains(…, node)</c> is identity containment over the type-argument list (<see cref="Core.Some{T}"/> with
    /// <see cref="ReferenceEquals"/>), not a value search: the list's element type is the interface
    /// <see cref="ITypeNode"/> and the node being tested is not one in this arm.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>isPartOfTypeExpressionWithTypeArguments</c> and the class-extends test it needs are file-local
    /// helpers upstream (utilities.ts:2884, :7495); both are ported below (<c>Checker.Support.cs</c> states its own
    /// transitional copies of them beside its copy of this member).
    /// </para>
    /// </remarks>
    public static bool IsPartOfTypeNode(Node node)
    {
        if (SyntaxKind.FirstTypeNode <= node.Kind && node.Kind <= SyntaxKind.LastTypeNode)
        {
            return true;
        }

        switch (node.Kind)
        {
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.BigIntKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.ObjectKeyword:
            case SyntaxKind.UndefinedKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.NeverKeyword:
                return true;
            case SyntaxKind.VoidKeyword:
                return node.Parent!.Kind != SyntaxKind.VoidExpression;
            case SyntaxKind.ExpressionWithTypeArguments:
                return IsPartOfTypeExpressionWithTypeArguments(node);
            case SyntaxKind.TypeParameter:
                return node.Parent!.Kind == SyntaxKind.MappedType || node.Parent.Kind == SyntaxKind.InferType;

            // Identifiers and qualified names may be type nodes, depending on their context. Climb
            // above them to find the lowest container
            case SyntaxKind.Identifier:
                // If the identifier is the RHS of a qualified name, then it's a type iff its parent is.
                if (node.Parent!.Kind == SyntaxKind.QualifiedName && ReferenceEquals(((QualifiedName)node.Parent).Right, node))
                {
                    node = node.Parent!;
                }
                else if (node.Parent.Kind == SyntaxKind.PropertyAccessExpression && ReferenceEquals(((PropertyAccessExpression)node.Parent).Name, node))
                {
                    node = node.Parent!;
                }

                // At this point, node is either a qualified name or an identifier
                Debug.Assert(
                    node.Kind == SyntaxKind.Identifier || node.Kind == SyntaxKind.QualifiedName || node.Kind == SyntaxKind.PropertyAccessExpression,
                    "'node' was expected to be a qualified name, identifier or property access in 'isPartOfTypeNode'.");
                goto case SyntaxKind.QualifiedName;

            case SyntaxKind.QualifiedName:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ThisKeyword:
                {
                    var parent = node.Parent!;
                    if (parent.Kind == SyntaxKind.TypeQuery)
                    {
                        return false;
                    }

                    if (parent.Kind == SyntaxKind.ImportType)
                    {
                        return !((ImportTypeNode)parent).IsTypeOf;
                    }

                    // Do not recursively call isPartOfTypeNode on the parent. In the example:
                    //
                    //     let a: A.B.C;
                    //
                    // Calling isPartOfTypeNode would consider the qualified name A.B a type node.
                    // Only C and A.B.C are type nodes.
                    if (SyntaxKind.FirstTypeNode <= parent.Kind && parent.Kind <= SyntaxKind.LastTypeNode)
                    {
                        return true;
                    }

                    switch (parent.Kind)
                    {
                        case SyntaxKind.ExpressionWithTypeArguments:
                            return IsPartOfTypeExpressionWithTypeArguments(parent);
                        case SyntaxKind.TypeParameter:
                            return ReferenceEquals(node, ((TypeParameterDeclaration)parent).Constraint);
                        case SyntaxKind.JSDocTemplateTag:
                            return ReferenceEquals(node, ((JSDocTemplateTag)parent).Constraint);
                        case SyntaxKind.PropertyDeclaration:
                        case SyntaxKind.PropertySignature:
                        case SyntaxKind.Parameter:
                        case SyntaxKind.VariableDeclaration:
                            // TS's `node === (parent as HasType).type`; the union has no C# name, so the port
                            // selects the face by the kind already matched.
                            return parent switch
                            {
                                PropertyDeclaration propertyDeclaration => ReferenceEquals(node, propertyDeclaration.Type),
                                PropertySignature propertySignature => ReferenceEquals(node, propertySignature.Type),
                                ParameterDeclaration parameterDeclaration => ReferenceEquals(node, parameterDeclaration.Type),
                                VariableDeclaration variableDeclaration => ReferenceEquals(node, variableDeclaration.Type),
                                _ => false,
                            };
                        case SyntaxKind.FunctionDeclaration:
                        case SyntaxKind.FunctionExpression:
                        case SyntaxKind.ArrowFunction:
                        case SyntaxKind.Constructor:
                        case SyntaxKind.MethodDeclaration:
                        case SyntaxKind.MethodSignature:
                        case SyntaxKind.GetAccessor:
                        case SyntaxKind.SetAccessor:
                            return ReferenceEquals(node, ((ISignatureDeclarationBase)parent).Type);
                        case SyntaxKind.CallSignature:
                        case SyntaxKind.ConstructSignature:
                        case SyntaxKind.IndexSignature:
                            return ReferenceEquals(node, ((ISignatureDeclarationBase)parent).Type);
                        case SyntaxKind.TypeAssertionExpression:
                            return ReferenceEquals(node, ((TypeAssertion)parent).Type);
                        case SyntaxKind.CallExpression:
                        case SyntaxKind.NewExpression:
                        case SyntaxKind.TaggedTemplateExpression:
                            var typeArguments = parent switch
                            {
                                CallExpression callExpression => callExpression.TypeArguments,
                                NewExpression newExpression => newExpression.TypeArguments,
                                TaggedTemplateExpression taggedTemplateExpression => taggedTemplateExpression.TypeArguments,
                                _ => null,
                            };
                            return typeArguments is not null && Core.Some(typeArguments, t => ReferenceEquals(t, node));
                    }

                    break;
                }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2884-2888 — <c>isPartOfTypeExpressionWithTypeArguments</c>.</summary>
    private static bool IsPartOfTypeExpressionWithTypeArguments(Node node)
        => NodeTests.IsJSDocImplementsTag(node.Parent!)
            || NodeTests.IsJSDocAugmentsTag(node.Parent!)
            || (NodeTests.IsHeritageClause(node.Parent!) && !IsExpressionWithTypeArgumentsInClassExtendsClause(node));

    /// <summary>ported from: src/compiler/utilities.ts:3014-3017 — <c>isVariableDeclarationInVariableStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.parent.kind</c> and then <c>node.parent.parent.kind</c>; the two steps are the same
    /// two steps here, stated as one nested property pattern so the nullable <c>Parent</c> slots cannot be read through.
    /// </remarks>
    public static bool IsVariableDeclarationInVariableStatement(VariableDeclaration node)
        => node.Parent is { Kind: SyntaxKind.VariableDeclarationList, Parent: { Kind: SyntaxKind.VariableStatement } };

    /// <summary>ported from: src/compiler/utilities.ts:3027-3030 — <c>isCommonJsExportPropertyAssignment</c>.</summary>
    public static bool IsCommonJsExportPropertyAssignment(Node node)
    {
        if (!IsInJSFile(node))
        {
            return false;
        }
        return NodeTests.IsBinaryExpression(node)
            && GetAssignmentDeclarationKind(node) == AssignmentDeclarationKind.ExportsProperty;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3033-3037 — <c>isValidESSymbolDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.name</c> read follows <c>isVariableDeclaration</c>'s narrowing; the port casts to
    /// <see cref="VariableDeclaration"/> for the slot, which the model types <c>Node?</c>, and the <c>isIdentifier</c>
    /// test is the reference's test with that <c>!</c>. The nested ternaries keep the reference's order of evaluation.
    /// </remarks>
    public static bool IsValidESSymbolDeclaration(Node node)
        => (NodeTests.IsVariableDeclaration(node)
                ? IsVarConst(node)
                    && NodeTests.IsIdentifier(((VariableDeclaration)node).Name!)
                    && IsVariableDeclarationInVariableStatement((VariableDeclaration)node)
                : NodeTests.IsPropertyDeclaration(node)
                    ? HasEffectiveReadonlyModifier(node) && HasStaticModifier(node)
                    : NodeTests.IsPropertySignature(node) && HasEffectiveReadonlyModifier(node))
            || IsCommonJsExportPropertyAssignment(node);

    /// <summary>ported from: src/compiler/utilities.ts:3068-3070 — <c>isFunctionBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's leading <c>node &amp;&amp;</c> guards a parameter its own type says is not optional; the port's
    /// parameter is non-nullable, and <see cref="IsFunctionLike"/> keeps its own null guard for the parent.
    /// </remarks>
    public static bool IsFunctionBlock(Node node)
        => node.Kind == SyntaxKind.Block && IsFunctionLike(node.Parent);

    /// <summary>ported from: src/compiler/utilities.ts:3127-3129 — <c>getContainingFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard answers the <c>SignatureDeclaration | undefined</c> union; the port answers <c>Node?</c>,
    /// which is all the checker's call site reads. <c>findAncestor</c> is the port's <see cref="FindAncestor(Node?, Func{Node, bool})"/>
    /// and <c>isFunctionLike</c> the port's <see cref="IsFunctionLike"/>.
    /// </remarks>
    public static Node? GetContainingFunction(Node node) => FindAncestor(node.Parent, IsFunctionLike);

    /// <summary>ported from: src/compiler/utilities.ts:3142-3149 — <c>getContainingClassStaticBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's visitor answers <c>"quit"</c> for a class or function boundary; the port's quit-capable overload
    /// <see cref="FindAncestor(Node?, Func{Node, bool?})"/> spells that as <c>null</c>, which is why the lambda's first
    /// arm is cast.
    /// </remarks>
    public static Node? GetContainingClassStaticBlock(Node node)
        => FindAncestor(node.Parent, n => IsClassLike(n) || IsFunctionLike(n) ? (bool?)null : NodeTests.IsClassStaticBlockDeclaration(n));

    /// <summary>ported from: src/compiler/utilities.ts:3824-3830 — <c>isJSDocIndexSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>TypeReferenceNode | ExpressionWithTypeArguments</c> union and its
    /// <c>node.typeArguments &amp;&amp; …</c> chain answers <c>boolean | undefined</c>; the port takes the upper bound
    /// <see cref="Node"/> (the kind test is the first thing the body does anyway) and answers <c>bool</c>, which is what
    /// the checker's condition reads.
    /// </remarks>
    public static bool IsJSDocIndexSignature(Node node)
    {
        if (!NodeTests.IsTypeReferenceNode(node))
        {
            return false;
        }

        var typeReference = (TypeReferenceNode)node;
        return typeReference.TypeName is Identifier typeName
            && typeName.EscapedText == "Object"
            && typeReference.TypeArguments is not null
            && typeReference.TypeArguments.Count == 2
            && typeReference.TypeArguments[0].Kind is SyntaxKind.StringKeyword or SyntaxKind.NumberKeyword;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4041-4051 — <c>getNameOfExpando</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Declaration</c> and its result the <c>DeclarationName | undefined</c> union; the
    /// port takes the upper bound <see cref="Node"/> both callers hold and answers <c>Node?</c>, which is what the
    /// caller casts. TS reads <c>node.parent</c> as a binary expression and destructures it again for the
    /// <c>||=</c>/<c>??=</c> case; the port's property patterns state those same reads, and a null-conditional step
    /// stands in for the reference's assumption that a parent's parent exists.
    /// </remarks>
    public static Node? GetNameOfExpando(Node node)
    {
        if (node.Parent is BinaryExpression binary)
        {
            var operatorToken = binary.OperatorToken!.Kind;
            if ((operatorToken is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken) && binary.Parent is BinaryExpression defaultedAssignment)
            {
                binary = defaultedAssignment;
            }

            if (binary.OperatorToken!.Kind == SyntaxKind.EqualsToken && binary.Left is Identifier left)
            {
                return left;
            }
        }
        else if (node.Parent is VariableDeclaration variableDeclaration)
        {
            return variableDeclaration.Name;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4487-4489 — <c>isTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the guard's other half, <c>isJSDocTypeAlias</c> (utilities.ts:4482-4484), is already the port's
    /// <see cref="IsJSDocTypeAlias"/>.
    /// </remarks>
    public static bool IsTypeAlias(Node node) => IsJSDocTypeAlias(node) || NodeTests.IsTypeAliasDeclaration(node);

    // ---------------------------------------------------------------------------------------------
    // Assignment targets (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:4900-4918 — <c>getAssignmentTargetKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getAssignmentTarget</c> is the private member <c>Utilities.Binder.Nodes.cs</c> states
    /// (utilities.ts:4854-4897); its result is the four-kind <c>AssignmentTarget</c> union, which the port answers as
    /// <c>Node?</c>, so each arm casts as it did upstream.
    /// <para>
    /// PORT-NOTE: the reference's switch has no default arm — its four arms cover everything
    /// <c>getAssignmentTarget</c> can return, so the implicit <c>undefined</c> for any other target is unreachable. C#
    /// requires a value on every path and answers <see cref="AssignmentKind.None"/>, which reads the same at both call
    /// sites (each tests for <c>AssignmentKind.Definite</c>/<c>AssignmentKind.Compound</c>).
    /// </para>
    /// </remarks>
    public static AssignmentKind GetAssignmentTargetKind(Node node)
    {
        var target = GetAssignmentTarget(node);
        if (target is null)
        {
            return AssignmentKind.None;
        }

        switch (target.Kind)
        {
            case SyntaxKind.BinaryExpression:
                var binaryOperator = ((BinaryExpression)target).OperatorToken!.Kind;
                return binaryOperator == SyntaxKind.EqualsToken || IsLogicalOrCoalescingAssignmentOperator(binaryOperator)
                    ? AssignmentKind.Definite
                    : AssignmentKind.Compound;
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return AssignmentKind.Compound;
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                return AssignmentKind.Definite;
            default:
                return AssignmentKind.None;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The parenthesis walks (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:5001-5006 — <c>walkUp</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>while (node &amp;&amp; node.kind === kind)</c> ends the walk at a node whose <c>parent</c> is
    /// undefined — the loop test fails on that falsy value and the function answers it. The port's parameter is
    /// nullable precisely so that this walk can answer <c>null</c>, which <see cref="IsDeleteTarget"/> reads.
    /// </remarks>
    private static Node? WalkUp(Node? node, SyntaxKind kind)
    {
        while (node is not null && node.Kind == kind)
        {
            node = node.Parent;
        }
        return node;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5009-5011 — <c>walkUpParenthesizedTypes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's declared result is <c>Node</c>, though its <c>walkUp</c> can answer the undefined above; the
    /// port's <c>!</c> keeps the reference's declared type, which is the one the call site
    /// (<c>getESSymbolLikeTypeForNode</c>) reads.
    /// </remarks>
    public static Node WalkUpParenthesizedTypes(Node node) => WalkUp(node, SyntaxKind.ParenthesizedType)!;

    /// <summary>ported from: src/compiler/utilities.ts:5035-5038 — <c>skipTypeParentheses</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's loop test is the kind test <c>isParenthesizedTypeNode</c>; the port states it as the class
    /// pattern, which is the same test and reads the <c>type</c> slot it needs in the same step. The parameter and the
    /// result are the generated <see cref="ITypeNode"/> — the model's face for <c>TypeNode</c> — because the caller
    /// hands a conditional type's <c>checkType</c>/<c>extendsType</c> and feeds the result to a tuple-type test.
    /// </remarks>
    public static ITypeNode SkipTypeParentheses(ITypeNode node)
    {
        while (node is ParenthesizedTypeNode parenthesizedType)
        {
            node = parenthesizedType.Type!;
        }
        return node;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5054-5060 — <c>isDeleteTarget</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is non-nullable; the port takes <c>Node?</c> because the element-access slice holds a
    /// nullable expression, and a node that is not there is not a delete target — the same treatment
    /// <c>IsNodeDescendantOf</c>'s ancestor gets below.
    /// <para>
    /// PORT-NOTE: TS walks parenthesized <em>expressions</em> here (<c>walkUpParenthesizedExpressions</c>,
    /// utilities.ts:5014-5016, whose body is one <c>walkUp</c> call); the port states that call against the same private
    /// <see cref="WalkUp"/> the type walk above uses.
    /// </para>
    /// </remarks>
    public static bool IsDeleteTarget(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        // A node is delete target iff. it is PropertyAccessExpression/ElementAccessExpression with parentheses skipped
        if (node.Kind is not (SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression))
        {
            return false;
        }

        node = WalkUp(node.Parent, SyntaxKind.ParenthesizedExpression);
        return node is not null && node.Kind == SyntaxKind.DeleteExpression;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5063-5069 — <c>isNodeDescendantOf</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS walks the <c>node</c> parameter itself, its <c>while (node)</c> ending the walk where a node's
    /// <c>parent</c> is undefined; a non-nullable parameter cannot hold that value, so the walk uses a local. The
    /// reference's <c>node === ancestor</c> is identity, which the port states as <see cref="ReferenceEquals"/>.
    /// </remarks>
    public static bool IsNodeDescendantOf(Node node, Node? ancestor)
    {
        Node? current = node;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Property names and symbols (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:5397-5424 — <c>getPropertyNameForPropertyNameNode</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's parameter is the <c>PropertyName | JsxAttributeName</c> union; the port takes the upper bound
    /// <see cref="Node"/> and switches on the kind, which is that union's own discriminator. The
    /// <c>Identifier</c>/<c>PrivateIdentifier</c> arms are two unrelated classes in the model, so each selects the face
    /// that declares <c>escapedText</c>, and the four literal kinds share <see cref="LiteralExpression"/> for the
    /// <c>text</c> read.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>escapeLeadingUnderscores(name.text)</c> reads a non-optional <c>text</c>; the model's
    /// <see cref="LiteralExpression.Text"/> is <c>string?</c>, so an absent text contributes an empty name rather than
    /// the crash the reference's own type rules out. The computed-name arm's two predicates are the port's existing
    /// <see cref="IsStringOrNumericLiteralLike"/> and <see cref="IsSignedNumericLiteral"/>, and the <c>tokenToString</c>
    /// is <c>ScannerUtilities.TokenToString</c> (the same call the binder makes for a signed numeric literal).
    /// </para>
    /// </remarks>
    public static string? GetPropertyNameForPropertyNameNode(Node name)
    {
        switch (name.Kind)
        {
            case SyntaxKind.Identifier:
                return ((Identifier)name).EscapedText;
            case SyntaxKind.PrivateIdentifier:
                return ((PrivateIdentifier)name).EscapedText;
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
                return EscapeLeadingUnderscores(((LiteralExpression)name).Text ?? "");
            case SyntaxKind.ComputedPropertyName:
                {
                    var nameExpression = ((ComputedPropertyName)name).Expression!;
                    if (IsStringOrNumericLiteralLike(nameExpression))
                    {
                        return EscapeLeadingUnderscores(((LiteralExpression)nameExpression).Text ?? "");
                    }
                    else if (IsSignedNumericLiteral(nameExpression))
                    {
                        var prefixUnary = (PrefixUnaryExpression)nameExpression;
                        if (prefixUnary.Operator == SyntaxKind.MinusToken)
                        {
                            return ScannerUtilities.TokenToString(prefixUnary.Operator) + ((NumericLiteral)prefixUnary.Operand!).Text;
                        }
                        return ((NumericLiteral)prefixUnary.Operand!).Text;
                    }
                    return null;
                }
            case SyntaxKind.JsxNamespacedName:
                return GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)name);
            default:
                Debug.AssertNever(name);
                return null;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:5454-5456 — <c>isKnownSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>startsWith(symbol.escapedName as string, "__@")</c> casts a branded <c>__String</c> that is
    /// always present; the model's <see cref="ISymbol.EscapedName"/> is <c>string?</c>, and an absent escaped name is
    /// not the <c>"__@"</c>-prefixed name of an internal symbol (the same reading <c>symbolName</c> states for the same
    /// slot).
    /// </remarks>
    public static bool IsKnownSymbol(ISymbol symbol) => Core.StartsWith(symbol.EscapedName ?? "", "__@");

    // ---------------------------------------------------------------------------------------------
    // Effective modifiers (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7190-7192 — <c>hasEffectiveModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!!getSelectedEffectiveModifierFlags(node, flags)</c> is the effective (JSDoc-inclusive)
    /// reader utilities.ts:7276-7278 and its selector, whose family is still unported; the port asks the same question
    /// of <see cref="GetSyntacticModifierFlags"/>, which is the substitution <c>Utilities.Binder.Modifiers.cs</c>
    /// already documents for <c>getCombinedModifierFlags</c> — the two differ only in the modifiers that come from JSDoc
    /// comments on a JS file's declarations.
    /// </remarks>
    public static bool HasEffectiveModifier(Node node, ModifierFlags flags)
        => (GetSyntacticModifierFlags(node) & flags) != ModifierFlags.None;

    /// <summary>ported from: src/compiler/utilities.ts:7231-7233 — <c>hasEffectiveReadonlyModifier</c>.</summary>
    public static bool HasEffectiveReadonlyModifier(Node node) => HasEffectiveModifier(node, ModifierFlags.Readonly);

    // ---------------------------------------------------------------------------------------------
    // Heritage clauses and entity names (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7444-7447 — <c>tryGetClassExtendingExpressionWithTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>ClassLikeDeclaration | undefined</c>; the port answers <c>Node?</c>, the face its only
    /// reader compares against null, and selects the class from the pair below.
    /// </remarks>
    public static Node? TryGetClassExtendingExpressionWithTypeArguments(Node node)
    {
        var cls = TryGetClassImplementingOrExtendingExpressionWithTypeArguments(node);
        if (cls is null)
        {
            return null;
        }

        var (classDeclaration, isImplements) = cls.Value;
        return isImplements ? null : classDeclaration;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7455-7468 — <c>tryGetClassImplementingOrExtendingExpressionWithTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the interface <c>ClassImplementingOrExtendingExpressionWithTypeArguments</c>
    /// (<c>{ readonly class; readonly isImplements }</c>, utilities.ts:7450-7453); the port states that pair as a
    /// nullable tuple, which is the same two reads without a one-use type declaration.
    /// <para>
    /// PORT-NOTE: TS's <c>isClassLike(node.parent.parent)</c> passes a possibly-undefined parent to a predicate the
    /// generated guards make falsy for it; the port's pattern tests the parent for presence first, which is the same
    /// answer.
    /// </para>
    /// </remarks>
    public static (Node Class, bool IsImplements)? TryGetClassImplementingOrExtendingExpressionWithTypeArguments(Node node)
    {
        if (NodeTests.IsExpressionWithTypeArguments(node))
        {
            var parent = node.Parent!;
            if (NodeTests.IsHeritageClause(parent) && parent.Parent is { } classDeclaration && IsClassLike(classDeclaration))
            {
                return (classDeclaration, ((HeritageClause)parent).Token == SyntaxKind.ImplementsKeyword);
            }

            if (NodeTests.IsJSDocAugmentsTag(parent)
                && GetEffectiveJSDocHost(parent) is { } host
                && IsClassLike(host))
            {
                return (host, false);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7495-7497 — <c>isExpressionWithTypeArgumentsInClassExtendsClause</c>.</summary>
    public static bool IsExpressionWithTypeArgumentsInClassExtendsClause(Node node)
        => TryGetClassExtendingExpressionWithTypeArguments(node) is not null;

    /// <summary>ported from: src/compiler/utilities.ts:7505-7522 — <c>getFirstIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>EntityNameOrEntityNameExpression</c> union (identifier, qualified name or
    /// property access), so its three arms are the only kinds that arrive and the implicit <c>undefined</c> for anything
    /// else is unreachable; the port takes the upper bound <see cref="Node"/> and states that assumption with
    /// <see cref="Debug.Fail{T}(string?)"/> — the caller reads the result as a node and hands it straight on. The two
    /// walks mutate the parameter exactly as the reference reassigns it.
    /// </remarks>
    public static Identifier GetFirstIdentifier(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                return (Identifier)node;
            case SyntaxKind.QualifiedName:
                do
                {
                    node = ((QualifiedName)node).Left!;
                }
                while (node.Kind != SyntaxKind.Identifier);
                return (Identifier)node;
            case SyntaxKind.PropertyAccessExpression:
                do
                {
                    node = ((PropertyAccessExpression)node).Expression!;
                }
                while (node.Kind != SyntaxKind.Identifier);
                return (Identifier)node;
            default:
                return Debug.Fail<Identifier>("'node' was expected to be an entity name or entity name expression in 'getFirstIdentifier'.");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Literal and declaration predicates (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:10529-10531 — <c>pseudoBigIntToString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS destructures the record <c>{ negative, base10Value }</c>; <see cref="IPseudoBigInt"/> is the port's
    /// interface with those two slots, and its <c>Base10Value</c> is <c>string?</c> where TS's is a non-optional
    /// <c>string</c> — the concatenation contributes nothing for an absent value, and <c>PseudoBigInt.Parse</c> always
    /// answers a digit string.
    /// </remarks>
    public static string PseudoBigIntToString(IPseudoBigInt value)
        => (value.Negative && value.Base10Value != "0" ? "-" : "") + value.Base10Value;

    /// <summary>ported from: src/compiler/utilities.ts:10910-10912 — <c>isFunctionExpressionOrArrowFunction</c>.</summary>
    public static bool IsFunctionExpressionOrArrowFunction(Node node)
        => node.Kind is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction;

    /// <summary>ported from: src/compiler/utilities.ts:10920-10943 — <c>isNumericLiteralName</c>.</summary>
    /// <remarks>
    /// <para>
    /// The intent of numeric names is that they are names with text in a numeric form, and that setting
    /// properties/indexing with them is always equivalent to doing so with the numeric literal 'numLit', acquired by
    /// applying the abstract 'ToNumber' operation on the name's text.
    /// </para>
    /// <para>
    /// The subtlety is in the latter portion, as we cannot reliably say that anything that looks like a numeric literal
    /// is a numeric name: the text of the name must be equal to 'ToString(numLit)' for this to hold. Consider the
    /// property name '"0xF00D"'. When one indexes with '0xF00D', they are actually indexing with the value of
    /// 'ToString(0xF00D)' according to the ECMAScript specification, so it is actually as if the user indexed with the
    /// string '"61453"'. Thus the text of all numeric literals equivalent to '61453' such as '0xF00D', '0xf00D',
    /// '0170015', etc. are not valid numeric names, because their 'ToString' representation is not equal to their
    /// original text. This is motivated by ECMA-262 sections 9.3.1, 9.8.1, 11.1.5, and 11.2.1.
    /// </para>
    /// <para>
    /// Here, we test whether 'ToString(ToNumber(name))' is exactly equal to 'name'. The '+' prefix operator is
    /// equivalent here to applying the abstract ToNumber operation. Applying the 'toString()' method on a number gives
    /// us the abstract ToString operation on a number.
    /// </para>
    /// <para>
    /// Note that this accepts the values 'Infinity', '-Infinity', and 'NaN', and that this is intentional. This is
    /// desired behavior, because when indexing with them as numeric entities, you are indexing with the strings
    /// '"Infinity"', '"-Infinity"', and '"NaN"' respectively.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>(+name).toString()</c> is the port's <see cref="Js.ToNumber"/> followed by
    /// <see cref="Js.NumberToString"/>, which is ECMAScript's <c>Number::toString(10)</c> — the boxed
    /// <c>ToString</c> would follow the current culture. TS's parameter is <c>string | __String</c>; the port's is the
    /// <c>string</c> both callers hold.
    /// </para>
    /// </remarks>
    public static bool IsNumericLiteralName(string name) => Js.NumberToString(Js.ToNumber(name)) == name;

    /// <summary>ported from: src/compiler/utilities.ts:11025-11045 — <c>isTypeDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.parent.parent.phaseModifier</c> and <c>node.parent.parent.isTypeOnly</c> read through a
    /// specifier's two parents (its specifier list and its import/export declaration); the port's nullable <c>Parent</c>
    /// slots make those steps null-conditional, and the phase modifier's slot is <c>SyntaxKind?</c>, so the comparison
    /// is against the keyword rather than a pattern.
    /// </remarks>
    public static bool IsTypeDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeParameter:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.JSDocTypedefTag:
            case SyntaxKind.JSDocCallbackTag:
            case SyntaxKind.JSDocEnumTag:
                return true;
            case SyntaxKind.ImportClause:
                return ((ImportClause)node).PhaseModifier == SyntaxKind.TypeKeyword;
            case SyntaxKind.ImportSpecifier:
                return ((ImportSpecifier)node).Parent?.Parent is ImportClause importClause
                    && importClause.PhaseModifier == SyntaxKind.TypeKeyword;
            case SyntaxKind.ExportSpecifier:
                return ((ExportSpecifier)node).Parent?.Parent is ExportDeclaration exportDeclaration
                    && exportDeclaration.IsTypeOnly;
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:11162-11164 — <c>isTypeUsableAsPropertyName</c>.</summary>
    public static bool IsTypeUsableAsPropertyName(IType type)
        => (type.Flags & TypeFlags.StringOrNumberLiteralOrUnique) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:11170-11178 — <c>getPropertyNameFromType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>StringLiteralType | NumberLiteralType | UniqueESSymbolType</c> union and each
    /// arm reads one slot off it; the port takes <see cref="IType"/>, which is what the caller holds, and selects the
    /// face by the same flags TS selects it by, through <see cref="ILiteralType"/>/<see cref="IUniqueESSymbolType"/>.
    /// The reference's <c>"" + value</c> is the string itself for the string arm and <see cref="Js.NumberToString"/> for
    /// the number arm (the boxed <c>ToString</c> would follow the current culture); the unique-symbol arm's
    /// <c>escapedName</c> is <c>string?</c> in the model, and an absent one contributes the empty name.
    /// <c>Debug.fail()</c> is <see cref="Debug.Fail{T}(string?)"/>, whose message is the reference's own default.
    /// </remarks>
    public static string GetPropertyNameFromType(IType type)
    {
        if ((type.Flags & TypeFlags.UniqueESSymbol) != 0)
        {
            return ((IUniqueESSymbolType)type).EscapedName ?? "";
        }

        if ((type.Flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
        {
            var value = ((ILiteralType)type).Value;
            var valueText = value is string text ? text
                : value is double number ? Js.NumberToString(number)
                : value?.ToString() ?? "";
            return EscapeLeadingUnderscores(valueText);
        }

        return Debug.Fail<string>();
    }

    // ---------------------------------------------------------------------------------------------
    // utilitiesPublic.ts
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1170-1172 — <c>getJSDocDeprecatedTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getFirstJSDocTag(node, isJSDocDeprecatedTag)</c> answers the predicate's narrowed
    /// <c>JSDocDeprecatedTag | undefined</c>; the port's family member is typed <see cref="JSDocTag"/>, and the
    /// <c>as</c> is the predicate's own guarantee (the reading <c>getJSDocTypeTag</c> uses).
    /// </remarks>
    public static JSDocDeprecatedTag? GetJSDocDeprecatedTag(Node node)
        => GetFirstJSDocTag(node, static tag => NodeTests.IsJSDocDeprecatedTag(tag)) as JSDocDeprecatedTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1426-1429 — <c>isConstTypeReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.typeName</c> after <c>isTypeReferenceNode</c> narrows the node and
    /// <c>isIdentifier</c> narrows the name; the port casts the first and patterns the second, and reads
    /// <c>!node.typeArguments</c> as the model's <c>is null</c>.
    /// </remarks>
    public static bool IsConstTypeReference(Node node)
    {
        if (!NodeTests.IsTypeReferenceNode(node))
        {
            return false;
        }

        var typeReference = (TypeReferenceNode)node;
        return typeReference.TypeName is Identifier typeName
            && typeName.EscapedText == "const"
            && typeReference.TypeArguments is null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1600-1602 — <c>isPrivateIdentifierClassElementDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS composes <c>isPropertyDeclaration(node) || isMethodOrAccessor(node)</c>
    /// (utilitiesPublic.ts:1765-1774) with <c>isPrivateIdentifier(node.name)</c> and narrows to
    /// <c>PrivateClassElementDeclaration</c>; the port answers <c>bool</c> and reads the same test through the private
    /// <c>GetPrivateIdentifierClassElementName</c> (Utilities.Binder.Modifiers.cs), which is exactly the four classes'
    /// private-identifier name test.
    /// </remarks>
    public static bool IsPrivateIdentifierClassElementDeclaration(Node node)
        => GetPrivateIdentifierClassElementName(node) is not null;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1968-1983 — <c>isCallLikeExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to the <c>CallLikeExpression</c> union, which the port answers as <c>bool</c>; the
    /// binary arm reads <c>operatorToken.kind</c> through the class the kind already selected.
    /// </remarks>
    public static bool IsCallLikeExpression(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.Decorator:
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxOpeningFragment:
                return true;
            case SyntaxKind.BinaryExpression:
                return ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.InstanceOfKeyword;
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1985-1987 — <c>isCallOrNewExpression</c>.</summary>
    public static bool IsCallOrNewExpression(Node node)
        => node.Kind is SyntaxKind.CallExpression or SyntaxKind.NewExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2108-2112 — <c>isAssertionExpression</c>.</summary>
    public static bool IsAssertionExpression(Node node)
        => node.Kind is SyntaxKind.TypeAssertionExpression or SyntaxKind.AsExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2419-2424 — <c>isStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isStatementKindButNotDeclarationKind</c> and <c>isDeclarationStatementKind</c> are already the
    /// private kind tables Utilities.cs states (utilitiesPublic.ts:2360, :2375); <c>isBlockStatement</c> is the private
    /// member below.
    /// </remarks>
    public static bool IsStatement(Node node)
        => IsStatementKindButNotDeclarationKind(node.Kind)
            || IsDeclarationStatementKind(node.Kind)
            || IsBlockStatement(node);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2426-2434 — <c>isBlockStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>if (node.parent !== undefined)</c> guard is the port's pattern test; the reference falls
    /// through to the function-block test either way. TS's guard narrows to <c>Block</c>, which the port's kind test
    /// above has already established for <c>IsFunctionBlock</c>.
    /// </remarks>
    private static bool IsBlockStatement(Node node)
    {
        if (node.Kind != SyntaxKind.Block)
        {
            return false;
        }

        if (node.Parent is { } parent && parent.Kind is SyntaxKind.TryStatement or SyntaxKind.CatchClause)
        {
            return false;
        }

        return !IsFunctionBlock(node);
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2597-2599 — <c>isTypeReferenceType</c>.</summary>
    public static bool IsTypeReferenceType(Node node)
        => node.Kind is SyntaxKind.TypeReference or SyntaxKind.ExpressionWithTypeArguments;
}
