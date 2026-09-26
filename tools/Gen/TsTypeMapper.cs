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
    public bool IsValueType(string type)
    {
        var bare = type.TrimEnd('?');
        return !bare.Contains('<') && _valueTypes.Contains(bare);
    }

    /// <summary>Maps a TS type expression, appending <c>?</c> when the member is optional.</summary>
    public string MapMember(string tsType, bool optional)
    {
        var mapped = Map(tsType);
        if (!optional || mapped.EndsWith('?')) return mapped;

        // Nullability annotation; legal on both reference types and value types.
        return mapped + "?";
    }

    public string Map(string tsType)
    {
        // `node.getText()` on a long type keeps its line breaks; collapse them so the shape tests below and
        // the emitted type are both single-line.
        var text = CollapseWhitespace(tsType);
        if (text.Length == 0) return "object?";

        // Parenthesised groups carry no information once the inner type is mapped.
        var unwrapped = StripOuterParentheses(text);
        if (!ReferenceEquals(unwrapped, text)) return Map(unwrapped);

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

        var union = SplitTopLevel(text, '|');
        if (union.Count > 1) return MapUnion(union);

        var intersection = SplitTopLevel(text, '&');
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

    private string MapUnion(List<string> parts)
    {
        var real = parts.Select(static p => p.Trim()).Where(static p => p is not ("undefined" or "null")).ToList();
        if (real.Count == 0) return "object?";
        if (real.Count == 1) return Map(real[0]);

        // `SyntaxKind.A | SyntaxKind.B` is still a SyntaxKind.
        if (real.TrueForAll(static p => p.StartsWith("SyntaxKind.", StringComparison.Ordinal))) return "SyntaxKind";

        // Every member a known node type: widen to their most derived common ancestor.
        var bareNames = real.Select(BareName).Where(static n => n.Length != 0).ToList();
        if (bareNames.Count == real.Count && bareNames.TrueForAll(_model.IsKnown))
        {
            return TypeName(_model.CommonBase(bareNames));
        }

        // Anything else (string | number, literal unions, …) has no C# counterpart narrower than object.
        return "object?";
    }

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

    private static string ConvertGeneric(string name, List<string> arguments) => name switch
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
        // Declared outside the extracted files; see MapReference for why unknown generics degrade.
        "ResolutionNameAndModeGetter" => "object?",
        _ => $"{name}<{string.Join(", ", arguments)}>",
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
