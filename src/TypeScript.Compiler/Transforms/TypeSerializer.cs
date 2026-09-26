#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/typeSerializer.ts:1-633 — the whole decorator-metadata type serializer
/// <summary>
/// ported from: src/compiler/transformers/typeSerializer.ts:132-633 — <c>createRuntimeTypeSerializer</c>: the serializer
/// that turns a type node into the constructor expression a <c>design:type</c>/<c>design:paramtypes</c>/
/// <c>design:returntype</c> decorator-metadata entry carries.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports these as module-level functions of
/// <c>transformers/typeSerializer.ts</c>, so they land on the class named after that module and are called qualified
/// (<c>TypeSerializer.CreateRuntimeTypeSerializer</c>). The module's two exported types are nested in it
/// (<see cref="RuntimeTypeSerializerContext"/>, <see cref="RuntimeTypeSerializer"/>), which is the treatment
/// <see cref="TransformationUtilities"/> gives <c>transformers/utilities.ts</c>'s own classes and interfaces.
/// </para>
/// <para>
/// PORT-NOTE (the unions erased, §12.5): <c>SerializedEntityName</c> (<c>Identifier | PropertyAccessEntityNameExpression</c>)
/// is <see cref="MemberExpression"/> — both arms are one — and <c>SerializedTypeNode</c> (that plus
/// <c>ConditionalExpression | VoidExpression</c>) is <see cref="Expression"/>, the reference's own fallback shape.
/// <c>EntityName</c> (<c>Identifier | QualifiedName</c>) has no model interface, so <see cref="Node"/> is its slot and
/// the <c>QualifiedName</c> arm narrows by kind. <c>PropertyAccessEntityNameExpression</c> is the intersection
/// <c>PropertyAccessExpression &amp; EntityNameExpression</c>; the model's
/// <see cref="NodeFactory.CreatePropertyAccessExpression"/> already answers the <see cref="PropertyAccessExpression"/>
/// half, which is what the reference's cast records, so no cast is needed here.
/// </para>
/// <para>
/// PORT-NOTE (the context face, §15.9): the reference's parameter is the full <c>TransformationContext</c>, which no
/// port type can implement (<c>ICoreTransformationContext.Factory</c> is <c>INodeFactory?</c> and
/// <see cref="NodeFactory"/> does not implement <see cref="INodeFactory"/> — <see cref="Transformer"/>'s header). This
/// module reads the concrete factory, the compiler options, <c>hoistVariableDeclaration</c> and
/// <c>getEmitResolver</c>, which is <see cref="ITransformationUtilitiesContext"/> — the slice
/// <c>transformers/utilities.ts</c>, <c>destructuring.ts</c> and <c>taggedTemplate.ts</c> already read — so that is
/// the declared parameter, and every real caller (<c>transformTypeScript</c>'s context) satisfies it as it stands.
/// </para>
/// <para>
/// PORT-NOTE (the two accessor queries): <c>getAccessorTypeNode</c> and <c>getParametersOfDecoratedDeclaration</c> call
/// <c>getAllAccessorDeclarations</c> (utilities.ts:6832-6885) and <c>getSetAccessorTypeAnnotationNode</c>
/// (utilities.ts:6776-6779). The port's first copy of the former is a local function inside
/// <c>Utilities.ClassElementOrClassElementParameterIsDecorated</c> and the second is private to
/// <see cref="TransformationUtilities"/>; the latter is not landed in <c>Utilities</c> at all. Both are therefore
/// re-stated below, private to this class, with the reference bodies; consolidating the three copies is a report item.
/// </para>
/// <para>
/// PORT-NOTE (the two <c>Debug.failBadSyntaxKind</c>/<c>Debug.assertNever</c> arms): TS returns those calls in
/// expression position because they are <c>never</c>; the port's <see cref="Debug.FailBadSyntaxKind"/> and
/// <see cref="Debug.AssertNever"/> are <c>void</c>, so the value-producing form is <see cref="Debug.Fail{T}"/> carrying
/// the same message the helper builds — the shape <c>Checker.Check.Symbols.cs</c> and <c>Checker.Check.Calls.cs</c>
/// already record for the same situation.
/// </para>
/// </remarks>
internal static class TypeSerializer
{
    /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:86-92 — <c>RuntimeTypeSerializerContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>currentLexicalScope</c>'s type is the union <c>SourceFile | Block | ModuleBlock | CaseBlock</c>,
    /// whose common base in this model is <see cref="Node"/>; <c>currentNameScope</c>'s is
    /// <c>ClassLikeDeclaration</c>, which the model carries as <see cref="IClassLikeDeclarationBase"/> (the class and
    /// class-expression union). The reference's callers fill the type in with an object literal at each serialization,
    /// so it is a class with the same two settable members.
    /// </remarks>
    internal sealed class RuntimeTypeSerializerContext
    {
        /// <summary>Specifies the current lexical block scope.</summary>
        public Node CurrentLexicalScope { get; set; } = null!;

        /// <summary>Specifies the containing <c>class</c>, but only when there is no other block scope between the
        /// current location and the <c>class</c>.</summary>
        public IClassLikeDeclarationBase? CurrentNameScope { get; set; }
    }

