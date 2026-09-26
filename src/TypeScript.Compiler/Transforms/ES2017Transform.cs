#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2017.ts:1-1154 — the whole ES2017 downlevel transform.
/// <summary>
/// ported from: src/compiler/transformers/es2017.ts:119-1154 — <c>transformES2017</c>: the transformer that rewrites
/// <c>async</c>/<c>await</c> into the <c>__awaiter</c> call wrapping a generator function, elides the <c>async</c>
/// modifier, lowers the <c>for&hellip;in</c>/<c>for&hellip;of</c>/<c>var</c> declarations an async body may contain when a
/// declared name collides with a parameter name, captures the lexical <c>arguments</c> an async body reads, and
/// substitutes <c>super</c> accesses inside async methods.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the shape): as <c>ES2015Transform</c> records for <c>es2015.ts</c>, the reference exports one function
/// whose body is a table of closures over the factory's locals; the port states the factory as
/// <see cref="ES2017Transform.TransformES2017"/> and the closures as the members of
/// <see cref="ES2017Transformer"/> (the visiting half in <c>ES2017Transform.cs</c>, the substitution hooks in
/// <c>ES2017Transform.Substitution.cs</c>). The two module-level declarations the reference exports beside the factory —
/// <c>createSuperAccessVariableStatement</c> (es2017.ts:1059-1154) and the two const enums
/// (<c>ES2017SubstitutionFlags</c>, <c>ContextFlags</c>) — are stated here as well, because the file they would live in
/// is the landed <c>ES2020</c>/<c>ESNext</c> family's <c>es2018.ts</c>'s own import source.
/// </para>
/// <para>
/// PORT-NOTE (the generator machinery): the ES2017 target emits native generators, so the reference's
/// <c>transformES2017</c> does not call <c>transformGenerators</c> — it builds the <c>__awaiter(this, …, function () {
/// return __generator(…) })</c> shape through <c>emitHelpers().createAwaiterHelper</c>, whose generator-function
/// argument is <see cref="EmitHelpers.AwaiterHelper"/>'s own text (landed with the emit-helper slice). That is the
/// reference's <c>transformGenerators</c> hand-off, in the state the ES2017 target reaches it.
/// </para>
/// </remarks>
internal static class ES2017Transform
{
    /// <summary>ported from: src/compiler/transformers/es2017.ts:119-1154 — <c>transformES2017</c>.</summary>
    public static Func<object?, object?> TransformES2017(ITransformerContext context)
        => new ES2017Transformer(context).TransformSourceFileOrBundle;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:1059-1154 — <c>createSuperAccessVariableStatement</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the parameter types): TS types <c>node</c> as the <c>FunctionLikeDeclaration</c> union; the port takes
    /// the concrete <see cref="Node"/> the four callers already hold, and <c>names</c> as the insertion-ordered set the
    /// accessors' order depends on (see <see cref="ES2017TransformSupport.OrderedSet{T}"/>).
    /// </para>
    /// <para>
    /// PORT-NOTE (the empty parameter list): the reference's getter arrow is
    /// <c>createArrowFunction(undefined, undefined, [], undefined, undefined, body)</c> — the port's
    /// <see cref="NodeFactory.CreateArrowFunction"/> takes the same five absent slots plus the empty list, and its
    /// <c>equalsGreaterThanToken</c> default (<c>=&gt;</c>) is the reference's own.
    /// </para>
    /// </remarks>
    public static VariableStatement CreateSuperAccessVariableStatement(
        NodeFactory factory,
        IEmitResolver resolver,
        Node node,
        ES2017TransformSupport.OrderedSet<string> names)
    {
        // Create a variable declaration with a getter/setter (if binding) definition for each name:
        //   const _super = Object.create(null, { x: { get: () => super.x, set: (v) => super.x = v }, ... });
        var hasBinding = resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync);
        var accessors = new List<Node>();
        foreach (var key in names)
        {
            var name = Utilities.UnescapeLeadingUnderscores(key);
            var getterAndSetter = new List<Node>();
            getterAndSetter.Add(factory.CreatePropertyAssignment(
                "get",
                factory.CreateArrowFunction(
                    /*modifiers*/ null,
                    /*typeParameters*/ null,
                    /*parameters*/ [],
                    /*type*/ null,
                    /*equalsGreaterThanToken*/ null,
                    EmitNodes.SetEmitFlags(
                        factory.CreatePropertyAccessExpression(
                            EmitNodes.SetEmitFlags(factory.CreateSuper(), EmitFlags.NoSubstitution),
                            name),
                        EmitFlags.NoSubstitution))));
            if (hasBinding)
            {
                getterAndSetter.Add(
                    factory.CreatePropertyAssignment(
                        "set",
                        factory.CreateArrowFunction(
                            /*modifiers*/ null,
                            /*typeParameters*/ null,
                            /*parameters*/ [
                                factory.CreateParameterDeclaration(
                                    /*modifiers*/ null,
                                    /*dotDotDotToken*/ null,
                                    "v",
                                    /*questionToken*/ null,
                                    /*type*/ null,
                                    /*initializer*/ null),
                            ],
                            /*type*/ null,
                            /*equalsGreaterThanToken*/ null,
                            factory.CreateAssignment(
                                EmitNodes.SetEmitFlags(
                                    factory.CreatePropertyAccessExpression(
                                        EmitNodes.SetEmitFlags(factory.CreateSuper(), EmitFlags.NoSubstitution),
                                        name),
                                    EmitFlags.NoSubstitution),
                                factory.CreateIdentifier("v")))));
            }
            accessors.Add(
                factory.CreatePropertyAssignment(
                    name,
                    factory.CreateObjectLiteralExpression(getterAndSetter, /*multiLine*/ null)));
        }
        return factory.CreateVariableStatement(
            /*modifiers*/ null,
            factory.CreateVariableDeclarationList(
                [
                    factory.CreateVariableDeclaration(
                        factory.CreateUniqueName("_super", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel),
                        /*exclamationToken*/ null,
                        /*type*/ null,
                        factory.CreateCallExpression(
                            factory.CreatePropertyAccessExpression(
                                factory.CreateIdentifier("Object"),
                                "create"),
                            /*typeArguments*/ null,
                            [
                                factory.CreateNull(),
                                factory.CreateObjectLiteralExpression(accessors, /*multiLine*/ true),
                            ])),
                ],
                NodeFlags.Const));
    }
}

