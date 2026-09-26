// ported from: src/compiler/utilities.ts, utilitiesPublic.ts, checker.ts
/// <summary>
/// The <c>this</c>-container, name-analysis and value-expression helpers of <c>utilities.ts</c> that the binder's
/// declaration and flow slices are written in terms of (M5-0).
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no module-level functions, so the free functions of <c>utilities.ts</c> land on this
/// partial class as each batch needs them, every member carrying the line it came from (the same arrangement as
/// <c>Utilities.Parser.cs</c> and the M4d slice in <c>Utilities.cs</c>, docs/architecture.md §15.9). The members
/// below are the ones <c>binder.ts</c> imports for <c>getThisContainer</c>, the name/declaration predicates and the
/// JS-assignment walks; a few helpers those bodies are defined in terms of live here too, because a faithful body
/// needs them and no other file has claimed them.
/// <para>
/// PORT-NOTE: TS's <c>T | undefined</c> is <c>T?</c> (§5.1); its type-guard predicates (<c>node is X</c>) answer
/// <c>bool</c>, because C# cannot narrow through a predicate, so a caller casts to the interface or class it needs
/// (noted per member). A union of node types that has no C# base interface of its own — <c>AssignmentTarget</c>,
/// <c>PropertyNameLiteral</c>, <c>ExportAssignment | BinaryExpression</c>, <c>AccessExpression</c> — is spelled
/// <c>Node</c>, or split into the switch arms TS's one union-wide member read covers.
/// </para>
/// <para>
/// PORT-NOTE: TS tests truthiness on objects here (<c>!!node</c>, <c>if (!node)</c>, <c>node &amp;&amp;</c>); the port
/// keeps those tests as object null-tests — <c>Js.IsTruthy</c> where no narrowing is needed, <c>is null</c>/<c>is not
/// null</c> where the next expression needs the narrowed type.
/// </para>
/// <para>
/// PORT-NOTE: the members this slice's bodies call but does not declare belong to the JS-expando batch, which is
/// <c>Utilities.JsExpando.cs</c>: <c>IsEntityNameExpression</c> (for <see cref="IsAliasableExpression"/>) is the only
/// one this file reaches, and that file reaches nothing of this one. Six of the names <c>binder.ts</c> imports from
/// this family — <c>isSignedNumericLiteral</c>, <c>isPropertyNameLiteral</c>, <c>isEntityNameExpression</c>,
/// <c>isPropertyAccessEntityNameExpression</c>, <c>isPrototypeAccess</c> and <c>getLeftmostAccessExpression</c> —
/// are defined there instead of here, where their expando walk needs them and where they were written first; the
/// assembly must hold exactly one definition of each.
/// </para>
/// </remarks>
namespace TypeScript.Compiler;

internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // The `this` container
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3137-3139 — <c>getContainingClass</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard answers <c>ClassLikeDeclaration | undefined</c> (<c>ClassDeclaration | ClassExpression</c>);
    /// C# cannot narrow through it, and the two classes share only <c>IClassLikeDeclarationBase</c>, so the port keeps
    /// <c>Node?</c> — the caller casts. The predicate is spelled as a lambda so it binds to the plain
    /// <c>Func&lt;Node, bool&gt;</c> overload of <see cref="FindAncestor(Node?, Func{Node, bool})"/> (a lambda that
    /// answers <c>bool</c> is the better conversion).
    /// </remarks>
    public static Node? GetContainingClass(Node node) => FindAncestor(node.Parent, n => IsClassLike(n));

    /// <summary>ported from: src/compiler/utilities.ts:3182-3250 — <c>getThisContainer</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS declares four overloads (3182-3188) whose return type narrows on the two flags
    /// (<c>ThisContainer | ComputedPropertyName | ArrowFunction</c>); the union has no C# anchor and the flags cannot
    /// narrow a return type, so the port keeps the single implementation signature with a <c>Node</c> result.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS reassigns the parameter in the loop and guards the parent read with <c>if (!node)</c>. The
    /// parameter is non-nullable here, so the walk keeps a non-null <c>current</c> local and reads each step into
    /// <c>next</c> before testing it — and because that read sits at the top of the loop, each <c>break</c> still
    /// re-enters through the parent read, which is what makes a computed property name's *parent* (and a
    /// decorator's) the node the next iteration tests.
    /// </para>
    /// </remarks>
    public static Node GetThisContainer(Node node, bool includeArrowFunctions, bool includeClassComputedPropertyName)
    {
        Debug.Assert(node.Kind != SyntaxKind.SourceFile);
        var current = node;
        while (true)
        {
            var next = current.Parent;
            if (next is null)
            {
                // If we never pass in a SourceFile, this should be unreachable, since we'll stop when we reach that.
                return Debug.Fail<Node>();
            }

            current = next;
            switch (current.Kind)
            {
                case SyntaxKind.ComputedPropertyName:
                    // If the grandparent node is an object literal (as opposed to a class),
                    // then the computed property is not a 'this' container.
                    // A computed property name in a class needs to be a this container
                    // so that we can error on it.
                    if (includeClassComputedPropertyName && IsClassLike(current.Parent!.Parent!))
                    {
                        return current;
                    }

                    // If this is a computed property, then the parent should not
                    // make it a this container. The parent might be a property
                    // in an object literal, like a method or accessor. But in order for
                    // such a parent to be a this container, the reference must be in
                    // the *body* of the container.
                    current = current.Parent!.Parent!;
                    break;

                case SyntaxKind.Decorator:
                    // Decorators are always applied outside of the body of a class or method.
                    if (current.Parent!.Kind == SyntaxKind.Parameter && IsClassElement(current.Parent!.Parent!))
                    {
                        // If the decorator's parent is a Parameter, we resolve the this container from
                        // the grandparent class declaration.
                        current = current.Parent!.Parent!;
                    }
                    else if (IsClassElement(current.Parent!))
                    {
                        // If the decorator's parent is a class element, we resolve the 'this' container
                        // from the parent class declaration.
                        current = current.Parent!;
                    }

                    break;

                case SyntaxKind.ArrowFunction:
                    if (!includeArrowFunctions)
                    {
                        continue;
                    }

                    // falls through
                    goto case SyntaxKind.FunctionDeclaration;

                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.ClassStaticBlockDeclaration:
                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.SourceFile:
                    return current;
            }
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:3383-3395 — <c>getImmediatelyInvokedFunctionExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the parenthesized walk reads <c>parent</c> before testing it, exactly as TS does; the port
    /// spells the read's non-null result with <c>!</c> because <c>Node.Parent</c> is a nullable slot.
    /// </remarks>
    public static CallExpression? GetImmediatelyInvokedFunctionExpression(Node func)
    {
        if (func.Kind is SyntaxKind.FunctionExpression or SyntaxKind.ArrowFunction)
        {
            var prev = func;
            var parent = func.Parent!;
            while (parent.Kind == SyntaxKind.ParenthesizedExpression)
            {
                prev = parent;
                parent = parent.Parent!;
            }

            if (parent.Kind == SyntaxKind.CallExpression && ((CallExpression)parent).Expression == prev)
            {
                return (CallExpression)parent;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Names and declarations
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3420-3422 — <c>isThisInitializedDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>!!node</c> is the port's <c>node is not null</c> — it must narrow for
    /// <see cref="NodeTests.IsVariableDeclaration"/>, which takes a non-nullable node.
    /// </remarks>
    public static bool IsThisInitializedDeclaration(Node? node)
        => node is not null
            && NodeTests.IsVariableDeclaration(node)
            && ((VariableDeclaration)node).Initializer?.Kind == SyntaxKind.ThisKeyword;

    /// <summary>ported from: src/compiler/utilities.ts:4925-4927 — <c>isAssignmentTarget</c>.</summary>
    public static bool IsAssignmentTarget(Node node) => Js.IsTruthy(GetAssignmentTarget(node));

    /// <summary>ported from: src/compiler/utilities.ts:4854-4897 — <c>getAssignmentTarget</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's result is the union <c>AssignmentTarget</c> (<c>BinaryExpression | PrefixUnaryExpression |
    /// PostfixUnaryExpression | ForInOrOfStatement</c>); the port answers <c>Node?</c>, which is all
    /// <see cref="IsAssignmentTarget"/> reads, and so the caller casts.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS reads <c>operator</c>/<c>initializer</c> off one union-typed local and so groups four kinds
    /// into two cases; C# has no union cast, so each kind gets its own arm (the same treatment
    /// <see cref="IsSuperProperty"/> documents). The arms keep TS's order, and every arm answers the node TS
    /// answered — <c>node</c> at that point is the same object TS's local held.
    /// </para>
    /// </remarks>
    private static Node? GetAssignmentTarget(Node node)
    {
        var parent = node.Parent!;
        while (true)
        {
            switch (parent.Kind)
            {
                case SyntaxKind.BinaryExpression:
                {
                    var binaryExpression = (BinaryExpression)parent;
                    var binaryOperator = binaryExpression.OperatorToken!.Kind;
                    return IsAssignmentOperator(binaryOperator) && binaryExpression.Left == node ? binaryExpression : null;
                }

                case SyntaxKind.PrefixUnaryExpression:
                    return ((PrefixUnaryExpression)parent).Operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken
                        ? parent
                        : null;

                case SyntaxKind.PostfixUnaryExpression:
                    return ((PostfixUnaryExpression)parent).Operator is SyntaxKind.PlusPlusToken or SyntaxKind.MinusMinusToken
                        ? parent
                        : null;

                case SyntaxKind.ForInStatement:
                    return ((ForInStatement)parent).Initializer == node ? parent : null;

                case SyntaxKind.ForOfStatement:
                    return ((ForOfStatement)parent).Initializer == node ? parent : null;

                case SyntaxKind.ParenthesizedExpression:
                case SyntaxKind.ArrayLiteralExpression:
                case SyntaxKind.SpreadElement:
                case SyntaxKind.NonNullExpression:
                    node = parent;
                    break;

                case SyntaxKind.SpreadAssignment:
                    node = parent.Parent!;
                    break;

                case SyntaxKind.ShorthandPropertyAssignment:
                    if (((ShorthandPropertyAssignment)parent).Name != node)
                    {
                        return null;
                    }

                    node = parent.Parent!;
                    break;

                case SyntaxKind.PropertyAssignment:
                    if (((PropertyAssignment)parent).Name == node)
                    {
                        return null;
                    }

                    node = parent.Parent!;
                    break;

                default:
                    return null;
            }

            parent = node.Parent!;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:5120-5150 — <c>isIdentifierName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's member-name/parent-access case reads <c>name</c> off the union
    /// <c>NamedDeclaration | PropertyAccessExpression</c>; every class in it implements
    /// <see cref="INamedDeclaration"/> in the port, so one interface cast covers all nine kinds — but the two
    /// <c>propertyName</c> cases are on distinct classes, so they are split (C# has no union cast).
    /// </remarks>
    public static bool IsIdentifierName(Identifier node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.MethodSignature:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.EnumMember:
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.PropertyAccessExpression:
                // Name in member declaration or property name in property access
                return ((INamedDeclaration)parent).Name == node;

            case SyntaxKind.QualifiedName:
                // Name on right hand side of dot in a type query or type reference
                return ((QualifiedName)parent).Right == node;

            case SyntaxKind.BindingElement:
                // Property name in binding element
                return ((BindingElement)parent).PropertyName == node;

            case SyntaxKind.ImportSpecifier:
                // Property name in import specifier
                return ((ImportSpecifier)parent).PropertyName == node;

            case SyntaxKind.ExportSpecifier:
            case SyntaxKind.JsxAttribute:
            case SyntaxKind.JsxSelfClosingElement:
            case SyntaxKind.JsxOpeningElement:
            case SyntaxKind.JsxClosingElement:
                // Any name in an export specifier or JSX Attribute or Jsx Element
                return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5173-5175 — <c>isAliasableExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isEntityNameExpression</c> is the JS-expando batch's member (this file's remarks); the port
    /// answers <c>bool</c> where TS's guard narrows the argument.
    /// </remarks>
    public static bool IsAliasableExpression(Expression e)
        => IsEntityNameExpression(e) || NodeTests.IsClassExpression(e);

    /// <summary>ported from: src/compiler/utilities.ts:5178-5181 — <c>exportAssignmentIsAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ExportAssignment | BinaryExpression</c>, which has no C# anchor; the
    /// port takes <c>Node</c> and <see cref="GetExportAssignmentExpression"/> reads the right arm from the kind.
    /// </remarks>
    public static bool ExportAssignmentIsAlias(Node node)
        => IsAliasableExpression(GetExportAssignmentExpression(node));

    /// <summary>ported from: src/compiler/utilities.ts:5184-5186 — <c>getExportAssignmentExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's union parameter is a <c>Node</c> here for the same reason as
    /// <see cref="ExportAssignmentIsAlias"/>; TS's non-optional <c>expression</c>/<c>right</c> slots are nullable in
    /// the port, hence the <c>!</c>.
    /// </remarks>
    public static Expression GetExportAssignmentExpression(Node node)
        => NodeTests.IsExportAssignment(node)
            ? ((ExportAssignment)node).Expression!
            : ((BinaryExpression)node).Right!;

    /// <summary>ported from: src/compiler/utilities.ts:5348-5359 — <c>isAsyncFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>body !== undefined</c>/<c>asteriskToken === undefined</c> are the port's
    /// <c>Body is not null</c>/<c>AsteriskToken is null</c> (TS's <c>FunctionLikeDeclaration</c> union is the
    /// generated <see cref="IFunctionLikeDeclarationBase"/>). TS asks
    /// <c>hasSyntacticModifier(node, ModifierFlags.Async)</c>, which is <see cref="HasSyntacticModifier"/> — the
    /// four kinds are the ones whose modifiers <c>canHaveModifiers</c> admits.
    /// </remarks>
    public static bool IsAsyncFunction(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.MethodDeclaration:
            {
                var function = (IFunctionLikeDeclarationBase)node;
                return function.Body is not null && function.AsteriskToken is null && HasSyntacticModifier(node, ModifierFlags.Async);
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:5444-5446 — <c>getEscapedTextOfIdentifierOrLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>PropertyNameLiteral</c> (and its body also reaches a JSX namespaced
    /// name through the caller); the port takes <c>Node</c> and the branches cast. The first branch returns the
    /// *escaped* text (unlike <see cref="IdText"/>, which unescapes it), and <c>escapedText</c>/<c>text</c> are
    /// nullable slots in the port, so a missing text reads as the empty name TS never has.
    /// </remarks>
    public static string GetEscapedTextOfIdentifierOrLiteral(Node node)
    {
        if (IsMemberName(node))
        {
            return node is Identifier identifier ? identifier.EscapedText ?? "" : ((PrivateIdentifier)node).EscapedText ?? "";
        }

        if (NodeTests.IsJsxNamespacedName(node))
        {
            return GetEscapedTextOfJsxNamespacedName((JsxNamespacedName)node);
        }

        return EscapeLeadingUnderscores(((LiteralExpression)node).Text ?? "");
    }

    /// <summary>ported from: src/compiler/utilities.ts:5449-5451 — <c>getSymbolNameForPrivateIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the declaration-merged <c>Symbol</c> class, which the port spells
    /// <see cref="ISymbol"/>; <c>__String</c> is <c>string</c> until D4's pool lands (§5.1). The body needs
    /// <see cref="GetSymbolId"/>, which lives here with its counter — see that member's note.
    /// </remarks>
    public static string GetSymbolNameForPrivateIdentifier(ISymbol containingClassSymbol, string description)
        => $"__#{GetSymbolId(containingClassSymbol)}@{description}";

    /// <summary>ported from: src/compiler/checker.ts:1469-1476 — <c>getSymbolId</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS declares this and its <c>nextSymbolId</c> counter (checker.ts:1169) as module-level members of
    /// <c>checker.ts</c>, yet <c>binder.ts</c> imports the function — <c>binder.ts:693</c> mangles a private
    /// identifier's name with it — and <see cref="GetSymbolNameForPrivateIdentifier"/> is in this slice. The counter
    /// therefore lives here until the checker lands: M6 must call this member instead of declaring a second one, or
    /// the binder and the checker would hand out colliding ids.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>SymbolId</c> is a branded <c>number</c> and <c>symbol.id</c> starts <c>undefined</c>; the
    /// port's <see cref="ISymbol.Id"/> is a plain <c>int</c> whose <c>0</c> is that unset value (TS's
    /// <c>if (!symbol.id)</c>), and the counter starts at <c>1</c> like TS's.
    /// </para>
    /// </remarks>
    public static int GetSymbolId(ISymbol symbol)
    {
        if (symbol.Id == 0)
        {
            symbol.Id = s_nextSymbolId;
            s_nextSymbolId++;
        }

        return symbol.Id;
    }

    private static int s_nextSymbolId = 1;

    /// <summary>ported from: src/compiler/utilities.ts:5584-5586 — <c>isPushOrUnshiftIdentifier</c>.</summary>
    public static bool IsPushOrUnshiftIdentifier(Identifier node)
        => node.EscapedText == "push" || node.EscapedText == "unshift";

    /// <summary>ported from: src/compiler/utilities.ts:5597-5600 — <c>isPartOfParameterDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>Declaration</c>, which has no C# anchor; the port takes <c>Node</c>,
    /// which is what <see cref="GetRootDeclaration"/> walks anyway.
    /// </remarks>
    public static bool IsPartOfParameterDeclaration(Node node) => GetRootDeclaration(node).Kind == SyntaxKind.Parameter;

    // ---------------------------------------------------------------------------------------------
    // Value expressions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7484-7492 — <c>isDestructuringAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard answers <c>DestructuringAssignment</c>, a union with no C# anchor; the port answers
    /// <c>bool</c>, and reads <c>left</c> through <see cref="BinaryExpression"/> because
    /// <see cref="IsAssignmentExpression"/> already narrowed the kind.
    /// </remarks>
    public static bool IsDestructuringAssignment(Node node)
    {
        if (IsAssignmentExpression(node, /*excludeCompoundAssignment*/ true))
        {
            var kind = ((BinaryExpression)node).Left!.Kind;
            return kind is SyntaxKind.ObjectLiteralExpression or SyntaxKind.ArrayLiteralExpression;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7525-7532 — <c>isDottedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>||</c> chain is written as a kind switch — <c>&amp;&amp;</c> binds tighter there, so the two
    /// recursive arms are the only ones that read a child, which the switch arms keep.
    /// </remarks>
    public static bool IsDottedName(Expression node) => node.Kind switch
    {
        SyntaxKind.Identifier
            or SyntaxKind.ThisKeyword
            or SyntaxKind.SuperKeyword
            or SyntaxKind.MetaProperty => true,
        SyntaxKind.PropertyAccessExpression => IsDottedName(((PropertyAccessExpression)node).Expression!),
        SyntaxKind.ParenthesizedExpression => IsDottedName(((ParenthesizedExpression)node).Expression!),
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilities.ts:7597-7600 — <c>isEmptyObjectLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>properties.length</c> off the narrowed <c>ObjectLiteralExpression</c>; the port's
    /// <c>Properties</c> is a nullable slot, so the <c>!</c> carries TS's invariant that the factory filled it.
    /// </remarks>
    public static bool IsEmptyObjectLiteral(Node expression)
        => expression.Kind == SyntaxKind.ObjectLiteralExpression && ((ObjectLiteralExpression)expression).Properties!.Count == 0;

    /// <summary>ported from: src/compiler/utilities.ts:11144-11146 — <c>getEscapedTextOfJsxNamespacedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>namespace</c>/<c>name</c> are non-optional slots that the port types nullable, hence the
    /// <c>!</c>; <c>escapedText</c> is nullable too, so a missing one contributes an empty part rather than the
    /// crash TS would see on such a node.
    /// </remarks>
    public static string GetEscapedTextOfJsxNamespacedName(JsxNamespacedName node)
        => $"{node.Namespace!.EscapedText}:{IdText(node.Name!)}";
}