    /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:94-130 — <c>RuntimeTypeSerializer</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE (the shape): the reference states this as an interface and fills it in with the object literal
    /// <c>createRuntimeTypeSerializer</c> returns — a table of four closures over that function's locals. The port states
    /// the same shape as this class, which holds the same locals as fields and the same four members; the enclosing
    /// module function is <see cref="CreateRuntimeTypeSerializer"/>.
    /// </para>
    /// <para>
    /// The reference's documentation of the serialization each member performs is on the members below, where
    /// <c>SerializeTypeNode</c> carries the full table.
    /// </para>
    /// </remarks>
    internal sealed class RuntimeTypeSerializer
    {
        private readonly NodeFactory _factory;
        private readonly Action<Identifier> _hoistVariableDeclaration;
        private readonly IEmitResolver _resolver;
        private readonly ScriptTarget _languageVersion;
        private readonly bool _strictNullChecks;

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:135-136 — the two module-level <c>let</c>s
        /// <c>setSerializerContextAnd</c> saves and restores.</summary>
        /// <remarks>PORT-NOTE: TS's <c>let currentLexicalScope: SourceFile | …</c> is declared without an initializer
        /// and assigned before every read (the four exported members are the only way in); the port's field is
        /// <c>null!</c>-initialized — the same "assigned before read" contract with the type it is read as.</remarks>
        private Node _currentLexicalScope = null!;
        private IClassLikeDeclarationBase? _currentNameScope;

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:132-144 — the destructuring at the head of
        /// <c>createRuntimeTypeSerializer</c>.</summary>
        /// <remarks>PORT-NOTE: <c>hoistVariableDeclaration</c> is the reference's standalone function, which
        /// <see cref="NodeFactory.CreateTempVariable(Action{Identifier}?, bool?)"/> records each temporary with; the
        /// context's member takes a nullable node, so the field's delegate is the widening one (the reference's own
        /// function accepts <c>undefined</c>). The constructor is public because a nested type's members are not
        /// reachable from its enclosing type, and <see cref="CreateRuntimeTypeSerializer"/> — the reference's own entry
        /// point — is what builds the serializer.
        /// </remarks>
        public RuntimeTypeSerializer(ITransformationUtilitiesContext context)
        {
            _factory = context.Factory;
            _hoistVariableDeclaration = context.HoistVariableDeclaration;
            var compilerOptions = context.GetCompilerOptions();
            _resolver = context.GetEmitResolver();
            _languageVersion = Utilities.GetEmitScriptTarget(compilerOptions);
            _strictNullChecks = Utilities.GetStrictOptionValue(compilerOptions, "strictNullChecks");
        }

        /// <summary>Serializes a type node for use with decorator type metadata.</summary>
        /// <remarks>
        /// Types are serialized in the following fashion:
        /// <list type="bullet">
        /// <item><description>Void types point to "undefined" (e.g. "void 0")</description></item>
        /// <item><description>Function and Constructor types point to the global "Function" constructor.</description></item>
        /// <item><description>Interface types with a call or construct signature types point to the global "Function"
        /// constructor.</description></item>
        /// <item><description>Array and Tuple types point to the global "Array" constructor.</description></item>
        /// <item><description>Type predicates and booleans point to the global "Boolean" constructor.</description></item>
        /// <item><description>String literal types and strings point to the global "String" constructor.</description></item>
        /// <item><description>Enum and number types point to the global "Number" constructor.</description></item>
        /// <item><description>Symbol types point to the global "Symbol" constructor.</description></item>
        /// <item><description>Type references to classes (or class-like variables) point to the constructor for the
        /// class.</description></item>
        /// <item><description>Anything else points to the global "Object" constructor.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="serializerContext">The current lexical and name scopes.</param>
        /// <param name="node">The type node to serialize.</param>
        public Expression SerializeTypeNode(RuntimeTypeSerializerContext serializerContext, ITypeNode node)
            => SetSerializerContextAnd<ITypeNode, Expression>(serializerContext, SerializeTypeNode, node);

        /// <summary>Serializes the type of a node for use with decorator type metadata.</summary>
        /// <param name="serializerContext">The current lexical and name scopes.</param>
        /// <param name="node">The node that should have its type serialized.</param>
        /// <param name="container">The class the declaration belongs to.</param>
        public Expression SerializeTypeOfNode(
            RuntimeTypeSerializerContext serializerContext,
            Node node,
            IClassLikeDeclarationBase container)
            => SetSerializerContextAnd<Node, IClassLikeDeclarationBase, Expression>(
                serializerContext, SerializeTypeOfNode, node, container);

        /// <summary>Serializes the types of the parameters of a node for use with decorator type metadata.</summary>
        /// <param name="serializerContext">The current lexical and name scopes.</param>
        /// <param name="node">The node that should have its parameter types serialized.</param>
        /// <param name="container">The class the declaration belongs to.</param>
        public ArrayLiteralExpression SerializeParameterTypesOfNode(
            RuntimeTypeSerializerContext serializerContext,
            Node node,
            IClassLikeDeclarationBase container)
            => SetSerializerContextAnd<Node, IClassLikeDeclarationBase, ArrayLiteralExpression>(
                serializerContext, SerializeParameterTypesOfNode, node, container);

