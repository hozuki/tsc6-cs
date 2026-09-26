namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:1330-1345 — interface CreateSourceFileOptions
/// <summary>
/// What <c>createSourceFile</c> needs besides the text.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this interface *in parser.ts*, which is outside the files the type model is extracted from, so
/// the two <c>SourceFile</c> slots that hold it are <c>object?</c> in the generated class. The port needs the
/// constructible type anyway (the same treatment <c>AutoGenerateInfo</c>/<c>CallBinding</c> got) and the slots stay
/// <c>object?</c> until the generator learns about this file — see §15.10.
/// </remarks>
internal sealed class CreateSourceFileOptions
{
    public required ScriptTarget LanguageVersion { get; init; }

    /// <summary>
    /// The format the file is detected as; usually undefined unless module resolution ran.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>ResolutionMode</c> (<c>ModuleKind.ESNext | ModuleKind.CommonJS | undefined</c>), which
    /// the model maps to <c>object?</c> in the slots that carry it; the port keeps the same shape here so the value can
    /// be assigned to them without a cast.
    /// </remarks>
    public object? ImpliedNodeFormat { get; init; }

    /// <summary>Overrides how module-ness is set for this file.</summary>
    public Action<SourceFile>? SetExternalModuleIndicator { get; init; }

    public List<string>? PackageJsonLocations { get; init; }

    public object? PackageJsonScope { get; init; }

    public JSDocParsingMode JsDocParsingMode { get; init; } = JSDocParsingMode.ParseAll;
}
