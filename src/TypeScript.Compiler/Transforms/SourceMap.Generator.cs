namespace TypeScript.Compiler;

// ported from: src/compiler/sourcemap.ts:36-360 — `createSourceMapGenerator` and the source map writer it builds.
//
// PORT-NOTE: the reference is a function whose members are closures over `var`s (the file explains the `var`s: they
// avoid TDZ checks, which cost). The port states the same state as the fields of this class and the same members as
// its methods, with the printing-time `<c>Enter</c>/<c>Exit</c>` timer pair held as the one timer both halves call
// (the reference destructures `{ enter, exit }` from it).

/// <summary>ported from: src/compiler/sourcemap.ts:36-360 — <c>SourceMapGenerator</c>.</summary>
internal sealed class SourceMapGenerator : ISourceMapGenerator
{
    private readonly IEmitHost _host;
    private readonly string _file;
    private readonly string _sourceRoot;
    private readonly string _sourcesDirectoryPath;
    private readonly ITimer _timer;

    // Current source map file and its index in the sources list
    private readonly List<string> _rawSources = [];
    private readonly List<string> _sources = [];
    private readonly Dictionary<string, int> _sourceToSourceIndexMap = new(StringComparer.Ordinal);
    private List<string>? _sourcesContent;

    private readonly List<string> _names = [];
    private Dictionary<string, int>? _nameToNameIndexMap;

    /// <summary>The index of the <c>undefined</c> name TS's <c>Map</c> happily keys (<c>appendSourceMap</c> reads a
    /// <c>nameIndex</c> that can be past the end of <c>names</c>).</summary>
    private int? _nullNameIndex;

    private readonly List<char> _mappingCharCodes = [];
    private string _mappings = "";

    // Last recorded and encoded mappings
    private int _lastGeneratedLine;
    private int _lastGeneratedCharacter;
    private int _lastSourceIndex;
    private int _lastSourceLine;
    private int _lastSourceCharacter;
    private int _lastNameIndex;
    private bool _hasLast;

    private int _pendingGeneratedLine;
    private int _pendingGeneratedCharacter;
    private int _pendingSourceIndex;
    private int _pendingSourceLine;
    private int _pendingSourceCharacter;
    private int _pendingNameIndex;
    private bool _hasPending;
    private bool _hasPendingSource;
    private bool _hasPendingName;

