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

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1963-1966 — <c>isCallLikeOrFunctionLikeExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>CallLikeExpression | FunctionExpression | ArrowFunction</c>; the port answers
    /// <c>bool</c> (mapping rule (d)), which is what the two landed callers pass to <c>FindAncestor</c>. Both disjuncts are
    /// the sibling <c>Checker/Utilities.Checker.cs</c> members of the same names, in the reference's order.
    /// </remarks>
    public static bool IsCallLikeOrFunctionLikeExpression(Node node)
        => IsCallLikeExpression(node) || IsFunctionExpressionOrArrowFunction(node);

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

    /// <summary>ported from: src/compiler/types.ts:3536-3541 — the <c>typeParameters</c> slot of the
    /// <c>DeclarationWithTypeParameterChildren</c> union.</summary>
    /// <remarks>
    /// PORT-NOTE: that union is <c>SignatureDeclaration | ClassLikeDeclaration | InterfaceDeclaration |
    /// TypeAliasDeclaration | JSDocTemplateTag</c>, and C# must name each face: the two class kinds reach the slot
    /// through <see cref="IClassLikeDeclarationBase"/>, the function-like kinds through
    /// <see cref="ISignatureDeclarationBase"/>, and the remaining three are concrete classes. (In release-6.0
    /// <c>InterfaceDeclaration</c> extends <c>DeclarationStatement, JSDocContainer</c> — *not*
    /// <c>ClassLikeDeclarationBase</c>, because its <c>kind</c> is a single member — so an <c>is
    /// IClassLikeDeclarationBase</c> test cannot cover it.)
    /// <para>
    /// PORT-NOTE: one home for the read. It was declared three times under this name — here, on <c>Checker</c>
    /// (<c>Checker.Check.Symbols.cs</c>) and on <c>Program</c> (<c>Program.DiagnosticsAggregation.cs</c>) — with this
    /// copy missing the <c>InterfaceDeclaration</c> arm and none of them covering <c>JSDocTemplateTag</c>, which is
    /// what <c>docs/architecture.md</c> §15.15's "one name, one owner, one home" and its deciding question (a read whose
    /// reference declaration is in <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> belongs to <c>Utilities</c>) forbid;
    /// the other two now call this member. The missing
    /// face was not cosmetic: <c>getEffectiveTypeParameterDeclarations</c> answered "no type parameters" for every
    /// generic interface, so <c>getDeclaredTypeOfSymbol</c> built a non-generic type for it and <c>getGlobalType</c>
    /// failed its arity check — <c>globalArrayType</c>, <c>globalPromiseType</c> and their neighbours resolved to
    /// <c>emptyGenericType</c> and every <c>createArrayType</c> produced the empty object type.
    /// </para>
    /// </remarks>
    internal static NodeArray<TypeParameterDeclaration>? TypeParametersOf(Node node) => node switch
    {
        IClassLikeDeclarationBase declaration => declaration.TypeParameters,
        InterfaceDeclaration declaration => declaration.TypeParameters,
        ISignatureDeclarationBase declaration => declaration.TypeParameters,
        TypeAliasDeclaration declaration => declaration.TypeParameters,
        JSDocTemplateTag declaration => declaration.TypeParameters,
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
    // Call-shape and numeric helpers (utilities.ts:2701-2704, 10362-10370)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2701-2704 — <c>isSuperCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>SuperCall</c>; the port answers <c>bool</c> (mapping rule (d)), which is what
    /// both landed callers pass to a condition (Checker/Checker.CallResolution.cs, Checker/Checker.Check.Expressions.cs).
    /// </remarks>
    public static bool IsSuperCall(Node n)
        => n.Kind == SyntaxKind.CallExpression && ((CallExpression)n).Expression!.Kind == SyntaxKind.SuperKeyword;

    /// <summary>ported from: src/compiler/utilities.ts:10362-10370 — <c>minAndMax</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the fresh record <c>{ readonly min: number; readonly max: number }</c> over JS numbers, and
    /// it asserts a non-empty input. The port answers a value tuple of <c>int</c> — the one landed caller's <c>getValue</c>
    /// returns <c>int</c> (its <c>GetNumNonRestParameters</c>) and destructures the result with
    /// <c>var (minArgumentCount, maxNonRestParam) = …</c>, so the <c>ValueTuple</c> supplies both the <c>Deconstruct</c> and
    /// the element type its two consumers already use. The assert is the reference's <c>Debug.assert(arr.length !== 0)</c>.
    /// </remarks>
    public static (int Min, int Max) MinAndMax<T>(IList<T>? arr, Func<T, int> getValue)
    {
        Debug.Assert(Core.Length(arr) != 0);
        var min = getValue(arr![0]);
        var max = min;
        for (var i = 1; i < arr!.Count; i++)
        {
            var value = getValue(arr[i]);
            if (value < min)
            {
                min = value;
            }
            else if (value > max)
            {
                max = value;
            }
        }

        return (min, max);
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

    /// <summary>ported from: src/compiler/utilities.ts:10905-10907 — <c>isCatchClauseVariableDeclaration</c>.</summary>
    public static bool IsCatchClauseVariableDeclaration(Node node)
        => node.Kind == SyntaxKind.VariableDeclaration && node.Parent!.Kind == SyntaxKind.CatchClause;

    /// <summary>ported from: src/compiler/utilities.ts:11104-11108 — <c>isNonNullAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>AccessExpression</c>; the port answers <c>bool</c> (mapping rule (d)), which
    /// is all its one caller reads (<c>Checker.Inference.cs</c>'s <c>narrowTypeByDiscriminant</c>). The operand read is
    /// the union member <see cref="GetAccessExpressionExpression"/> reaches.
    /// </remarks>
    public static bool IsNonNullAccess(Node node)
        => IsAccessExpression(node) && NodeTests.IsNonNullExpression(GetAccessExpressionExpression((Expression)node));

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


    // ---------------------------------------------------------------------------------------------
    // The A1 low band — `utilities.ts` / `utilitiesPublic.ts` members (artifacts/order/A1-util-low.txt).
    //
    // PORT-NOTE: the band's members are the reference's own. The helpers they are defined in terms of, which no order
    // file assigns and no landed slice declares, are declared here too, exactly as the reference has them
    // (`isTypeOnlyImportDeclaration`/`isTypeOnlyExportDeclaration`, `isNonContextualKeyword`, `isScopeMarker`,
    // `isQuoteOrBacktick`, `formatJSDocLink`, `isCommonJSContainingModuleKind`, `isProtoSetter`, the `nodeIsDecorated`
    // cluster, `getFirstConstructorWithBody` and `createSingleLineStringWriter`): the file header's rule (f) makes a
    // file-local upstream helper a `private static` member of this class, and one name owns one home only if the
    // helper is declared by the slice that needs it. Each such member's own remark says so.
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7241-7243 — <c>getSelectedEffectiveModifierFlags</c>.</summary>
    public static ModifierFlags GetSelectedEffectiveModifierFlags(Node node, ModifierFlags flags)
        => GetEffectiveModifierFlags(node) & flags;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1536-1548 — <c>isTypeOnlyImportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>isTypeOnlyImportOrExportDeclaration</c> is defined in terms
    /// of it and no file declares it. The reference's <c>ImportSpecifier</c> arm reads
    /// <c>node.parent.parent.phaseModifier</c>, i.e. <c>ImportDeclaration.phaseModifier</c>, which the generated model
    /// does not carry (<c>Model/NodeClasses.cs</c> declares <c>PhaseModifier</c> on <c>ImportClause</c> alone); the arm
    /// therefore reads the <c>ImportClause</c>'s slot, which the parser fills from the same `type` token
    /// (<c>NodeFactory.cs:1892</c>). The other two arms are the reference's, verbatim.
    /// </remarks>
    public static bool IsTypeOnlyImportDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ImportSpecifier:
                return ((ImportSpecifier)node).IsTypeOnly
                    || (((ImportSpecifier)node).Parent!.Parent as ImportDeclaration)?.ImportClause?.PhaseModifier == SyntaxKind.TypeKeyword;
            case SyntaxKind.NamespaceImport:
                return ((ImportClause)((NamespaceImport)node).Parent!).PhaseModifier == SyntaxKind.TypeKeyword;
            case SyntaxKind.ImportClause:
                return ((ImportClause)node).PhaseModifier == SyntaxKind.TypeKeyword;
            case SyntaxKind.ImportEqualsDeclaration:
                return ((ImportEqualsDeclaration)node).IsTypeOnly;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1550-1560 — <c>isTypeOnlyExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>isTypeOnlyImportOrExportDeclaration</c> is defined in terms
    /// of it and no file declares it.
    /// </remarks>
    public static bool IsTypeOnlyExportDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ExportSpecifier:
                return ((ExportSpecifier)node).IsTypeOnly || ((ExportDeclaration)((ExportSpecifier)node).Parent!.Parent!).IsTypeOnly;
            case SyntaxKind.ExportDeclaration:
                return ((ExportDeclaration)node).IsTypeOnly
                    && ((ExportDeclaration)node).ModuleSpecifier is not null
                    && ((ExportDeclaration)node).ExportClause is null;
            case SyntaxKind.NamespaceExport:
                return ((ExportDeclaration)((NamespaceExport)node).Parent!).IsTypeOnly;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1562-1564 — <c>isTypeOnlyImportOrExportDeclaration</c>.</summary>
    public static bool IsTypeOnlyImportOrExportDeclaration(Node node)
        => IsTypeOnlyImportDeclaration(node) || IsTypeOnlyExportDeclaration(node);

    /// <summary>ported from: src/compiler/utilities.ts:6782-6790 — <c>getThisParameter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>SignatureDeclaration | JSDocSignature</c> union and the body reads
    /// <c>signature.parameters</c> only after the <c>isJSDocSignature</c> test — <c>JSDocSignature</c> is not an
    /// <c>SignatureDeclaration</c>, so the port's parameter is <see cref="INode"/> (the common face of the landed
    /// callers' <c>Node</c> and <c>IFunctionLikeDeclarationBase</c>) and the <c>ISignatureDeclarationBase</c> cast sits
    /// inside the test, in the reference's order.
    /// </remarks>
    public static ParameterDeclaration? GetThisParameter(INode signature)
    {
        // callback tags do not currently support this parameters
        if (signature is not JSDocSignature)
        {
            var parameters = ((ISignatureDeclarationBase)signature).Parameters;
            if (parameters is not null && parameters.Count != 0)
            {
                var thisParameter = parameters[0];
                if (ParameterIsThisKeyword(thisParameter))
                {
                    return thisParameter;
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3789-3791 — <c>isInternalModuleImportEqualsDeclaration</c>.</summary>
    public static bool IsInternalModuleImportEqualsDeclaration(Node node)
        => node.Kind == SyntaxKind.ImportEqualsDeclaration
            && ((ImportEqualsDeclaration)node).ModuleReference!.Kind != SyntaxKind.ExternalModuleReference;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2116-2128 — <c>isIterationStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's two type-guard overloads (the <c>false</c>-only one answering
    /// <c>IterationStatement</c> and the <c>boolean</c> one answering <c>IterationStatement | LabeledStatement</c>)
    /// collapse into this one member: C# has no type predicates (mapping rule (d)) and every landed caller passes the
    /// flag explicitly and tests the result for truth.
    /// </remarks>
    public static bool IsIterationStatement(Node node, bool lookInLabeledStatements)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
                return true;
            case SyntaxKind.LabeledStatement:
                return lookInLabeledStatements && IsIterationStatement((Node)((LabeledStatement)node).Statement!, lookInLabeledStatements);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:1300-1303 — <c>moduleExportNameTextUnescaped</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ModuleExportName</c> union (<c>Identifier | StringLiteral</c>); the port
    /// takes <c>Node?</c> because the landed callers pass <c>(Node?)specifier.PropertyName ?? specifier.Name</c>, and
    /// reads the same two slots the reference does.
    /// </remarks>
    public static string ModuleExportNameTextUnescaped(Node? node)
        => node!.Kind == SyntaxKind.StringLiteral
            ? ((StringLiteral)node).Text!
            : UnescapeLeadingUnderscores(((Identifier)node).EscapedText!);

    /// <summary>The single-line writer <c>usingSingleLineStringWriter</c> and the checker's <c>*ToStringWorker</c> pair share.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares <c>const stringWriter = createSingleLineStringWriter()</c> (utilities.ts:668) as a
    /// module-level singleton; the port states it as the static field the same closure would capture.
    /// </remarks>
    private static readonly IEmitTextWriter s_stringWriter = new SingleLineStringWriter();

    /// <summary>ported from: src/compiler/utilities.ts:670-703 — <c>createSingleLineStringWriter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's object literal is the port's <see cref="SingleLineStringWriter"/>. TS leaves
    /// <c>nonEscapingWrite</c> undefined (it is optional in <c>EmitTextWriter</c>); the port's generated interface
    /// declares it non-optional, so the implementation writes the text like the rest of the <c>write*</c> family.
    /// </remarks>
    private static IEmitTextWriter CreateSingleLineStringWriter() => new SingleLineStringWriter();

    /// <summary>ported from: src/compiler/utilities.ts:785-795 — <c>usingSingleLineStringWriter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>action: (writer: EmitTextWriter) =&gt; void</c> is the port's
    /// <c>Func&lt;IEmitTextWriter, IEmitTextWriter&gt;</c>: every landed caller passes one of the checker's
    /// <c>*ToStringWorker</c> local functions, whose return type is the writer they were handed (each of those is
    /// <c>IEmitTextWriter Worker(IEmitTextWriter writer)</c>), and C# has no implicit conversion discarding a
    /// non-void result in a method-group conversion.
    /// </remarks>
    public static string UsingSingleLineStringWriter(Func<IEmitTextWriter, IEmitTextWriter> action)
    {
        var oldString = s_stringWriter.GetText();
        try
        {
            action(s_stringWriter);
            return s_stringWriter.GetText();
        }
        finally
        {
            s_stringWriter.Clear();
            s_stringWriter.WriteKeyword(oldString);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:2129-2131 — <c>isExternalModuleAugmentation</c>.</summary>
    public static bool IsExternalModuleAugmentation(Node node)
        => IsAmbientModule(node) && IsModuleAugmentationExternal((ModuleDeclaration)node);

    /// <summary>ported from: src/compiler/utilities.ts:3631-3701 — <c>isExpressionNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>case SyntaxKind.Identifier:</c> falls through into the literal group; the port spells that out
    /// as <c>goto case</c>, which is the same control flow. TS's two JSDoc member-name loops reassign their parameter;
    /// the port does the same.
    /// </remarks>
    public static bool IsExpressionNode(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.SuperKeyword:
            case SyntaxKind.NullKeyword:
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.ElementAccessExpression:
            case SyntaxKind.CallExpression:
            case SyntaxKind.NewExpression:
            case SyntaxKind.TaggedTemplateExpression:
            case SyntaxKind.AsExpression:
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.SatisfiesExpression:
            case SyntaxKind.NonNullExpression:
            case SyntaxKind.ParenthesizedExpression:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.VoidExpression:
            case SyntaxKind.DeleteExpression:
            case SyntaxKind.TypeOfExpression:
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
            case SyntaxKind.BinaryExpression:
            case SyntaxKind.ConditionalExpression:
            case SyntaxKind.SpreadElement:
            case SyntaxKind.TemplateExpression:
            case SyntaxKind.OmittedExpression:
            case SyntaxKind.JsxElement:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxFragment:
            case SyntaxKind.YieldExpression:
            case SyntaxKind.AwaitExpression:
                return true;
            case SyntaxKind.MetaProperty:
                // `import.defer` in `import.defer(...)` is not an expression
                return !IsImportCall(node.Parent!) || !ReferenceEquals(((CallExpression)node.Parent!).Expression, node);
            case SyntaxKind.ExpressionWithTypeArguments:
                return !NodeTests.IsHeritageClause(node.Parent!) && !NodeTests.IsJSDocAugmentsTag(node.Parent!);
            case SyntaxKind.QualifiedName:
                while (node.Parent!.Kind == SyntaxKind.QualifiedName)
                {
                    node = node.Parent!;
                }
                return node.Parent!.Kind == SyntaxKind.TypeQuery
                    || IsJSDocLinkLike(node.Parent!)
                    || NodeTests.IsJSDocNameReference(node.Parent!)
                    || NodeTests.IsJSDocMemberName(node.Parent!)
                    || IsJSXTagName(node);
            case SyntaxKind.JSDocMemberName:
                while (NodeTests.IsJSDocMemberName(node.Parent!))
                {
                    node = node.Parent!;
                }
                return node.Parent!.Kind == SyntaxKind.TypeQuery
                    || IsJSDocLinkLike(node.Parent!)
                    || NodeTests.IsJSDocNameReference(node.Parent!)
                    || NodeTests.IsJSDocMemberName(node.Parent!)
                    || IsJSXTagName(node);
            case SyntaxKind.PrivateIdentifier:
                return NodeTests.IsBinaryExpression(node.Parent!)
                    && ReferenceEquals(((BinaryExpression)node.Parent!).Left, node)
                    && ((BinaryExpression)node.Parent!).OperatorToken!.Kind == SyntaxKind.InKeyword;
            case SyntaxKind.Identifier:
                if (node.Parent!.Kind == SyntaxKind.TypeQuery
                    || IsJSDocLinkLike(node.Parent!)
                    || NodeTests.IsJSDocNameReference(node.Parent!)
                    || NodeTests.IsJSDocMemberName(node.Parent!)
                    || IsJSXTagName(node))
                {
                    return true;
                }
                // falls through
                goto case SyntaxKind.NumericLiteral;
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.BigIntLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
            case SyntaxKind.ThisKeyword:
                return IsInExpressionContext(node);
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:2653-2655 — <c>isDeclarationReadonly</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>Declaration</c> face; the port's is <see cref="IDeclaration"/> because two
    /// landed callers cast to it explicitly, and the two helpers it reads take the <c>Node</c> face (the explicit
    /// downcast is the same one the callers already write).
    /// </remarks>
    public static bool IsDeclarationReadonly(IDeclaration declaration)
        => (GetCombinedModifierFlags((Node)declaration) & ModifierFlags.Readonly) != 0
            && !IsParameterPropertyDeclaration((Node)declaration, ((Node)declaration).Parent!);

    /// <summary>ported from: src/compiler/utilities.ts:2152-2154 — <c>isCommonJSContainingModuleKind</c>.</summary>
    /// <remarks>PORT-NOTE: not an entry of the assignment, but <c>isEffectiveExternalModule</c> is defined in terms of it.</remarks>
    private static bool IsCommonJSContainingModuleKind(ModuleKind kind)
        => kind == ModuleKind.CommonJS || ModuleKind.Node16 <= kind && kind <= ModuleKind.NodeNext;

    /// <summary>ported from: src/compiler/utilities.ts:2157-2159 — <c>isEffectiveExternalModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isExternalModule</c> is <c>parser.ts:1386</c>, whose landed port is
    /// <c>Parser.IsExternalModule</c> (the checker slices already call it that way); <c>getEmitModuleKind</c>'s
    /// <c>CompilerOptions</c> face is the generated <see cref="ICompilerOptions"/>.
    /// </remarks>
    public static bool IsEffectiveExternalModule(SourceFile node, ICompilerOptions compilerOptions)
        => Parser.IsExternalModule(node)
            || (IsCommonJSContainingModuleKind(GetEmitModuleKind(compilerOptions)) && node.CommonJsModuleIndicator is not null);

    /// <summary>ported from: src/compiler/utilities.ts:3152-3154 — <c>getContainingFunctionOrClassStaticBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>SignatureDeclaration | ClassStaticBlockDeclaration</c> union, which has no common
    /// port face that is not <c>Node</c> (the erased <c>SignatureDeclaration</c> name does not exist in the port); the
    /// landed callers only test the result with <c>NodeTests.IsClassStaticBlockDeclaration</c> and compare it to null.
    /// </remarks>
    public static Node? GetContainingFunctionOrClassStaticBlock(Node node)
        => FindAncestor(node.Parent, IsFunctionLikeOrClassStaticBlockDeclaration);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2538-2540 — <c>isSetAccessor</c>.</summary>
    public static bool IsSetAccessor(Node node) => node.Kind == SyntaxKind.SetAccessor;

    /// <summary>ported from: src/compiler/utilities.ts:5286-5288 — <c>isNonContextualKeyword</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>isStringANonContextualKeyword</c> is defined in terms of it and
    /// no file declares the port of <c>isContextualKeyword</c> (types.ts); the reference's
    /// <c>FirstContextualKeyword &lt;= token &amp;&amp; token &lt;= LastContextualKeyword</c> is the port's own pair of
    /// <c>SyntaxKind</c> aliases (<c>Model/SyntaxKind.cs:411-412</c>).
    /// </remarks>
    private static bool IsNonContextualKeyword(SyntaxKind token)
        => IsKeyword(token) && !(SyntaxKind.FirstContextualKeyword <= token && token <= SyntaxKind.LastContextualKeyword);

    /// <summary>ported from: src/compiler/utilities.ts:5291-5294 — <c>isStringANonContextualKeyword</c>.</summary>
    public static bool IsStringANonContextualKeyword(string name)
    {
        var token = ScannerUtilities.StringToToken(name);
        return token is not null && IsNonContextualKeyword(token.Value);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3912-3914 — <c>isSingleOrDoubleQuote</c>.</summary>
    public static bool IsSingleOrDoubleQuote(int charCode)
        => charCode == CharacterCodes.SingleQuote || charCode == CharacterCodes.DoubleQuote;

    /// <summary>ported from: src/compiler/utilities.ts:3525-3534 — <c>nodeIsDecorated</c> (the three overloads at 3525/3527/3529 and the implementation at 3531-3534).</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment. The reference declares three type-guard overloads plus the
    /// implementation; the port keeps one member with TS's optional <c>parent</c>/<c>grandparent</c> (rule (d): the
    /// guards only narrow) — the three-argument and four-argument landed calls bind to it directly, and the reference's
    /// two-argument overload is the same call with the defaults.
    /// </remarks>
    public static bool NodeIsDecorated(bool useLegacyDecorators, Node node, Node? parent = null, Node? grandparent = null)
        => HasDecorators(node) && NodeCanBeDecorated(useLegacyDecorators, node, parent!, grandparent!);

    /// <summary>ported from: src/compiler/utilities.ts:3536-3545 — <c>nodeOrChildIsDecorated</c>.</summary>
    /// <remarks>PORT-NOTE: not an entry of the assignment; <c>childIsDecorated</c> is defined in terms of it.</remarks>
    public static bool NodeOrChildIsDecorated(bool useLegacyDecorators, Node node, Node? parent = null, Node? grandparent = null)
        => NodeIsDecorated(useLegacyDecorators, node, parent!, grandparent!)
            || ChildIsDecorated(useLegacyDecorators, node, parent!);

    /// <summary>ported from: src/compiler/utilities.ts:3548-3565 — <c>childIsDecorated</c>.</summary>
    /// <remarks>PORT-NOTE: not an entry of the assignment; <c>classOrConstructorParameterIsDecorated</c> is defined in terms of it.</remarks>
    public static bool ChildIsDecorated(bool useLegacyDecorators, Node node, Node? parent = null)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ClassDeclaration:
                return Core.Some(((IClassLikeDeclarationBase)node).Members, m => NodeOrChildIsDecorated(useLegacyDecorators, (Node)m, node, parent!));
            case SyntaxKind.ClassExpression:
                return !useLegacyDecorators && Core.Some(((IClassLikeDeclarationBase)node).Members, m => NodeOrChildIsDecorated(useLegacyDecorators, (Node)m, node, parent!));
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.Constructor:
                return Core.Some(((ISignatureDeclarationBase)node).Parameters, p => NodeIsDecorated(useLegacyDecorators, p, node, parent!));
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6759-6761 — <c>getFirstConstructorWithBody</c>.</summary>
    /// <remarks>PORT-NOTE: not an entry of the assignment; <c>classOrConstructorParameterIsDecorated</c> is defined in terms of it.</remarks>
    public static ConstructorDeclaration? GetFirstConstructorWithBody(Node node)
        => (ConstructorDeclaration?)Core.Find(
            ((IClassLikeDeclarationBase)node).Members,
            (member, _) => NodeTests.IsConstructorDeclaration((Node)member) && NodeIsPresent(((IFunctionLikeDeclarationBase)member).Body));

    /// <summary>ported from: src/compiler/utilities.ts:3569-3573 — <c>classOrConstructorParameterIsDecorated</c>.</summary>
    public static bool ClassOrConstructorParameterIsDecorated(bool useLegacyDecorators, Node node)
    {
        if (NodeIsDecorated(useLegacyDecorators, node)) return true;
        var constructor = GetFirstConstructorWithBody(node);
        return constructor is not null && ChildIsDecorated(useLegacyDecorators, constructor, node);
    }

    /// <summary>ported from: src/compiler/utilities.ts:6277-6280 — <c>isIntrinsicJsxName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>__String | string</c> is the port's <c>string</c> (both landed callers pass a <c>string</c>,
    /// and <c>__String</c> erases to it); <c>charCodeAt(0)</c> is <c>ScannerUtilities.CharCodeAt</c>, which answers
    /// <c>-1</c> where JS answers <c>NaN</c> — both fail every comparison below.
    /// </remarks>
    public static bool IsIntrinsicJsxName(string name)
    {
        var ch = ScannerUtilities.CharCodeAt(name, 0);
        return (ch >= CharacterCodes.a && ch <= CharacterCodes.z) || name.Contains('-');
    }

    /// <summary>ported from: src/compiler/utilities.ts:3819-3821 — <c>isInJSDoc</c>.</summary>
    public static bool IsInJSDoc(Node? node) => node is not null && (node.Flags & NodeFlags.JSDoc) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:5073-5076 — <c>isDeclarationName</c>.</summary>
    public static bool IsDeclarationName(Node name)
        => !NodeTests.IsSourceFile(name)
            && !IsBindingPattern(name)
            && IsDeclaration(name.Parent!)
            && name.Parent is INamedDeclaration { Name: not null } declaration
            && ReferenceEquals(declaration.Name, name);

    /// <summary>ported from: src/compiler/utilities.ts:3471-3520 — <c>nodeCanBeDecorated</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's four type-guard overloads (the two-argument <c>ClassDeclaration</c> one answering
    /// <c>true</c>, the <c>ClassExpression</c> one, the <c>ClassElement</c> one and the general one) collapse into this
    /// member with TS's optional <c>parent</c>/<c>grandparent</c>: every landed caller passes all four arguments, and the
    /// <c>NodeIsDecorated</c>/<c>ChildIsDecorated</c> helpers below pass what the reference's two- and three-argument
    /// overloads pass. The function-like body test is <c>IFunctionLikeDeclarationBase.Body</c> (the port's face for the
    /// erased <c>FunctionLikeDeclaration</c>).
    /// </remarks>
    public static bool NodeCanBeDecorated(bool useLegacyDecorators, Node node, Node? parent = null, Node? grandparent = null)
    {
        // private names cannot be used with decorators yet
        if (useLegacyDecorators && IsNamedDeclaration(node) && node is INamedDeclaration { Name: not null } named && NodeTests.IsPrivateIdentifier(named.Name))
        {
            return false;
        }

        switch (node.Kind)
        {
            case SyntaxKind.ClassDeclaration:
                // class declarations are valid targets
                return true;

            case SyntaxKind.ClassExpression:
                // class expressions are valid targets for native decorators
                return !useLegacyDecorators;

            case SyntaxKind.PropertyDeclaration:
                // property declarations are valid if their parent is a class declaration.
                return parent is not null
                    && (useLegacyDecorators
                        ? NodeTests.IsClassDeclaration(parent)
                        : IsClassLike(parent) && !HasAbstractModifier(node) && !HasAmbientModifier(node));

            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.MethodDeclaration:
                // if this method has a body and its parent is a class declaration, this is a valid target.
                return ((IFunctionLikeDeclarationBase)node).Body is not null
                    && parent is not null
                    && (useLegacyDecorators ? NodeTests.IsClassDeclaration(parent) : IsClassLike(parent));

            case SyntaxKind.Parameter:
                // TODO(rbuckton): Parameter decorator support for ES decorators must wait until it is standardized
                if (!useLegacyDecorators) return false;
                // if the parameter's parent has a body and its grandparent is a class declaration, this is a valid target.
                return parent is not null
                    && ((IFunctionLikeDeclarationBase)parent).Body is not null
                    && (parent.Kind == SyntaxKind.Constructor
                        || parent.Kind == SyntaxKind.MethodDeclaration
                        || parent.Kind == SyntaxKind.SetAccessor)
                    && !ReferenceEquals(GetThisParameter(parent), node)
                    && grandparent is not null
                    && grandparent.Kind == SyntaxKind.ClassDeclaration;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7180-7182 — <c>hasEffectiveModifiers</c>.</summary>
    public static bool HasEffectiveModifiers(Node node)
        => GetEffectiveModifierFlags(node) != ModifierFlags.None;

    /// <summary>ported from: src/compiler/utilities.ts:4349-4374 — <c>tryGetModuleSpecifierFromDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>CanHaveModuleSpecifier | JSDocImportTag</c> union, whose port face is the
    /// widened <c>Node</c> the caller (<c>Utilities.CanHaveModuleSpecifier</c>) narrows to; the result is the
    /// <c>StringLiteralLike</c> union, which the port's model spells <see cref="LiteralExpression"/>
    /// (<c>StringLiteral</c> and <c>NoSubstitutionTemplateLiteral</c> are its only members, both deriving from it), so
    /// the landed <c>as LiteralExpression</c> is the identity and <c>?.Text</c> reads the reference's <c>.text</c>.
    /// The <c>findAncestor(node.initializer, …)</c> arm answers <c>arguments[0]</c>, whose static face is
    /// <c>Expression</c>.
    /// </remarks>
    public static LiteralExpression? TryGetModuleSpecifierFromDeclaration(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.BindingElement:
                var initializer = node.Kind == SyntaxKind.VariableDeclaration
                    ? ((VariableDeclaration)node).Initializer
                    : ((BindingElement)node).Initializer;
                return FindAncestor(initializer, n => IsRequireCall(n, /*requireStringLiteralLikeArgument*/ true)) is CallExpression require
                    ? (LiteralExpression?)Core.FirstOrUndefined(require.Arguments)
                    : null;
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.JSDocImportTag:
                var moduleSpecifier = node.Kind == SyntaxKind.ImportDeclaration
                    ? ((ImportDeclaration)node).ModuleSpecifier
                    : node.Kind == SyntaxKind.ExportDeclaration
                        ? ((ExportDeclaration)node).ModuleSpecifier
                        : ((JSDocImportTag)node).ModuleSpecifier;
                return Core.TryCast<LiteralExpression, Node>(moduleSpecifier, IsStringLiteralLike);
            case SyntaxKind.ImportEqualsDeclaration:
                return Core.TryCast<LiteralExpression, Node>(
                    Core.TryCast<ExternalModuleReference, Node>(((ImportEqualsDeclaration)node).ModuleReference, NodeTests.IsExternalModuleReference)?.Expression,
                    IsStringLiteralLike);
            case SyntaxKind.ImportClause:
            case SyntaxKind.NamespaceExport:
                var parentWithSpecifier = node.Parent!;
                return Core.TryCast<LiteralExpression, Node>(
                    parentWithSpecifier.Kind == SyntaxKind.ImportDeclaration
                        ? ((ImportDeclaration)parentWithSpecifier).ModuleSpecifier
                        : ((ExportDeclaration)parentWithSpecifier).ModuleSpecifier,
                    IsStringLiteralLike);
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ExportSpecifier:
                var grandparentWithSpecifier = node.Parent!.Parent!;
                return Core.TryCast<LiteralExpression, Node>(
                    grandparentWithSpecifier.Kind == SyntaxKind.ImportDeclaration
                        ? ((ImportDeclaration)grandparentWithSpecifier).ModuleSpecifier
                        : ((ExportDeclaration)grandparentWithSpecifier).ModuleSpecifier,
                    IsStringLiteralLike);
            case SyntaxKind.ImportSpecifier:
                return Core.TryCast<LiteralExpression, Node>(((ImportDeclaration)node.Parent!.Parent!.Parent!).ModuleSpecifier, IsStringLiteralLike);
            case SyntaxKind.ImportType:
                return IsLiteralImportTypeNode(node)
                    ? (LiteralExpression?)((LiteralTypeNode)((ImportTypeNode)node).Argument!).Literal
                    : null;
            default:
                Debug.AssertNever(node);
                return null;
        }
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2629-2631 — <c>isJSDocLinkLike</c>.</summary>
    public static bool IsJSDocLinkLike(Node node)
        => node.Kind == SyntaxKind.JSDocLink || node.Kind == SyntaxKind.JSDocLinkCode || node.Kind == SyntaxKind.JSDocLinkPlain;

    /// <summary>ported from: src/compiler/utilities.ts:7587-7589 — <c>isInstanceOfExpression</c>.</summary>
    public static bool IsInstanceOfExpression(Node node)
        => NodeTests.IsBinaryExpression(node) && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.InstanceOfKeyword;

    /// <summary>ported from: src/compiler/utilities.ts:7403-7405 — <c>isBinaryLogicalOperator</c>.</summary>
    public static bool IsBinaryLogicalOperator(SyntaxKind token)
        => token == SyntaxKind.BarBarToken || token == SyntaxKind.AmpersandAmpersandToken;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1503-1511 — <c>isLiteralExpressionOfObject</c>.</summary>
    public static bool IsLiteralExpressionOfObject(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ObjectLiteralExpression:
            case SyntaxKind.ArrayLiteralExpression:
            case SyntaxKind.RegularExpressionLiteral:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ClassExpression:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5255-5264 — <c>getAncestor</c>.</summary>
    public static Node? GetAncestor(Node? node, SyntaxKind kind)
    {
        while (node is not null)
        {
            if (node.Kind == kind)
            {
                return node;
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3618-3628 — <c>isJSXTagName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference reads <c>(parent as JsxOpeningLikeElement).tagName</c>; the three jsx element classes
    /// share no port base that declares <c>TagName</c>, so the arm switches on the kind it already tested.
    /// </remarks>
    public static bool IsJSXTagName(Node node)
    {
        var parent = node.Parent!;
        return parent.Kind switch
        {
            SyntaxKind.JsxOpeningElement => ReferenceEquals(((JsxOpeningElement)parent).TagName, node),
            SyntaxKind.JsxSelfClosingElement => ReferenceEquals(((JsxSelfClosingElement)parent).TagName, node),
            SyntaxKind.JsxClosingElement => ReferenceEquals(((JsxClosingElement)parent).TagName, node),
            _ => false,
        };
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2542-2544 — <c>isGetAccessor</c>.</summary>
    public static bool IsGetAccessor(Node node) => node.Kind == SyntaxKind.GetAccessor;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1299-1307 — <c>formatJSDocLink</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>getTextOfJSDocComment</c> is defined in terms of it. TS reads
    /// <c>link.name</c>/<c>link.text</c> off the <c>JSDocLink | JSDocLinkCode | JSDocLinkPlain</c> union; the port's
    /// three classes share no base declaring those slots, so the read is a three-arm pattern.
    /// </remarks>
    private static string FormatJSDocLink(Node link)
    {
        var (nameNode, text) = link switch
        {
            JSDocLink jSDocLink => ((Node?)jSDocLink.Name, jSDocLink.Text),
            JSDocLinkCode jSDocLinkCode => ((Node?)jSDocLinkCode.Name, jSDocLinkCode.Text),
            JSDocLinkPlain jSDocLinkPlain => ((Node?)jSDocLinkPlain.Name, jSDocLinkPlain.Text),
            _ => (null, null),
        };
        var kind = link.Kind == SyntaxKind.JSDocLink ? "link"
            : link.Kind == SyntaxKind.JSDocLinkCode ? "linkcode"
            : "linkplain";
        var name = nameNode is not null ? EntityNameToString(nameNode) : "";
        var space = nameNode is not null && (text == "" || Core.StartsWith(text!, "://")) ? "" : " ";
        return $"{{@{kind} {name}{space}{text}}}";
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1294-1297 — <c>getTextOfJSDocComment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>string | NodeArray&lt;JSDocComment&gt;</c>, which the generated model widens to
    /// <c>object?</c> on <c>JSDocTag.Comment</c>/<c>JSDoc.Comment</c> — which is what the landed callers hand over, so
    /// the parameter is <c>object?</c> and the two arms are the reference's <c>typeof comment === "string"</c> and
    /// <c>comment.map(…).join("")</c>
    /// </remarks>
    public static string? GetTextOfJSDocComment(object? comment)
    {
        if (comment is null) return null;
        if (comment is string text) return text;
        // PORT-NOTE: the array face is the model's `NodeArray<Node>` (NodeFactory's CreateJSDocComment overload takes
        // it); List<T> is invariant, so the sequence view is what both array shapes share.
        var builder = new System.Text.StringBuilder();
        foreach (var c in (IEnumerable<Node>)comment)
        {
            builder.Append(c.Kind == SyntaxKind.JSDocText ? ((JSDocText)c).Text : FormatJSDocLink(c));
        }
        return builder.ToString();
    }

    /// <summary>ported from: src/compiler/utilities.ts:6270-6274 — <c>isQuoteOrBacktick</c>.</summary>
    /// <remarks>PORT-NOTE: not an entry of the assignment, but <c>stripQuotes</c> is defined in terms of it.</remarks>
    private static bool IsQuoteOrBacktick(int charCode)
        => charCode == CharacterCodes.SingleQuote
            || charCode == CharacterCodes.DoubleQuote
            || charCode == CharacterCodes.Backtick;

    /// <summary>ported from: src/compiler/utilities.ts:6262-6268 — <c>stripQuotes</c>.</summary>
    public static string StripQuotes(string name)
    {
        var length = name.Length;
        if (length >= 2 && name[0] == name[length - 1] && IsQuoteOrBacktick(ScannerUtilities.CharCodeAt(name, 0)))
        {
            return name.Substring(1, length - 2);
        }
        return name;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2315-2317 — <c>hasPossibleExternalModuleReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>AnyImportOrReExport | ModuleDeclaration | ImportTypeNode | ImportCall</c> union,
    /// whose only port face is <c>Node</c>; the single landed caller passes this member to
    /// <c>Utilities.GetParseTreeNode(node, test)</c>, whose test is <c>Func&lt;Node, bool&gt;</c>.
    /// </remarks>
    public static bool HasPossibleExternalModuleReference(Node node)
        => IsAnyImportOrReExport(node) || NodeTests.IsModuleDeclaration(node) || NodeTests.IsImportTypeNode(node) || IsImportCall(node);

    /// <summary>ported from: src/compiler/utilities.ts:7592-7594 — <c>isRightSideOfInstanceofExpression</c>.</summary>
    public static bool IsRightSideOfInstanceofExpression(Node node)
        => IsInstanceOfExpression(node.Parent!) && ReferenceEquals(node, ((BinaryExpression)node.Parent!).Right);

    /// <summary>ported from: src/compiler/utilities.ts:664-666 — <c>isExternalModuleSymbol</c>.</summary>
    public static bool IsExternalModuleSymbol(ISymbol moduleSymbol)
        => (moduleSymbol.Flags & SymbolFlags.Module) != 0
            && ScannerUtilities.CharCodeAt(moduleSymbol.EscapedName ?? "", 0) == CharacterCodes.DoubleQuote;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2495-2501 — <c>isJsxCallLike</c>.</summary>
    public static bool IsJsxCallLike(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.JsxOpeningElement
            || kind == SyntaxKind.JsxSelfClosingElement
            || kind == SyntaxKind.JsxOpeningFragment;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3454-3468 — <c>getInvokedExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>CallLikeExpression</c> union and the result the
    /// <c>Expression | JsxTagNameExpression</c> union; both are the port's <c>Node</c>, and the caller casts the result
    /// to <c>Expression</c>. The reference's <c>default: return node.expression</c> covers the remaining union members,
    /// which share no port base declaring <c>Expression</c>.
    /// </remarks>
    public static Node GetInvokedExpression(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TaggedTemplateExpression:
                return ((TaggedTemplateExpression)node).Tag!;
            case SyntaxKind.JsxOpeningElement:
                return ((JsxOpeningElement)node).TagName!;
            case SyntaxKind.JsxSelfClosingElement:
                return ((JsxSelfClosingElement)node).TagName!;
            case SyntaxKind.BinaryExpression:
                return ((BinaryExpression)node).Right!;
            case SyntaxKind.JsxOpeningFragment:
                return node;
            default:
                return node switch
                {
                    CallExpression callExpression => callExpression.Expression!,
                    NewExpression newExpression => newExpression.Expression!,
                    PropertyAccessExpression propertyAccess => propertyAccess.Expression!,
                    ElementAccessExpression elementAccess => elementAccess.Expression!,
                    Decorator decorator => decorator.Expression!,
                    _ => throw new InvalidOperationException("unreachable: the node is not a call-like expression"),
                };
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:7540-7564 — <c>tryGetPropertyAccessOrIdentifierToString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>Expression | JsxTagNameExpression</c> union, whose port face is <c>Node</c>
    /// (the single landed caller casts its argument to <c>Expression</c> first). The recursion is over that same
    /// widened parameter, so the recursive calls need no casts.
    /// </remarks>
    public static string? TryGetPropertyAccessOrIdentifierToString(Node expr)
    {
        if (NodeTests.IsPropertyAccessExpression(expr))
        {
            var baseStr = TryGetPropertyAccessOrIdentifierToString(((PropertyAccessExpression)expr).Expression!);
            if (baseStr is not null)
            {
                return baseStr + "." + EntityNameToString(((PropertyAccessExpression)expr).Name!);
            }
        }
        else if (NodeTests.IsElementAccessExpression(expr))
        {
            var baseStr = TryGetPropertyAccessOrIdentifierToString(((ElementAccessExpression)expr).Expression!);
            if (baseStr is not null && IsPropertyName(((ElementAccessExpression)expr).ArgumentExpression!))
            {
                return baseStr + "." + GetPropertyNameForPropertyNameNode(((ElementAccessExpression)expr).ArgumentExpression!);
            }
        }
        else if (NodeTests.IsIdentifier(expr))
        {
            return UnescapeLeadingUnderscores(((Identifier)expr).EscapedText!);
        }
        else if (NodeTests.IsJsxNamespacedName(expr))
        {
            return GetTextOfJsxNamespacedName((JsxNamespacedName)expr);
        }
        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3293-3304 — <c>getNewTargetContainer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>FunctionDeclaration | ConstructorDeclaration | FunctionExpression</c> union,
    /// whose only port face is <c>Node</c>; the single landed caller compares it to null and switches on <c>Kind</c>.
    /// </remarks>
    public static Node? GetNewTargetContainer(Node node)
    {
        var container = GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
        if (container is not null)
        {
            switch (container.Kind)
            {
                case SyntaxKind.Constructor:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                    return container;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2923-2964 — <c>forEachYieldExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's nested <c>traverse</c> is the local function below and its truthiness test
    /// (<c>if (value) return value;</c>) is <c>Js.IsTruthy</c>, the same one <c>ForEachChildTable.ForEachChild</c> uses
    /// for its own early exit. <c>forEachChild</c> is <c>ForEachChildTable.ForEachChild</c> (the port keeps TS's name
    /// for the table, CS0542), and <c>node.name</c> is read off the <c>INamedDeclaration</c> face the port's
    /// function-like classes implement.
    /// </remarks>
    public static T? ForEachYieldExpression<T>(Block body, Func<YieldExpression, T?> visitor)
    {
        return Traverse(body);

        T? Traverse(Node node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.YieldExpression:
                    var value = visitor((YieldExpression)node);
                    if (Js.IsTruthy(value))
                    {
                        return value;
                    }
                    var operand = ((YieldExpression)node).Expression;
                    if (operand is null)
                    {
                        return default;
                    }
                    return Traverse(operand);
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.TypeAliasDeclaration:
                    // These are not allowed inside a generator now, but eventually they may be allowed
                    // as local types. Regardless, skip them to avoid the work.
                    return default;
                default:
                    if (IsFunctionLike(node))
                    {
                        if (node is INamedDeclaration { Name: { } name } && name.Kind == SyntaxKind.ComputedPropertyName)
                        {
                            // Note that we will not include methods/accessors of a class because they would require
                            // first descending into the class. This is by design.
                            return Traverse(((ComputedPropertyName)name).Expression!);
                        }
                    }
                    else if (!IsPartOfTypeNode(node))
                    {
                        // This is the general case, which should include mostly expressions and statements.
                        // Also includes NodeArrays.
                        return ForEachChildTable.ForEachChild(node, Traverse);
                    }
                    return default;
            }
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:3576-3601 — <c>classElementOrClassElementParameterIsDecorated</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>parent.members</c> is read through <c>IClassLikeDeclarationBase</c> and the element face is
    /// <c>IClassElement</c> (the model's <c>ClassElement</c> union), so the shared helpers below receive
    /// <c>Node</c>-cast members. <c>getAllAccessorDeclarations</c> is a local function here because the port's own
    /// <c>GetAllAccessorDeclarations</c> name is the <c>ITypeChecker</c> surface the checker class owns
    /// (<c>Checker.GetAllAccessorDeclarationsForDeclaration</c>), and §15.15 forbids <c>Utilities</c> and
    /// <c>Checker</c> from declaring same-named members; the local copy is the reference's <c>utilities.ts:6832-6885</c>
    /// body unchanged.
    /// </remarks>
    public static bool ClassElementOrClassElementParameterIsDecorated(bool useLegacyDecorators, Node node, Node parent)
    {
        NodeArray<ParameterDeclaration>? parameters = null;
        if (IsAccessor(node))
        {
            var (firstAccessor, secondAccessor, _, setAccessor) = GetAllAccessorDeclarations(((IClassLikeDeclarationBase)parent).Members, node);
            var firstAccessorWithDecorators = firstAccessor is not null && HasDecorators(firstAccessor) ? firstAccessor
                : secondAccessor is not null && HasDecorators(secondAccessor) ? secondAccessor
                : null;
            if (firstAccessorWithDecorators is null || !ReferenceEquals(node, firstAccessorWithDecorators))
            {
                return false;
            }
            parameters = setAccessor?.Parameters;
        }
        else if (NodeTests.IsMethodDeclaration(node))
        {
            parameters = ((MethodDeclaration)node).Parameters;
        }
        if (NodeIsDecorated(useLegacyDecorators, node, parent))
        {
            return true;
        }
        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                if (ParameterIsThisKeyword(parameter)) continue;
                if (NodeIsDecorated(useLegacyDecorators, parameter, node, parent)) return true;
            }
        }
        return false;

        // ported from: src/compiler/utilities.ts:6832-6885 — <c>getAllAccessorDeclarations</c>.
        static (Node? FirstAccessor, Node? SecondAccessor, GetAccessorDeclaration? GetAccessor, SetAccessorDeclaration? SetAccessor)
            GetAllAccessorDeclarations(IEnumerable<IClassElement>? declarations, Node accessor)
        {
            Node? firstAccessor = null;
            Node? secondAccessor = null;
            GetAccessorDeclaration? getAccessor = null;
            SetAccessorDeclaration? setAccessor = null;
            if (HasDynamicName(accessor))
            {
                firstAccessor = accessor;
                if (accessor.Kind == SyntaxKind.GetAccessor)
                {
                    getAccessor = (GetAccessorDeclaration)accessor;
                }
                else if (accessor.Kind == SyntaxKind.SetAccessor)
                {
                    setAccessor = (SetAccessorDeclaration)accessor;
                }
                else
                {
                    Debug.Fail("Accessor has wrong kind");
                }
            }
            else
            {
                foreach (var member in declarations ?? [])
                {
                    var m = (Node)member;
                    if (!IsAccessor(m) || IsStatic(m) != IsStatic(accessor))
                    {
                        continue;
                    }
                    var memberName = GetPropertyNameForPropertyNameNode(((INamedDeclaration)member).Name!);
                    var accessorName = GetPropertyNameForPropertyNameNode(((INamedDeclaration)accessor).Name!);
                    if (memberName == accessorName)
                    {
                        if (firstAccessor is null)
                        {
                            firstAccessor = m;
                        }
                        else if (secondAccessor is null)
                        {
                            secondAccessor = m;
                        }

                        if (m.Kind == SyntaxKind.GetAccessor && getAccessor is null)
                        {
                            getAccessor = (GetAccessorDeclaration)m;
                        }

                        if (m.Kind == SyntaxKind.SetAccessor && setAccessor is null)
                        {
                            setAccessor = (SetAccessorDeclaration)m;
                        }
                    }
                }
            }
            return (firstAccessor, secondAccessor, getAccessor, setAccessor);
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:5469-5472 — <c>isProtoSetter</c>.</summary>
    /// <remarks>PORT-NOTE: not an entry of the assignment, but <c>isNamedEvaluationSource</c> is defined in terms of it.</remarks>
    private static bool IsProtoSetter(Node node)
        => NodeTests.IsIdentifier(node) ? IdText(node) == "__proto__"
            : NodeTests.IsStringLiteral(node) && ((StringLiteral)node).Text == "__proto__";

    /// <summary>ported from: src/compiler/utilities.ts:5522-5554 — <c>isNamedEvaluationSource</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>NamedEvaluationSource</c> union (a type guard over property assignments, variable
    /// declarations, parameters, binding elements, property declarations, assignment expressions and export
    /// assignments), which the port answers as <c>bool</c> (rule (d)); the caller only tests it.
    /// </remarks>
    public static bool IsNamedEvaluationSource(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.PropertyAssignment:
                return !IsProtoSetter(((PropertyAssignment)node).Name!);
            case SyntaxKind.ShorthandPropertyAssignment:
                return ((ShorthandPropertyAssignment)node).ObjectAssignmentInitializer is not null;
            case SyntaxKind.VariableDeclaration:
                return NodeTests.IsIdentifier(((VariableDeclaration)node).Name!) && ((VariableDeclaration)node).Initializer is not null;
            case SyntaxKind.Parameter:
                return NodeTests.IsIdentifier(((ParameterDeclaration)node).Name!)
                    && ((ParameterDeclaration)node).Initializer is not null
                    && ((ParameterDeclaration)node).DotDotDotToken is null;
            case SyntaxKind.BindingElement:
                return NodeTests.IsIdentifier(((BindingElement)node).Name!)
                    && ((BindingElement)node).Initializer is not null
                    && ((BindingElement)node).DotDotDotToken is null;
            case SyntaxKind.PropertyDeclaration:
                return ((PropertyDeclaration)node).Initializer is not null;
            case SyntaxKind.BinaryExpression:
                switch (((BinaryExpression)node).OperatorToken!.Kind)
                {
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.AmpersandAmpersandEqualsToken:
                    case SyntaxKind.BarBarEqualsToken:
                    case SyntaxKind.QuestionQuestionEqualsToken:
                        return NodeTests.IsIdentifier(((BinaryExpression)node).Left!);
                }
                break;
            case SyntaxKind.ExportAssignment:
                return true;
        }
        return false;
    }

/// <summary>ported from: src/compiler/utilities.ts:670-703 — <c>createSingleLineStringWriter</c>'s object literal.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>createSingleLineStringWriter</c> returns an object literal typed by
/// <c>EmitTextWriter</c>; the port's face is <see cref="IEmitTextWriter"/> (plus <c>ISymbolWriter</c>, which that
/// extends), so the literal becomes this class and the closure variable <c>str</c> becomes the field. The
/// indentation and position members answer the reference's own constants (<c>getLine</c>/<c>getColumn</c>/<c>getIndent</c>
/// are <c>0</c>, <c>isAtStartOfLine</c>/<c>hasTrailingComment</c> are <c>false</c>) and <c>writeLine</c> appends a single
/// space, which is what the reference's "Completely ignore indentation for string writers. And map newlines to a
/// single space." comment describes. TS leaves <c>nonEscapingWrite</c> undefined (optional in its interface); the
/// generated interface declares it, so it writes the text like the rest of the family.
/// </remarks>
internal sealed class SingleLineStringWriter : IEmitTextWriter
{
    private string _str = "";

    private void WriteText(string? text) => _str += text ?? "";

    public void Write(string? s) => WriteText(s);

    public void WriteTrailingSemicolon(string? text) => WriteText(text);

    public void WriteComment(string? text) => WriteText(text);

    public void RawWrite(string? s) => WriteText(s);

    public void WriteLiteral(string? s) => WriteText(s);

    public void NonEscapingWrite(string? text) => WriteText(text);

    public void WriteKeyword(string? text) => WriteText(text);

    public void WriteOperator(string? text) => WriteText(text);

    public void WritePunctuation(string? text) => WriteText(text);

    public void WriteSpace(string? text) => WriteText(text);

    public void WriteStringLiteral(string? text) => WriteText(text);

    public void WriteParameter(string? text) => WriteText(text);

    public void WriteProperty(string? text) => WriteText(text);

    public void WriteSymbol(string? text, ISymbol? symbol) => WriteText(text);

    public void WriteLine(bool? force) => _str += " ";

    public string GetText() => _str;

    public int GetTextPos() => _str.Length;

    public int GetLine() => 0;

    public int GetColumn() => 0;

    public int GetIndent() => 0;

    public bool IsAtStartOfLine() => false;

    public bool HasTrailingComment() => false;

    public bool HasTrailingWhitespace() => _str.Length != 0 && ScannerUtilities.IsWhiteSpaceLike(_str[^1]);

    public void IncreaseIndent()
    {
    }

    public void DecreaseIndent()
    {
    }

    public void Clear() => _str = "";

    public override string ToString() => _str;
}

/// <summary>ported from: src/compiler/utilities.ts:7450-7453 — <c>ClassImplementingOrExtendingExpressionWithTypeArguments</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS declares this record type in <c>utilities.ts</c>; the port's generated model houses types.ts records
/// as <c>I&lt;Name&gt;</c> interfaces (<c>Model/NodeInterfaces.cs</c>), and this one has no generator entry (it is not a
/// types.ts interface), so it is declared here with the two readonly members its one landed caller reads — the
/// <c>Class</c> face is <see cref="IDeclaration"/> because that caller passes it straight to
/// <c>Checker.GetSymbolOfDeclaration(IDeclaration)</c>.
/// </remarks>
internal sealed record ClassImplementingOrExtendingExpressionWithTypeArguments(IDeclaration Class, bool IsImplements);

    // ---------------------------------------------------------------------------------------------
    // The A1 low band, continued (artifacts/order/A1-util-low.txt).
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3809-3811 — <c>isInJsonFile</c>.</summary>
    public static bool IsInJsonFile(Node? node) => node is not null && (node.Flags & NodeFlags.JsonFile) != 0;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1180-1182 — <c>getJSDocEnumTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getFirstJSDocTag</c> is the port's <c>Utilities.GetFirstJSDocTag(Node, Func&lt;JSDocTag, bool&gt;)</c>
    /// (<c>Utilities/Utilities.JSDoc.cs</c>, private in this same class), which is how the landed
    /// <c>GetJSDocDeprecatedTag</c> pair reads it; the tag's narrow face is the cast at the end.
    /// </remarks>
    public static JSDocEnumTag? GetJSDocEnumTag(Node node)
        => GetFirstJSDocTag(node, static tag => NodeTests.IsJSDocEnumTag(tag)) as JSDocEnumTag;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1757-1763 — <c>isClassInstanceProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>Declaration</c> face, which the port's model spells
    /// <see cref="IDeclaration"/> (the caller passes elements of <c>ISymbol.Declarations</c>); the two shared helpers
    /// below take that same face, and <c>node.expression</c> is read through the port's
    /// <c>Utilities.GetAccessExpressionExpression</c> — the reference's read off the
    /// <c>BindableStaticAccessExpression</c> union, which the guard above it establishes.
    /// </remarks>
    public static bool IsClassInstanceProperty(IDeclaration node)
    {
        var n = (Node)node;
        if (IsInJSFile(n) && IsExpandoPropertyDeclaration(node))
        {
            return (!IsBindableStaticAccessExpression(n) || !IsPrototypeAccess(GetAccessExpressionExpression(n)))
                && !IsBindableStaticNameExpression(n, /*excludeThisKeyword*/ true);
        }
        return n.Parent is not null && IsClassLike(n.Parent) && NodeTests.IsPropertyDeclaration(n) && !HasAccessorModifier(n);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3413-3419 — <c>isThisProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>(node as PropertyAccessExpression | ElementAccessExpression).expression</c>; both are
    /// <c>MemberExpression</c> in the port, so the cast is to that base and the kind test is the reference's.
    /// </remarks>
    public static bool IsThisProperty(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.PropertyAccessExpression ? ((PropertyAccessExpression)node).Expression!.Kind == SyntaxKind.ThisKeyword
            : kind == SyntaxKind.ElementAccessExpression && ((ElementAccessExpression)node).Expression!.Kind == SyntaxKind.ThisKeyword;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3425-3432 — <c>isThisInitializedObjectBindingExpression</c>.</summary>
    public static bool IsThisInitializedObjectBindingExpression(Node? node)
    {
        if (node is null || !(NodeTests.IsShorthandPropertyAssignment(node) || NodeTests.IsPropertyAssignment(node)))
        {
            return false;
        }
        var assignment = node.Parent!.Parent!;
        return NodeTests.IsBinaryExpression(assignment)
            && ((BinaryExpression)assignment).OperatorToken!.Kind == SyntaxKind.EqualsToken
            && ((BinaryExpression)assignment).Right!.Kind == SyntaxKind.ThisKeyword;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3257-3284 — <c>isThisContainerOrFunctionBlock</c>.</summary>
    public static bool IsThisContainerOrFunctionBlock(Node node)
    {
        switch (node.Kind)
        {
            // Arrow functions use the same scope, but may do so in a "delayed" manner
            // For example, `const getThis = () => this` may be before a super() call in a derived constructor
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.PropertyDeclaration:
                return true;
            case SyntaxKind.Block:
                switch (node.Parent!.Kind)
                {
                    case SyntaxKind.Constructor:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.GetAccessor:
                    case SyntaxKind.SetAccessor:
                        // Object properties can have computed names; only method-like bodies start a new scope
                        return true;
                    default:
                        return false;
                }
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:2519-2523 — <c>scanTokenAtPosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>createScanner(languageVersion, skipTrivia, languageVariant, text, onError, start)</c> is
    /// <c>Scanner.Create</c>, whose trailing <c>length</c> parameter TS leaves unset; TS's <c>/*onError*/ undefined</c>
    /// is the null callback. The shape is the one the landed <c>GetSpanOfTokenAtPosition</c> already uses.
    /// </remarks>
    public static SyntaxKind ScanTokenAtPosition(SourceFile sourceFile, int pos)
    {
        var scanner = Scanner.Create(sourceFile.LanguageVersion, /*skipTrivia*/ true, sourceFile.LanguageVariant, sourceFile.Text ?? "", /*onError*/ null, pos);
        scanner.Scan();
        return scanner.GetToken();
    }

    /// <summary>ported from: src/compiler/utilities.ts:4837-4839 — <c>hasTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>HasTypeArguments</c> union, whose port face is the generated
    /// <c>INodeWithTypeArguments</c>; the reference's <c>!!node.typeArguments</c> is the pattern below, and the
    /// landed caller reads the same slot off that interface.
    /// </remarks>
    public static bool HasTypeArguments(Node node) => node is INodeWithTypeArguments { TypeArguments: not null };

    /// <summary>ported from: src/compiler/utilities.ts:639-641 — <c>getDeclarationsOfKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>T extends Declaration</c> constraint and <c>as T[]</c> cast become
    /// <c>where T : class</c> plus the element cast below, which is the shape the landed generic sibling
    /// <c>Utilities.GetDeclarationOfKind&lt;T&gt;</c> (<c>Checker/Checker.Support.cs:2533</c>) uses; TS's
    /// <c>filter(… )</c> is <c>Core.Filter</c> over the ordered declaration list.
    /// </remarks>
    public static List<T> GetDeclarationsOfKind<T>(ISymbol symbol, SyntaxKind kind)
        where T : class
    {
        var result = new List<T>();
        var declarations = Core.Filter(symbol.Declarations, d => d.Kind == kind);
        if (declarations is not null)
        {
            foreach (var declaration in declarations)
            {
                result.Add((T)declaration);
            }
        }
        return result;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1364-1366 — <c>isGetOrSetAccessorDeclaration</c>.</summary>
    public static bool IsGetOrSetAccessorDeclaration(Node node)
        => node.Kind == SyntaxKind.SetAccessor || node.Kind == SyntaxKind.GetAccessor;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2504-2510 — <c>isCaseOrDefaultClause</c>.</summary>
    public static bool IsCaseOrDefaultClause(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.CaseClause
            || kind == SyntaxKind.DefaultClause;
    }

    /// <summary>ported from: src/compiler/utilities.ts:1305-1307 — <c>moduleExportNameTextEscaped</c>.</summary>
    public static string ModuleExportNameTextEscaped(Node node)
        => node.Kind == SyntaxKind.StringLiteral
            ? EscapeLeadingUnderscores(((StringLiteral)node).Text!)
            : ((Identifier)node).EscapedText!;

    /// <summary>ported from: src/compiler/utilities.ts:1238-1279 — <c>getTokenPosOfNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>sourceFile?: SourceFileLike</c> is <c>SourceFile?</c> in the port: the model's
    /// <c>ISourceFileLike</c> interface is implemented by no ported type (the same note the landed scanner helpers
    /// carry), and the one landed caller passes a <c>SourceFile</c>. <c>node.jsDoc![0]</c> reads the port's
    /// <c>JSDocArray</c> (a <c>NodeArray&lt;JSDoc&gt;</c>) through the <c>IJSDocContainer</c> face; <c>skipTrivia</c> is
    /// <c>ScannerUtilities.SkipTrivia</c>, <c>getNodeChildren</c> is <c>NodeChildren.GetNodeChildren</c> and
    /// <c>firstOrUndefined</c> is <c>Core.FirstOrUndefined</c>.
    /// </remarks>
    public static int GetTokenPosOfNode(Node node, SourceFile? sourceFile = null, bool includeJsDoc = false)
    {
        // With nodes that have no width (i.e. 'Missing' nodes), we actually *don't*
        // want to skip trivia because this will launch us forward to the next token.
        if (NodeIsMissing(node))
        {
            return node.Pos;
        }

        if (IsJSDocNode(node) || node.Kind == SyntaxKind.JsxText)
        {
            // JsxText cannot actually contain comments, even though the scanner will think it sees comments
            return ScannerUtilities.SkipTrivia((sourceFile ?? GetSourceFileOfNode(node))!.Text!, node.Pos, /*stopAfterLineBreak*/ false, /*stopAtComments*/ true);
        }

        if (includeJsDoc && HasJSDocNodes(node))
        {
            return GetTokenPosOfNode(((JSDocArray)((IJSDocContainer)node).JsDoc!)[0], sourceFile, includeJsDoc);
        }

        // For a syntax list, it is possible that one of its children has JSDocComment nodes, while
        // the syntax list itself considers them as normal trivia. Therefore if we simply skip
        // trivia for the list, we may have skipped the JSDocComment as well. So we should process its
        // first child to determine the actual position of its first token.
        if (node.Kind == SyntaxKind.SyntaxList)
        {
            sourceFile ??= GetSourceFileOfNode(node);
            var first = Core.FirstOrUndefined(NodeChildren.GetNodeChildren(node, sourceFile!));
            if (first is not null)
            {
                return GetTokenPosOfNode(first, sourceFile, includeJsDoc);
            }
        }

        return ScannerUtilities.SkipTrivia(
            (sourceFile ?? GetSourceFileOfNode(node))!.Text!,
            node.Pos,
            /*stopAfterLineBreak*/ false,
            /*stopAtComments*/ false,
            IsInJSDoc(node));
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1605-1607 — <c>isPrivateIdentifierPropertyAccessExpression</c>.</summary>
    public static bool IsPrivateIdentifierPropertyAccessExpression(Node node)
        => NodeTests.IsPropertyAccessExpression(node) && NodeTests.IsPrivateIdentifier(((PropertyAccessExpression)node).Name!);

    /// <summary>ported from: src/compiler/utilities.ts:3020-3025 — <c>isCommonJsExportedExpression</c>.</summary>
    public static bool IsCommonJsExportedExpression(Node node)
    {
        if (!IsInJSFile(node)) return false;
        return (NodeTests.IsObjectLiteralExpression(node.Parent!) && NodeTests.IsBinaryExpression(node.Parent!.Parent!) && GetAssignmentDeclarationKind(node.Parent!.Parent!) == AssignmentDeclarationKind.ModuleExports)
            || IsCommonJsExportPropertyAssignment(node.Parent!);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3040-3052 — <c>introducesArgumentsExoticObject</c>.</summary>
    public static bool IntroducesArgumentsExoticObject(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7575-7578 — <c>isRightSideOfAccessExpression</c>.</summary>
    public static bool IsRightSideOfAccessExpression(Node node)
        => node.Parent is not null && (NodeTests.IsPropertyAccessExpression(node.Parent) && ReferenceEquals(((PropertyAccessExpression)node.Parent).Name, node)
            || NodeTests.IsElementAccessExpression(node.Parent) && ReferenceEquals(((ElementAccessExpression)node.Parent).ArgumentExpression, node));

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1957-1961 — <c>isPropertyAccessOrQualifiedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>PropertyAccessExpression | QualifiedName</c>; the port answers <c>bool</c>
    /// (mapping rule (d)), and its caller re-tests with the two kind tests where it needs the narrowing.
    /// </remarks>
    public static bool IsPropertyAccessOrQualifiedName(Node node)
        => NodeTests.IsPropertyAccessExpression(node) || NodeTests.IsQualifiedName(node);

    /// <summary>ported from: src/compiler/utilities.ts:7581-7585 — <c>isRightSideOfQualifiedNameOrPropertyAccessOrJSDocMemberName</c>.</summary>
    public static bool IsRightSideOfQualifiedNameOrPropertyAccessOrJSDocMemberName(Node node)
        => NodeTests.IsQualifiedName(node.Parent!) && ReferenceEquals(((QualifiedName)node.Parent!).Right, node)
            || NodeTests.IsPropertyAccessExpression(node.Parent!) && ReferenceEquals(((PropertyAccessExpression)node.Parent!).Name, node)
            || NodeTests.IsJSDocMemberName(node.Parent!) && ReferenceEquals(((JSDocMemberName)node.Parent!).Right, node);

    /// <summary>ported from: src/compiler/utilities.ts:4830-4835 — <c>getTypeParameterFromJsDoc</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter type is the intersection <c>TypeParameterDeclaration &amp; { parent: JSDocTemplateTag }</c>
    /// and its body destructures <c>typeParameters</c> from the <c>SignatureDeclaration | InterfaceDeclaration |
    /// ClassDeclaration</c> union; the port reads that slot through the same <c>TypeParametersOf</c> helper
    /// <c>GetEffectiveTypeParameterDeclarations</c> uses (the model splits it over three faces).
    /// </remarks>
    public static TypeParameterDeclaration? GetTypeParameterFromJsDoc(TypeParameterDeclaration node)
    {
        var name = node.Name!.EscapedText;
        var typeParameters = TypeParametersOf(node.Parent!.Parent!.Parent!);
        return typeParameters is not null ? Core.Find(typeParameters, (p, _) => p.Name!.EscapedText == name) : null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1532-1534 — <c>isImportOrExportSpecifier</c>.</summary>
    public static bool IsImportOrExportSpecifier(Node node)
        => NodeTests.IsImportSpecifier(node) || NodeTests.IsExportSpecifier(node);

    /// <summary>ported from: src/compiler/utilities.ts:5112-5117 — <c>isLiteralComputedPropertyDeclarationName</c>.</summary>
    public static bool IsLiteralComputedPropertyDeclarationName(Node node)
        => IsStringOrNumericLiteralLike(node)
            && node.Parent!.Kind == SyntaxKind.ComputedPropertyName
            && IsDeclaration(node.Parent!.Parent!);

    /// <summary>ported from: src/compiler/utilities.ts:6814-6824 — <c>isThisInTypeQuery</c>.</summary>
    public static bool IsThisInTypeQuery(Node node)
    {
        if (!IsThisIdentifier(node))
        {
            return false;
        }

        while (NodeTests.IsQualifiedName(node.Parent!) && ReferenceEquals(((QualifiedName)node.Parent!).Left, node))
        {
            node = node.Parent!;
        }

        return node.Parent!.Kind == SyntaxKind.TypeQuery;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3704-3757 — <c>isInExpressionContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the <c>HasInitializer</c> arm as <c>(parent as HasInitializer).initializer</c> and the
    /// expression-statement arm as <c>(parent as ExpressionStatement).expression</c> — two union faces whose members
    /// share no port base declaring those slots, so the port names the slot per kind in the two local readers below
    /// (every kind listed is one whose class declares it, which is what the single cast asserts in the reference). The
    /// <c>ForInStatement | ForOfStatement</c> arm is likewise split by kind, and <c>AssertionExpression</c> (TS's
    /// <c>TypeAssertion | AsExpression</c> union) is the port's two classes <c>TypeAssertion</c> and
    /// <c>AsExpression</c>.
    /// </remarks>
    public static bool IsInExpressionContext(Node node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.Parameter:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.EnumMember:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.BindingElement:
                return ReferenceEquals(InitializerOf(parent), node);
            case SyntaxKind.ExpressionStatement:
            case SyntaxKind.IfStatement:
            case SyntaxKind.DoStatement:
            case SyntaxKind.WhileStatement:
            case SyntaxKind.ReturnStatement:
            case SyntaxKind.WithStatement:
            case SyntaxKind.SwitchStatement:
            case SyntaxKind.CaseClause:
            case SyntaxKind.ThrowStatement:
                return ReferenceEquals(StatementExpressionOf(parent), node);
            case SyntaxKind.ForStatement:
                var forStatement = (ForStatement)parent;
                return (ReferenceEquals(forStatement.Initializer, node) && forStatement.Initializer!.Kind != SyntaxKind.VariableDeclarationList)
                    || ReferenceEquals(forStatement.Condition, node)
                    || ReferenceEquals(forStatement.Incrementor, node);
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                var initializer = parent.Kind == SyntaxKind.ForInStatement ? ((ForInStatement)parent).Initializer : ((ForOfStatement)parent).Initializer;
                var expression = parent.Kind == SyntaxKind.ForInStatement ? ((ForInStatement)parent).Expression : ((ForOfStatement)parent).Expression;
                return (ReferenceEquals(initializer, node) && initializer!.Kind != SyntaxKind.VariableDeclarationList)
                    || ReferenceEquals(expression, node);
            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                return parent.Kind == SyntaxKind.AsExpression
                    ? ReferenceEquals(((AsExpression)parent).Expression, node)
                    : ReferenceEquals(((TypeAssertion)parent).Expression, node);
            case SyntaxKind.TemplateSpan:
                return ReferenceEquals(((TemplateSpan)parent).Expression, node);
            case SyntaxKind.ComputedPropertyName:
                return ReferenceEquals(((ComputedPropertyName)parent).Expression, node);
            case SyntaxKind.Decorator:
            case SyntaxKind.JsxExpression:
            case SyntaxKind.JsxSpreadAttribute:
            case SyntaxKind.SpreadAssignment:
                return true;
            case SyntaxKind.ExpressionWithTypeArguments:
                return ReferenceEquals(((ExpressionWithTypeArguments)parent).Expression, node) && !IsPartOfTypeNode(parent);
            case SyntaxKind.ShorthandPropertyAssignment:
                // TODO(jakebailey): it's possible that node could be the name, too
                return ReferenceEquals(((ShorthandPropertyAssignment)parent).ObjectAssignmentInitializer, node);
            case SyntaxKind.SatisfiesExpression:
                return ReferenceEquals(((SatisfiesExpression)parent).Expression, node);
            default:
                return IsExpressionNode(parent);
        }

        // PORT-NOTE: TS's `(parent as HasInitializer).initializer` — the seven kinds the arm lists all declare the slot.
        static Node? InitializerOf(Node parent) => parent switch
        {
            VariableDeclaration declaration => declaration.Initializer,
            ParameterDeclaration declaration => declaration.Initializer,
            PropertyDeclaration declaration => declaration.Initializer,
            PropertySignature declaration => declaration.Initializer,
            EnumMember declaration => declaration.Initializer,
            PropertyAssignment declaration => declaration.Initializer,
            BindingElement declaration => declaration.Initializer,
            _ => null,
        };

        // PORT-NOTE: TS's `(parent as ExpressionStatement).expression` — the nine kinds the arm lists all declare it.
        static Node? StatementExpressionOf(Node parent) => parent switch
        {
            ExpressionStatement statement => statement.Expression,
            IfStatement statement => statement.Expression,
            DoStatement statement => statement.Expression,
            WhileStatement statement => statement.Expression,
            ReturnStatement statement => statement.Expression,
            WithStatement statement => statement.Expression,
            SwitchStatement statement => statement.Expression,
            CaseClause statement => statement.Expression,
            ThrowStatement statement => statement.Expression,
            _ => null,
        };
    }

    /// <summary>ported from: src/compiler/utilities.ts:3773-3775 — <c>isExternalModuleImportEqualsDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the intersection <c>ImportEqualsDeclaration &amp; { moduleReference: ExternalModuleReference }</c>;
    /// the port answers <c>bool</c> (rule (d)) and the single landed caller casts at its own use site.
    /// </remarks>
    public static bool IsExternalModuleImportEqualsDeclaration(Node node)
        => node.Kind == SyntaxKind.ImportEqualsDeclaration
            && ((ImportEqualsDeclaration)node).ModuleReference!.Kind == SyntaxKind.ExternalModuleReference;

    /// <summary>ported from: src/compiler/utilities.ts:3778-3781 — <c>getExternalModuleImportEqualsDeclarationExpression</c>.</summary>
    public static Expression GetExternalModuleImportEqualsDeclarationExpression(Node node)
    {
        Debug.Assert(IsExternalModuleImportEqualsDeclaration(node));
        return ((ExternalModuleReference)((ImportEqualsDeclaration)node).ModuleReference!).Expression!;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7455-7468 — <c>tryGetClassImplementingOrExtendingExpressionWithTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the record's <c>class</c> slot is <see cref="IDeclaration"/> because the landed caller
    /// (<c>Checker.Check.Source.cs</c>) passes it to <c>GetSymbolOfDeclaration(IDeclaration)</c>.
    /// </remarks>
    public static ClassImplementingOrExtendingExpressionWithTypeArguments? TryGetClassImplementingOrExtendingExpressionWithTypeArguments(Node node)
    {
        if (NodeTests.IsExpressionWithTypeArguments(node))
        {
            if (NodeTests.IsHeritageClause(node.Parent!) && IsClassLike(node.Parent!.Parent!))
            {
                return new ClassImplementingOrExtendingExpressionWithTypeArguments(
                    (IDeclaration)node.Parent!.Parent!,
                    ((HeritageClause)node.Parent!).Token == SyntaxKind.ImplementsKeyword);
            }
            if (NodeTests.IsJSDocAugmentsTag(node.Parent!))
            {
                var host = GetEffectiveJSDocHost(node.Parent!);
                if (host is not null && IsClassLike(host))
                {
                    return new ClassImplementingOrExtendingExpressionWithTypeArguments((IDeclaration)host, false);
                }
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7444-7447 — <c>tryGetClassExtendingExpressionWithTypeArguments</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>ClassLikeDeclaration</c> face, which the port widens to
    /// <see cref="IDeclaration"/> (the record's slot it returns). The reference's one caller,
    /// <c>isExpressionWithTypeArgumentsInClassExtendsClause</c>, is already landed as the folded predicate
    /// <c>Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause</c> (<c>Checker/Checker.Support.cs</c>), so this
    /// member is declared for the reference's own completeness and has no port caller yet.
    /// </remarks>
    public static IDeclaration? TryGetClassExtendingExpressionWithTypeArguments(Node node)
    {
        var cls = TryGetClassImplementingOrExtendingExpressionWithTypeArguments(node);
        return cls is not null && !cls.IsImplements ? cls.Class : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:1351-1353 — <c>indexOfNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>binarySearch(nodeArray, node, getPos, compareValues)</c> is <c>Core.BinarySearch</c> with the
    /// file-local <c>getPos</c> (utilities.ts:1341) inlined as the position selector and the port's
    /// <c>Core.CompareValues</c> as the comparer (<c>Core.BinarySearch</c> takes the port's own
    /// <c>Comparer&lt;T&gt;</c> delegate); <c>getPos</c> itself is therefore not declared as a member.
    /// </remarks>
    public static int IndexOfNode(IList<Node> nodeArray, Node node)
        => Core.BinarySearch(nodeArray, node, static range => range.Pos, static (a, b) => Core.CompareValues(a, b));

    /// <summary>ported from: src/compiler/utilities.ts:7568-7573 — <c>isRightSideOfQualifiedNameOrPropertyAccess</c>.</summary>
    public static bool IsRightSideOfQualifiedNameOrPropertyAccess(Node node)
        => (node.Parent!.Kind == SyntaxKind.QualifiedName && ReferenceEquals(((QualifiedName)node.Parent!).Right, node))
            || (node.Parent!.Kind == SyntaxKind.PropertyAccessExpression && ReferenceEquals(((PropertyAccessExpression)node.Parent!).Name, node))
            || (node.Parent!.Kind == SyntaxKind.MetaProperty && ReferenceEquals(((MetaProperty)node.Parent!).Name, node));

    /// <summary>ported from: src/compiler/utilities.ts:1002-1012 — <c>isStatementWithLocals</c>.</summary>
    public static bool IsStatementWithLocals(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Block:
            case SyntaxKind.CaseBlock:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2117-2121 — <c>isBlockScopedContainerTopLevel</c>.</summary>
    public static bool IsBlockScopedContainerTopLevel(Node node)
        => node.Kind == SyntaxKind.SourceFile
            || node.Kind == SyntaxKind.ModuleDeclaration
            || IsFunctionLikeOrClassStaticBlockDeclaration(node);

    /// <summary>ported from: src/compiler/utilities.ts:3880-3882 — <c>isBindingElementOfBareOrAccessedRequire</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>BindingElementOfBareOrAccessedRequire</c> union (a type guard), which the port
    /// answers as <c>bool</c> (rule (d)); the caller only tests it.
    /// </remarks>
    public static bool IsBindingElementOfBareOrAccessedRequire(Node node)
        => NodeTests.IsBindingElement(node) && IsVariableDeclarationInitializedToBareOrAccessedRequire(node.Parent!.Parent!);

    /// <summary>ported from: src/compiler/utilities.ts:3434-3452 — <c>getEntityNameFromTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>TypeNode</c> parameter is the port's <see cref="ITypeNode"/> and the
    /// <c>EntityNameOrEntityNameExpression</c> result is <c>Node?</c> (that union has no port face; the landed caller
    /// passes the result to <c>ResolveEntityName(Node, …)</c> and <c>Utilities.EntityNameToString(Node)</c>). TS's
    /// <c>case SyntaxKind.Identifier as TypeNodeSyntaxKind: case SyntaxKind.QualifiedName as TypeNodeSyntaxKind:</c> arm
    /// answers the node itself, which is the <c>(Node)node</c> below.
    /// </remarks>
    public static Node? GetEntityNameFromTypeNode(ITypeNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TypeReference:
                return ((TypeReferenceNode)node).TypeName;

            case SyntaxKind.ExpressionWithTypeArguments:
                return IsEntityNameExpression(((ExpressionWithTypeArguments)node).Expression!)
                    ? ((ExpressionWithTypeArguments)node).Expression
                    : null;

            // TODO(rbuckton): These aren't valid TypeNodes, but we treat them as such because of `isPartOfTypeNode`, which returns `true` for things that aren't `TypeNode`s.
            case SyntaxKind.Identifier:
            case SyntaxKind.QualifiedName:
                return (Node)node;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2970-2981 — <c>getRestParameterElementType</c>.</summary>
    public static ITypeNode? GetRestParameterElementType(ITypeNode? node)
    {
        if (node is not null && node.Kind == SyntaxKind.ArrayType)
        {
            return ((ArrayTypeNode)node).ElementType;
        }
        else if (node is not null && node.Kind == SyntaxKind.TypeReference)
        {
            return Core.SingleOrUndefined(((TypeReferenceNode)node).TypeArguments);
        }
        else
        {
            return null;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:4029-4034 — <c>isDefaultedExpandoInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is <c>boolean | undefined</c> because the <c>name</c> it computes can be undefined; the
    /// port keeps <c>bool?</c> and the same three conjuncts, with <c>getExpandoInitializer</c>'s non-null result as the
    /// truthiness test (it answers <c>Expression | undefined</c>). <c>isSameEntityName</c>'s two parameters are
    /// <c>Expression</c>, which TS's preceding <c>isEntityNameExpression(name)</c> guard establishes — the port keeps
    /// that order and casts there.
    /// </remarks>
    public static bool? IsDefaultedExpandoInitializer(BinaryExpression node)
    {
        var name = NodeTests.IsVariableDeclaration(node.Parent!) ? ((VariableDeclaration)node.Parent!).Name
            : NodeTests.IsBinaryExpression(node.Parent!) && ((BinaryExpression)node.Parent!).OperatorToken!.Kind == SyntaxKind.EqualsToken ? ((BinaryExpression)node.Parent!).Left
            : null;
        return name is not null
            && GetExpandoInitializer(node.Right!, IsPrototypeAccess(name)) is not null
            && IsEntityNameExpression(name)
            && IsSameEntityName((Expression)name, node.Left!);
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2477-2483 — <c>isJsxAttributeLike</c>.</summary>
    public static bool IsJsxAttributeLike(Node node)
    {
        var kind = node.Kind;
        return kind == SyntaxKind.JsxAttribute
            || kind == SyntaxKind.JsxSpreadAttribute;
    }

    /// <summary>ported from: src/compiler/utilities.ts:620 — <c>noTruncationMaximumTruncationLength</c>.</summary>
    public const int NoTruncationMaximumTruncationLength = 1_000_000;

    /// <summary>ported from: src/compiler/utilities.ts:618 — <c>defaultMaximumTruncationLength</c>.</summary>
    public const int DefaultMaximumTruncationLength = 160;

    /// <summary>ported from: src/compiler/utilities.ts:2083-2085 — <c>isModuleWithStringLiteralName</c>.</summary>
    public static bool IsModuleWithStringLiteralName(Node node)
        => NodeTests.IsModuleDeclaration(node) && ((ModuleDeclaration)node).Name!.Kind == SyntaxKind.StringLiteral;

    /// <summary>ported from: src/compiler/utilities.ts:2297-2312 — <c>isLateVisibilityPaintedStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>LateVisibilityPaintedStatement</c> union (a type guard); the port answers
    /// <c>bool</c> (rule (d)) — the landed caller only tests it. <c>LateVisibilityPaintedStatement</c> is one of the
    /// generated union names that does not exist in the port.
    /// </remarks>
    public static bool IsLateVisibilityPaintedStatement(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.VariableStatement:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.EnumDeclaration:
                return true;
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2133-2135 — <c>isScopeMarker</c>.</summary>
    /// <remarks>PORT-NOTE: not an entry of the assignment, but <c>hasScopeMarker</c> is defined in terms of it.</remarks>
    private static bool IsScopeMarker(IStatement result)
        => NodeTests.IsExportAssignment((Node)result) || NodeTests.IsExportDeclaration((Node)result);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2138-2140 — <c>hasScopeMarker</c>.</summary>
    public static bool HasScopeMarker(IList<IStatement> statements) => Core.Some(statements, IsScopeMarker);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2143-2145 — <c>needsScopeMarker</c>.</summary>
    public static bool NeedsScopeMarker(IStatement result)
        => !IsAnyImportOrReExport((Node)result)
            && !NodeTests.IsExportAssignment((Node)result)
            && !HasSyntacticModifier((Node)result, ModifierFlags.Export)
            && !IsAmbientModule((Node)result);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2148-2151 — <c>isExternalModuleIndicator</c>.</summary>
    public static bool IsExternalModuleIndicator(IStatement result)
    {
        // Exported top-level member indicates moduleness
        return IsAnyImportOrReExport((Node)result) || NodeTests.IsExportAssignment((Node)result) || HasSyntacticModifier((Node)result, ModifierFlags.Export);
    }

    /// <summary>ported from: src/compiler/utilities.ts:5459-5461 — <c>isPrivateIdentifierSymbol</c>.</summary>
    public static bool IsPrivateIdentifierSymbol(ISymbol symbol) => Core.StartsWith(symbol.EscapedName ?? "", "__#");

    /// <summary>ported from: src/compiler/utilities.ts:2148-2150 — <c>getNonAugmentationDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>Declaration</c> face; the port answers <c>Node?</c> because its one landed caller
    /// hands the result straight to <c>Utilities.GetSourceFileOfNode(Node?)</c>, and the port's <c>IDeclaration</c> face
    /// (which <c>ISymbol.Declarations</c> carries) is not implicitly convertible to that parameter. TS's
    /// <c>declarations?.find(…)</c> is <c>Core.Find</c> over the ordered list, whose predicate answers the same
    /// <c>isExternalModuleAugmentation</c>/<c>isGlobalScopeAugmentation</c> pair.
    /// </remarks>
    public static Node? GetNonAugmentationDeclaration(ISymbol symbol)
        => (Node?)Core.Find(
            symbol.Declarations,
            (d, _) => !IsExternalModuleAugmentation((Node)d)
                && !(NodeTests.IsModuleDeclaration((Node)d) && IsGlobalScopeAugmentation((ModuleDeclaration)(Node)d)));

    /// <summary>ported from: src/compiler/utilities.ts:4330-4347 — <c>canHaveModuleSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the <c>CanHaveModuleSpecifier</c> union, which the port answers as <c>bool</c> (rule
    /// (d)) — its caller only tests it before reading the specifier through <c>TryGetModuleSpecifierFromDeclaration</c>.
    /// </remarks>
    public static bool CanHaveModuleSpecifier(Node? node)
    {
        switch (node?.Kind)
        {
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.BindingElement:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ImportClause:
            case SyntaxKind.NamespaceExport:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ImportType:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:992-994 — <c>getSourceFileOfModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>module.valueDeclaration || getNonAugmentationDeclaration(module)</c> is the <c>??</c> below;
    /// the <c>valueDeclaration</c> slot is the port's <c>IDeclaration?</c>, so it is cast to the <c>Node?</c> face
    /// <c>GetSourceFileOfNode</c> takes before the <c>??</c> (TS has one structural type there).
    /// </remarks>
    public static SourceFile? GetSourceFileOfModule(ISymbol module)
        => GetSourceFileOfNode((Node?)module.ValueDeclaration ?? GetNonAugmentationDeclaration(module));

    /// <summary>ported from: src/compiler/utilities.ts:5189-5192 — <c>getPropertyAssignmentAliasLikeExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>PropertyAssignment | ShorthandPropertyAssignment | PropertyAccessExpression</c>
    /// union, whose port face is the widened <c>Node</c> the landed caller produces with its own cast; the last arm reads
    /// <c>(node.parent as BinaryExpression).right</c>, and the first two the slots the reference names.
    /// </remarks>
    public static Expression GetPropertyAssignmentAliasLikeExpression(Node node)
        => node.Kind == SyntaxKind.ShorthandPropertyAssignment ? ((ShorthandPropertyAssignment)node).Name!
            : node.Kind == SyntaxKind.PropertyAssignment ? ((PropertyAssignment)node).Initializer!
            : ((BinaryExpression)node.Parent!).Right!;
}
