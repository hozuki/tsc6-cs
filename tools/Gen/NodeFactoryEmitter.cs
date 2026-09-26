using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// Generates the fixed-shape half of the node factory: <c>Parser/NodeFactory.Generated.cs</c>.
/// </summary>
/// <remarks>
/// Porting decision D2 applied to code rather than to tables. Of the 428 <c>create*</c>/<c>update*</c> members in
/// <c>factory/nodeFactory.ts</c>, 295 have one of two fixed bodies — a <c>createBase*</c> call followed by field
/// assignments, or a single comparison chain forwarding to <c>update(createX(…), node)</c> — so they are generated.
/// Everything else (the helpers, the members with real logic, the module-level functions) stays hand-written.
/// <para>
/// The generated type is <c>partial class NodeFactory</c>, whose other half is <c>Parser/NodeFactory.cs</c>
/// (§9 item 8). A member the extractor could not describe, or whose expressions have no translation, is left to that
/// hand-written half and named in this file's header — the same "fail rather than emit something half-right" rule the
/// table extractors follow. See docs/architecture.md §15.7.
/// </para>
/// </remarks>
internal sealed class NodeFactoryEmitter
{
    private readonly TsModel _model;

    private readonly TsTypeMapper _mapper;

    /// <summary>The <c>factory/nodeTests.ts</c> predicate names, which a generated member calls through their class.</summary>
    private readonly HashSet<string> _predicates;

    /// <summary>The class each concrete kind is built as, keyed by the kind's <em>value</em>.</summary>
    /// <remarks>
    /// The <c>createBase*</c> call in TS names the <em>static</em> type it wants the object seen as, and that is not
    /// always the runtime class: <c>createNonNullChain</c> builds
    /// <c>createBaseNode&lt;NonNullChain&gt;(SyntaxKind.NonNullExpression)</c>, where <c>NonNullChain</c> is the same
    /// node carrying the <c>OptionalChain</c> flag. C# has one class per kind, so the constructor — and therefore this
    /// table — decides which class an instance actually is (docs/architecture.md §15.7).
    /// <para>
    /// Keyed by value rather than by name because `SyntaxKind` has aliases: <c>SyntaxKind.AssertClause</c> is the same
    /// value as <c>SyntaxKind.ImportAttributes</c>, and only the latter has a class.
    /// </para>
    /// </remarks>
    private readonly Dictionary<long, string> _classByKind;

    /// <summary>Each <c>SyntaxKind</c> member's value, aliases included.</summary>
    private readonly Dictionary<string, long> _kindValues;

    /// <summary>The <c>SyntaxKind</c> members <c>TypedEnum</c> has a nested type for (§16).</summary>
    private readonly HashSet<string> _typedEnumMembers;

    /// <summary>The parameters of the member being emitted that are typed enum slots and therefore read through it.</summary>
    private HashSet<string> _currentTypedEnumParameters = [];