    /// <summary>ported from: src/compiler/sourcemap.ts:36-49 — <c>createSourceMapGenerator</c>'s state.</summary>
    internal SourceMapGenerator(
        IEmitHost host,
        string file,
        string sourceRoot,
        string sourcesDirectoryPath,
        SourceMapGeneratorOptions generatorOptions)
    {
        _host = host;
        _file = file;
        _sourceRoot = sourceRoot;
        _sourcesDirectoryPath = sourcesDirectoryPath;
        _timer = Performance.CreateTimerIf(
            Js.IsTruthy(generatorOptions.ExtendedDiagnostics),
            "Source Map",
            "beforeSourcemap",
            "afterSourcemap");
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:52 — <c>getSources</c>.</summary>
    /// <remarks>PORT-NOTE: TS answers its live <c>rawSources</c> array; the port answers the live list it holds, which
    /// is the same observation for the emit path that reads it into <c>SourceMapEmitResult.inputSourceFileNames</c>.</remarks>
    public List<string> GetSources() => _rawSources;

    /// <summary>ported from: src/compiler/sourcemap.ts:86-99 — <c>addSource</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>fileName</c> is a <c>string</c>; the generated member takes
    /// <c>string?</c>, so the value the reference is guaranteed to hold is read as <c>fileName!</c>. A missing file name
    /// is the reference's own failure too — the path helpers it passes the value to dereference it.</remarks>
    public int AddSource(string? fileName)
    {
        _timer.Enter();
        var source = Paths.GetRelativePathToDirectoryOrUrl(
            _sourcesDirectoryPath,
            fileName!,
            Utilities.EmitHostCurrentDirectory(_host),
            (Core.GetCanonicalFileName)_host.GetCanonicalFileName!,
            /*isAbsolutePathAnUrl*/ true);

        if (!_sourceToSourceIndexMap.TryGetValue(source, out var sourceIndex))
        {
            sourceIndex = _sources.Count;
            _sources.Add(source);
            _rawSources.Add(fileName!);
            _sourceToSourceIndexMap.Add(source, sourceIndex);
        }

        _timer.Exit();
        return sourceIndex;
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:101-111 — <c>setSourceContent</c>.</summary>
    /// <remarks>PORT-NOTE: TS pads <c>sourcesContent</c> with <c>null</c> up to (but not including) the index and then
    /// assigns, which appends when the index is the current length and overwrites otherwise; the port states both
    /// cases. A <c>null</c> content is the reference's "no content for this source" and writes nothing.</remarks>
    public void SetSourceContent(int sourceIndex, string? content)
    {
        _timer.Enter();
        if (content is not null)
        {
            _sourcesContent ??= [];
            while (_sourcesContent.Count < sourceIndex)
            {
                _sourcesContent.Add(null!);
            }

            if (_sourcesContent.Count == sourceIndex) _sourcesContent.Add(content);
            else _sourcesContent[sourceIndex] = content;
        }

        _timer.Exit();
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:113-126 — <c>addName</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>name</c> is a <c>string</c> and its <c>Map</c> keys it by value; the
    /// generated member takes <c>string?</c> (the value <c>appendSourceMap</c> can produce when a mapping's
    /// <c>nameIndex</c> is past the end of <c>names</c>), and the CLR's <see cref="Dictionary{TKey,TValue}"/> rejects a
    /// <c>null</c> key, so that one value's index is held separately — TS's <c>Map</c> treats the <c>undefined</c> key as
    /// a single entry, exactly as the second field does.</remarks>
    public int AddName(string? name)
    {
        _timer.Enter();
        int nameIndex;
        if (name is null)
        {
            _nullNameIndex ??= AddNameWorker(null!);
            nameIndex = _nullNameIndex.Value;
        }
        else
        {
            _nameToNameIndexMap ??= new Dictionary<string, int>(StringComparer.Ordinal);
            if (!_nameToNameIndexMap.TryGetValue(name, out nameIndex))
            {
                nameIndex = AddNameWorker(name);
                _nameToNameIndexMap.Add(name, nameIndex);
            }
        }

        _timer.Exit();
        return nameIndex;
    }

    /// <summary>The two statements TS's <c>addName</c> runs when the name is new.</summary>
    private int AddNameWorker(string name)
    {
        var nameIndex = _names.Count;
        _names.Add(name);
        return nameIndex;
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:143 — <c>addMapping</c>'s two-argument overload.</summary>
    /// <remarks>PORT-NOTE: the generated member exists because TS's one signature has four optional parameters; it is the
    /// reference's <c>addMapping(generatedLine, generatedCharacter)</c> — the call whose source triple is
    /// <c>undefined</c> — and reaches the same worker the full member does.</remarks>
    public void AddMapping(int generatedLine, int generatedCharacter)
        => AddMappingWorker(generatedLine, generatedCharacter, /*sourceIndex*/ null, /*sourceLine*/ null, /*sourceCharacter*/ null, /*nameIndex*/ null);

    /// <summary>ported from: src/compiler/sourcemap.ts:143-174 — <c>addMapping</c>.</summary>
    /// <remarks>PORT-NOTE: the generated member states the source triple as non-nullable <c>int</c> where TS's optional
    /// parameters are <c>number | undefined</c>; the reference's <c>undefined</c> arm is the two-argument member above and
    /// <c>appendSourceMap</c>'s "no source" mapping, both of which reach the worker with <c>null</c> — the same
    /// <c>undefined</c> the reference passes. Every other member of the worker is the reference's <c>addMapping</c>
    /// exactly: the three local tests read the same values and commit through <see cref="CommitPendingMapping"/>.</remarks>
    public void AddMapping(
        int generatedLine,
        int generatedCharacter,
        int sourceIndex,
        int sourceLine,
        int sourceCharacter,
        int? nameIndex)
        => AddMappingWorker(generatedLine, generatedCharacter, sourceIndex, sourceLine, sourceCharacter, nameIndex);

    /// <summary>The body of the reference's <c>addMapping</c>, over the <c>undefined</c>-able positions.</summary>
    private void AddMappingWorker(
        int generatedLine,
        int generatedCharacter,
        int? sourceIndex,
        int? sourceLine,
        int? sourceCharacter,
        int? nameIndex)
    {
        Debug.Assert(generatedLine >= _pendingGeneratedLine, "generatedLine cannot backtrack");
        Debug.Assert(generatedCharacter >= 0, "generatedCharacter cannot be negative");
        Debug.Assert(sourceIndex is null || sourceIndex >= 0, "sourceIndex cannot be negative");
        Debug.Assert(sourceLine is null || sourceLine >= 0, "sourceLine cannot be negative");
        Debug.Assert(sourceCharacter is null || sourceCharacter >= 0, "sourceCharacter cannot be negative");
        _timer.Enter();
        // If this location wasn't recorded or the location in source is going backwards, record the mapping
        if (
            IsNewGeneratedPosition(generatedLine, generatedCharacter)
            || IsBacktrackingSourcePosition(sourceIndex, sourceLine, sourceCharacter)
        )
        {
            CommitPendingMapping();
            _pendingGeneratedLine = generatedLine;
            _pendingGeneratedCharacter = generatedCharacter;
            _hasPendingSource = false;
            _hasPendingName = false;
            _hasPending = true;
        }

        if (sourceIndex is not null && sourceLine is not null && sourceCharacter is not null)
        {
            _pendingSourceIndex = sourceIndex.Value;
            _pendingSourceLine = sourceLine.Value;
            _pendingSourceCharacter = sourceCharacter.Value;
            _hasPendingSource = true;
            if (nameIndex is not null)
            {
                _pendingNameIndex = nameIndex.Value;
                _hasPendingName = true;
            }
        }

        _timer.Exit();
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:128-132 — <c>isNewGeneratedPosition</c>.</summary>
    private bool IsNewGeneratedPosition(int generatedLine, int generatedCharacter)
        => !_hasPending
            || _pendingGeneratedLine != generatedLine
            || _pendingGeneratedCharacter != generatedCharacter;

    /// <summary>ported from: src/compiler/sourcemap.ts:134-141 — <c>isBacktrackingSourcePosition</c>.</summary>
    private bool IsBacktrackingSourcePosition(int? sourceIndex, int? sourceLine, int? sourceCharacter)
        => sourceIndex is not null
            && sourceLine is not null
            && sourceCharacter is not null
            && _pendingSourceIndex == sourceIndex
            && (_pendingSourceLine > sourceLine
                || _pendingSourceLine == sourceLine && _pendingSourceCharacter > sourceCharacter);

    /// <summary>ported from: src/compiler/sourcemap.ts:176-238 — <c>appendSourceMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>sourceIndexToNewSourceIndexMap</c>/<c>nameIndexToNewNameIndexMap</c> are sparse numeric
    /// arrays whose holes read as <c>undefined</c>; the port holds <c>int?</c> lists padded up to the index, where a
    /// <c>null</c> slot is exactly the reference's hole. TS reads <c>map.sourcesContent[i]</c> and tests
    /// <c>typeof … === "string"</c>, which an out-of-range read fails; the port states the same test as a bounds guard
    /// and a <c>string</c> pattern.
    /// </remarks>
    public void AppendSourceMap(
        int generatedLine,
        int generatedCharacter,
        IRawSourceMap? sourceMap,
        string? sourceMapPath,
        ILineAndCharacter? start,
        ILineAndCharacter? end)
    {
        Debug.Assert(generatedLine >= _pendingGeneratedLine, "generatedLine cannot backtrack");
        Debug.Assert(generatedCharacter >= 0, "generatedCharacter cannot be negative");
        _timer.Enter();
        // First, decode the old component sourcemap
        var sourceIndexToNewSourceIndexMap = new List<int?>();
        List<int?>? nameIndexToNewNameIndexMap = null;
        var mappingIterator = SourceMap.DecodeMappings(sourceMap!.Mappings!);
        foreach (var raw in mappingIterator)
        {
            if (
                end is not null && (
                    raw.GeneratedLine > end.Line
                    || (raw.GeneratedLine == end.Line && raw.GeneratedCharacter > end.Character)
                )
            )
            {
                break;
            }

            if (
                start is not null && (
                    raw.GeneratedLine < start.Line
                    || (start.Line == raw.GeneratedLine && raw.GeneratedCharacter < start.Character)
                )
            )
            {
                continue;
            }

            // Then reencode all the updated mappings into the overall map
            int? newSourceIndex = null;
            int? newSourceLine = null;
            int? newSourceCharacter = null;
            int? newNameIndex = null;
            if (raw.SourceIndex is not null)
            {
                var rawSourceIndex = raw.SourceIndex.Value;
                while (sourceIndexToNewSourceIndexMap.Count <= rawSourceIndex)
                {
                    sourceIndexToNewSourceIndexMap.Add(null);
                }

                newSourceIndex = sourceIndexToNewSourceIndexMap[rawSourceIndex];
                if (newSourceIndex is null)
                {
                    // Apply offsets to each position and fixup source entries
                    var rawPath = sourceMap.Sources![rawSourceIndex];
                    var relativePath = Js.IsTruthy(sourceMap.SourceRoot) ? Paths.CombinePaths(sourceMap.SourceRoot!, rawPath) : rawPath;
                    var combinedPath = Paths.CombinePaths(Paths.GetDirectoryPath(sourceMapPath!), relativePath);
                    newSourceIndex = AddSource(combinedPath);
                    sourceIndexToNewSourceIndexMap[rawSourceIndex] = newSourceIndex;
                    // PORT-NOTE: the source map's content is `string | null` (see RawSourceMap.SourcesContent); the port
                    // reads the element only when the reference's `typeof … === "string"` holds.
                    if (
                        sourceMap.SourcesContent is not null
                        && rawSourceIndex < sourceMap.SourcesContent.Count
                        && sourceMap.SourcesContent[rawSourceIndex] is string content
                    )
                    {
                        SetSourceContent(newSourceIndex.Value, content);
                    }
                }

                newSourceLine = raw.SourceLine;
                newSourceCharacter = raw.SourceCharacter;
                if (sourceMap.Names is not null && raw.NameIndex is not null)
                {
                    var rawNameIndex = raw.NameIndex.Value;
                    nameIndexToNewNameIndexMap ??= [];
                    while (nameIndexToNewNameIndexMap.Count <= rawNameIndex)
                    {
                        nameIndexToNewNameIndexMap.Add(null);
                    }

                    newNameIndex = nameIndexToNewNameIndexMap[rawNameIndex];
                    if (newNameIndex is null)
                    {
                        newNameIndex = AddName(rawNameIndex < sourceMap.Names.Count ? sourceMap.Names[rawNameIndex] : null);
                        nameIndexToNewNameIndexMap[rawNameIndex] = newNameIndex;
                    }
                }
            }

            var rawGeneratedLine = raw.GeneratedLine - (start is not null ? start.Line : 0);
            var newGeneratedLine = rawGeneratedLine + generatedLine;
            var rawGeneratedCharacter = start is not null && start.Line == raw.GeneratedLine ? raw.GeneratedCharacter - start.Character : raw.GeneratedCharacter;
            var newGeneratedCharacter = rawGeneratedLine == 0 ? rawGeneratedCharacter + generatedCharacter : rawGeneratedCharacter;
            AddMappingWorker(newGeneratedLine, newGeneratedCharacter, newSourceIndex, newSourceLine, newSourceCharacter, newNameIndex);
        }

        _timer.Exit();
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:240-248 — <c>shouldCommitMapping</c>.</summary>
    private bool ShouldCommitMapping()
        => !_hasLast
            || _lastGeneratedLine != _pendingGeneratedLine
            || _lastGeneratedCharacter != _pendingGeneratedCharacter
            || _lastSourceIndex != _pendingSourceIndex
            || _lastSourceLine != _pendingSourceLine
            || _lastSourceCharacter != _pendingSourceCharacter
            || _lastNameIndex != _pendingNameIndex;

    /// <summary>ported from: src/compiler/sourcemap.ts:250-257 — <c>appendMappingCharCode</c>.</summary>
    /// <remarks>PORT-NOTE: TS collects <c>number</c>s and builds the string with
    /// <c>String.fromCharCode.apply</c>; the port collects the <c>char</c> code units and flushes them into a string,
    /// so the chunking exists for the reference's reason only (a bounded buffer), never for an argument-count limit.</remarks>
    private void AppendMappingCharCode(char charCode)
    {
        _mappingCharCodes.Add(charCode);
        // String.fromCharCode accepts its arguments on the stack, so we have to chunk the input,
        // otherwise we can get stack overflows for large source maps
        if (_mappingCharCodes.Count >= 1024)
        {
            FlushMappingBuffer();
        }
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:259-311 — <c>commitPendingMapping</c>.</summary>
    private void CommitPendingMapping()
    {
        if (!_hasPending || !ShouldCommitMapping())
        {
            return;
        }

        _timer.Enter();

        // Line/Comma delimiters
        if (_lastGeneratedLine < _pendingGeneratedLine)
        {
            // Emit line delimiters
            do
            {
                AppendMappingCharCode((char)CharacterCodes.Semicolon);
                _lastGeneratedLine++;
            }
            while (_lastGeneratedLine < _pendingGeneratedLine);

            // Only need to set this once
            _lastGeneratedCharacter = 0;
        }
        else
        {
            Debug.AssertEqual(_lastGeneratedLine, _pendingGeneratedLine, "generatedLine cannot backtrack");
            // Emit comma to separate the entry
            if (_hasLast)
            {
                AppendMappingCharCode((char)CharacterCodes.Comma);
            }
        }

        // 1. Relative generated character
        AppendBase64VLQ(_pendingGeneratedCharacter - _lastGeneratedCharacter);
        _lastGeneratedCharacter = _pendingGeneratedCharacter;

        if (_hasPendingSource)
        {
            // 2. Relative sourceIndex
            AppendBase64VLQ(_pendingSourceIndex - _lastSourceIndex);
            _lastSourceIndex = _pendingSourceIndex;

            // 3. Relative source line
            AppendBase64VLQ(_pendingSourceLine - _lastSourceLine);
            _lastSourceLine = _pendingSourceLine;

            // 4. Relative source character
            AppendBase64VLQ(_pendingSourceCharacter - _lastSourceCharacter);
            _lastSourceCharacter = _pendingSourceCharacter;

            if (_hasPendingName)
            {
                // 5. Relative nameIndex
                AppendBase64VLQ(_pendingNameIndex - _lastNameIndex);
                _lastNameIndex = _pendingNameIndex;
            }
        }

        _hasLast = true;
        _timer.Exit();
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:313-318 — <c>flushMappingBuffer</c>.</summary>
    private void FlushMappingBuffer()
    {
        if (_mappingCharCodes.Count > 0)
        {
            _mappings += new string(_mappingCharCodes.ToArray());
            _mappingCharCodes.Clear();
        }
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:320-332 — <c>toJSON</c>.</summary>
    /// <remarks>PORT-NOTE: TS returns the object literal; the port returns <see cref="RawSourceMap"/>, which carries the
    /// same members in the same order — and, unlike a general-purpose serializer, writes them the way
    /// <c>JSON.stringify</c> does (see <see cref="RawSourceMap.ToString"/>).</remarks>
    public IRawSourceMap ToJSON()
    {
        CommitPendingMapping();
        FlushMappingBuffer();
        return new RawSourceMap
        {
            Version = 3,
            File = _file,
            SourceRoot = _sourceRoot,
            Sources = _sources,
            Names = _names,
            Mappings = _mappings,
            SourcesContent = _sourcesContent,
        };
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:53 — <c>toString</c>.</summary>
    /// <remarks>PORT-NOTE: the generated <see cref="IRawSourceMap"/> inherits <c>object.ToString()</c>'s <c>string?</c>
    /// face; the reference's <c>JSON.stringify</c> always answers a string, and <see cref="RawSourceMap.ToString"/>
    /// always answers one, so the result is read as one.</remarks>
    public override string ToString() => ToJSON().ToString()!;

    /// <summary>ported from: src/compiler/sourcemap.ts:334-360 — <c>appendBase64VLQ</c>.</summary>
    private void AppendBase64VLQ(int inValue)
    {
        // Add a new least significant bit that has the sign of the value.
        // if negative number the least significant bit that gets added to the number has value 1
        // else least significant bit value that gets added is 0
        // eg. -1 changes to binary : 01 [1] => 3
        //     +1 changes to binary : 01 [0] => 2
        if (inValue < 0)
        {
            inValue = ((-inValue) << 1) + 1;
        }
        else
        {
            inValue = inValue << 1;
        }

        // Encode 5 bits at a time starting from least significant bits
        do
        {
            var currentDigit = inValue & 31; // 11111
            inValue = inValue >> 5;
            if (inValue > 0)
            {
                // There are still more digits to decode, set the msb (6th bit)
                currentDigit = currentDigit | 32;
            }

            AppendMappingCharCode(SourceMap.Base64FormatEncode(currentDigit));
        }
        while (inValue > 0);
    }
}
