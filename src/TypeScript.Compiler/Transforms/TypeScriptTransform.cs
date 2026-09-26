#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/ts.ts:209-2750 — the whole TypeScript-specific erase transform.
/// <summary>
/// ported from: src/compiler/transformers/ts.ts:235-2750 — <c>transformTypeScript</c>: the transformer that erases the
/// TypeScript-only syntax of a <see cref="SourceFile"/> (or of every file of a <see cref="Bundle"/>), leaving the
/// JavaScript-shaped tree every later transform and the printer work on.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformTypeScript</c>, from
/// <c>transformers/ts.ts</c>; its body is a table of closures over the factory's locals, so the port states the factory
/// as <see cref="TransformTypeScript"/> and the closures as the members of <see cref="TypeScriptTransformer"/> — the
/// same "closures → classes" treatment <c>Transformer.cs</c> records for <c>transformNodes</c>'s context literal
/// (docs/architecture.md §5 rule 6).
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is
/// <c>(node: SourceFile | Bundle) =&gt; SourceFile | Bundle</c>; the port's transformer list spells its slots
/// <c>Func&lt;object?, object?&gt;</c> (Transformer.cs's header records why), so the factory answers that face and the
/// two arms of <c>transformSourceFileOrBundle</c> narrow with the kind test the reference's own <c>isBundle</c> uses.
/// </para>
/// <para>
/// PORT-NOTE (the visitor slots): TS's <c>visitor</c>/<c>nodesVisitor</c> are the <c>Visitor</c> function type, which
/// the port spells <c>Func&lt;Node, object?&gt;</c> (Utilities/Utilities.Visitor.cs), and <c>VisitResult&lt;T&gt;</c> is
/// <c>T | readonly Node[] | undefined</c>, which the port discriminates the same way <c>NodeFactory.CopyCustomPrologue</c>
/// does (<c>null</c>, then a node list, then a node).
/// </para>
/// <para>
/// PORT-NOTE (<c>T extends Node</c>): TS's generic members over a node type (<c>saveStateAndInvoke</c>,
/// <c>visitNodes</c>&hellip;) keep one type variable per call, whose constraint is the model's
/// <see cref="Node"/> class (contract §3: the visitor slot is typed over the class, the slots that hold a
/// categorisation interface are converted at the boundary).
/// </para>
/// <para>
/// PORT-NOTE (the neighbour slices): <c>ts.ts:246</c>'s <c>createRuntimeTypeSerializer</c> is
/// <see cref="TypeSerializer.CreateRuntimeTypeSerializer"/> and its context object is
/// <see cref="TypeSerializer.RuntimeTypeSerializerContext"/> — that module's own M7-B slice, landed beside this one.
/// Every member of the reference is stated here.
/// </para>
/// </remarks>
internal static class TypeScriptTransform
{
    /// <summary>ported from: src/compiler/transformers/ts.ts:235-283 — <c>transformTypeScript</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
    /// <c>Func&lt;object?, object?&gt;</c>; the returned member is the reference's own
    /// <c>transformSourceFileOrBundle</c>, staying a method group so no delegate is allocated per call beyond the one
    /// the caller stores.
    /// </remarks>
    public static Func<object?, object?> TransformTypeScript(ITransformerContext context)
        => new TypeScriptTransformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/ts.ts:211-217 — the const enum `TypeScriptSubstitutionFlags`.
/// <summary>ported from: src/compiler/transformers/ts.ts:211-217 — <c>TypeScriptSubstitutionFlags</c>.</summary>
/// <remarks>PORT-NOTE: the reference declares this const enum at the top of <c>transformers/ts.ts</c>; the port states it
/// beside the transform with the same members in the same order, and <see cref="FlagsAttribute"/> because the two
/// members are combined with <c>|</c>.</remarks>
[Flags]
internal enum TypeScriptSubstitutionFlags
{
    /// <summary><c>TypeScriptSubstitutionFlags.None</c>.</summary>
    None = 0,

    /// <summary><c>TypeScriptSubstitutionFlags.NamespaceExports</c>.</summary>
    NamespaceExports = 1 << 1,