// ported from: src/compiler/transformers/es2017.ts:105-111 — the const enum `ES2017SubstitutionFlags`.
/// <summary>ported from: src/compiler/transformers/es2017.ts:105-111 — <c>ES2017SubstitutionFlags</c>.</summary>
[Flags]
internal enum ES2017SubstitutionFlags
{
    /// <summary><c>ES2017SubstitutionFlags.None</c>.</summary>
    None = 0,

    /// <summary><c>AsyncMethodsWithSuper</c> — enables substitutions for async methods with <c>super</c> calls.</summary>
    AsyncMethodsWithSuper = 1 << 0,
}

/// <summary>ported from: src/compiler/transformers/es2017.ts:120-1153 — the body of <c>transformES2017</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the state): the reference's <c>enabledSubstitutions</c>, <c>enclosingSuperContainerFlags</c>,
/// <c>enclosingFunctionParameterNames</c>, <c>capturedSuperProperties</c>, <c>hasSuperElementAccess</c>,
/// <c>lexicalArgumentsBinding</c>, <c>substitutedSuperAccessors</c> and <c>contextFlags</c> are the factory function's
/// locals that the closures mutate; they are this class's fields, under the same names.
/// </para>
/// <para>
/// PORT-NOTE (<c>ContextFlags</c>): the reference states this const enum at <c>transformers/es2017.ts:113-117</c> with
/// its own two members; <c>types.ts</c> already declares a <c>ContextFlags</c> (the checker's contextual-type flags,
/// <c>Model/ContextFlags.cs</c>), so the port states this one nested in <see cref="ES2017Transformer"/> — the two
/// reference declarations keep them apart by being in different modules, and nothing but this transform's own members
/// reads this one.
/// </para>
/// <para>
/// PORT-NOTE (the <c>SuperContainer</c> union): the reference's <c>type SuperContainer = ClassDeclaration |
/// MethodDeclaration | GetAccessorDeclaration | SetAccessorDeclaration | ConstructorDeclaration</c> is read only by
/// <c>isSuperContainer</c>'s type guard and by <c>onEmitNode</c>'s <c>resolver.hasNodeCheckFlag(node, …)</c> calls, so
/// the port states the guard's kind test as a <c>bool</c> and passes the node as it stands.
/// </para>
/// <para>
/// PORT-NOTE (the parameter-name sets): <c>enclosingFunctionParameterNames</c> is only tested for membership and copied
/// (<c>new Set(other)</c>, <c>delete</c>), so a <c>HashSet&lt;string&gt;</c> is that set exactly; the <c>__String</c>
/// element type is the port's <c>string</c> (the model's <c>EscapedText</c>).
/// </para>
/// <para>
/// PORT-NOTE (the escaped names): the reference's catch-clause set and the collision tests read
/// <c>name.escapedText</c>; the port reads the same <c>EscapedText</c> slot.
/// </para>
/// </remarks>
internal sealed partial class ES2017Transformer
{
    // ported from: src/compiler/transformers/es2017.ts:113-117 — the const enum `ContextFlags`.
    /// <summary>ported from: src/compiler/transformers/es2017.ts:113-117 — <c>ContextFlags</c>.</summary>
    /// <remarks>PORT-NOTE: see the class's note on why this enum is nested here.</remarks>
    [Flags]
    internal enum ContextFlags
    {
        /// <summary><c>ContextFlags.None</c>.</summary>
        None = 0,

        /// <summary><c>NonTopLevel</c>.</summary>
        NonTopLevel = 1 << 0,

        /// <summary><c>HasLexicalThis</c>.</summary>
        HasLexicalThis = 1 << 1,
    }

    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly ICompilerOptions _compilerOptions;
    private readonly ScriptTarget _languageVersion;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;
    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:165 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:136 — <c>enabledSubstitutions</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's comment records that this is persisted between each SourceFile
    /// transformation and is not reset; it is a field for that reason.</remarks>
    private ES2017SubstitutionFlags _enabledSubstitutions = ES2017SubstitutionFlags.None;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:142 — <c>enclosingSuperContainerFlags</c>.</summary>
    private NodeCheckFlags _enclosingSuperContainerFlags;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:144 — <c>enclosingFunctionParameterNames</c>.</summary>
    private HashSet<string>? _enclosingFunctionParameterNames;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:149 — <c>capturedSuperProperties</c>.</summary>
    private ES2017TransformSupport.OrderedSet<string>? _capturedSuperProperties;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:151 — <c>hasSuperElementAccess</c>.</summary>
    private bool _hasSuperElementAccess;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:152 — <c>lexicalArgumentsBinding</c>.</summary>
    private Identifier? _lexicalArgumentsBinding;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:154 — <c>substitutedSuperAccessors</c>.</summary>
    /// <remarks>PORT-NOTE: TS's hole-filled <c>boolean[]</c> indexed by node id reads <c>undefined</c> (falsy) past its
    /// end; the port states a <c>List&lt;bool&gt;</c> grown by <see cref="SetSubstitutedSuperAccessor"/> and read by
    /// <see cref="IsSubstitutedSuperAccessor"/>, which has the same two readings.</remarks>
    private readonly List<bool> _substitutedSuperAccessors = [];

