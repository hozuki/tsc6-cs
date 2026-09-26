namespace TypeScript.Compiler.Tools;

/// <summary>Maps TS type expressions to C# types.</summary>
/// <remarks>
/// C# has no structural types, no union types and no string-literal types, so several TS shapes have to be
/// widened. The rules, in the order they are applied:
/// <list type="bullet">
/// <item>Unions drop <c>undefined</c>/<c>null</c> (nullability is carried by the <c>?</c> annotation) and
/// widen to the most derived type all members share — <see cref="TsTypeModel.CommonBase"/>.</item>
/// <item>Intersections keep their first member. TS uses <c>A &amp; { … }</c> to add "extra shape" to a node
/// (e.g. <c>EmitNode &amp; { autoGenerate: … }</c>), and the first member is always the real type.</item>
/// <item><c>name?: T</c> becomes <c>T?</c>; every generated type is internal, so writability is not part of
/// the public surface.</item>
/// <item>TS's collection generics map onto their BCL counterparts (<c>Map</c>→<c>Dictionary</c>, …).</item>
/// <item>Interfaces are prefixed with <c>I</c> (porting decision D1), so a reference has to consult the model
/// to know which name to emit.</item>
/// </list>
/// </remarks>
internal sealed class TsTypeMapper
{
    /// <summary>
    /// Types whose C# shape is decided by hand rather than derived.
    /// </summary>
    /// <remarks>
    /// <c>__String</c> is the interned-symbol-name type (porting decision D4: eventually a value type with a
    /// global pool; <c>string</c> until milestone M4). <c>Path</c> is a branded string, which the port
    /// already collapsed to <c>string</c> back in M1b.
    /// </remarks>
    private static readonly Dictionary<string, string> s_overrides = new(StringComparer.Ordinal)
    {
        ["__String"] = "string",
        ["Path"] = "string",
        ["NodeId"] = "int",
        ["JSDocSyntaxKind"] = "SyntaxKind",
        ["KeywordSyntaxKind"] = "SyntaxKind",
        ["PunctuationSyntaxKind"] = "SyntaxKind",
        ["TokenSyntaxKind"] = "SyntaxKind",
        ["IdentifierTypePredicate"] = "TypePredicateBase",
        ["ThisTypePredicate"] = "TypePredicateBase",
    };

    private readonly TsTypeModel _model;

    /// <summary>Types that are C# value types, so they never take a <c>?</c> nullability annotation.</summary>
    private readonly HashSet<string> _valueTypes = new(StringComparer.Ordinal)
    {
        "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "bool", "char", "void",
        // The port keeps `number` as int and `string` as a reference type (nullable), so only the generated
        // enums remain to be added.
    };

    /// <summary>
    /// The generated enums, by TS name.
    /// </summary>
    /// <remarks>
    /// The interface/alias model does not contain enums — they are emitted from their own declarations into
    /// their own files — so a member typed by one has to be recognised here, or it falls through to the
    /// "unknown name" degradation and loses its type (<c>watchFile?: WatchFileKind</c> would become
    /// <c>object?</c>).
    /// </remarks>
    private readonly HashSet<string> _enumNames;

    /// <param name="valueTypeNames">
    /// Names that are C# value types on the generated side — the enums. Passing them in keeps this class
    /// from having to know about the extracted model's enum list.
    /// </param>
    public TsTypeMapper(TsTypeModel model, IEnumerable<string> valueTypeNames)
    {
        _model = model;
        _enumNames = new HashSet<string>(valueTypeNames, StringComparer.Ordinal);
        foreach (var name in _enumNames) _valueTypes.Add(name);
    }

    /// <summary>Whether a mapped C# type is a value type (and therefore must not be nullable-annotated).</summary>
    /// <summary>Type parameters of the member currently being mapped, so `T` stays `T`.</summary>
    private ISet<string>? _currentTypeParameters;

    public bool IsValueType(string type)
    {
        var bare = type.TrimEnd('?');
        return !bare.Contains('<') && _valueTypes.Contains(bare);
    }

