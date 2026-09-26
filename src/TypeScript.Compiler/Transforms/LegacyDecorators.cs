#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/legacyDecorators.ts:1-846 — the whole legacyDecorators module.
/// <summary>
/// ported from: src/compiler/transformers/legacyDecorators.ts:88-846 — <c>transformLegacyDecorators</c>: the transformer
/// that applies the legacy (TypeScript-style) decorators of a <see cref="SourceFile"/> at runtime — it elides the
/// decorator syntax from the tree and appends the <c>__decorate</c>/<c>__param</c> calls that re-apply it, hoisting a
/// class alias for a decorated class that refers to itself inside its own body.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports one function, <c>transformLegacyDecorators</c>, from
/// <c>transformers/legacyDecorators.ts</c>; its body is a table of closures over the factory's locals, so the port states
/// the factory as <see cref="TransformLegacyDecorators"/> and the closures as the members of
/// <see cref="LegacyDecoratorTransformer"/> — the same "closures → classes" treatment <c>TypeScriptTransform</c> records
/// for <c>ts.ts</c>.
/// </para>
/// <para>
/// PORT-NOTE (the transformer face): TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is
/// <c>(node: SourceFile | Bundle) =&gt; SourceFile | Bundle</c>; the port's transformer list spells its slots
/// <c>Func&lt;object?, object?&gt;</c> (Transformer.cs's header records why), and the returned member is the reference's
/// own <c>chainBundle(context, transformSourceFile)</c> — <see cref="TransformationUtilities.ChainBundle"/> is that
/// call's port, whose face is <c>Func&lt;Node, Node&gt;</c> and whose two arms are the <c>kind</c> test.
/// </para>
/// <para>
/// PORT-NOTE (the visitor slots): TS's <c>visitor</c>/<c>nodesVisitor</c> are the <c>Visitor</c> function type, which the
/// port spells <c>Func&lt;Node, object?&gt;</c> (Utilities/Utilities.Visitor.cs), and <c>VisitResult&lt;T&gt;</c> is
/// <c>T | readonly Node[] | undefined</c>, which the port discriminates the same way
/// <c>NodeFactory.CopyCustomPrologue</c> does (<c>null</c>, then a node list, then a node).
/// </para>
/// <para>
/// PORT-NOTE (the hook): the reference saves only <c>context.onSubstituteNode</c> (the substitution that rewrites a
/// constructor reference to its class alias) — it installs no emit-notification hook; the port's constructor does the
/// same, unlike <see cref="TypeScriptTransformer"/>/<see cref="ES2015Transformer"/>, which install both.
/// </para>
/// <para>
/// PORT-NOTE (the unions): TS's <c>ClassLikeDeclaration</c> (<c>ClassExpression | ClassDeclaration</c>) and its
/// <c>ClassElement</c>/<c>EnumMember</c> unions have no C# spelling, so the parameters the reference types with them take
/// the model's <see cref="Node"/> — the common base — and the members that read a slot the two arms share cast at the
/// boundary (<c>IClassLikeDeclarationBase</c>, <c>INamedDeclaration</c>), the treatment
/// <c>TransformationUtilities.GetAllDecoratorsOfClass</c> records.
/// </para>
/// <para>
/// PORT-NOTE (<c>factory.getInternalName</c>/<c>getLocalName</c>/<c>getDeclarationName</c>): the <c>getName</c> family
/// lives on <see cref="TypeScriptTransformSupport"/> (the M5 residue that file records), so those calls carry the factory
/// as their first argument. Likewise <c>moveRangePastModifiers</c> is
/// <see cref="TypeScriptTransformSupport.MoveRangePastModifiers"/> and every range it produces reaches
/// <c>setSourceMapRange</c> through <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/>, because the reference's
/// structural conversion from a node to a <c>SourceMapRange</c> has no port equivalent (docs/architecture.md §15.5).
/// </para>
/// </remarks>
internal static class LegacyDecorators
{
    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:88-846 — <c>transformLegacyDecorators</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
    /// <c>Func&lt;object?, object?&gt;</c>; the returned member is
    /// <see cref="LegacyDecoratorTransformer.TransformSourceFileOrBundle"/>, staying a method group so no delegate is
    /// allocated per call beyond the one the caller stores.</remarks>
    public static Func<object?, object?> TransformLegacyDecorators(ITransformerContext context)
        => new LegacyDecoratorTransformer(context).TransformSourceFileOrBundle;
}

