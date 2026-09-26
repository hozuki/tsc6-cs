namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:1258-1264, 6171-6326 — the source-map pipeline of the printer.
//
// PORT-NOTE: the reference declares `sourceMapSource` and `mostRecentlyAddedSourceMapSource` as non-nullable locals that
// start out `undefined` (emitter.ts:1259-1262), which is why it can write `isJsonSourceMapSource(sourceMapSource)` and
// `skipSourceTrivia(source, …)` without a null check. `Printer.State.cs` declares the same slots as
// `ISourceMapSource?` (the port spells `T | undefined` as `T?` — Directory.Build.props sets `Nullable`), so each read
// below is `_sourceMapSource!` / `savedSourceMapSource!`: the value the reference is guaranteed to hold at that point,
// never a fallback.
//
// PORT-NOTE: `SourceMapGenerator` (src/compiler/sourcemap.ts:36-360) is the port's
// `Transforms/SourceMap.Generator.cs`, and `_sourceMapGenerator` is the value the reference's `sourceMapGenerator!`
// is: `setWriter` computes `sourceMapsDisabled = !writer || !sourceMapGenerator` (emitter.ts:1412-1420), so every
// mapping call site below is only reachable while the generator is present, and reads it with `!`. The family stays
// inert while `_sourceMapsDisabled` is true, which is every path the four `Checker.*ToString` helpers take:
// `writeNode`/`writeList` pass no generator (emitter.ts:1342/1350), which is what `printNode`/`printList` go through.
internal sealed partial class Printer
{
    // ---------------------------------------------------------------------------------------------
    // The phase and its two halves (emitter.ts:6171-6214)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:6171-6177 — <c>pipelineEmitWithSourceMaps</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>internal</c>, not <c>private</c>, for the same method-group-identity reason as
    /// <c>PipelineEmitWithComments</c> (emitter.ts:1521, <c>pipelinePhase === pipelineEmitWithSourceMaps</c>).
    /// </remarks>
    internal void PipelineEmitWithSourceMaps(EmitHint hint, Node node)
    {
        var pipelinePhase = GetNextPipelinePhase(PipelinePhase.SourceMaps, hint, node);
        EmitSourceMapsBeforeNode(node);
        pipelinePhase(hint, node);
        EmitSourceMapsAfterNode(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:6178-6194 — <c>emitSourceMapsBeforeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>sourceMapRange.source</c> reads the optional <c>source</c> member of TS's <c>SourceMapRange</c>.
    /// The port's <c>EmitNodes.GetSourceMapRange</c> (Parser/EmitNodes.cs:176) answers <c>ITextRange</c>, the shape both
    /// of its arms share — a range that *is* the node carries no <c>source</c>, exactly as in the reference — so the
    /// member is read back with <c>as ISourceMapRange</c>, and the reference's second read of the same expression
    /// (emitter.ts:6188) reuses the <c>source</c> local instead.
    /// </remarks>
    private void EmitSourceMapsBeforeNode(Node node)
    {
        var emitFlags = Utilities.GetEmitFlags(node);
        var sourceMapRange = EmitNodes.GetSourceMapRange(node);

        // Emit leading sourcemap
        var source = (sourceMapRange as ISourceMapRange)?.Source ?? _sourceMapSource;
        if (
            node.Kind != SyntaxKind.NotEmittedStatement
            && (emitFlags & EmitFlags.NoLeadingSourceMap) == 0
            && sourceMapRange.Pos >= 0
        )
        {
            EmitSourcePos(source!, SkipSourceTrivia(source!, sourceMapRange.Pos));
        }

        if ((emitFlags & EmitFlags.NoNestedSourceMaps) != 0)
        {
            _sourceMapsDisabled = true;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6196-6214 — <c>emitSourceMapsAfterNode</c>.</summary>
    /// <remarks>PORT-NOTE: the <c>sourceMapRange.source</c> read is the one <see cref="EmitSourceMapsBeforeNode"/>
    /// explains.</remarks>
    private void EmitSourceMapsAfterNode(Node node)
    {
        var emitFlags = Utilities.GetEmitFlags(node);
        var sourceMapRange = EmitNodes.GetSourceMapRange(node);

        // Emit trailing sourcemap
        if ((emitFlags & EmitFlags.NoNestedSourceMaps) != 0)
        {
            _sourceMapsDisabled = false;
        }

        if (
            node.Kind != SyntaxKind.NotEmittedStatement
            && (emitFlags & EmitFlags.NoTrailingSourceMap) == 0
            && sourceMapRange.End >= 0
        )
        {
            EmitSourcePos((sourceMapRange as ISourceMapRange)?.Source ?? _sourceMapSource!, sourceMapRange.End);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:6216-6221 — <c>skipSourceTrivia</c>.</summary>
    /// <remarks>Skips trivia such as comments and white-space that can be optionally overridden by the source-map
    /// source.</remarks>
    private int SkipSourceTrivia(ISourceMapSource source, int pos)
        => source.SkipTrivia is not null ? source.SkipTrivia(pos) : ScannerUtilities.SkipTrivia(source.Text!, pos);

    // ---------------------------------------------------------------------------------------------
    // Positions (emitter.ts:6223-6262)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:6223-6242 — <c>emitPos</c>.</summary>
    /// <remarks>
    /// <para>Emits a mapping.</para>
    /// <para>If the position is synthetic (undefined or a negative value), no mapping will be created.</para>
    /// <para>PORT-NOTE: <c>getLineAndCharacterOfPosition</c> (scanner.ts:548-550) is the memoized
    /// <c>sourceMapSource.lineMap ??= getLineStarts(sourceMapSource)</c> plus <c>computeLineAndCharacterOfPosition</c>;
    /// the port's <c>ScannerUtilities.GetLineAndCharacterOfPosition</c> takes an <c>ISourceFileLike</c>, which
    /// <c>ISourceMapSource</c> is not, so the two steps are inlined with the same memo — the shape
    /// <c>Checker.Check.Grammar.cs:1152</c> and <c>Program/Program.DiagnosticsAggregation.cs:430</c> already use.
    /// The reference passes over the optional <c>nameIndex</c> with <c>undefined</c>, which the port's generated
    /// overload spells <c>null</c>.</para>
    /// </remarks>
    private void EmitPos(int pos)
    {
        if (_sourceMapsDisabled || ScannerUtilities.PositionIsSynthesized(pos) || IsJsonSourceMapSource(_sourceMapSource!))
        {
            return;
        }

        var source = _sourceMapSource!;
        var lineStarts = source.LineMap ?? (source.LineMap = ScannerUtilities.ComputeLineStarts(source.Text ?? ""));
        var lineAndCharacter = ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, pos);
        var sourceLine = lineAndCharacter.Line;
        var sourceCharacter = lineAndCharacter.Character;
        _sourceMapGenerator!.AddMapping(
            _writer.GetLine(),
            _writer.GetColumn(),
            _sourceMapSourceIndex,
            sourceLine,
            sourceCharacter,
            /*nameIndex*/ null);
    }

    /// <summary>ported from: src/compiler/emitter.ts:6244-6262 — <c>emitSourcePos</c>.</summary>
    private void EmitSourcePos(ISourceMapSource source, int pos)
    {
        if (source != _sourceMapSource)
        {
            var savedSourceMapSource = _sourceMapSource;
            var savedSourceMapSourceIndex = _sourceMapSourceIndex;
            SetSourceMapSource(source);
            EmitPos(pos);
            ResetSourceMapSource(savedSourceMapSource!, savedSourceMapSourceIndex);
        }
        else
        {
            EmitPos(pos);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Tokens (emitter.ts:6264-6288)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:6264-6288 — <c>emitTokenWithSourceMap</c>.</summary>
    /// <remarks>
    /// <para>Emits a token of a node with possible leading and trailing source maps.</para>
    /// <para>PORT-NOTE: <c>emitNode.tokenSourceMapRanges[token]</c> indexes TS's sparse array by token, which yields
    /// <c>undefined</c> for a hole; the port's <c>EmitNodes.GetTokenSourceMapRange</c> (Parser/EmitNodes.cs:186) is the
    /// helper that reads the padded list the same way, so the range is taken from it (the <c>emitNode</c> local is still
    /// the reference's, for <c>flags</c>). The <c>writer</c> parameter is the reference's <c>(s: string) =&gt; void</c>
    /// (<see cref="Action{T}"/>), and <c>emitCallback</c> is the token writer it dispatches to — the shape
    /// <c>IPrintHandlers.OnEmitSourceMapOfToken</c> declares.</para>
    /// </remarks>
    private int EmitTokenWithSourceMap(Node? node, SyntaxKind token, Action<string> writer, int tokenPos, Func<SyntaxKind, Action<string>, int, int> emitCallback)
    {
        if (_sourceMapsDisabled || node is not null && Utilities.IsInJsonFile(node))
        {
            return emitCallback(token, writer, tokenPos);
        }

        var emitNode = node?.EmitNode;
        var emitFlags = emitNode is not null ? emitNode.Flags : EmitFlags.None;
        var range = node is not null ? EmitNodes.GetTokenSourceMapRange(node, token) : null;
        var source = range?.Source ?? _sourceMapSource;

        tokenPos = SkipSourceTrivia(source!, range is not null ? range.Pos : tokenPos);
        if ((emitFlags & EmitFlags.NoTokenLeadingSourceMaps) == 0 && tokenPos >= 0)
        {
            EmitSourcePos(source!, tokenPos);
        }

        tokenPos = emitCallback(token, writer, tokenPos);

        if (range is not null) tokenPos = range.End;
        if ((emitFlags & EmitFlags.NoTokenTrailingSourceMaps) == 0 && tokenPos >= 0)
        {
            EmitSourcePos(source!, tokenPos);
        }

        return tokenPos;
    }

    // ---------------------------------------------------------------------------------------------
    // The active source (emitter.ts:6290-6326)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:6290-6315 — <c>setSourceMapSource</c>.</summary>
    private void SetSourceMapSource(ISourceMapSource source)
    {
        if (_sourceMapsDisabled)
        {
            return;
        }

        _sourceMapSource = source;

        if (source == _mostRecentlyAddedSourceMapSource)
        {
            // Fast path for when the new source map is the most recently added, in which case
            // we use its captured index without going through the source map generator.
            _sourceMapSourceIndex = _mostRecentlyAddedSourceMapSourceIndex;
            return;
        }

        if (IsJsonSourceMapSource(source))
        {
            return;
        }

        _sourceMapSourceIndex = _sourceMapGenerator!.AddSource(source.FileName);
        if (Js.IsTruthy(_printerOptions.InlineSources))
        {
            _sourceMapGenerator!.SetSourceContent(_sourceMapSourceIndex, source.Text);
        }

        _mostRecentlyAddedSourceMapSource = source;
        _mostRecentlyAddedSourceMapSourceIndex = _sourceMapSourceIndex;
    }

    /// <summary>ported from: src/compiler/emitter.ts:6317-6320 — <c>resetSourceMapSource</c>.</summary>
    private void ResetSourceMapSource(ISourceMapSource source, int sourceIndex)
    {
        _sourceMapSource = source;
        _sourceMapSourceIndex = sourceIndex;
    }

    /// <summary>ported from: src/compiler/emitter.ts:6322-6324 — <c>isJsonSourceMapSource</c>.</summary>
    /// <remarks>PORT-NOTE: <c>fileExtensionIs</c> is <c>Core.Paths.FileExtensionIs</c> (path.ts:120-122) and
    /// <c>Extension.Json</c> is the typed enum member, whose string is <c>.Value</c>.</remarks>
    private bool IsJsonSourceMapSource(ISourceMapSource sourceFile)
        => Paths.FileExtensionIs(sourceFile.FileName!, Extension.Json.Value);
}
