namespace TypeScript.Compiler;

// ported from: src/compiler/sourcemap.ts:654-823 — the mapped-position helpers, `createDocumentPositionMapper` and
// `identitySourceMapConsumer`.
//
// PORT-NOTE: the reference's mapper is an object literal of closures over `createDocumentPositionMapper`'s locals; the
// port keeps those locals as locals of the factory and hands the two members to `DocumentPositionMapper` (a class over
// the two delegates), which is the shape a C# `foreach`-free closure can take.

/// <summary>ported from: src/compiler/types.ts:7749-7752 — <c>DocumentPosition</c>.</summary>
/// <remarks>PORT-NOTE: TS builds these as object literals, and `IDocumentPositionMapper`'s two members answer one; the port
/// needs a constructible type (the same situation <see cref="LineAndCharacter"/> records).</remarks>
internal sealed class DocumentPosition : IDocumentPosition
{
    /// <summary>ported from: src/compiler/types.ts:7750 — <c>fileName</c>.</summary>
    public string? FileName { get; set; }

    /// <summary>ported from: src/compiler/types.ts:7751 — <c>pos</c>.</summary>
    public int Pos { get; set; }
}

/// <summary>ported from: src/compiler/sourcemap.ts:654-660 — <c>MappedPosition</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS also declares <c>SourceMappedPosition</c> (sourcemap.ts:662-666) as the narrowing
/// <see cref="IsSourceMappedPosition"/> produces; as with <see cref="Mapping"/>, the port keeps the guard and reads the
/// three members it proves through the <c>!</c> of the values the guard established.
/// </remarks>
internal sealed class MappedPosition
{
    /// <summary>ported from: src/compiler/sourcemap.ts:655 — <c>generatedPosition</c>.</summary>
    public int GeneratedPosition { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:656 — <c>source</c>.</summary>
    public string? Source { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:657 — <c>sourceIndex</c>.</summary>
    public int? SourceIndex { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:658 — <c>sourcePosition</c>.</summary>
    public int? SourcePosition { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:659 — <c>nameIndex</c>.</summary>
    public int? NameIndex { get; set; }
}

/// <summary>ported from: src/compiler/sourcemap.ts:699-817 — the mapper object literal
/// <c>createDocumentPositionMapper</c> builds.</summary>
/// <remarks>PORT-NOTE: TS returns the object literal of the factory's two closures; the port states the same two members
/// over the delegates the factory passes in.</remarks>
internal sealed class DocumentPositionMapper(
    Func<IDocumentPosition, IDocumentPosition> getSourcePosition,
    Func<IDocumentPosition, IDocumentPosition> getGeneratedPosition) : IDocumentPositionMapper
{
    /// <summary>ported from: src/compiler/sourcemap.ts:709 — <c>getSourcePosition</c>.</summary>
    /// <remarks>PORT-NOTE: the generated member takes <c>IDocumentPosition?</c>; the reference's parameter is a
    /// <c>DocumentPosition</c>, so the value it is guaranteed to hold is read as <c>input!</c>.</remarks>
    public IDocumentPosition GetSourcePosition(IDocumentPosition? input) => getSourcePosition(input!);

    /// <summary>ported from: src/compiler/sourcemap.ts:710 — <c>getGeneratedPosition</c>.</summary>
    /// <inheritdoc cref="GetSourcePosition"/>
    public IDocumentPosition GetGeneratedPosition(IDocumentPosition? input) => getGeneratedPosition(input!);
}

internal static partial class SourceMap
{
    /// <summary>ported from: src/compiler/sourcemap.ts:668-671 — <c>isSourceMappedPosition</c>.</summary>
    private static bool IsSourceMappedPosition(MappedPosition value)
        => value.SourceIndex is not null
            && value.SourcePosition is not null;

    /// <summary>ported from: src/compiler/sourcemap.ts:673-677 — <c>sameMappedPosition</c>.</summary>
    private static bool SameMappedPosition(MappedPosition left, MappedPosition right)
        => left.GeneratedPosition == right.GeneratedPosition
            && left.SourceIndex == right.SourceIndex
            && left.SourcePosition == right.SourcePosition;

    /// <summary>ported from: src/compiler/sourcemap.ts:679-684 — <c>compareSourcePositions</c>.</summary>
    private static Comparison CompareSourcePositions(MappedPosition left, MappedPosition right)
    {
        // Compares sourcePosition without comparing sourceIndex
        // since the mappings are grouped by sourceIndex
        Debug.Assert(left.SourceIndex == right.SourceIndex);
        return Core.CompareValues(left.SourcePosition, right.SourcePosition);
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:686-688 — <c>compareGeneratedPositions</c>.</summary>
    private static Comparison CompareGeneratedPositions(MappedPosition left, MappedPosition right)
        => Core.CompareValues(left.GeneratedPosition, right.GeneratedPosition);

    /// <summary>ported from: src/compiler/sourcemap.ts:698-817 — <c>createDocumentPositionMapper</c>.</summary>
    /// <remarks>PORT-NOTE: <c>map.sources.map((source, i) =&gt; [host.getCanonicalFileName(source), i])</c> feeds a
    /// <c>Map</c> whose constructor keeps the last index for a repeated key; the port's loop assigns in the same order,
    /// so a repeated canonical name resolves the same way. <c>some(sourceMappings)</c> reads a list that may be absent
    /// (the reference's <c>undefined</c>), which <see cref="Core.Some{T}(IList{T}?, Func{T, bool}?)"/> already answers
    /// <c>false</c> for.
    /// </remarks>
    internal static IDocumentPositionMapper CreateDocumentPositionMapper(
        IDocumentPositionMapperHost host,
        IRawSourceMap map,
        string mapPath)
    {
        var mapDirectory = Paths.GetDirectoryPath(mapPath);
        var sourceRoot = Js.IsTruthy(map.SourceRoot) ? Paths.GetNormalizedAbsolutePath(map.SourceRoot!, mapDirectory) : mapDirectory;
        var generatedAbsoluteFilePath = Paths.GetNormalizedAbsolutePath(map.File!, mapDirectory);
        var generatedFile = host.GetSourceFileLike(generatedAbsoluteFilePath);
        var sourceFileAbsolutePaths = new List<string>(map.Sources!.Count);
        foreach (var source in map.Sources)
        {
            sourceFileAbsolutePaths.Add(Paths.GetNormalizedAbsolutePath(source, sourceRoot));
        }

        var sourceToSourceIndexMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sourceFileAbsolutePaths.Count; i++)
        {
            sourceToSourceIndexMap[host.GetCanonicalFileName(sourceFileAbsolutePaths[i])] = i;
        }

        List<MappedPosition>? decodedMappings = null;
        IList<MappedPosition>? generatedMappings = null;
        List<IList<MappedPosition>?>? sourceMappingsByIndex = null;

        return new DocumentPositionMapper(GetSourcePosition, GetGeneratedPosition);

        /// <summary>ported from: src/compiler/sourcemap.ts:715-735 — <c>processMapping</c>.</summary>
        MappedPosition ProcessMapping(Mapping mapping)
        {
            var generatedPosition = generatedFile is not null
                ? ScannerUtilities.GetPositionOfLineAndCharacter(generatedFile, mapping.GeneratedLine, mapping.GeneratedCharacter, /*allowEdits*/ true)
                : -1;
            string? source = null;
            int? sourcePosition = null;
            if (IsSourceMapping(mapping))
            {
                var sourceFile = host.GetSourceFileLike(sourceFileAbsolutePaths[mapping.SourceIndex!.Value]);
                source = map.Sources![mapping.SourceIndex!.Value];
                sourcePosition = sourceFile is not null
                    ? ScannerUtilities.GetPositionOfLineAndCharacter(sourceFile, mapping.SourceLine!.Value, mapping.SourceCharacter!.Value, /*allowEdits*/ true)
                    : -1;
            }

            return new MappedPosition
            {
                GeneratedPosition = generatedPosition,
                Source = source,
                SourceIndex = mapping.SourceIndex,
                SourcePosition = sourcePosition,
                NameIndex = mapping.NameIndex,
            };
        }

        /// <summary>ported from: src/compiler/sourcemap.ts:737-752 — <c>getDecodedMappings</c>.</summary>
        List<MappedPosition> GetDecodedMappings()
        {
            if (decodedMappings is null)
            {
                var decoder = DecodeMappings(map.Mappings!);
                var mappings = Core.ArrayFrom(decoder, ProcessMapping);
                if (decoder.Error is not null)
                {
                    host.Log($"Encountered error while decoding sourcemap: {decoder.Error}");
                    decodedMappings = [];
                }
                else
                {
                    decodedMappings = mappings;
                }
            }

            return decodedMappings;
        }

        /// <summary>ported from: src/compiler/sourcemap.ts:754-766 — <c>getSourceMappings</c>.</summary>
        /// <remarks>PORT-NOTE: TS's <c>lists</c> is a sparse array indexed by source index, so an index with no mappings
        /// reads as <c>undefined</c> and an index past the end reads as <c>undefined</c> too; the port states both with a
        /// <c>null</c> slot and the bounds test below.</remarks>
        IList<MappedPosition>? GetSourceMappings(int sourceIndex)
        {
            if (sourceMappingsByIndex is null)
            {
                var lists = new List<List<MappedPosition>?>();
                foreach (var mapping in GetDecodedMappings())
                {
                    if (!IsSourceMappedPosition(mapping)) continue;
                    var index = mapping.SourceIndex!.Value;
                    while (lists.Count <= index)
                    {
                        lists.Add(null);
                    }

                    var list = lists[index];
                    if (list is null)
                    {
                        lists[index] = list = [];
                    }

                    list.Add(mapping);
                }

                sourceMappingsByIndex = [];
                foreach (var list in lists)
                {
                    sourceMappingsByIndex.Add(list is null ? null : Core.SortAndDeduplicate(list, CompareSourcePositions, SameMappedPosition));
                }
            }

            return sourceIndex < sourceMappingsByIndex.Count ? sourceMappingsByIndex[sourceIndex] : null;
        }

        /// <summary>ported from: src/compiler/sourcemap.ts:768-777 — <c>getGeneratedMappings</c>.</summary>
        IList<MappedPosition> GetGeneratedMappings()
        {
            if (generatedMappings is null)
            {
                var list = new List<MappedPosition>();
                foreach (var mapping in GetDecodedMappings())
                {
                    list.Add(mapping);
                }

                generatedMappings = Core.SortAndDeduplicate(list, CompareGeneratedPositions, SameMappedPosition);
            }

            return generatedMappings;
        }

        /// <summary>ported from: src/compiler/sourcemap.ts:779-798 — <c>getGeneratedPosition</c>.</summary>
        /// <remarks>PORT-NOTE: the reference's local <c>sourceMappings</c> shadows its own <c>getSourceMappings</c>; the
        /// port names the local after what it holds. <c>binarySearchKey</c> is <see cref="Core.BinarySearchKey{T,TU}"/>
        /// and the reference's <c>compareValues</c> key comparer is stated as the same call; its key selector parameter
        /// is the reference's <c>getSourcePositionOfMapping</c> (sourcemap.ts:690-692), inlined here because this is its
        /// one call site and <c>Core.BinarySearchKey</c>'s selector also takes the element's index, which the reference's
        /// selector does not read.</remarks>
        IDocumentPosition GetGeneratedPosition(IDocumentPosition loc)
        {
            if (!sourceToSourceIndexMap.TryGetValue(host.GetCanonicalFileName(loc.FileName), out var sourceIndex)) return loc;

            var sourceMappings = GetSourceMappings(sourceIndex);
            if (!Core.Some(sourceMappings)) return loc;

            // PORT-NOTE: `Some` proves the list is present; the reference reads it directly.
            var targetIndex = Core.BinarySearchKey(sourceMappings!, loc.Pos, static (m, _) => m.SourcePosition!.Value, static (a, b) => Core.CompareValues(a, b));
            if (targetIndex < 0)
            {
                // if no exact match, closest is 2's complement of result
                targetIndex = ~targetIndex;
            }

            var mapping = targetIndex < sourceMappings!.Count ? sourceMappings[targetIndex] : null;
            if (mapping is null || mapping.SourceIndex != sourceIndex)
            {
                return loc;
            }

            return new DocumentPosition { FileName = generatedAbsoluteFilePath, Pos = mapping.GeneratedPosition }; // Closest pos
        }

        /// <summary>ported from: src/compiler/sourcemap.ts:800-815 — <c>getSourcePosition</c>.</summary>
        /// <remarks>PORT-NOTE: <c>getGeneratedPositionOfMapping</c> (sourcemap.ts:694-696) is inlined here for the same
        /// reason <see cref="GetGeneratedPosition"/>'s selector is.</remarks>
        IDocumentPosition GetSourcePosition(IDocumentPosition loc)
        {
            var mappings = GetGeneratedMappings();
            if (!Core.Some(mappings)) return loc;

            var targetIndex = Core.BinarySearchKey(mappings, loc.Pos, static (m, _) => m.GeneratedPosition, static (a, b) => Core.CompareValues(a, b));
            if (targetIndex < 0)
            {
                // if no exact match, closest is 2's complement of result
                targetIndex = ~targetIndex;
            }

            var mapping = targetIndex < mappings.Count ? mappings[targetIndex] : null;
            if (mapping is null || !IsSourceMappedPosition(mapping))
            {
                return loc;
            }

            return new DocumentPosition
            {
                FileName = sourceFileAbsolutePaths[mapping.SourceIndex!.Value],
                Pos = mapping.SourcePosition!.Value, // Closest pos
            };
        }
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:819-823 — <c>identitySourceMapConsumer</c>.</summary>
    /// <remarks>PORT-NOTE: TS initializes the constant with the <c>identity</c> function for both members, so both answer
    /// the position they were given; <see cref="Core.Identity{T}"/> is the port's <c>identity</c>.</remarks>
    internal static readonly IDocumentPositionMapper IdentitySourceMapConsumer = new DocumentPositionMapper(Core.Identity, Core.Identity);
}