    /// <summary><c>TypeScriptSubstitutionFlags.NonQualifiedEnumMembers</c>.</summary>
    NonQualifiedEnumMembers = 1 << 3,
}

// ported from: src/compiler/transformers/ts.ts:219-232 — the const enum `ClassFacts`.
/// <summary>ported from: src/compiler/transformers/ts.ts:219-232 — <c>ClassFacts</c>.</summary>
/// <remarks>PORT-NOTE: as <see cref="TypeScriptSubstitutionFlags"/>; the three composite members are stated by value so
/// the enum's own members carry the reference's names.</remarks>
[Flags]
internal enum ClassFacts
{
    /// <summary><c>ClassFacts.None</c>.</summary>
    None = 0,

    /// <summary><c>ClassFacts.HasStaticInitializedProperties</c>.</summary>
    HasStaticInitializedProperties = 1 << 0,

    /// <summary><c>ClassFacts.HasClassOrConstructorParameterDecorators</c>.</summary>
    HasClassOrConstructorParameterDecorators = 1 << 1,

    /// <summary><c>ClassFacts.HasMemberDecorators</c>.</summary>
    HasMemberDecorators = 1 << 2,

    /// <summary><c>ClassFacts.IsExportOfNamespace</c>.</summary>
    IsExportOfNamespace = 1 << 3,

    /// <summary><c>ClassFacts.IsNamedExternalExport</c>.</summary>
    IsNamedExternalExport = 1 << 4,

    /// <summary><c>ClassFacts.IsDefaultExternalExport</c>.</summary>
    IsDefaultExternalExport = 1 << 5,

    /// <summary><c>ClassFacts.IsDerivedClass</c>.</summary>
    IsDerivedClass = 1 << 6,

    /// <summary><c>ClassFacts.HasAnyDecorators</c>.</summary>
    HasAnyDecorators = HasClassOrConstructorParameterDecorators | HasMemberDecorators,

    /// <summary><c>ClassFacts.MayNeedImmediatelyInvokedFunctionExpression</c>.</summary>
    MayNeedImmediatelyInvokedFunctionExpression = HasAnyDecorators | HasStaticInitializedProperties,

    /// <summary><c>ClassFacts.IsExported</c>.</summary>
    IsExported = IsExportOfNamespace | IsDefaultExternalExport | IsNamedExternalExport,
}

/// <summary>ported from: src/compiler/transformers/ts.ts:235-2750 — the body of <c>transformTypeScript</c>.</summary>
/// <remarks>
/// PORT-NOTE (the state): the reference's <c>currentSourceFile</c>, <c>currentNamespace</c>,
/// <c>currentNamespaceContainerName</c>, <c>currentLexicalScope</c>, <c>currentScopeFirstDeclarationsOfName</c>,
/// <c>enabledSubstitutions</c> and <c>applicableSubstitutions</c> are the factory function's locals that the closures
/// mutate; they are this class's fields. The reference's <c>Map&lt;__String, Node&gt;</c> is the port's
/// <c>Dictionary&lt;string, Node&gt;</c> (<c>__String</c> is erased to <c>string</c> port-wide).
/// </remarks>
internal sealed partial class TypeScriptTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly ICompilerOptions _compilerOptions;
    private readonly ScriptTarget _languageVersion;
    private readonly ModuleKind _moduleKind;
    private readonly bool _legacyDecorators;

    /// <summary>ported from: src/compiler/transformers/ts.ts:246 — <c>typeSerializer</c>.</summary>
    /// <remarks>PORT-NOTE: <c>createRuntimeTypeSerializer</c> is <see cref="TypeSerializer.CreateRuntimeTypeSerializer"/>
    /// (that module's own slice of M7-B), and the reference's <c>undefined</c> arm is the port's <c>null</c>.</remarks>
    private readonly TypeSerializer.RuntimeTypeSerializer? _typeSerializer;

    private readonly Action<EmitHint, Node, Action<EmitHint, Node>> _previousOnEmitNode;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;

    // These variables contain state that changes as we descend into the tree.
    private SourceFile? _currentSourceFile;
    private ModuleDeclaration? _currentNamespace;
    private Identifier? _currentNamespaceContainerName;
    private Node? _currentLexicalScope;
    private Dictionary<string, Node>? _currentScopeFirstDeclarationsOfName;

    /// <summary>ported from: src/compiler/transformers/ts.ts:266-271 — <c>enabledSubstitutions</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's comment records that these two are persisted between each SourceFile
    /// transformation and are not reset; they are fields for that reason.</remarks>
    private TypeScriptSubstitutionFlags _enabledSubstitutions = TypeScriptSubstitutionFlags.None;