        /// <summary>Serializes the return type of a node for use with decorator type metadata.</summary>
        /// <param name="serializerContext">The current lexical and name scopes.</param>
        /// <param name="node">The node that should have its return type serialized.</param>
        public Expression SerializeReturnTypeOfNode(RuntimeTypeSerializerContext serializerContext, Node node)
            => SetSerializerContextAnd<Node, Expression>(serializerContext, SerializeReturnTypeOfNode, node);

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:153-167 — <c>setSerializerContextAnd</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS states two overloads and one implementation that discriminates on <c>arg === undefined</c>; C#
        /// resolves the same two shapes statically, so the port states one member per shape and the four exported
        /// members pick theirs by the arguments they pass. <c>TNode</c> is constrained by <see cref="INode"/> rather than
        /// <c>Node</c> because the node a serializer receives may be an <see cref="ITypeNode"/>, which <c>Node</c>
        /// implements rather than the reverse, and TS's <c>TNode extends Node | undefined</c> is absorbed by each
        /// callee's nullable parameter.
        /// </remarks>
        private TReturn SetSerializerContextAnd<TNode, TReturn>(
            RuntimeTypeSerializerContext serializerContext,
            Func<TNode, TReturn> cb,
            TNode node)
            where TNode : INode
        {
            var savedCurrentLexicalScope = _currentLexicalScope;
            var savedCurrentNameScope = _currentNameScope;

            _currentLexicalScope = serializerContext.CurrentLexicalScope;
            _currentNameScope = serializerContext.CurrentNameScope;

            var result = cb(node);

            _currentLexicalScope = savedCurrentLexicalScope;
            _currentNameScope = savedCurrentNameScope;
            return result;
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:154-167 — <c>setSerializerContextAnd</c>
        /// with the second argument.</summary>
        private TReturn SetSerializerContextAnd<TNode, T, TReturn>(
            RuntimeTypeSerializerContext serializerContext,
            Func<TNode, T, TReturn> cb,
            TNode node,
            T arg)
            where TNode : INode
        {
            var savedCurrentLexicalScope = _currentLexicalScope;
            var savedCurrentNameScope = _currentNameScope;

            _currentLexicalScope = serializerContext.CurrentLexicalScope;
            _currentNameScope = serializerContext.CurrentNameScope;

            var result = cb(node, arg);

            _currentLexicalScope = savedCurrentLexicalScope;
            _currentNameScope = savedCurrentNameScope;
            return result;
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:169-174 — <c>getAccessorTypeNode</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: the reference's first parameter is the <c>AccessorDeclaration</c> union and its second the
        /// <c>ClassLikeDeclaration</c> union; the port takes the model's <see cref="Node"/> and
        /// <see cref="IClassLikeDeclarationBase"/> for them (the casts below are where the union members apply). The
        /// <c>a &amp;&amp; b || c &amp;&amp; d</c> chain is the port's null-coalescing chain: in TS a setter's absent
        /// annotation is <c>undefined</c> and the right operand is evaluated, which is exactly the <c>??</c> fallback.
        /// </remarks>
        private ITypeNode? GetAccessorTypeNode(Node node, IClassLikeDeclarationBase container)
        {
            var (_, _, getAccessor, setAccessor) =
                GetAllAccessorDeclarations(container.Members, (IFunctionLikeDeclarationBase)node);
            var setAccessorType = setAccessor is not null ? GetSetAccessorTypeAnnotationNode(setAccessor) : null;
            return setAccessorType ?? (getAccessor is not null ? Utilities.GetEffectiveReturnTypeNode(getAccessor) : null);
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:179-194 — <c>serializeTypeOfNode</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: the reference's union <c>PropertyDeclaration | ParameterDeclaration | AccessorDeclaration |
        /// ClassLikeDeclaration | MethodDeclaration</c> has <see cref="Node"/> as its common base in the model, and each
        /// arm narrows to the class whose <c>type</c> slot it reads (the two declaration kinds share no interface with a
        /// <c>type</c> member, which is why the reference's one <c>case</c> pair is two here).
        /// </remarks>
        private Expression SerializeTypeOfNode(Node node, IClassLikeDeclarationBase container)
        {
            switch (node.Kind)
            {
                case SyntaxKind.PropertyDeclaration:
                    return SerializeTypeNode(((PropertyDeclaration)node).Type);

                case SyntaxKind.Parameter:
                    return SerializeTypeNode(((ParameterDeclaration)node).Type);

                case SyntaxKind.SetAccessor:
                case SyntaxKind.GetAccessor:
                    return SerializeTypeNode(GetAccessorTypeNode(node, container));

                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.MethodDeclaration:
                    return _factory.CreateIdentifier("Function");

                default:
                    return _factory.CreateVoidZero();
            }
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:200-226 — <c>serializeParameterTypesOfNode</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS's <c>nodeIsPresent(node.body)</c> is <see cref="Utilities.NodeIsPresent"/> over the function-like
        /// interface's body, and <c>parameter.name</c>'s <c>escapedText</c> read is the <see cref="Identifier"/> cast
        /// after <see cref="NodeTests.IsIdentifier"/> (the escaping is by <c>escapedText</c>, so this is the reference's
        /// own "is the <c>this</c> parameter" test rather than <c>parameterIsThisKeyword</c>, which it does not call).
        /// </remarks>
        private ArrayLiteralExpression SerializeParameterTypesOfNode(Node node, IClassLikeDeclarationBase container)
        {
            var valueDeclaration = Utilities.IsClassLike(node)
                ? (Node?)Utilities.GetFirstConstructorWithBody(node)
                : Utilities.IsFunctionLike(node) && Utilities.NodeIsPresent(((IFunctionLikeDeclarationBase)node).Body)
                ? node
                : null;

            var expressions = new List<Expression>();
            if (valueDeclaration is not null)
            {
                var parameters = GetParametersOfDecoratedDeclaration(valueDeclaration, container);
                var numParameters = parameters.Count;
                for (var i = 0; i < numParameters; i++)
                {
                    var parameter = parameters[i];
                    if (i == 0 && NodeTests.IsIdentifier(parameter.Name!)
                        && ((Identifier)parameter.Name!).EscapedText == "this")
                    {
                        continue;
                    }

                    if (parameter.DotDotDotToken is not null)
                    {
                        expressions.Add(SerializeTypeNode(Utilities.GetRestParameterElementType(parameter.Type)));
                    }
                    else
                    {
                        expressions.Add(SerializeTypeOfNode(parameter, container));
                    }
                }
            }

            return _factory.CreateArrayLiteralExpression(expressions, null);
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:228-236 — <c>getParametersOfDecoratedDeclaration</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: the reference's <c>container &amp;&amp;</c> guard is kept although both callers pass a non-null
        /// container — it is the reference's own reading of this parameter, and the port states the parameter nullable to
        /// match it. <c>node.parameters</c> is the <c>SignatureDeclaration</c> union's slot, which the model carries on
        /// <see cref="ISignatureDeclarationBase"/>.
        /// </remarks>
        private static NodeArray<ParameterDeclaration> GetParametersOfDecoratedDeclaration(
            Node node,
            IClassLikeDeclarationBase? container)
        {
            if (container is not null && node.Kind == SyntaxKind.GetAccessor)
            {
                var (_, _, _, setAccessor) =
                    GetAllAccessorDeclarations(container.Members, (IFunctionLikeDeclarationBase)node);
                if (setAccessor is not null)
                {
                    return setAccessor.Parameters!;
                }
            }

            return ((ISignatureDeclarationBase)node).Parameters!;
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:242-251 — <c>serializeReturnTypeOfNode</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: <c>node.type</c> after <c>isFunctionLike</c> is the signature interface's <c>Type</c> slot; the
        /// reference reads the annotation itself here rather than <c>getEffectiveReturnTypeNode</c>, so no JSDoc
        /// fallback applies.
        /// </remarks>
        private Expression SerializeReturnTypeOfNode(Node node)
        {
            if (Utilities.IsFunctionLike(node) && ((ISignatureDeclarationBase)node).Type is not null)
            {
                return SerializeTypeNode(((ISignatureDeclarationBase)node).Type);
            }
            else if (Utilities.IsAsyncFunction(node))
            {
                return _factory.CreateIdentifier("Promise");
            }

            return _factory.CreateVoidZero();
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:271-365 — <c>serializeTypeNode</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: the reference's <c>node</c> is <c>TypeNode | undefined</c> and is reassigned by
        /// <c>skipTypeParentheses</c>; the port keeps the nullable parameter and narrows it into the parenthesised type,
        /// which is the same value. The <c>UnionOrIntersectionTypeNode</c> and <c>ConditionalTypeNode</c> arms pass the
        /// <c>types</c> arrays those classes hold. <c>Debug.failBadSyntaxKind</c> is <see cref="Debug.Fail{T}"/> (see the
        /// file header).
        /// </remarks>
        private Expression SerializeTypeNode(ITypeNode? node)
        {
            if (node is null)
            {
                return _factory.CreateIdentifier("Object");
            }

            var typeNode = Utilities.SkipTypeParentheses(node);

            switch (typeNode.Kind)
            {
                case SyntaxKind.VoidKeyword:
                case SyntaxKind.UndefinedKeyword:
                case SyntaxKind.NeverKeyword:
                    return _factory.CreateVoidZero();

                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                    return _factory.CreateIdentifier("Function");

                case SyntaxKind.ArrayType:
                case SyntaxKind.TupleType:
                    return _factory.CreateIdentifier("Array");

                case SyntaxKind.TypePredicate:
                    return ((TypePredicateNode)typeNode).AssertsModifier is not null
                        ? _factory.CreateVoidZero()
                        : _factory.CreateIdentifier("Boolean");

                case SyntaxKind.BooleanKeyword:
                    return _factory.CreateIdentifier("Boolean");

                case SyntaxKind.TemplateLiteralType:
                case SyntaxKind.StringKeyword:
                    return _factory.CreateIdentifier("String");

                case SyntaxKind.ObjectKeyword:
                    return _factory.CreateIdentifier("Object");

                case SyntaxKind.LiteralType:
                    return SerializeLiteralOfLiteralTypeNode(((LiteralTypeNode)typeNode).Literal!);

                case SyntaxKind.NumberKeyword:
                    return _factory.CreateIdentifier("Number");

                case SyntaxKind.BigIntKeyword:
                    return GetGlobalConstructor("BigInt", ScriptTarget.ES2020);

                case SyntaxKind.SymbolKeyword:
                    return GetGlobalConstructor("Symbol", ScriptTarget.ES2015);

                case SyntaxKind.TypeReference:
                    return SerializeTypeReferenceNode((TypeReferenceNode)typeNode);

                case SyntaxKind.IntersectionType:
                    return SerializeUnionOrIntersectionConstituents(
                        ((IntersectionTypeNode)typeNode).Types!, /*isIntersection*/ true);

                case SyntaxKind.UnionType:
                    return SerializeUnionOrIntersectionConstituents(
                        ((UnionTypeNode)typeNode).Types!, /*isIntersection*/ false);

                case SyntaxKind.ConditionalType:
                    return SerializeUnionOrIntersectionConstituents(
                        [((ConditionalTypeNode)typeNode).TrueType!, ((ConditionalTypeNode)typeNode).FalseType!],
                        /*isIntersection*/ false);

                case SyntaxKind.TypeOperator:
                    if (((TypeOperatorNode)typeNode).Operator == SyntaxKind.ReadonlyKeyword)
                    {
                        return SerializeTypeNode(((TypeOperatorNode)typeNode).Type);
                    }

                    break;

                case SyntaxKind.TypeQuery:
                case SyntaxKind.IndexedAccessType:
                case SyntaxKind.MappedType:
                case SyntaxKind.TypeLiteral:
                case SyntaxKind.AnyKeyword:
                case SyntaxKind.UnknownKeyword:
                case SyntaxKind.ThisType:
                case SyntaxKind.ImportType:
                    break;

                // handle JSDoc types from an invalid parse
                case SyntaxKind.JSDocAllType:
                case SyntaxKind.JSDocUnknownType:
                case SyntaxKind.JSDocFunctionType:
                case SyntaxKind.JSDocVariadicType:
                case SyntaxKind.JSDocNamepathType:
                    break;

                // PORT-NOTE: the reference's one arm spans the three JSDoc wrapper kinds, whose union has no common
                // member-carrying base in this model (`JSDocType` declares no `type`), so each kind narrows to its own
                // class — the same three kinds, the same slot, in the same order.
                case SyntaxKind.JSDocNullableType:
                    return SerializeTypeNode(((JSDocNullableType)typeNode).Type);

                case SyntaxKind.JSDocNonNullableType:
                    return SerializeTypeNode(((JSDocNonNullableType)typeNode).Type);

                case SyntaxKind.JSDocOptionalType:
                    return SerializeTypeNode(((JSDocOptionalType)typeNode).Type);

                default:
                    return Debug.Fail<Expression>(
                        $"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(typeNode.Kind)} was unexpected.");
            }

            return _factory.CreateIdentifier("Object");
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:367-400 — <c>serializeLiteralOfLiteralTypeNode</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS's parameter is <c>LiteralTypeNode["literal"]</c> — <c>NullLiteral | BooleanLiteral |
        /// LiteralExpression | PrefixUnaryExpression</c> — whose common base in this model is
        /// <see cref="UpdateExpression"/> (the slot the model gives <c>LiteralTypeNode.Literal</c>); the negated-numeric
        /// arm's operand is re-entered through <see cref="LiteralExpression"/>, which is what the reference's
        /// <c>as NumericLiteral | BigIntLiteral</c> records, and <c>Debug.failBadSyntaxKind</c> is
        /// <see cref="Debug.Fail{T}"/> (see the file header).
        /// </remarks>
        private Expression SerializeLiteralOfLiteralTypeNode(UpdateExpression node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.StringLiteral:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                    return _factory.CreateIdentifier("String");

                case SyntaxKind.PrefixUnaryExpression:
                    {
                        var operand = ((PrefixUnaryExpression)node).Operand;
                        switch (operand!.Kind)
                        {
                            case SyntaxKind.NumericLiteral:
                            case SyntaxKind.BigIntLiteral:
                                return SerializeLiteralOfLiteralTypeNode((LiteralExpression)operand);

                            default:
                                return Debug.Fail<Expression>(
                                    $"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(operand!.Kind)} was unexpected.");
                        }
                    }

                case SyntaxKind.NumericLiteral:
                    return _factory.CreateIdentifier("Number");

                case SyntaxKind.BigIntLiteral:
                    return GetGlobalConstructor("BigInt", ScriptTarget.ES2020);

                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                    return _factory.CreateIdentifier("Boolean");

                case SyntaxKind.NullKeyword:
                    return _factory.CreateVoidZero();

                default:
                    return Debug.Fail<Expression>(
                        $"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(node.Kind)} was unexpected.");
            }
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:402-447 — <c>serializeUnionOrIntersectionConstituents</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS's <c>readonly TypeNode[]</c> is the port's <see cref="IList{T}"/> so that a
        /// <see cref="NodeArray{T}"/> and the conditional arm's fresh list both bind; the reassigning <c>for…of</c> is a
        /// <c>foreach</c> over the unskipped constituent with the reference's <c>skipTypeParentheses</c> applied to a
        /// local. <c>serializeTypeNode</c>'s answer is read for <c>Object</c> by the same
        /// <see cref="NodeTests.IsIdentifier"/> test.
        /// </remarks>
        private Expression SerializeUnionOrIntersectionConstituents(IList<ITypeNode> types, bool isIntersection)
        {
            // Note when updating logic here also update `getEntityNameForDecoratorMetadata` in checker.ts so that aliases can be marked as referenced
            Expression? serializedType = null;
            foreach (var typeNodeIn in types)
            {
                var typeNode = Utilities.SkipTypeParentheses(typeNodeIn);
                if (typeNode.Kind == SyntaxKind.NeverKeyword)
                {
                    if (isIntersection) return _factory.CreateVoidZero(); // Reduce to `never` in an intersection
                    continue; // Elide `never` in a union
                }

                if (typeNode.Kind == SyntaxKind.UnknownKeyword)
                {
                    if (!isIntersection) return _factory.CreateIdentifier("Object"); // Reduce to `unknown` in a union
                    continue; // Elide `unknown` in an intersection
                }

                if (typeNode.Kind == SyntaxKind.AnyKeyword)
                {
                    return _factory.CreateIdentifier("Object"); // Reduce to `any` in a union or intersection
                }

                if (!_strictNullChecks
                    && ((NodeTests.IsLiteralTypeNode((Node)typeNode)
                            && ((LiteralTypeNode)typeNode).Literal!.Kind == SyntaxKind.NullKeyword)
                        || typeNode.Kind == SyntaxKind.UndefinedKeyword))
                {
                    continue; // Elide null and undefined from unions for metadata, just like what we did prior to the implementation of strict null checks
                }

                var serializedConstituent = SerializeTypeNode(typeNode);
                if (NodeTests.IsIdentifier(serializedConstituent)
                    && ((Identifier)serializedConstituent).EscapedText == "Object")
                {
                    // One of the individual is global object, return immediately
                    return serializedConstituent;
                }

                // If there exists union that is not `void 0` expression, check if the the common type is identifier.
                // anything more complex and we will just default to Object
                if (serializedType is not null)
                {
                    // Different types
                    if (!EquateSerializedTypeNodes(serializedType, serializedConstituent))
                    {
                        return _factory.CreateIdentifier("Object");
                    }
                }
                else
                {
                    // Initialize the union type
                    serializedType = serializedConstituent;
                }
            }

            // If we were able to find common type, use it
            return serializedType ?? _factory.CreateVoidZero(); // Fallback is only hit if all union constituents are null/undefined/never
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:449-484 — <c>equateSerializedTypeNodes</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: the reference's nested conditional chain is stated as one <c>if</c> per arm in the same order, each
        /// asking the same predicate of <c>left</c> and then of <c>right</c>; the arms that recurse into a child read that
        /// child's slot off the narrowed class (the reference reads them off the union members the same predicates
        /// narrow). <c>escapedText</c>/<c>text</c> are <c>===</c> on strings, which is C#'s <c>==</c>.
        /// </remarks>
        private static bool EquateSerializedTypeNodes(Expression left, Expression right)
        {
            // temp vars used in fallback
            if (Utilities.IsGeneratedIdentifier(left))
            {
                return Utilities.IsGeneratedIdentifier(right);
            }

            // entity names
            if (NodeTests.IsIdentifier(left))
            {
                return NodeTests.IsIdentifier(right)
                    && ((Identifier)left).EscapedText == ((Identifier)right).EscapedText;
            }

            if (NodeTests.IsPropertyAccessExpression(left))
            {
                return NodeTests.IsPropertyAccessExpression(right)
                    && EquateSerializedTypeNodes(
                        ((PropertyAccessExpression)left).Expression!, ((PropertyAccessExpression)right).Expression!)
                    && EquateSerializedTypeNodes(
                        ((PropertyAccessExpression)left).Name!, ((PropertyAccessExpression)right).Name!);
            }

            // `void 0`
            if (NodeTests.IsVoidExpression(left))
            {
                return NodeTests.IsVoidExpression(right)
                    && NodeTests.IsNumericLiteral(((VoidExpression)left).Expression!)
                    && ((NumericLiteral)((VoidExpression)left).Expression!).Text == "0"
                    && NodeTests.IsNumericLiteral(((VoidExpression)right).Expression!)
                    && ((NumericLiteral)((VoidExpression)right).Expression!).Text == "0";
            }

            // `"undefined"` or `"function"` in `typeof` checks
            if (NodeTests.IsStringLiteral(left))
            {
                return NodeTests.IsStringLiteral(right)
                    && ((StringLiteral)left).Text == ((StringLiteral)right).Text;
            }

            // used in `typeof` checks for fallback
            if (NodeTests.IsTypeOfExpression(left))
            {
                return NodeTests.IsTypeOfExpression(right)
                    && EquateSerializedTypeNodes(
                        ((TypeOfExpression)left).Expression!, ((TypeOfExpression)right).Expression!);
            }

            // parens in `typeof` checks with temps
            if (NodeTests.IsParenthesizedExpression(left))
            {
                return NodeTests.IsParenthesizedExpression(right)
                    && EquateSerializedTypeNodes(
                        ((ParenthesizedExpression)left).Expression!, ((ParenthesizedExpression)right).Expression!);
            }

            // conditionals used in fallback
            if (NodeTests.IsConditionalExpression(left))
            {
                return NodeTests.IsConditionalExpression(right)
                    && EquateSerializedTypeNodes(
                        ((ConditionalExpression)left).Condition!, ((ConditionalExpression)right).Condition!)
                    && EquateSerializedTypeNodes(
                        ((ConditionalExpression)left).WhenTrue!, ((ConditionalExpression)right).WhenTrue!)
                    && EquateSerializedTypeNodes(
                        ((ConditionalExpression)left).WhenFalse!, ((ConditionalExpression)right).WhenFalse!);
            }

            // logical binary and assignments used in fallback
            if (NodeTests.IsBinaryExpression(left))
            {
                return NodeTests.IsBinaryExpression(right)
                    && ((BinaryExpression)left).OperatorToken!.Kind == ((BinaryExpression)right).OperatorToken!.Kind
                    && EquateSerializedTypeNodes(
                        ((BinaryExpression)left).Left!, ((BinaryExpression)right).Left!)
                    && EquateSerializedTypeNodes(
                        ((BinaryExpression)left).Right!, ((BinaryExpression)right).Right!);
            }

            return false;
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:490-545 — <c>serializeTypeReferenceNode</c>.</summary>
        /// <remarks>
        /// <para>
        /// Serializes a <c>TypeReferenceNode</c> to an appropriate JS constructor value for use with decorator type
        /// metadata.
        /// </para>
        /// <para>
        /// PORT-NOTE: <c>currentNameScope ?? currentLexicalScope</c> is the resolver call's location; the port casts the
        /// name scope to <see cref="Node"/> for the coalescing because the reference's two operands are one union, and
        /// every implementor of <see cref="IClassLikeDeclarationBase"/> is a node. The <c>Unknown</c> arm's
        /// <c>findAncestor</c> predicate is <see cref="Utilities.FindAncestor(Node?, Func{Node, bool})"/> with the same
        /// <c>trueType === n || falseType === n</c> identity tests. <c>Debug.assertNever(kind)</c> is
        /// <see cref="Debug.Fail{T}"/> carrying the message it builds (see the file header).
        /// </para>
        /// </remarks>
        private Expression SerializeTypeReferenceNode(TypeReferenceNode node)
        {
            var kind = _resolver.GetTypeReferenceSerializationKind(
                node.TypeName, (Node?)_currentNameScope ?? _currentLexicalScope);
            switch (kind)
            {
                case TypeReferenceSerializationKind.Unknown:
                    // From conditional type type reference that cannot be resolved is Similar to any or unknown
                    if (Utilities.FindAncestor(node, n => n.Parent is not null
                            && NodeTests.IsConditionalTypeNode(n.Parent)
                            && (ReferenceEquals(((ConditionalTypeNode)n.Parent).TrueType, n)
                                || ReferenceEquals(((ConditionalTypeNode)n.Parent).FalseType, n))) is not null)
                    {
                        return _factory.CreateIdentifier("Object");
                    }

                    var serialized = SerializeEntityNameAsExpressionFallback(node.TypeName!);
                    var temp = _factory.CreateTempVariable(_hoistVariableDeclaration, null);
                    return _factory.CreateConditionalExpression(
                        _factory.CreateTypeCheck(_factory.CreateAssignment(temp, serialized), "function"),
                        /*questionToken*/ null,
                        temp,
                        /*colonToken*/ null,
                        _factory.CreateIdentifier("Object"));

                case TypeReferenceSerializationKind.TypeWithConstructSignatureAndValue:
                    return SerializeEntityNameAsExpression(node.TypeName!);

                case TypeReferenceSerializationKind.VoidNullableOrNeverType:
                    return _factory.CreateVoidZero();

                case TypeReferenceSerializationKind.BigIntLikeType:
                    return GetGlobalConstructor("BigInt", ScriptTarget.ES2020);

                case TypeReferenceSerializationKind.BooleanType:
                    return _factory.CreateIdentifier("Boolean");

                case TypeReferenceSerializationKind.NumberLikeType:
                    return _factory.CreateIdentifier("Number");

                case TypeReferenceSerializationKind.StringLikeType:
                    return _factory.CreateIdentifier("String");

                case TypeReferenceSerializationKind.ArrayLikeType:
                    return _factory.CreateIdentifier("Array");

                case TypeReferenceSerializationKind.ESSymbolType:
                    return GetGlobalConstructor("Symbol", ScriptTarget.ES2015);

                case TypeReferenceSerializationKind.TypeWithCallSignature:
                    return _factory.CreateIdentifier("Function");

                case TypeReferenceSerializationKind.Promise:
                    return _factory.CreateIdentifier("Promise");

                case TypeReferenceSerializationKind.ObjectType:
                    return _factory.CreateIdentifier("Object");

                default:
                    return Debug.Fail<Expression>($"Illegal value: {kind}");
            }
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:558-563 — <c>createCheckedValue</c>.</summary>
        /// <remarks>
        /// Produces an expression that results in <c>right</c> if <c>left</c> is not undefined at runtime:
        /// <code>
        /// typeof left !== "undefined" &amp;&amp; right
        /// </code>
        /// We use <c>typeof L !== "undefined"</c> (rather than <c>L !== undefined</c>) since <c>L</c> may not be
        /// declared. It's acceptable for this expression to result in <c>false</c> at runtime, as the result is intended
        /// to be further checked by any containing expression.
        /// </remarks>
        private BinaryExpression CreateCheckedValue(Expression left, Expression right)
            => _factory.CreateLogicalAnd(
                _factory.CreateStrictInequality(
                    _factory.CreateTypeOfExpression(left), _factory.CreateStringLiteral("undefined", null, null)),
                right);

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:569-589 — <c>serializeEntityNameAsExpressionFallback</c>.</summary>
        /// <remarks>
        /// Serializes an entity name which may not exist at runtime, but whose access shouldn't throw.
        /// <para>
        /// PORT-NOTE: <c>node.left</c> is the <c>EntityName</c> union's slot, which the model spells <see cref="Node"/>;
        /// the three arms narrow it to <see cref="QualifiedName"/> exactly where the reference's <c>node.left</c> is read
        /// (the first arm's own kind test is what licenses the cast in the second). <c>left.left</c>/<c>left.right</c>
        /// are the <see cref="BinaryExpression"/> slots this member's recursive arm returns.
        /// </para>
        /// </remarks>
        private BinaryExpression SerializeEntityNameAsExpressionFallback(Node node)
        {
            if (node.Kind == SyntaxKind.Identifier)
            {
                // A -> typeof A !== "undefined" && A
                var copied = SerializeEntityNameAsExpression(node);
                return CreateCheckedValue(copied, copied);
            }

            if (((QualifiedName)node).Left!.Kind == SyntaxKind.Identifier)
            {
                // A.B -> typeof A !== "undefined" && A.B
                return CreateCheckedValue(
                    SerializeEntityNameAsExpression(((QualifiedName)node).Left!),
                    SerializeEntityNameAsExpression(node));
            }

            // A.B.C -> typeof A !== "undefined" && (_a = A.B) !== void 0 && _a.C
            var left = SerializeEntityNameAsExpressionFallback(((QualifiedName)node).Left!);
            var temp = _factory.CreateTempVariable(_hoistVariableDeclaration, null);
            return _factory.CreateLogicalAnd(
                _factory.CreateLogicalAnd(
                    left.Left!,
                    _factory.CreateStrictInequality(
                        _factory.CreateAssignment(temp, left.Right), _factory.CreateVoidZero())),
                _factory.CreatePropertyAccessExpression(temp, ((QualifiedName)node).Right));
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:595-608 — <c>serializeEntityNameAsExpression</c>.</summary>
        /// <remarks>
        /// Serializes an entity name as an expression for decorator type metadata.
        /// <para>
        /// PORT-NOTE: <c>name.original = undefined</c> detaches the clone from its original so
        /// <c>getParseTreeNode</c> treats it as a parse-tree node; the model's slot is nullable, so the cut is
        /// <c>Original = null</c> — the same "no original" the walk in <see cref="Utilities.GetParseTreeNode(Node?, Func{Node, bool}?)"/>
        /// tests for. TS's <c>switch</c> falls out of the function with <c>undefined</c> for any other kind; the port's
        /// <c>EntityName</c> union is exactly these two kinds, so the fall-through is the impossible-state failure.
        /// </para>
        /// </remarks>
        private MemberExpression SerializeEntityNameAsExpression(Node node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.Identifier:
                    {
                        // Create a clone of the name with a new parent, and treat it as if it were
                        // a source tree node for the purposes of the checker.
                        var name = Utilities.SetParent(
                            Utilities.SetTextRange(FactoryUtilities.ParseNodeFactory.CloneNode((Identifier)node), node),
                            node.Parent)!;
                        name.Original = null;
                        // ensure the parent is set to a parse tree node.
                        Utilities.SetParent(name, Utilities.GetParseTreeNode(_currentLexicalScope));
                        return name;
                    }

                case SyntaxKind.QualifiedName:
                    return SerializeQualifiedNameAsExpression((QualifiedName)node);
            }

            return Debug.Fail<MemberExpression>(
                $"Unexpected node.\r\nNode {Debug.FormatSyntaxKind(node.Kind)} was unexpected.");
        }

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:614-616 — <c>serializeQualifiedNameAsExpression</c>.</summary>
        /// <remarks>
        /// Serializes an qualified name as an expression for decorator type metadata.
        /// <para>
        /// PORT-NOTE: the reference casts the property access to <c>PropertyAccessEntityNameExpression</c>; the model's
        /// factory answers <see cref="PropertyAccessExpression"/> already, which is the same node (see the file header).
        /// </para>
        /// </remarks>
        private MemberExpression SerializeQualifiedNameAsExpression(QualifiedName node)
            => _factory.CreatePropertyAccessExpression(SerializeEntityNameAsExpression(node.Left!), node.Right);

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:618-626 — <c>getGlobalConstructorWithFallback</c>.</summary>
        private ConditionalExpression GetGlobalConstructorWithFallback(string name)
            => _factory.CreateConditionalExpression(
                _factory.CreateTypeCheck(_factory.CreateIdentifier(name), "function"),
                /*questionToken*/ null,
                _factory.CreateIdentifier(name),
                /*colonToken*/ null,
                _factory.CreateIdentifier("Object"));

        /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:628-632 — <c>getGlobalConstructor</c>.</summary>
        private Expression GetGlobalConstructor(string name, ScriptTarget minLanguageVersion)
            => _languageVersion < minLanguageVersion
                ? GetGlobalConstructorWithFallback(name)
                : _factory.CreateIdentifier(name);

        /// <summary>ported from: src/compiler/utilities.ts:6832-6885 — <c>getAllAccessorDeclarations</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: this is the reference body again, private to this class (see the file header); TS's first parameter
        /// is <c>readonly Declaration[] | undefined</c> and its answer the <c>AllAccessorDeclarations</c> interface, which
        /// the model carries as <see cref="IAllAccessorDeclarations"/> — the port answers the same four slots as a tuple
        /// because both callers destructure them, which is the shape
        /// <c>TransformationUtilities.GetAllAccessorDeclarations</c> states for the same reference.
        /// </remarks>
        private static (
            Node? FirstAccessor,
            Node? SecondAccessor,
            GetAccessorDeclaration? GetAccessor,
            SetAccessorDeclaration? SetAccessor)
            GetAllAccessorDeclarations(IEnumerable<IClassElement>? declarations, IFunctionLikeDeclarationBase accessor)
        {
            Node? firstAccessor = null;
            Node? secondAccessor = null;
            GetAccessorDeclaration? getAccessor = null;
            SetAccessorDeclaration? setAccessor = null;
            if (Utilities.HasDynamicName((Node)accessor))
            {
                firstAccessor = (Node)accessor;
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
                    if (!Utilities.IsAccessor(m) || Utilities.IsStatic(m) != Utilities.IsStatic((Node)accessor))
                    {
                        continue;
                    }

                    var memberName = Utilities.GetPropertyNameForPropertyNameNode(((INamedDeclaration)member).Name!);
                    var accessorName = Utilities.GetPropertyNameForPropertyNameNode(accessor.Name!);
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

        /// <summary>ported from: src/compiler/utilities.ts:6776-6779 — <c>getSetAccessorTypeAnnotationNode</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: not landed in <c>Utilities</c>; this is the reference body (see the file header), reading the
        /// value parameter through <see cref="Utilities.GetSetAccessorValueParameter"/> — which is landed — and its
        /// <c>type</c> slot, which the model carries on the <see cref="ParameterDeclaration"/> class.
        /// </remarks>
        private static ITypeNode? GetSetAccessorTypeAnnotationNode(SetAccessorDeclaration accessor)
        {
            var parameter = Utilities.GetSetAccessorValueParameter(accessor);
            return parameter?.Type;
        }
    }

    /// <summary>ported from: src/compiler/transformers/typeSerializer.ts:132-151 — <c>createRuntimeTypeSerializer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's answer is the object literal whose four members the constructor above records; the
    /// reference's destructuring of <c>context</c> is that constructor's head, so this member only builds it.
    /// </remarks>
    public static RuntimeTypeSerializer CreateRuntimeTypeSerializer(ITransformationUtilitiesContext context)
        => new(context);
}
