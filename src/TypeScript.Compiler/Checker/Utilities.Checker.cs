namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts (the members the checker slices call that no earlier slice landed)
//    997-999    isPlainJsFile
//  2677-2679    isVarConst
//  2724-2726    isLiteralImportTypeNode
//  3014-3017    isVariableDeclarationInVariableStatement
//  3027-3030    isCommonJsExportPropertyAssignment
//  3033-3037    isValidESSymbolDeclaration
//  3068-3070    isFunctionBlock
//  3127-3129    getContainingFunction
//  3142-3149    getContainingClassStaticBlock
//  3768-3770    isNamespaceReexportDeclaration
//  3824-3830    isJSDocIndexSignature
//  4041-4051    getNameOfExpando
//  4487-4489    isTypeAlias
//  4900-4918    getAssignmentTargetKind
//  5001-5006    walkUp (file-local upstream)
//  5009-5011    walkUpParenthesizedTypes
//  5035-5038    skipTypeParentheses
//  5054-5060    isDeleteTarget
//  5397-5424    getPropertyNameForPropertyNameNode
//  5454-5456    isKnownSymbol
//  7190-7192    hasEffectiveModifier
//  7231-7233    hasEffectiveReadonlyModifier
//  8285-8287    isUMDExportSymbol
//  10529-10531  pseudoBigIntToString
//  10910-10912  isFunctionExpressionOrArrowFunction
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

