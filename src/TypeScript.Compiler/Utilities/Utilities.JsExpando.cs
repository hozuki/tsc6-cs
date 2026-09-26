namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts, src/compiler/types.ts
/// <summary>
/// The JS dynamic-assignment and expando family of <c>utilities.ts</c> (M5-0).
/// </summary>
/// <remarks>
/// PORT-NOTE: <c>utilities.ts</c> is a Layer 5 file, but the binder's module/export slice (M5-4) asks this family
/// "does this assignment declare an export, a prototype member, or an expando?" long before M5 ports the rest of the
/// file. The members therefore land here first — on the same partial class and under the same arrangement as the M4d
/// slice in <c>Utilities.cs</c> and the parser-imports slice in <c>Utilities.Parser.cs</c>
/// (docs/architecture.md §15.9) — and the batch that owns the file extends it rather than duplicating it.
/// <para>
/// The file also carries the smaller <c>utilities.ts</c> members those bodies are defined in terms of —
/// <c>isSameEntityName</c>, <c>isAccessExpression</c>, <c>getLeftmostAccessExpression</c>,
/// <c>isEntityNameExpression</c>, <c>isPropertyAccessEntityNameExpression</c>, <c>isPrototypeAccess</c>,
/// <c>isPropertyNameLiteral</c>, <c>isSignedNumericLiteral</c>, <c>isAssignmentDeclaration</c>,
/// <c>isEffectiveModuleDeclaration</c>, <c>isModuleIdentifier</c> and the private <c>isLiteralLikeAccess</c> family —
/// because a faithful body needs them and no other batch has claimed them. Each carries its own line-cited summary,
/// so the M5 batch can see exactly what already exists. One member the bodies call is declared by a sibling slice
/// instead of here: <c>isDynamicName</c> (utilities.ts:5387) in <c>Utilities.Binder.cs</c>.
/// </para>
/// <para>
/// PORT-NOTE: TS's aliases over node unions (<c>AccessExpression</c>, <c>BindableStaticNameExpression</c>,
/// <c>BindableObjectDefinePropertyCall</c>, <c>EntityNameOrEntityNameExpression</c>, <c>DeclarationName</c>,
/// <c>MemberName</c>, <c>RequireOrImportCall</c>) have no C# counterpart, so a parameter TS types with one is
/// <c>Node</c> here and the body narrows with a kind switch (§12.5) — the same treatment
/// <see cref="IsSuperProperty"/> records. Type-guard predicates answer <c>bool</c>, because C# cannot narrow through
/// a predicate and the caller casts.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:3843-3858 — <c>isRequireCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares three signatures (3839, 3841, 3843) so that a literal <c>true</c> for the second
    /// argument narrows the result to <c>RequireOrImportCall &amp; { expression: Identifier; arguments:
    /// [StringLiteralLike] }</c>. C# cannot choose an overload by a literal argument value, so the port keeps the
    /// implementation signature and answers <c>bool</c>; a caller that needs the narrowed shape casts.
    /// </remarks>
    public static bool IsRequireCall(Node callExpression, bool requireStringLiteralLikeArgument)
    {
        if (!NodeTests.IsCallExpression(callExpression)) return false;

        var call = (CallExpression)callExpression;
        var expression = call.Expression;
        if (expression is null || !NodeTests.IsIdentifier(expression) || ((Identifier)expression).EscapedText != "require")
        {
            return false;
        }

        // TS's `args.length !== 1` — `length(undefined)` is 0 there, so a missing argument list answers false here too.
        var arguments = call.Arguments;
        if (arguments is null || arguments.Count != 1) return false;

        var arg = arguments[0];
        return !requireStringLiteralLikeArgument || IsStringLiteralLike(arg);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3875-3877 —
    /// <c>isVariableDeclarationInitializedToBareOrAccessedRequire</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows the node to <c>VariableDeclarationInitializedTo&lt;RequireOrImportCall |
    /// AccessExpression&gt;</c> (types.ts:5735 spells that alias). C# cannot narrow through a predicate, so this
    /// answers <c>bool</c> and a caller that needs <c>initializer</c> casts to <c>VariableDeclaration</c> itself.
    /// </remarks>
    public static bool IsVariableDeclarationInitializedToBareOrAccessedRequire(Node node)
        => IsVariableDeclarationInitializedWithRequireHelper(node, allowAccessedRequire: true);

    /// <summary>ported from: src/compiler/utilities.ts:3898-3902 —
    /// <c>isVariableDeclarationInitializedWithRequireHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the accessed form reads the leftmost access expression's <c>expression</c> member, which TS reaches
    /// through the <c>AccessExpression</c> union; <see cref="GetAccessExpressionExpression"/> does the same by
    /// switching on the kind.
    /// </remarks>
    private static bool IsVariableDeclarationInitializedWithRequireHelper(Node node, bool allowAccessedRequire)
    {
        if (!NodeTests.IsVariableDeclaration(node)) return false;

        var declaration = (VariableDeclaration)node;
        return declaration.Initializer is not null
            && IsRequireCall(
                allowAccessedRequire ? GetLeftmostAccessExpression(declaration.Initializer) : declaration.Initializer,
                requireStringLiteralLikeArgument: true);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3922-3924 — <c>isAssignmentDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Declaration</c>, which is <see cref="IDeclaration"/> here; the kind predicates
    /// take the <c>Node</c> class, so the body casts. Every implementation of the interface in the model is a node
    /// class, which is what makes the cast total — the same arrangement <c>forEachReturnStatement</c> uses for
    /// <c>Statement</c>.
    /// </remarks>
    public static bool IsAssignmentDeclaration(IDeclaration decl)
    {
        var node = (Node)decl;
        return NodeTests.IsBinaryExpression(node)
            || IsAccessExpression(node)
            || NodeTests.IsIdentifier(node)
            || NodeTests.IsCallExpression(node);
    }

    /// <summary>ported from: src/compiler/utilities.ts:3953-3960 — <c>hasExpandoValueProperty</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's callback is a <c>&amp;&amp;</c> chain whose last operand is an <c>Expression</c>, so `forEach`
    /// yields that expression; a C# <c>&amp;&amp;</c> cannot produce the expression, so the chain is spelled out as
    /// early returns and the last step returns the initializer. <c>forEach</c> stops at the first truthy result, which
    /// for a reference result is the first non-null one (<see cref="Core.ForEach{T, TU}"/> does the same).
    /// </remarks>
    private static Expression? HasExpandoValueProperty(ObjectLiteralExpression node, bool isPrototypeAssignment)
        => Core.ForEach<Node, Expression>(node.Properties, (property, _) =>
        {
            if (!NodeTests.IsPropertyAssignment(property)) return null;

            var assignment = (PropertyAssignment)property;
            var name = assignment.Name;
            if (name is null || !NodeTests.IsIdentifier(name) || ((Identifier)name).EscapedText != "value") return null;
            if (assignment.Initializer is null) return null;

            return GetExpandoInitializer(assignment.Initializer, isPrototypeAssignment);
        });

    /// <summary>ported from: src/compiler/utilities.ts:3968-3980 — <c>getAssignedExpandoInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>||</c> between the two calls tests truthiness of an <c>Expression</c>, which for a
    /// reference means "not undefined"; <c>??</c> is that test spelled the C# way.
    /// <para>
    /// PORT-NOTE: after <c>isBindableObjectDefinePropertyCall</c> TS's guard gives <c>arguments</c> the tuple type of
    /// types.ts:3055, so it may read <c>[1].text</c> and pass <c>[2]</c> where an <c>ObjectLiteralExpression</c> is
    /// wanted without a check. The port's guard only *proves* the length and the second argument's kind, so the
    /// literal text is read through the already-ported <c>getTextOfIdentifierOrLiteral</c> (utilities.ts:5439) and the
    /// third argument is matched on its kind: TS's unchecked assertion would make a non-literal third argument yield
    /// <c>undefined</c> through <c>forEach</c>, which is what the unmatched branch does here instead of throwing in the
    /// cast.
    /// </para>
    /// </remarks>
    public static Expression? GetAssignedExpandoInitializer(Node? node)
    {
        if (node is not null && node.Parent is not null && NodeTests.IsBinaryExpression(node.Parent)
            && ((BinaryExpression)node.Parent).OperatorToken!.Kind == SyntaxKind.EqualsToken)
        {
            var parent = (BinaryExpression)node.Parent;
            var isPrototypeAssignment = IsPrototypeAccess(parent.Left!);
            return GetExpandoInitializer(parent.Right!, isPrototypeAssignment)
                ?? GetDefaultedExpandoInitializer(parent.Left!, parent.Right!, isPrototypeAssignment);
        }

        if (node is not null && NodeTests.IsCallExpression(node) && IsBindableObjectDefinePropertyCall((CallExpression)node))
        {
            var arguments = ((CallExpression)node).Arguments!;
            var result = arguments[2] is ObjectLiteralExpression valueProperty
                ? HasExpandoValueProperty(valueProperty, GetTextOfIdentifierOrLiteral(arguments[1]) == "prototype")
                : null;
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3994-4009 — <c>getExpandoInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the initializer TS returns is typed <c>Expression</c> where it returns the node it was handed, so the
    /// port casts; the kind tests in front of each return are what make the cast total.
    /// </remarks>
    public static Expression? GetExpandoInitializer(Node initializer, bool isPrototypeAssignment)
    {
        if (NodeTests.IsCallExpression(initializer))
        {
            var e = SkipParentheses(((CallExpression)initializer).Expression!);
            return e.Kind == SyntaxKind.FunctionExpression || e.Kind == SyntaxKind.ArrowFunction
                ? (Expression)initializer
                : null;
        }

        if (initializer.Kind is SyntaxKind.FunctionExpression or SyntaxKind.ClassExpression or SyntaxKind.ArrowFunction)
        {
            return (Expression)initializer;
        }

        if (NodeTests.IsObjectLiteralExpression(initializer)
            && (((ObjectLiteralExpression)initializer).Properties!.Count == 0 || isPrototypeAssignment))
        {
            return (Expression)initializer;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4019-4026 — <c>getDefaultedExpandoInitializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>&amp;&amp;</c> chain short-circuits, so <c>getExpandoInitializer</c> runs only once the
    /// operator is known to be <c>||</c> or <c>??</c> and <c>isSameEntityName</c> only once an initializer was found;
    /// the early returns keep both orders.
    /// </remarks>
    private static Expression? GetDefaultedExpandoInitializer(
        Expression name,
        Expression initializer,
        bool isPrototypeAssignment)
    {
        if (!NodeTests.IsBinaryExpression(initializer)) return null;

        var binary = (BinaryExpression)initializer;
        if (binary.OperatorToken!.Kind is not (SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken)) return null;

        var e = GetExpandoInitializer(binary.Right!, isPrototypeAssignment);
        return e is not null && IsSameEntityName(name, binary.Left!) ? e : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4064-4083 — <c>isSameEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but both <c>getDefaultedExpandoInitializer</c> and
    /// <c>isDefaultedExpandoInitializer</c> are defined in terms of it, so it arrives with this slice.
    /// <para>
    /// PORT-NOTE: TS reads <c>initializer.expression</c> and <c>name.expression</c> off the union the
    /// <c>isLiteralLikeAccess</c> test establishes; <see cref="GetAccessExpressionExpression"/> switches on the kind.
    /// The three <c>escapedText</c> comparisons become one pattern test on the same string.
    /// </para>
    /// </remarks>
    public static bool IsSameEntityName(Expression name, Expression initializer)
    {
        if (IsPropertyNameLiteral(name) && IsPropertyNameLiteral(initializer))
        {
            return GetTextOfIdentifierOrLiteral(name) == GetTextOfIdentifierOrLiteral(initializer);
        }

        if (IsMemberName(name) && IsLiteralLikeAccess(initializer))
        {
            var initializerExpression = GetAccessExpressionExpression(initializer);
            var prefixedByGlobals = NodeTests.IsIdentifier(initializerExpression)
                && ((Identifier)initializerExpression).EscapedText is "window" or "self" or "global";
            if (initializerExpression.Kind == SyntaxKind.ThisKeyword || prefixedByGlobals)
            {
                return IsSameEntityName(name, GetNameOrArgument(initializer)!);
            }
        }

        if (IsLiteralLikeAccess(name) && IsLiteralLikeAccess(initializer))
        {
            return GetElementOrPropertyAccessName(name) == GetElementOrPropertyAccessName(initializer)
                && IsSameEntityName(GetAccessExpressionExpression(name), GetAccessExpressionExpression(initializer));
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4086-4091 — <c>getRightMostAssignedExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isAssignmentExpression</c> with <c>excludeCompoundAssignment</c> is a binary expression, which is
    /// what makes the <c>right</c> read total; the port's predicate answers <c>bool</c>, so the cast is spelled out.
    /// </remarks>
    public static Expression GetRightMostAssignedExpression(Expression node)
    {
        while (IsAssignmentExpression(node, true))
        {
            node = ((BinaryExpression)node).Right!;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4094-4096 — <c>isExportsIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>isIdentifier</c> guard is not the port's to reuse here without a cast, so the kind test is
    /// followed by the <c>Identifier</c> cast; the comparison is on <c>escapedText</c> exactly as TS writes it (not
    /// <c>idText</c>).
    /// </remarks>
    public static bool IsExportsIdentifier(Node node)
        => NodeTests.IsIdentifier(node) && ((Identifier)node).EscapedText == "exports";

    /// <summary>ported from: src/compiler/utilities.ts:4099-4101 — <c>isModuleIdentifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>isModuleExportsAccessExpression</c> is defined in terms of it.
    /// </remarks>
    public static bool IsModuleIdentifier(Node node)
        => NodeTests.IsIdentifier(node) && ((Identifier)node).EscapedText == "module";

    /// <summary>ported from: src/compiler/utilities.ts:4104-4108 — <c>isModuleExportsAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>LiteralLikeElementAccessExpression &amp; { expression: Identifier }</c>;
    /// the port answers <c>bool</c> and the caller casts. The two arms the guard admits are read through
    /// <see cref="GetAccessExpressionExpression"/>, because <c>node.expression</c> is a union member in TS.
    /// </remarks>
    public static bool IsModuleExportsAccessExpression(Node node)
    {
        if (!NodeTests.IsPropertyAccessExpression(node) && !IsLiteralLikeElementAccess(node)) return false;

        return IsModuleIdentifier(GetAccessExpressionExpression(node))
            && GetElementOrPropertyAccessName(node) == "exports";
    }

    /// <summary>ported from: src/compiler/utilities.ts:4113-4116 — <c>getAssignmentDeclarationKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>BinaryExpression | CallExpression</c> union, which has no C# counterpart,
    /// so the port takes <c>Node</c> and the worker narrows by kind; a caller holding either class passes it directly.
    /// </remarks>
    public static AssignmentDeclarationKind GetAssignmentDeclarationKind(Node expr)
    {
        var special = GetAssignmentDeclarationKindWorker(expr);
        return special == AssignmentDeclarationKind.Property || IsInJSFile(expr)
            ? special
            : AssignmentDeclarationKind.None;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4119-4127 — <c>isBindableObjectDefinePropertyCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>BindableObjectDefinePropertyCall</c> (types.ts:3055), whose tuple type is
    /// what lets <c>arguments[0]</c>/<c>[1]</c> be read; the port answers <c>bool</c> and callers keep indexing the
    /// list. The <c>&amp;&amp;</c> chain becomes early returns so the index reads stay behind the length test, which is
    /// the order TS evaluates them in.
    /// </remarks>
    public static bool IsBindableObjectDefinePropertyCall(CallExpression expr)
    {
        var arguments = expr.Arguments;
        if (arguments is null || arguments.Count != 3) return false;
        if (expr.Expression is not PropertyAccessExpression expression) return false;
        var receiver = expression.Expression;
        if (receiver is null || !NodeTests.IsIdentifier(receiver)) return false;
        if (IdText(receiver) != "Object") return false;
        if (IdText(expression.Name!) != "defineProperty") return false;

        return IsStringOrNumericLiteralLike(arguments[1])
            && IsBindableStaticNameExpression(arguments[0], true);
    }

    /// <summary>ported from: src/compiler/utilities.ts:4132-4134 — <c>isLiteralLikeAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>LiteralLikeElementAccessExpression | PropertyAccessExpression</c>, a union
    /// that has no C# anchor; the port answers <c>bool</c> (the same treatment as its two arms).
    /// </remarks>
    private static bool IsLiteralLikeAccess(Node node)
        => NodeTests.IsPropertyAccessExpression(node) || IsLiteralLikeElementAccess(node);

    /// <summary>ported from: src/compiler/utilities.ts:4139-4141 — <c>isLiteralLikeElementAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>LiteralLikeElementAccessExpression</c> (an <c>ElementAccessExpression</c>
    /// whose argument is a string or numeric literal, types.ts:3065); the port answers <c>bool</c> and callers that
    /// need the narrowed members cast.
    /// </remarks>
    private static bool IsLiteralLikeElementAccess(Node node)
        => NodeTests.IsElementAccessExpression(node)
            && IsStringOrNumericLiteralLike(((ElementAccessExpression)node).ArgumentExpression!);

    /// <summary>ported from: src/compiler/utilities.ts:4148-4151 — <c>isBindableStaticAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>BindableStaticAccessExpression</c> (types.ts:3080); the port answers
    /// <c>bool</c>. The <c>node.expression</c> that TS reads off its <c>PropertyAccessExpression</c> arm is a plain
    /// slot on that class, so no union helper is needed here; the port writes the nested <c>||</c> as a condition on
    /// one branch so the expression is read once.
    /// </remarks>
    public static bool IsBindableStaticAccessExpression(Node node, bool excludeThisKeyword = false)
    {
        if (NodeTests.IsPropertyAccessExpression(node))
        {
            var propertyAccess = (PropertyAccessExpression)node;
            if ((!excludeThisKeyword && propertyAccess.Expression!.Kind == SyntaxKind.ThisKeyword)
                || (NodeTests.IsIdentifier(propertyAccess.Name!)
                    && IsBindableStaticNameExpression(propertyAccess.Expression!, true)))
            {
                return true;
            }
        }

        return IsBindableStaticElementAccessExpression(node, excludeThisKeyword);
    }

    /// <summary>ported from: src/compiler/utilities.ts:4158-4163 — <c>isBindableStaticElementAccessExpression</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>isBindableStaticAccessExpression</c> is defined in terms of it.
    /// TS's guard narrows to <c>BindableStaticElementAccessExpression</c> (types.ts:3070); the port answers <c>bool</c>
    /// and reads <c>node.expression</c> through <see cref="GetAccessExpressionExpression"/>.
    /// </remarks>
    public static bool IsBindableStaticElementAccessExpression(Node node, bool excludeThisKeyword = false)
    {
        if (!IsLiteralLikeElementAccess(node)) return false;

        var expression = GetAccessExpressionExpression(node);
        return (!excludeThisKeyword && expression.Kind == SyntaxKind.ThisKeyword)
            || IsEntityNameExpression(expression)
            || IsBindableStaticAccessExpression(expression, true);
    }

    /// <summary>ported from: src/compiler/utilities.ts:4166-4168 — <c>isBindableStaticNameExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>BindableStaticNameExpression</c> (types.ts:3060, the
    /// <c>EntityNameExpression | BindableStaticElementAccessExpression</c> union); the port answers <c>bool</c> and a
    /// caller that needs the narrowed shape casts to the arm it checked.
    /// </remarks>
    public static bool IsBindableStaticNameExpression(Node node, bool excludeThisKeyword = false)
        => IsEntityNameExpression(node) || IsBindableStaticAccessExpression(node, excludeThisKeyword);

    /// <summary>ported from: src/compiler/utilities.ts:4171-4176 — <c>getNameOrArgument</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>PropertyAccessExpression | LiteralLikeElementAccessExpression</c> union and
    /// its result is <c>MemberName | (Expression &amp; (NumericLiteral | StringLiteralLike))</c>. Every member of that
    /// result is an <c>Expression</c> in the model, so the result is typed <c>Expression?</c>; the model's
    /// <c>name</c> and <c>argumentExpression</c> slots are nullable where TS declares them non-optional, which is what
    /// the <c>?</c> carries. The second arm casts to <c>ElementAccessExpression</c> because the first arm has already
    /// returned for a property access — the narrowing TS gets from the union.
    /// </remarks>
    public static Expression? GetNameOrArgument(Node expr)
        => NodeTests.IsPropertyAccessExpression(expr)
            ? ((PropertyAccessExpression)expr).Name
            : ((ElementAccessExpression)expr).ArgumentExpression;

    /// <summary>ported from: src/compiler/utilities.ts:4178-4200 — <c>getAssignmentDeclarationKindWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>BinaryExpression | CallExpression</c> union; the port takes <c>Node</c>,
    /// narrows the call arm with the kind test and casts the remaining node to <c>BinaryExpression</c>, which the union
    /// guarantees is the only other kind. <c>expr.left.expression</c> and <c>expr.left</c> are read through
    /// <see cref="GetAccessExpressionExpression"/> where TS reads the union member.
    /// </remarks>
    private static AssignmentDeclarationKind GetAssignmentDeclarationKindWorker(Node expr)
    {
        if (NodeTests.IsCallExpression(expr))
        {
            if (!IsBindableObjectDefinePropertyCall((CallExpression)expr)) return AssignmentDeclarationKind.None;

            var entityName = ((CallExpression)expr).Arguments![0];
            if (IsExportsIdentifier(entityName) || IsModuleExportsAccessExpression(entityName))
            {
                return AssignmentDeclarationKind.ObjectDefinePropertyExports;
            }

            if (IsBindableStaticAccessExpression(entityName) && GetElementOrPropertyAccessName(entityName) == "prototype")
            {
                return AssignmentDeclarationKind.ObjectDefinePrototypeProperty;
            }

            return AssignmentDeclarationKind.ObjectDefinePropertyValue;
        }

        var binary = (BinaryExpression)expr;
        if (binary.OperatorToken!.Kind != SyntaxKind.EqualsToken
            || !IsAccessExpression(binary.Left!)
            || IsVoidZero(GetRightMostAssignedExpression(binary.Left!)))
        {
            return AssignmentDeclarationKind.None;
        }

        if (IsBindableStaticNameExpression(GetAccessExpressionExpression(binary.Left!), true)
            && GetElementOrPropertyAccessName(binary.Left!) == "prototype"
            && NodeTests.IsObjectLiteralExpression(GetInitializerOfBinaryExpression(binary)))
        {
            // F.prototype = { ... }
            return AssignmentDeclarationKind.Prototype;
        }

        return GetAssignmentDeclarationPropertyAccessKind(binary.Left!);
    }

    /// <summary>ported from: src/compiler/utilities.ts:4202-4204 — <c>isVoidZero</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard chain is spelled out as an early return plus a <c>NumericLiteral</c> pattern so the
    /// <c>text</c> it compares can be read off the node the second kind test established.
    /// </remarks>
    private static bool IsVoidZero(Node node)
    {
        if (!NodeTests.IsVoidExpression(node)) return false;

        var expression = ((VoidExpression)node).Expression;
        return expression is NumericLiteral numeric && numeric.Text == "0";
    }

    /// <summary>ported from: src/compiler/utilities.ts:4212-4221 —
    /// <c>getElementOrPropertyAccessArgumentExpressionOrName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>AccessExpression</c> union and its result the
    /// <c>Identifier | PrivateIdentifier | StringLiteralLike | NumericLiteral | ElementAccessExpression | undefined</c>
    /// union; both collapse to <c>Node</c>/<c>Node?</c> here. The element-access arm casts because the property-access
    /// arm has already returned, and the fall-through result is the node itself, exactly as in TS.
    /// </remarks>
    public static Node? GetElementOrPropertyAccessArgumentExpressionOrName(Node node)
    {
        if (NodeTests.IsPropertyAccessExpression(node)) return ((PropertyAccessExpression)node).Name;

        var arg = SkipParentheses(((ElementAccessExpression)node).ArgumentExpression!);
        if (NodeTests.IsNumericLiteral(arg) || IsStringLiteralLike(arg)) return arg;

        return node;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4228-4239 — <c>getElementOrPropertyAccessName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads (4224, 4226) — the first answers a non-optional <c>__String</c> for the
    /// literal-like forms, the second <c>__String | undefined</c> — over one body that can answer <c>undefined</c>.
    /// The port keeps the single method and its nullable result; a caller that has already established the
    /// literal-like form can rely on it being non-null. <c>__String</c> is <c>string</c> here (§5).
    /// </remarks>
    public static string? GetElementOrPropertyAccessName(Node node)
    {
        var name = GetElementOrPropertyAccessArgumentExpressionOrName(node);
        if (name is not null)
        {
            if (NodeTests.IsIdentifier(name)) return ((Identifier)name).EscapedText;
            if (IsStringLiteralLike(name) || NodeTests.IsNumericLiteral(name))
            {
                return EscapeLeadingUnderscores(((LiteralExpression)name).Text!);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4242-4277 — <c>getAssignmentDeclarationPropertyAccessKind</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>AccessExpression</c> union, so the port takes <c>Node</c> and reads
    /// <c>lhs.expression</c> through <see cref="GetAccessExpressionExpression"/>. The walk that TS writes as
    /// <c>while (!isIdentifier(nextToLast.expression))</c> is the same walk; the guard that admits this branch
    /// (`isBindableStaticNameExpression(lhs.expression, true)`) is what makes it end at an identifier, which is why the
    /// final read can cast to <c>Identifier</c>.
    /// <para>
    /// PORT-NOTE: <c>isDynamicName</c> (utilities.ts:5387) is declared by the sibling slice that owns the name-lookup
    /// family, so this file calls <c>IsDynamicName</c> rather than declaring it.
    /// </para>
    /// </remarks>
    public static AssignmentDeclarationKind GetAssignmentDeclarationPropertyAccessKind(Node lhs)
    {
        var expression = GetAccessExpressionExpression(lhs);
        if (expression.Kind == SyntaxKind.ThisKeyword)
        {
            return AssignmentDeclarationKind.ThisProperty;
        }

        if (IsModuleExportsAccessExpression(lhs))
        {
            // module.exports = expr
            return AssignmentDeclarationKind.ModuleExports;
        }

        if (IsBindableStaticNameExpression(expression, true))
        {
            if (IsPrototypeAccess(expression))
            {
                // F.G....prototype.x = expr
                return AssignmentDeclarationKind.PrototypeProperty;
            }

            var nextToLast = lhs;
            while (!NodeTests.IsIdentifier(GetAccessExpressionExpression(nextToLast)))
            {
                nextToLast = GetAccessExpressionExpression(nextToLast);
            }

            var id = (Identifier)GetAccessExpressionExpression(nextToLast);
            if ((id.EscapedText == "exports"
                    || (id.EscapedText == "module" && GetElementOrPropertyAccessName(nextToLast) == "exports"))
                // ExportsProperty does not support binding with computed names
                && IsBindableStaticAccessExpression(lhs))
            {
                // exports.name = expr OR module.exports.name = expr OR exports["name"] = expr ...
                return AssignmentDeclarationKind.ExportsProperty;
            }

            if (IsBindableStaticNameExpression(lhs, true)
                || (NodeTests.IsElementAccessExpression(lhs) && IsDynamicName(lhs)))
            {
                // F.G...x = expr
                return AssignmentDeclarationKind.Property;
            }
        }

        return AssignmentDeclarationKind.None;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4280-4285 — <c>getInitializerOfBinaryExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>getAssignmentDeclarationKindWorker</c> is defined in terms of
    /// it.
    /// </remarks>
    public static Expression GetInitializerOfBinaryExpression(BinaryExpression expr)
    {
        while (NodeTests.IsBinaryExpression(expr.Right!))
        {
            expr = (BinaryExpression)expr.Right!;
        }

        return expr.Right!;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4307-4318 — <c>setValueDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>Declaration</c>, which is <see cref="IDeclaration"/> here; the <c>Node</c>-typed
    /// helpers take it through a cast (every implementation is a node class). The flag test on the first argument
    /// reads TS's <c>node.flags &amp; NodeFlags.Ambient</c> as a bit test, and the two <c>Node</c>-typed operands of
    /// <c>isEffectiveModuleDeclaration</c> are cast the same way.
    /// </remarks>
    public static void SetValueDeclaration(ISymbol symbol, IDeclaration node)
    {
        var valueDeclaration = symbol.ValueDeclaration;
        if (valueDeclaration is null
            || (!((node.Flags & NodeFlags.Ambient) != 0
                    && !IsInJSFile((Node)node)
                    && (valueDeclaration.Flags & NodeFlags.Ambient) == 0)
                && IsAssignmentDeclaration(valueDeclaration)
                && !IsAssignmentDeclaration(node))
            || (valueDeclaration.Kind != node.Kind && IsEffectiveModuleDeclaration((Node)valueDeclaration)))
        {
            // other kinds of value declarations take precedence over modules and assignment declarations
            symbol.ValueDeclaration = node;
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:4321-4327 — <c>isFunctionSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS answers <c>boolean | undefined</c> — the <c>&amp;&amp;</c> chain yields <c>undefined</c> when the
    /// declaration has no initializer — and every caller treats the result as truthy or falsy, so the port answers
    /// <c>bool</c> and spells the chain out. <c>isVariableDeclaration</c> narrows the declaration, so the port casts it
    /// to read <c>initializer</c>.
    /// </remarks>
    public static bool IsFunctionSymbol(ISymbol? symbol)
    {
        if (symbol is null || symbol.ValueDeclaration is null) return false;

        var decl = symbol.ValueDeclaration;
        if (decl.Kind == SyntaxKind.FunctionDeclaration) return true;

        return NodeTests.IsVariableDeclaration((Node)decl)
            && ((VariableDeclaration)decl).Initializer is not null
            && IsFunctionLike(((VariableDeclaration)decl).Initializer);
    }

    // ---------------------------------------------------------------------------------------------
    // Members the bodies above are defined in terms of
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2098-2100 — <c>isEffectiveModuleDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but <c>setValueDeclaration</c> is defined in terms of it.
    /// </remarks>
    private static bool IsEffectiveModuleDeclaration(Node node)
        => NodeTests.IsModuleDeclaration(node) || NodeTests.IsIdentifier(node);

    /// <summary>ported from: src/compiler/utilities.ts:5367-5369 — <c>isSignedNumericLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of the assignment, but the sibling slice's <c>isDynamicName</c> (utilities.ts:5387,
    /// declared in <c>Utilities.Binder.cs</c>) is defined in terms of it, so it lives here with the same line-cited
    /// arrangement as the rest of this file's helpers. TS's guard narrows to
    /// <c>PrefixUnaryExpression &amp; { operand: NumericLiteral }</c>; the port answers <c>bool</c> and the caller
    /// casts.
    /// </remarks>
    public static bool IsSignedNumericLiteral(Node node)
    {
        if (!NodeTests.IsPrefixUnaryExpression(node)) return false;

        var prefixUnary = (PrefixUnaryExpression)node;
        return prefixUnary.Operator is SyntaxKind.PlusToken or SyntaxKind.MinusToken
            && NodeTests.IsNumericLiteral(prefixUnary.Operand!);
    }

    /// <summary>ported from: src/compiler/utilities.ts:5427-5437 — <c>isPropertyNameLiteral</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>PropertyNameLiteral</c> (types.ts:2374, which also lists
    /// <c>JsxNamespacedName</c> and <c>BigIntLiteral</c> even though this switch does not admit them); the port answers
    /// <c>bool</c> and callers cast.
    /// </remarks>
    public static bool IsPropertyNameLiteral(Node node) => node.Kind switch
    {
        SyntaxKind.Identifier
            or SyntaxKind.StringLiteral
            or SyntaxKind.NoSubstitutionTemplateLiteral
            or SyntaxKind.NumericLiteral => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilities.ts:7500-7502 — <c>isEntityNameExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>EntityNameExpression</c> (types.ts:2965, the
    /// <c>Identifier | PropertyAccessEntityNameExpression</c> union); the port answers <c>bool</c>.
    /// </remarks>
    public static bool IsEntityNameExpression(Node node)
        => node.Kind == SyntaxKind.Identifier || IsPropertyAccessEntityNameExpression(node);

    /// <summary>ported from: src/compiler/utilities.ts:7535-7537 — <c>isPropertyAccessEntityNameExpression</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>PropertyAccessEntityNameExpression</c> (types.ts:2996, a
    /// <c>PropertyAccessExpression</c> whose <c>name</c> is an <c>Identifier</c>); the port answers <c>bool</c>.
    /// </remarks>
    public static bool IsPropertyAccessEntityNameExpression(Node node)
    {
        if (!NodeTests.IsPropertyAccessExpression(node)) return false;

        var propertyAccess = (PropertyAccessExpression)node;
        return NodeTests.IsIdentifier(propertyAccess.Name!) && IsEntityNameExpression(propertyAccess.Expression!);
    }

    /// <summary>ported from: src/compiler/utilities.ts:7563-7565 — <c>isPrototypeAccess</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>BindableStaticAccessExpression</c> (types.ts:3080); the port answers
    /// <c>bool</c> and the caller casts.
    /// </remarks>
    public static bool IsPrototypeAccess(Node node)
        => IsBindableStaticAccessExpression(node) && GetElementOrPropertyAccessName(node) == "prototype";

    /// <summary>ported from: src/compiler/utilities.ts:8355-8357 — <c>isAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>AccessExpression</c> (types.ts:2967, the
    /// <c>PropertyAccessExpression | ElementAccessExpression</c> union); the port answers <c>bool</c> and the caller
    /// casts to the arm it checked — the union has no shared member other than the one
    /// <see cref="GetAccessExpressionExpression"/> reads.
    /// </remarks>
    public static bool IsAccessExpression(Node node)
        => node.Kind is SyntaxKind.PropertyAccessExpression or SyntaxKind.ElementAccessExpression;

    /// <summary>ported from: src/compiler/utilities.ts:8374-8379 — <c>getLeftmostAccessExpression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the walk assigns <c>expr.expression</c> back into its own variable; TS reads that union member
    /// directly and the port reads it through <see cref="GetAccessExpressionExpression"/>.
    /// </remarks>
    public static Expression GetLeftmostAccessExpression(Expression expr)
    {
        while (IsAccessExpression(expr))
        {
            expr = GetAccessExpressionExpression(expr);
        }

        return expr;
    }

    /// <summary>ported from: src/compiler/types.ts:2967 — <c>AccessExpression.expression</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS has no function for this — <c>PropertyAccessExpression.expression</c> and
    /// <c>ElementAccessExpression.expression</c> are both declared, so <c>lhs.expression</c> type-checks on the union
    /// (types.ts:2967) and the read is inlined at each use. C# has no union, so the members that need the read go
    /// through this kind switch — the same treatment <see cref="IsSuperProperty"/> records. Every caller has already
    /// established one of the two kinds (through <see cref="IsAccessExpression"/>, <c>isPropertyAccessExpression</c> or
    /// <c>isLiteralLikeElementAccess</c>), so the default arm is a programming error rather than an answer TS can
    /// reach.
    /// <para>
    /// PORT-NOTE: this member is <c>public</c> rather than private because the binder reads the same union member:
    /// <c>bindWorker</c>'s access-expression arm declares the module export symbol on <c>expr.expression</c>
    /// (binder.ts:2896) and its binary-expression arm tests <c>left.expression</c> (binder.ts:2920). Making the read
    /// reachable keeps one home for it instead of a second kind switch in the binder.
    /// </para>
    /// </remarks>
    public static Expression GetAccessExpressionExpression(Node node) => node switch
    {
        PropertyAccessExpression propertyAccess => propertyAccess.Expression!,
        ElementAccessExpression elementAccess => elementAccess.Expression!,
        _ => throw new InvalidOperationException("unreachable: the node is not an access expression"),
    };
}