    /// <summary>Maps a TS type expression, appending <c>?</c> when the member is optional.</summary>
    /// <remarks>
    /// <paramref name="typeParameters"/> are the type parameters in scope for the member being mapped. They are set
    /// only for the duration of the call, because the mapping is recursive and single-threaded: without them a
    /// generic member would lose its parameter and degrade to <c>object</c> (`createNodeArray&lt;T extends Node&gt;`
    /// would emit <c>List&lt;object&gt;</c> instead of <c>List&lt;T&gt;</c>).
    /// </remarks>
    public string MapMember(string tsType, bool optional, IReadOnlyList<TsTypeParameter>? typeParameters = null)
    {
        var previous = _currentTypeParameters;
        _currentTypeParameters = typeParameters is null || typeParameters.Count == 0
            ? null
            : new HashSet<string>(typeParameters.Select(tp => tp.Name), StringComparer.Ordinal);

        try
        {
            var mapped = Map(tsType);
            if (!optional || mapped.EndsWith('?')) return mapped;

            // Nullability annotation; legal on both reference types and value types.
            return mapped + "?";
        }
        finally
        {
            _currentTypeParameters = previous;
        }
    }

    /// <summary>
    /// Maps a TS type expression to a single C# type, widening a union to the narrowest type that holds all
    /// of its members.
    /// </summary>
    public string Map(string tsType) => Collapse(Candidates(tsType));

