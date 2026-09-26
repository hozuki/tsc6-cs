#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/declarations.ts:1865-1977 — the module-level helpers the factory's closures
// call: `isAlwaysType` (1865-1871), `maskModifiers` (1873-1875), `maskModifierFlags` (1877-1888),
// `canHaveLiteralInitializer` (1890-1901), `isPreservedDeclarationStatement` (1916-1932) and `isProcessedComponent`
// (1954-1977) — plus the reference module's own dependencies that this port does not yet declare anywhere
// (see each member's note; they are reported as this slice's cross-file needs).
/// <summary>
/// ported from: src/compiler/transformers/declarations.ts:1865-1977 — the two unions' guards and the three
/// module-level helper functions, plus the seven <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> members the declaration
/// transformer reads that have no other home in the port yet.
/// </summary>
/// <remarks>
/// PORT-NOTE (home, §15.15): TS declares the first five members at the bottom of
/// <c>transformers/declarations.ts</c> and the imported ones in <c>utilities.ts</c>/<c>utilitiesPublic.ts</c>. The
/// latter are declared here, private to the declaration transformer, for the reason the scope ruling gives: they are
/// small, self-contained reads of the parse tree and the host, and the files that should own them are other slices'.
/// Each carries its reference line and the member it belongs to, so moving it later is a rename; they are named in this
/// slice's report.
/// </remarks>
internal static class DeclarationsTransformSupport
{
    /// <summary>ported from: src/compiler/transformers/declarations.ts:1865-1871 — <c>isAlwaysType</c>.</summary>
    internal static bool IsAlwaysType(Node node) => node.Kind == SyntaxKind.InterfaceDeclaration;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1873-1875 — <c>maskModifiers</c>.</summary>
    /// <remarks>PORT-NOTE: TS answers <c>Modifier[] | undefined</c>; the model spells the same list
    /// <c>List&lt;Node&gt;?</c> at every <c>update*</c> slot that takes modifiers, which is why the conversion is here
    /// rather than at each call site.</remarks>
    internal static List<Node>? MaskModifiers(NodeFactory factory, Node node, ModifierFlags? modifierMask, ModifierFlags? modifierAdditions)
        => AsNodeList(factory.CreateModifiersFromModifierFlags(MaskModifierFlags(node, modifierMask, modifierAdditions)));

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1877-1888 — <c>maskModifierFlags</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's two defaults are the two <c>null</c> states of the port's nullable
    /// parameters (C# default arguments cannot name <c>ModifierFlags.All ^ ModifierFlags.Public</c> in a way that
    /// reads as the reference's, so the two <c>??</c> arms state them).</remarks>
    internal static ModifierFlags MaskModifierFlags(Node node, ModifierFlags? modifierMask = null, ModifierFlags? modifierAdditions = null)
    {
        var flags = (Utilities.GetEffectiveModifierFlags(node) & (modifierMask ?? (ModifierFlags.All ^ ModifierFlags.Public)))
            | (modifierAdditions ?? ModifierFlags.None);
        if ((flags & ModifierFlags.Default) != 0 && (flags & ModifierFlags.Export) == 0)
        {
            // A non-exported default is a nonsequitor - we usually try to remove all export modifiers
            // from statements in ambient declarations; but a default export must retain its export modifier to be syntactically valid
            flags ^= ModifierFlags.Export;
        }
        if ((flags & ModifierFlags.Default) != 0 && (flags & ModifierFlags.Ambient) != 0)
        {
            flags ^= ModifierFlags.Ambient; // `declare` is never required alongside `default` (and would be an error if printed)
        }
        return flags;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1890-1901 — <c>canHaveLiteralInitializer</c>.</summary>
    /// <remarks>PORT-NOTE: TS's guard narrows to <c>CanHaveLiteralInitializer</c>; the port answers <c>bool</c>
    /// (rule (d)) and the members that read the initializer go through <c>InitializerOf</c>, the same pair of tests the
    /// reference's <c>node.initializer</c> makes after the narrowing.</remarks>
    internal static bool CanHaveLiteralInitializer(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
                return !Utilities.HasEffectiveModifier(node, ModifierFlags.Private);
            case SyntaxKind.Parameter:
            case SyntaxKind.VariableDeclaration:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1916-1932 — <c>isPreservedDeclarationStatement</c>.</summary>
    internal static bool IsPreservedDeclarationStatement(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.VariableStatement:
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ExportDeclaration:
            case SyntaxKind.ExportAssignment:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1954-1977 — <c>isProcessedComponent</c>.</summary>
    internal static bool IsProcessedComponent(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ConstructSignature:
            case SyntaxKind.Constructor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.CallSignature:
            case SyntaxKind.IndexSignature:
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.ExpressionWithTypeArguments:
            case SyntaxKind.TypeReference:
            case SyntaxKind.ConditionalType:
            case SyntaxKind.FunctionType:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.ImportType:
                return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:1977 — <c>isJSDocImportTag</c> (utilitiesPublic.ts).</summary>
    internal static bool IsJSDocImportTag(Node node) => node.Kind == SyntaxKind.JSDocImportTag;

    /// <summary>ported from: src/compiler/utilities.ts:12030-12052 — <c>isPrimitiveLiteralValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): this is one of the members this slice's report names as unlanded elsewhere; the checker states
    /// a private copy for <c>expressionToTypeNode</c> (<c>Checker/SyntacticNodeBuilder.Kinds.cs:705</c>) which this
    /// module cannot reach. The body is the reference's, unchanged.
    /// </remarks>
    internal static bool IsPrimitiveLiteralValue(Expression node, bool includeBigInt = true)
    {
        switch (node.Kind)
        {
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                return true;
            case SyntaxKind.BigIntLiteral:
                return includeBigInt;
            case SyntaxKind.PrefixUnaryExpression:
                var prefix = (PrefixUnaryExpression)node;
                if (prefix.Operator == SyntaxKind.MinusToken)
                {
                    return NodeTests.IsNumericLiteral(prefix.Operand!) || (includeBigInt && NodeTests.IsBigIntLiteral(prefix.Operand!));
                }
                if (prefix.Operator == SyntaxKind.PlusToken)
                {
                    return NodeTests.IsNumericLiteral(prefix.Operand!);
                }
                return false;
            default:
                // PORT-NOTE: the reference's `assertType<never>(node); return false` is a type-only assertion, so the
                // port keeps the same `return false`.
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:12056-12061 — <c>unwrapParenthesizedExpression</c>.</summary>
    /// <remarks>PORT-NOTE (home): see <see cref="IsPrimitiveLiteralValue"/>; this is the second unlanded member the
    /// report names. The reference's loop is stated as written (it strips <c>ParenthesizedExpression</c> only, where
    /// <c>Utilities.SkipParentheses</c> also strips assertions).</remarks>
    internal static Expression UnwrapParenthesizedExpression(Expression o)
    {
        while (o.Kind == SyntaxKind.ParenthesizedExpression)
        {
            o = ((ParenthesizedExpression)o).Expression!;
        }
        return o;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts — <c>isVarUsing</c>.</summary>
    /// <remarks>PORT-NOTE (home): the third unlanded member (see <see cref="IsPrimitiveLiteralValue"/>).</remarks>
    internal static bool IsVarUsing(Node node)
        => node.Kind == SyntaxKind.VariableDeclarationList && (((VariableDeclarationList)node).Flags & NodeFlags.Using) != 0;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts — <c>isVarAwaitUsing</c>.</summary>
    /// <remarks>PORT-NOTE (home): the fourth unlanded member (see <see cref="IsPrimitiveLiteralValue"/>).</remarks>
    internal static bool IsVarAwaitUsing(Node node)
        => node.Kind == SyntaxKind.VariableDeclarationList && (((VariableDeclarationList)node).Flags & NodeFlags.AwaitUsing) != 0;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2648-2671 — <c>isInternalDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE (home): the fifth unlanded member (see <see cref="IsPrimitiveLiteralValue"/>); its
    /// <c>getLeadingCommentRangesOfNode</c> helper (utilities.ts:2758-2760) is the private member below. TS's
    /// <c>parseTreeNode.parent as SignatureDeclaration</c> read is <see cref="ParametersOf"/>, and its
    /// <c>forEach(leadingCommentRanges, …)</c> is the loop below (the reference's <c>!!</c> over the callback makes the
    /// two the same test).
    /// </remarks>
    internal static bool IsInternalDeclaration(Node node, SourceFile? sourceFile = null)
    {
        sourceFile ??= Utilities.GetSourceFileOfNode(node)!;
        var parseTreeNode = Utilities.GetParseTreeNode(node);
        if (parseTreeNode is not null && parseTreeNode.Kind == SyntaxKind.Parameter)
        {
            var parameters = ParametersOf(parseTreeNode.Parent!);
            var paramIdx = parameters!.IndexOf((ParameterDeclaration)parseTreeNode);
            var previousSibling = paramIdx > 0 ? parameters[paramIdx - 1] : null;
            var text = sourceFile.Text!;
            var commentRanges = previousSibling is not null
                ? Core.Concatenate(
                    // to handle
                    // ... parameters, /** @internal */
                    // public param: string
                    ScannerUtilities.GetTrailingCommentRanges(text, ScannerUtilities.SkipTrivia(text, previousSibling.End + 1, /*stopAfterLineBreak*/ false, /*stopAtComments*/ true)),
                    ScannerUtilities.GetLeadingCommentRanges(text, node.Pos))
                : ScannerUtilities.GetTrailingCommentRanges(text, ScannerUtilities.SkipTrivia(text, node.Pos, /*stopAfterLineBreak*/ false, /*stopAtComments*/ true));
            return Js.IsTruthy(Core.Length(commentRanges)) && HasInternalAnnotation(Core.Last(commentRanges!), sourceFile);
        }
        var leadingCommentRanges = parseTreeNode is not null ? GetLeadingCommentRangesOfNode(parseTreeNode, sourceFile) : null;
        foreach (var range in leadingCommentRanges ?? [])
        {
            if (HasInternalAnnotation(range, sourceFile)) return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2758-2760 — <c>getLeadingCommentRangesOfNode</c>.</summary>
    private static IList<CommentRange>? GetLeadingCommentRangesOfNode(Node node, SourceFile sourceFileOfNode)
        => node.Kind != SyntaxKind.JsxText ? ScannerUtilities.GetLeadingCommentRanges(sourceFileOfNode.Text!, node.Pos) : null;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2643-2646 — <c>hasInternalAnnotation</c>.</summary>
    private static bool HasInternalAnnotation(ICommentRange range, SourceFile sourceFile)
    {
        var comment = sourceFile.Text!.Substring(range.Pos, range.End - range.Pos);
        return comment.Contains("@internal", StringComparison.Ordinal);
    }

    /// <summary>ported from: src/compiler/utilities.ts:6500-6502 — <c>getResolvedExternalModuleName</c>.</summary>
    /// <remarks>PORT-NOTE (home): the sixth unlanded member (see <see cref="IsPrimitiveLiteralValue"/>).
    /// <c>host.getCanonicalFileName</c> is the set-get slot <see cref="ISourceFileMayBeEmittedHost"/> widens to
    /// <c>object?</c>; the cast is the same one <c>Utilities.GetSourceFilePathInNewDir</c> makes
    /// (<c>Utilities/Utilities.EmitPaths.cs:136</c>).</remarks>
    internal static string GetResolvedExternalModuleName(IEmitHost host, SourceFile file, SourceFile? referenceFile = null)
        => file.ModuleName ?? GetExternalModuleNameFromPath(host, file.FileName!, referenceFile?.FileName);

    /// <summary>The <c>host.getCanonicalFileName</c> read of the three members above.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ResolveModuleNameResolutionHost</c> (utilities.ts:6493-6497) declares
    /// <c>getCanonicalFileName(p: string): string</c>; the port has no such interface, and the only host the declaration
    /// transformer holds is <see cref="IEmitHost"/>, whose <c>getCanonicalFileName</c> is the set-get slot
    /// <c>ISourceFileMayBeEmittedHost</c> widens to <c>object?</c> — the cast is the one
    /// <c>Utilities.GetSourceFilePathInNewDir</c> makes (<c>Utilities/Utilities.EmitPaths.cs:136</c>).</remarks>
    private static Core.GetCanonicalFileName CanonicalFileName(IEmitHost host)
        => (Core.GetCanonicalFileName)host.GetCanonicalFileName!;

    /// <summary>A local helper of <c>getExternalModuleNameFromDeclaration</c> (utilities.ts:6504-6506).</summary>
    private static string GetCanonicalAbsolutePath(IEmitHost host, string path)
        => CanonicalFileName(host)(Paths.GetNormalizedAbsolutePath(path, Utilities.EmitHostCurrentDirectory(host)));

    /// <summary>ported from: src/compiler/utilities.ts:6509-6521 — <c>getExternalModuleNameFromDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE (home): the seventh unlanded member (see <see cref="IsPrimitiveLiteralValue"/>). The
    /// reference's <c>getExternalModuleName(declaration)</c> is <see cref="Utilities.GetExternalModuleName"/>, which the
    /// port already declares for the module-name resolver.</remarks>
    internal static string? GetExternalModuleNameFromDeclaration(IEmitHost host, IEmitResolver resolver, Node declaration)
    {
        var file = resolver.GetExternalModuleFileFromDeclaration(declaration);
        if (file is null || file.IsDeclarationFile)
        {
            return null;
        }
        // If the declaration already uses a non-relative name, and is outside the common source directory, continue to use it
        var specifier = Utilities.GetExternalModuleName(declaration);
        if (
            specifier is not null && Utilities.IsStringLiteralLike(specifier) && !Paths.PathIsRelative(((StringLiteral)specifier).Text!)
            && !GetCanonicalAbsolutePath(host, file.Path!).Contains(
                GetCanonicalAbsolutePath(host, Paths.EnsureTrailingDirectorySeparator(host.GetCommonSourceDirectory())),
                StringComparison.Ordinal)
        )
        {
            return null;
        }
        return GetResolvedExternalModuleName(host, file);
    }

    /// <summary>ported from: src/compiler/utilities.ts:6528-6536 — <c>getExternalModuleNameFromPath</c>.</summary>
    /// <remarks>PORT-NOTE (home): the eighth unlanded member (see <see cref="IsPrimitiveLiteralValue"/>). The local
    /// <c>getCanonicalFileName</c> wrapper the reference declares is the delegate cast made at both uses.</remarks>
    internal static string GetExternalModuleNameFromPath(IEmitHost host, string fileName, string? referencePath = null)
    {
        var getCanonicalFileName = CanonicalFileName(host);
        var dir = Paths.ToPath(referencePath is not null ? Paths.GetDirectoryPath(referencePath) : host.GetCommonSourceDirectory(), Utilities.EmitHostCurrentDirectory(host), getCanonicalFileName);
        var filePath = Paths.GetNormalizedAbsolutePath(fileName, Utilities.EmitHostCurrentDirectory(host));
        var relativePath = Paths.GetRelativePathToDirectoryOrUrl(dir, filePath, dir, getCanonicalFileName, /*isAbsolutePathAnUrl*/ false);
        var extensionless = Utilities.RemoveFileExtension(relativePath);
        return referencePath is not null ? Paths.EnsurePathIsNonModuleName(extensionless) : extensionless;
    }

    /// <summary>ported from: src/compiler/utilities.ts:711-717 — <c>isIdentifierText</c>.</summary>
    /// <remarks>PORT-NOTE (home): the ninth unlanded member (see <see cref="IsPrimitiveLiteralValue"/>). The
    /// reference's <c>codePointAt</c>/<c>charSize</c> are <see cref="ScannerUtilities.CodePointAt"/>/
    /// <see cref="ScannerUtilities.CharSize"/>.</remarks>
    internal static bool IsIdentifierText(string name, ScriptTarget languageVersion, LanguageVariant? identifierVariant = null)
    {
        var ch = ScannerUtilities.CodePointAt(name, 0);
        if (!ScannerUtilities.IsIdentifierStart(ch, languageVersion)) return false;
        for (var i = ScannerUtilities.CharSize(ch); i < name.Length; i += ScannerUtilities.CharSize(ch))
        {
            if (!ScannerUtilities.IsIdentifierPart(ch = ScannerUtilities.CodePointAt(name, i), languageVersion, identifierVariant ?? LanguageVariant.Standard)) return false;
        }
        return true;
    }

    /// <summary>The <c>SignatureDeclaration.parameters</c> read of <c>isInternalDeclaration</c>.</summary>
    private static NodeArray<ParameterDeclaration>? ParametersOf(Node node) => node switch
    {
        ISignatureDeclarationBase declaration => declaration.Parameters,
        _ => null,
    };

    /// <summary>The <c>Modifier[]</c> face of an <c>ensureModifiers</c> answer.</summary>
    /// <remarks>PORT-NOTE: three <c>update*</c> slots — <c>updatePropertySignature</c>, <c>updateMethodSignature</c>
    /// and <c>updateConstructorTypeNode</c> — are declared over the model's <c>ModifierToken</c> because TS declares
    /// those three nodes' <c>modifiers</c> as <c>Modifier[]</c>, while the other thirty take <c>ModifierLike[]</c>. The
    /// conversion is stated once here; every element is a modifier at run time, because that is what
    /// <c>ensureModifiers</c> filtered for.</remarks>
    internal static List<ModifierToken>? AsModifierTokens(List<Node>? nodes)
    {
        if (nodes is null) return null;
        var result = new List<ModifierToken>(nodes.Count);
        foreach (var node in nodes)
        {
            result.Add((ModifierToken)node);
        }
        return result;
    }

    /// <summary>The <c>ModifierLike[]</c> face of a <c>createModifiersFromModifierFlags</c> answer.</summary>
    internal static List<Node>? AsNodeList(List<ModifierToken>? tokens)
    {
        if (tokens is null) return null;
        var result = new List<Node>(tokens.Count);
        foreach (var token in tokens)
        {
            result.Add(token);
        }
        return result;
    }

    /// <summary>ported from: src/compiler/visitorPublic.ts:289-311 — <c>visitArray(node.modifiers, …)</c> as <c>ensureModifiers</c> calls it.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>visitArray(node.modifiers, n =&gt; tryCast(n, isModifier), isModifier)</c> answers the input
    /// array itself when no element was dropped and a fresh array otherwise, which the two arms below reproduce. The
    /// one conversion the model needs is the <c>Modifier</c>-typed slots (<c>NodeArray&lt;ModifierToken&gt;</c>), whose
    /// arrays are not <c>List&lt;Node&gt;</c> at run time (§12.9) — those cannot carry a decorator, so the reference's
    /// filter answers the same elements and the port copies them into the <c>ModifierLike</c> list type.
    /// </remarks>
    internal static List<Node>? FilterModifiers(Node node)
    {
        var modifiers = Utilities.ModifiersOf(node);
        if (modifiers is null) return null;
        if (modifiers is List<Node> list)
        {
            var dropped = false;
            foreach (var modifier in list)
            {
                if (!Utilities.IsModifier(modifier)) { dropped = true; break; }
            }
            if (!dropped) return list;
            var filtered = new List<Node>(list.Count);
            foreach (var modifier in list)
            {
                if (Utilities.IsModifier(modifier)) filtered.Add(modifier);
            }
            return filtered;
        }
        var result = new List<Node>();
        foreach (var modifier in modifiers)
        {
            result.Add(modifier);
        }
        return result;
    }
}
