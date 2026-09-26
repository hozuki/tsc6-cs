namespace TypeScript.Compiler.Tools;

/// <summary>How a TS interface surfaces in C#.</summary>
internal enum TypeRole
{
    /// <summary>Becomes a C# class — a concrete node or a step in the single-inheritance chain.</summary>
    Class,

    /// <summary>Becomes a C# interface — a "mixin" capability that several unrelated chains share.</summary>
    Interface,
}

/// <summary>
/// The classification of every interface and alias in <c>types.ts</c>, plus the class-inheritance graph
/// the type mapper needs.
/// </summary>
/// <remarks>
/// Porting decision D1: a TS interface becomes a C# class when it lies on a single inheritance chain and an
/// interface when it is a mixin. The rule that separates them:
/// <list type="bullet">
/// <item><c>Node</c> is the root class.</item>
/// <item>An interface that pins <c>kind: SyntaxKind.X</c> is a concrete node → class.</item>
/// <item>Otherwise, if all of an interface's direct inheritors lie on one chain (each is an ancestor of the
/// next), it is a chain step → class. That covers <c>Expression</c>, <c>Statement</c>, <c>LeftHandSideExpression</c>…</item>
/// <item>Otherwise its inheritors come from unrelated chains → it is a mixin → interface. That covers
/// <c>Declaration</c>, <c>JSDocContainer</c>, <c>LocalsContainer</c>, <c>FlowContainer</c>.</item>
/// </list>
/// This matters because C# has single inheritance: <c>Identifier : PrimaryExpression, IDeclaration, …</c>
/// is only expressible if <c>Declaration</c> is an interface.
/// </remarks>
internal sealed class TsTypeModel
{
    private readonly Dictionary<string, TsInterface> _interfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TsTypeAlias> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeRole> _roles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _classBase = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dualRole = new(StringComparer.Ordinal);

    public TsTypeModel(IEnumerable<TsInterface> interfaces, IEnumerable<TsTypeAlias> aliases)
    {
        foreach (var i in interfaces) _interfaces[i.Name] = i;
        foreach (var a in aliases) _aliases[a.Name] = a;

        Classify();
    }

    public IReadOnlyDictionary<string, TsInterface> Interfaces => _interfaces;

    public IReadOnlyDictionary<string, TsTypeAlias> Aliases => _aliases;

    /// <summary>
    /// Class steps that are <em>also</em> inherited in a non-first position, and therefore need a companion
    /// <c>IX</c> interface as well as the class. <c>PrimaryExpression</c> is the base of <c>Identifier</c> but
    /// merely an add-on for <c>LiteralExpression extends LiteralLikeNode, PrimaryExpression</c>; C# single
    /// inheritance can express one of those directly and needs an interface for the other.
    /// </summary>
    public IReadOnlyCollection<string> DualRoleClasses => _dualRole;

    // A forced class is a class by construction (see s_forcedClasses), so it must not take the mixin `I` prefix even
    // though the classification pass may have left it as an interface — `NodeArray<T>` is hand-written as a class.
    public TypeRole RoleOf(string name) => s_forcedClasses.Contains(name)
        ? TypeRole.Class
        : _roles.TryGetValue(name, out var role) ? role : TypeRole.Interface;

    public bool IsKnown(string name) => _interfaces.ContainsKey(name) || _aliases.ContainsKey(name);

    /// <summary>
    /// The TS name behind a mapped C# type, or <c>null</c> when the name comes from somewhere else (a BCL
    /// collection, <c>string</c>, a generated enum) — which is exactly the distinction that decides whether a
    /// set of candidates has a common base in the C# class chain.
    /// </summary>
    /// <remarks>
    /// Mixin interfaces carry an <c>I</c> prefix in C# (D1), and the bare name is tried first because
    /// <c>Identifier</c> is itself a modelled name and must not be read as <c>I</c> + <c>dentifier</c>.
    /// </remarks>
    public string? TsNameOf(string csharpName)
    {
        if (IsKnown(csharpName)) return csharpName;
        if (csharpName.StartsWith('I') && IsKnown(csharpName[1..])) return csharpName[1..];
        return null;
    }