    /// <summary>
    /// The C# types a TS type can be: one entry for anything but a union, and one per member for a union,
    /// after expanding aliases. So <c>string | PropertyName</c> reports <c>string</c> plus each property-name
    /// node type rather than the alias itself.
    /// </summary>
    /// <remarks>
    /// A union is where TS's type system says "either of these" and C# must choose. Two answers are possible
    /// and <see cref="MapSlot"/> picks between them: widen to one type (<see cref="Collapse"/>) when the
    /// members share a base, or emit C# overloads when they do not.
    /// </remarks>
    public IReadOnlyList<string> Candidates(string tsType)
    {
        var text = CollapseWhitespace(tsType);
        if (text.Length == 0) return ["object?"];

        // A type parameter of the member being emitted is already a legal C# name; mapping it would degrade it
        // to `object` (see MapMember).
        if (_currentTypeParameters?.Contains(text) == true) return [text];

        // Parenthesised groups carry no information once the inner type is mapped.
        var unwrapped = StripOuterParentheses(text);
        if (!ReferenceEquals(unwrapped, text)) return Candidates(unwrapped);

        // A name the port decides by hand wins over the alias it is declared as. `__String` is the case that
        // matters: TS spells it `(string & { … }) | (void & { … }) | InternalSymbolName`, so expanding it
        // would hand back a `void` candidate, and `void` is not a valid C# parameter type.
        if (s_overrides.TryGetValue(text, out var overridden)) return [overridden];

        // A non-generic alias is transparent: `ModifierLike` is `Modifier | Decorator`, and that union is what
        // decides between one widened type and several overloads.
        if (_model.AliasOf(text) is { TypeParameters.Count: 0 } alias) return Candidates(alias.Value);

        // A generic alias is not expanded: substituting nothing for its type parameters would produce a
        // misleading type (see MapReference).
        if (_model.AliasOf(text) is not null) return ["object?"];

        // TS writes long unions with a leading `|` (`type Modifier = | AbstractKeyword | …`), so empty parts
        // have to be dropped before counting: left in, they become an `object?` candidate that poisons every
        // member of the union.
        var parts = SplitTopLevel(text, '|').Select(static part => part.Trim()).Where(static part => part.Length != 0).ToList();
        if (parts.Count == 1 && parts[0] == text) return [MapSingle(text)];

        // `undefined`, `null` and `void` carry no value, and nullability is expressed by the `?` annotation.
        // `never` is the bottom type: `A | never` is just `A`, so it contributes no candidate either.
        var union = parts.Where(static part => part is not ("undefined" or "null" or "void" or "never")).ToList();
        if (union.Count == 0) return ["object?"];
        if (union.Count == 1) return Candidates(union[0]);

        var result = new List<string>();
        foreach (var part in union)
        {
            foreach (var candidate in Candidates(part))
            {
                if (!result.Contains(candidate, StringComparer.Ordinal)) result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>Maps a TS type that is not a union (aliases and parentheses are handled by the caller).</summary>
    private string MapSingle(string text)
    {
        // Tuple types (`[DiagnosticMessage, ...string[]]`) have no C# equivalent; degrade like other
        // structural shapes.
        if (text.StartsWith('[')) return "object?";

        if (text.StartsWith("readonly ", StringComparison.Ordinal))
        {
            var inner = text["readonly ".Length..].Trim();

            // `readonly T[]` maps to List<T>, the same as `T[]`: TS's readonly modifier is a compile-time
            // constraint and the value is an ordinary mutable array at run time. A read-only C# view would also
            // be unusable with the Core array helpers, which take IList<T> for exactly this reason
            // (docs/architecture.md §12.3).
            return inner.EndsWith("[]", StringComparison.Ordinal)
                ? $"List<{Map(inner[..^2])}>"
                : Map(inner);
        }

        var intersection = SplitTopLevel(text, '&').Where(static part => part.Trim().Length != 0).ToList();
        if (intersection.Count > 1) return Map(intersection[0]);

        if (text.EndsWith("[]", StringComparison.Ordinal)) return $"List<{Map(text[..^2])}>";

        if (IsFunctionType(text, out var parameters, out var returnType))
        {
            var mapped = parameters.Select(Map).ToList();
            var mappedReturn = Map(returnType);

            // `void` cannot be a generic argument, so a void-returning function maps to Action, not Func.
            return mappedReturn == "void"
                ? mapped.Count == 0 ? "Action" : $"Action<{string.Join(", ", mapped)}>"
                : $"Func<{string.Join(", ", mapped.Append(mappedReturn))}>";
        }

        return MapReference(text);
    }

    /// <summary>
    /// Widens several candidates to the single C# type a slot can declare: the most derived base the node
    /// types share, or <c>object?</c> when an atom such as <c>string</c> is in the mix and shares nothing.
    /// </summary>
    public string Collapse(IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0) return "object?";
        if (candidates.Count == 1) return candidates[0];

        var (nodes, atoms) = Partition(candidates);
        return atoms.Count == 0 ? Widen(nodes) : "object?";
    }

    /// <summary>
    /// Splits candidates into those the model knows — which therefore have a common base in the C# class
    /// chain — and the atoms that come from TS itself (<c>string</c>, <c>int</c>, a generated enum, a BCL
    /// collection) and share nothing with the node types.
    /// </summary>
    private (List<string> Nodes, List<string> Atoms) Partition(IReadOnlyList<string> candidates)
    {
        var nodes = new List<string>();
        var atoms = new List<string>();

        foreach (var candidate in candidates)
        {
            if (_model.TsNameOf(candidate) is null) atoms.Add(candidate);
            else nodes.Add(candidate);
        }

        return (nodes, atoms);
    }

    /// <summary>The most derived C# type all of <paramref name="nodeTypes"/> share.</summary>
    private string Widen(List<string> nodeTypes)
    {
        var names = nodeTypes
            .Select(_model.TsNameOf)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return TypeName(_model.CommonBase(names));
    }

    /// <summary>
    /// Maps a member slot and works out the C# overloads a union needs.
    /// </summary>
    /// <remarks>
    /// Where TS says "either a string or a node", C# can say it with overloads: one thin overload per
    /// candidate, next to the core signature that takes the wide type. <see cref="MappedSlot.Overloads"/> is
    /// empty whenever the candidates collapse to one type, which is the common case — the widened base covers
    /// them all.
    /// </remarks>
    public MappedSlot MapSlot(string tsType, bool optional, IReadOnlyList<TsTypeParameter>? typeParameters = null)
    {
        var previous = _currentTypeParameters;
        _currentTypeParameters = typeParameters is null || typeParameters.Count == 0
            ? null
            : new HashSet<string>(typeParameters.Select(static tp => tp.Name), StringComparer.Ordinal);

        try
        {
            var candidates = Candidates(tsType);
            var core = Collapse(candidates);
            var overloads = OverloadsOf(candidates, core);
            var note = core == "object?" && overloads.Count == 0 ? DegradeNote(tsType) : null;

            return new MappedSlot(
                Nullable(core, optional),
                overloads.Select(overload => Nullable(overload, optional)).ToList(),
                note);
        }
        finally
        {
            _currentTypeParameters = previous;
        }
    }

    private List<string> OverloadsOf(IReadOnlyList<string> candidates, string core)
    {
        if (candidates.Count <= 1) return [];

        var (nodes, atoms) = Partition(candidates);
        var overloads = new List<string>();
        if (nodes.Count != 0) overloads.Add(Widen(nodes));
        overloads.AddRange(atoms);

        // A single distinct candidate narrower than the core is already the core itself — nothing to overload.
        var distinct = overloads
            .Distinct(StringComparer.Ordinal)
            .Where(overload => overload != core)
            .OrderBy(static overload => overload, StringComparer.Ordinal)
            .ToList();

        return distinct.Count > 1 ? distinct : [];
    }

    /// <summary>Appends the nullability annotation for an optional TS member.</summary>
    private static string Nullable(string type, bool optional)
        => optional && !type.EndsWith('?') ? type + "?" : type;

    private static string CollapseWhitespace(string text)
    {
        if (!text.Any(static c => char.IsWhiteSpace(c))) return text.Trim();

        var sb = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length != 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Returns the contents of a parenthesised group that spans the whole expression.</summary>
    private static string StripOuterParentheses(string text)
    {
        if (text.Length < 2 || text[0] != '(' || text[^1] != ')') return text;

        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i == text.Length - 1 ? text[1..^1].Trim() : text;
            }
        }

        return text;
    }

    /// <summary>
    /// Explains why a slot has no C# type better than <c>object?</c>, phrased for the comment the emitter
    /// writes next to the member.
    /// </summary>
    /// <remarks>
    /// Only consulted when overloads cannot cover the slot: a union whose members share no base is handled by
    /// overloads, not by a note. What is left is a TS shape with no C# spelling at all (<c>any</c>, <c>keyof</c>,
    /// a tuple, an anonymous object type) or a name from outside the files the model is extracted from.
    /// </remarks>
    private string? DegradeNote(string tsType)
    {
        var text = CollapseWhitespace(tsType).Trim();
        if (text.Length == 0) return null;

        switch (text)
        {
            case "any":
            case "unknown":
            case "never":
            case "null":
            case "undefined":
                return $"TS's <c>{text}</c> has no C# counterpart, so the slot stays <c>object?</c>.";
        }

        if (text.StartsWith("keyof ", StringComparison.Ordinal))
            return "TS's <c>keyof</c> has no C# counterpart, so the slot stays <c>object?</c>.";
        if (text.StartsWith('['))
            return "TS's tuple type has no C# counterpart, so the slot stays <c>object?</c>.";
        if (text.StartsWith('{'))
            return "TS's anonymous object type has no C# counterpart, so the slot stays <c>object?</c>.";

        foreach (var name in Identifiers(text))
        {
            if (IsKnownName(name)) continue;
            return $"<c>{name}</c> is declared outside the extracted sources, so the slot stays <c>object?</c>.";
        }

        return null;
    }

    /// <summary>Whether a TS name maps to something the port declares.</summary>
    private bool IsKnownName(string name)
        => s_tsKeywords.Contains(name)
            || _currentTypeParameters?.Contains(name) == true
            || _model.IsKnown(name)
            || _enumNames.Contains(name)
            || s_overrides.ContainsKey(name);

    /// <summary>The identifiers in a TS type expression, in order of appearance, skipping string literals.</summary>
    private static IEnumerable<string> Identifiers(string text)
    {
        var start = -1;
        var quote = '\0';

        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : ' ';

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                start = -1;
                continue;
            }

            if (c is '"' or '\'' or '`')
            {
                quote = c;
                start = -1;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                if (start < 0) start = i;
                continue;
            }

            if (start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }

    /// <summary>TS syntax that appears in a type expression without naming a type.</summary>
    private static readonly HashSet<string> s_tsKeywords = new(StringComparer.Ordinal)
    {
        "readonly", "extends", "keyof", "infer", "in", "is", "typeof", "import", "new", "asserts",
        "string", "number", "boolean", "void", "this", "true", "false", "null", "undefined",
    };

    private string MapReference(string text)
    {
        var lt = text.IndexOf('<');
        if (lt > 0 && text.EndsWith('>'))
        {
            var name = text[..lt].Trim();

            // An alias wins over a type reference: TS writes generic aliases such as
            // `TransformerFactory<T>` whose C# shape is a delegate, not a generic type.
            if (_model.AliasOf(name) is { } genericAlias) return Map(genericAlias.Value);

            var arguments = SplitTopLevel(text[(lt + 1)..^1], ',').Select(Map).ToList();
            return ConvertGeneric(name, arguments);
        }

        if (text.StartsWith("SyntaxKind.", StringComparison.Ordinal)) return "SyntaxKind";

        switch (text)
        {
            case "string": return "string";
            case "number": return "int";
            case "boolean": return "bool";
            case "any":
            case "unknown":
            case "undefined":
            case "null": return "object?";
            case "void": return "void";
            // `never` is TS's bottom type. It shows up as `readonly never[]` for "no elements"; `void` is
            // not usable as a generic argument, so it maps to object like the other unrepresentable shapes.
            case "never": return "object?";
            case "this": return "Node";
            case "true":
            case "false": return "bool";
        }

        if (text.StartsWith('"') || text.StartsWith('\'')) return "string";
        if (char.IsDigit(text[0])) return "int";
        if (text.StartsWith("keyof ", StringComparison.Ordinal)) return "object?";
        if (text.StartsWith('{')) return "object?";

        if (s_overrides.TryGetValue(text, out var overridden)) return overridden;

        // A generated enum: TS enums are nominal and the port keeps them as C# enums, so a member typed by one
        // keeps that type rather than degrading to object. Enums are not in the interface/alias model, hence
        // the separate lookup.
        if (_enumNames.Contains(text)) return text;

        // A non-generic alias is transparent: map its body.
        if (_model.AliasOf(text) is { TypeParameters.Count: 0 } alias) return Map(alias.Value);

        // A *generic* alias is not expanded: substituting nothing for its type parameters would produce a
        // misleading type. The ones the model actually uses are transform-layer callbacks
        // (TransformerFactory<T>, …), which milestone M7 will give real C# shapes.
        if (_model.AliasOf(text) is not null) return "object?";

        if (_model.IsKnown(text))
        {
            // TS allows a bare reference to a generic type, falling back to its declared defaults
            // (`EvaluatorResult<T = string | number | undefined>`). C# has no type-parameter defaults, so the
            // arguments are supplied explicitly.
            if (_model.Interfaces.TryGetValue(text, out var generic) && generic.TypeParameters.Count > 0)
            {
                var arguments = string.Join(", ", Enumerable.Repeat("object?", generic.TypeParameters.Count));
                return $"{TypeName(text)}<{arguments}>";
            }

            return TypeName(text);
        }

        // Unknown name: something declared outside the extracted files. Degrading to object keeps the
        // generated code compiling instead of referencing a type nothing declares.
        return "object?";
    }

    private string ConvertGeneric(string name, List<string> arguments) => name switch
    {
        // A Dictionary key cannot be null, while the TS key type may be nullable.
        "Map" or "ReadonlyMap" => $"Dictionary<{arguments[0].TrimEnd('?')}, {arguments[1]}>",
        "Set" or "ReadonlySet" => $"HashSet<{arguments[0].TrimEnd('?')}>",
        "Array" or "ReadonlyArray" => $"List<{arguments[0]}>",
        // The token family has a runtime `SyntaxKind` rather than a type parameter (see Token.cs), so the
        // argument is dropped: `PunctuationToken<SyntaxKind.CommaToken>` is just a PunctuationToken.
        "Token" or "PunctuationToken" or "KeywordToken" or "ModifierToken" => name,
        // TS's built-in utility types have no C# counterpart worth generating.
        "Required" or "Readonly" or "Partial" or "Pick" or "Omit" or "Record" or "Exclude" or "Extract" => "object?",
        // `MapLike<T>` is declared in corePublic.ts as a string-keyed object; that is a Dictionary here.
        "MapLike" => $"Dictionary<string, {arguments[0]}>",
        // `MultiMap<K, V>` is a `Map<K, V[]>` in core.ts; spelled out because the port has no MultiMap type.
        "MultiMap" => $"Dictionary<{arguments[0].TrimEnd('?')}, List<{arguments[1]}>>",
        // `IterableIterator<T>` is IEnumerable in C#: the port only ever enumerates these. Returning IEnumerator
        // would lose the ability to enumerate twice, which TS's iterators allow.
        "IterableIterator" or "Iterator" or "Iterable" => $"IEnumerable<{arguments[0]}>",
        // Declared outside the extracted files; see MapReference for why unknown generics degrade.
        "ResolutionNameAndModeGetter" => "object?",
        // Everything else is a model type. The name goes through TypeName, which is what puts the `I` prefix on a
        // mixin interface — without it `KeywordTypeNode<TKind>` came out unprefixed while the generator emitted
        // `IKeywordTypeNode<TKind>`.
        _ => $"{TypeName(name)}<{string.Join(", ", arguments)}>",
    };

    /// <summary>The C# name for a TS type: mixin interfaces get the <c>I</c> prefix (porting decision D1).</summary>
    public string TypeName(string tsName)
        => _model.RoleOf(tsName) == TypeRole.Interface ? "I" + tsName : tsName;

    /// <summary>Strips generic arguments from a type reference.</summary>
    public static string BareName(string typeReference)
    {
        var lt = typeReference.IndexOf('<');
        return (lt < 0 ? typeReference : typeReference[..lt]).Trim();
    }

    /// <summary>Splits on a separator that sits at bracket depth zero.</summary>
    /// <remarks>
    /// <c>=&gt;</c> has to be skipped explicitly: its <c>&gt;</c> is not a closing angle bracket, and treating
    /// it as one drives the depth negative so that every later separator looks nested — which silently
    /// merges a function type's remaining parameters into the previous parameter.
    /// </remarks>
    public static List<string> SplitTopLevel(string text, char separator)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '=' && i + 1 < text.Length && text[i + 1] == '>')
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '<' or '(' or '[' or '{':
                    depth++;
                    break;
                case '>' or ')' or ']' or '}':
                    depth--;
                    break;
                default:
                    if (c == separator && depth == 0)
                    {
                        result.Add(text[start..i]);
                        start = i + 1;
                    }

