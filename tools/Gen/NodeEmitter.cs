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
        sb.Append($"internal interface {mapper.TypeName(iface.Name)}{GenericsOf(iface.TypeParameters)}");
        if (bases.Count != 0) sb.Append($" : {string.Join(", ", bases)}");
        sb.AppendLine();
        sb.AppendLine("{");

        // Members re-declared from a base interface are not repeated: with different types that is a
        // conflicting redeclaration in C#, and implementors would have to satisfy both.
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        CollectInheritedNames(model, iface, inherited);

        foreach (var member in iface.Members)
        {
            if (Skip(member, model.ReachesNode(iface.Name)) || inherited.Contains(member.Name)) continue;
            sb.AppendLine($"    {Declaration(member, mapper, inInterface: true)}");
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
        sb.Append($"internal {modifiers}class {iface.Name}{GenericsOf(iface.TypeParameters)} : {baseName}");
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
            sb.AppendLine($"    {Declaration(entry.Member, mapper, inInterface: false, forceType: entry.Type)}");
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
                explicitImplementations.Add(member.IsReadonly
                    ? $"    {prefix} => {name};"
                    : $"    {prefix} {{ get => {name}; set => {name} = ({own.Type})value!; }}");
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

    private static string Declaration(TsMember member, TsTypeMapper mapper, bool inInterface, string? forceType = null)
    {
        var type = forceType ?? mapper.MapMember(member.Type, member.IsOptional);

        // Generated code runs under `#nullable enable`, and TS's non-optional properties are set by the
        // parser/factory rather than at construction, so reference-typed members are declared nullable to
        // match how the field is actually populated.
        if (!type.EndsWith('?') && type != "void" && !mapper.IsValueType(type)) type += "?";

        var name = PascalCase(member.Name);
        var accessors = member.IsReadonly ? "{ get; }" : "{ get; set; }";
        // Interface members are implicitly public; class members must say so to satisfy the interface.
        var accessibility = inInterface ? "" : "public ";
        return $"{accessibility}{type} {name} {accessors}";
    }

    private static bool Skip(TsMember member, bool extendsNode)
    {
        // `_xxxBrand` exists only to make TS's structural types nominal, and its declared type is literally
        // `undefined`. `SyntaxList._children` also starts with an underscore but is real state, so this test is on
        // the type rather than on the name.
        if (member.Name.StartsWith('_') && member.Type == "undefined") return true;
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
