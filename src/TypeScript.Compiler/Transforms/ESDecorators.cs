#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/esDecorators.ts:1-2533 — the whole ES decorators transform.
/// <summary>
/// ported from: src/compiler/transformers/esDecorators.ts:295-312 — <c>transformESDecorators</c>: the transform that
/// rewrites standard ("ES") decorators into <c>__esDecorate</c>/<c>__runInitializers</c> calls, together with the
/// class/`this`/`super` restructuring those calls require.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformESDecorators</c>, from
/// <c>transformers/esDecorators.ts</c>; its body is a table of closures over that function's locals, so the port states
/// the factory as <see cref="TransformESDecorators"/> and the closures as the members of
/// <see cref="ESDecoratorTransformer"/> — the same "closures → classes" treatment <c>Transformer.cs</c> records for
/// <c>transformNodes</c>'s context literal (docs/architecture.md §5 rule 6). The two neighbour modules the reference
/// calls into are <see cref="ClassThis"/> and <see cref="NamedEvaluation"/> — the ports of
/// <c>transformers/classThis.ts</c> and <c>transformers/namedEvaluation.ts</c>, both landed beside this slice (this
/// file's references to the second are qualified: <c>NamedEvaluation.TransformNamedEvaluation</c>,
/// <c>NamedEvaluation.IsNamedEvaluation</c>, <c>NamedEvaluation.ClassHasDeclaredOrExplicitlyAssignedName</c>,
/// <c>NamedEvaluation.IsClassNamedEvaluationHelperBlock</c> and
/// <c>NamedEvaluation.InjectClassNamedEvaluationHelperBlockIfMissing</c>), so no second copy of that module lives
/// here.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
/// <c>Func&lt;object?, object?&gt;</c> (Transformer.cs's header records why), so the factory answers that face and
/// <see cref="ESDecoratorTransformer.TransformSourceFileOrBundle"/> narrows with the same <c>isBundle</c> test the
/// reference's <c>chainBundle</c> makes.
/// </para>
/// <para>
/// PORT-NOTE (the visitor slots): TS's <c>Visitor</c>/<c>VisitResult&lt;T&gt;</c> are the port's
/// <c>Func&lt;Node, object?&gt;</c> and its <c>null</c> / node-list / node discrimination
/// (Utilities/Utilities.Visitor.cs), so every visitor here answers <c>object?</c> exactly as the reference's does.
/// </para>
/// </remarks>
internal static class ESDecorators
{
    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:295-312 — <c>transformESDecorators</c>.</summary>
    public static Func<object?, object?> TransformESDecorators(ITransformerContext context)
        => new ESDecoratorTransformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/esDecorators.ts:227-232 — the `MemberInfo` interface
/// <summary>ported from: src/compiler/transformers/esDecorators.ts:227-232 — <c>MemberInfo</c>.</summary>
internal sealed class MemberInfo
{
    /// <summary><c>memberDecoratorsName</c>: used in class definition step 4.a.</summary>
    public required Identifier MemberDecoratorsName { get; init; }

    /// <summary><c>memberInitializersName</c>: used in class definition step 12 and constructor evaluation step 2.a.</summary>
    public Identifier? MemberInitializersName { get; set; }

    /// <summary><c>memberExtraInitializersName</c>: used in class definition step 12 and constructor evaluation step 2.b.</summary>
    public Identifier? MemberExtraInitializersName { get; set; }

    /// <summary><c>memberDescriptorName</c>.</summary>
    public Identifier? MemberDescriptorName { get; set; }
}

// ported from: src/compiler/transformers/esDecorators.ts:234-260 — the `ClassInfo` interface
/// <summary>ported from: src/compiler/transformers/esDecorators.ts:234-260 — <c>ClassInfo</c>.</summary>
/// <remarks>
/// PORT-NOTE (<c>class</c>): TS's <c>class: ClassLikeDeclaration</c> is the union of <see cref="ClassDeclaration"/>
/// and <see cref="ClassExpression"/>, whose only common model face is <see cref="IClassLikeDeclarationBase"/> (the same
/// choice <c>Utilities.Binder.cs</c> records for the reference's union).
/// <para>
/// PORT-NOTE (<c>memberInfos</c>): TS's <c>Map&lt;ClassElement, MemberInfo&gt;</c> is read only through
/// <c>forEachEntry</c>, i.e. for its insertion order (the two loops that push the per-member <c>let</c> declarations),
/// and never by key — so the port states it as the ordered <see cref="List{T}"/> of member/info pairs below, where a
/// <c>Dictionary</c> would silently reorder the emitted declarations. The order of the list is the reference's
/// insertion order, since the only writer appends.
/// </para>
/// </remarks>
internal sealed class ClassInfo
{
    /// <summary><c>class</c>.</summary>
    public required IClassLikeDeclarationBase Class { get; init; }

