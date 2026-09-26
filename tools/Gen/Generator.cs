namespace TypeScript.Compiler.Tools;

/// <summary>A generated file: its spec, path relative to the repo root, and full content.</summary>
internal sealed record GeneratedFile(EnumSpec Spec, string RelativePath, string Content);

/// <summary>
/// Drives generation of the C# data tables from a TypeScript checkout.
/// </summary>
/// <remarks>
/// Kept free of console I/O so the unit tests can call <see cref="Generate"/> and compare the result
/// against what is committed, which is what makes the "generated files are checked in" decision safe.
/// </remarks>
internal static class Generator
{
    /// <summary>
    /// The enums we mirror. <c>UnderlyingType</c> is the type expected to hold the values; generation
    /// widens it automatically if a member does not fit.
    /// </summary>
    public static readonly EnumSpec[] Specs =
    [
        new("SyntaxKind", "src/compiler/types.ts", "int", Flags: false,
            "Syntax kinds, mirroring TS's <c>SyntaxKind</c> const enum. Values match TS exactly; sentinel members such as <c>FirstStatement</c> are aliases and therefore share their target's value."),
        new("NodeFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Node flags set by the parser, binder and transformer."),
        new("ModifierFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Modifier flags for declarations."),
        new("JsxFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags describing how a JSX element's tag was resolved."),
        new("SymbolFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags describing what a symbol is and where it comes from."),
        new("CheckFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags describing the state of a symbol's type checking."),
        new("NodeCheckFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags recording what the checker has determined about a node."),
        new("TypeFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags describing the shape of a type."),
        new("SignatureFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags describing properties of a signature."),
        new("TransformFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags describing what a subtree contains, used to skip unnecessary transforms."),
        new("EmitFlags", "src/compiler/types.ts", "uint", Flags: true,
            "Flags that guide the emitter for a single node."),

        // 字符串值枚举。TS 把它们声明为 <c>const enum</c>，但成员值是字符串，调用点一律当作 string 使用，
        // 因此生成的是常量类而非 C# enum —— 理由见 EnumEmitter.EmitStringTable 的 remarks。
        new("Extension", "src/compiler/types.ts", null, Flags: false,
            "Well-known file extensions, as used by module resolution and by <c>changeFullExtension</c>."),
        new("InternalSymbolName", "src/compiler/types.ts", null, Flags: false,
            "Names of compiler-internal symbols (the <c>__call</c>-style names that cannot collide with user identifiers)."),
        new("PrivateIdentifierKind", "src/compiler/factory/emitHelpers.ts", null, Flags: false,
            "The single-letter marker the emitter writes to describe an erased <c>#private</c> member."),
        new("ForegroundColorEscapeSequences", "src/compiler/program.ts", null, Flags: false,
            "ANSI escape sequences used when colorizing compiler output."),
    ];

    /// <summary>Default output root for generated files, relative to the repo root.</summary>
    public const string DefaultOutputRoot = "src/TypeScript.Compiler";

    /// <summary>Generates every table in <see cref="Specs"/> from <paramref name="tsRoot"/>.</summary>
    public static List<GeneratedFile> Generate(string tsRoot, string outputRoot = DefaultOutputRoot, Action<string>? warn = null)
    {
        var files = new List<GeneratedFile>(Specs.Length);

        foreach (var spec in Specs)
        {
            var tsPath = Path.Combine(tsRoot, spec.TsRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(tsPath))
            {
                throw new FileNotFoundException($"TypeScript source not found: {tsPath}", tsPath);
            }

            var parsed = TsEnumParser.Parse(File.ReadAllText(tsPath), spec.Name);
            var effectiveSpec = WidenIfNeeded(spec, parsed, warn);
            var content = EnumEmitter.Emit(parsed, effectiveSpec);

            files.Add(new GeneratedFile(
                effectiveSpec,
                $"{outputRoot}/Model/{spec.Name}.cs",
                content));
        }

        return files;
    }

    private static EnumSpec WidenIfNeeded(EnumSpec spec, TsEnum parsed, Action<string>? warn)
    {
        // String-valued enums have no width.
        if (spec.UnderlyingType is null) return spec;

        var max = parsed.MaxValue;
        var required = max <= int.MaxValue ? "int"
            : max <= uint.MaxValue ? "uint"
            : "ulong";

        if (Rank(required) <= Rank(spec.UnderlyingType)) return spec;

        warn?.Invoke(
            $"{spec.Name} needs '{required}' (max 0x{max:X}) but was declared '{spec.UnderlyingType}'; widening");
        return spec with { UnderlyingType = required };

        static int Rank(string type) => type switch
        {
            "int" => 0,
            "uint" => 1,
            "ulong" => 2,
            _ => throw new InvalidOperationException($"unsupported underlying type '{type}'"),
        };
    }
}
