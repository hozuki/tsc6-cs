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

        foreach (var iface in model.Interfaces.Values.OrderBy(static i => i.Name, StringComparer.Ordinal))
        {
            if (model.RoleOf(iface.Name) != TypeRole.Interface || s_handWritten.Contains(iface.Name)) continue;

            var bases = iface.Extends
                .Select(TsTypeMapper.BareName)
                .Where(b => model.RoleOf(b) == TypeRole.Interface)
                .Select(mapper.TypeName)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            interfaces.AppendLine($"/// <summary>ported from types.ts: <c>{iface.Name}</c>.</summary>");
            var generics = GenericsOf(iface.TypeParameters);
            interfaces.Append($"internal interface {mapper.TypeName(iface.Name)}{generics}");
            if (bases.Count != 0) interfaces.Append($" : {string.Join(", ", bases)}");
            interfaces.AppendLine();
            interfaces.AppendLine("{");


            // Members re-declared from a base interface are not repeated: with different types that is a
            // conflicting redeclaration in C#, and the implementing classes would have to satisfy both.
            // A re-declared member that *is* emitted uses the widest type across the whole base chain, so the
            // interface and its implementors always agree.
            var interfaceCandidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var inheritedNames = new HashSet<string>(StringComparer.Ordinal);
            CollectInheritedMembers(model, iface, inheritedNames, mapper, interfaceCandidates);

            foreach (var member in iface.Members)
            {
                if (Skip(member)) continue;
                Consider(interfaceCandidates, mapper, member);
            }

            foreach (var member in iface.Members)
            {
                if (Skip(member) || inheritedNames.Contains(member.Name)) continue;

                var types = interfaceCandidates.TryGetValue(member.Name, out var declaredTypes) ? declaredTypes : [];
                var type = types.Count == 1 ? types[0] : "object?";
                var accessors = member.IsReadonly ? "{ get; }" : "{ get; set; }";
                interfaces.AppendLine($"    {type} {PascalCase(member.Name)} {accessors}");
            }

            interfaces.AppendLine("}");
            interfaces.AppendLine();
        }

        foreach (var iface in model.Interfaces.Values.OrderBy(static i => i.Name, StringComparer.Ordinal))
        {
            if (model.RoleOf(iface.Name) != TypeRole.Class || s_handWritten.Contains(iface.Name)) continue;

            var baseName = model.ClassBaseOf(iface.Name) is { } declared && !s_handWritten.Contains(declared)
                ? declared
                : "Node";

            var implemented = model.InterfaceClosureOf(iface.Name, transitive: true)
                .Where(i => !s_handWritten.Contains(i))
                .Select(mapper.TypeName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static n => n, StringComparer.Ordinal)
                .ToList();

            var generics = GenericsOf(iface.TypeParameters);
            // A class with no pinned kind is a categorisation step (`Expression`, `Statement`, …): it has no
            // Kind of its own, so it stays abstract and only its concrete descendants implement one.
            var modifiers = iface.ConcreteKind is null
                ? "abstract "
                : IsSealed(model, iface.Name) ? "sealed " : "";

            classes.AppendLine($"/// <summary>ported from types.ts: <c>{iface.Name}</c>.</summary>");
            classes.Append($"internal {modifiers}class {iface.Name}{generics} : {baseName}");
            if (implemented.Count != 0) classes.Append($", {string.Join(", ", implemented)}");
            classes.AppendLine();
            classes.AppendLine("{");

            if (iface.ConcreteKind is { } kind)
            {
                classes.AppendLine($"    public override SyntaxKind Kind => SyntaxKind.{kind};");
            }

            // A member can be declared with different types along the interface chain (`Declaration.name` is
            // optional, `NamedDeclaration.name` is required; `FunctionDeclaration.name` is an Identifier).
            // C# requires the implementing member to match every interface exactly, so a name with more than
            // one candidate type collapses to object? — the same widening the mapper already applies to TS
            // unions it cannot represent.
            var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var mixin in model.InterfaceClosureOf(iface.Name, transitive: true))
            {
                if (!model.Interfaces.TryGetValue(mixin, out var mixinInterface)) continue;
                foreach (var member in mixinInterface.Members) Consider(candidates, mapper, member);
            }

            foreach (var member in OwnMembers(model, iface)) Consider(candidates, mapper, member);

            var emitted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var member in OwnMembers(model, iface))
            {
                if (!emitted.Add(member.Name)) continue;

                var types = candidates.TryGetValue(member.Name, out var declaredTypes) ? declaredTypes : [];
                var type = types.Count == 1 ? types[0] : "object?";
                var accessors = member.IsReadonly ? "{ get; }" : "{ get; set; }";
                // PascalCase as well as the interface side: TS uses camelCase, and a raw name can collide
                // with a C# keyword (`class`, `object`) or with the enclosing type's name.
                classes.AppendLine($"    public {type} {PascalCase(member.Name)} {accessors}");
            }

            classes.AppendLine("}");
            classes.AppendLine();
        }

        return (interfaces.ToString().Replace("\r\n", "\n"), classes.ToString().Replace("\r\n", "\n"));
    }

    /// <summary>
    /// The class's own members plus every mixin member it must implement, de-duplicated by name and with
    /// anything an ancestor already provides removed.
    /// </summary>
    /// <remarks>
    /// The ancestor filter matters: a concrete node inherits both its base class and that base's mixins, so
    /// without it every derived class would redeclare <c>Symbol</c>, <c>JsDoc</c>, … and hide the base
    /// property (CS0108) instead of inheriting it.
    /// </remarks>
    private static IEnumerable<TsMember> OwnMembers(TsTypeModel model, TsInterface iface)
    {
        var provided = new HashSet<string>(StringComparer.Ordinal);

        for (var baseName = model.ClassBaseOf(iface.Name); baseName is not null; baseName = model.ClassBaseOf(baseName))
        {
            if (!model.Interfaces.TryGetValue(baseName, out var baseInterface)) break;

            foreach (var member in baseInterface.Members) provided.Add(member.Name);
            foreach (var mixin in model.InterfaceClosureOf(baseName, transitive: true))
            {
                if (!model.Interfaces.TryGetValue(mixin, out var mixinInterface)) continue;
                foreach (var member in mixinInterface.Members) provided.Add(member.Name);
            }
        }

        foreach (var member in iface.Members)
        {
            if (!provided.Contains(member.Name)) yield return member;
        }

        foreach (var mixin in model.InterfaceClosureOf(iface.Name, transitive: true))
        {
            if (!model.Interfaces.TryGetValue(mixin, out var mixinInterface)) continue;
            foreach (var member in mixinInterface.Members)
            {
                if (!provided.Contains(member.Name)) yield return member;
            }
        }
    }

    private static bool Skip(TsMember member)
    {
        // `_xxxBrand` exists only to make TS's structural types nominal, and `kind` is emitted from the
        // pinned SyntaxKind (or left abstract) rather than as a field.
        if (member.Name.StartsWith('_')) return true;
        if (member.Name == "kind") return true;

        // Interfaces such as ConcretePragmaSpecs have string-literal keys (`"amd-dependency"`), which are
        // JS pragma names with no C# spelling. They are internal plumbing, so they are dropped rather than
        // renamed into something misleading.
        if (!IsCSharpIdentifier(member.Name)) return true;

        return s_nodeMembers.Contains(member.Name);
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

    private static string Member(TsMember member, TsTypeMapper mapper, int order, bool inInterface)
    {
        var type = mapper.MapMember(member.Type, member.IsOptional);
        var name = PascalCase(member.Name);
        // A few TS members collide after PascalCasing (e.g. `modifiers` and `Modifiers`); the suffix keeps
        // the generated type compiling, and the name is only reachable from generated code.
        if (order >= 1000) name += order;
        var accessors = member.IsReadonly ? "{ get; }" : "{ get; set; }";
        // Interface members are implicitly public; class members must say so to satisfy the interface.
        var accessibility = inInterface ? "" : "public ";
        return $"{accessibility}{type} {name} {accessors}";
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
            if (model.ClassBaseOf(candidate.Name) == name) return false;
        }

        return true;
    }

    /// <summary>Collects member names declared anywhere up an interface's base chain, plus their types.</summary>
    private static void CollectInheritedMembers(
        TsTypeModel model,
        TsInterface iface,
        HashSet<string> names,
        TsTypeMapper mapper,
        Dictionary<string, List<string>> candidates)
    {
        foreach (var baseReference in iface.Extends)
        {
            var bare = TsTypeMapper.BareName(baseReference);
            if (!model.Interfaces.TryGetValue(bare, out var baseInterface)) continue;

            foreach (var member in baseInterface.Members)
            {
                names.Add(member.Name);
                if (!Skip(member)) Consider(candidates, mapper, member);
            }

            CollectInheritedMembers(model, baseInterface, names, mapper, candidates);
        }
    }

    /// <summary>Records a member's mapped type, keeping every distinct spelling seen for that name.</summary>
    private static void Consider(Dictionary<string, List<string>> candidates, TsTypeMapper mapper, TsMember member)
    {
        if (Skip(member)) return;
        if (!candidates.TryGetValue(member.Name, out var types)) candidates[member.Name] = types = [];

        var mapped = mapper.MapMember(member.Type, member.IsOptional);
        if (!types.Contains(mapped, StringComparer.Ordinal)) types.Add(mapped);
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