    /// <summary><c>classDecoratorsName</c>: used in class definition step 2.</summary>
    public Identifier? ClassDecoratorsName { get; set; }

    /// <summary><c>classDescriptorName</c>: used in class definition step 10.</summary>
    public Identifier? ClassDescriptorName { get; set; }

    /// <summary><c>classExtraInitializersName</c>: used in class definition step 13.</summary>
    public Identifier? ClassExtraInitializersName { get; set; }

    /// <summary><c>classThis</c>: the <c>_classThis</c> name, if needed.</summary>
    public Identifier? ClassThis { get; set; }

    /// <summary><c>classSuper</c>: the <c>_classSuper</c> name, if needed.</summary>
    public Identifier? ClassSuper { get; set; }

    /// <summary><c>metadataReference</c>.</summary>
    public required Identifier MetadataReference { get; init; }

    /// <summary><c>memberInfos</c>: used in class definition step 4.a, 12, and constructor evaluation.</summary>
    public List<MemberInfoEntry>? MemberInfos { get; set; }

    /// <summary><c>instanceMethodExtraInitializersName</c>: used in constructor evaluation step 1.</summary>
    public Identifier? InstanceMethodExtraInitializersName { get; set; }

    /// <summary><c>staticMethodExtraInitializersName</c>: used in class definition step 11.</summary>
    public Identifier? StaticMethodExtraInitializersName { get; set; }

    /// <summary><c>staticNonFieldDecorationStatements</c>.</summary>
    public List<IStatement>? StaticNonFieldDecorationStatements { get; set; }

    /// <summary><c>nonStaticNonFieldDecorationStatements</c>.</summary>
    public List<IStatement>? NonStaticNonFieldDecorationStatements { get; set; }

    /// <summary><c>staticFieldDecorationStatements</c>.</summary>
    public List<IStatement>? StaticFieldDecorationStatements { get; set; }

    /// <summary><c>nonStaticFieldDecorationStatements</c>.</summary>
    public List<IStatement>? NonStaticFieldDecorationStatements { get; set; }

    /// <summary><c>hasStaticInitializers</c>.</summary>
    public bool HasStaticInitializers { get; set; }

    /// <summary><c>hasNonAmbientInstanceFields</c>.</summary>
    public bool HasNonAmbientInstanceFields { get; set; }

    /// <summary><c>hasStaticPrivateClassElements</c>.</summary>
    public bool HasStaticPrivateClassElements { get; set; }

    /// <summary><c>pendingStaticInitializers</c>.</summary>
    public List<Expression>? PendingStaticInitializers { get; set; }

    /// <summary><c>pendingInstanceInitializers</c>.</summary>
    public List<Expression>? PendingInstanceInitializers { get; set; }
}

/// <summary>The element/entry pair of the reference's <c>Map&lt;ClassElement, MemberInfo&gt;</c>.</summary>
/// <remarks>PORT-NOTE: see <see cref="ClassInfo.MemberInfos"/> — the reference's <c>forEachEntry(map, (info, member) =&gt; …)</c>
/// hands the value first and the key second, which is exactly the pair stored here.</remarks>
internal sealed class MemberInfoEntry(IClassElement member, MemberInfo info)
{
    /// <summary>The reference's <c>member</c> (the map key).</summary>
    public IClassElement Member { get; } = member;

    /// <summary>The reference's <c>memberInfo</c> (the map value).</summary>
    public MemberInfo Info { get; } = info;