// Deliberately NOT declared here: the `Utilities` partial that Checker/Checker.Support.cs states beside its checker
// block (:1839-2242) already declares these on the same class, and a second declaration would be a CS0111 duplicate.
// The task's members among them are utilities.ts:2788-2882 isPartOfTypeNode, :5063-5069 isNodeDescendantOf,
// :7505-7522 getFirstIdentifier and :10920-10943 isNumericLiteralName, together with isPartOfTypeNode's two file-local
// helpers (:2884-2888 isPartOfTypeExpressionWithTypeArguments, :7444-7497 isExpressionWithTypeArgumentsInClassExtendsClause).
// Call shape: Utilities.IsPartOfTypeNode(Node) -> bool, Utilities.IsNodeDescendantOf(Node?, Node?) -> bool,
// Utilities.GetFirstIdentifier(Node) -> Identifier, Utilities.IsNumericLiteralName(string) -> bool.
/// <summary>
/// The <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members the checker slices call that no earlier slice landed.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: these are declared here rather than in the file named after their source file because this is the slice
/// that needs them: the checker reaches them through <c>Utilities.*</c>. Each member carries its own <c>ported from</c>
/// line, so moving a group to <c>Utilities/</c> when the M5 slice lands is mechanical — the same treatment
/// <c>Program/Utilities.ModuleResolution.cs</c> documents for the program slice.
/// </para>
/// <para>
/// PORT-NOTE: the note under the file's header names the six members of the task's list that are <em>not</em> declared
/// here, because the <c>Utilities</c> partial Checker/Checker.Support.cs states beside its checker block declares them
/// already — one name, one declaration. Their call shape is:
/// <c>Utilities.IsPartOfTypeNode(Node)</c> answers <c>bool</c> with
/// <c>Utilities.IsPartOfTypeExpressionWithTypeArguments(Node)</c> private beside it and
/// <c>Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(Node)</c> <c>internal</c> there (widened from
/// private for the cross-class call at Checker/Checker.Check.Source.cs:844),
/// <c>Utilities.IsNodeDescendantOf(Node?, Node?)</c> answers <c>bool</c>,
/// <c>Utilities.GetFirstIdentifier(Node)</c> answers <see cref="Identifier"/> and
/// <c>Utilities.IsNumericLiteralName(string)</c> answers <c>bool</c>.
/// </para>
/// <para>
/// PORT-NOTE: a member whose upstream body needs another member of this file has no extra dependency: the seven
/// helpers this file adds beside the task's list (<c>IsVarConst</c>, <c>HasEffectiveModifier</c>,
/// <c>HasEffectiveReadonlyModifier</c>, <c>IsVariableDeclarationInVariableStatement</c>, <c>IsFunctionBlock</c>,
/// <c>IsBlockStatement</c>, <c>WalkUp</c>) are all <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members too, so they
/// are ported in place rather than called by a name nobody declares. Everything else the bodies call already exists
/// elsewhere in the port and is named in the member's own remarks.
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
    /// nullable expression, and a node that is not there is not a delete target.
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
    /// <para>
    /// PORT-NOTE: TS's parameter is non-nullable; the port takes <c>Node?</c> because the checker's derived-member pass
    /// hands it <c>decl as Node</c> over a declaration it then tests, and a node that is not there is not a
    /// private-identifier class element.
    /// </para>
    /// </remarks>
    public static bool IsPrivateIdentifierClassElementDeclaration(Node? node)
        => node is not null && GetPrivateIdentifierClassElementName(node) is not null;

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

    /// <summary>ported from: src/compiler/visitorPublic.ts:223-271 — <c>visitNodes</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>visitorPublic.ts</c>'s <c>visitNodes</c> has three overloads in the reference (the two generic ones
    /// only refine the return type through the <c>test</c> predicate); C# states the implementation overload, whose
    /// parameters are the ones the checker's node builder passes. TS's <c>Visitor</c> returns the <c>VisitResult</c> union
    /// <c>Node | readonly Node[] | undefined</c>, which the port spells <c>object?</c> — the same convention
    /// <c>NodeFactory.CopyCustomPrologue</c> uses and discriminates the same way (see <c>AppendVisitedNode</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>factory.createNodeArray</c> is the module-level singleton of <c>factory/nodeFactory.ts</c>,
    /// i.e. <c>NodeFactory.Factory</c> here — the same instance the node builder reads as <c>_factory</c>;
    /// <c>setTextRangePosEnd</c> is the member of this class in <c>Utilities/Utilities.Parser.cs</c>. TS's sparse-array
    /// guard (<c>node !== undefined</c>, <c>visited === undefined</c>) is kept for the <c>visited</c> half — a
    /// <c>VisitResult</c> really can be <c>undefined</c>, meaning "reuse this node" — while the <c>node</c> half cannot
    /// occur: the port's <c>NodeArray&lt;T&gt;</c> is a dense <c>List&lt;T&gt;</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>Debug.assertEachNode</c>/<c>assertNode</c> (<c>debug.ts</c>) are file-local here so that the port's
    /// <c>Debug</c> class gains no members this slice does not need; they assert exactly what the reference's do.
    /// </para>
    /// </remarks>
    public static NodeArray<Node>? VisitNodes(
        NodeArray<Node>? nodes,
        Func<Node, object?>? visitor,
        Func<Node, bool>? test = null,
        int? start = null,
        int? count = null)
    {
        if (nodes is null)
        {
            // If the input type is undefined, then the output type can be undefined.
            return nodes;
        }

        // Ensure start and count have valid values
        var length = nodes.Count;
        if (start is null || start < 0)
        {
            start = 0;
        }

        if (count is null || count > length - start)
        {
            count = length - start;
        }

        bool? hasTrailingComma;
        var pos = -1;
        var end = -1;
        if (start > 0 || count < length)
        {
            // Since this is a fragment of a node array, we do not copy over the previous location
            // and will only copy over `hasTrailingComma` if we are including the last element.
            hasTrailingComma = nodes.HasTrailingComma && start + count == length;
        }
        else
        {
            pos = nodes.Pos;
            end = nodes.End;
            hasTrailingComma = nodes.HasTrailingComma;
        }

        var updated = VisitArrayWorker(nodes, visitor, test, start.Value, count.Value);
        if (!ReferenceEquals(updated, nodes))
        {
            var updatedArray = NodeFactory.Factory.CreateNodeArray(updated, hasTrailingComma);
            SetTextRangePosEnd(updatedArray, pos, end);
            return updatedArray;
        }

        // If we are here, updated === nodes. This means that it's still a NodeArray,
        // and also that its contents passed the tests in visitArrayWorker, so has contents
        // of type TOut.
        return nodes;

        static void AssertNode(Node node, Func<Node, bool>? test)
            => Debug.Assert(test is null || test(node), "visited node must pass the test");

        static void AssertEachNode(IEnumerable<Node> nodes, Func<Node, bool>? test)
        {
            foreach (var node in nodes)
            {
                AssertNode(node, test);
            }
        }

        // PORT-NOTE: TS's `visitArrayWorker` (visitorPublic.ts:328-376) — file-local because `visitNodes` is its only
        // caller, exactly as the reference keeps it module-private.
        static List<Node> VisitArrayWorker(
            NodeArray<Node> nodes,
            Func<Node, object?>? visitor,
            Func<Node, bool>? test,
            int start,
            int count)
        {
            List<Node>? updated = null;

            var length = nodes.Count;
            if (start > 0 || count < length)
            {
                // If we are not visiting all of the original nodes, we must always create a new array.
                updated = [];
            }

            // Visit each original node.
            for (var i = 0; i < count; i++)
            {
                var node = nodes[i + start];
                var visited = visitor is not null ? visitor(node) : node;
                if (updated is not null || visited is null || !ReferenceEquals(visited, node))
                {
                    if (updated is null)
                    {
                        // Ensure we have a copy of `nodes`, up to the current index.
                        updated = nodes.GetRange(0, i);
                        AssertEachNode(updated, test);
                    }
                    if (visited is not null)
                    {
                        if (visited is IEnumerable<Node> visitedNodes)
                        {
                            foreach (var visitedNode in visitedNodes)
                            {
                                AssertNode(visitedNode, test);
                                updated.Add(visitedNode);
                            }
                        }
                        else
                        {
                            AssertNode((Node)visited, test);
                            updated.Add((Node)visited);
                        }
                    }
                }
            }

            if (updated is not null)
            {
                // If we have an updated array, then all items will have been tested.
                return updated;
            }

            // If we are going to return the original array, ensure it passes the test.
            AssertEachNode(nodes, test);
            return nodes;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:3768-3770 — <c>isNamespaceReexportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: stated here because no order file assigns this member and its only ported reader is
    /// <c>Checker.GetAccessibleSymbolChain</c> (the sibling partials under <c>Utilities/</c> were held by other agents
    /// this wave). TS's <c>node.parent.moduleSpecifier</c> reads the parent of a <c>NamespaceExport</c>, which the guard
    /// in front of it selects as the <c>ExportDeclaration</c>; the port states that cast.
    /// </remarks>
    public static bool IsNamespaceReexportDeclaration(Node node)
        => NodeTests.IsNamespaceExport(node) && ((ExportDeclaration)node.Parent!).ModuleSpecifier is not null;

    /// <summary>ported from: src/compiler/utilities.ts:8285-8287 — <c>isUMDExportSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: stated here for the same reason as <see cref="IsNamespaceReexportDeclaration"/>; its only ported
    /// reader is <c>Checker.GetAccessibleSymbolChain</c>. TS's truthiness chain is the port's explicit null tests in the
    /// same order, and <c>isNamespaceExportDeclaration</c> is <see cref="NodeTests.IsNamespaceExportDeclaration"/>.
    /// </remarks>
    public static bool IsUMDExportSymbol(ISymbol? symbol)
        => symbol?.Declarations is not null
            && symbol.Declarations.Count > 0
            && NodeTests.IsNamespaceExportDeclaration((Node)symbol.Declarations[0]);
}
