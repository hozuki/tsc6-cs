using System.Globalization;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

/// <summary>
/// The <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members the landed checker slices call that no earlier slice declared.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: this file is the complement of <c>Checker/Utilities.Checker.cs</c> (the 35 members the M6Generics
/// utilities sub-slice landed) — the two files declare into the same <c>Utilities</c> partial class and share no member
/// name, which is the layout M6 settled: one class, not one directory. The names here are exactly the ones the
/// checker slices call that no file declared: 28 of them as `Utilities.&lt;Name&gt;(…)` (CS0117 against <c>Utilities</c>
/// in the whole-tree build), plus <c>EntityNameToString</c> and <c>GetAliasDeclarationFromName</c>, which
/// <c>Checker/Checker.SymbolSupport.cs</c> calls by bare name, and <c>GetJSDocImplementsTags</c>/<c>GetHeritageClause</c>
/// and the rest of the <c>getEffective…TypeNodes</c> family those bodies need.
/// </para>
/// <para>
/// PORT-NOTE: the recurring mappings are stated once here; each member's own remark names only what is specific to it.
/// (a) A TS union parameter becomes <c>Node</c> — the widest type every landed call site passes — with a kind switch or
/// an <c>is</c> pattern inside, because C# has no structural union; the remark names the reference union.
/// (b) <c>undefined</c> used as "absent" becomes <c>null</c> and <c>T | undefined</c> becomes <c>T?</c>.
/// (c) Truthiness of an object becomes <c>is not null</c>/<c>is null</c>; <c>Js.IsTruthy</c> is for JS values, not for
/// node, symbol or type slots. (d) A TS type-guard return (<c>node is X</c>) becomes <c>bool</c>: C# has no
/// user-defined type predicates, so the caller writes the cast the guard used to perform. (e) An array the reference
/// returns by identity is projected into a fresh list only where the port's element types differ
/// (<c>NodeArray&lt;IClassElement&gt;</c> versus <c>NodeArray&lt;Node&gt;</c>); every landed caller iterates, none aliases.
/// (f) A file-local upstream helper (<c>function foo()</c> with no <c>export</c>) is declared <c>private static</c> here,
/// in the same class, which is what its call sites require.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // Ambient modules, diagnostic plumbing (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2107-2109 — <c>isShorthandAmbientModuleSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>moduleSymbol.valueDeclaration</c> is <c>IDeclaration?</c> in the port, so the file-local helper's
    /// <c>Node?</c> parameter takes the one reference conversion the model needs.
    /// </remarks>
    public static bool IsShorthandAmbientModuleSymbol(ISymbol moduleSymbol)
        => IsShorthandAmbientModule((Node?)moduleSymbol.ValueDeclaration);

    /// <summary>ported from: src/compiler/utilities.ts:2111-2114 — <c>isShorthandAmbientModule</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!(node as ModuleDeclaration).body</c> tests the slot for absence; the port's
    /// <c>Body is null</c> is the same question for a nullable node slot.
    /// </remarks>
    private static bool IsShorthandAmbientModule(Node? node)
        => node is not null && node.Kind == SyntaxKind.ModuleDeclaration && ((ModuleDeclaration)node).Body is null;

    /// <summary>ported from: src/compiler/utilities.ts:8776-8778 — <c>getDiagnosticFilePath</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>diagnostic.file ? diagnostic.file.path : undefined</c> is the port's null-propagating
    /// <c>File?.Path</c> (the "absent file" arm is the same <c>null</c>). Its two call sites are inside the
    /// <c>Utilities</c> partial (<c>Utilities/Utilities.Diagnostics.cs</c>'s two diagnostic comparers), which is why it
    /// is declared here rather than beside them.
    /// </remarks>
    public static string? GetDiagnosticFilePath(Diagnostic diagnostic) => diagnostic.File?.Path;

    // ---------------------------------------------------------------------------------------------
    // Entity names (utilities.ts:2390-2413, 5153-5170)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2390-2413 — <c>entityNameToString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union
    /// <c>EntityNameOrEntityNameExpression | JSDocMemberName | JsxTagNameExpression | PrivateIdentifier</c>; the port
    /// takes <c>Node</c> (the union's common face) and the switch mirrors the reference's kind switch arm for arm,
    /// including its <c>PropertyAccessExpression</c> guard and its <c>Debug.assertNever</c> default — the port's
    /// <c>Debug.Fail&lt;T&gt;</c> has the same throw-only contract and returns the method's type for the compiler.
    /// </remarks>
    public static string EntityNameToString(Node node) => node.Kind switch
    {
        SyntaxKind.ThisKeyword => "this",
        SyntaxKind.PrivateIdentifier or SyntaxKind.Identifier
            => GetFullWidth(node) == 0 ? IdText(node) : GetTextOfNode(node),
        SyntaxKind.QualifiedName
            => EntityNameToString(((QualifiedName)node).Left!) + "." + EntityNameToString(((QualifiedName)node).Right!),
        SyntaxKind.PropertyAccessExpression
            => ((PropertyAccessExpression)node).Name is Identifier or PrivateIdentifier
                ? EntityNameToString(((PropertyAccessExpression)node).Expression!)
                    + "." + EntityNameToString(((PropertyAccessExpression)node).Name!)
                : Debug.Fail<string>("Unexpected node kind: " + ((PropertyAccessExpression)node).Name!.Kind),
        SyntaxKind.JSDocMemberName
            => EntityNameToString(((JSDocMemberName)node).Left!) + "#" + EntityNameToString(((JSDocMemberName)node).Right!),
        SyntaxKind.JsxNamespacedName
            => EntityNameToString(((JsxNamespacedName)node).Namespace!) + ":" + EntityNameToString(((JsxNamespacedName)node).Name!),
        _ => Debug.Fail<string>("Unexpected node kind: " + node.Kind),
    };

    /// <summary>ported from: src/compiler/utilities.ts:5153-5170 — <c>getAliasDeclarationFromName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>EntityName</c> and its <c>QualifiedName</c> arm reassigns the parameter to a
    /// parent, so the port widens the parameter to <c>Node</c>; the returned declaration is the port's
    /// <c>IDeclaration</c> face of <c>Declaration</c>. TS's switch has no default (it falls out as <c>undefined</c>),
    /// which the port states as the <c>default</c> arm.
    /// </remarks>
    public static IDeclaration? GetAliasDeclarationFromName(Node node)
    {
        switch (node.Parent?.Kind)
        {
            case SyntaxKind.ImportClause:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.NamespaceExport:
                return (IDeclaration)node.Parent!;
            case SyntaxKind.QualifiedName:
                do
                {
                    node = node.Parent!;
                }
                while (node.Parent?.Kind == SyntaxKind.QualifiedName);

                return GetAliasDeclarationFromName(node);
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Source files, declarations and their members (utilities.ts:2633-2635, 2983-2993)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2633-2635 — <c>isGlobalSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node as SourceFile</c> cast follows the kind test; the port's <c>&amp;&amp;</c> keeps the same
    /// short-circuit, so the cast only runs for a source file.
    /// </remarks>
    public static bool IsGlobalSourceFile(Node node)
        => node.Kind == SyntaxKind.SourceFile && !IsExternalOrCommonJsModule((SourceFile)node);

    /// <summary>ported from: src/compiler/utilities.ts:2983-2993 — <c>getMembersOfDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is
    /// <c>NodeArray&lt;ClassElement | TypeElement | ObjectLiteralElement&gt; | undefined</c> and it hands back the
    /// declaration's own array. The port's <c>NodeArray&lt;IClassElement&gt;</c> and <c>NodeArray&lt;ITypeElement&gt;</c>
    /// share no list type, so the members are projected into a fresh <c>List&lt;Node&gt;</c> — the one landed caller
    /// (<c>Checker/Checker.TypeSupport.cs</c>'s late-binding walk) iterates it and never aliases it. The kind switch is
    /// the reference's, including its fall-out <c>undefined</c> as <c>null</c>.
    /// </remarks>
    public static List<Node>? GetMembersOfDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.TypeLiteral:
                return [.. MembersOfObjectTypeDeclaration(node)!.Cast<Node>()];
            case SyntaxKind.ObjectLiteralExpression:
                return [.. ((ObjectLiteralExpression)node).Properties!.Cast<Node>()];
            default:
                return null;
        }
    }

    /// <summary>The <c>node.members</c> read of <see cref="GetMembersOfDeclaration"/>'s four object-type kinds.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reaches the one <c>members</c> slot on the union its guard narrowed to
    /// <c>ObjectTypeDeclaration</c>; the port's two faces name different element types
    /// (<c>IClassElement</c> for classes, <c>ITypeElement</c> for interfaces and type literals), so the read is a
    /// pattern over the four concrete declarations.
    /// </remarks>
    private static IEnumerable<INode>? MembersOfObjectTypeDeclaration(Node node) => node switch
    {
        ClassDeclaration declaration => declaration.Members,
        ClassExpression declaration => declaration.Members,
        InterfaceDeclaration declaration => declaration.Members,
        TypeLiteralNode declaration => declaration.Members,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // Initializers and the question token (utilities.ts:3931-3951, 4460-4472)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3931-3941 — <c>getEffectiveInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>HasExpressionInitializer</c>
    /// (<c>VariableDeclaration | ParameterDeclaration | BindingElement | PropertyDeclaration | PropertyAssignment |
    /// EnumMember</c>), which the port states as <c>Node</c> plus the two slot helpers below. The `||` chain and its
    /// order are the reference's, including the <c>node.name &amp;&amp; …</c> guard that the port writes as a pattern.
    /// </remarks>
    public static Expression? GetEffectiveInitializer(Node node)
    {
        var initializer = InitializerOfHasExpressionInitializer(node);
        if (IsInJSFile(node) && initializer is not null && NodeTests.IsBinaryExpression(initializer)
            && ((BinaryExpression)initializer).OperatorToken!.Kind is SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken
            && NameOfHasExpressionInitializer(node) is { } name
            && IsEntityNameExpression(name)
            && IsSameEntityName((Expression)name, ((BinaryExpression)initializer).Left!))
        {
            return ((BinaryExpression)initializer).Right;
        }

        return initializer;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3948-3951 — <c>getDeclaredExpandoInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>HasExpressionInitializer</c>, stated here as <c>Node</c> with the two
    /// slot helpers shared with <see cref="GetEffectiveInitializer"/>. TS's <c>init &amp;&amp; getExpandoInitializer(…)</c>
    /// returns <c>undefined</c> for a falsy initializer, which the port states as the <c>null</c> arm of the conditional.
    /// </remarks>
    public static Expression? GetDeclaredExpandoInitializer(Node node)
    {
        var init = GetEffectiveInitializer(node);
        return init is null ? null : GetExpandoInitializer(init, IsPrototypeAccess(NameOfHasExpressionInitializer(node)!));
    }

    /// <summary>The <c>node.initializer</c> read of <c>HasExpressionInitializer</c> (utilities.ts:3949).</summary>
    /// <remarks>
    /// PORT-NOTE: the model has no <c>HasExpressionInitializer</c> interface (§12), so the read is a pattern over the
    /// six kinds the alias names.
    /// </remarks>
    private static Expression? InitializerOfHasExpressionInitializer(Node node) => node switch
    {
        VariableDeclaration declaration => declaration.Initializer,
        ParameterDeclaration declaration => declaration.Initializer,
        BindingElement declaration => declaration.Initializer,
        PropertyDeclaration declaration => declaration.Initializer,
        PropertyAssignment declaration => declaration.Initializer,
        EnumMember declaration => declaration.Initializer,
        _ => null,
    };

    /// <summary>The <c>node.name</c> read of <c>HasExpressionInitializer</c> (utilities.ts:3936, :3950).</summary>
    /// <remarks>PORT-NOTE: the same six-kind pattern as <see cref="InitializerOfHasExpressionInitializer"/>.</remarks>
    private static Node? NameOfHasExpressionInitializer(Node node) => node switch
    {
        VariableDeclaration declaration => declaration.Name,
        ParameterDeclaration declaration => declaration.Name,
        BindingElement declaration => declaration.Name,
        PropertyDeclaration declaration => declaration.Name,
        PropertyAssignment declaration => declaration.Name,
        EnumMember declaration => declaration.Name,
        _ => null,
    };

    /// <summary>ported from: src/compiler/utilities.ts:4460-4472 — <c>hasQuestionToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard casts the union to <c>ParameterDeclaration | MethodDeclaration | PropertyDeclaration</c>
    /// and reads <c>questionToken</c>; the port reads the same slot through the seven-kind pattern below (the model's
    /// <c>QuestionToken</c> slots live on the concrete classes, not on one face).
    /// </remarks>
    public static bool HasQuestionToken(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Parameter:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                return QuestionTokenOf(node) is not null;
            default:
                return false;
        }
    }

    /// <summary>The <c>questionToken</c> read of <see cref="HasQuestionToken"/>'s seven kinds.</summary>
    private static PunctuationToken? QuestionTokenOf(Node node) => node switch
    {
        ParameterDeclaration declaration => declaration.QuestionToken,
        MethodDeclaration declaration => declaration.QuestionToken,
        MethodSignature declaration => declaration.QuestionToken,
        ShorthandPropertyAssignment declaration => declaration.QuestionToken,
        PropertyAssignment declaration => declaration.QuestionToken,
        PropertyDeclaration declaration => declaration.QuestionToken,
        PropertySignature declaration => declaration.QuestionToken,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // JSDoc parameter symbols and value-signature predicates (utilities.ts:4750-4764, 4997-4999)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:4750-4764 — <c>getParameterSymbolFromJSDoc</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isIdentifier(node.name)</c> narrows; the port's <c>is not Identifier</c> pattern is the same
    /// test with the cast the guard performed. The signature is the port's <c>ISignatureDeclarationBase</c> (what
    /// <c>getHostSignatureFromJSDoc</c> returns), whose <c>Parameters</c> slot is the reference's <c>decl.parameters</c>.
    /// </remarks>
    public static ISymbol? GetParameterSymbolFromJSDoc(JSDocParameterTag node)
    {
        if (node.Symbol is not null)
        {
            return node.Symbol;
        }

        if (node.Name is not Identifier identifier)
        {
            return null;
        }

        var name = identifier.EscapedText;
        var declaration = GetHostSignatureFromJSDoc(node);
        if (declaration is null)
        {
            return null;
        }

        var parameter = Core.Find(
            declaration.Parameters,
            (p, _) => p.Name is Identifier parameterName && parameterName.EscapedText == name);
        return parameter?.Symbol;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4997-4999 — <c>isValueSignatureDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return is the type guard <c>node is ValueSignatureDeclaration</c>; the port states it as
    /// <c>bool</c> (mapping rule (d)), and its five disjuncts keep the reference's order.
    /// </remarks>
    public static bool IsValueSignatureDeclaration(Node node)
        => NodeTests.IsFunctionExpression(node)
            || NodeTests.IsArrowFunction(node)
            || IsMethodOrAccessor(node)
            || NodeTests.IsFunctionDeclaration(node)
            || NodeTests.IsConstructorDeclaration(node);

    // ---------------------------------------------------------------------------------------------
    // Base and implements type nodes (utilities.ts:5195-5252)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:5195-5205 — <c>getEffectiveBaseTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ClassLikeDeclaration | InterfaceDeclaration</c> and both landed call sites
    /// (<c>Checker/Checker.SymbolSupport.cs</c>'s two base-type readers) pass
    /// <c>GetClassLikeDeclarationOfSymbol</c>'s <c>Node?</c>, so the parameter is <c>Node?</c> — the existing-declaration
    /// rule, not the union's interface. The null arm answers <c>null</c> exactly where TS would throw on
    /// <c>node.kind</c>; the call sites guard with <c>is not null</c>.
    /// </remarks>
    public static ExpressionWithTypeArguments? GetEffectiveBaseTypeNode(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        var baseType = GetClassExtendsHeritageElement(node);
        if (baseType is not null && IsInJSFile(node))
        {
            // Prefer an @augments tag because it may have type parameters.
            var tag = GetJSDocAugmentsTag(node);
            if (tag is not null)
            {
                return tag.Class;
            }
        }

        return baseType;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5208-5211 — <c>getClassExtendsHeritageElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ClassLikeDeclaration | InterfaceDeclaration</c>; the port takes <c>Node</c>
    /// because <see cref="GetEffectiveBaseTypeNode"/>'s widened parameter is what feeds it. TS's
    /// <c>heritageClause &amp;&amp; heritageClause.types.length &gt; 0 ? … : undefined</c> is restated with
    /// <c>is not null</c> and <c>Core.Length</c>.
    /// </remarks>
    public static ExpressionWithTypeArguments? GetClassExtendsHeritageElement(Node node)
    {
        var heritageClause = GetHeritageClause(HeritageClausesOf(node), SyntaxKind.ExtendsKeyword);
        return heritageClause is not null && Core.Length(heritageClause.Types) > 0 ? heritageClause.Types![0] : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5214-5223 — <c>getEffectiveImplementsTypeNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ClassLikeDeclaration</c>, stated as the port's
    /// <c>IClassLikeDeclarationBase</c> face (its one landed caller casts to it). The JS arm returns a mapped array in TS
    /// and the heritage clause's own array otherwise, so the port's return type is
    /// <c>IList&lt;ExpressionWithTypeArguments&gt;?</c> — the model's <c>NodeArray&lt;T&gt;</c> is an <c>IList&lt;T&gt;</c>,
    /// so the non-JS arm still hands back the declaration's array by identity.
    /// </remarks>
    public static IList<ExpressionWithTypeArguments>? GetEffectiveImplementsTypeNodes(IClassLikeDeclarationBase node)
    {
        if (IsInJSFile((Node)node))
        {
            return [.. GetJSDocImplementsTags((Node)node).Select(tag => tag.Class!)];
        }

        var heritageClause = GetHeritageClause(node.HeritageClauses, SyntaxKind.ImplementsKeyword);
        return heritageClause?.Types;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5236-5239 — <c>getInterfaceBaseTypeNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>InterfaceDeclaration</c> interface; the port names the concrete class of the
    /// same name, which is what all three landed call sites cast to. It is deliberately not
    /// <c>IClassLikeDeclarationBase</c>: C# interface implementation requires exact member types and
    /// <c>InterfaceDeclaration.Members</c> is <c>NodeArray&lt;ITypeElement&gt;</c> where that face declares
    /// <c>NodeArray&lt;IClassElement&gt;</c>, so the class does not implement it.
    /// </remarks>
    public static NodeArray<ExpressionWithTypeArguments>? GetInterfaceBaseTypeNodes(InterfaceDeclaration node)
    {
        var heritageClause = GetHeritageClause(node.HeritageClauses, SyntaxKind.ExtendsKeyword);
        return heritageClause?.Types;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5242-5252 — <c>getHeritageClause</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>for (const clause of clauses)</c> over the optional array is the port's
    /// <c>is not null</c> guard plus <c>foreach</c>; the argument type is the model's
    /// <c>NodeArray&lt;HeritageClause&gt;</c>, which is what every <c>HeritageClauses</c> slot declares.
    /// </remarks>
    public static HeritageClause? GetHeritageClause(NodeArray<HeritageClause>? clauses, SyntaxKind kind)
    {
        if (clauses is not null)
        {
            foreach (var clause in clauses)
            {
                if (clause.Token == kind)
                {
                    return clause;
                }
            }
        }

        return null;
    }

    /// <summary>The <c>node.heritageClauses</c> read of the two members above (utilities.ts:5209, :5237).</summary>
    /// <remarks>
    /// PORT-NOTE: <c>IClassLikeDeclarationBase</c> declares the slot for classes (and class expressions);
    /// <c>InterfaceDeclaration</c> declares its own with the same type, so the read is a two-arm pattern.
    /// </remarks>
    private static NodeArray<HeritageClause>? HeritageClausesOf(Node node) => node switch
    {
        IClassLikeDeclarationBase declaration => declaration.HeritageClauses,
        InterfaceDeclaration declaration => declaration.HeritageClauses,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // Lexical environments, JSDoc type parameters, accessors, export defaults (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:5611-5622 — <c>nodeStartsNewLexicalEnvironment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the nine-way <c>kind ===</c> chain is the port's <c>or</c> pattern over the same kinds, all of which
    /// exist in the model's <see cref="SyntaxKind"/>.
    /// </remarks>
    public static bool NodeStartsNewLexicalEnvironment(Node node)
        => node.Kind is SyntaxKind.Constructor
            or SyntaxKind.FunctionExpression
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.ArrowFunction
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.SourceFile;

    /// <summary>ported from: src/compiler/utilities.ts:6918-6920 — <c>getJSDocTypeParameterDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>flatMap(getJSDocTags(node), tag =&gt; … ? tag.typeParameters : undefined)</c> is
    /// <c>Core.FlatMapToMutable</c> with the same "skip the falsy result" contract, so the element order and the dropping
    /// of non-template tags are the reference's. The return type is the port's mutable
    /// <c>List&lt;TypeParameterDeclaration&gt;</c> because the two landed callers (<c>getOuterTypeParameters</c> and
    /// <c>getSignatureFromDeclaration</c>) both need a list, whereas TS's declared return is <c>readonly</c>.
    /// </remarks>
    public static List<TypeParameterDeclaration> GetJSDocTypeParameterDeclarations(Node node)
        => Core.FlatMapToMutable<JSDocTag, TypeParameterDeclaration>(
            GetJSDocTags(node),
            (tag, _) => IsNonTypeAliasTemplate(tag) ? ((JSDocTemplateTag)tag).TypeParameters : null);

    /// <summary>ported from: src/compiler/utilities.ts:6923-6925 — <c>isNonTypeAliasTemplate</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>tag.parent.tags!.some(isJSDocTypeAlias)</c> is <c>Core.Some</c> over the port's
    /// <c>NodeArray&lt;JSDocTag&gt;</c>; both <c>some</c> calls and the <c>JSDoc</c> kind guard keep the reference's order.
    /// The <c>isJSDocTemplateTag</c> guard is what makes the caller's cast to <c>JSDocTemplateTag</c> sound.
    /// </remarks>
    private static bool IsNonTypeAliasTemplate(JSDocTag tag)
        => NodeTests.IsJSDocTemplateTag(tag)
            && !(tag.Parent!.Kind == SyntaxKind.JSDoc
                && (Core.Some(((JSDoc)tag.Parent!).Tags, t => IsJSDocTypeAlias(t))
                    || Core.Some(((JSDoc)tag.Parent!).Tags, t => NodeTests.IsJSDocOverloadTag(t))));

    /// <summary>ported from: src/compiler/utilities.ts:7226-7228 — <c>hasAccessorModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>hasSyntacticModifier(node, ModifierFlags.Accessor)</c> is the port's member of the same name
    /// with the same flag (<c>ModifierFlags.Accessor = 1u &lt;&lt; 9</c> in the model).
    /// </remarks>
    public static bool HasAccessorModifier(Node node) => HasSyntacticModifier(node, ModifierFlags.Accessor);

    /// <summary>ported from: src/compiler/utilities.ts:7609-7615 — <c>getLocalSymbolForExportDefault</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!symbol.declarations</c> is the port's <c>Declarations is null</c>; the loop returns the first
    /// bounded <c>decl.localSymbol</c>, which the model carries on <c>IDeclaration</c>.
    /// </remarks>
    public static ISymbol? GetLocalSymbolForExportDefault(ISymbol symbol)
    {
        if (!IsExportDefaultSymbol(symbol) || symbol.Declarations is null)
        {
            return null;
        }

        foreach (var declaration in symbol.Declarations)
        {
            if (declaration.LocalSymbol is not null)
            {
                return declaration.LocalSymbol;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7617-7619 — <c>isExportDefaultSymbol</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>symbol.declarations[0]</c> is an <c>IDeclaration</c> and <c>hasSyntacticModifier</c> takes the
    /// port's <c>Node</c>, hence the one cast; <c>length(symbol.declarations) &gt; 0</c> is <c>Core.Length</c>.
    /// </remarks>
    private static bool IsExportDefaultSymbol(ISymbol symbol)
        => symbol is not null
            && Core.Length(symbol.Declarations) > 0
            && HasSyntacticModifier((Node)symbol.Declarations![0], ModifierFlags.Default);

    /// <summary>ported from: src/compiler/utilities.ts:8330-8352 — <c>isTypeNodeKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return is the type guard <c>kind is TypeNodeSyntaxKind</c>; the port states it as <c>bool</c>
    /// (mapping rule (d)). The range test and the twenty-three keyword/JSDoc kinds are the reference's, in its order.
    /// </remarks>
    public static bool IsTypeNodeKind(SyntaxKind kind)
        => (kind >= SyntaxKind.FirstTypeNode && kind <= SyntaxKind.LastTypeNode)
            || kind == SyntaxKind.AnyKeyword
            || kind == SyntaxKind.UnknownKeyword
            || kind == SyntaxKind.NumberKeyword
            || kind == SyntaxKind.BigIntKeyword
            || kind == SyntaxKind.ObjectKeyword
            || kind == SyntaxKind.BooleanKeyword
            || kind == SyntaxKind.StringKeyword
            || kind == SyntaxKind.SymbolKeyword
            || kind == SyntaxKind.VoidKeyword
            || kind == SyntaxKind.UndefinedKeyword
            || kind == SyntaxKind.NeverKeyword
            || kind == SyntaxKind.IntrinsicKeyword
            || kind == SyntaxKind.ExpressionWithTypeArguments
            || kind == SyntaxKind.JSDocAllType
            || kind == SyntaxKind.JSDocUnknownType
            || kind == SyntaxKind.JSDocNullableType
            || kind == SyntaxKind.JSDocNonNullableType
            || kind == SyntaxKind.JSDocOptionalType
            || kind == SyntaxKind.JSDocFunctionType
            || kind == SyntaxKind.JSDocVariadicType;

    // ---------------------------------------------------------------------------------------------
    // Optional JSDoc properties, const assertions, constructors (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:11054-11060 — <c>isOptionalJSDocPropertyLikeTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's destructuring <c>const { isBracketed, typeExpression } = node</c> reads the two slots on the
    /// <c>JSDocPropertyLikeTag</c> class the guard established; the port names that class in the one cast. TS's
    /// <c>!!typeExpression &amp;&amp; typeExpression.type.kind === …</c> is the port's <c>is not null</c> pair.
    /// </remarks>
    public static bool IsOptionalJSDocPropertyLikeTag(Node node)
    {
        if (!IsJSDocPropertyLikeTag(node))
        {
            return false;
        }

        var propertyLikeTag = (JSDocPropertyLikeTag)node;
        return propertyLikeTag.IsBracketed
            || propertyLikeTag.TypeExpression is not null
                && propertyLikeTag.TypeExpression.Type!.Kind == SyntaxKind.JSDocOptionalType;
    }

    /// <summary>ported from: src/compiler/utilities.ts:11079-11085 — <c>isJSDocOptionalParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ParameterDeclaration</c>; every landed call site guards with
    /// <c>NodeTests.IsParameter(node)</c> first, so the port takes <c>Node</c> and states that same guard as an
    /// <c>is ParameterDeclaration</c> pattern instead of an unchecked cast. TS's <c>node.type &amp;&amp; …</c> is the
    /// port's <c>is { Kind: … }</c> property pattern and <c>.some(…)</c> is <c>Core.Some</c>.
    /// </remarks>
    public static bool IsJSDocOptionalParameter(Node node)
        => IsInJSFile(node)
            && node is ParameterDeclaration parameter
            && (parameter.Type is { Kind: SyntaxKind.JSDocOptionalType }
                || Core.Some(GetJSDocParameterTags(parameter), t => IsOptionalJSDocPropertyLikeTag(t)));

    /// <summary>ported from: src/compiler/utilities.ts:11449-11452 — <c>isConstAssertion</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>location.type</c> on the assertion union and <c>location.typeExpression</c> on a
    /// <c>JSDocTypeTag</c>; the port reads the same two slots through the kind switch below (the assertion union's two
    /// classes both declare <c>Type</c>, so one helper covers them). Both <c>&amp;&amp;</c>/<c>||</c> groupings are the
    /// reference's.
    /// </remarks>
    public static bool IsConstAssertion(Node node)
        => IsAssertionExpression(node) && IsConstTypeReference((Node)AssertionTypeOf(node)!)
            || NodeTests.IsJSDocTypeTag(node) && IsConstTypeReference(((JSDocTypeTag)node).TypeExpression!);

    /// <summary>The <c>location.type</c> read of <c>isAssertionExpression</c>'s two kinds (utilities.ts:11450).</summary>
    private static ITypeNode? AssertionTypeOf(Node node) => node switch
    {
        TypeAssertion assertion => assertion.Type,
        AsExpression assertion => assertion.Type,
        _ => null,
    };

    /// <summary>ported from: src/compiler/utilities.ts:11455-11462 — <c>findConstructorDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>ClassLikeDeclaration</c> and its one landed call site passes
    /// <c>location.Parent!</c>, a <c>Node</c>, so the port takes <c>Node</c> and reads <c>members</c> through the
    /// class-like switch below (the model's two class faces name different element types). <c>nodeIsPresent(body)</c> is
    /// <c>!NodeIsMissing(…)</c>, the port's <c>Utilities.NodeIsMissing</c>, which <c>nodeIsPresent</c> is defined as
    /// upstream (utilities.ts:1088-1090).
    /// </remarks>
    public static ConstructorDeclaration? FindConstructorDeclaration(Node node)
    {
        var members = MembersOfClassLike(node);
        if (members is null)
        {
            return null;
        }

        foreach (var member in members)
        {
            if (member.Kind == SyntaxKind.Constructor && !NodeIsMissing(((ConstructorDeclaration)member).Body))
            {
                return (ConstructorDeclaration)member;
            }
        }

        return null;
    }

    /// <summary>The <c>node.members</c> read of <see cref="FindConstructorDeclaration"/> (utilities.ts:11456).</summary>
    /// <remarks>PORT-NOTE: the class-like union's two class faces, each with its own <c>Members</c> element type.</remarks>
    private static NodeArray<IClassElement>? MembersOfClassLike(Node node) => node switch
    {
        ClassDeclaration declaration => declaration.Members,
        ClassExpression declaration => declaration.Members,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // utilitiesPublic.ts: JSDoc tags, entity names, accessors, type nodes (1106-1765)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1106-1108 — <c>hasJSDocParameterTags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!!getFirstJSDocTag(node, isJSDocParameterTag)</c> is the port's private
    /// <c>GetFirstJSDocTag</c> (declared in <c>Utilities/Utilities.JSDoc.cs</c>, same class) with the same find-first
    /// contract; the double negation becomes <c>is not null</c>.
    /// </remarks>
    public static bool HasJSDocParameterTags(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocParameterTag) is not null;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1111-1113 — <c>getJSDocAugmentsTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's generic <c>getFirstJSDocTag</c> narrows to <c>JSDocAugmentsTag</c> through its predicate; the port
    /// asks the same finder and states the narrowing as the <c>as</c> cast the predicate justifies.
    /// </remarks>
    public static JSDocAugmentsTag? GetJSDocAugmentsTag(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocAugmentsTag) as JSDocAugmentsTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1116-1118 — <c>getJSDocImplementsTags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's body is <c>getAllJSDocTags(node, isJSDocImplementsTag)</c>, and <c>getAllJSDocTags</c>
    /// (utilitiesPublic.ts:1284-1286) is <c>getJSDocTags(node).filter(predicate)</c>. The port has no
    /// <c>GetAllJSDocTags</c> member and no caller that needs one, so the reference's own <c>filter</c> is stated as
    /// <c>Core.Filter</c> over the landed <c>GetJSDocTags</c> and the projection into
    /// <c>List&lt;JSDocImplementsTag&gt;</c> is the narrowing the TS predicate performed.
    /// </remarks>
    public static List<JSDocImplementsTag> GetJSDocImplementsTags(Node node)
        => [.. (Core.Filter(GetJSDocTags(node), tag => NodeTests.IsJSDocImplementsTag(tag)) ?? []).Cast<JSDocImplementsTag>()];

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1185-1187 — <c>getJSDocThisTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the same finder-plus-cast shape as <see cref="GetJSDocAugmentsTag"/>.
    /// </remarks>
    public static JSDocThisTag? GetJSDocThisTag(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocThisTag) as JSDocThisTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1224-1231 — <c>getJSDocType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's local <c>tag</c> holds either a <c>JSDocTypeTag</c> or a <c>JSDocParameterTag</c> and only reads
    /// their shared <c>typeExpression</c>; the port states that shared read as one local, keeping the reference's
    /// <c>!tag &amp;&amp; isParameter(node)</c> order and its <c>find(…, tag =&gt; !!tag.typeExpression)</c>.
    /// </remarks>
    public static ITypeNode? GetJSDocType(Node node)
    {
        JSDocTypeExpression? typeExpression = null;
        if (GetFirstJSDocTag(node, NodeTests.IsJSDocTypeTag) is JSDocTypeTag typeTag)
        {
            typeExpression = typeTag.TypeExpression;
        }
        else if (NodeTests.IsParameter(node))
        {
            var parameterTag = Core.Find(
                GetJSDocParameterTags((ParameterDeclaration)node),
                (t, _) => t.TypeExpression is not null);
            typeExpression = parameterTag?.TypeExpression;
        }

        return typeExpression?.Type;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1318-1349 — <c>getEffectiveTypeParameterDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>DeclarationWithTypeParameters</c>, stated as <c>Node</c> plus the
    /// <c>typeParameters</c> pattern below. Its two <c>flatMap</c> calls become <c>Core.FlatMapToMutable</c> (the same
    /// skip-falsy contract) and its two returns of the module-level <c>emptyArray</c> become fresh empty lists — every
    /// landed caller iterates and none aliases, so the identity deviation is unobservable. TS reads
    /// <c>node.typeParameters</c> a second time inside the <c>canHaveIllegalTypeParameters</c> arm; the port reads the
    /// slot once because the first arm has already returned when it was non-null and the read has no side effect.
    /// </remarks>
    public static List<TypeParameterDeclaration> GetEffectiveTypeParameterDeclarations(Node node)
    {
        if (NodeTests.IsJSDocSignature(node))
        {
            if (NodeTests.IsJSDocOverloadTag(node.Parent!))
            {
                var jsDoc = GetJSDocRoot(node.Parent!);
                if (jsDoc is not null && Core.Length(((JSDoc)jsDoc).Tags) > 0)
                {
                    return Core.FlatMapToMutable<JSDocTag, TypeParameterDeclaration>(
                        ((JSDoc)jsDoc).Tags,
                        (tag, _) => NodeTests.IsJSDocTemplateTag(tag) ? ((JSDocTemplateTag)tag).TypeParameters : null);
                }
            }

            return [];
        }

        if (IsJSDocTypeAlias(node))
        {
            Debug.Assert(node.Parent!.Kind == SyntaxKind.JSDoc);
            return Core.FlatMapToMutable<JSDocTag, TypeParameterDeclaration>(
                ((JSDoc)node.Parent!).Tags,
                (tag, _) => NodeTests.IsJSDocTemplateTag(tag) ? ((JSDocTemplateTag)tag).TypeParameters : null);
        }

        var typeParameters = TypeParametersOf(node);
        if (typeParameters is not null)
        {
            return [.. typeParameters];
        }

        if (FactoryUtilities.CanHaveIllegalTypeParameters(node) && typeParameters is not null)
        {
            return [.. typeParameters];
        }

        if (IsInJSFile(node))
        {
            var declarations = GetJSDocTypeParameterDeclarations(node);
            if (declarations.Count != 0)
            {
                return declarations;
            }

            var typeTag = GetJSDocType(node);
            if (typeTag is not null && NodeTests.IsFunctionTypeNode((Node)typeTag) && ((FunctionTypeNode)typeTag).TypeParameters is not null)
            {
                return [.. ((FunctionTypeNode)typeTag).TypeParameters!];
            }
        }

        return [];
    }

    /// <summary>The <c>node.typeParameters</c> read of <see cref="GetEffectiveTypeParameterDeclarations"/> (utilitiesPublic.ts:1332).</summary>
    /// <remarks>
    /// PORT-NOTE: the model splits the slot over two faces plus the type-alias class
    /// (<c>IClassLikeDeclarationBase.TypeParameters</c>, <c>ISignatureDeclarationBase.TypeParameters</c>,
    /// <c>TypeAliasDeclaration.TypeParameters</c>), so the read is a three-arm pattern.
    /// </remarks>
    private static NodeArray<TypeParameterDeclaration>? TypeParametersOf(Node node) => node switch
    {
        IClassLikeDeclarationBase declaration => declaration.TypeParameters,
        ISignatureDeclarationBase declaration => declaration.TypeParameters,
        TypeAliasDeclaration declaration => declaration.TypeParameters,
        _ => null,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1449-1451 — <c>isJSDocPropertyLikeTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's type guard becomes <c>bool</c> (mapping rule (d)); the two <c>kind</c> comparisons are the
    /// model's kinds for <c>JSDocPropertyTag</c> and <c>JSDocParameterTag</c>, both of which derive from the port's
    /// <c>JSDocPropertyLikeTag</c> class.
    /// </remarks>
    public static bool IsJSDocPropertyLikeTag(Node node)
        => node.Kind == SyntaxKind.JSDocPropertyTag || node.Kind == SyntaxKind.JSDocParameterTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1651-1655 — <c>isEntityName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's type guard becomes <c>bool</c> (mapping rule (d)); the two kinds are unchanged.</remarks>
    public static bool IsEntityName(Node node)
        => node.Kind == SyntaxKind.QualifiedName || node.Kind == SyntaxKind.Identifier;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1748-1750 — <c>isAccessor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node &amp;&amp; (…)</c> tests the argument for null before reading <c>kind</c>; the port declares the
    /// parameter <c>Node?</c> and keeps that guard, because <c>Node?</c> is what the landed callers' arguments may be.
    /// </remarks>
    public static bool IsAccessor(Node? node)
        => node is not null && (node.Kind == SyntaxKind.GetAccessor || node.Kind == SyntaxKind.SetAccessor);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1765-1774 — <c>isMethodOrAccessor</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>switch</c> with an explicit <c>default: return false</c> is stated as the equivalent
    /// <c>or</c> pattern over the three kinds.
    /// </remarks>
    public static bool IsMethodOrAccessor(Node node)
        => node.Kind is SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1815-1817 — <c>isTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's type guard becomes <c>bool</c> (mapping rule (d)); the body delegates to the ported
    /// <see cref="IsTypeNodeKind"/> (utilities.ts:8330).
    /// </remarks>
    public static bool IsTypeNode(Node node) => IsTypeNodeKind(node.Kind);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2203-2205 — <c>isModuleOrEnumDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's type guard becomes <c>bool</c> (mapping rule (d)).</remarks>
    public static bool IsModuleOrEnumDeclaration(Node node)
        => node.Kind == SyntaxKind.ModuleDeclaration || node.Kind == SyntaxKind.EnumDeclaration;

    // ---------------------------------------------------------------------------------------------
    // Initializers and rest parameters (utilitiesPublic.ts:2573-2641)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2573-2575 — <c>hasInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>(node as HasInitializer).initializer</c> for the union <c>HasInitializer</c>
    /// (<c>HasExpressionInitializer | ForStatement | ForInStatement | ForOfStatement | JsxAttribute</c>). The model has no
    /// such interface (§12: one physical slot per class), so the read is a pattern over exactly those ten kinds;
    /// <c>!!</c> is the port's <c>is not null</c>. <c>PropertySignature</c> deliberately has no arm: TS's
    /// <c>HasIllegalExpressionInitializer</c> is not part of <c>HasInitializer</c>.
    /// </remarks>
    public static bool HasInitializer(Node node) => node switch
    {
        VariableDeclaration declaration => declaration.Initializer is not null,
        ParameterDeclaration declaration => declaration.Initializer is not null,
        BindingElement declaration => declaration.Initializer is not null,
        PropertyDeclaration declaration => declaration.Initializer is not null,
        PropertyAssignment declaration => declaration.Initializer is not null,
        EnumMember declaration => declaration.Initializer is not null,
        JsxAttribute declaration => declaration.Initializer is not null,
        ForStatement statement => statement.Initializer is not null,
        ForInStatement statement => statement.Initializer is not null,
        ForOfStatement statement => statement.Initializer is not null,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2633-2636 — <c>hasRestParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>SignatureDeclaration | JSDocSignature</c> and its
    /// <c>lastOrUndefined(s.parameters)</c> reads that union's one <c>parameters</c> property — which the port's model
    /// spells on two faces with different element types, hence the helper below. <c>!!last &amp;&amp; isRestParameter(last)</c>
    /// is the port's <c>is not null</c> pair.
    /// </remarks>
    public static bool HasRestParameter(Node signature)
    {
        var last = LastParameterOf(signature);
        return last is not null && IsRestParameter(last);
    }

    /// <summary>The <c>s.parameters</c> last-element read of <see cref="HasRestParameter"/> (utilitiesPublic.ts:2634).</summary>
    /// <remarks>
    /// PORT-NOTE: <c>JSDocSignature.Parameters</c> is <c>List&lt;JSDocParameterTag&gt;</c> and
    /// <c>ISignatureDeclarationBase.Parameters</c> is <c>NodeArray&lt;ParameterDeclaration&gt;</c>; the JSDoc arm comes
    /// first because that class is not a declaration face at all.
    /// </remarks>
    private static Node? LastParameterOf(Node node) => node switch
    {
        JSDocSignature signature => Core.LastOrUndefined(signature.Parameters),
        ISignatureDeclarationBase signature => Core.LastOrUndefined(signature.Parameters),
        _ => null,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2638-2641 — <c>isRestParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ParameterDeclaration | JSDocParameterTag</c>, stated as <c>Node</c>
    /// because that is what the landed caller (<c>getSignatureFromDeclaration</c>'s parameter loop) passes. TS reads the
    /// type off the tag or off the parameter and then reads <c>dotDotDotToken</c> unconditionally through a cast that is a
    /// no-op at runtime (the slot is absent on a tag); the port keeps that behaviour with an
    /// <c>is ParameterDeclaration</c> pattern whose false arm contributes nothing, exactly as an absent slot does.
    /// </remarks>
    public static bool IsRestParameter(Node node)
    {
        var type = ParameterTypeOf(node);
        return node is ParameterDeclaration parameter && parameter.DotDotDotToken is not null
            || type is not null && type.Kind == SyntaxKind.JSDocVariadicType;
    }

    /// <summary>The type-expression-or-type read of <see cref="IsRestParameter"/> (utilitiesPublic.ts:2639).</summary>
    private static ITypeNode? ParameterTypeOf(Node node) => node switch
    {
        JSDocParameterTag tag => tag.TypeExpression?.Type,
        ParameterDeclaration parameter => parameter.Type,
        _ => null,
    };
    // =============================================================================================
    // Second batch: the same family, for the checker files that landed after the first pass
    // (their `Utilities.<Name>` calls are CS0117 against this class in the whole-tree build, plus
    // the four members the fleet routed here: GetSemanticJsxChildren, GetTextOfJsxAttributeName,
    // IsComputedNonLiteralName, HasType).
    // =============================================================================================

    // ---------------------------------------------------------------------------------------------
    // Maps (utilities.ts:778-782)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:778-782 — <c>copyEntries</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameters are <c>ReadonlyMap&lt;K, V&gt;</c> and <c>Map&lt;K, V&gt;</c>. The port's map for
    /// symbol tables is <c>SymbolTable</c> (Model/SymbolTable.cs), which implements
    /// <c>IReadOnlyDictionary&lt;string, ISymbol&gt;</c> and indexes with a settable this[] — so the read face is the
    /// dictionary interface and the write face is the class. Its one caller
    /// (<c>Checker/Checker.TypeOfSymbols.cs</c>) passes member tables for both.
    /// </remarks>
    public static void CopyEntries(IReadOnlyDictionary<string, ISymbol> source, SymbolTable target)
    {
        foreach (var entry in source)
        {
            target[entry.Key] = entry.Value;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Property names, JSX text and children (utilities.ts:2359-2361, 6046-6057, 11132-11151, 11205-11207)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2354-2356 — <c>getNameFromIndexInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>IndexInfo</c> interface and its <c>info.declaration.parameters[0].name</c>
    /// read is the declaration-name union; the port's <c>IIndexInfo.Declaration</c> is the concrete
    /// <c>IndexSignatureDeclaration</c>, whose <c>Parameters</c> slot the <c>ISignatureDeclarationBase</c> face
    /// supplies. <c>declarationNameToString</c> is the landed <c>Utilities.DeclarationNameToString(Node?)</c>, and the
    /// reference's <c>undefined</c> arm stays <c>null</c>.
    /// </remarks>
    public static string? GetNameFromIndexInfo(IIndexInfo info)
        => info.Declaration is not null ? DeclarationNameToString(info.Declaration.Parameters![0].Name) : null;

    /// <summary>ported from: src/compiler/utilities.ts:655-657 — <c>isTransientSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>TransientSymbol</c>; the port answers <c>bool</c> (mapping rule (d)), which is
    /// what its callers feed to a cast (<c>Checker/Checker.CallResolution.cs</c> does `(ITransientSymbol)(… ? target :
    /// CloneSymbol(target))`).
    /// </remarks>
    public static bool IsTransientSymbol(ISymbol symbol) => (symbol.Flags & SymbolFlags.Transient) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:11205-11207 — <c>getNameFromImportAttribute</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ImportAttribute</c> node and its <c>name</c> is
    /// <c>Identifier | StringLiteral</c>; the port's model types the slot as <c>MemberExpression?</c>, so the
    /// <c>isIdentifier</c> guard is the port's <c>is Identifier</c> pattern and the other arm reads the literal's text
    /// through the cast the guard licenses. <c>__String</c> is <c>string</c> in the port, so no nominal wrapper is
    /// needed.
    /// </remarks>
    public static string GetNameFromImportAttribute(ImportAttribute node)
        => node.Name is Identifier identifier
            ? identifier.EscapedText!
            : EscapeLeadingUnderscores(((StringLiteral)node.Name!).Text!);

    /// <summary>ported from: src/compiler/utilities.ts:2359-2361 — <c>isComputedNonLiteralName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>name.expression</c> is read after the kind test narrows the union to
    /// <c>ComputedPropertyName</c>; the port's <c>&amp;&amp;</c> keeps the same short-circuit, so the cast only runs for
    /// that kind. <c>isStringOrNumericLiteralLike</c> is the ported utilitiesPublic predicate of the same name.
    /// </remarks>
    public static bool IsComputedNonLiteralName(Node name)
        => name.Kind == SyntaxKind.ComputedPropertyName
            && !IsStringOrNumericLiteralLike(((ComputedPropertyName)name).Expression!);

    /// <summary>ported from: src/compiler/utilities.ts:2385-2387 — <c>getTextOfPropertyName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Debug.checkDefined(tryGetTextOfPropertyName(name))</c> asserts the <c>__String</c> the try
    /// helper may not answer; the port's <c>Debug.CheckDefined</c> has the same contract, and
    /// <c>Utilities.TryGetTextOfPropertyName</c> is the already-landed half (it lives in the <c>Utilities</c> partial
    /// inside <c>Program/Program.ProgramDiagnostics.cs</c>).
    /// </remarks>
    public static string GetTextOfPropertyName(Node name) => Debug.CheckDefined(TryGetTextOfPropertyName(name));

    /// <summary>ported from: src/compiler/utilities.ts:6046-6057 — <c>getSemanticJsxChildren</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter and result are <c>readonly JsxChild[]</c>, a union of five node classes with no common
    /// port face beyond <c>Node</c>, so both are <c>Node</c> here and the filter's three arms are the reference's. The
    /// one caller (Checker/Checker.Relations.Entries.cs) reads <c>Core.Length</c> and iterates.
    /// </remarks>
    public static List<Node> GetSemanticJsxChildren(NodeArray<Node> children)
        => [.. Core.Filter(children, child => child.Kind switch
        {
            SyntaxKind.JsxExpression => ((JsxExpression)child).Expression is not null,
            SyntaxKind.JsxText => !((JsxText)child).ContainsOnlyTriviaWhiteSpaces,
            _ => true,
        })!.Cast<Node>()];

    /// <summary>ported from: src/compiler/utilities.ts:11132-11134 — <c>getTextOfJsxAttributeName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>JsxAttributeName</c> (<c>Identifier | JsxNamespacedName</c>), stated as
    /// <c>Node</c>; the <c>isIdentifier</c> guard is the port's <c>is Identifier</c> pattern.
    /// </remarks>
    public static string GetTextOfJsxAttributeName(Node node)
        => node is Identifier identifier ? IdText(identifier) : GetTextOfJsxNamespacedName((JsxNamespacedName)node);

    /// <summary>ported from: src/compiler/utilities.ts:11149-11151 — <c>getTextOfJsxNamespacedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference is a template literal; <c>idText</c> is the ported <see cref="IdText"/>.
    /// </remarks>
    public static string GetTextOfJsxNamespacedName(JsxNamespacedName node)
        => $"{IdText(node.Namespace!)}:{IdText(node.Name!)}";

    // ---------------------------------------------------------------------------------------------
    // Initializer-shape and type-annotation predicates (utilitiesPublic.ts:2564-2590)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2578-2590 — <c>hasOnlyExpressionInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return is the type guard <c>node is HasExpressionInitializer</c>; the port answers <c>bool</c>
    /// (mapping rule (d)) for the six kinds the guard names, in its order.
    /// </remarks>
    public static bool HasOnlyExpressionInitializer(Node node)
        => node.Kind is SyntaxKind.VariableDeclaration
            or SyntaxKind.Parameter
            or SyntaxKind.BindingElement
            or SyntaxKind.PropertyDeclaration
            or SyntaxKind.PropertyAssignment
            or SyntaxKind.EnumMember;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2564-2566 — <c>hasType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>(node as HasType).type</c> for the union <c>HasType</c> (fourteen members: the signature
    /// declarations, the five variable/property/parameter declarations, the five type-node wrappers, the two assertion
    /// kinds and the three JSDoc type nodes). The model has no such interface, so the port states the union as a
    /// pattern over its concrete classes and asks whether the slot is present — which is exactly what the reference's
    /// truthiness test asks for a node whose slot exists but is unset.
    /// </remarks>
    public static bool HasType(Node node) => node switch
    {
        ISignatureDeclarationBase declaration => declaration.Type is not null,
        VariableDeclaration declaration => declaration.Type is not null,
        ParameterDeclaration declaration => declaration.Type is not null,
        PropertySignature declaration => declaration.Type is not null,
        PropertyDeclaration declaration => declaration.Type is not null,
        TypePredicateNode declaration => declaration.Type is not null,
        ParenthesizedTypeNode declaration => declaration.Type is not null,
        TypeOperatorNode declaration => declaration.Type is not null,
        MappedTypeNode declaration => declaration.Type is not null,
        TypeAssertion declaration => declaration.Type is not null,
        AsExpression declaration => declaration.Type is not null,
        TypeAliasDeclaration declaration => declaration.Type is not null,
        JSDocTypeExpression declaration => declaration.Type is not null,
        JSDocNonNullableType declaration => declaration.Type is not null,
        JSDocNullableType declaration => declaration.Type is not null,
        _ => false,
    };

    // ---------------------------------------------------------------------------------------------
    // Type predicates, optionals, assignment kind, numeric names (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3085-3087 — <c>isIdentifierTypePredicate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows the <c>TypePredicate</c> union; the port answers <c>bool</c> (mapping rule (d)) and
    /// reads the <c>Kind</c> slot the generated <c>ITypePredicateBase</c> carries. TS's <c>predicate &amp;&amp; …</c> is the
    /// port's nullable parameter plus <c>is not null</c>.
    /// </remarks>
    public static bool IsIdentifierTypePredicate(ITypePredicateBase? predicate)
        => predicate is not null && predicate.Kind == TypePredicateKind.Identifier;

    /// <summary>ported from: src/compiler/utilities.ts:3090-3092 — <c>isThisTypePredicate</c>.</summary>
    /// <remarks>PORT-NOTE: the same shape as <see cref="IsIdentifierTypePredicate"/> with the This kind.</remarks>
    public static bool IsThisTypePredicate(ITypePredicateBase? predicate)
        => predicate is not null && predicate.Kind == TypePredicateKind.This;

    /// <summary>ported from: src/compiler/utilities.ts:11088-11100 — <c>isOptionalDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Declaration</c> — the port's <c>IDeclaration</c> — and its kind switch reads the
    /// <c>questionToken</c> slot through casts; the port states the same switch as class patterns, which is the union
    /// narrowing the casts performed. The JSDoc arms reuse <see cref="IsOptionalJSDocPropertyLikeTag"/> and the
    /// parameter arm reuses <see cref="IsJSDocOptionalParameter"/>, both landed above.
    /// </remarks>
    public static bool IsOptionalDeclaration(IDeclaration declaration) => declaration switch
    {
        PropertyDeclaration property => property.QuestionToken is not null,
        PropertySignature property => property.QuestionToken is not null,
        ParameterDeclaration parameter => parameter.QuestionToken is not null || IsJSDocOptionalParameter(parameter),
        JSDocPropertyTag tag => IsOptionalJSDocPropertyLikeTag(tag),
        JSDocParameterTag tag => IsOptionalJSDocPropertyLikeTag(tag),
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilities.ts:4294-4296 — <c>isPrototypePropertyAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>PrototypePropertyAssignment</c>; the port answers <c>bool</c> (mapping rule
    /// (d)). <c>getAssignmentDeclarationKind</c> is the ported <c>Utilities</c> member of the same name
    /// (Utilities/Utilities.JsExpando.cs), and the enum arm is <c>AssignmentDeclarationKind.PrototypeProperty</c>.
    /// </remarks>
    public static bool IsPrototypePropertyAssignment(Node node)
        => NodeTests.IsBinaryExpression(node) && GetAssignmentDeclarationKind(node) == AssignmentDeclarationKind.PrototypeProperty;

    /// <summary>ported from: src/compiler/utilities.ts:10900-10902 — <c>isInfinityOrNaNString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>string | __String</c>, both of which are <c>string</c> in the port
    /// (<c>__String</c> is a nominal alias), so the three comparisons are the reference's verbatim.
    /// </remarks>
    public static bool IsInfinityOrNaNString(string name)
        => name == "Infinity" || name == "-Infinity" || name == "NaN";

    // ---------------------------------------------------------------------------------------------
    // Modifier and decorator readers (utilities.ts:7211-7238; utilitiesPublic.ts:1024-1034, 1131-1177)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7236-7238 — <c>hasDecorators</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>HasDecorators</c>; the port answers <c>bool</c> (mapping rule (d)), which is
    /// all the one caller below reads.
    /// </remarks>
    public static bool HasDecorators(Node node) => HasSyntacticModifier(node, ModifierFlags.Decorator);

    /// <summary>ported from: src/compiler/utilities.ts:7211-7213 — <c>hasOverrideModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>hasEffectiveModifier</c> is declared in the sibling <c>Checker/Utilities.Checker.cs</c> (the same
    /// class) as <c>HasEffectiveModifier(Node, ModifierFlags)</c>.
    /// </remarks>
    public static bool HasOverrideModifier(Node node) => HasEffectiveModifier(node, ModifierFlags.Override);

    /// <summary>ported from: src/compiler/utilities.ts:7216-7218 — <c>hasAbstractModifier</c>.</summary>
    public static bool HasAbstractModifier(Node node) => HasSyntacticModifier(node, ModifierFlags.Abstract);

    /// <summary>ported from: src/compiler/utilities.ts:7221-7223 — <c>hasAmbientModifier</c>.</summary>
    public static bool HasAmbientModifier(Node node) => HasSyntacticModifier(node, ModifierFlags.Ambient);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1647-1649 — <c>isModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>Modifier</c>; the port answers <c>bool</c> (mapping rule (d)) and delegates to
    /// the ported <c>Utilities.IsModifierKind</c> (Utilities/Utilities.Parser.cs:275, utilitiesPublic.ts:1612-1634).
    /// <c>Program/Program.DiagnosticsAggregation.cs</c> keeps a private <c>IsModifier</c> of its own in the
    /// <c>Program</c> class — a different class, so neither is a duplicate of the other.
    /// </remarks>
    public static bool IsModifier(Node node) => IsModifierKind(node.Kind);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1024-1028 — <c>getDecorators</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>HasDecorators</c> and its result is <c>readonly Decorator[] | undefined</c>; the
    /// port filters the node's <c>modifiers</c> slot — which the model spreads over its classes, so it reads the same
    /// private <c>ModifiersOf</c> helper <c>Utilities/Utilities.Binder.cs</c> uses for
    /// <c>getSyntacticModifierFlagsNoCache</c> — and casts the filtered elements to the concrete <c>Decorator</c> the
    /// <c>IsDecorator</c> kind test licenses. The <c>undefined</c> arm stays <c>null</c>.
    /// </remarks>
    public static List<Decorator>? GetDecorators(Node node)
        => HasDecorators(node) ? [.. ModifiersOf(node)!.Cast<Decorator>()] : null;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1030-1034 — <c>getModifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>HasModifiers</c> and its result is <c>readonly Modifier[] | undefined</c>. The
    /// port reads the <c>modifiers</c> slot through the same private helper as <see cref="GetDecorators"/> and casts the
    /// filtered elements to the model's <c>ModifierToken</c> (Model/Token.cs), which is the concrete type
    /// <c>Utilities.IsModifierKind</c> licenses; <c>ModifierFlags.Modifier</c> is the enum's own mask.
    /// </remarks>
    public static List<ModifierToken>? GetModifiers(Node node)
        => HasSyntacticModifier(node, ModifierFlags.Modifier) ? [.. ModifiersOf(node)!.Cast<ModifierToken>()] : null;

    /// <summary>ported from: src/compiler/utilities.ts:7253-7275 — <c>getModifierFlagsWorker</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>node.modifierFlagsCache</c> is the model's <c>Node.ModifierFlagsCache</c> slot, which
    /// <c>Utilities/Utilities.Binder.cs</c> already writes with the same two bits; <c>getSyntacticModifierFlagsNoCache</c>,
    /// <c>selectSyntacticModifierFlags</c>, <c>selectEffectiveModifierFlags</c> and
    /// <c>getRawJSDocModifierFlagsNoCache</c> are the ported members below. <c>alwaysIncludeJSDoc</c> is kept although no
    /// ported caller passes it, because the reference's own signature has it and a later caller would otherwise have to
    /// change the shape.
    /// </remarks>
    private static ModifierFlags GetModifierFlagsWorker(Node node, bool includeJSDoc, bool alwaysIncludeJSDoc = false)
    {
        if (node.Kind >= SyntaxKind.FirstToken && node.Kind <= SyntaxKind.LastToken)
        {
            return ModifierFlags.None;
        }

        if ((node.ModifierFlagsCache & ModifierFlags.HasComputedFlags) == 0)
        {
            node.ModifierFlagsCache = GetSyntacticModifierFlagsNoCache(node) | ModifierFlags.HasComputedFlags;
        }

        if (alwaysIncludeJSDoc || (includeJSDoc && IsInJSFile(node)))
        {
            if ((node.ModifierFlagsCache & ModifierFlags.HasComputedJSDocModifiers) == 0 && node.Parent is not null)
            {
                node.ModifierFlagsCache |= GetRawJSDocModifierFlagsNoCache(node) | ModifierFlags.HasComputedJSDocModifiers;
            }

            return SelectEffectiveModifierFlags(node.ModifierFlagsCache);
        }

        return SelectSyntacticModifierFlags(node.ModifierFlagsCache);
    }

    /// <summary>ported from: src/compiler/utilities.ts:7312-7314 — <c>selectSyntacticModifierFlags</c> (file-local upstream).</summary>
    private static ModifierFlags SelectSyntacticModifierFlags(ModifierFlags flags)
        => flags & ModifierFlags.SyntacticModifiers;

    /// <summary>ported from: src/compiler/utilities.ts:7316-7319 — <c>selectEffectiveModifierFlags</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>&gt;&gt;&gt; 23</c> is an unsigned shift on a 32-bit value; the port states it as
    /// <c>(uint)… &gt;&gt; 23</c> and casts back to the enum, which is the reference's arithmetic exactly (the enum's
    /// storage is <c>uint</c>).
    /// </remarks>
    private static ModifierFlags SelectEffectiveModifierFlags(ModifierFlags flags)
        => (flags & ModifierFlags.NonCacheOnlyModifiers) | (ModifierFlags)((uint)(flags & ModifierFlags.JSDocCacheOnlyModifiers) >> 23);

    /// <summary>ported from: src/compiler/utilities.ts:7290-7308 — <c>getRawJSDocModifierFlagsNoCache</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!!node.parent &amp;&amp; !isParameter(node)</c> guard is the port's <c>Parent is not null</c> plus the
    /// <c>NodeTests.IsParameter</c> kind test; the five JSDoc tag probes and the deprecated probe keep the reference's
    /// order and nesting (the deprecated probe sits inside the parent guard, not the JS-file guard).
    /// </remarks>
    private static ModifierFlags GetRawJSDocModifierFlagsNoCache(Node node)
    {
        var flags = ModifierFlags.None;
        if (node.Parent is not null && !NodeTests.IsParameter(node))
        {
            if (IsInJSFile(node))
            {
                if (GetJSDocPublicTagNoCache(node) is not null) flags |= ModifierFlags.JSDocPublic;
                if (GetJSDocPrivateTagNoCache(node) is not null) flags |= ModifierFlags.JSDocPrivate;
                if (GetJSDocProtectedTagNoCache(node) is not null) flags |= ModifierFlags.JSDocProtected;
                if (GetJSDocReadonlyTagNoCache(node) is not null) flags |= ModifierFlags.JSDocReadonly;
                if (GetJSDocOverrideTagNoCache(node) is not null) flags |= ModifierFlags.JSDocOverride;
            }

            if (GetJSDocDeprecatedTagNoCache(node) is not null) flags |= ModifierFlags.Deprecated;
        }

        return flags;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7276-7278 — <c>getEffectiveModifierFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the cache-aware JSDoc path is the worker above; the landed callers
    /// (<c>Checker/Checker.TypeOfSymbols.cs</c>, <c>Checker/Checker.Relations.Identical.cs</c>) pass a declaration or a
    /// declaration node already cast to <c>Node</c>.
    /// </remarks>
    public static ModifierFlags GetEffectiveModifierFlags(Node node) => GetModifierFlagsWorker(node, /*includeJSDoc*/ true);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1131-1133 — <c>getJSDocPublicTagNoCache</c>.</summary>
    private static JSDocPublicTag? GetJSDocPublicTagNoCache(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocPublicTag, /*noCache*/ true) as JSDocPublicTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1141-1143 — <c>getJSDocPrivateTagNoCache</c>.</summary>
    private static JSDocPrivateTag? GetJSDocPrivateTagNoCache(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocPrivateTag, /*noCache*/ true) as JSDocPrivateTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1151-1153 — <c>getJSDocProtectedTagNoCache</c>.</summary>
    private static JSDocProtectedTag? GetJSDocProtectedTagNoCache(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocProtectedTag, /*noCache*/ true) as JSDocProtectedTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1161-1163 — <c>getJSDocReadonlyTagNoCache</c>.</summary>
    private static JSDocReadonlyTag? GetJSDocReadonlyTagNoCache(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocReadonlyTag, /*noCache*/ true) as JSDocReadonlyTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1165-1167 — <c>getJSDocOverrideTagNoCache</c>.</summary>
    private static JSDocOverrideTag? GetJSDocOverrideTagNoCache(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocOverrideTag, /*noCache*/ true) as JSDocOverrideTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1175-1177 — <c>getJSDocDeprecatedTagNoCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: its cache-aware twin <c>GetJSDocDeprecatedTag</c> is the landed member in
    /// <c>Checker/Utilities.Checker.cs</c>; this is the <c>noCache</c> half the effective-modifier path needs, and the
    /// reference does not export it either.
    /// </remarks>
    private static JSDocDeprecatedTag? GetJSDocDeprecatedTagNoCache(Node node)
        => GetFirstJSDocTag(node, NodeTests.IsJSDocDeprecatedTag, /*noCache*/ true) as JSDocDeprecatedTag;

    // ---------------------------------------------------------------------------------------------
    // BigInt spellings and checking (utilities.ts:10545-10575)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:10545-10549 — <c>parseValidBigInt</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns the object literal <c>{ negative, base10Value }</c> that satisfies the <c>PseudoBigInt</c>
    /// interface; the port returns the interface and constructs <see cref="PseudoBigIntValue"/> (declared below), which
    /// is the first concrete face of <c>IPseudoBigInt</c> in the tree — the checker's <c>(IPseudoBigInt)value</c> reads
    /// needed one. <c>text.slice(1)</c> is the port's range operator and <c>parsePseudoBigInt</c> is
    /// <c>PseudoBigInt.Parse</c> (Core/PseudoBigInt.cs).
    /// </remarks>
    public static IPseudoBigInt ParseValidBigInt(string text)
    {
        var negative = text.StartsWith('-');
        var base10Value = PseudoBigInt.Parse((negative ? text[1..] : text) + "n");
        return new PseudoBigIntValue { Negative = negative, Base10Value = base10Value };
    }

    /// <summary>ported from: src/compiler/utilities.ts:10557-10575 — <c>isValidBigIntString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds a scanner with <c>createScanner(ScriptTarget.ESNext, /*skipTrivia*/ false)</c>; the port's
    /// entry point is <c>Scanner.Create</c> (Lexer/Scanner.cs), whose optional arguments default to the same variant and
    /// error callback. TS's <c>scanner.setOnError(() =&gt; success = false)</c> becomes a three-parameter lambda because
    /// the port's <c>ErrorCallback</c> carries the message, its length and an argument. The four conjuncts, their order
    /// and the <c>TokenFlags.ContainsSeparator</c> exclusion are the reference's, and the round-trip arm reuses the
    /// ported <c>PseudoBigIntToString</c> (Checker/Utilities.Checker.cs) over the scanner's token value.
    /// </remarks>
    public static bool IsValidBigIntString(string s, bool roundTripOnly)
    {
        if (s == "")
        {
            return false;
        }

        var scanner = Scanner.Create(ScriptTarget.ESNext, /*skipTrivia*/ false);
        var success = true;
        scanner.SetOnError((_, _, _) => success = false);
        scanner.SetText(s + "n", /*start*/ null, /*length*/ null);
        var result = scanner.Scan();
        var negative = result == SyntaxKind.MinusToken;
        if (negative)
        {
            result = scanner.Scan();
        }

        var flags = scanner.GetTokenFlags();
        return success
            && result == SyntaxKind.BigIntLiteral
            && scanner.GetTokenEnd() == s.Length + 1
            && (flags & TokenFlags.ContainsSeparator) == 0
            && (!roundTripOnly
                || s == PseudoBigIntToString(new PseudoBigIntValue
                {
                    Negative = negative,
                    Base10Value = PseudoBigInt.Parse(scanner.GetTokenValue()!),
                }));
    }

    // ---------------------------------------------------------------------------------------------
    // The evaluator's result record (utilities.ts:11311-11313)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:11311-11313 — <c>evaluatorResult</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the object literal <c>{ value, isSyntacticallyString, resolvedOtherFiles, hasExternalReferences }</c>
    /// and its <c>EvaluatorResult&lt;T&gt;</c> is a type alias; the port's model has the interface
    /// <c>IEvaluatorResult&lt;T&gt;</c> (Model/NodeInterfaces.cs:902, the type of every <c>ITypeChecker.Evaluate*</c>
    /// slot) but no concrete face, so <see cref="EvaluatorResultValue{T}"/> below is that face and this member is the
    /// factory. The type parameter and its default are the reference's — the four slots are not.
    /// </remarks>
    public static IEvaluatorResult<T> EvaluatorResult<T>(
        T value,
        bool isSyntacticallyString = false,
        bool resolvedOtherFiles = false,
        bool hasExternalReferences = false)
        => new EvaluatorResultValue<T>
        {
            Value = value,
            IsSyntacticallyString = isSyntacticallyString,
            ResolvedOtherFiles = resolvedOtherFiles,
            HasExternalReferences = hasExternalReferences,
        };

    // ---------------------------------------------------------------------------------------------
    // String escaping (utilities.ts:6154-6212)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:6207-6212 — <c>escapeString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's second parameter is the literal union <c>CharacterCodes.doubleQuote | singleQuote | backtick</c>;
    /// the port's <c>CharacterCodes</c> is a static class of constants rather than an enum, so the parameter is
    /// <c>int?</c> and the three-way choice is the reference's. The replacement work is the reference's own
    /// <c>getReplacement</c>, stated below with the input string threaded through instead of JS's
    /// <c>String.replace</c> argument, and the three patterns are the port's <c>[GeneratedRegex]</c> partial methods
    /// (§9.7 keeps them off the runtime-compilation path).
    /// </remarks>
    public static string EscapeString(string s, int? quoteChar = null)
    {
        var escapedCharsRegExp = quoteChar == CharacterCodes.Backtick ? BacktickQuoteEscapedCharsRegExp()
            : quoteChar == CharacterCodes.SingleQuote ? SingleQuoteEscapedCharsRegExp()
            : DoubleQuoteEscapedCharsRegExp();
        return escapedCharsRegExp.Replace(s, match => GetReplacement(s, match));
    }

    /// <summary>ported from: src/compiler/utilities.ts:6154-6156 — <c>doubleQuoteEscapedCharsRegExp</c>.</summary>
    [GeneratedRegex("[\\\\\"\\u0000-\\u001f\\u2028\\u2029\\u0085]", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleQuoteEscapedCharsRegExp();

    /// <summary>ported from: src/compiler/utilities.ts:6157-6158 — <c>singleQuoteEscapedCharsRegExp</c>.</summary>
    [GeneratedRegex("[\\\\'\\u0000-\\u001f\\u2028\\u2029\\u0085]", RegexOptions.CultureInvariant)]
    private static partial Regex SingleQuoteEscapedCharsRegExp();

    /// <summary>ported from: src/compiler/utilities.ts:6159-6161 — <c>backtickQuoteEscapedCharsRegExp</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's comment (template strings preserve simple LF newlines but encode CRLF) is why the CRLF
    /// alternative comes first in the pattern; the order is kept.
    /// </remarks>
    [GeneratedRegex("\\r\\n|[\\\\`\\u0000-\\u0009\\u000b-\\u001f\\u2028\\u2029\\u0085]", RegexOptions.CultureInvariant)]
    private static partial Regex BacktickQuoteEscapedCharsRegExp();

    /// <summary>ported from: src/compiler/utilities.ts:6162-6177 — <c>escapedCharsMap</c> (file-local upstream).</summary>
    private static readonly Dictionary<string, string> EscapedCharsMap = new(StringComparer.Ordinal)
    {
        ["\t"] = "\\t",
        ["\v"] = "\\v",
        ["\f"] = "\\f",
        ["\b"] = "\\b",
        ["\r"] = "\\r",
        ["\n"] = "\\n",
        ["\\"] = "\\\\",
        ["\""] = "\\\"",
        ["'"] = "\\'",
        ["`"] = "\\`",
        ["\u2028"] = "\\u2028", // lineSeparator
        ["\u2029"] = "\\u2029", // paragraphSeparator
        ["\u0085"] = "\\u0085", // nextLine
        ["\r\n"] = "\\r\\n", // special case for CRLFs in backticks
    };

    /// <summary>ported from: src/compiler/utilities.ts:6179-6183 — <c>encodeUtf16EscapeSequence</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>charCode.toString(16).toUpperCase()</c> is the port's hex formatting, and
    /// <c>("0000" + hexCode).slice(-4)</c> is stated as the last four characters of the zero-padded spelling so a code
    /// point above the BMP keeps the reference's truncation rather than growing a fifth digit.
    /// </remarks>
    private static string EncodeUtf16EscapeSequence(int charCode)
    {
        var hexCharCode = charCode.ToString("x", CultureInfo.InvariantCulture).ToUpperInvariant();
        var paddedHexCode = ("0000" + hexCharCode)[^4..];
        return "\\u" + paddedHexCode;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6185-6205 — <c>getReplacement</c> (file-local upstream).</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>String.replace</c> passes (matched, offset, input) to the replacer; the port's <c>Match</c>
    /// carries the match and its index but not the input, so the input is threaded through as a parameter.
    /// <c>input.charCodeAt(offset + c.length)</c> answers NaN past the end and every comparison against NaN is false —
    /// the port's <c>-1</c> sentinel is that same "no digit follows" answer (the branch order and both escape spellings
    /// are the reference's).
    /// </remarks>
    private static string GetReplacement(string input, Match match)
    {
        var c = match.Value;
        if (c.Length != 0 && c[0] == CharacterCodes.NullCharacter)
        {
            var lookAhead = match.Index + match.Length < input.Length ? input[match.Index + match.Length] : -1;
            if (lookAhead >= CharacterCodes._0 && lookAhead <= CharacterCodes._9)
            {
                // If the null character is followed by digits, print as a hex escape to prevent the result from
                // parsing as an octal (which is forbidden in strict mode)
                return "\\x00";
            }

            // Otherwise, keep printing a literal \0 for the null character
            return "\\0";
        }

        return EscapedCharsMap.TryGetValue(c, out var escaped) ? escaped : EncodeUtf16EscapeSequence(c[0]);
    }

/// <summary>
/// The concrete face of <see cref="IPseudoBigInt"/> the port was missing.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS satisfies <c>PseudoBigInt</c> with an object literal at each construction site
/// (<c>parseValidBigInt</c>, utilities.ts:10548, is the first). C# needs a declared type, and the checker's literal
/// paths already cast a literal's value to the interface (<c>Checker/Checker.IndexAccess.cs</c>'s bigint arm), so this
/// is that type. Every deviation is nominal: the two slots and their names are the interface's.
/// </remarks>
internal sealed class PseudoBigIntValue : IPseudoBigInt
{
    public bool Negative { get; set; }

    public string? Base10Value { get; set; }
}

/// <summary>
/// The concrete face of <see cref="IEvaluatorResult{T}"/> the port was missing.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>evaluatorResult</c> returns an object literal typed by the <c>EvaluatorResult&lt;T&gt;</c> alias
/// (utilities.ts:11311-11313); the port's model declares the interface but no implementation, and the checker's
/// <c>ITypeChecker.Evaluate*</c> slots are typed <c>IEvaluatorResult&lt;object?&gt;</c>. This is that implementation; the
/// four slots and their names are the interface's.
/// </remarks>
internal sealed class EvaluatorResultValue<T> : IEvaluatorResult<T>
{
    public T? Value { get; set; }

    public bool IsSyntacticallyString { get; set; }

    public bool ResolvedOtherFiles { get; set; }

    public bool HasExternalReferences { get; set; }
}

}