// ported from: src/compiler/transformers/legacyDecorators.ts:88-846 — the body of `transformLegacyDecorators`.
/// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:88-846 — the body of
/// <c>transformLegacyDecorators</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the state): the reference's <c>classAliases</c> is the factory function's local the closures mutate (it is
/// enabled lazily by <c>enableSubstitutionForClassAliases</c>); it is this class's field. TS types it <c>Identifier[]</c>
/// and indexes it with a node id, which the port states as <c>Dictionary&lt;int, Identifier&gt;</c> (§12.4's <c>Map</c>→<c>Dictionary</c>
/// ruling applied to an id-indexed table).
/// </para>
/// <para>
/// PORT-NOTE (the returned pair of <c>transformDecoratorsOfClassElements</c>): the reference answers the object literal
/// <c>{ decorationStatements, members }</c>; the port states the same two slots as a tuple, which is what the caller
/// destructures — the treatment <c>TransformationUtilities.GetAllAccessorDeclarations</c> records for a reference object
/// literal whose only readers destructure it.
/// </para>
/// </remarks>
internal sealed partial class LegacyDecoratorTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHelperFactory _emitHelpers;
    private readonly IEmitResolver _resolver;
    private readonly ScriptTarget _languageVersion;
    private readonly Func<EmitHint, Node, Node> _previousOnSubstituteNode;

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:110 —
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    private readonly Func<Node, Node> _transformSourceFile;

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:109 — <c>classAliases</c>: a map that keeps track
    /// of aliases created for classes with decorators to avoid issues with the double-binding behavior of classes.</summary>
    /// <remarks>PORT-NOTE: the reference declares it <c>let classAliases: Identifier[]</c> and leaves it unassigned until
    /// <c>enableSubstitutionForClassAliases</c> runs, so "enabled" is "not undefined" here too — the port's <c>null</c> is
    /// that <c>undefined</c>, and <c>classAliases[key]</c>'s <c>undefined</c> is a failed <c>TryGetValue</c>.</remarks>
    private Dictionary<int, Identifier>? _classAliases;

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:90-112 — the factory's prologue.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference destructures <c>factory</c>, <c>getEmitHelperFactory</c> and <c>hoistVariableDeclaration</c>
    /// out of the context and reads <c>compilerOptions</c> for the emit target; the port keeps the context itself and reads
    /// the same members through it (their bodies are <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs), so the
    /// sequence is the same.
    /// </remarks>
    public LegacyDecoratorTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _emitHelpers = context.GetEmitHelperFactory();
        _resolver = context.GetEmitResolver();
        _languageVersion = Utilities.GetEmitScriptTarget(context.GetCompilerOptions());
        _transformSourceFile = TransformationUtilities.ChainBundle(context, TransformSourceFile);

        // Save the previous transformation hooks.
        _previousOnSubstituteNode = context.OnSubstituteNode!;

        // Set new transformation hooks.
        context.OnSubstituteNode = OnSubstituteNode;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:88-110 — the returned
    /// <c>chainBundle(context, transformSourceFile)</c>.</summary>
    public object? TransformSourceFileOrBundle(object? node) => _transformSourceFile((Node)node!);

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:113-117 — <c>transformSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: <c>visitEachChild</c> is <see cref="Utilities.VisitEachChildWorker{TNode}"/> — the alias the
    /// checker's own call sites are written against (Utilities/Utilities.Visitor.cs's header records the ruling).</remarks>
    private SourceFile TransformSourceFile(SourceFile node)
    {
        var visited = Utilities.VisitEachChildWorker(node, Visitor, _context);
        EmitNodes.AddEmitHelpers(visited, _context.ReadEmitHelpers());
        return visited;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:119-121 — <c>modifierVisitor</c>.</summary>
    private static object? ModifierVisitor(Node node) => NodeTests.IsDecorator(node) ? null : node;

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:123-151 — <c>visitor</c>.</summary>
    /// <remarks>PORT-NOTE: <c>node.transformFlags &amp; TransformFlags.ContainsDecorators</c> is a bit test the port states
    /// with <c>!= 0</c>; the reference's <c>undefined</c> arm (an elided decorator) is the port's <c>null</c> and its
    /// <c>visitEachChild(node, visitor, context)</c> fallback is <see cref="Utilities.VisitEachChildWorker{TNode}"/>.</remarks>
    private object? Visitor(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsDecorators) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case SyntaxKind.Decorator:
                // Decorators are elided. They will be emitted as part of `visitClassDeclaration`.
                return null;
            case SyntaxKind.ClassDeclaration:
                return VisitClassDeclaration((ClassDeclaration)node);
            case SyntaxKind.ClassExpression:
                return VisitClassExpression((ClassExpression)node);
            case SyntaxKind.Constructor:
                return VisitConstructorDeclaration((ConstructorDeclaration)node);
            case SyntaxKind.MethodDeclaration:
                return VisitMethodDeclaration((MethodDeclaration)node);
            case SyntaxKind.SetAccessor:
                return VisitSetAccessorDeclaration((SetAccessorDeclaration)node);
            case SyntaxKind.GetAccessor:
                return VisitGetAccessorDeclaration((GetAccessorDeclaration)node);
            case SyntaxKind.PropertyDeclaration:
                return VisitPropertyDeclaration((PropertyDeclaration)node);
            case SyntaxKind.Parameter:
                return VisitParameterDeclaration((ParameterDeclaration)node);
            default:
                return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:153-163 — <c>visitClassDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>visitNodes</c> and <c>childIsDecorated</c> are <see cref="Utilities.VisitNodes{TNode}"/>/<see
    /// cref="Utilities.ChildIsDecorated"/>; TS's <c>singleOrMany(statements)</c> is the reference's own expression here (one
    /// statement → the statement, otherwise the array), which the visitor slot's <c>object?</c> carries.</remarks>
    private object? VisitClassDeclaration(ClassDeclaration node)
    {
        if (!(Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ true, node)
            || Utilities.ChildIsDecorated(/*useLegacyDecorators*/ true, node)))
        {
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }

        var statements = Utilities.ClassOrConstructorParameterIsDecorated(/*useLegacyDecorators*/ true, node)
            ? TransformClassDeclarationWithClassDecorators(node, node.Name)
            : TransformClassDeclarationWithoutClassDecorators(node, node.Name);

        return statements.Count == 1 ? (object)statements[0] : statements;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:165-167 —
    /// <c>decoratorContainsPrivateIdentifierInExpression</c>.</summary>
    private static bool DecoratorContainsPrivateIdentifierInExpression(Decorator decorator)
        => (decorator.TransformFlags & TransformFlags.ContainsPrivateIdentifierInExpression) != 0;

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:169-171 —
    /// <c>parameterDecoratorsContainPrivateIdentifierInExpression</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>readonly Decorator[] | undefined</c>; <c>AllDecorators.parameters</c> is the
    /// model's <c>List&lt;List&lt;Decorator&gt;&gt;</c> and the hole a parameter without decorators leaves there is the
    /// <c>?</c> (TransformationUtilities.GetAllDecoratorsOfClass records the cast).</remarks>
    private static bool ParameterDecoratorsContainPrivateIdentifierInExpression(List<Decorator>? parameterDecorators)
        => Core.Some(parameterDecorators, DecoratorContainsPrivateIdentifierInExpression);

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:173-181 —
    /// <c>hasClassElementWithDecoratorContainingPrivateIdentifierInExpression</c>.</summary>
    /// <remarks>PORT-NOTE: <c>canHaveDecorators</c> is <see cref="Utilities.CanHaveDecorators"/> (which answers <c>bool</c>
    /// rather than narrowing, so the member is read through the cast the loop already holds) and
    /// <c>getAllDecoratorsOfClassElement</c> is <see cref="TransformationUtilities.GetAllDecoratorsOfClassElement"/>.</remarks>
    private bool HasClassElementWithDecoratorContainingPrivateIdentifierInExpression(ClassDeclaration node)
    {
        foreach (var member in node.Members!)
        {
            if (!Utilities.CanHaveDecorators((Node)member)) continue;
            var allDecorators = TransformationUtilities.GetAllDecoratorsOfClassElement(member, node, /*useLegacyDecorators*/ true);
            if (Core.Some(allDecorators?.Decorators, DecoratorContainsPrivateIdentifierInExpression)) return true;
            if (Core.Some(allDecorators?.Parameters, p => ParameterDecoratorsContainPrivateIdentifierInExpression(p))) return true;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:183-200 —
    /// <c>transformDecoratorsOfClassElements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>members: NodeArray&lt;ClassElement&gt;</c> is the model's
    /// <c>NodeArray&lt;IClassElement&gt;</c> and its answer <c>{ decorationStatements, members }</c> is the tuple below (this
    /// class's header records the ruling); the second arm's
    /// <c>setTextRange(factory.createNodeArray([...members, staticBlock]), members)</c> is
    /// <see cref="Utilities.SetTextRange{T}"/> over a list the port builds with <c>Add</c>/<c>AddRange</c>.
    /// </remarks>
    private (List<IStatement>? DecorationStatements, NodeArray<IClassElement>? Members) TransformDecoratorsOfClassElements(
        ClassDeclaration node,
        NodeArray<IClassElement>? members)
    {
        var decorationStatements = new List<IStatement>();
        AddClassElementDecorationStatements(decorationStatements, node, /*isStatic*/ false);
        AddClassElementDecorationStatements(decorationStatements, node, /*isStatic*/ true);
        if (HasClassElementWithDecoratorContainingPrivateIdentifierInExpression(node))
        {
            var extendedMembers = new List<IClassElement>(members!);
            extendedMembers.Add(_factory.CreateClassStaticBlockDeclaration(
                _factory.CreateBlock(decorationStatements, /*multiLine*/ true)));
            members = Utilities.SetTextRange(
                _factory.CreateNodeArray(extendedMembers, /*hasTrailingComma*/ null),
                members!);
            decorationStatements = null;
        }
        return (decorationStatements, members);
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:208-230 —
    /// <c>transformClassDeclarationWithoutClassDecorators</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's trailing <c>addRange([updated], decorationStatements)</c> appends in place and
    /// answers the same array, which the port states as
    /// <c>(List&lt;IStatement&gt;)Core.AddRange(new List&lt;IStatement&gt; { updated }, decorationStatements)!</c> — the cast
    /// <c>ES2015Transform.Loops.cs</c> already records for that helper's answer.
    /// <para>
    /// Transforms a non-decorated class declaration.
    /// </para>
    /// </remarks>
    private List<IStatement> TransformClassDeclarationWithoutClassDecorators(ClassDeclaration node, Identifier? name)
    {
        //  ${modifiers} class ${name} ${heritageClauses} {
        //      ${members}
        //  }

        var modifiers = Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier);
        var heritageClauses = Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause);
        var members = Utilities.VisitNodes(node.Members, Visitor, Utilities.IsClassElement);

        var (decorationStatements, decoratedMembers) = TransformDecoratorsOfClassElements(node, members);
        members = decoratedMembers;

        var updated = _factory.UpdateClassDeclaration(
            node,
            modifiers,
            name,
            /*typeParameters*/ null,
            heritageClauses,
            members);

        return (List<IStatement>)Core.AddRange(new List<IStatement> { updated }, decorationStatements)!;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:236-407 —
    /// <c>transformClassDeclarationWithClassDecorators</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>hasSyntacticModifier</c>, <c>isExportOrDefaultModifier</c>, <c>isModifierLike</c>,
    /// <c>isClassStaticBlockDeclaration</c> and <c>isGeneratedIdentifier</c> are <see cref="Utilities.HasSyntacticModifier"/>,
    /// <see cref="FactoryUtilities.IsExportOrDefaultModifier"/>, <see cref="Utilities.IsModifierLike"/>,
    /// <see cref="NodeTests.IsClassStaticBlockDeclaration"/> and <see cref="Utilities.IsGeneratedIdentifier"/>; the
    /// reference's defaulted argument lists (<c>createBlock([...])</c> and the four-argument <c>createDecorateHelper</c>) are
    /// stated with <c>null</c>.
    /// </para>
    /// <para>
    /// Transforms a decorated class declaration and appends the resulting statements. If the class requires an alias to avoid
    /// issues with double-binding, the alias is returned.
    /// </para>
    /// </remarks>
    private List<IStatement> TransformClassDeclarationWithClassDecorators(ClassDeclaration node, Identifier? name)
    {
        // When we emit an ES6 class that has a class decorator, we must tailor the
        // emit to certain specific cases.
        //
        // In the simplest case, we emit the class declaration as a let declaration, and
        // evaluate decorators after the close of the class body:
        //
        //  [Example 1]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  class C {                       | }
        //  }                               | C = __decorate([dec], C);
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export class C {                | }
        //  }                               | C = __decorate([dec], C);
        //                                  | export { C };
        //  ---------------------------------------------------------------------
        //
        // If a class declaration contains a reference to itself *inside* of the class body,
        // this introduces two bindings to the class: One outside of the class body, and one
        // inside of the class body. If we apply decorators as in [Example 1] above, there
        // is the possibility that the decorator `dec` will return a new value for the
        // constructor, which would result in the binding inside of the class no longer
        // pointing to the same reference as the binding outside of the class.
        //
        // As a result, we must instead rewrite all references to the class *inside* of the
        // class body to instead point to a local temporary alias for the class:
        //
        //  [Example 2]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = C_1 = class C {
        //  class C {                       |   static x() { return C_1.y; }
        //    static x() { return C.y; }    | }
        //    static y = 1;                 | C.y = 1;
        //  }                               | C = C_1 = __decorate([dec], C);
        //                                  | var C_1;
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export class C {                |   static x() { return C_1.y; }
        //    static x() { return C.y; }    | }
        //    static y = 1;                 | C.y = 1;
        //  }                               | C = C_1 = __decorate([dec], C);
        //                                  | export { C };
        //                                  | var C_1;
        //  ---------------------------------------------------------------------
        //
        // If a class declaration is the default export of a module, we instead emit
        // the export after the decorated declaration:
        //
        //  [Example 3]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let default_1 = class {
        //  export default class {          | }
        //  }                               | default_1 = __decorate([dec], default_1);
        //                                  | export default default_1;
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export default class C {        | }
        //  }                               | C = __decorate([dec], C);
        //                                  | export default C;
        //  ---------------------------------------------------------------------
        //
        // If the class declaration is the default export and a reference to itself
        // inside of the class body, we must emit both an alias for the class *and*
        // move the export after the declaration:
        //
        //  [Example 4]
        //  ---------------------------------------------------------------------
        //  TypeScript                      | Javascript
        //  ---------------------------------------------------------------------
        //  @dec                            | let C = class C {
        //  export default class C {        |   static x() { return C_1.y; }
        //    static x() { return C.y; }    | }
        //    static y = 1;                 | C.y = 1;
        //  }                               | C = C_1 = __decorate([dec], C);
        //                                  | export default C;
        //                                  | var C_1;
        //  ---------------------------------------------------------------------
        //

        var isExport = Utilities.HasSyntacticModifier(node, ModifierFlags.Export);
        var isDefault = Utilities.HasSyntacticModifier(node, ModifierFlags.Default);
        var modifiers = Utilities.VisitNodes(
            node.Modifiers,
            n => FactoryUtilities.IsExportOrDefaultModifier(n) || NodeTests.IsDecorator(n) ? null : n,
            Utilities.IsModifierLike);

        var location = TypeScriptTransformSupport.MoveRangePastModifiers(node);
        var classAlias = GetClassAliasIfNeeded(node);

        // When we transform to ES5/3 this will be moved inside an IIFE and should reference the name
        // without any block-scoped variable collision handling
        var declName = _languageVersion < ScriptTarget.ES2015
            ? TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true)
            : TypeScriptTransformSupport.GetLocalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true);

        //  ... = class ${name} ${heritageClauses} {
        //      ${members}
        //  }
        var heritageClauses = Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause);
        var members = Utilities.VisitNodes(node.Members, Visitor, Utilities.IsClassElement);

        var (decorationStatements, decoratedMembers) = TransformDecoratorsOfClassElements(node, members);
        members = decoratedMembers;

        // If we're emitting to ES2022 or later then we need to reassign the class alias before
        // static initializers are evaluated.
        var assignClassAliasInStaticBlock = _languageVersion >= ScriptTarget.ES2022
            && classAlias is not null
            && Core.Some(members, member =>
                NodeTests.IsPropertyDeclaration((Node)member) && Utilities.HasSyntacticModifier((Node)member, ModifierFlags.Static)
                || NodeTests.IsClassStaticBlockDeclaration((Node)member));
        if (assignClassAliasInStaticBlock)
        {
            var extendedMembers = new List<IClassElement>
            {
                _factory.CreateClassStaticBlockDeclaration(
                    _factory.CreateBlock(
                        [
                            _factory.CreateExpressionStatement(
                                _factory.CreateAssignment(classAlias!, _factory.CreateThis())),
                        ],
                        /*multiLine*/ null)),
            };
            extendedMembers.AddRange(members!);
            members = Utilities.SetTextRange(
                _factory.CreateNodeArray(extendedMembers, /*hasTrailingComma*/ null),
                members!);
        }

        var classExpression = _factory.CreateClassExpression(
            modifiers,
            node.Name is not null && Utilities.IsGeneratedIdentifier(node.Name) ? null : node.Name,
            /*typeParameters*/ null,
            heritageClauses,
            members);

        NodeFactory.SetOriginalNode(classExpression, node);
        Utilities.SetTextRange(classExpression, location);

        //  let ${name} = ${classExpression} where name is either declaredName if the class doesn't contain self-reference
        //                                         or decoratedClassAlias if the class contain self-reference.
        // PORT-NOTE: the reference's `BinaryExpression | ClassExpression` is stated as `Expression` — the slot the
        // variable declaration's initializer takes.
        Expression varInitializer = classAlias is not null && !assignClassAliasInStaticBlock
            ? _factory.CreateAssignment(classAlias, classExpression)
            : classExpression;
        var varDecl = _factory.CreateVariableDeclaration(declName, /*exclamationToken*/ null, /*type*/ null, varInitializer);
        NodeFactory.SetOriginalNode(varDecl, node);

        var varDeclList = _factory.CreateVariableDeclarationList([varDecl], NodeFlags.Let);
        var varStatement = _factory.CreateVariableStatement(/*modifiers*/ null, varDeclList);
        NodeFactory.SetOriginalNode(varStatement, node);
        Utilities.SetTextRange(varStatement, location);
        EmitNodes.SetCommentRange(varStatement, node);

        var statements = new List<IStatement> { varStatement };
        Core.AddRange(statements, decorationStatements);
        AddConstructorDecorationStatement(statements, node);

        if (isExport)
        {
            if (isDefault)
            {
                var exportStatement = _factory.CreateExportDefault(declName);
                statements.Add(exportStatement);
            }
            else
            {
                var exportStatement = _factory.CreateExternalModuleExport(
                    TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null));
                statements.Add(exportStatement);
            }
        }

        return statements;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:409-419 — <c>visitClassExpression</c>.</summary>
    private ClassExpression VisitClassExpression(ClassExpression node)
    {
        // Legacy decorators were not supported on class expressions
        return _factory.UpdateClassExpression(
            node,
            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
            node.Name,
            /*typeParameters*/ null,
            Utilities.VisitNodes(node.HeritageClauses, Visitor, NodeTests.IsHeritageClause),
            Utilities.VisitNodes(node.Members, Visitor, Utilities.IsClassElement));
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:421-428 — <c>visitConstructorDeclaration</c>.</summary>
    private ConstructorDeclaration VisitConstructorDeclaration(ConstructorDeclaration node)
        => _factory.UpdateConstructorDeclaration(
            node,
            Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
            Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
            Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock));

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:430-438 — <c>finishClassElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>updated !== original</c> is a node-identity test, stated with
    /// <see cref="object.ReferenceEquals(object?, object?)"/>; the second assignment's range goes through
    /// <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/> (this file's header records why). TS's <c>ClassElement</c>
    /// union keeps one type variable per call, whose constraint is the model's <see cref="Node"/>.</remarks>
    private T FinishClassElement<T>(T updated, T original)
        where T : Node
    {
        if (!ReferenceEquals(updated, original))
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitNodes.SetCommentRange(updated, original);
            EmitNodes.SetSourceMapRange(updated, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers(original)));
        }
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:440-455 — <c>visitMethodDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>Debug.checkDefined</c> is <see cref="Debug.CheckDefined{T}"/>; the reference's two
    /// <c>undefined</c> arguments are the <c>null</c> the port states the defaulted parameters with.</remarks>
    private MethodDeclaration VisitMethodDeclaration(MethodDeclaration node)
        => FinishClassElement(
            _factory.UpdateMethodDeclaration(
                node,
                Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
                node.AsteriskToken,
                Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName), "Expected a property name"),
                /*questionToken*/ null,
                /*typeParameters*/ null,
                Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
                /*type*/ null,
                Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock)),
            node);

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:457-469 — <c>visitGetAccessorDeclaration</c>.</summary>
    private GetAccessorDeclaration VisitGetAccessorDeclaration(GetAccessorDeclaration node)
        => FinishClassElement(
            _factory.UpdateGetAccessorDeclaration(
                node,
                Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
                Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName), "Expected a property name"),
                Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
                /*type*/ null,
                Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock)),
            node);

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:471-482 — <c>visitSetAccessorDeclaration</c>.</summary>
    private SetAccessorDeclaration VisitSetAccessorDeclaration(SetAccessorDeclaration node)
        => FinishClassElement(
            _factory.UpdateSetAccessorDeclaration(
                node,
                Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
                Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName), "Expected a property name"),
                Utilities.VisitNodes(node.Parameters, Visitor, NodeTests.IsParameter),
                Utilities.VisitNode(node.Body, Visitor, NodeTests.IsBlock)),
            node);

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:484-500 — <c>visitPropertyDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>ClassElement | undefined</c> answer is <c>PropertyDeclaration?</c> here (the two arms are
    /// <c>undefined</c> and the property the arm above built, which <c>UpdatePropertyDeclaration</c> pins), and
    /// <c>node.flags &amp; NodeFlags.Ambient</c> is the <c>!= 0</c> bit test.</remarks>
    private PropertyDeclaration? VisitPropertyDeclaration(PropertyDeclaration node)
    {
        if ((node.Flags & NodeFlags.Ambient) != 0 || Utilities.HasSyntacticModifier(node, ModifierFlags.Ambient))
        {
            return null;
        }

        return FinishClassElement(
            _factory.UpdatePropertyDeclaration(
                node,
                Utilities.VisitNodes(node.Modifiers, ModifierVisitor, Utilities.IsModifier),
                Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsPropertyName), "Expected a property name"),
                /*questionOrExclamationToken*/ null,
                /*type*/ null,
                Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression)),
            node);
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:502-521 — <c>visitParameterDeclaration</c>.</summary>
    /// <remarks>PORT-NOTE: <c>elideNodes(factory, node.modifiers)</c> is <see cref="FactoryUtilities.ElideNodes{T}"/>; the
    /// comment the reference leaves on the three range assignments is the one <see cref="FinishClassElement{T}"/>
    /// carries.</remarks>
    private ParameterDeclaration VisitParameterDeclaration(ParameterDeclaration node)
    {
        var updated = _factory.UpdateParameterDeclaration(
            node,
            FactoryUtilities.ElideNodes(_factory, node.Modifiers),
            node.DotDotDotToken,
            Debug.CheckDefined(Utilities.VisitNode(node.Name, Visitor, Utilities.IsBindingName), "Expected a binding name"),
            /*questionToken*/ null,
            /*type*/ null,
            Utilities.VisitNode(node.Initializer, Visitor, Utilities.IsExpression));
        if (!ReferenceEquals(updated, node))
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitNodes.SetCommentRange(updated, node);
            Utilities.SetTextRange(updated, TypeScriptTransformSupport.MoveRangePastModifiers(node));
            EmitNodes.SetSourceMapRange(updated, TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers(node)));
            EmitNodes.SetEmitFlags(updated.Name!, EmitFlags.NoTrailingSourceMap);
        }
        return updated;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:523-525 — <c>isSyntheticMetadataDecorator</c>.</summary>
    /// <remarks>PORT-NOTE: <c>isCallToHelper</c> is <see cref="EmitHelpers.IsCallToHelper"/>, whose helper-name parameter is
    /// the port's <c>string</c> (<c>__String</c> is erased to <c>string</c> port-wide).</remarks>
    private static bool IsSyntheticMetadataDecorator(Decorator node)
        => EmitHelpers.IsCallToHelper(node.Expression!, "___metadata");

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:532-544 —
    /// <c>transformAllDecoratorsOfDeclaration</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>groupBy</c> destructures the two groups it computed; the port calls the landed
    /// <see cref="Core.GroupBy{T, TK}"/>, whose dictionary is keyed by the predicate's own boolean rendered through
    /// <c>ToString</c> (so <c>"False"</c>/<c>"True"</c>) and whose absent groups are a failed lookup — the reference's
    /// <c>undefined</c>.
    /// </para>
    /// <para>
    /// Transforms all of the decorators for a declaration into an array of expressions.
    /// </para>
    /// </remarks>
    private List<Expression>? TransformAllDecoratorsOfDeclaration(IAllDecorators? allDecorators)
    {
        if (allDecorators is null)
        {
            return null;
        }

        // ensure that metadata decorators are last
        var grouped = Core.GroupBy(allDecorators.Decorators, IsSyntheticMetadataDecorator);
        grouped.TryGetValue("False", out var decorators);
        grouped.TryGetValue("True", out var metadata);
        var decoratorExpressions = new List<Expression>();
        Core.AddRange(decoratorExpressions, Core.Map<Decorator, Expression>(decorators, (decorator, _) => TransformDecorator(decorator)));
        Core.AddRange(
            decoratorExpressions,
            Core.FlatMap<List<Decorator>, Expression>(
                allDecorators.Parameters,
                (parameterDecorators, parameterOffset) => TransformDecoratorsOfParameter(parameterDecorators, parameterOffset)));
        Core.AddRange(decoratorExpressions, Core.Map<Decorator, Expression>(metadata, (decorator, _) => TransformDecorator(decorator)));
        return decoratorExpressions;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:554-556 —
    /// <c>addClassElementDecorationStatements</c>.</summary>
    /// <remarks>PORT-NOTE: <c>map</c> is <see cref="Core.Map{T, TU}"/> and <c>addRange</c> is <see cref="Core.AddRange{T}"/>;
    /// the reference's element type <c>Statement</c> is the port's <see cref="IStatement"/>, which the <c>map</c> call states
    /// explicitly because <c>List&lt;T&gt;</c> is invariant.</remarks>
    private void AddClassElementDecorationStatements(List<IStatement> statements, ClassDeclaration node, bool isStatic)
        => Core.AddRange(
            statements,
            Core.Map<Expression, IStatement>(
                GenerateClassElementDecorationExpressions(node, isStatic),
                (expression, _) => _factory.CreateExpressionStatement(expression)));

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:564-567 — <c>isDecoratedClassElement</c>.</summary>
    /// <remarks>PORT-NOTE: <c>nodeOrChildIsDecorated</c> and <c>isStatic</c> are
    /// <see cref="Utilities.NodeOrChildIsDecorated"/>/<see cref="Utilities.IsStatic"/>; TS's <c>ClassLikeDeclaration</c>
    /// parent is the model's <see cref="Node"/>.</remarks>
    private static bool IsDecoratedClassElement(IClassElement member, bool isStaticElement, Node parent)
        => Utilities.NodeOrChildIsDecorated(/*useLegacyDecorators*/ true, (Node)member, parent)
            && isStaticElement == Utilities.IsStatic((Node)member);

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:577-579 — <c>getDecoratedClassElements</c>.</summary>
    /// <remarks>PORT-NOTE: <c>filter</c> is <see cref="Core.Filter{T}"/>, whose <c>null</c> (the reference's <c>undefined</c>
    /// members list) the caller treats as "no members" the way TS's <c>filter</c> treats it.</remarks>
    private static IList<IClassElement>? GetDecoratedClassElements(Node node, bool isStatic)
        => Core.Filter(
            ((IClassLikeDeclarationBase)node).Members,
            member => IsDecoratedClassElement(member, isStatic, node));

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:589-596 —
    /// <c>generateClassElementDecorationExpressions</c>.</summary>
    /// <remarks>PORT-NOTE: <c>append</c> is <see cref="Core.Append{T}"/>, which skips a <c>null</c> value exactly as the
    /// reference's <c>append</c> skips <c>undefined</c>; the loop's type is <c>List&lt;Expression&gt;</c> so the cast that
    /// helper's <c>IList&lt;T&gt;</c> answer needs is local to the assignment.</remarks>
    private List<Expression>? GenerateClassElementDecorationExpressions(Node node, bool isStatic)
    {
        var members = GetDecoratedClassElements(node, isStatic);
        List<Expression>? expressions = null;
        foreach (var member in members ?? [])
        {
            expressions = (List<Expression>?)Core.Append(expressions, GenerateClassElementDecorationExpression(node, member));
        }
        return expressions;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:604-662 —
    /// <c>generateClassElementDecorationExpression</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>getClassMemberPrefix</c>, <c>getExpressionForPropertyName</c> and the <c>createDecorateHelper</c> call
    /// keep the reference's argument order; <c>isPropertyDeclaration</c> and <c>hasAccessorModifier</c> are
    /// <see cref="NodeTests.IsPropertyDeclaration"/> and <see cref="Utilities.HasAccessorModifier"/>, and the second range
    /// assignment goes through <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/> (this file's header records why).
    /// The <c>void 0</c> argument the reference's comment describes is <c>factory.createVoidZero</c>, and the <c>null</c> one
    /// <c>factory.createNull</c> — both the reference's own.
    /// </para>
    /// <para>
    /// PORT-NOTE (the union): TS types <c>member</c> as the <c>ClassElement</c> union, so the port takes
    /// <see cref="IClassElement"/> (the union's interface) and casts to <see cref="Node"/> where a predicate or a modifier
    /// test is asked.
    /// </para>
    /// <para>
    /// Emits the call to <c>__decorate</c> at runtime for one class element; the shape of the call for a method (with its
    /// <c>__param(0, dec2)</c> entry), an accessor and a property is the reference's own comment.
    /// </para>
    /// </remarks>
    private Expression? GenerateClassElementDecorationExpression(Node node, IClassElement member)
    {
        var allDecorators = TransformationUtilities.GetAllDecoratorsOfClassElement(member, node, /*useLegacyDecorators*/ true);
        var decoratorExpressions = TransformAllDecoratorsOfDeclaration(allDecorators);
        if (decoratorExpressions is null)
        {
            return null;
        }

        // Emit the call to __decorate. Given the following:
        //
        //   class C {
        //     @dec method(@dec2 x) {}
        //     @dec get accessor() {}
        //     @dec prop;
        //   }
        //
        // The emit for a method is:
        //
        //   __decorate([
        //       dec,
        //       __param(0, dec2)
        //   ], C.prototype, "method", null);
        //
        // The emit for an accessor is:
        //
        //   __decorate([
        //       dec
        //   ], C.prototype, "accessor", null);
        //
        // The emit for a property is:
        //
        //   __decorate([
        //       dec
        //   ], C.prototype, "prop");

        var prefix = GetClassMemberPrefix(node, member);
        var memberName = GetExpressionForPropertyName(
            (Node)member,
            /*generateNameForComputedPropertyName*/ !Utilities.HasSyntacticModifier((Node)member, ModifierFlags.Ambient));
        var descriptor = NodeTests.IsPropertyDeclaration((Node)member) && !Utilities.HasAccessorModifier((Node)member)
            // We emit `void 0` here to indicate to `__decorate` that it can invoke `Object.defineProperty` directly, but that it
            // should not invoke `Object.getOwnPropertyDescriptor`.
            ? (Expression)_factory.CreateVoidZero()
            // We emit `null` here to indicate to `__decorate` that it can invoke `Object.getOwnPropertyDescriptor` directly.
            // We have this extra argument here so that we can inject an explicit property descriptor at a later date.
            : _factory.CreateNull();

        var helper = _emitHelpers.CreateDecorateHelper(
            decoratorExpressions,
            prefix,
            memberName,
            descriptor);

        EmitNodes.SetEmitFlags(helper, EmitFlags.NoComments);
        EmitNodes.SetSourceMapRange(
            helper,
            TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers((Node)member)));
        return helper;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:669-674 — <c>addConstructorDecorationStatement</c>.</summary>
    /// <remarks>PORT-NOTE: <c>setOriginalNode</c> is <see cref="NodeFactory.SetOriginalNode{T}"/>; the reference's
    /// <c>if (expression)</c> is the <c>is not null</c> test every optional node answer gets.</remarks>
    private void AddConstructorDecorationStatement(List<IStatement> statements, ClassDeclaration node)
    {
        var expression = GenerateConstructorDecorationExpression(node);
        if (expression is not null)
        {
            statements.Add(NodeFactory.SetOriginalNode(_factory.CreateExpressionStatement(expression), node));
        }
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:681-700 —
    /// <c>generateConstructorDecorationExpression</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>getAllDecoratorsOfClass</c> is <see cref="TransformationUtilities.GetAllDecoratorsOfClass"/>,
    /// <c>getOriginalNodeId</c> is <see cref="TransformationUtilities.GetOriginalNodeId"/>, and the reference's
    /// <c>classAliases &amp;&amp; classAliases[id]</c> is the port's optional lookup on the id-indexed
    /// <see cref="Dictionary{TKey, TValue}"/>.
    /// </para>
    /// <para>
    /// PORT-NOTE (the defaulted arguments): the reference calls <c>createDecorateHelper(decoratorExpressions, localName)</c>,
    /// whose remaining two parameters are optional; the port states them as <c>null</c>. The
    /// <c>ClassExpression | ClassDeclaration</c> union is the model's <see cref="Node"/>, as
    /// <see cref="TransformationUtilities.GetAllDecoratorsOfClass"/> records.
    /// </para>
    /// </remarks>
    private Expression? GenerateConstructorDecorationExpression(Node node)
    {
        var allDecorators = TransformationUtilities.GetAllDecoratorsOfClass(node, /*useLegacyDecorators*/ true);
        var decoratorExpressions = TransformAllDecoratorsOfDeclaration(allDecorators);
        if (decoratorExpressions is null)
        {
            return null;
        }

        var classAlias = _classAliases?.GetValueOrDefault(TransformationUtilities.GetOriginalNodeId(node));

        // When we transform to ES5/3 this will be moved inside an IIFE and should reference the name
        // without any block-scoped variable collision handling
        var localName = _languageVersion < ScriptTarget.ES2015
            ? TypeScriptTransformSupport.GetInternalName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true)
            : TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ false, /*allowSourceMaps*/ true);
        var decorate = _emitHelpers.CreateDecorateHelper(decoratorExpressions, localName, /*memberName*/ null, /*descriptor*/ null);
        var expression = _factory.CreateAssignment(
            localName,
            classAlias is not null ? _factory.CreateAssignment(classAlias, decorate) : decorate);
        EmitNodes.SetEmitFlags(expression, EmitFlags.NoComments);
        EmitNodes.SetSourceMapRange(
            expression,
            TypeScriptTransformSupport.SourceMapRangeOf(TypeScriptTransformSupport.MoveRangePastModifiers(node)));
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:707-709 — <c>transformDecorator</c>.</summary>
    /// <remarks>PORT-NOTE: <c>Debug.checkDefined</c> is <see cref="Debug.CheckDefined{T}"/>; the decorator's expression is
    /// the model's <see cref="LeftHandSideExpression"/>, which the <c>isExpression</c> test the visit carries already
    /// licenses as an <see cref="Expression"/>.</remarks>
    private Expression TransformDecorator(Decorator decorator)
        => Debug.CheckDefined(
            Utilities.VisitNode(decorator.Expression, Visitor, Utilities.IsExpression),
            "Expected a decorator expression");

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:717-733 — <c>transformDecoratorsOfParameter</c>.</summary>
    /// <remarks>PORT-NOTE: <c>createParamHelper</c> is <see cref="IEmitHelperFactory.CreateParamHelper"/>,
    /// <c>setTextRange</c>/<c>setEmitFlags</c> are <see cref="Utilities.SetTextRange{T}"/>/
    /// <see cref="EmitNodes.SetEmitFlags{T}"/>, and the reference's <c>readonly Decorator[] | undefined</c> parameter is the
    /// model's <c>List&lt;Decorator&gt;?</c> (AllDecorators' own cast records the hole).</remarks>
    private List<Expression>? TransformDecoratorsOfParameter(List<Decorator>? decorators, int parameterOffset)
    {
        List<Expression>? expressions = null;
        if (decorators is not null)
        {
            expressions = [];
            foreach (var decorator in decorators)
            {
                var helper = _emitHelpers.CreateParamHelper(
                    TransformDecorator(decorator),
                    parameterOffset);
                Utilities.SetTextRange(helper, decorator.Expression);
                EmitNodes.SetEmitFlags(helper, EmitFlags.NoComments);
                expressions.Add(helper);
            }
        }

        return expressions;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:741-757 — <c>getExpressionForPropertyName</c>.</summary>
    /// <remarks>PORT-NOTE: <c>idText</c> is <see cref="Utilities.IdText"/> and <c>cloneNode</c> is
    /// <see cref="NodeFactory.CloneNode{T}"/>; TS's <c>ClassElement | EnumMember</c> union is the model's <see cref="Node"/>
    /// (both arms carry <c>name</c>, read through <see cref="INamedDeclaration"/>), and <c>isSimpleInlineableExpression</c>
    /// is <see cref="TransformationUtilities.IsSimpleInlineableExpression"/>.</remarks>
    private Expression GetExpressionForPropertyName(Node member, bool generateNameForComputedPropertyName)
    {
        var name = ((INamedDeclaration)member).Name!;
        if (NodeTests.IsPrivateIdentifier(name))
        {
            return _factory.CreateIdentifier("");
        }
        else if (NodeTests.IsComputedPropertyName(name))
        {
            return generateNameForComputedPropertyName && !TransformationUtilities.IsSimpleInlineableExpression(((ComputedPropertyName)name).Expression!)
                ? _factory.GetGeneratedNameForNode(name, /*flags*/ null)
                : ((ComputedPropertyName)name).Expression!;
        }
        else if (NodeTests.IsIdentifier(name))
        {
            return _factory.CreateStringLiteral(Utilities.IdText(name), /*isSingleQuote*/ null, /*hasExtendedUnicodeEscape*/ null);
        }
        else
        {
            return _factory.CloneNode((Expression)name);
        }
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:759-768 — <c>enableSubstitutionForClassAliases</c>.</summary>
    /// <remarks>PORT-NOTE: the reference enables the substitution for identifiers and then allocates the alias table
    /// (<c>classAliases = []</c>); the port's "was it enabled" test is the <c>null</c> check on <see cref="_classAliases"/>.</remarks>
    private void EnableSubstitutionForClassAliases()
    {
        if (_classAliases is null)
        {
            // We need to enable substitutions for identifiers. This allows us to
            // substitute class names inside of a class declaration.
            _context.EnableSubstitution(SyntaxKind.Identifier);

            // Keep track of class aliases.
            _classAliases = [];
        }
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:775-783 — <c>getClassAliasIfNeeded</c>.</summary>
    /// <remarks>PORT-NOTE: <c>hasNodeCheckFlag</c> is <see cref="IEmitResolver.HasNodeCheckFlag"/>, <c>createUniqueName</c>
    /// is <see cref="NodeFactory.CreateUniqueName(string?, GeneratedIdentifierFlags?)"/>, and <c>hoistVariableDeclaration</c>
    /// is <see cref="ITransformerContext.HoistVariableDeclaration"/>; the reference's
    /// <c>name &amp;&amp; !isGeneratedIdentifier(name) ? idText(name) : "default"</c> is the same test over the model's
    /// <c>Identifier?</c>.</remarks>
    private Identifier? GetClassAliasIfNeeded(ClassDeclaration node)
    {
        if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.ContainsConstructorReference))
        {
            EnableSubstitutionForClassAliases();
            var classAlias = _factory.CreateUniqueName(
                node.Name is not null && !Utilities.IsGeneratedIdentifier(node.Name) ? Utilities.IdText(node.Name) : "default",
                /*flags*/ null);
            _classAliases![TransformationUtilities.GetOriginalNodeId(node)] = classAlias;
            _context.HoistVariableDeclaration(classAlias);
            return classAlias;
        }
        return null;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:785-787 — <c>getClassPrototype</c>.</summary>
    private Expression GetClassPrototype(Node node)
        => _factory.CreatePropertyAccessExpression(
            TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null),
            "prototype");

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:789-791 — <c>getClassMemberPrefix</c>.</summary>
    private Expression GetClassMemberPrefix(Node node, IClassElement member)
        => Utilities.IsStatic((Node)member)
            ? TypeScriptTransformSupport.GetDeclarationName(_factory, node, /*allowComments*/ null, /*allowSourceMaps*/ null)
            : GetClassPrototype(node);

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:801-807 — <c>onSubstituteNode</c>.</summary>
    /// <remarks>PORT-NOTE: <c>hint === EmitHint.Expression</c> is the enum equality the port keeps; the cast to
    /// <see cref="Expression"/> is the reference's own narrowing, which the substitution table's rule (an <c>Expression</c>
    /// hint always carries an expression) licenses.</remarks>
    private Node OnSubstituteNode(EmitHint hint, Node node)
    {
        node = _previousOnSubstituteNode(hint, node);
        if (hint == EmitHint.Expression)
        {
            return SubstituteExpression((Expression)node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:809-816 — <c>substituteExpression</c>.</summary>
    private Expression SubstituteExpression(Expression node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
                return SubstituteExpressionIdentifier((Identifier)node);
        }

        return node;
    }

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:818-820 — <c>substituteExpressionIdentifier</c>.</summary>
    private Expression SubstituteExpressionIdentifier(Identifier node)
        => TrySubstituteClassAlias(node)
            ?? node;

    /// <summary>ported from: src/compiler/transformers/legacyDecorators.ts:823-846 — <c>trySubstituteClassAlias</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>hasNodeCheckFlag</c> is <see cref="IEmitResolver.HasNodeCheckFlag"/> and
    /// <c>getReferencedValueDeclaration</c> is <see cref="IEmitResolver.GetReferencedValueDeclaration"/>; the two range
    /// assignments go through <see cref="TypeScriptTransformSupport.SourceMapRangeOf"/> (this file's header records why).
    /// </para>
    /// <para>
    /// PORT-NOTE (the reference's <c>declaration.id!</c>): <c>id</c> is <c>Node.id</c>, which the model carries as the
    /// lazily-assigned <c>int?</c> <c>Node.Id</c> — the reference's own <c>// TODO: GH#18217</c> marks the non-null assertion
    /// on a slot that may not have been filled. The port states the same read (<c>declaration</c> is an
    /// <see cref="IDeclaration"/>, whose <see cref="INode"/> face carries no id, so the cast is to the node class), and a
    /// declaration whose id was never computed is the failed lookup the reference's <c>undefined</c> key produces.
    /// </para>
    /// </remarks>
    private Expression? TrySubstituteClassAlias(Identifier node)
    {
        if (_classAliases is not null)
        {
            if (_resolver.HasNodeCheckFlag(node, NodeCheckFlags.ConstructorReference))
            {
                // Due to the emit for class decorators, any reference to the class from inside of the class body
                // must instead be rewritten to point to a temporary variable to avoid issues with the double-bind
                // behavior of class names in ES6.
                // Also, when emitting statics for class expressions, we must substitute a class alias for
                // constructor references in static property initializers.
                var declaration = _resolver.GetReferencedValueDeclaration(node);
                if (declaration is not null)
                {
                    var classAlias = ((Node)declaration).Id is int id ? _classAliases.GetValueOrDefault(id) : null;
                    if (classAlias is not null)
                    {
                        var clone = _factory.CloneNode(classAlias);
                        EmitNodes.SetSourceMapRange(clone, TypeScriptTransformSupport.SourceMapRangeOf(node));
                        EmitNodes.SetCommentRange(clone, node);
                        return clone;
                    }
                }
            }
        }

        return null;
    }
}