namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:1709 (filesByName's tri-state value), 1718 (projectReferenceRedirects' tri-state value)
// ported from: src/compiler/program.ts:1594-1601, 1641-1642, 1684 (the worker slots' signatures)
/// <summary>
/// The few shapes <c>program.ts</c> needs that the generated model does not carry: the two tri-state map values and
/// the three resolution-worker delegates.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS writes these as inline unions (<c>SourceFile | false | undefined</c>,
/// <c>ResolvedProjectReference | false</c>) and anonymous function types. C# needs a named type for each, and the
/// names below keep the reference's own names.
/// </remarks>
internal static class ProgramModel
{
}

/// <summary>ported from: src/compiler/program.ts:1709 — the three states of a <c>filesByName</c> value.</summary>
internal enum FileByNameState
{
    /// <summary>TS's <c>undefined</c>: this path has not been looked at.</summary>
    Unset,

    /// <summary>TS's <c>false</c>: this path was looked at and no file was found.</summary>
    Missing,

    /// <summary>The file that was found.</summary>
    File,
}

/// <summary>ported from: src/compiler/program.ts:1709 — <c>filesByName</c>'s value, <c>SourceFile | false | undefined</c>.</summary>
/// <remarks>
/// PORT-NOTE: a dictionary value cannot hold <c>undefined</c> and <c>false</c> apart, so the two "no file" states are
/// explicit here. The <c>SourceFile?</c> conversion lets the call sites keep the reference's
/// <c>filesByName.set(fileName, file)</c> shape.
/// </remarks>
internal sealed class FileByNameEntry
{
    /// <summary>TS's <c>undefined</c> — the path has not been looked at.</summary>
    public static readonly FileByNameEntry Unset = new() { State = FileByNameState.Unset };

    /// <summary>TS's <c>false</c> — the path was looked at and no file was found.</summary>
    public static readonly FileByNameEntry Missing = new() { State = FileByNameState.Missing };

    public FileByNameState State { get; private init; }

    public SourceFile? File { get; private init; }

    public static FileByNameEntry Of(SourceFile? file)
        => file is null ? Missing : new FileByNameEntry { State = FileByNameState.File, File = file };

    public static implicit operator FileByNameEntry?(SourceFile? file) => file is null ? null : Of(file);

    public SourceFile? ToSourceFile() => State == FileByNameState.File ? File : null;
}

/// <summary>ported from: src/compiler/program.ts:1718 — <c>projectReferenceRedirects</c>' value,
/// <c>ResolvedProjectReference | false</c>.</summary>
/// <remarks>PORT-NOTE: <c>false</c> is "this reference resolved to nothing", which the reference's callers distinguish
/// from "no entry"; the flag keeps that distinction.</remarks>
internal sealed class ProjectReferenceRedirect
{
    /// <summary>The <c>false</c> arm.</summary>
    public static readonly ProjectReferenceRedirect Missing = new() { IsMissing = true };

    public bool IsMissing { get; private init; }

    public IResolvedProjectReference? ResolvedRef { get; private init; }

    public static ProjectReferenceRedirect Of(IResolvedProjectReference? resolvedRef)
        => resolvedRef is null ? Missing : new ProjectReferenceRedirect { ResolvedRef = resolvedRef };
}

/// <summary>ported from: src/compiler/program.ts:1594-1600 — <c>actualResolveModuleNamesWorker</c>'s signature.</summary>
/// <remarks>PORT-NOTE: TS's optional parameters (<c>options?</c>, <c>reusedNames?</c>) are explicit here because the
/// worker is a delegate; every call site passes them.</remarks>
internal delegate List<IResolvedModuleWithFailedLookupLocations> ResolveModuleNamesWorker(
    List<LiteralExpression> moduleNames,
    string containingFile,
    IResolvedProjectReference? redirectedReference,
    ICompilerOptions options,
    SourceFile containingSourceFile,
    List<LiteralExpression>? reusedNames);

/// <summary>ported from: src/compiler/program.ts:1641-1650 — <c>actualResolveTypeReferenceDirectiveNamesWorker</c>'s
/// signature.</summary>
/// <remarks>PORT-NOTE: TS instantiates this with <c>T extends FileReference | string</c>; C# erases the union to
/// <c>object</c>, and the one caller that needs the typed list casts inside its own arm (the overloads of
/// <c>resolveTypeReferenceDirectiveNamesWorker</c> keep the two typed shapes).</remarks>
internal delegate List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveNamesWorker(
    object typeDirectiveNames,
    string containingFile,
    IResolvedProjectReference? redirectedReference,
    ICompilerOptions options,
    SourceFile? containingSourceFile,
    object? reusedNames);

/// <summary>ported from: src/compiler/program.ts:1684 — <c>actualResolveLibrary</c>'s signature.</summary>
internal delegate IResolvedModuleWithFailedLookupLocations ResolveLibraryWorker(
    string libraryName,
    string resolveFrom,
    ICompilerOptions options,
    string libFileName);

// ported from: src/compiler/types.ts:4307-4315 (`RedirectInfo`) and :4699-4702 (`LibResolution<T>`)
/// <summary>
/// The two <c>types.ts</c> shapes <c>program.ts</c> fills with object literals — the same treatment §15.9 gives every
/// such interface: a constructible class beside the generated interface.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares both slots of <c>RedirectInfo</c> <c>readonly</c>, so the generated interface exposes them
/// getter-only; the class adds setters because the reference constructs it with an object literal
/// (<c>{ redirectTarget, unredirected }</c>) and a C# object initializer needs them. <c>LibResolution&lt;T&gt;</c>
/// carries no constraint in the port because the one call site instantiates it as <c>LibResolution&lt;object?&gt;</c>,
/// which TS's <c>T extends ResolvedModuleWithFailedLookupLocations</c> would reject — the generated interface is
/// unconstrained for the same reason.
/// </remarks>
internal sealed class RedirectInfo : IRedirectInfo
{
    public SourceFile? RedirectTarget { get; set; }

    public SourceFile? Unredirected { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:4699-4702 — <c>LibResolution&lt;T&gt;</c>.</summary>
internal sealed class LibResolution<T> : ILibResolution<T>
{
    public T? Resolution { get; set; }

    public string? Actual { get; set; }
}

/// <summary>ported from: src/compiler/utilities.ts:6614-6620 — <c>EmitFileNames</c>.</summary>
/// <remarks>
/// PORT-NOTE: the generator emits interfaces from <c>types.ts</c> only (it extracts a few named enums from
/// <c>utilities.ts</c> — see <c>tools/Gen/Generator.cs</c>'s table), so this shape has no generated counterpart to sit
/// beside, and it is declared here because the one M6 member that consumes it,
/// <see cref="Program.ForEachEmittedFile"/>, is the M7 boundary member <c>Program.EmitBoundary.cs</c> carries. M7's
/// emitter (the <c>getOutputPathsFor</c>/<c>getOutputPathsForBundle</c> pair that builds it) must reuse this
/// declaration rather than open a second one.
/// <para>
/// PORT-NOTE: every slot is optional in the reference, hence <c>string?</c>; the two slots <c>verifyEmitFilePath</c>
/// reads are <c>jsFilePath</c> and <c>declarationFilePath</c> (<c>Program.Verify.cs</c>).
/// </para>
/// </remarks>
internal interface IEmitFileNames
{
    string? JsFilePath { get; }

    string? SourceMapFilePath { get; }

    string? DeclarationFilePath { get; }

    string? DeclarationMapPath { get; }

    string? BuildInfoPath { get; }
}