    /// <summary>ported from: src/compiler/transformers/es2017.ts:156 — <c>contextFlags</c>.</summary>
    private ContextFlags _contextFlags = ContextFlags.None;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:120-167 — the factory's prologue.</summary>
    /// <remarks>PORT-NOTE: the reference destructures <c>factory</c>, <c>getEmitHelperFactory</c>,
    /// <c>resumeLexicalEnvironment</c>, <c>endLexicalEnvironment</c> and <c>hoistVariableDeclaration</c> out of the
    /// context; the port keeps the context and reads the same members through it, and the two hook slots it replaces
    /// are kept so the hooks it wraps can be restored by the calls it makes.</remarks>
    public ES2017Transformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _compilerOptions = context.GetCompilerOptions();
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);

        // Save the previous transformation hooks.
        _previousOnEmitNode = context.OnEmitNode!;
        _previousOnSubstituteNode = context.OnSubstituteNode!;

        // Set new transformation hooks.
        context.OnEmitNode = OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:165 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/es2017.ts:168-178 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        SetContextFlag(ContextFlags.NonTopLevel, false);
        SetContextFlag(ContextFlags.HasLexicalThis, !Utilities.IsEffectiveStrictModeSourceFile(node, _compilerOptions));
        var visited = Utilities.VisitEachChildWorker(node, Visitor, _context);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:180-182 — <c>setContextFlag</c>.</summary>
    private void SetContextFlag(ContextFlags flag, bool val)
        => _contextFlags = val ? _contextFlags | flag : _contextFlags & ~flag;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:184-186 — <c>inContext</c>.</summary>
    private bool InContext(ContextFlags flags) => (_contextFlags & flags) != 0;

    /// <summary>ported from: src/compiler/transformers/es2017.ts:188-190 — <c>inTopLevelContext</c>.</summary>
    private bool InTopLevelContext() => !InContext(ContextFlags.NonTopLevel);

    /// <summary>ported from: src/compiler/transformers/es2017.ts:192-194 — <c>inHasLexicalThisContext</c>.</summary>
    private bool InHasLexicalThisContext() => InContext(ContextFlags.HasLexicalThis);

    /// <summary>ported from: src/compiler/transformers/es2017.ts:196-205 — <c>doWithContext</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>cb: (value: T) =&gt; U</c> is the port's <c>Func&lt;TIn, TOut&gt;</c>; the reference's
    /// <c>if (contextFlagsToSet)</c> is the enum's non-zero test.</remarks>
    private TOut DoWithContext<TIn, TOut>(ContextFlags flags, Func<TIn, TOut> cb, TIn value)
    {
        var contextFlagsToSet = flags & ~_contextFlags;
        if (contextFlagsToSet != ContextFlags.None)
        {
            SetContextFlag(contextFlagsToSet, /*val*/ true);
            var result = cb(value);
            SetContextFlag(contextFlagsToSet, /*val*/ false);
            return result;
        }
        return cb(value);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:207-209 — <c>visitDefault</c>.</summary>
    private object? VisitDefault(Node node) => Utilities.VisitEachChildWorker(node, Visitor, _context);

    /// <summary>ported from: src/compiler/transformers/es2017.ts:211-231 — <c>argumentsVisitor</c>.</summary>
    private object? ArgumentsVisitor(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.Constructor:
                return node;
            case SyntaxKind.Parameter:
            case SyntaxKind.BindingElement:
            case SyntaxKind.VariableDeclaration:
                break;
            case SyntaxKind.Identifier:
                if (_lexicalArgumentsBinding is not null && _resolver.IsArgumentsLocalBinding((Identifier)node))
                {
                    return _lexicalArgumentsBinding;
                }
                break;
        }
        return Utilities.VisitEachChildWorker(node, ArgumentsVisitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:233-282 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>AsyncKeyword</c> arm answers <c>undefined</c> ("the ES2017 async modifier
    /// should be elided for targets &lt; ES2017"), which is the port's <c>null</c> — <see cref="Utilities.VisitNode"/>
    /// and <see cref="Utilities.VisitNodes"/> read it as "this element disappears".</remarks>
    private object? Visitor(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsES2017) == 0)
        {
            return _lexicalArgumentsBinding is not null ? ArgumentsVisitor(node) : node;
        }

        switch (node.Kind)
        {
            case SyntaxKind.AsyncKeyword:
                // ES2017 async modifier should be elided for targets < ES2017
                return null;

            case SyntaxKind.AwaitExpression:
                return VisitAwaitExpression((AwaitExpression)node);

            case SyntaxKind.MethodDeclaration:
                return DoWithContext(ContextFlags.NonTopLevel | ContextFlags.HasLexicalThis, VisitMethodDeclaration, (MethodDeclaration)node);

            case SyntaxKind.FunctionDeclaration:
                return DoWithContext(ContextFlags.NonTopLevel | ContextFlags.HasLexicalThis, VisitFunctionDeclaration, (FunctionDeclaration)node);

            case SyntaxKind.FunctionExpression:
                return DoWithContext(ContextFlags.NonTopLevel | ContextFlags.HasLexicalThis, VisitFunctionExpression, (FunctionExpression)node);

            case SyntaxKind.ArrowFunction:
                return DoWithContext(ContextFlags.NonTopLevel, VisitArrowFunction, (ArrowFunction)node);

            case SyntaxKind.PropertyAccessExpression:
                if (_capturedSuperProperties is not null && NodeTests.IsPropertyAccessExpression(node) && ((PropertyAccessExpression)node).Expression!.Kind == SyntaxKind.SuperKeyword)
                {
                    _capturedSuperProperties.Add(((Identifier)((PropertyAccessExpression)node).Name!).EscapedText!);
                }
                return Utilities.VisitEachChildWorker(node, Visitor, _context);

            case SyntaxKind.ElementAccessExpression:
                if (_capturedSuperProperties is not null && ((ElementAccessExpression)node).Expression!.Kind == SyntaxKind.SuperKeyword)
                {
                    _hasSuperElementAccess = true;
                }
                return Utilities.VisitEachChildWorker(node, Visitor, _context);

            case SyntaxKind.GetAccessor:
                return DoWithContext(ContextFlags.NonTopLevel | ContextFlags.HasLexicalThis, VisitGetAccessorDeclaration, (GetAccessorDeclaration)node);
            case SyntaxKind.SetAccessor:
                return DoWithContext(ContextFlags.NonTopLevel | ContextFlags.HasLexicalThis, VisitSetAccessorDeclaration, (SetAccessorDeclaration)node);
            case SyntaxKind.Constructor:
                return DoWithContext(ContextFlags.NonTopLevel | ContextFlags.HasLexicalThis, VisitConstructorDeclaration, (ConstructorDeclaration)node);
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
                return DoWithContext(ContextFlags.NonTopLevel | ContextFlags.HasLexicalThis, VisitDefault, node);

            default:
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:284-314 — <c>asyncBodyVisitor</c>.</summary>
    /// <remarks>PORT-NOTE: the default arm is the reference's <c>Debug.assertNever(node, "Unhandled node.")</c>; the
    /// port's <c>Debug.AssertNever</c> throws and answers nothing, so the arm breaks into the trailing
    /// <c>return Visitor(node);</c> the reference's own <c>if</c> states after the switch (unreachable either way).</remarks>
    private object? AsyncBodyVisitor(Node node)
    {
        if (Utilities.IsNodeWithPossibleHoistedDeclaration(node))
        {
            switch (node.Kind)
            {
                case SyntaxKind.VariableStatement:
                    return VisitVariableStatementInAsyncBody((VariableStatement)node);
                case SyntaxKind.ForStatement:
                    return VisitForStatementInAsyncBody((ForStatement)node);
                case SyntaxKind.ForInStatement:
                    return VisitForInStatementInAsyncBody((ForInStatement)node);
                case SyntaxKind.ForOfStatement:
                    return VisitForOfStatementInAsyncBody((ForOfStatement)node);
                case SyntaxKind.CatchClause:
                    return VisitCatchClauseInAsyncBody((CatchClause)node);
                case SyntaxKind.Block:
                case SyntaxKind.SwitchStatement:
                case SyntaxKind.CaseBlock:
                case SyntaxKind.CaseClause:
                case SyntaxKind.DefaultClause:
                case SyntaxKind.TryStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.IfStatement:
                case SyntaxKind.WithStatement:
                case SyntaxKind.LabeledStatement:
                    return Utilities.VisitEachChildWorker(node, AsyncBodyVisitor, _context);
                default:
                    Debug.AssertNever(node, "Unhandled node.");
                    break;
            }
        }
        return Visitor(node);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:316-341 — <c>visitCatchClauseInAsyncBody</c>.</summary>
    private CatchClause VisitCatchClauseInAsyncBody(CatchClause node)
    {
        var catchClauseNames = new HashSet<string>();
        RecordDeclarationName(node.VariableDeclaration!, catchClauseNames);

        // names declared in a catch variable are block scoped
        HashSet<string>? catchClauseUnshadowedNames = null;
        foreach (var escapedName in catchClauseNames)
        {
            if (_enclosingFunctionParameterNames!.Contains(escapedName))
            {
                catchClauseUnshadowedNames ??= [.. _enclosingFunctionParameterNames];
                catchClauseUnshadowedNames.Remove(escapedName);
            }
        }

        if (catchClauseUnshadowedNames is not null)
        {
            var savedEnclosingFunctionParameterNames = _enclosingFunctionParameterNames;
            _enclosingFunctionParameterNames = catchClauseUnshadowedNames;
            var result = Utilities.VisitEachChildWorker(node, AsyncBodyVisitor, _context);
            _enclosingFunctionParameterNames = savedEnclosingFunctionParameterNames;
            return result;
        }

        return Utilities.VisitEachChildWorker(node, AsyncBodyVisitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:343-349 — <c>visitVariableStatementInAsyncBody</c>.</summary>
    private object? VisitVariableStatementInAsyncBody(VariableStatement node)
    {
        if (IsVariableDeclarationListWithCollidingName(node.DeclarationList))
        {
            var expression = VisitVariableDeclarationListWithCollidingNames(node.DeclarationList!, /*hasReceiver*/ false);
            return expression is null ? null : _factory.CreateExpressionStatement(expression);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:351-360 — <c>visitForInStatementInAsyncBody</c>.</summary>
    private ForInStatement VisitForInStatementInAsyncBody(ForInStatement node)
        => _factory.UpdateForInStatement(
            node,
            IsVariableDeclarationListWithCollidingName(node.Initializer)
                ? VisitVariableDeclarationListWithCollidingNames((VariableDeclarationList)node.Initializer!, /*hasReceiver*/ true)
                : Debug.CheckDefined(Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsForInitializer)),
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
            Utilities.VisitIterationBody(node.Statement!, AsyncBodyVisitor, _context));

    /// <summary>ported from: src/compiler/transformers/es2017.ts:362-372 — <c>visitForOfStatementInAsyncBody</c>.</summary>
    private ForOfStatement VisitForOfStatementInAsyncBody(ForOfStatement node)
        => _factory.UpdateForOfStatement(
            node,
            Utilities.VisitNode(node.AwaitModifier, Visitor, NodeTests.IsAwaitKeyword),
            IsVariableDeclarationListWithCollidingName(node.Initializer)
                ? VisitVariableDeclarationListWithCollidingNames((VariableDeclarationList)node.Initializer!, /*hasReceiver*/ true)
                : Debug.CheckDefined(Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsForInitializer)),
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
            Utilities.VisitIterationBody(node.Statement!, AsyncBodyVisitor, _context));

    /// <summary>ported from: src/compiler/transformers/es2017.ts:374-392 — <c>visitForStatementInAsyncBody</c>.</summary>
    private ForStatement VisitForStatementInAsyncBody(ForStatement node)
        => _factory.UpdateForStatement(
            node,
            IsVariableDeclarationListWithCollidingName(node.Initializer)
                ? VisitVariableDeclarationListWithCollidingNames((VariableDeclarationList)node.Initializer!, /*hasReceiver*/ false)
                : Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsForInitializer),
            Utilities.VisitNode(node.Condition, Visitor, Utilities.IsExpression),
            Utilities.VisitNode(node.Incrementor, Visitor, Utilities.IsExpression),
            Utilities.VisitIterationBody(node.Statement!, AsyncBodyVisitor, _context));

    /// <summary>ported from: src/compiler/transformers/es2017.ts:394-409 — <c>visitAwaitExpression</c>.</summary>
    /// <remarks>
    /// <para>This function will be called any time a ES2017 await expression is encountered.</para>
    /// <para>PORT-NOTE: the reference's <c>createYieldExpression(undefined, …)</c> is the port's two-argument
    /// <c>CreateYieldExpression</c> with the absent asterisk token stated; <c>setOriginalNode(setTextRange(…, node),
    /// node)</c> is <see cref="NodeFactory.SetOriginalNode"/>/<see cref="Utilities.SetTextRange"/> in the reference's
    /// own nesting order.</para>
    /// </remarks>
    private Expression VisitAwaitExpression(AwaitExpression node)
    {
        // do not downlevel a top-level await as it is module syntax...
        if (InTopLevelContext())
        {
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
        return NodeFactory.SetOriginalNode(
            Utilities.SetTextRange(
                _factory.CreateYieldExpression(
                    /*asteriskToken*/ null,
                    Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression)),
                node),
            node);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:411-430 — <c>visitConstructorDeclaration</c>.</summary>
    private ConstructorDeclaration VisitConstructorDeclaration(ConstructorDeclaration node)
    {
        var savedLexicalArgumentsBinding = _lexicalArgumentsBinding;
        _lexicalArgumentsBinding = null;
        var updated = _factory.UpdateConstructorDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifier),
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            TransformMethodBody(node));
        _lexicalArgumentsBinding = savedLexicalArgumentsBinding;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:432-454 — <c>visitMethodDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: the reference assigns <c>parameters</c> inside the argument list — the async parameter-list
    /// transform runs after the modifiers are visited — so the port keeps the assignment in the same argument position
    /// rather than hoisting it above the call.</remarks>
    private MethodDeclaration VisitMethodDeclaration(MethodDeclaration node)
    {
        NodeArray<ParameterDeclaration>? parameters;
        var functionFlags = Utilities.GetFunctionFlags(node);
        var savedLexicalArgumentsBinding = _lexicalArgumentsBinding;
        _lexicalArgumentsBinding = null;
        var updated = _factory.UpdateMethodDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike),
            node.AsteriskToken,
            node.Name,
            /*questionToken*/ null,
            /*typeParameters*/ null,
            parameters = (functionFlags & FunctionFlags.Async) != 0
                ? TransformAsyncFunctionParameterList(node)
                : Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            (functionFlags & FunctionFlags.Async) != 0
                ? (Block)TransformAsyncFunctionBody(node, parameters!)
                : TransformMethodBody(node));
        _lexicalArgumentsBinding = savedLexicalArgumentsBinding;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:456-469 — <c>visitGetAccessorDeclaration</c>.</summary>
    private GetAccessorDeclaration VisitGetAccessorDeclaration(GetAccessorDeclaration node)
    {
        var savedLexicalArgumentsBinding = _lexicalArgumentsBinding;
        _lexicalArgumentsBinding = null;
        var updated = _factory.UpdateGetAccessorDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike),
            node.Name,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            TransformMethodBody(node));
        _lexicalArgumentsBinding = savedLexicalArgumentsBinding;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:471-484 — <c>visitSetAccessorDeclaration</c>.</summary>
    private SetAccessorDeclaration VisitSetAccessorDeclaration(SetAccessorDeclaration node)
    {
        var savedLexicalArgumentsBinding = _lexicalArgumentsBinding;
        _lexicalArgumentsBinding = null;
        var updated = _factory.UpdateSetAccessorDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike),
            node.Name,
            Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            TransformMethodBody(node));
        _lexicalArgumentsBinding = savedLexicalArgumentsBinding;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:493-522 — <c>visitFunctionDeclaration</c>.</summary>
    private IStatement VisitFunctionDeclaration(FunctionDeclaration node)
    {
        NodeArray<ParameterDeclaration>? parameters;
        var savedLexicalArgumentsBinding = _lexicalArgumentsBinding;
        _lexicalArgumentsBinding = null;
        var functionFlags = Utilities.GetFunctionFlags(node);
        var updated = _factory.UpdateFunctionDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifierLike),
            node.AsteriskToken,
            node.Name,
            /*typeParameters*/ null,
            parameters = (functionFlags & FunctionFlags.Async) != 0
                ? TransformAsyncFunctionParameterList(node)
                : Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            (functionFlags & FunctionFlags.Async) != 0
                ? (Block)TransformAsyncFunctionBody(node, parameters!)
                : Utilities.VisitFunctionBody(node.Body, Visitor, _context));
        _lexicalArgumentsBinding = savedLexicalArgumentsBinding;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:524-553 — <c>visitFunctionExpression</c>.</summary>
    private Expression VisitFunctionExpression(FunctionExpression node)
    {
        NodeArray<ParameterDeclaration>? parameters;
        var savedLexicalArgumentsBinding = _lexicalArgumentsBinding;
        _lexicalArgumentsBinding = null;
        var functionFlags = Utilities.GetFunctionFlags(node);
        var updated = _factory.UpdateFunctionExpression(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifier),
            node.AsteriskToken,
            node.Name,
            /*typeParameters*/ null,
            parameters = (functionFlags & FunctionFlags.Async) != 0
                ? TransformAsyncFunctionParameterList(node)
                : Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            (functionFlags & FunctionFlags.Async) != 0
                ? (Block)TransformAsyncFunctionBody(node, parameters!)
                : Utilities.VisitFunctionBody(node.Body, Visitor, _context));
        _lexicalArgumentsBinding = savedLexicalArgumentsBinding;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:555-571 — <c>visitArrowFunction</c>.</summary>
    /// <remarks>PORT-NOTE: an arrow function returns the awaiter call itself when its body is concise, so the reference's
    /// <c>transformAsyncFunctionBody</c> overload for it answers a <c>ConciseBody</c>; the port's single member answers
    /// the <see cref="Node"/> both arms build.</remarks>
    private ArrowFunction VisitArrowFunction(ArrowFunction node)
    {
        NodeArray<ParameterDeclaration>? parameters;
        var functionFlags = Utilities.GetFunctionFlags(node);
        return _factory.UpdateArrowFunction(
            node,
            Utilities.VisitNodes(node.Modifiers, Visitor, Utilities.IsModifier),
            /*typeParameters*/ null,
            parameters = (functionFlags & FunctionFlags.Async) != 0
                ? TransformAsyncFunctionParameterList(node)
                : Utilities.VisitParameterList(node.Parameters, Visitor, _context),
            /*type*/ null,
            node.EqualsGreaterThanToken,
            (functionFlags & FunctionFlags.Async) != 0
                ? TransformAsyncFunctionBody(node, parameters!)
                : Utilities.VisitFunctionBody(node.Body, Visitor, _context));
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:573-584 — <c>recordDeclarationName</c>.</summary>
    /// <remarks>PORT-NOTE: TS destructures <c>{ name }</c> out of the
    /// <c>ParameterDeclaration | VariableDeclaration | BindingElement</c> union; the port takes the node and reads the
    /// same <c>name</c> slot from the two shapes that carry it (both declare <c>Name</c>).</remarks>
    private static void RecordDeclarationName(Node node, HashSet<string> names)
    {
        var name = DeclarationNameOf(node);
        if (name is not null && NodeTests.IsIdentifier(name))
        {
            names.Add(((Identifier)name).EscapedText!);
        }
        else
        {
            foreach (var element in ES2017TransformSupport.BindingPatternElements(name!)!)
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    RecordDeclarationName(element, names);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:586-591 — <c>isVariableDeclarationListWithCollidingName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's type guard narrows the <c>ForInitializer</c> union to
    /// <c>VariableDeclarationList</c>; the port answers the same <c>bool</c> and lets the callers read the list through
    /// the narrowed kind (<see cref="NodeTests.IsVariableDeclarationList"/> is the same test).</remarks>
    private bool IsVariableDeclarationListWithCollidingName(Node? node)
        => node is not null
            && NodeTests.IsVariableDeclarationList(node)
            && (((VariableDeclarationList)node).Flags & NodeFlags.BlockScoped) == 0
            && Core.Some(((VariableDeclarationList)node).Declarations, CollidesWithParameterName);

    /// <summary>ported from: src/compiler/transformers/es2017.ts:593-605 — <c>visitVariableDeclarationListWithCollidingNames</c>.</summary>
    private Expression? VisitVariableDeclarationListWithCollidingNames(VariableDeclarationList node, bool hasReceiver)
    {
        HoistVariableDeclarationList(node);

        var variables = TypeScriptTransformSupport.GetInitializedVariables(node);
        if (variables.Count == 0)
        {
            if (hasReceiver)
            {
                return Utilities.VisitNode(_factory.Converters!.ConvertToAssignmentElementTarget(node.Declarations![0].Name), Visitor, Utilities.IsExpression);
            }
            return null;
        }

        return _factory.InlineExpressions(Core.Map(variables, (variable, _) => TransformInitializedVariable(variable))!);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:607-609 — <c>hoistVariableDeclarationList</c>.</summary>
    private void HoistVariableDeclarationList(VariableDeclarationList node)
    {
        foreach (var declaration in node.Declarations!)
        {
            HoistVariable(declaration);
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:611-622 — <c>hoistVariable</c>.</summary>
    private void HoistVariable(Node node)
    {
        var name = DeclarationNameOf(node);
        if (name is not null && NodeTests.IsIdentifier(name))
        {
            _context.HoistVariableDeclaration((Identifier)name);
        }
        else
        {
            foreach (var element in ES2017TransformSupport.BindingPatternElements(name!)!)
            {
                if (!NodeTests.IsOmittedExpression(element))
                {
                    HoistVariable(element);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:624-633 — <c>transformInitializedVariable</c>.</summary>
    private Expression TransformInitializedVariable(VariableDeclaration node)
    {
        var converted = EmitNodes.SetSourceMapRange(
            _factory.CreateAssignment(
                _factory.Converters!.ConvertToAssignmentElementTarget(node.Name),
                node.Initializer!),
            TypeScriptTransformSupport.SourceMapRangeOf(node));
        return Debug.CheckDefined(Utilities.VisitNode(converted, Visitor, Utilities.IsExpression));
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:635-647 — <c>collidesWithParameterName</c>.</summary>
    private bool CollidesWithParameterName(Node node)
    {
        var name = DeclarationNameOf(node);
        if (name is not null && NodeTests.IsIdentifier(name))
        {
            return _enclosingFunctionParameterNames!.Contains(((Identifier)name).EscapedText!);
        }
        else
        {
            foreach (var element in ES2017TransformSupport.BindingPatternElements(name!)!)
            {
                if (!NodeTests.IsOmittedExpression(element) && CollidesWithParameterName(element))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:649-691 — <c>transformMethodBody</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's parameter is the <c>MethodDeclaration | AccessorDeclaration | ConstructorDeclaration</c> union;
    /// the port takes the concrete <see cref="Node"/> the three callers hold and reads the union's two shared slots
    /// through the interfaces every member of it implements (<c>IFunctionLikeDeclarationBase.Body</c>,
    /// <c>ISignatureDeclarationBase.Parameters</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>getOriginalNode(node, isFunctionLikeDeclaration)</c>): the port's
    /// <see cref="Utilities.GetOriginalNode"/> takes the same predicate; its answer is only read by
    /// <c>getFunctionFlags</c>, so the port keeps the reference's two-step shape.
    /// </para>
    /// </remarks>
    private Block? TransformMethodBody(Node node)
    {
        Debug.AssertIsDefined(((IFunctionLikeDeclarationBase)node).Body);

        var savedCapturedSuperProperties = _capturedSuperProperties;
        var savedHasSuperElementAccess = _hasSuperElementAccess;
        _capturedSuperProperties = [];
        _hasSuperElementAccess = false;

        var updated = Debug.CheckDefined(
            Utilities.VisitFunctionBody((Block)((IFunctionLikeDeclarationBase)node).Body!, Visitor, _context));

        // Minor optimization, emit `_super` helper to capture `super` access in an arrow.
        // This step isn't needed if we eventually transform this to ES5.
        var originalMethod = Utilities.GetOriginalNode(node, Utilities.IsFunctionLikeDeclaration)!;
        var emitSuperHelpers = _languageVersion >= ScriptTarget.ES2015
            && (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync) || _resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAccessInAsync))
            && (Utilities.GetFunctionFlags(originalMethod) & FunctionFlags.AsyncGenerator) != FunctionFlags.AsyncGenerator;

        if (emitSuperHelpers)
        {
            EnableSubstitutionForAsyncMethodsWithSuper();
            if (_capturedSuperProperties!.Count > 0)
            {
                var variableStatement = ES2017Transform.CreateSuperAccessVariableStatement(_factory, _resolver, node, _capturedSuperProperties);
                SetSubstitutedSuperAccessor(Utilities.GetNodeId(variableStatement), true);

                var statements = new List<IStatement>(updated.Statements!);
                TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, [variableStatement]);
                updated = _factory.UpdateBlock(updated, statements);
            }

            if (_hasSuperElementAccess)
            {
                // Emit helpers for super element access expressions (`super[x]`).
                if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync))
                {
                    EmitNodes.AddEmitHelper(updated, EmitHelpers.AdvancedAsyncSuperHelper);
                }
                else if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAccessInAsync))
                {
                    EmitNodes.AddEmitHelper(updated, EmitHelpers.AsyncSuperHelper);
                }
            }
        }

        _capturedSuperProperties = savedCapturedSuperProperties;
        _hasSuperElementAccess = savedHasSuperElementAccess;
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:693-700 — <c>createCaptureArgumentsStatement</c>.</summary>
    private VariableStatement CreateCaptureArgumentsStatement()
    {
        Debug.Assert(_lexicalArgumentsBinding is not null);
        var variable = _factory.CreateVariableDeclaration(
            _lexicalArgumentsBinding,
            /*exclamationToken*/ null,
            /*type*/ null,
            _factory.CreateIdentifier("arguments"));
        var statement = _factory.CreateVariableStatement(/*modifiers*/ null, new List<VariableDeclaration> { variable });
        FactoryUtilities.StartOnNewLine(statement);
        EmitNodes.AddEmitFlags(statement, EmitFlags.CustomPrologue);
        return statement;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:702-734 — <c>transformAsyncFunctionParameterList</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>createToken(SyntaxKind.DotDotDotToken)</c> is the port's
    /// <see cref="NodeFactory.CreateToken"/> narrowed to the <c>PunctuationToken</c> the parameter slot takes, and its
    /// one-argument <c>createNodeArray</c> is the port's two-argument form with the absent trailing comma stated.</remarks>
    private NodeArray<ParameterDeclaration> TransformAsyncFunctionParameterList(Node node)
    {
        var parameters = ((ISignatureDeclarationBase)node).Parameters!;
        if (TransformationUtilities.IsSimpleParameterList(parameters))
        {
            return Utilities.VisitParameterList(parameters, Visitor, _context)!;
        }

        var newParameters = new List<ParameterDeclaration>();
        foreach (var parameter in parameters)
        {
            if (parameter.Initializer is not null || parameter.DotDotDotToken is not null)
            {
                // for an arrow function, capture the remaining arguments in a rest parameter.
                // for any other function/method this isn't necessary as we can just use `arguments`.
                if (node.Kind == SyntaxKind.ArrowFunction)
                {
                    var restParameter = _factory.CreateParameterDeclaration(
                        /*modifiers*/ null,
                        (PunctuationToken)_factory.CreateToken(SyntaxKind.DotDotDotToken),
                        _factory.CreateUniqueName("args", GeneratedIdentifierFlags.ReservedInNestedScopes),
                        /*questionToken*/ null,
                        /*type*/ null,
                        /*initializer*/ null);
                    newParameters.Add(restParameter);
                }
                break;
            }
            // for arrow functions we capture fixed parameters to forward to `__awaiter`. For all other functions
            // we add fixed parameters to preserve the function's `length` property.
            var newParameter = _factory.CreateParameterDeclaration(
                /*modifiers*/ null,
                /*dotDotDotToken*/ null,
                _factory.GetGeneratedNameForNode(parameter.Name, GeneratedIdentifierFlags.ReservedInNestedScopes),
                /*questionToken*/ null,
                /*type*/ null,
                /*initializer*/ null);
            newParameters.Add(newParameter);
        }
        var newParametersArray = _factory.CreateNodeArray(newParameters, /*hasTrailingComma*/ null);
        Utilities.SetTextRange(newParametersArray, parameters);
        return newParametersArray;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:736-872 — <c>transformAsyncFunctionBody</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the overloads): TS declares three signatures — one answering a <c>FunctionBody</c> for the
    /// method/accessor/function arms and one answering a <c>ConciseBody</c> for the arrow arm — whose bodies are this one
    /// implementation. The port states the implementation once, answering the <see cref="Node"/> both answer, and the
    /// non-arrow callers narrow it to the <c>Block</c> their <c>update*</c> slot takes (the reference's overload does the
    /// same at the type level).
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>node.body</c>/<c>node.type</c>/<c>node.parameters</c>): read through
    /// <c>IFunctionLikeDeclarationBase</c>/<c>ISignatureDeclarationBase</c>, the interfaces every member of the
    /// <c>FunctionLikeDeclaration</c> union implements.
    /// </para>
    /// <para>
    /// PORT-NOTE (<c>promiseConstructor</c>): the reference's <c>resolver.getTypeReferenceSerializationKind(typeName)</c>
    /// passes no location, which the ported checker states as an omitted location — the port passes <c>null</c> for the
    /// same call.
    /// </para>
    /// </remarks>
    private Node TransformAsyncFunctionBody(Node node, NodeArray<ParameterDeclaration> outerParameters)
    {
        var parameters = ((ISignatureDeclarationBase)node).Parameters!;
        var innerParameters = !TransformationUtilities.IsSimpleParameterList(parameters)
            ? Utilities.VisitParameterList(parameters, Visitor, _context)
            : null;
        _context.ResumeLexicalEnvironment();

        var original = Utilities.GetOriginalNode(node, Utilities.IsFunctionLike)!;
        var nodeType = ((ISignatureDeclarationBase)original).Type;
        var promiseConstructor = _languageVersion < ScriptTarget.ES2015 ? GetPromiseConstructor(nodeType) : null;
        var isArrowFunction = node.Kind == SyntaxKind.ArrowFunction;
        var savedLexicalArgumentsBinding = _lexicalArgumentsBinding;
        var hasLexicalArguments = _resolver.HasNodeCheckFlag(node, NodeCheckFlags.CaptureArguments);
        var captureLexicalArguments = hasLexicalArguments && _lexicalArgumentsBinding is null;
        if (captureLexicalArguments)
        {
            _lexicalArgumentsBinding = _factory.CreateUniqueName("arguments", /*flags*/ null);
        }

        Expression? argumentsExpression = null;
        if (innerParameters is not null)
        {
            if (isArrowFunction)
            {
                // `node` does not have a simple parameter list, so `outerParameters` refers to placeholders that are
                // forwarded to `innerParameters`, matching how they are introduced in `transformAsyncFunctionParameterList`.
                var parameterBindings = new List<Expression>();
                Debug.Assert(outerParameters.Count <= parameters.Count);
                for (var i = 0; i < parameters.Count; i++)
                {
                    Debug.Assert(i < outerParameters.Count);
                    var originalParameter = parameters[i];
                    var outerParameter = outerParameters[i];
                    Debug.AssertNode(outerParameter.Name, NodeTests.IsIdentifier);
                    if (originalParameter.Initializer is not null || originalParameter.DotDotDotToken is not null)
                    {
                        Debug.Assert(i == outerParameters.Count - 1);
                        parameterBindings.Add(_factory.CreateSpreadElement((Expression)outerParameter.Name!));
                        break;
                    }
                    parameterBindings.Add((Expression)outerParameter.Name!);
                }
                argumentsExpression = _factory.CreateArrayLiteralExpression(parameterBindings, /*multiLine*/ null);
            }
            else
            {
                argumentsExpression = _factory.CreateIdentifier("arguments");
            }
        }

        // An async function is emit as an outer function that calls an inner
        // generator function. To preserve lexical bindings, we pass the current
        // `this` and `arguments` objects to `__awaiter`. The generator function
        // passed to `__awaiter` is executed inside of the callback to the
        // promise constructor.

        var savedEnclosingFunctionParameterNames = _enclosingFunctionParameterNames;
        _enclosingFunctionParameterNames = [];
        foreach (var parameter in parameters)
        {
            RecordDeclarationName(parameter, _enclosingFunctionParameterNames);
        }

        var savedCapturedSuperProperties = _capturedSuperProperties;
        var savedHasSuperElementAccess = _hasSuperElementAccess;
        if (!isArrowFunction)
        {
            _capturedSuperProperties = [];
            _hasSuperElementAccess = false;
        }

        var hasLexicalThis = InHasLexicalThisContext();

        var asyncBody = TransformAsyncFunctionBodyWorker(((IFunctionLikeDeclarationBase)node).Body!);
        asyncBody = _factory.UpdateBlock(asyncBody, _factory.MergeLexicalEnvironment(asyncBody.Statements, _context.EndLexicalEnvironment()));

        Node result;
        if (!isArrowFunction)
        {
            var statements = new List<IStatement>
            {
                _factory.CreateReturnStatement(
                    _emitHelpers.CreateAwaiterHelper(
                        hasLexicalThis,
                        argumentsExpression,
                        promiseConstructor,
                        innerParameters,
                        asyncBody)),
            };

            // Minor optimization, emit `_super` helper to capture `super` access in an arrow.
            // This step isn't needed if we eventually transform this to ES5.
            var emitSuperHelpers = _languageVersion >= ScriptTarget.ES2015
                && (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync) || _resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAccessInAsync));

            if (emitSuperHelpers)
            {
                EnableSubstitutionForAsyncMethodsWithSuper();
                if (_capturedSuperProperties!.Count > 0)
                {
                    var variableStatement = ES2017Transform.CreateSuperAccessVariableStatement(_factory, _resolver, node, _capturedSuperProperties);
                    SetSubstitutedSuperAccessor(Utilities.GetNodeId(variableStatement), true);
                    TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, [variableStatement]);
                }
            }

            if (captureLexicalArguments)
            {
                TypeScriptTransformSupport.InsertStatementsAfterStandardPrologue(statements, [CreateCaptureArgumentsStatement()]);
            }

            var block = _factory.CreateBlock(statements, /*multiLine*/ true);
            Utilities.SetTextRange(block, ((IFunctionLikeDeclarationBase)node).Body!);

            if (emitSuperHelpers && _hasSuperElementAccess)
            {
                // Emit helpers for super element access expressions (`super[x]`).
                if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAssignmentInAsync))
                {
                    EmitNodes.AddEmitHelper(block, EmitHelpers.AdvancedAsyncSuperHelper);
                }
                else if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.MethodWithSuperPropertyAccessInAsync))
                {
                    EmitNodes.AddEmitHelper(block, EmitHelpers.AsyncSuperHelper);
                }
            }

            result = block;
        }
        else
        {
            result = _emitHelpers.CreateAwaiterHelper(
                hasLexicalThis,
                argumentsExpression,
                promiseConstructor,
                innerParameters,
                asyncBody);

            if (captureLexicalArguments)
            {
                var block = _factory.Converters!.ConvertToFunctionBlock(result, /*multiLine*/ null);
                result = _factory.UpdateBlock(block, _factory.MergeLexicalEnvironment(block.Statements, [CreateCaptureArgumentsStatement()]));
            }
        }

        _enclosingFunctionParameterNames = savedEnclosingFunctionParameterNames;
        if (!isArrowFunction)
        {
            _capturedSuperProperties = savedCapturedSuperProperties;
            _hasSuperElementAccess = savedHasSuperElementAccess;
            _lexicalArgumentsBinding = savedLexicalArgumentsBinding;
        }
        return result;
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:874-881 — <c>transformAsyncFunctionBodyWorker</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>convertToFunctionBlock(…)</c> passes no <c>multiLine</c>; the port states the
    /// absent argument.</remarks>
    private Block TransformAsyncFunctionBodyWorker(Node body, int? start = null)
    {
        if (NodeTests.IsBlock(body))
        {
            var block = (Block)body;
            return _factory.UpdateBlock(block, Utilities.VisitNodes(block.Statements, AsyncBodyVisitor, Utilities.IsStatement, start));
        }
        return _factory.Converters!.ConvertToFunctionBlock(
            Debug.CheckDefined(Utilities.VisitNode(body, AsyncBodyVisitor, Utilities.IsConciseBody)),
            /*multiLine*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/es2017.ts:883-896 — <c>getPromiseConstructor</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>type &amp;&amp; getEntityNameFromTypeNode(type)</c> is the port's null test plus
    /// <see cref="Utilities.GetEntityNameFromTypeNode"/> (whose parameter is the non-null <c>ITypeNode</c>);
    /// <c>isEntityName</c> is <see cref="Utilities.IsEntityName"/>.</remarks>
    private Node? GetPromiseConstructor(ITypeNode? type)
    {
        var typeName = type is null ? null : Utilities.GetEntityNameFromTypeNode(type);
        if (typeName is not null && Utilities.IsEntityName(typeName))
        {
            var serializationKind = _resolver.GetTypeReferenceSerializationKind(typeName, /*location*/ null);
            if (
                serializationKind == TypeReferenceSerializationKind.TypeWithConstructSignatureAndValue
                || serializationKind == TypeReferenceSerializationKind.Unknown
            )
            {
                return typeName;
            }
        }

        return null;
    }

    /// <summary>TS's <c>substitutedSuperAccessors[getNodeId(node)] = true</c>; see the field's note.</summary>
    private void SetSubstitutedSuperAccessor(int nodeId, bool value)
    {
        while (_substitutedSuperAccessors.Count <= nodeId)
        {
            _substitutedSuperAccessors.Add(false);
        }
        _substitutedSuperAccessors[nodeId] = value;
    }

    /// <summary>The read half of TS's <c>substitutedSuperAccessors[getNodeId(node)]</c>; see the field's note.</summary>
    private bool IsSubstitutedSuperAccessor(int nodeId)
        => nodeId < _substitutedSuperAccessors.Count && _substitutedSuperAccessors[nodeId];

    /// <summary>The <c>name</c> slot the reference destructures out of a
    /// <c>ParameterDeclaration | VariableDeclaration | BindingElement</c>.</summary>
    /// <remarks>PORT-NOTE: the three members of that union declare <c>Name</c> on their own classes in the port (the
    /// model has no shared base), so this reader states the three reads the reference's destructuring performs.</remarks>
    private static Node? DeclarationNameOf(Node node)
        => node.Kind switch
        {
            SyntaxKind.Parameter => ((ParameterDeclaration)node).Name,
            SyntaxKind.VariableDeclaration => ((VariableDeclaration)node).Name,
            SyntaxKind.BindingElement => ((BindingElement)node).Name,
            _ => null,
        };
}
