using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// Emits the C# mirror of <c>types.ts</c>'s node model: one class per chain step and concrete node, one
/// interface per mixin.
/// </summary>
/// <remarks>
/// Porting decision D1 in practice:
/// <list type="bullet">
/// <item>Classes derive from <see cref="TsTypeModel.ClassBaseOf"/> (or <c>Node</c>) and implement every mixin
/// interface they inherit, transitively.</item>
/// <item>Interfaces carry only their own members and extend other interfaces, never a class — C# interfaces
/// cannot inherit one, so the link to <c>Node</c> is simply dropped.</item>
/// <item>Fields that live on <c>Node</c> itself (<c>kind</c>, <c>flags</c>, <c>parent</c>…) are not repeated
/// in derived classes, and <c>_xxxBrand</c> markers are dropped entirely: they exist only to make TS's
/// structural types nominal.</item>
/// </list>
/// <para>
/// The interesting case is a member TS declares with <em>different</em> types along the interface chain:
/// <c>FunctionDeclaration.body</c> is a <c>Block</c> while <c>FunctionLikeDeclarationBase.body</c> is a
/// <c>ConciseBody</c>. C# requires an implicit implementation to match every interface exactly, and
/// covariant returns apply only to <c>override</c>s rather than to interface implementations. So the class
/// keeps its own (narrower) type and each interface whose type differs gets an <em>explicit</em>
/// implementation forwarding to it — reads are the covariant direction and therefore always safe.
/// </para>
/// </remarks>
internal static class NodeEmitter
{
    /// <summary>Types written by hand because generated output cannot express them.</summary>
    private static readonly HashSet<string> s_handWritten = new(StringComparer.Ordinal)
    {
        "Node",
        "NodeArray",
        "MutableNodeArray",
        "Token",
        "PunctuationToken",
        "KeywordToken",
        "ModifierToken",
    };

    /// <summary>Members that already exist on the hand-written <c>Node</c> base class.</summary>
    private static readonly HashSet<string> s_nodeMembers = new(StringComparer.Ordinal)
    {
        "kind",
        "flags",
        "modifierFlagsCache",
        "transformFlags",
        "id",
        "parent",
        "original",
        "emitNode",
        "pos",
        "end",
    };

    public static (string Interfaces, string Classes) Emit(TsTypeModel model, TsTypeMapper mapper)
    {
        var interfaces = new StringBuilder();
        var classes = new StringBuilder();

        AppendHeader(interfaces, "Node model — mixin interfaces (porting decision D1).");
        interfaces.AppendLine("namespace TypeScript.Compiler;");
        interfaces.AppendLine();

        AppendHeader(classes, "Node model — the class side of the syntax tree (porting decision D1).");
        classes.AppendLine("namespace TypeScript.Compiler;");
        classes.AppendLine();

        var ordered = model.Interfaces.Values.OrderBy(static i => i.Name, StringComparer.Ordinal).ToList();

        foreach (var iface in ordered)
        {
            if (model.RoleOf(iface.Name) != TypeRole.Interface || s_handWritten.Contains(iface.Name)) continue;
            EmitInterface(interfaces, model, mapper, iface);
        }

        foreach (var iface in ordered)
        {
            if (model.RoleOf(iface.Name) != TypeRole.Class || s_handWritten.Contains(iface.Name)) continue;
            EmitClass(classes, model, mapper, iface);
        }

        return (interfaces.ToString().Replace("\r\n", "\n"), classes.ToString().Replace("\r\n", "\n"));
    }

