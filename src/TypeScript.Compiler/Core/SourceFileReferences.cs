namespace TypeScript.Compiler;

// ported from: src/compiler/types.ts:3860-3868 (FileReference, CheckJsDirective), 4278-4281 (AmdDependency)
/// <summary>
/// One of the directive-shaped values a source file carries: a referenced file, a type reference, an AMD dependency, or
/// the `@ts-check`/`@ts-nocheck` directive.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares all three as interfaces and every producer builds them as object literals (the parser's pragma
/// processing is the first). C# needs constructible types, so the three live together here — they are the same kind of
/// value (a plain record the parser fills in and the program reads), which is why they share a file the way
/// <c>Core/TextRange.cs</c> and <c>Core/LineAndCharacter.cs</c> each hold one.
/// <para>
/// PORT-NOTE: TS's <c>TypeReferenceDirective</c> is a *subtype* of <c>FileReference</c> (it adds nothing but a named
/// shape), and the model collapsed it into <c>IFileReference</c>; the port therefore has one <see cref="FileReference"/>
/// class for both lists, with <see cref="ResolutionMode"/> left null for the lib/path cases.
/// </para>
/// </remarks>
internal sealed class FileReference : IFileReference
{
    public FileReference(string fileName, int pos = -1, int end = -1)
    {
        FileName = fileName;
        Pos = pos;
        End = end;
    }

    public string? FileName { get; set; }

    /// <summary>`ModuleKind.ESNext`/`ModuleKind.CommonJS` when a `resolution-mode` argument said so.</summary>
    public object? ResolutionMode { get; set; }

    /// <summary>Whether a `preserve` argument asked for the reference to survive `--noResolve`.</summary>
    public bool? Preserve { get; set; }

    public int Pos { get; set; }

    public int End { get; set; }
}

/// <summary>A `/// &lt;amd-dependency path="…" name="…" /&gt;` pragma.</summary>
internal sealed class AmdDependency : IAmdDependency
{
    public AmdDependency(string? path, string? name = null)
    {
        Path = path;
        Name = name;
    }

    public string? Path { get; set; }

    public string? Name { get; set; }
}

/// <summary>The `@ts-check`/`@ts-nocheck` directive that won for a file.</summary>
internal sealed class CheckJsDirective : ICheckJsDirective
{
    public CheckJsDirective(bool enabled, int pos, int end)
    {
        Enabled = enabled;
        Pos = pos;
        End = end;
    }

    public bool Enabled { get; set; }

    public int Pos { get; set; }

    public int End { get; set; }
}