    /// <summary>ported from: src/compiler/transformers/ts.ts:273-277 — <c>applicableSubstitutions</c>.</summary>
    private TypeScriptSubstitutionFlags _applicableSubstitutions;

    /// <summary>ported from: src/compiler/transformers/ts.ts:235-282 — the factory's prologue and the returned
    /// <c>transformSourceFileOrBundle</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference destructures the context into <c>factory</c>, <c>getEmitHelperFactory</c> and the four
    /// lexical-environment members, then saves and installs the two emit hooks and enables the two property-access
    /// substitutions. The port keeps the context itself and reads the same members through it (their bodies are
    /// <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs), so the sequence is the same.
    /// </remarks>
    public TypeScriptTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _compilerOptions = context.GetCompilerOptions();
        _languageVersion = Utilities.GetEmitScriptTarget(_compilerOptions);
        _moduleKind = Utilities.GetEmitModuleKind(_compilerOptions);
        _legacyDecorators = Js.IsTruthy(_compilerOptions.ExperimentalDecorators);

        // ported from: src/compiler/transformers/ts.ts:246 — `const typeSerializer = compilerOptions.emitDecoratorMetadata ? createRuntimeTypeSerializer(context) : undefined;`
        _typeSerializer = Js.IsTruthy(_compilerOptions.EmitDecoratorMetadata)
            ? TypeSerializer.CreateRuntimeTypeSerializer(context)
            : null;

        // Save the previous transformation hooks.
        _previousOnEmitNode = context.OnEmitNode!;
        _previousOnSubstituteNode = context.OnSubstituteNode!;

        // Set new transformation hooks.
        context.OnEmitNode = OnEmitNode;
        context.OnSubstituteNode = OnSubstituteNode;

