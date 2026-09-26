namespace TypeScript.Compiler.Tools;

/// <summary>A generated file: its path relative to the repo root, and its full content.</summary>
internal sealed record GeneratedFile(string RelativePath, string Content);

/// <summary>
/// Drives generation of the C# tables and the node model from the TS type model.
/// </summary>
/// <remarks>
/// Free of console I/O so the unit tests can call <see cref="Generate"/> and compare the result against
/// what is committed, which is what makes the "generated files are checked in" decision safe.
/// </remarks>
internal static class Generator
{
    /// <summary>Default output root for generated files, relative to the repo root.</summary>
    public const string DefaultOutputRoot = "src/TypeScript.Compiler";

    /// <summary>
    /// The TS files the model is extracted from. Most of it lives in <c>types.ts</c>, but two enums we
    /// mirror are declared elsewhere, and the extractor reads them all with the compiler's own parser.
    /// </summary>
    public static readonly string[] TypeModelSources =
    [
        "src/compiler/types.ts",
        "src/compiler/program.ts",              // ForegroundColorEscapeSequences
        "src/compiler/factory/emitHelpers.ts",  // PrivateIdentifierKind
    ];

    /// <summary>
    /// The enums we mirror. <c>UnderlyingType</c> is the type expected to hold the values; generation widens
    /// it automatically if a member does not fit. A <c>null</c> underlying type means the TS enum is
    /// string-valued and becomes a constants class.
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

        // String-valued enums. TS declares them as `const enum` but every member is a string and every call
        // site passes it where a `string` is expected, so they become constants classes rather than C# enums
        // — a C# enum cannot carry "__call", "export=" or an ANSI escape as its value. See EnumEmitter.
        new("Extension", "src/compiler/types.ts", null, Flags: false,
            "Well-known file extensions, as used by module resolution and by <c>changeFullExtension</c>."),
        new("InternalSymbolName", "src/compiler/types.ts", null, Flags: false,
            "Names of compiler-internal symbols (the <c>__call</c>-style names that cannot collide with user identifiers)."),
        new("PrivateIdentifierKind", "src/compiler/factory/emitHelpers.ts", null, Flags: false,
            "The single-letter marker the emitter writes to describe an erased <c>#private</c> member."),
        new("ForegroundColorEscapeSequences", "src/compiler/program.ts", null, Flags: false,
            "ANSI escape sequences used when colorizing compiler output."),
    ];

    /// <summary>
    /// Enums already written by hand, which generation must not duplicate.
    /// </summary>
    /// <remarks>
    /// <c>CharacterCodes</c> lives in <c>Core/CharacterCodes.cs</c>: its members are used as <c>int</c> codes in
    /// arithmetic, and two of TS's member names (<c>$</c> and <c>_</c>) have no direct C# spelling, so it was
    /// ported by hand rather than through the table emitter.
    /// </remarks>
    private static readonly HashSet<string> s_handWrittenEnums = new(StringComparer.Ordinal) { "CharacterCodes" };

    /// <summary>Generates every table and the node model from the extracted type model.</summary>
    /// <param name="tsRoot">The TypeScript checkout the sources are read from.</param>
    public static List<GeneratedFile> Generate(
        TsModel model,
        string tsRoot,
        string outputRoot = DefaultOutputRoot,
        Action<string>? warn = null)
    {
        var files = new List<GeneratedFile>();
        var configured = Specs.ToDictionary(static s => s.Name, StringComparer.Ordinal);

        // Every enum in the model is emitted, not just the ones with a hand-written spec: the node model
        // references plenty of others (DiagnosticCategory, NodeBuilderFlags, …), and a missing type is a
        // compile error rather than a silent omission.
        foreach (var tsEnum in model.Enums)
        {
            if (s_handWrittenEnums.Contains(tsEnum.Name)) continue;

            var spec = configured.TryGetValue(tsEnum.Name, out var declared) ? declared : DefaultSpecFor(tsEnum);
            var effectiveSpec = WidenIfNeeded(spec, tsEnum, warn);

            files.Add(new GeneratedFile(
                $"{outputRoot}/Model/{tsEnum.Name}.cs",
                EnumEmitter.Emit(tsEnum, effectiveSpec)));
        }

        var typeModel = new TsTypeModel(model.Interfaces, model.Aliases);
        var mapper = new TsTypeMapper(typeModel, model.Enums.Select(static e => e.Name));
        var (interfaceSource, classSource) = NodeEmitter.Emit(typeModel, mapper);

        files.Add(new GeneratedFile($"{outputRoot}/Model/NodeInterfaces.cs", interfaceSource));
        files.Add(new GeneratedFile($"{outputRoot}/Model/NodeClasses.cs", classSource));

        // The diagnostic table and the scanner tables have their own sources (a JSON table and literals in
        // scanner.ts rather than type declarations), but they are generated and checked in the same way, so
        // they belong in the same list.
        files.Add(DiagnosticEmitter.Emit(tsRoot, outputRoot));
        files.Add(ScannerTablesEmitter.Emit(model, outputRoot));
        files.Add(NodeTestsEmitter.Emit(model, outputRoot));

        return files;
    }

    /// <summary>
    /// Defaults for an enum with no hand-written spec: numeric unless the members are strings, flagged when
    /// the values look like independent bits.
    /// </summary>
    private static EnumSpec DefaultSpecFor(TsEnum tsEnum)
    {
        var source = tsEnum.Source ?? TypeModelSources[0];
        var summary = $"ported from {source}: <c>{tsEnum.Name}</c>.";

        if (tsEnum.IsStringValued) return new EnumSpec(tsEnum.Name, source, null, Flags: false, summary);

        var values = tsEnum.Members.Select(static m => m.Value).Where(static v => v > 0).Distinct().ToList();
        var looksLikeFlags = tsEnum.Name.EndsWith("Flags", StringComparison.Ordinal)
            || (values.Count != 0 && values.TrueForAll(static v => (v & (v - 1)) == 0));

        return new EnumSpec(tsEnum.Name, source, "int", looksLikeFlags, summary);
    }

    /// <summary>Widens an enum's underlying type when a member does not fit the declared one.</summary>
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