    /// <summary>The reference's <c>forEachEntry(map, (memberInfo, member) =&gt; …)</c> argument order (value first).</summary>
    public void Deconstruct(out MemberInfo memberInfo, out IClassElement member)
    {
        memberInfo = Info;
        member = Member;
    }
}

// ported from: src/compiler/transformers/esDecorators.ts:262-295 — the lexical-environment stack entries
/// <summary>ported from: src/compiler/transformers/esDecorators.ts:288-295 — <c>LexicalEnvironmentStackEntry</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS discriminates the four entry shapes by a string <c>kind</c> member and reaches through the
/// intersection with <c>next</c>; the port states them as the class hierarchy below, where the discriminant is the
/// type and the <c>next</c> chain of each entry is typed by the entry the reference's own assertions require — the same
/// mapping <c>Parser/EmitHelpers.cs</c> records for <c>ESDecorateContext</c>.
/// </remarks>
internal abstract class LexicalEnvironmentStackEntry;

/// <summary>ported from: src/compiler/transformers/esDecorators.ts:262-267 — <c>ClassLexicalEnvironmentStackEntry</c>.</summary>
internal sealed class ClassLexicalEnvironmentStackEntry : LexicalEnvironmentStackEntry
{
    public required LexicalEnvironmentStackEntry? Next { get; init; }

    public required ClassInfo? ClassInfo { get; init; }

    public required List<Expression>? SavedPendingExpressions { get; init; }
}

/// <summary>ported from: src/compiler/transformers/esDecorators.ts:269-274 — <c>ClassElementLexicalEnvironmentStackEntry</c>.</summary>
internal sealed class ClassElementLexicalEnvironmentStackEntry : LexicalEnvironmentStackEntry
{
    public required ClassLexicalEnvironmentStackEntry Next { get; init; }

    public Identifier? ClassThis { get; set; }

    public Identifier? ClassSuper { get; set; }
}

/// <summary>ported from: src/compiler/transformers/esDecorators.ts:276-279 — <c>PropertyNameLexicalEnvironmentStackEntry</c>.</summary>
internal sealed class PropertyNameLexicalEnvironmentStackEntry : LexicalEnvironmentStackEntry
{
    public required ClassElementLexicalEnvironmentStackEntry Next { get; init; }
}

/// <summary>ported from: src/compiler/transformers/esDecorators.ts:281-286 — <c>OtherLexicalEnvironmentStackEntry</c>.</summary>
internal sealed class OtherLexicalEnvironmentStackEntry : LexicalEnvironmentStackEntry
{
    public required LexicalEnvironmentStackEntry? Next { get; init; }

    public required int Depth { get; set; }

    public required List<Expression>? SavedPendingExpressions { get; init; }
}

/// <summary>ported from: src/compiler/transformers/esDecorators.ts:295-2533 — the body of <c>transformESDecorators</c>.</summary>
/// <remarks>
/// PORT-NOTE (the state): the reference's <c>top</c>, <c>classInfo</c>, <c>classThis</c>, <c>classSuper</c>,
/// <c>pendingExpressions</c> and <c>shouldTransformPrivateStaticElementsInFile</c> are the factory function's locals
/// that the closures mutate; they are this class's fields.
/// </remarks>
internal sealed partial class ESDecoratorTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly ICompilerOptions _compilerOptions;
    private readonly ScriptTarget _languageVersion;