        // Enable substitution for property/element access to emit const enum values.
        context.EnableSubstitution(SyntaxKind.PropertyAccessExpression);
        context.EnableSubstitution(SyntaxKind.ElementAccessExpression);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:285-290 — <c>transformSourceFileOrBundle</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node)
        => NodeTests.IsBundle((Node)node!)
            ? TransformBundle((Bundle)node!)
            : TransformSourceFile((SourceFile)node!);

    /// <summary>ported from: src/compiler/transformers/ts.ts:292-298 — <c>transformBundle</c>.</summary>
    /// <remarks>PORT-NOTE: <c>node.sourceFiles.map(transformSourceFile)</c> is <see cref="Core.Map{T,TU}"/>, whose
    /// selector takes the index the reference's callback ignores.</remarks>
    private Bundle TransformBundle(Bundle node)
        => _factory.CreateBundle(Core.Map(node.SourceFiles, (sourceFile, _) => TransformSourceFile(sourceFile)));

    /// <summary>ported from: src/compiler/transformers/ts.ts:303-315 — <c>transformSourceFile</c>.</summary>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        _currentSourceFile = node;

        var visited = SaveStateAndInvoke(node, VisitSourceFile);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());

        _currentSourceFile = null;
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:322-338 — <c>saveStateAndInvoke</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>currentLexicalScope !== savedCurrentScope</c> is a node-identity test, so it is
    /// <see cref="object.ReferenceEquals(object?, object?)"/> here (the model's nodes are classes, not value types).
    /// </remarks>
    private T SaveStateAndInvoke<T, TNode>(TNode node, Func<TNode, T> f)
        where TNode : Node
    {
        // Save state
        var savedCurrentScope = _currentLexicalScope;
        var savedCurrentScopeFirstDeclarationsOfName = _currentScopeFirstDeclarationsOfName;

        // Handle state changes before visiting a node.
        OnBeforeVisitNode(node);

        var visited = f(node);

        // Restore state
        if (!ReferenceEquals(_currentLexicalScope, savedCurrentScope))
        {
            _currentScopeFirstDeclarationsOfName = savedCurrentScopeFirstDeclarationsOfName;
        }

        _currentLexicalScope = savedCurrentScope;
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:346-379 — <c>onBeforeVisitNode</c>.</summary>
    private void OnBeforeVisitNode(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.SourceFile:
            case SyntaxKind.CaseBlock:
            case SyntaxKind.ModuleBlock:
            case SyntaxKind.Block:
                _currentLexicalScope = node;
                _currentScopeFirstDeclarationsOfName = null;
                break;

            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.FunctionDeclaration:
                if (Utilities.HasSyntacticModifier(node, ModifierFlags.Ambient))
                {
                    break;
                }

                // Record these declarations provided that they have a name.
                if (((INamedDeclaration)node).Name is not null)
                {
                    RecordEmittedDeclarationInScope(node);
                }
                else
                {
                    // These nodes should always have names unless they are default-exports;
                    // however, class declaration parsing allows for undefined names, so syntactically invalid
                    // programs may also have an undefined name.
                    Debug.Assert(node.Kind == SyntaxKind.ClassDeclaration || Utilities.HasSyntacticModifier(node, ModifierFlags.Default));
                }

                break;
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:382-384 — <c>visitor</c>.</summary>
    private object? Visitor(Node node) => SaveStateAndInvoke(node, VisitorWorker);

    /// <summary>ported from: src/compiler/transformers/ts.ts:391-396 — <c>visitorWorker</c>.</summary>
    private object? VisitorWorker(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsTypeScript) != 0)
        {
            return VisitTypeScript(node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:403-405 — <c>sourceElementVisitor</c>.</summary>
    private object? SourceElementVisitor(Node node) => SaveStateAndInvoke(node, SourceElementVisitorWorker);

    /// <summary>ported from: src/compiler/transformers/ts.ts:412-424 — <c>sourceElementVisitorWorker</c>.</summary>
    private object? SourceElementVisitorWorker(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ImportDeclaration:
            case SyntaxKind.ImportEqualsDeclaration:
            case SyntaxKind.ExportAssignment:
            case SyntaxKind.ExportDeclaration:
                return VisitElidableStatement(node);
            default:
                return VisitorWorker(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:432-477 — <c>isElisionBlocked</c>.</summary>
    /// <remarks>PORT-NOTE: the four <c>Debug.assertNode(parsed, isX)</c> calls are the port's
    /// <see cref="Debug.AssertNode"/> — TS relies on them to narrow <c>parsed</c> to the declaration kind the switch
    /// arm reads, and C# cannot narrow through a predicate, so each arm casts the node it just asserted.</remarks>
    private bool IsElisionBlocked(Node node)
    {
        var parsed = Utilities.GetParseTreeNode(node);
        if (ReferenceEquals(parsed, node) || NodeTests.IsExportAssignment(node))
        {
            return false;
        }

        if (parsed is null || parsed.Kind != node.Kind)
        {
            // no longer safe to elide as the declaration was replaced with a node of a different kind
            return true;
        }

        switch (node.Kind)
        {
            case SyntaxKind.ImportDeclaration:
                Debug.AssertNode(parsed, NodeTests.IsImportDeclaration);
                if (!ReferenceEquals(((ImportDeclaration)node).ImportClause, ((ImportDeclaration)parsed).ImportClause))
                {
                    return true; // no longer safe to elide as the import clause has changed
                }
                if (!ReferenceEquals(((ImportDeclaration)node).Attributes, ((ImportDeclaration)parsed).Attributes))
                {
                    return true; // no longer safe to elide as the import attributes have changed
                }
                break;
            case SyntaxKind.ImportEqualsDeclaration:
                Debug.AssertNode(parsed, NodeTests.IsImportEqualsDeclaration);
                if (!ReferenceEquals(((ImportEqualsDeclaration)node).Name, ((ImportEqualsDeclaration)parsed).Name))
                {
                    return true; // no longer safe to elide as local binding has changed
                }
                if (((ImportEqualsDeclaration)node).IsTypeOnly != ((ImportEqualsDeclaration)parsed).IsTypeOnly)
                {
                    return true; // no longer safe to elide as `type` modifier has changed
                }
                if (!ReferenceEquals(((ImportEqualsDeclaration)node).ModuleReference, ((ImportEqualsDeclaration)parsed).ModuleReference)
                    && (Utilities.IsEntityName(((ImportEqualsDeclaration)node).ModuleReference!) || Utilities.IsEntityName(((ImportEqualsDeclaration)parsed).ModuleReference!)))
                {
                    return true; // no longer safe to elide as EntityName reference has changed.
                }
                break;
            case SyntaxKind.ExportDeclaration:
                Debug.AssertNode(parsed, NodeTests.IsExportDeclaration);
                if (!ReferenceEquals(((ExportDeclaration)node).ExportClause, ((ExportDeclaration)parsed).ExportClause))
                {
                    return true; // no longer safe to elide as the export clause has changed
                }
                if (!ReferenceEquals(((ExportDeclaration)node).Attributes, ((ExportDeclaration)parsed).Attributes))
                {
                    return true; // no longer safe to elide as the export attributes have changed
                }
                break;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:479-507 — <c>visitElidableStatement</c>.</summary>
    private object? VisitElidableStatement(Node node)
    {
        if (IsElisionBlocked(node))
        {
            // We do not reuse `visitorWorker`, as the ellidable statement syntax kinds are technically unrecognized by
            // the switch-case in `visitTypeScript`, and will trigger debug failures when debug verbosity is turned up.
            if ((node.TransformFlags & TransformFlags.ContainsTypeScript) != 0)
            {
                // This node contains TypeScript, so we should visit its children.
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
            }
            // Otherwise, we can just return the node
            return node;
        }
        switch (node.Kind)
        {
            case SyntaxKind.ImportDeclaration:
                return VisitImportDeclaration((ImportDeclaration)node);
            case SyntaxKind.ImportEqualsDeclaration:
                return VisitImportEqualsDeclaration((ImportEqualsDeclaration)node);
            case SyntaxKind.ExportAssignment:
                return VisitExportAssignment((ExportAssignment)node);
            case SyntaxKind.ExportDeclaration:
                return VisitExportDeclaration((ExportDeclaration)node);
            default:
                return Debug.Fail<object?>("Unhandled ellided statement");
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:509-511 — <c>namespaceElementVisitor</c>.</summary>
    private object? NamespaceElementVisitor(Node node) => SaveStateAndInvoke(node, NamespaceElementVisitorWorker);

    /// <summary>ported from: src/compiler/transformers/ts.ts:518-535 — <c>namespaceElementVisitorWorker</c>.</summary>
    private object? NamespaceElementVisitorWorker(Node node)
    {
        if (
            node.Kind == SyntaxKind.ExportDeclaration
            || node.Kind == SyntaxKind.ImportDeclaration
            || node.Kind == SyntaxKind.ImportClause
            || (node.Kind == SyntaxKind.ImportEqualsDeclaration
                && ((ImportEqualsDeclaration)node).ModuleReference!.Kind == SyntaxKind.ExternalModuleReference)
        )
        {
            // do not emit ES6 imports and exports since they are illegal inside a namespace
            return null;
        }
        else if ((node.TransformFlags & TransformFlags.ContainsTypeScript) != 0 || Utilities.HasSyntacticModifier(node, ModifierFlags.Export))
        {
            return VisitTypeScript(node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:541-543 — <c>getClassElementVisitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS answers <c>(node: Node) =&gt; VisitResult&lt;Node | undefined&gt;</c>, the port's
    /// <c>Func&lt;Node, object?&gt;</c>; the closure captures <c>parent</c> exactly as the reference's does.</remarks>
    private Func<Node, object?> GetClassElementVisitor(IClassLikeDeclarationBase parent)
        => node => SaveStateAndInvoke(node, n => ClassElementVisitorWorker(n, parent));

    /// <summary>ported from: src/compiler/transformers/ts.ts:550-585 — <c>classElementVisitorWorker</c>.</summary>
    private object? ClassElementVisitorWorker(Node node, IClassLikeDeclarationBase parent)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Constructor:
                return VisitConstructor((ConstructorDeclaration)node);

            case SyntaxKind.PropertyDeclaration:
                // Property declarations are not TypeScript syntax, but they must be visited
                // for the decorator transformation.
                return VisitPropertyDeclaration((PropertyDeclaration)node, (Node)parent);

            case SyntaxKind.GetAccessor:
                // Get Accessors can have TypeScript modifiers, decorators, and type annotations.
                return VisitGetAccessor((GetAccessorDeclaration)node, (Node)parent);

            case SyntaxKind.SetAccessor:
                // Set Accessors can have TypeScript modifiers and type annotations.
                return VisitSetAccessor((SetAccessorDeclaration)node, (Node)parent);

            case SyntaxKind.MethodDeclaration:
                // TypeScript method declarations may have decorators, modifiers
                // or type annotations.
                return VisitMethodDeclaration((MethodDeclaration)node, (Node)parent);

            case SyntaxKind.ClassStaticBlockDeclaration:
                return Utilities.VisitEachChildWorker(node, Visitor, _context);

            case SyntaxKind.SemicolonClassElement:
                return node;

            case SyntaxKind.IndexSignature:
                // Index signatures are elided
                return null;

            default:
                return FailBadSyntaxKind(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:588-590 — <c>getObjectLiteralElementVisitor</c>.</summary>
    /// <remarks>PORT-NOTE: TS answers the generic function <c>&lt;T extends Node&gt;(node: T) =&gt;
    /// VisitResult&lt;Node | undefined&gt;</c>; the port's erased visitor face is <c>Func&lt;Node, object?&gt;</c>, which
    /// is what its only reader (the <c>visitNodes</c> call in <see cref="VisitObjectLiteralExpression"/>) passes.</remarks>
    private Func<Node, object?> GetObjectLiteralElementVisitor(ObjectLiteralExpression parent)
        => node => SaveStateAndInvoke(node, n => ObjectLiteralElementVisitorWorker(n, parent));

    /// <summary>ported from: src/compiler/transformers/ts.ts:592-614 — <c>objectLiteralElementVisitorWorker</c>.</summary>
    private object? ObjectLiteralElementVisitorWorker(Node node, ObjectLiteralExpression parent)
    {
        switch (node.Kind)
        {
            case SyntaxKind.PropertyAssignment:
            case SyntaxKind.ShorthandPropertyAssignment:
            case SyntaxKind.SpreadAssignment:
                return Visitor(node);

            case SyntaxKind.GetAccessor:
                // Get Accessors can have TypeScript modifiers, decorators, and type annotations.
                return VisitGetAccessor((GetAccessorDeclaration)node, parent);

            case SyntaxKind.SetAccessor:
                // Set Accessors can have TypeScript modifiers, decorators, and type annotations.
                return VisitSetAccessor((SetAccessorDeclaration)node, parent);

            case SyntaxKind.MethodDeclaration:
                // TypeScript method declarations may have decorators, modifiers
                // or type annotations.
                return VisitMethodDeclaration((MethodDeclaration)node, parent);

            default:
                return FailBadSyntaxKind(node);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:617-619 — <c>decoratorElidingVisitor</c>.</summary>
    private object? DecoratorElidingVisitor(Node node)
        => NodeTests.IsDecorator(node) ? null : Visitor(node);

    /// <summary>ported from: src/compiler/transformers/ts.ts:621-623 — <c>modifierElidingVisitor</c>.</summary>
    private object? ModifierElidingVisitor(Node node)
        => Utilities.IsModifier(node) ? null : Visitor(node);

    /// <summary>ported from: src/compiler/transformers/ts.ts:625-636 — <c>modifierVisitor</c>.</summary>
    private object? ModifierVisitor(Node node)
    {
        if (NodeTests.IsDecorator(node)) return null;
        if ((Utilities.ModifierToFlag(node.Kind) & ModifierFlags.TypeScriptModifier) != 0)
        {
            return null;
        }
        else if (_currentNamespace is not null && node.Kind == SyntaxKind.ExportKeyword)
        {
            return null;
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:642-833 — <c>visitTypeScript</c>.</summary>
    /// <remarks>PORT-NOTE: TS's switch falls through from the modifier group to the type-node group to
    /// <c>IndexSignature</c>; C# accepts the same empty case labels stacked on one block, so the group is stated once.
    /// The <c>Debug.fail</c>/<c>Debug.failBadSyntaxKind</c> arms are <see cref="Debug.Fail{T}"/>/
    /// <see cref="FailBadSyntaxKind"/>, the port's value-producing forms of TS's <c>never</c>-returning helpers.</remarks>
    private object? VisitTypeScript(Node node)
    {
        if (Utilities.IsStatement(node) && Utilities.HasSyntacticModifier(node, ModifierFlags.Ambient))
        {
            // TypeScript ambient declarations are elided, but some comments may be preserved.
            // See the implementation of `getLeadingComments` in comments.ts for more details.
            return _factory.CreateNotEmittedStatement(node);
        }

        switch (node.Kind)
        {
            case SyntaxKind.ExportKeyword:
            case SyntaxKind.DefaultKeyword:
                // ES6 export and default modifiers are elided when inside a namespace.
                return _currentNamespace is not null ? null : node;

            case SyntaxKind.PublicKeyword:
            case SyntaxKind.PrivateKeyword:
            case SyntaxKind.ProtectedKeyword:
            case SyntaxKind.AbstractKeyword:
            case SyntaxKind.OverrideKeyword:
            case SyntaxKind.ConstKeyword:
            case SyntaxKind.DeclareKeyword:
            case SyntaxKind.ReadonlyKeyword:
            case SyntaxKind.InKeyword:
            case SyntaxKind.OutKeyword:
            // TypeScript accessibility and readonly modifiers are elided
            // falls through
            case SyntaxKind.ArrayType:
            case SyntaxKind.TupleType:
            case SyntaxKind.OptionalType:
            case SyntaxKind.RestType:
            case SyntaxKind.TypeLiteral:
            case SyntaxKind.TypePredicate:
            case SyntaxKind.TypeParameter:
            case SyntaxKind.AnyKeyword:
            case SyntaxKind.UnknownKeyword:
            case SyntaxKind.BooleanKeyword:
            case SyntaxKind.StringKeyword:
            case SyntaxKind.NumberKeyword:
            case SyntaxKind.NeverKeyword:
            case SyntaxKind.VoidKeyword:
            case SyntaxKind.SymbolKeyword:
            case SyntaxKind.ConstructorType:
            case SyntaxKind.FunctionType:
            case SyntaxKind.TypeQuery:
            case SyntaxKind.TypeReference:
            case SyntaxKind.UnionType:
            case SyntaxKind.IntersectionType:
            case SyntaxKind.ConditionalType:
            case SyntaxKind.ParenthesizedType:
            case SyntaxKind.ThisType:
            case SyntaxKind.TypeOperator:
            case SyntaxKind.IndexedAccessType:
            case SyntaxKind.MappedType:
            case SyntaxKind.LiteralType:
                // TypeScript type nodes are elided.
                // falls through
            case SyntaxKind.IndexSignature:
                // TypeScript index signatures are elided.
                return null;

            case SyntaxKind.TypeAliasDeclaration:
                // TypeScript type-only declarations are elided.
                return _factory.CreateNotEmittedStatement(node);

            case SyntaxKind.NamespaceExportDeclaration:
                // TypeScript namespace export declarations are elided.
                return null;

            case SyntaxKind.InterfaceDeclaration:
                // TypeScript interfaces are elided, but some comments may be preserved.
                // See the implementation of `getLeadingComments` in comments.ts for more details.
                return _factory.CreateNotEmittedStatement(node);

            case SyntaxKind.ClassDeclaration:
                // This may be a class declaration with TypeScript syntax extensions.
                //
                // TypeScript class syntax extensions include:
                // - decorators
                // - optional `implements` heritage clause
                // - parameter property assignments in the constructor
                // - index signatures
                // - method overload signatures
                return VisitClassDeclaration((ClassDeclaration)node);

            case SyntaxKind.ClassExpression:
                // This may be a class expression with TypeScript syntax extensions.
                //
                // TypeScript class syntax extensions include:
                // - decorators
                // - optional `implements` heritage clause
                // - parameter property assignments in the constructor
                // - index signatures
                // - method overload signatures
                return VisitClassExpression((ClassExpression)node);

            case SyntaxKind.HeritageClause:
                // This may be a heritage clause with TypeScript syntax extensions.
                //
                // TypeScript heritage clause extensions include:
                // - `implements` clause
                return VisitHeritageClause((HeritageClause)node);

            case SyntaxKind.ExpressionWithTypeArguments:
                // TypeScript supports type arguments on an expression in an `extends` heritage clause.
                return VisitExpressionWithTypeArguments((ExpressionWithTypeArguments)node);

            case SyntaxKind.ObjectLiteralExpression:
                return VisitObjectLiteralExpression((ObjectLiteralExpression)node);

            case SyntaxKind.Constructor:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return Debug.Fail<object?>("Class and object literal elements must be visited with their respective visitors");

            case SyntaxKind.FunctionDeclaration:
                // Typescript function declarations can have modifiers, decorators, and type annotations.
                return VisitFunctionDeclaration((FunctionDeclaration)node);

            case SyntaxKind.FunctionExpression:
                // TypeScript function expressions can have modifiers and type annotations.
                return VisitFunctionExpression((FunctionExpression)node);

            case SyntaxKind.ArrowFunction:
                // TypeScript arrow functions can have modifiers and type annotations.
                return VisitArrowFunction((ArrowFunction)node);

            case SyntaxKind.Parameter:
                // This may be a parameter declaration with TypeScript syntax extensions.
                //
                // TypeScript parameter declaration syntax extensions include:
                // - decorators
                // - accessibility modifiers
                // - the question mark (?) token for optional parameters
                // - type annotations
                // - this parameters
                return VisitParameter((ParameterDeclaration)node);

            case SyntaxKind.ParenthesizedExpression:
                // ParenthesizedExpressions are TypeScript if their expression is a
                // TypeAssertion or AsExpression
                return VisitParenthesizedExpression((ParenthesizedExpression)node);

            case SyntaxKind.TypeAssertionExpression:
            case SyntaxKind.AsExpression:
                // TypeScript type assertions are removed, but their subtrees are preserved.
                return VisitAssertionExpression(node);

            case SyntaxKind.SatisfiesExpression:
                return VisitSatisfiesExpression((SatisfiesExpression)node);

            case SyntaxKind.CallExpression:
                return VisitCallExpression((CallExpression)node);

            case SyntaxKind.NewExpression:
                return VisitNewExpression((NewExpression)node);

            case SyntaxKind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression((TaggedTemplateExpression)node);

            case SyntaxKind.NonNullExpression:
                // TypeScript non-null expressions are removed, but their subtrees are preserved.
                return VisitNonNullExpression((NonNullExpression)node);

            case SyntaxKind.EnumDeclaration:
                // TypeScript enum declarations do not exist in ES6 and must be rewritten.
                return VisitEnumDeclaration((EnumDeclaration)node);

            case SyntaxKind.VariableStatement:
                // TypeScript namespace exports for variable statements must be transformed.
                return VisitVariableStatement((VariableStatement)node);

            case SyntaxKind.VariableDeclaration:
                return VisitVariableDeclaration((VariableDeclaration)node);

            case SyntaxKind.ModuleDeclaration:
                // TypeScript namespace declarations must be transformed.
                return VisitModuleDeclaration((ModuleDeclaration)node);

            case SyntaxKind.ImportEqualsDeclaration:
                // TypeScript namespace or external module import.
                return VisitImportEqualsDeclaration((ImportEqualsDeclaration)node);

            case SyntaxKind.JsxSelfClosingElement:
                return VisitJsxSelfClosingElement((JsxSelfClosingElement)node);

            case SyntaxKind.JsxOpeningElement:
                return VisitJsxJsxOpeningElement((JsxOpeningElement)node);

            default:
                // node contains some other TypeScript syntax
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:838-847 — <c>visitSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: the reference passes two arguments to <c>updateSourceFile</c>; the port states the other five
    /// explicitly as <c>null</c>, which is the port's spelling of the reference's defaulted parameters (and matches the
    /// defaults themselves: <c>isDeclarationFile = node.isDeclarationFile</c>, the three reference lists from the node,
    /// and <c>_hasNoDefaultLib = false</c>).</remarks>
    private SourceFile VisitSourceFile(SourceFile node)
    {
        var alwaysStrict = Utilities.GetAlwaysStrict(_compilerOptions)
            && !(Parser.IsExternalModule(node) && _moduleKind >= ModuleKind.ES2015)
            && !Utilities.IsJsonSourceFile(node);

        return _factory.UpdateSourceFile(
            node,
            Utilities.VisitLexicalEnvironment(node.Statements!, SourceElementVisitor, _context, /*start*/ 0, alwaysStrict),
            /*isDeclarationFile*/ null,
            /*referencedFiles*/ null,
            /*typeReferenceDirectives*/ null,
            /*_hasNoDefaultLib*/ null,
            /*libReferenceDirectives*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/ts.ts:849-854 — <c>visitObjectLiteralExpression</c>.</summary>
    private ObjectLiteralExpression VisitObjectLiteralExpression(ObjectLiteralExpression node)
        => _factory.UpdateObjectLiteralExpression(
            node,
            Utilities.VisitNodes(node.Properties, GetObjectLiteralElementVisitor(node), Utilities.IsObjectLiteralElementLike));

    /// <summary>The value-producing form of <c>Debug.failBadSyntaxKind</c>.</summary>
    /// <remarks>PORT-NOTE: <c>debug.ts</c>'s helper is <c>void</c> with <c>[DoesNotReturn]</c> in the port
    /// (Core/Debug.Model.cs:42), so the two switch defaults that must answer a value use <see cref="Debug.Fail{T}"/>
    /// with the same message the helper builds — the shape Checker.Check.Symbols.cs records for the same situation.</remarks>
    private static object? FailBadSyntaxKind(Node node)
        => Debug.Fail<object?>($"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(node.Kind)} was unexpected.");
}