    private static void EmitInterface(StringBuilder sb, TsTypeModel model, TsTypeMapper mapper, TsInterface iface)
    {
        var bases = iface.Extends
            .Select(TsTypeMapper.BareName)
            .Where(b => model.RoleOf(b) == TypeRole.Interface)
            .Select(mapper.TypeName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        sb.AppendLine($"/// <summary>ported from types.ts: <c>{iface.Name}</c>.</summary>");
        sb.Append($"internal partial interface {mapper.TypeName(iface.Name)}{GenericsOf(iface.TypeParameters)}");
        if (bases.Count != 0) sb.Append($" : {string.Join(", ", bases)}");
        sb.AppendLine();
        sb.AppendLine("{");

        // Members re-declared from a base interface are not repeated: with different types that is a
        // conflicting redeclaration in C#, and implementors would have to satisfy both.
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        CollectInheritedNames(model, iface, inherited);
        var extendsNode = model.ReachesNode(iface.Name);

        // C# cannot overload on return type alone, but TS does: `createToken` has 20+ declarations that differ
        // only in what they return (`createToken(token: SyntaxKind.EndOfFileToken): EndOfFileToken`, …). Members
        // that would collide are merged into a single signature whose return type is the common base of the
        // candidates — the type a caller can rely on whichever declaration TS resolved to.
        foreach (var group in iface.Members
            .Where(member => !Skip(member, extendsNode) && !inherited.Contains(member.Name))
            .GroupBy(member => MemberKey(member, mapper)))
        {
            var candidates = group.ToList();
            var member = candidates[0];
            string? forcedReturnType = null;
            if (candidates.Count > 1 && member.IsMethod)
            {
                var names = candidates
                    .Select(candidate => TsTypeMapper.BareName(candidate.Type))
                    .Where(model.IsKnown)
                    .Distinct()
                    .ToList();
                if (names.Count > 1)
                {
                    forcedReturnType = mapper.Map(model.CommonBase(names));
                }
            }

            AppendMember(sb, member, mapper, inInterface: true, forceType: forcedReturnType);
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitClass(StringBuilder sb, TsTypeModel model, TsTypeMapper mapper, TsInterface iface)
    {
        // The base reference goes through the mapper like any other type: TS writes
        // `ObjectLiteralExpressionBase<ObjectLiteralElementLike>`, and the argument needs mapping too.
        var baseName = model.ClassBaseOf(iface.Name) is { } declared && !s_handWritten.Contains(TsTypeMapper.BareName(declared))
            ? mapper.Map(declared)
            : "Node";

        var implemented = model.InterfaceClosureOf(iface.Name, transitive: true)
            .Where(i => !s_handWritten.Contains(i))
            .Select(mapper.TypeName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToList();

        // A class with no pinned kind is a categorisation step (`Expression`, `Statement`, …): it has no Kind
        // of its own, so it stays abstract and only its concrete descendants implement one.
        var modifiers = iface.ConcreteKind is null
            ? "abstract "
            : IsSealed(model, iface.Name) ? "sealed " : "";

        sb.AppendLine($"/// <summary>ported from types.ts: <c>{iface.Name}</c>.</summary>");
        sb.Append($"internal {modifiers}partial class {iface.Name}{GenericsOf(iface.TypeParameters)} : {baseName}");
        if (implemented.Count != 0) sb.Append($", {string.Join(", ", implemented)}");
        sb.AppendLine();
        sb.AppendLine("{");

        if (iface.ConcreteKind is { } kind)
        {
            sb.AppendLine($"    public override SyntaxKind Kind => SyntaxKind.{kind};");
            sb.AppendLine();
        }

        // What the class itself provides, plus anything from the mixins that no ancestor already supplies.
        var provided = new HashSet<string>(StringComparer.Ordinal);
        CollectAncestorMembers(model, iface, provided);

        var classMembers = new Dictionary<string, (TsMember Member, string Type)>(StringComparer.Ordinal);

        foreach (var member in iface.Members)
        {
            if (!Skip(member, extendsNode: true) && !provided.Contains(member.Name))
            {
                classMembers[member.Name] = (member, mapper.MapMember(member.Type, member.IsOptional));
            }
        }

        foreach (var mixin in model.InterfaceClosureOf(iface.Name, transitive: true))
        {
            if (!model.Interfaces.TryGetValue(mixin, out var mixinInterface)) continue;
            foreach (var member in mixinInterface.Members)
            {
                if (Skip(member, extendsNode: true) || provided.Contains(member.Name) || classMembers.ContainsKey(member.Name)) continue;
                classMembers[member.Name] = (member, mapper.MapMember(member.Type, member.IsOptional));
            }
        }

        foreach (var entry in classMembers.Values)
        {
            AppendMember(sb, entry.Member, mapper, inInterface: false, forceType: entry.Type);
        }

        // Interfaces whose declaration of a member differs from the class's own need an explicit
        // implementation: C# requires an implicit one to match exactly, and covariant returns apply only to
        // overrides. Reads forward covariantly, which is always safe.
        var explicitImplementations = new List<string>();
        foreach (var mixin in model.InterfaceClosureOf(iface.Name, transitive: true))
        {
            if (!model.Interfaces.TryGetValue(mixin, out var mixinInterface)) continue;

            foreach (var member in mixinInterface.Members)
            {
                if (Skip(member, extendsNode: true) || !classMembers.TryGetValue(member.Name, out var own)) continue;

                // The interface may not declare this member at all: it is skipped there when a base
                // interface already declares it, and implementing it explicitly would then fail to match.
                var inheritedByMixin = new HashSet<string>(StringComparer.Ordinal);
                CollectInheritedNames(model, mixinInterface, inheritedByMixin);
                if (inheritedByMixin.Contains(member.Name)) continue;

                var interfaceType = mapper.MapMember(member.Type, member.IsOptional);
                if (interfaceType == own.Type) continue;

                var name = PascalCase(member.Name);
                var prefix = $"{interfaceType} {mapper.TypeName(mixin)}.{name}";

                // Both directions need an explicit cast: TS lets a class narrow the member and it lets the
                // class declare something else entirely (`ArrowFunction.name` is `never`, which is `object?`
                // here, while the interface says `DeclarationName`), so neither side is implicitly convertible.
                explicitImplementations.Add(member.IsReadonly
                    ? $"    {prefix} => ({interfaceType}){name};"
                    : $"    {prefix} {{ get => ({interfaceType}){name}; set => {name} = ({own.Type})value!; }}");
            }
        }

        if (explicitImplementations.Count != 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // TS narrows these on the class but widens them on the interface; C# needs both spelled out.");
            foreach (var line in explicitImplementations.Distinct(StringComparer.Ordinal))
            {
                sb.AppendLine(line);
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>Member names an ancestor class (or any of its mixins) already declares.</summary>
    private static void CollectAncestorMembers(TsTypeModel model, TsInterface iface, HashSet<string> names)
    {
        for (var baseName = model.BareClassBaseOf(iface.Name); baseName is not null; baseName = model.BareClassBaseOf(baseName))
        {
            if (!model.Interfaces.TryGetValue(baseName, out var baseInterface)) break;

            foreach (var member in baseInterface.Members) names.Add(member.Name);
            foreach (var mixin in model.InterfaceClosureOf(baseName, transitive: true))
            {
                if (!model.Interfaces.TryGetValue(mixin, out var mixinInterface)) continue;
                foreach (var member in mixinInterface.Members) names.Add(member.Name);
            }
        }
    }

    /// <summary>Member names declared anywhere up an interface's base chain.</summary>
    private static void CollectInheritedNames(TsTypeModel model, TsInterface iface, HashSet<string> names)
    {
        foreach (var baseReference in iface.Extends)
        {
            var bare = TsTypeMapper.BareName(baseReference);
            if (!model.Interfaces.TryGetValue(bare, out var baseInterface)) continue;

            foreach (var member in baseInterface.Members) names.Add(member.Name);
            CollectInheritedNames(model, baseInterface, names);
        }
    }

    /// <summary>
    /// Appends one member: its core signature, the overloads a union needs, and the note explaining a slot
    /// that had to stay <c>object?</c>.
    /// </summary>
    /// <remarks>
    /// Overloads are emitted for interfaces only, and only as <em>default</em> implementations forwarding to the
    /// core member. That is what keeps union support cheap: TS writes one signature per union member and C# needs
    /// one per narrowing, but every member still has exactly one body to implement.
    /// <para>
    /// The arguments are cast to the core parameter types. Without the cast the forwarding member would bind to
    /// itself wherever a narrowed overload happens to be applicable again, and recurse until the stack overflows.
    /// </para>
    /// </remarks>
    private static void AppendMember(StringBuilder sb, TsMember member, TsTypeMapper mapper, bool inInterface, string? forceType = null)
    {
        var name = PascalCase(member.Name);
        // Interface members are implicitly public; class members must say so to satisfy the interface.
        var accessibility = inInterface ? "" : "public ";

        if (!member.IsMethod)
        {
            var slot = mapper.MapSlot(member.Type, member.IsOptional);
            AppendNote(sb, slot.Note);

            // Generated code runs under `#nullable enable`, and TS's non-optional properties are set by the
            // parser/factory rather than at construction, so reference-typed members are declared nullable to
            // match how the field is actually populated.
            var accessors = member.IsReadonly ? "{ get; }" : "{ get; set; }";
            sb.AppendLine($"    {accessibility}{Nullable(slot.Type, mapper, forceType)} {name} {accessors}");
            return;
        }

        var slots = member.Parameters
            .Select(parameter => mapper.MapSlot(parameter.Type, parameter.IsOptional, member.TypeParameters))
            .ToList();

        foreach (var note in slots.Select(slot => slot.Note).Where(static note => note is not null))
        {
            AppendNote(sb, note);
        }

        // A method produces its result, so — unlike a property — a non-nullable TS return type maps to a
        // non-nullable C# one. A union return type cannot be overloaded either: it widens (MapSlot.Type).
        var returnSlot = mapper.MapSlot(member.Type, optional: false, member.TypeParameters);
        if (returnSlot.Note is not null) AppendNote(sb, returnSlot.Note);

        var returnType = forceType ?? (member.Type == "this" ? "object" : returnSlot.Type);

        var coreTypes = slots
            .Select((slot, index) => ParameterType(slot.Type, member.Parameters[index].IsRest, mapper))
            .ToList();

        sb.AppendLine($"    {accessibility}{SignatureOf(member, name, returnType, coreTypes, mapper)};");

        if (!inInterface) return;

        // One default implementation per combination of narrowed slots; the all-core combination is the member
        // just written, so it is skipped.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var combination in Combinations(slots))
        {
            if (combination.All(static part => part.IsCore)) continue;

            var types = combination
                .Select((part, index) => ParameterType(part.Type, member.Parameters[index].IsRest, mapper))
                .ToList();

            var arguments = combination
                .Select((part, index) => part.IsCore ? ParameterName(member.Parameters[index].Name) : $"(object?){ParameterName(member.Parameters[index].Name)}");

            var signature = SignatureOf(member, name, returnType, types, mapper);
            if (!seen.Add(signature)) continue;
            sb.AppendLine($"    {signature} => {name}({string.Join(", ", arguments)});");
        }
    }

    /// <summary>
    /// Every argument list the overloads need: each slot contributes its core type, plus one narrowing per
    /// overload it has.
    /// </summary>
    private static List<(bool IsCore, string Type)[]> Combinations(IReadOnlyList<MappedSlot> slots)
    {
        var result = new List<(bool IsCore, string Type)[]>();
        var current = new (bool IsCore, string Type)[slots.Count];
        Walk(0);
        return result;

        void Walk(int index)
        {
            if (index == slots.Count)
            {
                result.Add([.. current]);
                return;
            }

            current[index] = (true, slots[index].Type);
            Walk(index + 1);

            foreach (var overload in slots[index].Overloads)
            {
                current[index] = (false, overload);
                Walk(index + 1);
            }
        }
    }

    /// <summary>
    /// A C# method signature for a TS method member.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: <c>this</c> as a return type (TS's fluent setters, e.g. <c>ModeAwareCache.set</c>) has no C#
    /// equivalent; it maps to <c>object</c>, which every implementation satisfies.
    /// <para>
    /// PORT-NOTE: a type parameter keeps its constraint only when the constraint names a type the port models
    /// (<c>T extends Node</c>). Constraints that map to <c>object</c> are dropped rather than guessed at, and the
    /// implementation must match whatever is emitted here — an inaccurate constraint would fail to compile at the
    /// one place that cannot be worked around.
    /// </para>
    /// </remarks>
    private static string SignatureOf(TsMember member, string name, string returnType, IReadOnlyList<string> parameterTypes, TsTypeMapper mapper)
    {
        var sb = new StringBuilder();
        sb.Append(returnType).Append(' ').Append(name);

        if (member.TypeParameters.Count != 0)
        {
            sb.Append('<').Append(string.Join(", ", member.TypeParameters.Select(tp => tp.Name))).Append('>');
        }

        sb.Append('(');
        // TS's `...args: T[]` is C#'s `params T[] args`, and a parameter name keeps its TS spelling (camelCase)
        // — which occasionally lands on a C# keyword (`createTypeOperatorNode(operator, type)`), so it is
        // escaped rather than renamed.
        sb.Append(string.Join(", ", parameterTypes.Select((type, index) =>
            $"{(member.Parameters[index].IsRest ? "params " : "")}{type} {ParameterName(member.Parameters[index].Name)}")));
        sb.Append(')');

        foreach (var typeParameter in member.TypeParameters)
        {
            if (typeParameter.Constraint is null) continue;
            var constraint = mapper.MapMember(typeParameter.Constraint, false, member.TypeParameters).TrimEnd('?');
            if (constraint == "object" || mapper.IsValueType(constraint)) continue;
            sb.Append($" where {typeParameter.Name} : {constraint}");
        }

        return sb.ToString();
    }

    /// <summary>Writes the reason a slot could not be typed, as a remark on the member it belongs to.</summary>
    private static void AppendNote(StringBuilder sb, string? note)
    {
        if (note is null) return;
        sb.AppendLine($"    /// <remarks>PORT-NOTE: {note}</remarks>");
    }

    /// <summary>A reference-typed slot is nullable; a value type keeps whatever it was mapped to.</summary>
    private static string Nullable(string type, TsTypeMapper mapper, string? forced = null)
    {
        var declared = forced ?? type;
        // `void` is not a valid C# parameter or member type. TS only produces it in a slot through an
        // intersection that means "no value here" (`__String`), so such a slot takes `object?` instead.
        if (declared == "void") return "object?";

        return !declared.EndsWith('?') && !mapper.IsValueType(declared) ? declared + "?" : declared;
    }

    /// <summary>"void" cannot be a generic argument; numeric assignment is available in C#.</summary>
    private static string ParameterType(string type, bool isRest, TsTypeMapper mapper)
        => isRest ? type : Nullable(type, mapper);

    /// <summary>A parameter name, escaped when it collides with a C# keyword.</summary>
    private static string ParameterName(string name)
        => s_csharpKeywords.Contains(name) ? "@" + name : name;

    /// <summary>C# reserved keywords, so a parameter name that collides is escaped with `@`.</summary>
    private static readonly HashSet<string> s_csharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>
    /// Groups members that C# would consider the same signature: a property by name, a method by name and
    /// parameter types (return type is deliberately excluded — that is what has to be reconciled).
    /// </summary>
    /// <remarks>
    /// The parameter types are the *mapped* ones, with any nullability annotation stripped. Two reasons: TS's
    /// `createToken` overloads take `SyntaxKind.EndOfFileToken`, `SyntaxKind.Unknown` and so on, which are distinct
    /// TS types but all map to `SyntaxKind`; and C#'s overload resolution ignores nullable annotations, so
    /// `string` and `string?` are one signature to the compiler.
    /// </remarks>
    private static string MemberKey(TsMember member, TsTypeMapper mapper) => member.IsMethod
        ? $"m:{member.Name}({string.Join(",", member.Parameters.Select(p => mapper.MapMember(p.Type, false, member.TypeParameters).TrimEnd('?')))})"
        : $"p:{member.Name}";

    private static bool Skip(TsMember member, bool extendsNode)
    {
        // `_xxxBrand` exists only to make TS's structural types nominal, so those members are dropped. Real state
        // that happens to start with an underscore (`SyntaxList._children`) is added by the hand-written part of the
        // partial class instead — see Model/SyntaxList.cs.
        if (member.Name.StartsWith('_')) return true;
        // `kind` is emitted from the pinned SyntaxKind (or left abstract) on node types; a non-node type that
        // declares it — CommentRange.kind is a CommentKind — keeps it.
        if (member.Name == "kind" && extendsNode) return true;

        // Interfaces such as ConcretePragmaSpecs have string-literal keys (`"amd-dependency"`), which are
        // JS pragma names with no C# spelling. They are internal plumbing, so they are dropped rather than
        // renamed into something misleading.
        if (!IsCSharpIdentifier(member.Name)) return true;

        // The Node base class owns these; a type that does not derive from Node may still declare one of them for
        // its own purposes (EmitNode.flags is an EmitFlags, unrelated to Node.flags, and CommentRange.kind/pos/end
        // come from TextRange, not from Node).
        return extendsNode && s_nodeMembers.Contains(member.Name);
    }

    private static bool IsCSharpIdentifier(string name)
    {
        if (name.Length == 0) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }

        return true;
    }

    private static string PascalCase(string name)
        => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static string GenericsOf(IReadOnlyList<string> typeParameters)
        => typeParameters.Count == 0 ? "" : $"<{string.Join(", ", typeParameters)}>";

    /// <summary>True when no generated class derives from this one.</summary>
    private static bool IsSealed(TsTypeModel model, string name)
    {
        foreach (var candidate in model.Interfaces.Values)
        {
            if (candidate.Name == name) continue;
            if (model.RoleOf(candidate.Name) != TypeRole.Class) continue;
            if (model.BareClassBaseOf(candidate.Name) == name) return false;
        }

        return true;
    }

    private static void AppendHeader(StringBuilder sb, string summary)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//   Generated from src/compiler/types.ts by tools/Gen — DO NOT EDIT.");
        sb.AppendLine("//   Regenerate with:");
        sb.AppendLine("//     dotnet run --project tools/Gen -- --out src/TypeScript.Compiler");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine($"// {summary}");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }
}