    /// <summary>
    /// The C# base class reference of a generated class, including generic arguments, or <c>null</c> when
    /// it derives straight from <c>Node</c>.
    /// </summary>
    public string? ClassBaseOf(string name) => _classBase.GetValueOrDefault(name);

    /// <summary>The bare base class name, for ancestry comparisons.</summary>
    public string? BareClassBaseOf(string name)
        => _classBase.TryGetValue(name, out var reference) && reference is not null ? BareName(reference) : null;

    /// <summary>The mixin interfaces a generated class must implement (transitively closed, de-duplicated).</summary>
    public List<string> InterfaceClosureOf(string name, bool transitive = false)
    {
        var result = new List<string>();
        if (!_interfaces.TryGetValue(name, out var iface)) return result;

        if (!transitive)
        {
            foreach (var baseName in iface.Extends)
            {
                var bare = BareName(baseName);
                if (RoleOf(bare) == TypeRole.Interface && !result.Contains(bare)) result.Add(bare);
            }

            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        Collect(name);
        result.Remove("Node");
        return result;

        void Collect(string current)
        {
            if (!seen.Add(current) || !_interfaces.TryGetValue(current, out var it)) return;

            foreach (var baseName in it.Extends)
            {
                var bare = BareName(baseName);
                if (!_interfaces.ContainsKey(bare)) continue;

                if (RoleOf(bare) == TypeRole.Interface) result.Add(bare);
                Collect(bare);
            }
        }
    }

    /// <summary>The class chain of a type, from itself up to (and including) <c>Node</c>.</summary>
    public List<string> ClassChainOf(string name)
    {
        var chain = new List<string>();
        var current = name;
        var guard = 0;

        while (current is not null && guard++ < 64)
        {
            if (!chain.Contains(current)) chain.Add(current);
            current = BareClassBaseOf(current);
        }

        if (!chain.Contains("Node")) chain.Add("Node");
        return chain;
    }

    /// <summary>
    /// The most derived type that all of <paramref name="names"/> share — used to widen TS unions, which C#
    /// has no equivalent for. Returns <c>Node</c> when nothing narrower is common.
    /// </summary>
    public string CommonBase(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return "Node";

        var shared = ClassChainOf(names[0]);
        foreach (var name in names.Skip(1))
        {
            var other = ClassChainOf(name);
            shared = shared.Where(other.Contains).ToList();
            if (shared.Count == 0) return "Node";
        }

        // The chains run most-derived-first, so the first surviving entry is the most derived common base.
        return shared.Count != 0 ? shared[0] : "Node";
    }

    /// <summary>Strips generic arguments: <c>`Token&lt;TKind&gt;`</c> becomes <c>`Token`</c>.</summary>
    private static string BareName(string typeReference)
    {
        var lt = typeReference.IndexOf('<');
        return (lt < 0 ? typeReference : typeReference[..lt]).Trim();
    }

    /// <summary>The alias that <paramref name="name"/> resolves to, following chains of simple aliases.</summary>
    public TsTypeAlias? AliasOf(string name) => _aliases.GetValueOrDefault(name);

    /// <summary>
    /// Decides class vs interface for every interface.
    /// </summary>
    /// <remarks>
    /// The discriminating rule is <em>position</em>, not shape: TS lists a node's categorisation chain first
    /// and its mixins after it (<c>CallExpression extends LeftHandSideExpression, Declaration</c>). An
    /// interface that ever appears in a non-first position therefore cannot be anybody's C# base class —
    /// <c>Declaration</c> is inherited first by <c>SourceFile</c> but second by <c>CallExpression</c>, so it
    /// is a mixin. An interface that only ever appears first is a chain step and becomes a class, which is
    /// what keeps <c>Expression</c>/<c>LeftHandSideExpression</c> on the inheritance chain even though
    /// several sibling nodes share them.
    /// </remarks>
    /// <summary>
    /// Interfaces that must become classes even though no interface inherits them as a base. The token
    /// subtypes are reached only through type aliases (<c>type DotToken = PunctuationToken&lt;…&gt;</c>), so
    /// the position rule cannot see them.
    /// </summary>
    private static readonly string[] s_forcedClasses =
        ["Token", "PunctuationToken", "KeywordToken", "ModifierToken", "NodeArray", "MutableNodeArray", "ModeAwareCache"];

    private void Classify()
    {
        _roles["Node"] = TypeRole.Class;
        foreach (var forced in s_forcedClasses) _roles[forced] = TypeRole.Class;

        foreach (var iface in _interfaces.Values)
        {
            if (iface.HasConcreteKind) _roles[iface.Name] = TypeRole.Class;
        }

        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var iface in _interfaces.Values)
        {
            for (var i = 0; i < iface.Extends.Count; i++)
            {
                var bare = BareName(iface.Extends[i]);
                if (!positions.TryGetValue(bare, out var list)) positions[bare] = list = [];
                list.Add(i);
            }
        }

        foreach (var (name, seen) in positions)
        {
            if (_roles.ContainsKey(name)) continue;
            _roles[name] = seen.TrueForAll(static p => p == 0) ? TypeRole.Class : TypeRole.Interface;
        }

        // Interfaces nobody inherits from (NodeArray, TextRange, …) are not chain steps.
        foreach (var iface in _interfaces.Values)
        {
            if (!_roles.ContainsKey(iface.Name)) _roles[iface.Name] = TypeRole.Interface;
        }

        // The position rule on its own also reads "inherited first by exactly one interface" as a chain step, but
        // that shape is just as common for types that have nothing to do with syntax: `Symbol` is inherited first
        // only by `TransientSymbol`, `Type` by its subtypes, `CommentRange` by `SynthesizedComment`. What actually
        // marks a node is that its categorisation chain reaches `Node`, so anything that does not is demoted here
        // (`NodeArray`/`MutableNodeArray` are forced classes and never come through this path).
        foreach (var iface in _interfaces.Values)
        {
            if (RoleOf(iface.Name) == TypeRole.Class && !ReachesNode(iface.Name))
            {
                _roles[iface.Name] = TypeRole.Interface;
            }
        }

        foreach (var iface in _interfaces.Values)
        {
            if (RoleOf(iface.Name) != TypeRole.Class || iface.Name == "Node") continue;

            // Keep the generic arguments: a base such as ObjectLiteralExpressionBase<T> must be spelled
            // with them at the use site, and the bare name is only needed for comparisons.
            string? baseReference = null;
            foreach (var candidate in iface.Extends)
            {
                if (RoleOf(BareName(candidate)) == TypeRole.Class) { baseReference = candidate.Trim(); break; }
            }

            _classBase[iface.Name] = baseReference ?? "Node";
        }

        // A class step inherited in a non-first position needs a companion interface (see DualRoleClasses).
        foreach (var iface in _interfaces.Values)
        {
            for (var i = 1; i < iface.Extends.Count; i++)
            {
                var bare = BareName(iface.Extends[i]);
                if (RoleOf(bare) == TypeRole.Class) _dualRole.Add(bare);
            }
        }
    }

    /// <summary>Whether an interface sits on a categorisation chain that reaches <c>Node</c>.</summary>
    internal bool ReachesNode(string name, HashSet<string>? seen = null)
    {
        if (name == "Node") return true;

        seen ??= new HashSet<string>(StringComparer.Ordinal);
        if (!seen.Add(name)) return false;
        if (!_interfaces.TryGetValue(name, out var iface)) return false;

        foreach (var baseName in iface.Extends)
        {
            if (ReachesNode(BareName(baseName), seen)) return true;
        }

        return false;
    }
}
