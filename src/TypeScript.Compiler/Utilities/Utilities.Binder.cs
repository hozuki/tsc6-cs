namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts, utilitiesPublic.ts
/// <summary>
/// The <c>utilities.ts</c> / <c>utilitiesPublic.ts</c> slice that Layer 4 (the binder) reaches for ahead of M5.
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no module-level functions, so the free functions of both modules land on this partial class
/// as each batch needs them, every member carrying the line range it came from — the same arrangement as the M4 slices
/// in <c>Utilities.cs</c> and the Layer-5 slice in <c>Utilities.Parser.cs</c> (docs/architecture.md §15.9). This file is
/// M5-0's share: node and symbol predicates with no <c>CompilerOptions</c> and no checker dependency, plus the helpers
/// their bodies are defined in terms of. The JS-assignment/expando half of the same module lives in
/// <c>Utilities.JsExpando.cs</c> (its own batch) and is <em>called</em> from here — <c>getAssignmentDeclarationKind</c>,
/// <c>getElementOrPropertyAccessArgumentExpressionOrName</c>, <c>isBindableStaticElementAccessExpression</c> and
/// <c>isAccessExpression</c> — the way TS's single module has them.
/// <para>
/// PORT-NOTE: TS's <c>T | undefined</c> is <c>T?</c> (§5.1), its <c>readonly T[]</c> is <c>IList&lt;T&gt;</c> for
/// parameters (§10.2), <c>DeclarationName</c> is <c>object?</c> and <c>SymbolTable</c> is
/// <c>Dictionary&lt;string, ISymbol&gt;</c> (§12.3, §12.5). A type-guard predicate (<c>node is X</c>) answers
/// <c>bool</c> here, because C# cannot narrow through a predicate: the caller casts to the class or interface it needs,
/// and each such member says so in its own note.
/// </para>
/// <para>
/// PORT-NOTE: the effective-modifier family — <c>getEffectiveModifierFlags</c>, <c>getCombinedModifierFlags</c>,
/// <c>getRawJSDocModifierFlagsNoCache</c> and the JSDoc tag lookups those need — belongs to a later M5 slice and is not
/// here. The syntactic half is complete, cache included.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // Symbols
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:644-652 — <c>createSymbolTable</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS returns <c>Map&lt;__String, Symbol&gt;</c>, which is the <c>SymbolTable</c> alias
    /// (types.ts:6205). The port keeps it as the ordered <see cref="SymbolTable"/> rather than the §12.3
    /// <c>Dictionary</c> mapping, because the checker reads member/property order out of these tables
    /// (docs/architecture.md §5 rule 17).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>symbol.escapedName</c> is <c>__String</c> upstream — non-optional by construction, because every
    /// symbol is built through <c>escapeLeadingUnderscores</c>. <see cref="ISymbol.EscapedName"/> is the generated
    /// interface's <c>string?</c>, so the read is a checked one: a symbol without a name is a factory bug, and it must
    /// fail here rather than silently key the empty string.
    /// </para>
    /// </remarks>
    public static SymbolTable CreateSymbolTable(IList<ISymbol>? symbols = null)
    {
        var result = new SymbolTable();
        if (symbols is not null)
        {
            foreach (var symbol in symbols)
            {
                result[Debug.CheckDefined(symbol.EscapedName, "a symbol must have an escaped name")] = symbol;
            }
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Can this node carry a symbol table, a locals table, a name, modifiers? (utilitiesPublic.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2283-2319 — <c>canHaveLocals</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>HasLocals</c>; C# cannot narrow through a predicate, so the port answers
    /// <c>bool</c> and a caller that needs the table casts to <see cref="ILocalsContainer"/>.
    /// </remarks>
    public static bool CanHaveLocals(Node node) => node.Kind switch
    {
        SyntaxKind.ArrowFunction
            or SyntaxKind.Block
            or SyntaxKind.CallSignature
            or SyntaxKind.CaseBlock
            or SyntaxKind.CatchClause
            or SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.ConditionalType
            or SyntaxKind.Constructor
            or SyntaxKind.ConstructorType
            or SyntaxKind.ConstructSignature
            or SyntaxKind.ForStatement
            or SyntaxKind.ForInStatement
            or SyntaxKind.ForOfStatement
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.FunctionExpression
            or SyntaxKind.FunctionType
            or SyntaxKind.GetAccessor
            or SyntaxKind.IndexSignature
            or SyntaxKind.JSDocCallbackTag
            or SyntaxKind.JSDocEnumTag
            or SyntaxKind.JSDocFunctionType
            or SyntaxKind.JSDocSignature
            or SyntaxKind.JSDocTypedefTag
            or SyntaxKind.MappedType
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.MethodSignature
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.SetAccessor
            or SyntaxKind.SourceFile
            or SyntaxKind.TypeAliasDeclaration => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2208-2280 — <c>canHaveSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>Declaration</c>; C# cannot narrow through a predicate, so the port answers
    /// <c>bool</c> and a caller that needs the symbol casts to <see cref="IDeclaration"/>. The kind list is kept in TS's
    /// order so it can be diffed against the source.
    /// </remarks>
    public static bool CanHaveSymbol(Node node) => node.Kind switch
    {
        SyntaxKind.ArrowFunction
            or SyntaxKind.BinaryExpression
            or SyntaxKind.BindingElement
            or SyntaxKind.CallExpression
            or SyntaxKind.CallSignature
            or SyntaxKind.ClassDeclaration
            or SyntaxKind.ClassExpression
            or SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.Constructor
            or SyntaxKind.ConstructorType
            or SyntaxKind.ConstructSignature
            or SyntaxKind.ElementAccessExpression
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.EnumMember
            or SyntaxKind.ExportAssignment
            or SyntaxKind.ExportDeclaration
            or SyntaxKind.ExportSpecifier
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.FunctionExpression
            or SyntaxKind.FunctionType
            or SyntaxKind.GetAccessor
            or SyntaxKind.Identifier
            or SyntaxKind.ImportClause
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.ImportSpecifier
            or SyntaxKind.IndexSignature
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.JSDocCallbackTag
            or SyntaxKind.JSDocEnumTag
            or SyntaxKind.JSDocFunctionType
            or SyntaxKind.JSDocParameterTag
            or SyntaxKind.JSDocPropertyTag
            or SyntaxKind.JSDocSignature
            or SyntaxKind.JSDocTypedefTag
            or SyntaxKind.JSDocTypeLiteral
            or SyntaxKind.JsxAttribute
            or SyntaxKind.JsxAttributes
            or SyntaxKind.JsxSpreadAttribute
            or SyntaxKind.MappedType
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.MethodSignature
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.NamedTupleMember
            or SyntaxKind.NamespaceExport
            or SyntaxKind.NamespaceExportDeclaration
            or SyntaxKind.NamespaceImport
            or SyntaxKind.NewExpression
            or SyntaxKind.NoSubstitutionTemplateLiteral
            or SyntaxKind.NumericLiteral
            or SyntaxKind.ObjectLiteralExpression
            or SyntaxKind.Parameter
            or SyntaxKind.PropertyAccessExpression
            or SyntaxKind.PropertyAssignment
            or SyntaxKind.PropertyDeclaration
            or SyntaxKind.PropertySignature
            or SyntaxKind.SetAccessor
            or SyntaxKind.ShorthandPropertyAssignment
            or SyntaxKind.SourceFile
            or SyntaxKind.SpreadAssignment
            or SyntaxKind.StringLiteral
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.TypeLiteral
            or SyntaxKind.TypeParameter
            or SyntaxKind.VariableDeclaration => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:615-617 — <c>isParameterPropertyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>ParameterPropertyDeclaration</c> (<c>ParameterDeclaration</c> whose parent is
    /// a constructor and whose name is an <c>Identifier</c>); C# cannot narrow through a predicate, so the port answers
    /// <c>bool</c> and the caller casts. The <c>parent</c> parameter stays a <c>Node</c> — TS spells it that way on
    /// purpose, so a caller holding only the parent pointer can ask the question.
    /// </remarks>
    public static bool IsParameterPropertyDeclaration(Node node, Node parent)
        => NodeTests.IsParameter(node)
            && HasSyntacticModifier(node, ModifierFlags.ParameterPropertyModifier)
            && parent.Kind == SyntaxKind.Constructor;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:865-868 — <c>identifierToKeywordKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>KeywordSyntaxKind | undefined</c>; the port has no separate keyword enum, so the answer
    /// is the <c>SyntaxKind?</c> the scanner's token map already holds. <c>escapedText</c> is <c>__String</c> there and
    /// <c>string?</c> here — no text means the lookup misses, which is exactly the <c>undefined</c> TS answers — so the
    /// read falls back to the empty string instead of asserting.
    /// </remarks>
    public static SyntaxKind? IdentifierToKeywordKind(Identifier node)
    {
        var token = ScannerUtilities.StringToToken(node.EscapedText ?? "");
        return token is not null && IsKeyword(token.Value) ? token : null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:931-939 — <c>nodeHasName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS tests <c>isNamedDeclaration(statement) &amp;&amp; isIdentifier(statement.name)</c> and then reads the
    /// name; the port collapses the two tests and the read into one recursive pattern, which is the same question.
    /// </remarks>
    public static bool NodeHasName(Node statement, Identifier name)
    {
        if (statement is INamedDeclaration { Name: Identifier identifier } && IdText(identifier) == IdText(name))
        {
            return true;
        }

        if (statement is VariableStatement { DeclarationList: { } declarationList }
            && Core.Some(declarationList.Declarations, declaration => NodeHasName(declaration, name)))
        {
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Declarations and their names (utilitiesPublic.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2321-2358 — <c>isDeclarationKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes the table as an <c>||</c> chain; the port spells the same table as a kind switch, like its
    /// neighbours <c>IsDeclarationStatementKind</c> and <c>IsStatementKindButNotDeclarationKind</c> in Utilities.cs.
    /// </remarks>
    private static bool IsDeclarationKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.ArrowFunction
            or SyntaxKind.BindingElement
            or SyntaxKind.ClassDeclaration
            or SyntaxKind.ClassExpression
            or SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.Constructor
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.EnumMember
            or SyntaxKind.ExportSpecifier
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.FunctionExpression
            or SyntaxKind.GetAccessor
            or SyntaxKind.ImportClause
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.ImportSpecifier
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.JsxAttribute
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.MethodSignature
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.NamespaceExportDeclaration
            or SyntaxKind.NamespaceImport
            or SyntaxKind.NamespaceExport
            or SyntaxKind.Parameter
            or SyntaxKind.PropertyAssignment
            or SyntaxKind.PropertyDeclaration
            or SyntaxKind.PropertySignature
            or SyntaxKind.SetAccessor
            or SyntaxKind.ShorthandPropertyAssignment
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.TypeParameter
            or SyntaxKind.VariableDeclaration
            or SyntaxKind.JSDocTypedefTag
            or SyntaxKind.JSDocCallbackTag
            or SyntaxKind.JSDocPropertyTag
            or SyntaxKind.NamedTupleMember => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2398-2404 — <c>isDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>NamedDeclaration</c>; C# cannot narrow through a predicate, so the port
    /// answers <c>bool</c> and a caller that reads the name casts to <see cref="INamedDeclaration"/>. TS's
    /// <c>node.parent &amp;&amp;</c> truthiness test is the port's null test on the nullable parent pointer.
    /// </remarks>
    public static bool IsDeclaration(Node node)
    {
        if (node.Kind == SyntaxKind.TypeParameter)
        {
            return (node.Parent is { } parent && parent.Kind != SyntaxKind.JSDocTemplateTag) || IsInJSFile(node);
        }

        return IsDeclarationKind(node.Kind);
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:941-943 — <c>getNameOfJSDocTypedef</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Identifier | PrivateIdentifier | undefined</c>; both are nodes and the caller narrows,
    /// so the port answers <c>Node?</c>. The <c>||</c> is a node-or-undefined fallback (a node is never falsy), hence
    /// the <c>??</c>.
    /// </remarks>
    public static Node? GetNameOfJSDocTypedef(JSDocTypedefTag declaration)
        => (Node?)declaration.Name ?? NameForNamelessJSDocTypedef(declaration);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:882-923 — <c>nameForNamelessJSDocTypedef</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>Identifier | PrivateIdentifier | undefined</c>, and reads the host's
    /// <c>expression</c>/<c>statement</c>/<c>declarationList</c> slots as non-optional; the generated model declares
    /// them nullable, so each read is guarded — a null slot answers no name, which is what TS's own
    /// <c>if (!hostNode)</c> guard and its <c>break</c> arms do for the shapes they cover.
    /// </remarks>
    private static Node? NameForNamelessJSDocTypedef(Node declaration)
    {
        // A JSDocTypedefTag/JSDocEnumTag hangs off a JSDoc comment whose parent is the host node.
        var hostNode = declaration.Parent?.Parent;
        if (hostNode is null) return null;

        // Covers classes, functions - any named declaration host node.
        if (IsDeclaration(hostNode)) return GetDeclarationIdentifier(hostNode);

        // Covers remaining cases (answering null if none match).
        switch (hostNode)
        {
            case VariableStatement statement:
                if (statement.DeclarationList is { } declarationList
                    && declarationList.Declarations is { Count: > 0 } declarations)
                {
                    return GetDeclarationIdentifier(declarations[0]);
                }

                break;
            case ExpressionStatement statement:
                var expression = statement.Expression;
                if (expression is BinaryExpression binary
                    && binary.OperatorToken!.Kind == SyntaxKind.EqualsToken)
                {
                    expression = binary.Left;
                }

                switch (expression)
                {
                    case PropertyAccessExpression propertyAccess:
                        return propertyAccess.Name;
                    case ElementAccessExpression elementAccess when elementAccess.ArgumentExpression is Identifier argument:
                        return argument;
                }

                break;
            case ParenthesizedExpression statement:
                return statement.Expression is { } parenthesized ? GetDeclarationIdentifier(parenthesized) : null;
            case LabeledStatement statement:
                if (statement.Statement is { } labeled && (IsDeclaration((Node)labeled) || IsExpression((Node)labeled)))
                {
                    return GetDeclarationIdentifier((Node)labeled);
                }

                break;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:925-928 — <c>getDeclarationIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>name &amp;&amp; isIdentifier(name) ? name : undefined</c> is one type test here, which also
    /// covers the absent name.
    /// </remarks>
    private static Identifier? GetDeclarationIdentifier(Node node)
    {
        var name = GetNameOfDeclaration(node);
        return name as Identifier;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:951-995 — <c>getNonAssignedNameOfDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>DeclarationName | undefined</c>; <c>DeclarationName</c> is the port's <c>object?</c>
    /// (§12.5), and the caller narrows to the node it wants. TS's final <c>(declaration as NamedDeclaration).name</c>
    /// is a duck-typed slot read, not a type test, so it answers for the declarations the model leaves out of
    /// <see cref="INamedDeclaration"/> — <c>JsxAttribute</c> and <c>NamedTupleMember</c> — and the port reads it
    /// through <see cref="GetNamedDeclarationName"/> for that reason. A declaration with no name slot answers
    /// <c>null</c>, which is TS's <c>undefined</c>. The JSDoc arms read <c>name.kind</c> off a slot the model declares
    /// nullable; a null name therefore answers no name instead of throwing.
    /// <para>
    /// PORT-NOTE: the JS-assignment arms lean on the expando batch inside this same partial class —
    /// <c>getAssignmentDeclarationKind</c>, <c>getElementOrPropertyAccessArgumentExpressionOrName</c> and
    /// <c>isBindableStaticElementAccessExpression</c> — as TS's single module does.
    /// </para>
    /// </remarks>
    public static object? GetNonAssignedNameOfDeclaration(Node declaration)
    {
        switch (declaration.Kind)
        {
            case SyntaxKind.Identifier:
                return declaration;
            case SyntaxKind.JSDocPropertyTag:
            case SyntaxKind.JSDocParameterTag:
                if (((JSDocPropertyLikeTag)declaration).Name is QualifiedName qualifiedName) return qualifiedName.Right;
                break;
            case SyntaxKind.CallExpression:
            case SyntaxKind.BinaryExpression:
                switch (GetAssignmentDeclarationKind(declaration))
                {
                    case AssignmentDeclarationKind.ExportsProperty:
                    case AssignmentDeclarationKind.ThisProperty:
                    case AssignmentDeclarationKind.Property:
                    case AssignmentDeclarationKind.PrototypeProperty:
                        return GetElementOrPropertyAccessArgumentExpressionOrName(((BinaryExpression)declaration).Left!);
                    case AssignmentDeclarationKind.ObjectDefinePropertyValue:
                    case AssignmentDeclarationKind.ObjectDefinePropertyExports:
                    case AssignmentDeclarationKind.ObjectDefinePrototypeProperty:
                        return ((CallExpression)declaration).Arguments![1];
                    default:
                        return null;
                }

            case SyntaxKind.JSDocTypedefTag:
                return GetNameOfJSDocTypedef((JSDocTypedefTag)declaration);
            case SyntaxKind.JSDocEnumTag:
                return NameForNamelessJSDocTypedef(declaration);
            case SyntaxKind.ExportAssignment:
                return ((ExportAssignment)declaration).Expression is Identifier expression ? expression : null;
            case SyntaxKind.ElementAccessExpression:
                if (IsBindableStaticElementAccessExpression(declaration))
                {
                    return ((ElementAccessExpression)declaration).ArgumentExpression;
                }

                break;
        }

        return GetNamedDeclarationName(declaration);
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:997-1001 — <c>getNameOfDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>||</c> between the two lookups is a node-or-undefined fallback — a node is never falsy — so
    /// the port writes <c>??</c>. The result is <c>object?</c> (<c>DeclarationName</c>, §12.5) and the caller narrows.
    /// </remarks>
    public static object? GetNameOfDeclaration(Node? declaration)
    {
        if (declaration is null) return null;

        return GetNonAssignedNameOfDeclaration(declaration)
            ?? (NodeTests.IsFunctionExpression(declaration)
                    || NodeTests.IsArrowFunction(declaration)
                    || NodeTests.IsClassExpression(declaration)
                ? GetAssignedName(declaration)
                : null);
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1004-1022 — <c>getAssignedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's else-if chain becomes a switch on the parent's class; the <c>when</c> clause on the binary arm
    /// keeps TS's <c>node === node.parent.right</c> test, so a left-hand child falls through to the same
    /// <c>undefined</c> TS answers. Each arm's <c>name</c> slot is nullable in the model and non-optional upstream; an
    /// absent slot answers no name.
    /// </remarks>
    public static object? GetAssignedName(Node node)
    {
        var parent = node.Parent;
        if (parent is null) return null;

        switch (parent)
        {
            case PropertyAssignment assignment:
                return assignment.Name;
            case BindingElement element:
                return element.Name;
            case BinaryExpression binary when ReferenceEquals(node, binary.Right):
                if (binary.Left is Identifier identifier) return identifier;
                if (binary.Left is { } left && IsAccessExpression(left))
                {
                    return GetElementOrPropertyAccessArgumentExpressionOrName(left);
                }

                return null;
            case VariableDeclaration declaration when declaration.Name is Identifier name:
                return name;
            default:
                return null;
        }
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2406-2408 — <c>isDeclarationStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>DeclarationStatement</c>; the port answers <c>bool</c>. The kind table it
    /// calls is already ported as the private <c>IsDeclarationStatementKind</c> in Utilities.cs, so this file only adds
    /// TS's public wrapper.
    /// </remarks>
    public static bool IsDeclarationStatement(Node node) => IsDeclarationStatementKind(node.Kind);

    // ---------------------------------------------------------------------------------------------
    // Optional chains, boolean literals (utilitiesPublic.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1390-1392 — <c>isOptionalChainRoot</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>OptionalChainRoot</c> and then reads <c>questionDotToken</c> off it; C#
    /// cannot narrow through a predicate, so the token is asked for with a class pattern over the three shapes that
    /// carry one. The abstract <c>PropertyAccessChain</c>/<c>ElementAccessChain</c>/<c>CallChain</c> views are never
    /// instantiated by the port's factory (an optional chain is its base class plus the <c>OptionalChain</c> flag, as
    /// <c>IsPropertyAccessChain</c> in Utilities.cs records), which is why the base classes are named. <c>NonNullChain</c>
    /// has no token and TS excludes it separately — that test is kept.
    /// </remarks>
    public static bool IsOptionalChainRoot(Node node)
        => IsOptionalChain(node)
            && !NodeTests.IsNonNullExpression(node)
            && node is PropertyAccessExpression { QuestionDotToken: not null }
                or ElementAccessExpression { QuestionDotToken: not null }
                or CallExpression { QuestionDotToken: not null };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1399-1401 — <c>isExpressionOfOptionalChainRoot</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>Expression &amp; { parent: OptionalChainRoot }</c> and compares
    /// <c>node.parent.expression</c>; C# cannot narrow through a predicate, so the port answers <c>bool</c> and reads the
    /// parent's <c>expression</c> through the shared helper below.
    /// </remarks>
    public static bool IsExpressionOfOptionalChainRoot(Node node)
        => node.Parent is { } parent
            && IsOptionalChainRoot(parent)
            && ReferenceEquals(GetOptionalChainExpression(parent), node);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1416-1420 — <c>isOutermostOptionalChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>OptionalChain</c> union, which has no C# anchor, so it is a <c>Node</c> here
    /// and the <c>expression</c> read is the same shared helper as above. A root node without a parent is an outermost
    /// chain — that is TS's <c>!isOptionalChain(node.parent)</c> answering true for <c>undefined</c>.
    /// </remarks>
    public static bool IsOutermostOptionalChain(Node node)
    {
        var parent = node.Parent;
        if (parent is null || !IsOptionalChain(parent)) return true;
        if (IsOptionalChainRoot(parent)) return true;
        return !ReferenceEquals(GetOptionalChainExpression(parent), node);
    }

    /// <summary>The port of TS's <c>node.parent.expression</c> read, shared by the two optional-chain predicates above.</summary>
    /// <remarks>
    /// PORT-NOTE: TS narrows the parent to the <c>OptionalChain</c> union (<c>PropertyAccessChain | ElementAccessChain |
    /// CallChain | NonNullChain</c>) and reads <c>expression</c> off it. The union has no C# anchor and the chain views
    /// are not instantiated, so the read is spelled out per concrete shape; the four shapes are the whole of
    /// <c>isOptionalChain</c>'s answer, and the last arm is unreachable for a caller that checked first.
    /// </remarks>
    private static Node? GetOptionalChainExpression(Node optionalChain) => optionalChain switch
    {
        PropertyAccessExpression chain => chain.Expression,
        ElementAccessExpression chain => chain.Expression,
        CallExpression chain => chain.Expression,
        NonNullExpression chain => chain.Expression,
        _ => null,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1422-1424 — <c>isNullishCoalesce</c>.</summary>
    public static bool IsNullishCoalesce(Node node)
        => NodeTests.IsBinaryExpression(node)
            && ((BinaryExpression)node).OperatorToken!.Kind == SyntaxKind.QuestionQuestionToken;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1680-1682 — <c>isFunctionLikeOrClassStaticBlockDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>SignatureDeclaration | ClassStaticBlockDeclaration</c>; C# cannot narrow
    /// through a predicate, so the port answers <c>bool</c>.
    /// </remarks>
    public static bool IsFunctionLikeOrClassStaticBlockDeclaration(Node? node)
        => node is not null && (IsFunctionLikeKind(node.Kind) || NodeTests.IsClassStaticBlockDeclaration(node));

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1690-1692 — <c>isBooleanLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>BooleanLiteral</c>; C# cannot narrow through a predicate, so the port
    /// answers <c>bool</c>.
    /// </remarks>
    public static bool IsBooleanLiteral(Node node)
        => node.Kind is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword;

    // ---------------------------------------------------------------------------------------------
    // Class shapes (utilitiesPublic.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1732-1742 — <c>isClassElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>ClassElement</c>; C# cannot narrow through a predicate, so the port answers
    /// <c>bool</c>. The kind and its C# class are one and the same for a class element, but the question is asked of the
    /// kind, as TS asks it.
    /// </remarks>
    public static bool IsClassElement(Node node) => node.Kind switch
    {
        SyntaxKind.Constructor
            or SyntaxKind.PropertyDeclaration
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor
            or SyntaxKind.IndexSignature
            or SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.SemicolonClassElement => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1744-1746 — <c>isClassLike</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node &amp;&amp;</c> truthiness test has no counterpart because the parameter is non-nullable;
    /// its guard narrows to <c>ClassLikeDeclaration</c>, so the port answers <c>bool</c>.
    /// </remarks>
    public static bool IsClassLike(Node node)
        => node.Kind is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1752-1754 — <c>isAutoAccessorPropertyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>AutoAccessorPropertyDeclaration</c>; C# cannot narrow through a predicate, so
    /// the port answers <c>bool</c>.
    /// </remarks>
    public static bool IsAutoAccessorPropertyDeclaration(Node node)
        => NodeTests.IsPropertyDeclaration(node) && HasSyntacticModifier(node, ModifierFlags.Accessor);

    // ---------------------------------------------------------------------------------------------
    // Statements and other node shapes (utilitiesPublic.ts, utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2154-2156 — <c>isForInOrOfStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>ForInOrOfStatement</c>; the alias has no C# anchor, so the port answers
    /// <c>bool</c>.
    /// </remarks>
    public static bool IsForInOrOfStatement(Node node)
        => node.Kind is SyntaxKind.ForInStatement or SyntaxKind.ForOfStatement;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:636-642 — <c>walkUpBindingElementsAndPatterns</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>VariableDeclaration | ParameterDeclaration</c>; both are nodes and the one caller treats
    /// it as one, so the port answers <c>Node</c>. The parent reads are asserted because TS's parent pointer is
    /// non-optional in the same walk.
    /// </remarks>
    public static Node WalkUpBindingElementsAndPatterns(BindingElement binding)
    {
        var node = binding.Parent!;
        while (node.Parent is { } parent && NodeTests.IsBindingElement(parent)) node = parent.Parent!;
        return node.Parent!;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:644-660 — <c>getCombinedFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares the flags accessor as <c>(n: Node) =&gt; number</c> and shares this body between node flags
    /// and modifier flags. The port's enums have distinct underlying types, so the accessor is <c>Func&lt;Node,
    /// uint&gt;</c> — <c>NodeFlags</c> and <c>ModifierFlags</c> are both <c>uint</c>-backed in the model — and the
    /// caller casts the result. TS's <c>node &amp;&amp;</c> guards on the walked parents are kept, because a node outside
    /// a tree has no parent to read.
    /// </remarks>
    private static uint GetCombinedFlags(Node node, Func<Node, uint> getFlags)
    {
        Node? current = NodeTests.IsBindingElement(node)
            ? WalkUpBindingElementsAndPatterns((BindingElement)node)
            : node;

        var flags = getFlags(current);
        if (current.Kind == SyntaxKind.VariableDeclaration)
        {
            current = current.Parent;
        }

        if (current is not null && current.Kind == SyntaxKind.VariableDeclarationList)
        {
            flags |= getFlags(current);
            current = current.Parent;
        }

        if (current is not null && current.Kind == SyntaxKind.VariableStatement)
        {
            flags |= getFlags(current);
        }

        return flags;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:682-684 — <c>getNodeFlags</c>.</summary>
    private static uint GetNodeFlags(Node node) => (uint)node.Flags;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:678-680 — <c>getCombinedNodeFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS shares <c>getCombinedFlags</c> with <c>getCombinedModifierFlags</c>; that sibling now composes this
    /// same walk with <c>getSyntacticModifierFlags</c> where TS uses <c>getEffectiveModifierFlags</c> (which is still
    /// the JSDoc/effective-modifier slice's to land) — see <c>Utilities.GetCombinedModifierFlags</c> in
    /// Utilities/Utilities.Binder.Modifiers.cs for the walk's second entry point and that substitution's reason.
    /// </remarks>
    public static NodeFlags GetCombinedNodeFlags(Node node) => (NodeFlags)GetCombinedFlags(node, GetNodeFlags);

    /// <summary>ported from: src/compiler/utilities.ts:2066-2069 — <c>isBlockOrCatchScoped</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Declaration</c>, which the port spells <see cref="IDeclaration"/>; the combined
    /// flags walk needs the node itself, hence the cast.
    /// </remarks>
    public static bool IsBlockOrCatchScoped(IDeclaration declaration)
        => (GetCombinedNodeFlags((Node)declaration) & NodeFlags.BlockScoped) != 0
            || IsCatchClauseVariableDeclarationOrBindingElement(declaration);

    /// <summary>ported from: src/compiler/utilities.ts:2072-2075 — <c>isCatchClauseVariableDeclarationOrBindingElement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.parent.kind</c> is a non-optional read; the model's parent pointer is nullable, so an
    /// absent parent answers <c>false</c> where TS's type said it could not happen.
    /// </remarks>
    public static bool IsCatchClauseVariableDeclarationOrBindingElement(IDeclaration declaration)
    {
        var node = GetRootDeclaration((Node)declaration);
        return node.Kind == SyntaxKind.VariableDeclaration && node.Parent is CatchClause;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5603-5608 — <c>getRootDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS walks <c>node.parent.parent</c> from a binding element — the binding pattern, then the declaration —
    /// with non-optional parent pointers; the port asserts the same two reads. TS's parameter is <c>Node</c>, so a
    /// caller holding an <see cref="IDeclaration"/> casts.
    /// </remarks>
    public static Node GetRootDeclaration(Node node)
    {
        while (node.Kind == SyntaxKind.BindingElement) node = node.Parent!.Parent!;
        return node;
    }

    // ---------------------------------------------------------------------------------------------
    // Modules, source files, enums (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2078-2080 — <c>isAmbientModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>AmbientModuleDeclaration</c> and its <c>name</c> is the <c>ModuleName</c>
    /// union; the port answers <c>bool</c> and reads the model's nullable <c>Name</c> with a string-literal pattern — a
    /// missing name answers <c>false</c> where TS's non-optional type could not be missing.
    /// </remarks>
    public static bool IsAmbientModule(Node node)
        => node is ModuleDeclaration declaration
            && (declaration.Name is StringLiteral || IsGlobalScopeAugmentation(declaration));

    /// <summary>ported from: src/compiler/utilities.ts:2124-2126 — <c>isGlobalScopeAugmentation</c>.</summary>
    public static bool IsGlobalScopeAugmentation(ModuleDeclaration module)
        => (module.Flags & NodeFlags.GlobalAugmentation) != 0;

    /// <summary>ported from: src/compiler/utilities.ts:2134-2145 — <c>isModuleAugmentationExternal</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>AmbientModuleDeclaration</c>, which has no C# anchor (it is a <c>ModuleDeclaration</c>
    /// with a name of one shape), so the port takes the concrete class. The switch reads the nullable parent where TS's
    /// type says it cannot be absent — an absent parent falls to <c>false</c>. <c>isExternalModule</c> is already ported
    /// as <c>Parser.IsExternalModule</c> (parser.ts:1386-1388), whose body is the same one-liner, so it is called rather
    /// than declared twice.
    /// </remarks>
    public static bool IsModuleAugmentationExternal(ModuleDeclaration node)
    {
        switch (node.Parent?.Kind)
        {
            case SyntaxKind.SourceFile:
                return Parser.IsExternalModule((SourceFile)node.Parent);
            case SyntaxKind.ModuleBlock:
                // node.parent.parent is the enclosing module declaration, and its parent is the source file.
                var module = node.Parent!.Parent;
                return module is ModuleDeclaration declaration
                    && IsAmbientModule(declaration)
                    && module.Parent is SourceFile file
                    && !Parser.IsExternalModule(file);
            default:
                return false;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:2638-2640 — <c>isExternalOrCommonJsModule</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>(a || b) !== undefined</c> over <c>Node | boolean | undefined</c>; the port's two slots
    /// are <c>object?</c>/<c>Node?</c>, and the parser only ever stores <c>true</c> or a node (never <c>false</c>), so
    /// "not null" is the same question and is spelled the same way as <c>Parser.IsExternalModule</c>.
    /// </remarks>
    public static bool IsExternalOrCommonJsModule(SourceFile file)
        => file.ExternalModuleIndicator is not null || file.CommonJsModuleIndicator is not null;

    /// <summary>ported from: src/compiler/utilities.ts:2643-2645 — <c>isJsonSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>JsonSourceFile</c>, a shape of <c>SourceFile</c> with no C# counterpart, so
    /// the port answers <c>bool</c> and the caller keeps the concrete class.
    /// </remarks>
    public static bool IsJsonSourceFile(SourceFile file) => file.ScriptKind == ScriptKind.JSON;

    /// <summary>ported from: src/compiler/utilities.ts:2648-2650 — <c>isEnumConst</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks <c>getCombinedModifierFlags(node) &amp; ModifierFlags.Const</c>. For an enum declaration the
    /// combined walk adds nothing — <c>getCombinedFlags</c> only climbs through binding elements and variable
    /// declaration lists — and the JSDoc half never contributes <c>Const</c>, so the syntactic question is the whole
    /// question. <c>getCombinedModifierFlags</c> itself arrives with the effective-modifier slice.
    /// </remarks>
    public static bool IsEnumConst(EnumDeclaration node) => HasSyntacticModifier(node, ModifierFlags.Const);

    /// <summary>ported from: src/compiler/utilities.ts:4535-4560 — <c>canHaveFlowNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>HasFlowNode</c>, a union with no C# anchor; the port answers <c>bool</c>.
    /// </remarks>
    public static bool CanHaveFlowNode(Node node)
    {
        if (node.Kind >= SyntaxKind.FirstStatement && node.Kind <= SyntaxKind.LastStatement)
        {
            return true;
        }

        return node.Kind is SyntaxKind.Identifier
            or SyntaxKind.ThisKeyword
            or SyntaxKind.SuperKeyword
            or SyntaxKind.QualifiedName
            or SyntaxKind.MetaProperty
            or SyntaxKind.ElementAccessExpression
            or SyntaxKind.PropertyAccessExpression
            or SyntaxKind.BindingElement
            or SyntaxKind.FunctionExpression
            or SyntaxKind.ArrowFunction
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.GetAccessor
            or SyntaxKind.SetAccessor;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3283-3290 — <c>isInTopLevelContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reassigns its parameter; the port walks with a <c>current</c> local, and the declaration-name test
    /// reads the name through <see cref="INamedDeclaration"/> because the parent's class is only known by kind.
    /// </remarks>
    public static bool IsInTopLevelContext(Node node)
    {
        var current = node;
        if (current is Identifier
            && current.Parent is { } parent
            && (NodeTests.IsClassDeclaration(parent) || NodeTests.IsFunctionDeclaration(parent))
            && parent is INamedDeclaration { Name: var name } && name == current)
        {
            current = parent;
        }

        var container = GetThisContainer(current, /*includeArrowFunctions*/ true, /*includeClassComputedPropertyName*/ false);
        return NodeTests.IsSourceFile(container);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3760-3766 — <c>isPartOfTypeQuery</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parent pointers are non-optional; the port's are nullable slots, so a walk that runs out of
    /// parents answers <c>false</c> where TS's type said the root could not be reached.
    /// </remarks>
    public static bool IsPartOfTypeQuery(Node node)
    {
        var current = node;
        while (current.Kind is SyntaxKind.QualifiedName or SyntaxKind.Identifier)
        {
            if (current.Parent is null)
            {
                return false;
            }

            current = current.Parent;
        }

        return current.Kind == SyntaxKind.TypeQuery;
    }

    /// <summary>ported from: src/compiler/utilities.ts:12436-12446 — <c>isPotentiallyExecutableNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>some(declarations, d =&gt; d.initializer !== undefined)</c> is <see cref="Core.Some{T}"/>
    /// over the declarations' nullable initializer slot.
    /// </remarks>
    public static bool IsPotentiallyExecutableNode(Node node)
    {
        if (node.Kind >= SyntaxKind.FirstStatement && node.Kind <= SyntaxKind.LastStatement)
        {
            if (NodeTests.IsVariableStatement(node))
            {
                var declarationList = ((VariableStatement)node).DeclarationList!;
                if ((GetCombinedNodeFlags(declarationList) & NodeFlags.BlockScoped) != 0)
                {
                    return true;
                }

                return declarationList.Declarations is { } declarations
                    && Core.Some(declarations, d => d.Initializer is not null);
            }

            return true;
        }

        return NodeTests.IsClassDeclaration(node) || NodeTests.IsEnumDeclaration(node) || NodeTests.IsModuleDeclaration(node);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3073-3075 — <c>isObjectLiteralMethod</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's truthiness test on the parameter has no counterpart for a non-nullable one, and its guard
    /// narrows to <c>MethodDeclaration</c> — the port answers <c>bool</c>.
    /// </remarks>
    public static bool IsObjectLiteralMethod(Node node)
        => node.Kind == SyntaxKind.MethodDeclaration && node.Parent?.Kind == SyntaxKind.ObjectLiteralExpression;

    /// <summary>ported from: src/compiler/utilities.ts:3078-3082 — <c>isObjectLiteralOrClassExpressionMethodOrAccessor</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="IsObjectLiteralMethod"/>: a guard answers <c>bool</c> here.</remarks>
    public static bool IsObjectLiteralOrClassExpressionMethodOrAccessor(Node node)
        => (node.Kind is SyntaxKind.MethodDeclaration or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor)
            && (node.Parent?.Kind is SyntaxKind.ObjectLiteralExpression or SyntaxKind.ClassExpression);

    /// <summary>ported from: src/compiler/utilities.ts:5387-5394 — <c>isDynamicName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>DeclarationName</c> union has no C# anchor, so the parameter is <c>Node</c>; the body still
    /// narrows by kind first, and the two arms read <c>argumentExpression</c>/<c>expression</c> off their own class.
    /// </remarks>
    public static bool IsDynamicName(Node name)
    {
        if (name.Kind is not (SyntaxKind.ComputedPropertyName or SyntaxKind.ElementAccessExpression))
        {
            return false;
        }

        var expr = name is ElementAccessExpression elementAccess
            ? SkipParentheses(elementAccess.ArgumentExpression!)
            : ((ComputedPropertyName)name).Expression!;
        return !IsStringOrNumericLiteralLike(expr) && !IsSignedNumericLiteral(expr);
    }

    // ---------------------------------------------------------------------------------------------
    // Syntactic modifiers (utilities.ts)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7185-7187 — <c>hasSyntacticModifiers</c>.</summary>
    public static bool HasSyntacticModifiers(Node node) => GetSyntacticModifierFlags(node) != ModifierFlags.None;

    /// <summary>ported from: src/compiler/utilities.ts:7195-7197 — <c>hasSyntacticModifier</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>!!getSelectedSyntacticModifierFlags(node, flags)</c> is a truthiness test on a flag
    /// set; the port asks the same question with <c>!= ModifierFlags.None</c>.</remarks>
    public static bool HasSyntacticModifier(Node node, ModifierFlags flags)
        => GetSelectedSyntacticModifierFlags(node, flags) != ModifierFlags.None;

    /// <summary>ported from: src/compiler/utilities.ts:7200-7203 — <c>isStatic</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>&amp;&amp;</c> binds tighter than its <c>||</c>; the port keeps TS's grouping.</remarks>
    public static bool IsStatic(Node node)
        => (IsClassElement(node) && HasStaticModifier(node)) || NodeTests.IsClassStaticBlockDeclaration(node);

    /// <summary>ported from: src/compiler/utilities.ts:7206-7208 — <c>hasStaticModifier</c>.</summary>
    public static bool HasStaticModifier(Node node) => HasSyntacticModifier(node, ModifierFlags.Static);

    /// <summary>ported from: src/compiler/utilities.ts:7246-7248 — <c>getSelectedSyntacticModifierFlags</c>.</summary>
    public static ModifierFlags GetSelectedSyntacticModifierFlags(Node node, ModifierFlags flags)
        => GetSyntacticModifierFlags(node) & flags;

    /// <summary>ported from: src/compiler/utilities.ts:7292-7294 — <c>getSyntacticModifierFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS routes through <c>getModifierFlagsWorker(node, /*includeJSDoc*/ false)</c> (utilities.ts:7250);
    /// that worker's <c>includeJSDoc</c>/<c>alwaysIncludeJSDoc</c> branch is the effective-modifier path, which needs
    /// the JSDoc no-cache tag lookups and arrives with a later M5 slice. What remains is the syntactic branch this
    /// entry point takes, with the cache logic and <c>selectSyntacticModifierFlags</c> (utilities.ts:7312) intact.
    /// </remarks>
    public static ModifierFlags GetSyntacticModifierFlags(Node node)
    {
        if (node.Kind >= SyntaxKind.FirstToken && node.Kind <= SyntaxKind.LastToken)
        {
            return ModifierFlags.None;
        }

        if ((node.ModifierFlagsCache & ModifierFlags.HasComputedFlags) == 0)
        {
            node.ModifierFlagsCache = GetSyntacticModifierFlagsNoCache(node) | ModifierFlags.HasComputedFlags;
        }

        return node.ModifierFlagsCache & ModifierFlags.SyntacticModifiers;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7344-7351 — <c>getSyntacticModifierFlagsNoCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>node.modifiers</c> after <c>canHaveModifiers</c> narrows the node to <c>HasModifiers</c> —
    /// an intersection alias over ~25 types with no C# interface, so the port reads the slot off each class through
    /// <see cref="ModifiersOf"/>.
    /// </remarks>
    public static ModifierFlags GetSyntacticModifierFlagsNoCache(Node node)
    {
        var flags = CanHaveModifiers(node) ? ModifiersToFlags(ModifiersOf(node)) : ModifierFlags.None;
        if ((node.Flags & NodeFlags.NestedNamespace) != 0
            || (node.Kind == SyntaxKind.Identifier && (node.Flags & NodeFlags.IdentifierIsInJSDocNamespace) != 0))
        {
            flags |= ModifierFlags.Export;
        }

        return flags;
    }

    /// <summary>
    /// The <c>modifiers</c> slot, read off the class each of TS's <c>HasModifiers</c> kinds is.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>HasModifiers</c> is an intersection alias, so <c>node.modifiers</c> type-checks on the union;
    /// the port's node classes share no interface for the slot, so each kind is named. The result widens to
    /// <c>IEnumerable&lt;Node&gt;</c> because the slot's element type differs by kind (<c>ModifierToken</c> for a type
    /// parameter, <c>Node</c> elsewhere) and <see cref="ModifiersToFlags"/> only needs the kind of each modifier.
    /// </remarks>
    private static IEnumerable<Node>? ModifiersOf(Node node) => node switch
    {
        TypeParameterDeclaration declaration => declaration.Modifiers,
        ParameterDeclaration declaration => declaration.Modifiers,
        PropertySignature declaration => declaration.Modifiers,
        PropertyDeclaration declaration => declaration.Modifiers,
        MethodSignature declaration => declaration.Modifiers,
        MethodDeclaration declaration => declaration.Modifiers,
        ConstructorDeclaration declaration => declaration.Modifiers,
        GetAccessorDeclaration declaration => declaration.Modifiers,
        SetAccessorDeclaration declaration => declaration.Modifiers,
        IndexSignatureDeclaration declaration => declaration.Modifiers,
        ConstructorTypeNode declaration => declaration.Modifiers,
        FunctionExpression declaration => declaration.Modifiers,
        ArrowFunction declaration => declaration.Modifiers,
        ClassExpression declaration => declaration.Modifiers,
        VariableStatement declaration => declaration.Modifiers,
        FunctionDeclaration declaration => declaration.Modifiers,
        ClassDeclaration declaration => declaration.Modifiers,
        InterfaceDeclaration declaration => declaration.Modifiers,
        TypeAliasDeclaration declaration => declaration.Modifiers,
        EnumDeclaration declaration => declaration.Modifiers,
        ModuleDeclaration declaration => declaration.Modifiers,
        ImportEqualsDeclaration declaration => declaration.Modifiers,
        ImportDeclaration declaration => declaration.Modifiers,
        ExportAssignment declaration => declaration.Modifiers,
        ExportDeclaration declaration => declaration.Modifiers,
        _ => null,
    };
}