                    break;
            }
        }

        result.Add(text[start..]);
        return result;
    }

    /// <summary>
    /// Recognises <c>(a: A, b: B) =&gt; R</c>. TS writes the parameter names inline, C# has no place for
    /// them in a <see cref="Func{TResult}"/>, so only the types survive.
    /// </summary>
    private static bool IsFunctionType(string text, out List<string> parameters, out string returnType)
    {
        parameters = [];
        returnType = "void";

        if (!text.StartsWith('(')) return false;

        var depth = 0;
        var close = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0)
            {
                close = i;
                break;
            }
        }

        if (close < 0) return false;

        var after = text[(close + 1)..].TrimStart();
        if (!after.StartsWith("=>", StringComparison.Ordinal)) return false;

        returnType = after[2..].Trim();
        var inner = text[1..close].Trim();

        if (inner.Length != 0)
        {
            foreach (var parameter in SplitTopLevel(inner, ','))
            {
                var colon = parameter.IndexOf(':');
                parameters.Add(colon < 0 ? "object?" : parameter[(colon + 1)..].Trim());
            }
        }

        return true;
    }
}

/// <summary>
/// A mapped member slot: the C# type its core signature declares, the overloads a union needs, and — when the
/// slot could not be mapped to anything better — the reason, for the comment next to the member.
/// </summary>
/// <remarks>
/// <see cref="Overloads"/> is empty in the common case, where the candidates collapse to one type. When it is
/// not, the core signature takes the wide type and every overload is a thin narrowing of it, so the
/// hand-written implementation needs one body per member rather than one per overload.
/// </remarks>
internal readonly record struct MappedSlot(string Type, IReadOnlyList<string> Overloads, string? Note);