    private LexicalEnvironmentStackEntry? _top;
    private ClassInfo? _classInfo;
    private Identifier? _classThis;
    private Identifier? _classSuper;
    private List<Expression>? _pendingExpressions;
    private bool _shouldTransformPrivateStaticElementsInFile;

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:295-312 — the factory's prologue.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference destructures the context into <c>factory</c>, <c>getEmitHelperFactory</c>,
    /// <c>startLexicalEnvironment</c>, <c>endLexicalEnvironment</c> and <c>hoistVariableDeclaration</c>; the port keeps
    /// the context and reads the same members through it (<c>TransformationContext&lt;T&gt;</c>, Transformer.cs), so the
    /// sequence is the same. <c>startLexicalEnvironment</c>/<c>endLexicalEnvironment</c> are read through
    /// <see cref="_context"/> at their two call sites rather than captured, which is what the reference's closures do.
    /// </remarks>
    public ESDecoratorTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _compilerOptions = context.GetCompilerOptions();
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:312 — <c>chainBundle(context, transformSourceFile)</c>.</summary>
    /// <remarks>PORT-NOTE: <c>chainBundle</c> is <see cref="TransformationUtilities.ChainBundle"/>; the reference's union
    /// parameter is the <c>isBundle</c> test inside the delegate this class returns, stated here with the port's erased
    /// transformer face.</remarks>
    public object? TransformSourceFileOrBundle(object? node)
        => ((Node)node!).Kind == SyntaxKind.SourceFile
            ? TransformSourceFile((SourceFile)node!)
            : _factory.CreateBundle(Core.Map(((Bundle)node!).SourceFiles, (sourceFile, _) => TransformSourceFile(sourceFile)));

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:314-324 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        _top = null;
        _shouldTransformPrivateStaticElementsInFile = false;
        var visited = Utilities.VisitEachChildWorker(node, Visitor, _context);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());
        if (_shouldTransformPrivateStaticElementsInFile)
        {
            EmitNodes.AddInternalEmitFlags(visited, InternalEmitFlags.TransformPrivateStaticElements);
            _shouldTransformPrivateStaticElementsInFile = false;
        }
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:326-348 — <c>updateState</c>.</summary>
    private void UpdateState()
    {
        _classInfo = null;
        _classThis = null;
        _classSuper = null;
        switch (_top)
        {
            case ClassLexicalEnvironmentStackEntry classEntry:
                _classInfo = classEntry.ClassInfo;
                break;

            case ClassElementLexicalEnvironmentStackEntry classElement:
                _classInfo = classElement.Next.ClassInfo;
                _classThis = classElement.ClassThis;
                _classSuper = classElement.ClassSuper;
                break;

            case PropertyNameLexicalEnvironmentStackEntry name:
                {
                    var grandparent = name.Next.Next.Next;
                    if (grandparent is ClassElementLexicalEnvironmentStackEntry grandparentElement)
                    {
                        _classInfo = grandparentElement.Next.ClassInfo;
                        _classThis = grandparentElement.ClassThis;
                        _classSuper = grandparentElement.ClassSuper;
                    }
                    break;
                }
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:350-354 — <c>enterClass</c>.</summary>
    private void EnterClass(ClassInfo? classInfo)
    {
        _top = new ClassLexicalEnvironmentStackEntry { Next = _top, ClassInfo = classInfo, SavedPendingExpressions = _pendingExpressions };
        _pendingExpressions = null;
        UpdateState();
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:356-361 — <c>exitClass</c>.</summary>
    private void ExitClass()
    {
        Debug.Assert(_top is ClassLexicalEnvironmentStackEntry, "Incorrect value for top.kind.", () => $"Expected top.kind to be 'class' but got '{KindOf(_top)}' instead.");
        var entry = (ClassLexicalEnvironmentStackEntry)_top!;
        _pendingExpressions = entry.SavedPendingExpressions;
        _top = entry.Next;
        UpdateState();
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:363-371 — <c>enterClassElement</c>.</summary>
    private void EnterClassElement(Node node)
    {
        Debug.Assert(_top is ClassLexicalEnvironmentStackEntry, "Incorrect value for top.kind.", () => $"Expected top.kind to be 'class' but got '{KindOf(_top)}' instead.");
        var entry = new ClassElementLexicalEnvironmentStackEntry { Next = (ClassLexicalEnvironmentStackEntry)_top! };
        _top = entry;
        if (NodeTests.IsClassStaticBlockDeclaration(node) || NodeTests.IsPropertyDeclaration(node) && Utilities.HasStaticModifier(node))
        {
            entry.ClassThis = entry.Next.ClassInfo?.ClassThis;
            entry.ClassSuper = entry.Next.ClassInfo?.ClassSuper;
        }
        UpdateState();
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:373-378 — <c>exitClassElement</c>.</summary>
    private void ExitClassElement()
    {
        Debug.Assert(_top is ClassElementLexicalEnvironmentStackEntry, "Incorrect value for top.kind.", () => $"Expected top.kind to be 'class-element' but got '{KindOf(_top)}' instead.");
        var entry = (ClassElementLexicalEnvironmentStackEntry)_top!;
        Debug.Assert(entry.Next is ClassLexicalEnvironmentStackEntry, "Incorrect value for top.next.kind.", () => $"Expected top.next.kind to be 'class' but got '{KindOf(entry.Next)}' instead.");
        _top = entry.Next;
        UpdateState();
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:380-384 — <c>enterName</c>.</summary>
    private void EnterName()
    {
        Debug.Assert(_top is ClassElementLexicalEnvironmentStackEntry, "Incorrect value for top.kind.", () => $"Expected top.kind to be 'class-element' but got '{KindOf(_top)}' instead.");
        _top = new PropertyNameLexicalEnvironmentStackEntry { Next = (ClassElementLexicalEnvironmentStackEntry)_top! };
        UpdateState();
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:386-390 — <c>exitName</c>.</summary>
    private void ExitName()
    {
        Debug.Assert(_top is PropertyNameLexicalEnvironmentStackEntry, "Incorrect value for top.kind.", () => $"Expected top.kind to be 'name' but got '{KindOf(_top)}' instead.");
        _top = ((PropertyNameLexicalEnvironmentStackEntry)_top!).Next;
        UpdateState();
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:392-402 — <c>enterOther</c>.</summary>
    private void EnterOther()
    {
        if (_top is OtherLexicalEnvironmentStackEntry other)
        {
            Debug.Assert(_pendingExpressions is null);
            other.Depth++;
        }
        else
        {
            _top = new OtherLexicalEnvironmentStackEntry { Next = _top, Depth = 0, SavedPendingExpressions = _pendingExpressions };
            _pendingExpressions = null;
            UpdateState();
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:404-415 — <c>exitOther</c>.</summary>
    private void ExitOther()
    {
        Debug.Assert(_top is OtherLexicalEnvironmentStackEntry, "Incorrect value for top.kind.", () => $"Expected top.kind to be 'other' but got '{KindOf(_top)}' instead.");
        var entry = (OtherLexicalEnvironmentStackEntry)_top!;
        if (entry.Depth > 0)
        {
            Debug.Assert(_pendingExpressions is null);
            entry.Depth--;
        }
        else
        {
            _pendingExpressions = entry.SavedPendingExpressions;
            _top = entry.Next;
            UpdateState();
        }
    }

    /// <summary>The reference's <c>top?.kind</c>, used by the four assertion messages above.</summary>
    /// <remarks>PORT-NOTE: the port's discriminant is the entry's type, so the reference's own kind strings are recovered
    /// here for the messages only — nothing in the transform branches on them.</remarks>
    private static string KindOf(LexicalEnvironmentStackEntry? entry) => entry switch
    {
        ClassLexicalEnvironmentStackEntry => "class",
        ClassElementLexicalEnvironmentStackEntry => "class-element",
        PropertyNameLexicalEnvironmentStackEntry => "name",
        OtherLexicalEnvironmentStackEntry => "other",
        _ => "undefined",
    };

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:417-421 — <c>shouldVisitNode</c>.</summary>
    private bool ShouldVisitNode(Node node)
        => (node.TransformFlags & TransformFlags.ContainsDecorators) != 0
            || _classThis is not null && (node.TransformFlags & TransformFlags.ContainsLexicalThis) != 0
            || _classThis is not null && _classSuper is not null && (node.TransformFlags & TransformFlags.ContainsLexicalSuper) != 0;

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:423-493 — <c>visitor</c>.</summary>
    private object? Visitor(Node node)
    {
        if (!ShouldVisitNode(node))
        {
            return node;
        }

        switch (node.Kind)
        {
            case SyntaxKind.Decorator: // elided, will be emitted as part of `visitClassDeclaration`
                return Debug.Fail<object?>("Use `modifierVisitor` instead.");

            case SyntaxKind.ClassDeclaration:
                return VisitClassDeclaration((ClassDeclaration)node);

            case SyntaxKind.ClassExpression:
                return VisitClassExpression((ClassExpression)node);

            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return Debug.Fail<object?>("Not supported outside of a class. Use 'classElementVisitor' instead.");

            case SyntaxKind.Parameter:
                return VisitParameterDeclaration((ParameterDeclaration)node);

            // Support NamedEvaluation to ensure the correct class name for class expressions.
            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression((BinaryExpression)node, /*discarded*/ false);

            case SyntaxKind.PropertyAssignment:
                return VisitPropertyAssignment((PropertyAssignment)node);

            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration((VariableDeclaration)node);

            case SyntaxKind.BindingElement:
                return VisitBindingElement((BindingElement)node);

            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment((ExportAssignment)node);

            case SyntaxKind.ThisKeyword:
                return VisitThisExpression((ThisExpression)node);

            case SyntaxKind.ForStatement:
                return VisitForStatement((ForStatement)node);

            case SyntaxKind.ExpressionStatement:
                return VisitExpressionStatement((ExpressionStatement)node);

            case SyntaxKind.CommaListExpression:
                return VisitCommaListExpression((CommaListExpression)node, /*discarded*/ false);

            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression((ParenthesizedExpression)node, /*discarded*/ false);

            case SyntaxKind.PartiallyEmittedExpression:
                return VisitPartiallyEmittedExpression((PartiallyEmittedExpression)node, /*discarded*/ false);

            case SyntaxKind.CallExpression:
                return VisitCallExpression((CallExpression)node);

            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression((TaggedTemplateExpression)node);

            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, /*discarded*/ false);

            case SyntaxKind.PropertyAccessExpression:
                return VisitPropertyAccessExpression((PropertyAccessExpression)node);

            case SyntaxKind.ElementAccessExpression:
                return VisitElementAccessExpression((ElementAccessExpression)node);

            case SyntaxKind.ComputedPropertyName:
                return VisitComputedPropertyName((ComputedPropertyName)node);

            case SyntaxKind.MethodDeclaration: // object literal methods and accessors
            case SyntaxKind.SetAccessor:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.FunctionDeclaration:
                {
                    EnterOther();
                    var result = Utilities.VisitEachChildWorker(node, FallbackVisitor, _context);
                    ExitOther();
                    return result;
                }

            default:
                return Utilities.VisitEachChildWorker(node, FallbackVisitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:495-502 — <c>fallbackVisitor</c>.</summary>
    private object? FallbackVisitor(Node node)
        => NodeTests.IsDecorator(node) ? null : Visitor(node);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:504-511 — <c>modifierVisitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>ModifierLike</c> and its answer <c>Modifier | undefined</c>; the port's
    /// visitor slot is <c>Func&lt;Node, object?&gt;</c>, and the body is the reference's own kind test.</remarks>
    private static object? ModifierVisitor(Node node)
        => NodeTests.IsDecorator(node)
            // elided, will be emitted as part of `visitClassDeclaration`
            ? null
            : node;

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:513-530 — <c>classElementVisitor</c>.</summary>
    private object? ClassElementVisitor(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Constructor:
                return VisitConstructorDeclaration((ConstructorDeclaration)node);

            case SyntaxKind.MethodDeclaration:
                return VisitMethodDeclaration((MethodDeclaration)node);

            case SyntaxKind.GetAccessor:
                return VisitGetAccessorDeclaration((GetAccessorDeclaration)node);

            case SyntaxKind.SetAccessor:
                return VisitSetAccessorDeclaration((SetAccessorDeclaration)node);

            case SyntaxKind.PropertyDeclaration:
                return VisitPropertyDeclaration((PropertyDeclaration)node);

            case SyntaxKind.ClassStaticBlockDeclaration:
                return VisitClassStaticBlockDeclaration((ClassStaticBlockDeclaration)node);

            default:
                return Visitor(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:532-546 — <c>discardedValueVisitor</c>.</summary>
    private object? DiscardedValueVisitor(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.PrefixUnaryExpression:
            case SyntaxKind.PostfixUnaryExpression:
                return VisitPreOrPostfixUnaryExpression(node, /*discarded*/ true);

            case SyntaxKind.BinaryExpression:
                return VisitBinaryExpression((BinaryExpression)node, /*discarded*/ true);

            case SyntaxKind.CommaListExpression:
                return VisitCommaListExpression((CommaListExpression)node, /*discarded*/ true);

            case SyntaxKind.ParenthesizedExpression:
                return VisitParenthesizedExpression((ParenthesizedExpression)node, /*discarded*/ true);

            default:
                return Visitor(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:548-558 — <c>getHelperVariableName</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is the union <c>ClassLikeDeclaration | ClassElement</c>, which the model states
    /// as the common base <see cref="Node"/> plus the <see cref="INamedDeclaration"/> face its <c>name</c> slot needs —
    /// the same treatment <c>TypeScriptTransform.Classes.cs</c> records for its class/element unions.</remarks>
    private static string GetHelperVariableName(Node node)
    {
        var name = ((INamedDeclaration)node).Name;
        var declarationName = name is Identifier identifier && !Utilities.IsGeneratedIdentifier(identifier) ? Utilities.IdText(identifier) :
            name is PrivateIdentifier privateIdentifier && !Utilities.IsGeneratedIdentifier(privateIdentifier) ? Utilities.IdText(privateIdentifier)[1..] :
            name is StringLiteral stringLiteral && ScannerUtilities.IsIdentifierText(stringLiteral.Text!, ScriptTarget.ESNext) ? stringLiteral.Text! :
            Utilities.IsClassLike(node) ? "class" : "member";
        if (Utilities.IsGetAccessor(node)) declarationName = $"get_{declarationName}";
        if (Utilities.IsSetAccessor(node)) declarationName = $"set_{declarationName}";
        if (name is PrivateIdentifier) declarationName = $"private_{declarationName}";
        if (Utilities.IsStatic(node)) declarationName = $"static_{declarationName}";
        return "_" + declarationName;
    }

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:560-562 — <c>createHelperVariable</c>.</summary>
    private Identifier CreateHelperVariable(Node node, string suffix)
        => _factory.CreateUniqueName($"{GetHelperVariableName(node)}_{suffix}", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes);

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:564-576 — <c>createLet</c>.</summary>
    private VariableStatement CreateLet(Identifier name, Expression? initializer = null)
        => _factory.CreateVariableStatement(
            /*modifiers*/ null,
            _factory.CreateVariableDeclarationList(
            [
                _factory.CreateVariableDeclaration(
                    name,
                    /*exclamationToken*/ null,
                    /*type*/ null,
                    initializer),
            ], NodeFlags.Let));

    /// <summary>ported from: src/compiler/transformers/esDecorators.ts:578-668 — <c>createClassInfo</c>.</summary>
    private ClassInfo CreateClassInfo(IClassLikeDeclarationBase node)
    {
        var metadataReference = _factory.CreateUniqueName("_metadata", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
        Identifier? instanceMethodExtraInitializersName = null;
        Identifier? staticMethodExtraInitializersName = null;
        var hasStaticInitializers = false;
        var hasNonAmbientInstanceFields = false;
        var hasStaticPrivateClassElements = false;
        Identifier? classThis = null;
        List<Expression>? pendingStaticInitializers = null;
        List<Expression>? pendingInstanceInitializers = null;

        // Before visiting we perform a first pass to collect information we'll need
        // as we descend.

        if (Utilities.NodeIsDecorated(/*useLegacyDecorators*/ false, (Node)node))
        {
            // We do not mark _classThis as FileLevel if it may be reused by class private fields, which requires the
            // ability access the captured `_classThis` of outer scopes.
            var needsUniqueClassThis = Core.Some(node.Members, (member, _) => (Utilities.IsPrivateIdentifierClassElementDeclaration((Node)member) || Utilities.IsAutoAccessorPropertyDeclaration((Node)member)) && Utilities.HasStaticModifier((Node)member));
            classThis = _factory.CreateUniqueName(
                "_classThis",
                needsUniqueClassThis
                    ? GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes
                    : GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
        }

        foreach (var member in node.Members!)
        {
            if (Utilities.IsMethodOrAccessor((Node)member) && Utilities.NodeOrChildIsDecorated(/*useLegacyDecorators*/ false, (Node)member, (Node)node))
            {
                if (Utilities.HasStaticModifier((Node)member))
                {
                    if (staticMethodExtraInitializersName is null)
                    {
                        staticMethodExtraInitializersName = _factory.CreateUniqueName("_staticExtraInitializers", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
                        var initializer = _emitHelpers.CreateRunInitializersHelper(classThis is not null ? classThis : _factory.CreateThis(), staticMethodExtraInitializersName, /*value*/ null);
                        EmitNodes.SetSourceMapRange(initializer, NameOrDecoratorRange(node));
                        pendingStaticInitializers ??= [];
                        pendingStaticInitializers.Add(initializer);
                    }
                }
                else
                {
                    if (instanceMethodExtraInitializersName is null)
                    {
                        instanceMethodExtraInitializersName = _factory.CreateUniqueName("_instanceExtraInitializers", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
                        var initializer = _emitHelpers.CreateRunInitializersHelper(_factory.CreateThis(), instanceMethodExtraInitializersName, /*value*/ null);
                        EmitNodes.SetSourceMapRange(initializer, NameOrDecoratorRange(node));
                        pendingInstanceInitializers ??= [];
                        pendingInstanceInitializers.Add(initializer);
                    }
                    instanceMethodExtraInitializersName ??= _factory.CreateUniqueName("_instanceExtraInitializers", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
                }
            }
            if (NodeTests.IsClassStaticBlockDeclaration((Node)member))
            {
                if (!NamedEvaluation.IsClassNamedEvaluationHelperBlock((Node)member))
                {
                    hasStaticInitializers = true;
                }
            }
            else if (NodeTests.IsPropertyDeclaration((Node)member))
            {
                if (Utilities.HasStaticModifier((Node)member))
                {
                    hasStaticInitializers = hasStaticInitializers || ((PropertyDeclaration)member).Initializer is not null || Utilities.HasDecorators((Node)member);
                }
                else
                {
                    hasNonAmbientInstanceFields = hasNonAmbientInstanceFields || !Utilities.IsAmbientPropertyDeclaration((PropertyDeclaration)member);
                }
            }

            if ((Utilities.IsPrivateIdentifierClassElementDeclaration((Node)member) || Utilities.IsAutoAccessorPropertyDeclaration((Node)member)) && Utilities.HasStaticModifier((Node)member))
            {
                hasStaticPrivateClassElements = true;
            }

            // exit early if possible
            if (
                staticMethodExtraInitializersName is not null &&
                instanceMethodExtraInitializersName is not null &&
                hasStaticInitializers &&
                hasNonAmbientInstanceFields &&
                hasStaticPrivateClassElements
            )
            {
                break;
            }
        }

        return new ClassInfo
        {
            Class = node,
            ClassThis = classThis,
            MetadataReference = metadataReference,
            InstanceMethodExtraInitializersName = instanceMethodExtraInitializersName,
            StaticMethodExtraInitializersName = staticMethodExtraInitializersName,
            HasStaticInitializers = hasStaticInitializers,
            HasNonAmbientInstanceFields = hasNonAmbientInstanceFields,
            HasStaticPrivateClassElements = hasStaticPrivateClassElements,
            PendingStaticInitializers = pendingStaticInitializers,
            PendingInstanceInitializers = pendingInstanceInitializers,
        };
    }

    /// <summary>The reference's <c>node.name ?? moveRangePastDecorators(node)</c> source-map range.</summary>
    /// <remarks>PORT-NOTE (the model gap ClassThis.cs records): TS hands the class's <em>name node</em> to
    /// <c>setSourceMapRange</c>, legal there because <c>SourceMapRange</c> is an interface a <c>Node</c> satisfies
    /// structurally; no port node satisfies <see cref="ISourceMapRange"/>, so each arm is converted through
    /// <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/>, which copies the two members the printer reads.</remarks>
    private static ISourceMapRange NameOrDecoratorRange(IClassLikeDeclarationBase node)
        => TypeScriptTransformSupport.SourceMapRangeOf(
            node.Name ?? TypeScriptTransformSupport.MoveRangePastDecorators((Node)node));


}