    private NodeFactoryEmitter(TsModel model, TsTypeMapper mapper)
    {
        _model = model;
        _mapper = mapper;
        _predicates = new HashSet<string>(model.Predicates.Select(static predicate => predicate.Name), StringComparer.Ordinal);
        _kindValues = model.Enums.FirstOrDefault(static tsEnum => tsEnum.Name == "SyntaxKind") is { } syntaxKind
            ? syntaxKind.Members.ToDictionary(static member => member.Name, static member => member.Value, StringComparer.Ordinal)
            : new Dictionary<string, long>(StringComparer.Ordinal);
        _classByKind = model.Interfaces
            .Where(static iface => iface.ConcreteKind is not null && iface.ConcreteKind.Length != 0)
            .Where(iface => _kindValues.ContainsKey(iface.ConcreteKind!))
            .GroupBy(iface => _kindValues[iface.ConcreteKind!])
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static iface => iface.Name).OrderBy(static name => name, StringComparer.Ordinal).First());

        _typedEnumMembers = new HashSet<string>(TypedEnumEmitter.MemberNames(model), StringComparer.Ordinal);
    }

    /// <summary>The enum member a parameter type names (<c>SyntaxKind.CommaToken</c> → <c>CommaToken</c>), if any.</summary>
    private string? TypedEnumMemberOf(string tsType)
    {
        var text = tsType.Trim();
        if (!text.StartsWith("SyntaxKind.", StringComparison.Ordinal)) return null;

        var name = text["SyntaxKind.".Length..].Trim();
        return _typedEnumMembers.Contains(name) ? name : null;
    }

    public static GeneratedFile Emit(TsModel model, TsTypeMapper mapper, string outputRoot)
        => new NodeFactoryEmitter(model, mapper).EmitCore(outputRoot);

    private GeneratedFile EmitCore(string outputRoot)
    {
        var refused = new List<string>();
        var blocks = new List<string>();
        var generated = new HashSet<string>(StringComparer.Ordinal);

        // A member the port has not reached may still be *called* by one that has: an `update*` forwards to its
        // `create*`, and the stub answers `object?` where the `Update<T>` call needs a node. Such a member joins the
        // stubs too, and the pass repeats until nothing new is pulled in.
        var stubbed = new HashSet<string>(StringComparer.Ordinal);

        for (var pass = 0; pass < 8; pass++)
        {
            refused.Clear();
            var produced = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var before = stubbed.Count;

            foreach (var member in _model.Factory.Members)
            {
                if (!member.Supported || s_implemented.Contains(member.Name) || stubbed.Contains(member.Name)) continue;

                if (member.Kind == "update"
                    && member.UpdateCreate?.Name is { } createName
                    && !s_implemented.Contains(createName)
                    && !names.Contains(createName))
                {
                    refused.Add($"{member.Name}: (stub) its `create*` is not available yet");
                    stubbed.Add(member.Name);
                    continue;
                }

                var code = new StringBuilder();
                try
                {
                    if (member.Kind == "create") AppendCreate(code, member, _mapper, _model.Factory.Source);
                    else AppendUpdate(code, member, _mapper, _model.Factory.Source);

                    produced[member.Name] = code.ToString();
                    names.Add(member.Name);
                }
                catch (Untranslatable error)
                {
                    refused.Add($"{member.Name}: {error.Message}");
                    stubbed.Add(member.Name);
                }
            }

            blocks = produced.Values.ToList();
            generated = names;

            // The pass settled when it pulled nothing new into the stubs.
            if (stubbed.Count == before) break;
        }

        // Everything the generator cannot emit — a body with no translation, or a shape it does not recognise — is
        // stubbed until the hand-written half provides it, so the partial class always compiles (see s_implemented).
        var stubs = new List<string>();
        foreach (var member in _model.Factory.Members)
        {
            if (generated.Contains(member.Name) || s_implemented.Contains(member.Name)) continue;

            var code = new StringBuilder();
            try
            {
                AppendStub(code, member, _mapper, _model.Factory.Source);
                stubs.Add(code.ToString());
            }
            catch (Untranslatable error)
            {
                refused.Add($"{member.Name}: (stub) {error.Message}");
            }
        }

        var sb = new StringBuilder();
        AppendHeader(sb, _model.Factory, refused);
        foreach (var block in blocks) sb.Append(block);

        if (stubs.Count != 0)
        {
            sb.AppendLine("    //");
            sb.AppendLine("    // Stubs — the members the hand-written half does not provide yet (§15.7).");
            sb.AppendLine("    //");
            sb.AppendLine("    // Each throws, so a caller that reaches one fails loudly rather than seeing a half-built node.");
            sb.AppendLine();
            foreach (var stub in stubs) sb.Append(stub);
        }

        sb.AppendLine("}");

        return new GeneratedFile($"{outputRoot}/Parser/NodeFactory.Generated.cs", sb.ToString().Replace("\r\n", "\n"));
    }

    /// <summary>A member the port has not reached: the generated signature, and a body that throws.</summary>
    /// <remarks>
    /// The signature comes from the same mapping the generated members use, so the stub satisfies every call site the
    /// generator emitted and the hand-written half can replace it member by member.
    /// </remarks>
    private void AppendStub(StringBuilder sb, TsFactoryMember member, TsTypeMapper mapper, string source)
    {
        var summary = $"    /// <summary>ported from: {source} — <c>{member.Name}</c>.</summary>";
        sb.AppendLine(summary);
        sb.AppendLine("    /// <remarks>PORT-NOTE: stub — tools/Gen generates it because the hand-written half does not provide");
        sb.AppendLine("    /// it yet (docs/architecture.md §15.7).</remarks>");

        AppendSignature(sb, member, mapper, source, withSummary: false);
        sb.AppendLine("    {");
        sb.AppendLine($"        throw new NotImplementedException(\"the port has not reached `{member.Name}` yet\");");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendHeader(StringBuilder sb, TsFactory factory, List<string> refused)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//   Generated from {factory.Source} by tools/Gen — DO NOT EDIT.");
        sb.AppendLine("//   Regenerate with:");
        sb.AppendLine("//     dotnet run --project tools/Gen -- --out src/TypeScript.Compiler");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("// The fixed-shape half of the node factory (docs/architecture.md §15.7): a `createBase*` call plus field");
        sb.AppendLine("// assignments, or a single comparison chain forwarding to `update`. The other half of this partial class is");
        sb.AppendLine("// the hand-written Parser/NodeFactory.cs.");
        sb.AppendLine();

        var handWritten = factory.Members.Where(static member => !member.Supported).Select(static member => member.Name).ToList();
        sb.AppendLine($"// {handWritten.Count} members are not a fixed shape in the TS source, so they are hand-written:");
        foreach (var chunk in Chunk(handWritten, 6)) sb.AppendLine("//   " + string.Join(", ", chunk));
        sb.AppendLine();

        if (refused.Count != 0)
        {
            sb.AppendLine($"// {refused.Count} members have a fixed shape but an expression with no translation, so they are");
            sb.AppendLine("// hand-written as well:");
            foreach (var entry in refused) sb.AppendLine($"//   {entry}");
            sb.AppendLine();
        }

        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        // The instance property `Flags` shadows the `Core/Flags.cs` helper inside this type, exactly as it does in the
        // hand-written half of the partial class.
        sb.AppendLine("using CoreFlags = TypeScript.Compiler.Flags;");
        sb.AppendLine();
        sb.AppendLine("namespace TypeScript.Compiler;");
        sb.AppendLine();
        sb.AppendLine("internal partial class NodeFactory");
        sb.AppendLine("{");
    }

    /// <summary>Members whose fixed shape still needs a C# conversion only the hand-written half can spell.</summary>
    private static readonly HashSet<string> s_handWritten = new(StringComparer.Ordinal)
    {
        // `NodeArray<TypeNode>` does not convert to a `NodeArray<Node>` slot: C# generics are invariant, while TS's
        // `TypeNode` is a subtype of both `Node` and the `NamedTupleMember` the target slot also allows.
        "createTupleTypeNode",

        // `node.exclamationToken ?? node.type` reads two unrelated node types through one operator, so the result has
        // no C# type the generated shape could name.
        "createVariableDeclaration",

        // `node.dotDotDotToken` is a `Token` where `createJsxExpression` takes a `DotDotDotToken`. TS accepts a wider
        // value for a narrower parameter in the places its types overlap; C# does not.
        "updateJsxExpression",
    };

    /// <summary>Helpers the factory can hand over as a method group rather than call.</summary>
    /// <remarks>
    /// <c>sameFlatMap(elements, flattenCommaElements)</c> passes the helper itself, so the name has to be translated at
    /// a reference as well as at a call.
    /// </remarks>
    private static readonly HashSet<string> s_helpers = new(StringComparer.Ordinal)
    {
        "asName",
        "asNodeArray",
        "asExpression",
        "asToken",
        "asEmbeddedStatement",
        "asVariableDeclaration",
        "asInitializer",
        "createNodeArray",
        "flattenCommaElements",
        "setTextRange",
        "sameFlatMap",
        "propagateChildrenFlags",
        "propagateChildFlags",
    };

    /// <summary>The factory members <c>Parser/NodeFactory.cs</c> already provides.</summary>
    /// <remarks>
    /// The generator cannot see the hand-written half of the partial class, so this list is how it knows a member is
    /// done: everything else is emitted as a stub that throws, which keeps the generated half compiling while the port
    /// is in progress. Implementing a member means adding its name here and re-running the generator, which removes the
    /// stub (docs/architecture.md §15.7).
    /// </remarks>
    private static readonly HashSet<string> s_implemented = new(StringComparer.Ordinal)
    {
        "createBaseNode",
        "createBaseDeclaration",
        "createBaseToken",
        "createToken",
        "createNodeArray",
        "createNumericLiteral",
        "createBigIntLiteral",
        "createStringLiteral",
        "createStringLiteralFromNode",
        "createLiteralLikeNode",
        "createTemplateLiteralLikeNode",
        "createTemplateLiteralLikeToken",
        "createTemplateHead",
        "createTemplateMiddle",
        "createTemplateTail",
        "createNoSubstitutionTemplateLiteral",
        "createObjectBindingPattern",
        "updateObjectBindingPattern",
        "createArrayLiteralExpression",
        "updateArrayLiteralExpression",
        "createPropertyAccessExpression",
        "updatePropertyAccessExpression",
        "createPropertyAccessChain",
        "updatePropertyAccessChain",
        "createElementAccessExpression",
        "updateElementAccessExpression",
        "createElementAccessChain",
        "updateElementAccessChain",
        "createCallExpression",
        "updateCallExpression",
        "createBaseCallExpression",
        "createCallChain",
        "updateCallChain",
        "createVariableStatement",
        "updateVariableStatement",
        "createVariableDeclarationList",
        "updateVariableDeclarationList",
        "createImportEqualsDeclaration",
        "updateImportEqualsDeclaration",
        "createImportDeclaration",
        "updateImportDeclaration",
        "createImportClause",
        "updateImportClause",
        "createExportDeclaration",
        "updateExportDeclaration",
        "createKeywordTypeNode",
        "createUnionOrIntersectionTypeNode",
        "updateUnionOrIntersectionTypeNode",
        "createUnionTypeNode",
        "updateUnionTypeNode",
        "createIntersectionTypeNode",
        "updateIntersectionTypeNode",
        "createTupleTypeNode",
        "updateTupleTypeNode",
        "updateFunctionTypeNode",
        "createConstructorTypeNode1",
        "createConstructorTypeNode2",
        "updateConstructorTypeNode1",
        "updateConstructorTypeNode2",
        "createImportTypeNode",
        "updateImportTypeNode",
        "createNewExpression",
        "updateNewExpression",
        "createTaggedTemplateExpression",
        "updateTaggedTemplateExpression",
        "createPrefixUnaryExpression",
        "updatePrefixUnaryExpression",
        "createPostfixUnaryExpression",
        "updatePostfixUnaryExpression",
        "createConditionalExpression",
        "updateConditionalExpression",
        "createVariableDeclaration",
        "updateVariableDeclaration",
        "createSyntheticExpression",
        "createConstructorDeclaration",
        "updateConstructorDeclaration",
        "createGetAccessorDeclaration",
        "updateGetAccessorDeclaration",
        "createSetAccessorDeclaration",
        "updateSetAccessorDeclaration",
        "updateCallSignature",
        "updateConstructSignature",
        "updateIndexSignature",
        "createYieldExpression",
        "createBaseIdentifier",
        "createIdentifier",
        "createBasePrivateIdentifier",
        "createPrivateIdentifier",
        "createSuper",
        "createThis",
        "createNull",
        "createTrue",
        "createFalse",
        "createModifier",
        "createModifiersFromModifierFlags",
        "createMetaProperty",
        "updateNonNullExpression",
        "updateNonNullChain",
        "createParameterDeclaration",
        "updatePropertySignature",
        "createPropertyDeclaration",
        "updateMethodSignature",
        "createMethodDeclaration",
        "updateMethodDeclaration",
        "updateClassStaticBlockDeclaration",
        "update",
        "createOmittedExpression",
        "createNotEmittedTypeElement",
        "createVoidZero",
        "createExportDefault",
        "createExternalModuleExport",
    };

    /// <summary>Refuses a member whose translation would lose a type the call site depends on.</summary>
    /// <remarks>
    /// <c>createToken</c> is the case: TS resolves <c>createToken(SyntaxKind.QuestionToken)</c> to the declaration
    /// constrained to <c>PunctuationSyntaxKind</c> and gets a <c>PunctuationToken</c> back. The port's wide signature
    /// takes a <c>SyntaxKind</c> and answers <c>Node</c> (§16), so a member that consumes the result as a narrower type
    /// has to say which one it wants — which is what the hand-written half does.
    /// </remarks>
    private void RefuseUnrepresentable(TsFactoryMember member)
    {
        if (s_handWritten.Contains(member.Name))
        {
            throw new Untranslatable("listed as needing the hand-written half (see the emitter's s_handWritten)");
        }

        // A generic member reaches its type argument's members through TS's constraint, which C# only carries when the
        // constraint is a type the port models; the rest would have to be re-derived at each use.
        if (member.TypeParameters.Count != 0 && member.TypeParameters.Any(parameter => parameter.Constraint is not null))
        {
            throw new Untranslatable("declares a constrained type parameter");
        }

        // `any` is TS's way of saying "whatever the caller has"; the field it is assigned to does have a type, so the
        // two do not meet in C#.
        if (member.Parameters.Any(static parameter => parameter.Type.Trim() == "any"))
        {
            throw new Untranslatable("takes `any`");
        }

        if (HasUnresolvedIndexedAccess(member))
        {
            throw new Untranslatable("takes an indexed access type whose owner is not in the model");
        }

        if (member.Calls.Any(call => ContainsCall(call, "createToken"))
            || member.Assignments.Any(assignment =>
                ContainsCall(assignment.Value, "createToken") || ContainsCall(assignment.Condition, "createToken"))
            || member.Compares.Any(compare => ContainsCall(compare.Value, "createToken"))
            || ContainsCall(member.UpdateCreate, "createToken"))
        {
            throw new Untranslatable("calls `createToken`, whose C# result type is wider than the one TS infers");
        }
    }

    /// <summary>Whether a parameter is indexed on something the model does not know, so it maps to <c>object?</c>.</summary>
    private bool HasUnresolvedIndexedAccess(TsFactoryMember member)
        => member.Parameters.Any(parameter =>
        {
            var type = parameter.Type.Trim();
            var open = type.IndexOf("[\"", StringComparison.Ordinal);
            if (open <= 0 || !type.EndsWith("\"]", StringComparison.Ordinal)) return false;

            return !_model.Interfaces.Any(iface => iface.Name == type[..open].Trim());
        });

    /// <summary>Whether an expression contains a call to <paramref name="name"/> anywhere inside it.</summary>
    private static bool ContainsCall(TsExpression? expression, string name)
    {
        if (expression is null) return false;
        if (expression.Form == "call" && expression.Name == name) return true;

        return ContainsCall(expression.Target, name)
            || ContainsCall(expression.Left, name)
            || ContainsCall(expression.Right, name)
            || ContainsCall(expression.Operand, name)
            || ContainsCall(expression.Inner, name)
            || ContainsCall(expression.Condition, name)
            || ContainsCall(expression.WhenTrue, name)
            || ContainsCall(expression.WhenFalse, name)
            || expression.Args.Any(argument => ContainsCall(argument, name))
            || expression.Items.Any(item => ContainsCall(item, name));
    }

    /// <summary>The `kind` argument of the <c>createBase*</c> call.</summary>
    private string KindArgumentOf(TsFactoryMember member)
    {
        var expression = Required(member.KindExpression, "kind");

        // §16: a member that takes its kind as a parameter (`createTemplateLiteralLikeDeclaration(kind: …)`) receives a
        // `TypedEnum` instance, and the constructor wants the enum itself.
        return _currentTypedEnumParameters.Contains(expression) ? $"{expression}!.SyntaxKind" : expression;
    }

    /// <summary>ported from a fixed-shape <c>create*</c> member of <c>factory/nodeFactory.ts</c>.</summary>
    private void AppendCreate(StringBuilder sb, TsFactoryMember member, TsTypeMapper mapper, string source)
    {
        RefuseUnrepresentable(member);

        if (Required(member.Tail, "tail") != "node") throw new Untranslatable($"the body returns `update(…)`");

        var baseName = Required(member.Base, "base") switch
        {
            "createBaseNode" => "CreateBaseNode",
            "createBaseDeclaration" => "CreateBaseDeclaration",
            "createBaseToken" => "CreateBaseToken",
            var other => throw new Untranslatable($"unknown base `{other}`"),
        };

        var typeArgument = TypeArgumentOf(member);

        AppendSignature(sb, member, mapper, source);
        sb.AppendLine("    {");
        sb.AppendLine($"        var node = {baseName}<{typeArgument}>({KindArgumentOf(member)});");

        foreach (var assignment in member.Assignments)
        {
            var line = $"node.{PascalCase(assignment.Target)} {assignment.Op} {Translate(Required(assignment.Value))};";
            sb.AppendLine(assignment.Condition is { } condition
                ? $"        if ({Truthy(condition)}) {line}"
                : $"        {line}");
        }

        foreach (var call in member.Calls) sb.AppendLine($"        {TranslateCall(call)};");

        sb.AppendLine("        return node;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>The class the <c>createBase*</c> call instantiates.</summary>
    /// <remarks>
    /// The type argument TS writes is a static type, and the port has a class per kind rather than a class per
    /// interface: <c>createBaseNode&lt;NonNullChain&gt;</c> still builds a <c>NonNullExpression</c>. The kind in the
    /// same call is therefore the answer, and it is also the one that cannot drift — it is what the class itself pins.
    /// </remarks>
    private string TypeArgumentOf(TsFactoryMember member)
    {
        var typeArgument = Required(member.TypeArgument, "type argument");
        if (typeArgument.Contains('<')) throw new Untranslatable($"generic base type `{typeArgument}`");

        // The kind decides whenever the member names one: it cannot drift from the class, because the class is what
        // pins it. That is also what makes a *view* of a node work (`createBaseNode<NonNullChain>`).
        var kind = member.KindExpression;
        if (kind is not null && kind.StartsWith("SyntaxKind.", StringComparison.Ordinal)
            && _kindValues.TryGetValue(kind["SyntaxKind.".Length..], out var kindValue)
            && _classByKind.TryGetValue(kindValue, out var byKind))
        {
            return byKind;
        }

        // A member that computes its kind instead of naming one (`createJSDocUnaryTypeWorker(kind, …)`) leaves the type
        // argument as the only clue; it works when that lands on a class, and the member is hand-written otherwise.
        if (_mapper.IsClass(typeArgument)) return typeArgument;

        throw new Untranslatable($"no class for the kind `{member.KindExpression}` (type argument `{typeArgument}`)");
    }

    /// <summary>ported from a fixed-shape <c>update*</c> member of <c>factory/nodeFactory.ts</c>.</summary>
    /// <remarks>
    /// The comparisons and the <c>update</c> call are literal: TS compares with <c>!==</c>, which for every type a node
    /// member can have — a node, a node array, a string, an enum — is what C#'s <c>!=</c> does as well. The null
    /// forgiving operators are there because a generated parameter is declared nullable (the generated surface makes
    /// every reference-typed slot nullable), while TS treats the node as present.
    /// </remarks>
    private void AppendUpdate(StringBuilder sb, TsFactoryMember member, TsTypeMapper mapper, string source)
    {
        RefuseUnrepresentable(member);

        var create = Required(member.UpdateCreate, "update call");
        var createName = PascalCase(Required(create.Name, "create name"));
        var createArguments = string.Join(", ", create.Args.Select(argument => Translate(argument)));

        var conditions = member.Compares
            .Select(compare => $"node!.{PascalCase(compare.Target)} != {Translate(Required(compare.Value))}")
            .ToList();

        AppendSignature(sb, member, mapper, source);
        sb.AppendLine("    {");
        sb.AppendLine("        return " + string.Join(Environment.NewLine + "            || ", conditions));
        sb.AppendLine($"            ? Update({createName}({createArguments}), node)");
        sb.AppendLine("            : node!;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private void AppendSignature(
        StringBuilder sb,
        TsFactoryMember member,
        TsTypeMapper mapper,
        string source,
        bool withSummary = true)
    {
        _currentTypedEnumParameters = new HashSet<string>(
            member.Parameters
                .Where(parameter => TypedEnumMemberOf(parameter.Type) is not null)
                .Select(static parameter => parameter.Name),
            StringComparer.Ordinal);

        var returnType = ReturnType(member, mapper);
        var parameters = member.Parameters.Select(parameter =>
            $"{(parameter.IsRest ? "params " : "")}{ParameterType(member, parameter, mapper)} {ParameterName(parameter.Name)}");

        var generics = member.TypeParameters.Count == 0
            ? ""
            : $"<{string.Join(", ", member.TypeParameters.Select(static typeParameter => typeParameter.Name))}>";

        // A constraint is kept only when it names a type the port models: `T extends Node` survives, one that maps to
        // `object` does not (an inaccurate constraint fails to compile at the single place that cannot work around it).
        var constraints = new StringBuilder();
        foreach (var typeParameter in member.TypeParameters)
        {
            if (typeParameter.Constraint is null) continue;

            var constraint = mapper.MapMember(typeParameter.Constraint, false, member.TypeParameters).TrimEnd('?');
            if (constraint == "object" || mapper.IsValueType(constraint)) continue;
            constraints.Append($" where {typeParameter.Name} : {constraint}");
        }

        if (withSummary) sb.AppendLine($"    /// <summary>ported from: {source} — <c>{member.Name}</c>.</summary>");
        sb.AppendLine($"    public {returnType} {PascalCase(member.Name)}{generics}({string.Join(", ", parameters)}){constraints}");
    }

    private static string ReturnType(TsFactoryMember member, TsTypeMapper mapper)
    {
        // TS leaves the return type off its internal helpers (`createBaseStringLiteral` has none), and in that case the
        // generic argument of the `createBase*` call is what says which node is being built.
        var declared = member.ReturnType == "any" && member.TypeArgument is { } typeArgument ? typeArgument : member.ReturnType;

        return mapper.MapSlot(declared, optional: false, member.TypeParameters).Type;
    }

    private static string ParameterType(TsFactoryMember member, TsParameter parameter, TsTypeMapper mapper)
    {
        var type = mapper.MapSlot(parameter.Type, parameter.IsOptional, member.TypeParameters, parameter: true).Type;

        // `params` needs a collection; a rest parameter whose type did not map to one cannot be spelled.
        if (parameter.IsRest && !type.StartsWith("List<", StringComparison.Ordinal) && type != "object?[]")
        {
            throw new Untranslatable($"a `params` parameter of `{type}`");
        }

        // A reference-typed slot is declared nullable: generated members are filled in by the caller rather than at
        // construction (the same rule the node model follows).
        if (parameter.IsRest || type.EndsWith('?') || type == "void" || mapper.IsValueType(type)) return type;
        return type + "?";
    }

    //
    // Expressions
    //

    /// <summary>
    /// Translates an expression, optionally as a condition.
    /// </summary>
    /// <param name="boolean">
    /// True inside a position that needs a <c>bool</c> — an <c>if</c> guard, or the condition of a ternary. TS
    /// evaluates those for truthiness, and the operands have to be mapped as comparisons rather than as values.
    /// </param>
    private string Translate(TsExpression expression, bool boolean = false) => expression.Form switch
    {
        "identifier" => TranslateIdentifier(expression),
        "property" => $"{Translate(Required(expression.Target, "property target"))}.{PascalCase(Required(expression.Name))}",
        "call" => TranslateCall(expression),
        "method" => TranslateMethod(expression),
        "binary" => TranslateBinary(expression, boolean),
        "prefix" => TranslatePrefix(expression),
        "paren" => $"({Translate(Required(expression.Inner), boolean)})",
        "nonNull" => $"{Translate(Required(expression.Inner))}!",
        "conditional" => TranslateConditional(expression),
        "array" => $"[{string.Join(", ", expression.Items.Select(item => Translate(item)))}]",
        "string" => Literal(expression.Value),
        "number" => Required(expression.Value),
        "boolean" => expression.Boolean == true ? "true" : "false",
        "null" => "null",
        _ => throw new Untranslatable($"no translation for `{expression.Form}` ({expression.Text})"),
    };

    /// <summary>Translates a bare name: a parameter, or <c>undefined</c>.</summary>
    /// <remarks>
    /// §16: a parameter whose TS type is a single enum member is a <c>TypedEnum</c> instance in C#, and its kind is read
    /// through it. TS passes the member itself there, which is also a <c>SyntaxKind</c>.
    /// </remarks>
    private string TranslateIdentifier(TsExpression expression)
    {
        var name = Required(expression.Name);
        if (name == "undefined") return "null";

        // A helper passed as a method group rather than called.
        if (s_helpers.Contains(name)) return CallName(name);

        return _currentTypedEnumParameters.Contains(name)
            ? $"{ParameterName(name)}!.SyntaxKind"
            : ParameterName(name);
    }

    /// <summary>Translates <c>a &amp;&amp; b</c> / <c>a || b</c> / a comparison or an arithmetic expression.</summary>
    /// <remarks>
    /// TS's logical operators hand back one of their operands rather than a boolean, so what they mean depends on the
    /// position, and the port has to say which. As a condition both sides are conditions. As a value, every use in
    /// <c>nodeFactory.ts</c> guards an optional node or node array — <c>members &amp;&amp; createNodeArray(members)</c> —
    /// which is C#'s <c>a is null ? null : b</c>. This is the shape <c>asInitializer</c> and friends rely on, and an
    /// <c>||</c> in value position would mean something the port has no equivalent for, so it is refused instead.
    /// </remarks>
    private string TranslateBinary(TsExpression expression, bool boolean)
    {
        var op = Required(expression.Op);
        var left = Required(expression.Left);
        var right = Required(expression.Right);

        if (op is "&&" or "||")
        {
            return boolean
                ? $"({Truthy(left)} {op} {Truthy(right)})"
                : op == "&&"
                    ? $"({Translate(left)} is null ? null : {Translate(right)})"
                    : throw new Untranslatable("no translation for `||` outside a condition");
        }

        return $"({Translate(left)} {Operator(op)} {Translate(right)})";
    }

    private string TranslateConditional(TsExpression expression)
        => $"({Truthy(Required(expression.Condition))} ? {Translate(Required(expression.WhenTrue))} : {Translate(Required(expression.WhenFalse))})";

    /// <summary>Maps an operand TS evaluates for truthiness.</summary>
    /// <remarks>
    /// C# has no truthiness, so a value that is not already a boolean goes through <see cref="Js.IsTruthy"/> — the same
    /// rule the hand-written half follows (§10.2). A comparison or a <c>!</c> is left alone: it already is the answer,
    /// and wrapping it would add a conversion the TS source does not have.
    /// </remarks>
    private string Truthy(TsExpression expression)
        => IsBooleanForm(expression)
            ? Translate(expression, boolean: true)
            : $"Js.IsTruthy({Translate(expression, boolean: true)})";

    private static bool IsBooleanForm(TsExpression expression) => expression.Form switch
    {
        "boolean" => true,
        "prefix" => expression.Op == "!",
        "binary" => expression.Op is "===" or "!==" or "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||",
        _ => false,
    };

    private string TranslateCall(TsExpression expression)
    {
        var name = Required(expression.Name);
        var arguments = expression.Args.Select(argument => Translate(argument)).ToList();

        // TS's `createNodeArray(elements)` has an optional second parameter, and C# needs it spelled out.
        if (name == "createNodeArray" && arguments.Count == 1) arguments.Add("null");

        // The `factory/nodeTests.ts` predicates take a node TS knows is present, and a member the port declares nullable
        // can still be handed to one.
        if (_predicates.Contains(name) && arguments.Count == 1) arguments[0] += "!";

        return $"{CallName(name)}({string.Join(", ", arguments)})";
    }

    /// <summary>
    /// The C# member a TS helper maps to. Anything not listed keeps its name with the first letter capitalised, which
    /// is what all the other <c>camelCase</c> helpers need.
    /// </summary>
    private string CallName(string tsName)
    {
        // `factory/nodeTests.ts` is generated into its own class, and TS reaches its predicates from module scope.
        if (_predicates.Contains(tsName)) return "NodeTests." + PascalCase(tsName);

        return tsName switch
        {
            "asName" => "AsName",
            "asNodeArray" => "AsNodeArray",
            "asExpression" => "AsExpression",
            "asToken" => "AsToken",
            "asEmbeddedStatement" => "AsEmbeddedStatement",
            "asVariableDeclaration" => "AsVariableDeclaration",
            "asInitializer" => "AsInitializer",
            "createNodeArray" => "CreateNodeArray",
            "flattenCommaElements" => "FlattenCommaElements",
            "setTextRange" => "Utilities.SetTextRange",
            "sameFlatMap" => "Core.SameFlatMap",
            "propagateChildrenFlags" => "PropagateChildrenFlags",
            "propagateChildFlags" => "PropagateChildFlags",
            _ => PascalCase(tsName),
        };
    }

    private string TranslateMethod(TsExpression expression)
    {
        var receiver = Required(expression.Receiver);
        var name = Required(expression.Name);
        var arguments = string.Join(", ", expression.Args.Select(argument => Translate(argument)));

        // `parenthesizerRules().parenthesizeX(…)` is the factory's lazily-loaded parenthesizer, which arrives with
        // milestone M4d (§15.7): the call is generated, the implementation is not there yet.
        if (receiver == "parenthesizerRules") return $"Parenthesizer.{PascalCase(name)}({arguments})";

        throw new Untranslatable($"no translation for `{receiver}.{name}(…)`");
    }

    private string TranslatePrefix(TsExpression expression)
    {
        var operand = Translate(Required(expression.Operand));
        return Required(expression.Op) switch
        {
            // TS negates any value; C# negates only a `bool`, so the operand is evaluated for truthiness first — which
            // is also what TS means by `!!x`, the idiom that turns a value into a boolean.
            "!" => $"!{Truthy(Required(expression.Operand))}",

            // `~flags`: C# defines no unary complement on enums, so it goes through Core/Flags.cs (§11.3 item 2).
            "~" => $"CoreFlags.Not({operand})",
            "-" => $"-{operand}",
            var other => throw new Untranslatable($"no translation for the prefix operator `{other}`"),
        };
    }

    private static string Operator(string op) => op switch
    {
        "===" => "==",
        "!==" => "!=",
        "==" or "!=" or "??" or "||" or "&&" or "|" or "&" or "+" or "-" or "*" or "/" or "%"
            or "<" or ">" or "<=" or ">=" => op,
        _ => throw new Untranslatable($"no translation for the binary operator `{op}`"),
    };

    //
    // Small helpers
    //

    private static string PascalCase(string name)
        => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static string ParameterName(string name)
        => NodeEmitter.CSharpKeywords.Contains(name) ? "@" + name : name;

    /// <summary>A C# string literal for a value the extractor already decoded.</summary>
    private static string Literal(string? value)
    {
        var sb = new StringBuilder((value?.Length ?? 0) + 2).Append('"');
        foreach (var c in value ?? "")
        {
            _ = c switch
            {
                '\\' => sb.Append("\\\\"),
                '"' => sb.Append("\\\""),
                '\n' => sb.Append("\\n"),
                '\r' => sb.Append("\\r"),
                '\t' => sb.Append("\\t"),
                _ => sb.Append(c),
            };
        }

        return sb.Append('"').ToString();
    }

    private static IEnumerable<List<string>> Chunk(List<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
        {
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
        }
    }

    private static string Required(string? value, string what = "value")
        => value ?? throw new Untranslatable($"missing {what}");

    private static TsExpression Required(TsExpression? expression, string what = "expression")
        => expression ?? throw new Untranslatable($"missing {what}");

    /// <summary>Raised when a member cannot be translated; the caller leaves it to the hand-written half.</summary>
    private sealed class Untranslatable(string reason) : Exception(reason);
}
