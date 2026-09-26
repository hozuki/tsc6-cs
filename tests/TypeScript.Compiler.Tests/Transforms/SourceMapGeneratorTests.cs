#nullable enable

namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/sourcemap.ts:1-823
/// <summary>
/// Acceptance for the source-map writer (M7-B): the printer's source-map path — the mappings a printed file produces,
/// the <c>sources</c>/<c>names</c>/<c>sourcesContent</c> arrays beside them, the base64 VLQ codec that encodes them, the
/// decoder, the <c>sourceMappingURL</c> comment helpers and the document position mapper.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the oracle): every expectation below was measured before it was pinned, with the reference's own
/// <c>lib/typescript.js</c> (6.0.3). The two fixture maps come from
/// <c>ts.transpileModule(source, { fileName: "a.ts", compilerOptions: { target: ts.ScriptTarget.ESNext, sourceMap: true,
/// alwaysStrict: false, inlineSources } })</c> — <c>alwaysStrict: false</c> because the fixture is plain JavaScript, so
/// the reference's transform chain is a no-op and the emitted text and mappings are exactly what the printer produces;
/// the same invocation through <c>node lib/tsc.js --target esnext --sourceMap --alwaysStrict false --inlineSources</c> on a
/// throwaway file under <c>artifacts/</c> wrote the identical <c>.js</c>/<c>.js.map</c> bytes.
/// Every other expectation comes from the reference's own <c>sourcemap.ts</c> members run over the same inputs the tests
/// below build: <c>ts.createSourceMapGenerator</c> (with a host whose <c>getCanonicalFileName</c> is the identity),
/// <c>ts.decodeMappings</c>, <c>ts.tryParseRawSourceMap</c>, <c>ts.getLineInfo</c>, <c>ts.tryGetSourceMappingURL</c>,
/// <c>ts.createDocumentPositionMapper</c>, <c>ts.sameMapping</c> and <c>ts.isSourceMapping</c>.
/// </para>
/// <para>
/// PORT-NOTE (what this slice cannot observe): the emitter's own call to <c>createSourceMapGenerator</c>
/// (<c>Emit/Emitter.cs</c>, an M7-B blocker this slice may not edit) still reports the generator by name, so the emit
/// path that writes <c>.js.map</c> files from a <see cref="Program"/> is not reachable yet — the one-line hook that
/// connects it is named in this slice's report. The tests therefore drive the printer and the generator directly, which
/// is the same pair of objects the emitter passes to each other.
/// </para>
/// </remarks>
public class SourceMapGeneratorTests
{
    /// <summary>The fixture the two maps are measured from: plain JavaScript, so no transform rewrites it.</summary>
    private const string FixtureText = "function f(a) {\n    var x = a + 1;\n    return x;\n}\nclass C {\n    m() { return f(1); }\n}\n";

    /// <summary>ported from: the measured <c>transpileModule</c> output of <see cref="FixtureText"/>.</summary>
    private const string FixtureBody = "function f(a) {\n    var x = a + 1;\n    return x;\n}\nclass C {\n    m() { return f(1); }\n}\n";

    /// <summary>ported from: the measured <c>sourceMapText</c> of <see cref="FixtureText"/>.</summary>
    private const string FixtureMap = "{\"version\":3,\"file\":\"a.js\",\"sourceRoot\":\"\",\"sources\":[\"a.ts\"],\"names\":[],\"mappings\":\"AAAA,SAAS,CAAC,CAAC,CAAC;IACR,IAAI,CAAC,GAAG,CAAC,GAAG,CAAC,CAAC;IACd,OAAO,CAAC,CAAC;AACb,CAAC;AACD,MAAM,CAAC;IACH,CAAC,KAAK,OAAO,CAAC,CAAC,CAAC,CAAC,CAAC,CAAC,CAAC;CACvB\"}";

    /// <summary>ported from: the measured <c>sourceMapText</c> of <see cref="FixtureText"/> with <c>inlineSources</c>.</summary>
    private const string FixtureInlineMap = "{\"version\":3,\"file\":\"a.js\",\"sourceRoot\":\"\",\"sources\":[\"a.ts\"],\"names\":[],\"mappings\":\"AAAA,SAAS,CAAC,CAAC,CAAC;IACR,IAAI,CAAC,GAAG,CAAC,GAAG,CAAC,CAAC;IACd,OAAO,CAAC,CAAC;AACb,CAAC;AACD,MAAM,CAAC;IACH,CAAC,KAAK,OAAO,CAAC,CAAC,CAAC,CAAC,CAAC,CAAC,CAAC;CACvB\",\"sourcesContent\":[\"function f(a) {\\n    var x = a + 1;\\n    return x;\\n}\\nclass C {\\n    m() { return f(1); }\\n}\\n\"]}";

    /// <summary>ported from: the reference's measured <c>getSources()</c> after its measured <c>addSource</c> calls.</summary>
    private static readonly string[] AddSourceSources = ["/proj/src/a.ts", "/proj/src/b.ts", "/proj/src/sub/c.ts"];

    /// <summary>ported from: the reference's measured <c>toString()</c> after the measured <c>addName</c> calls.</summary>
    private const string AddNameMap = "{\"version\":3,\"file\":\"out.js\",\"sourceRoot\":\"\",\"sources\":[],\"names\":[\"alpha\",\"beta\",\"gamma\"],\"mappings\":\"AAAAC,IAAIC;AACJF\"}";

    /// <summary>ported from: the measured <c>addMapping</c> sequence's <c>toString()</c>.</summary>
    private const string MappingShapeMap = "{\"version\":3,\"file\":\"out.js\",\"sourceRoot\":\"\",\"sources\":[\"a.ts\",\"b.ts\"],\"names\":[],\"mappings\":\"AAAG,KAAA;A,ECIKA;;OAHP;ADCCC\"}";

    /// <summary>ported from: the measured <c>setSourceContent</c> case's <c>toString()</c>, whose content exercises every
    /// escape <c>JSON.stringify</c> writes.</summary>
    private const string SourcesContentMap = "{\"version\":3,\"file\":\"out.js\",\"sourceRoot\":\"\",\"sources\":[\"a.ts\",\"b.ts\"],\"names\":[],\"mappings\":\"\",\"sourcesContent\":[null,\"\\\"q\\\" \\\\b \\\\f \\\\n \\\\r \\\\t \\u0000 \\u001f \u007f \u0085 \u00e9\u4e2d\ud83d\ude00 \u2028 \\ud800 \\udfff end\",\"third\"]}";

    /// <summary>ported from: the measured content <see cref="SourcesContentMap"/> holds.</summary>
    private const string TrickyContent = "\"q\" \\b \\f \\n \\r \\t \u0000 \u001f \u007f \u0085 \u00e9\u4e2d\ud83d\ude00 \u2028 \ud800 \udfff end";

    /// <summary>ported from: the measured <c>appendSourceMap</c> result (offsets, source/name renumbering).</summary>
    private const string AppendAllMap = "{\"version\":3,\"file\":\"new.js\",\"sourceRoot\":\"/sourceRoot/\",\"sources\":[\"existing.ts\",\"../root/x.ts\"],\"names\":[],\"mappings\":\";;GCAA,IAAM,CAAC;AACL;;IACD\",\"sourcesContent\":[null,\"x-content\"]}";

    /// <summary>ported from: the measured <c>getSources()</c> of the same case.</summary>
    private static readonly string[] AppendAllSources = ["/proj/src/existing.ts", "/proj/old/../root/x.ts"];

    /// <summary>ported from: the measured <c>appendSourceMap</c> result when the whole map is clipped away.</summary>
    private const string AppendClippedMap = "{\"version\":3,\"file\":\"new.js\",\"sourceRoot\":\"/sourceRoot/\",\"sources\":[],\"names\":[],\"mappings\":\"\"}";

    /// <summary>ported from: the measured <c>appendSourceMap</c> result for a map whose <c>sourceRoot</c> is joined in.</summary>
    private const string AppendSourceRootMap = "{\"version\":3,\"file\":\"m.js\",\"sourceRoot\":\"\",\"sources\":[\"../src/a.ts\"],\"names\":[],\"mappings\":\"AAAA\",\"sourcesContent\":[\"a-content\"]}";

    /// <summary>ported from: the measured <c>getSources()</c> of the same case.</summary>
    private static readonly string[] AppendSourceRootSources = ["/proj/out/../src/a.ts"];

    /// <summary>ported from: the measured <c>appendSourceMap</c> input — the map every append case feeds.</summary>
    private static RawSourceMap OldMap() => new()
    {
        Version = 3,
        File = "old.js",
        SourceRoot = "../root",
        Sources = ["x.ts", "y.ts"],
        Names = ["n0", "n1"],
        Mappings = "AAAA,IAAM,CAAC;AACL;;IACD",
        SourcesContent = ["x-content", null!],
    };

    /// <summary>ported from: the fixture <c>mappings</c> string and its measured decode.</summary>
    private const string DecoderFixtureMappings = "AAAA,SAAS,CAAC,CAAC,CAAC;IACR,IAAI,CAAC,GAAG,CAAC,GAAG,CAAC,CAAC;IACd,OAAO,CAAC,CAAC;AACb,CAAC;AACD,MAAM,CAAC;IACH,CAAC,KAAK,OAAO,CAAC,CAAC,CAAC,CAAC,CAAC,CAAC,CAAC;CACvB";

    private const string DecoderFixtureCompact = "error=-;pos=170;mappings=[0:0/0:0:0;0:9/0:0:9;0:10/0:0:10;0:11/0:0:11;0:12/0:0:12;1:4/0:1:4;1:8/0:1:8;1:9/0:1:9;1:12/0:1:12;1:13/0:1:13;1:16/0:1:16;1:17/0:1:17;1:18/0:1:18;2:4/0:2:4;2:11/0:2:11;2:12/0:2:12;2:13/0:2:13;3:0/0:3:0;3:1/0:3:1;4:0/0:4:0;4:6/0:4:6;4:7/0:4:7;5:4/0:5:4;5:5/0:5:5;5:10/0:5:10;5:17/0:5:17;5:18/0:5:18;5:19/0:5:19;5:20/0:5:20;5:21/0:5:21;5:22/0:5:22;5:23/0:5:23;5:24/0:5:24;6:1/0:6:1]";

    /// <summary>ported from: the reference's measured decode of <c>"AAAAA,QAASA;AACb,OAAO"</c>.</summary>
    private const string DecoderWithNamesCompact = "error=Invalid sourceCharacter found;pos=16;mappings=[0:0/0:0:0@0;0:8/0:0:9@0]";

    // ---------------------------------------------------------------------------------------------
    // The printer's source-map path (the slice's headline behaviour)
    // ---------------------------------------------------------------------------------------------

    /// <summary>The printer writes the measured text, and the generator the measured map for it.</summary>
    /// <remarks>
    /// This is the printer's whole source-map pipeline: <c>setWriter</c> hands the generator to the printer,
    /// <c>setSourceFile</c> registers the source file, and every node's leading/trailing source position becomes one
    /// <c>addMapping</c> call in the order the reference's own <c>emitJsFileOrBundle</c> produces them.
    /// </remarks>
    [Fact]
    public void WritingAFileWithAGenerator_ProducesTheMeasuredTextAndMap()
    {
        var (text, map) = PrintFixture(/*inlineSources*/ false);

        Assert.Equal(FixtureBody, text);
        Assert.Equal(FixtureMap, map);
    }

    /// <summary>With <c>inlineSources</c> the same print also carries the file's text.</summary>
    /// <remarks>This is the <c>setSourceContent</c> half of <c>setSourceMapSource</c> (emitter.ts:6306-6311), whose
    /// content is the whole source text — the value the map's <c>sourcesContent</c> entry is measured from.</remarks>
    [Fact]
    public void WritingAFileWithInlineSources_ProducesTheMeasuredSourcesContent()
    {
        var (text, map) = PrintFixture(/*inlineSources*/ true);

        Assert.Equal(FixtureBody, text);
        Assert.Equal(FixtureInlineMap, map);
    }

    /// <summary>Prints <see cref="FixtureText"/> through a real printer and generator, answering both outputs.</summary>
    /// <remarks>
    /// PORT-NOTE: the fixture's file name and the generator's arguments are the ones the emitter computes for a single
    /// file: the js path is the file's own name with a <c>.js</c> extension (so <c>getBaseFileName</c> answers
    /// <c>a.js</c>), there is no <c>sourceRoot</c>, and the sources directory is the js file's directory
    /// (<c>getSourceMapDirectory</c>, emitter.ts:1046-1049) — here <c>/</c>, because <c>/a.js</c> sits in the root.
    /// </remarks>
    private static (string Text, string Map) PrintFixture(bool inlineSources)
    {
        var host = CreateEmitHost();
        var file = Parser.CreateSourceFile("/a.ts", FixtureText, ScriptTarget.ESNext, /*setParentNodes*/ true, ScriptKind.TS);
        var generator = SourceMap.CreateSourceMapGenerator(host, "a.js", "", "/", new SourceMapGeneratorOptions());
        var writer = Utilities.CreateTextWriter("\n");
        var printer = new Printer(new PrinterOptions
        {
            SourceMap = true,
            InlineSources = inlineSources,
            Target = ScriptTarget.ESNext,
            NewLine = NewLineKind.LineFeed,
        });

        printer.WriteFile(file, writer, generator);
        return (writer.GetText(), generator.ToString());
    }

    // ---------------------------------------------------------------------------------------------
    // The generator's own members (sourcemap.ts:86-360)
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>addSource</c> indexes the relative path it computes, once per distinct path.</summary>
    [Fact]
    public void AddSource_DedupesByTheRelativePathItComputes()
    {
        var generator = CreateGenerator();

        // The repeated absolute path, the same path spelled with a `.` segment, and a distinct path: the measured
        // indices are 0, 1, 0, 0, 2 — the two spellings of `a.ts` are one source.
        Assert.Equal(0, generator.AddSource("/proj/src/a.ts"));
        Assert.Equal(1, generator.AddSource("/proj/src/b.ts"));
        Assert.Equal(0, generator.AddSource("/proj/src/a.ts"));
        Assert.Equal(0, generator.AddSource("/proj/src/./a.ts"));
        Assert.Equal(2, generator.AddSource("/proj/src/sub/c.ts"));
        Assert.Equal(AddSourceSources, generator.GetSources());
    }

    /// <summary><c>addName</c> indexes names and the mappings that refer to them encode those indices.</summary>
    [Fact]
    public void AddName_IndexesNamesAndEncodesThemInTheMappings()
    {
        var generator = CreateGenerator();

        Assert.Equal(0, generator.AddName("alpha"));
        Assert.Equal(1, generator.AddName("beta"));
        Assert.Equal(0, generator.AddName("alpha"));
        Assert.Equal(2, generator.AddName("gamma"));

        generator.AddMapping(0, 0, 0, 0, 0, 1);
        generator.AddMapping(0, 4, 0, 0, 4, 2);
        generator.AddMapping(1, 0, 0, 1, 0, 0);

        Assert.Equal(AddNameMap, generator.ToString());
    }

    /// <summary>The mappings a sequence of <c>addMapping</c> calls encodes, including the no-source case.</summary>
    /// <remarks>The middle entries are the interesting ones: a repeated generated position with a different mapped
    /// position replaces the pending mapping, a name-less entry omits the fifth field, and a mapping with no source stops
    /// the record after the generated character.</remarks>
    [Fact]
    public void AddMapping_EncodesTheMeasuredMappings()
    {
        var generator = CreateGenerator();
        generator.AddSource("/proj/src/a.ts");
        generator.AddSource("/proj/src/b.ts");

        generator.AddMapping(0, 0, 0, 0, 0, null);
        generator.AddMapping(0, 0, 0, 0, 3, null); // same generated position, a new source position: replaces the pending one
        generator.AddMapping(0, 5, 0, 0, 3, null);
        generator.AddMapping(1, 0); // no source at all
        generator.AddMapping(1, 2, 1, 4, 8, 0);
        generator.AddMapping(3, 7, 0, 0, 0, null); // a line skip
        generator.AddMapping(3, 7, 1, 1, 1, null); // …and a different source at the same position
        generator.AddMapping(4, 0, 0, 2, 2, 1);

        Assert.Equal(MappingShapeMap, generator.ToString());
    }

    /// <summary><c>setSourceContent</c> pads with <c>null</c> and the map escapes content the way
    /// <c>JSON.stringify</c> does.</summary>
    [Fact]
    public void SetSourceContent_PadsWithNullsAndWritesJsonStringifyEscapes()
    {
        var generator = CreateGenerator();
        generator.AddSource("/proj/src/a.ts");
        generator.AddSource("/proj/src/b.ts");

        generator.SetSourceContent(1, TrickyContent);
        generator.SetSourceContent(0, null); // a null content writes nothing
        generator.SetSourceContent(2, "third"); // an index past the end pads the gap with nulls

        Assert.Equal(SourcesContentMap, generator.ToString());
    }

    /// <summary><c>appendSourceMap</c> offsets and renumbers the mappings of another map.</summary>
    [Fact]
    public void AppendSourceMap_OffsetsRenumbersAndCarriesSourcesContent()
    {
        var generator = CreateGenerator(file: "new.js", sourceRoot: "/sourceRoot/");
        generator.AddSource("/proj/src/existing.ts");

        generator.AppendSourceMap(2, 3, OldMap(), "/proj/old/old.js.map", /*start*/ null, /*end*/ null);

        Assert.Equal(AppendAllMap, generator.ToString());
        Assert.Equal(AppendAllSources, generator.GetSources());
    }

    /// <summary><c>appendSourceMap</c> keeps only the mappings inside the given range.</summary>
    /// <remarks>A range that excludes every mapping still visits them all (the loop <c>continue</c>s) but appends
    /// nothing — and, like the reference, adds no source for them either.</remarks>
    [Fact]
    public void AppendSourceMap_ClipsToTheGivenRange()
    {
        var generator = CreateGenerator(file: "new.js", sourceRoot: "/sourceRoot/");

        generator.AppendSourceMap(0, 0, OldMap(), "/proj/old/old.js.map", new LineAndCharacter { Line = 1, Character = 1 }, new LineAndCharacter { Line = 2, Character = 5 });

        Assert.Equal(AppendClippedMap, generator.ToString());
        Assert.Empty(generator.GetSources());
    }

    /// <summary><c>appendSourceMap</c> joins the appended map's <c>sourceRoot</c> into each source path.</summary>
    [Fact]
    public void AppendSourceMap_JoinsTheAppendedSourceRoot()
    {
        var generator = CreateGenerator(file: "m.js", sourceRoot: "", sourcesDirectoryPath: "/proj/out");
        var nested = new RawSourceMap
        {
            Version = 3,
            File = "old.js",
            SourceRoot = "../src",
            Sources = ["a.ts"],
            Names = [],
            Mappings = "AAAA",
            SourcesContent = ["a-content"],
        };

        generator.AppendSourceMap(0, 0, nested, "/proj/out/old.js.map", /*start*/ null, /*end*/ null);

        Assert.Equal(AppendSourceRootMap, generator.ToString());
        Assert.Equal(AppendSourceRootSources, generator.GetSources());
    }

    // ---------------------------------------------------------------------------------------------
    // The decoder (sourcemap.ts:436-616)
    // ---------------------------------------------------------------------------------------------

    /// <summary>The decoder answers the measured mapping list of the fixture map.</summary>
    [Fact]
    public void DecodeMappings_DecodesTheFixtureMap()
    {
        Assert.Equal(DecoderFixtureCompact, DecodeCompact(DecoderFixtureMappings));
    }

    /// <summary>The decoder stops with the reference's message, position and partial state on malformed input.</summary>
    /// <remarks>
    /// Each case is a measured pair of the decoder's own <c>error</c>/<c>pos</c> and the mappings it produced before
    /// failing — <c>""</c> for the three that end immediately after the generated character, and the partial list for
    /// the ones that fail later. The three <c>null</c>-error cases are the interesting edge of the format: a segment
    /// that ends exactly at the end of the string is complete, and <c>"AAAAA,A"</c> is two valid mappings (the second
    /// with no source at all).
    /// </remarks>
    [Fact]
    public void DecodeMappings_ReportsTheReferenceErrors()
    {
        // A truncated VLQ at the end of the generated character, an invalid base64 character in the middle and at the
        // end, a negative generated character, and a segment with too few fields.
        Assert.Equal("error=Unsupported Format: No entries after sourceLine;pos=3;mappings=[]", DecodeCompact("AAA"));
        Assert.Equal("error=Invalid character in VLQ;pos=2;mappings=[]", DecodeCompact("AA*A"));
        Assert.Equal("error=Invalid character in VLQ;pos=1;mappings=[]", DecodeCompact("A!"));
        Assert.Equal("error=Invalid generatedCharacter found;pos=1;mappings=[]", DecodeCompact("D"));
        Assert.Equal("error=Unsupported Format: No entries after sourceLine;pos=3;mappings=[]", DecodeCompact("AAC"));

        // A name index followed by another field, and a name index that runs past the end of the string.
        Assert.Equal("error=Unsupported Error Format: Entries after nameIndex;pos=5;mappings=[]", DecodeCompact("AAAAAA"));

        // No error: a complete five-field segment at the very end of the string, a four-field one, two segments, and
        // input that is only delimiters.
        Assert.Equal("error=-;pos=5;mappings=[0:0/0:0:0@0]", DecodeCompact("AAAAA"));
        Assert.Equal("error=-;pos=4;mappings=[0:0/0:0:1]", DecodeCompact("AAAC"));
        Assert.Equal("error=-;pos=7;mappings=[0:0/0:0:0@0;0:0]", DecodeCompact("AAAAA,A"));
        Assert.Equal("error=-;pos=6;mappings=[]", DecodeCompact(",,,;;,"));
        Assert.Equal("error=-;pos=0;mappings=[]", DecodeCompact(""));

        // The error keeps the accumulator's state, which is where the negative source character came from.
        Assert.Equal(DecoderWithNamesCompact, DecodeCompact("AAAAA,QAASA;AACb,OAAO"));
    }

    /// <summary><c>sameMapping</c> compares by identity or by value, and <c>isSourceMapping</c> tests the three
    /// members a source mapping needs.</summary>
    [Fact]
    public void SameMappingAndIsSourceMapping_MatchTheReference()
    {
        var left = FirstMapping("AAAA");
        var right = FirstMapping("AAAA");

        Assert.True(SourceMap.SameMapping(left, left));
        Assert.True(SourceMap.SameMapping(left, right));
        Assert.False(SourceMap.SameMapping(left, new Mapping { GeneratedLine = 0, GeneratedCharacter = 1 }));

        Assert.True(SourceMap.IsSourceMapping(left));
        Assert.False(SourceMap.IsSourceMapping(new Mapping { GeneratedLine = 0, GeneratedCharacter = 0 }));
    }

    // ---------------------------------------------------------------------------------------------
    // The map guard and parser (sourcemap.ts:404-434)
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>tryParseRawSourceMap</c> accepts exactly the maps <c>isRawSourceMap</c> describes.</summary>
    /// <remarks>
    /// The compact form is the parsed value's members (<c>version</c>, <c>file</c>, <c>sourceRoot</c>, <c>sources</c>,
    /// <c>names</c>, <c>sourcesContent</c>, <c>mappings</c>) or <c>-</c> for the reference's <c>undefined</c> — the
    /// answer for a map missing a required member, with a wrong version, with a non-string entry in <c>sources</c> or
    /// <c>names</c>, with a non-string/null entry in <c>sourcesContent</c>, and for input that is not JSON at all.
    /// </remarks>
    [Fact]
    public void TryParseRawSourceMap_AcceptsOnlyTheGuardedShapes()
    {
        Assert.Equal("version=3;file=a.js;sourceRoot=\"\";sources=[a.ts];names=[];content=-;mappings=\"AAAA\"",
            ParseCompact(SourceMap.TryParseRawSourceMap("{\"version\":3,\"file\":\"a.js\",\"sourceRoot\":\"\",\"sources\":[\"a.ts\"],\"names\":[],\"mappings\":\"AAAA\"}")));

        // `sourcesContent` keeps a `null` element, and `sourceRoot` may be absent.
        Assert.Equal("version=3;file=a.js;sourceRoot=-;sources=[a.ts];names=[n];content=[x,null];mappings=\"AAAA\"",
            ParseCompact(SourceMap.TryParseRawSourceMap("{\"version\":3,\"file\":\"a.js\",\"sources\":[\"a.ts\"],\"names\":[\"n\"],\"mappings\":\"AAAA\",\"sourcesContent\":[\"x\",null]}")));
        Assert.Equal("version=3;file=a.js;sourceRoot=-;sources=[a];names=-;content=[null];mappings=\"\"",
            ParseCompact(SourceMap.TryParseRawSourceMap("{\"version\":3,\"file\":\"a.js\",\"mappings\":\"\",\"sources\":[\"a\"],\"sourcesContent\":[null]}")));

        Assert.Null(SourceMap.TryParseRawSourceMap("{\"version\":2,\"file\":\"a.js\",\"mappings\":\"\"}"));
        Assert.Null(SourceMap.TryParseRawSourceMap("{\"version\":3,\"mappings\":\"\"}"));
        Assert.Null(SourceMap.TryParseRawSourceMap("{\"version\":3,\"file\":\"a.js\",\"mappings\":\"\",\"sources\":[1]}"));
        Assert.Null(SourceMap.TryParseRawSourceMap("{\"version\":3,\"file\":\"a.js\",\"mappings\":\"\",\"sources\":[\"a\"],\"sourcesContent\":[1]}"));
        Assert.Null(SourceMap.TryParseRawSourceMap("{\"version\":3,\"file\":\"a.js\",\"mappings\":\"\",\"names\":[null]}"));
        Assert.Null(SourceMap.TryParseRawSourceMap("not json"));
        Assert.Null(SourceMap.TryParseRawSourceMap("[]"));
        Assert.Null(SourceMap.TryParseRawSourceMap("null"));
    }

    // ---------------------------------------------------------------------------------------------
    // The sourceMappingURL comment helpers (sourcemap.ts:362-402)
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>getLineInfo</c> splits the text the way <c>substring</c> over the line starts does.</summary>
    /// <remarks>A line includes its terminator, and the last line runs to the end of the text — which is why the
    /// reference's <c>getLineText</c> needs no next start for it.</remarks>
    [Fact]
    public void GetLineInfo_SplitsTheTextIntoLines()
    {
        const string text = "var x = 1;\n//# sourceMappingURL=a.js.map\n\n";
        var lineInfo = SourceMap.GetLineInfo(text, ScannerUtilities.ComputeLineStarts(text));

        Assert.Equal(4, lineInfo.GetLineCount());
        Assert.Equal("var x = 1;\n", lineInfo.GetLineText(0));
        Assert.Equal("//# sourceMappingURL=a.js.map\n", lineInfo.GetLineText(1));
        Assert.Equal("\n", lineInfo.GetLineText(2));
        Assert.Equal("", lineInfo.GetLineText(3));

        const string noNewline = "a\nbb";
        var second = SourceMap.GetLineInfo(noNewline, ScannerUtilities.ComputeLineStarts(noNewline));
        Assert.Equal(2, second.GetLineCount());
        Assert.Equal("a\n", second.GetLineText(0));
        Assert.Equal("bb", second.GetLineText(1));
    }

    /// <summary><c>tryGetSourceMappingURL</c> finds the comment at the end of a file, or answers <c>null</c>.</summary>
    /// <remarks>
    /// The measured answers include the ones that make the scan stop: a line that is neither blank nor a map comment
    /// (the <c>//# other</c> line, and the code line after one) breaks the scan, so a comment above them is not found;
    /// trailing blank lines are skipped; and the value is trimmed only at its end, which is why the
    /// <c>//@ sourceMappingURL= b.js.map</c> case answers <c>" b.js.map"</c> — with the leading space.
    /// </remarks>
    [Fact]
    public void TryGetSourceMappingURL_FindsTheCommentOrAnswersNull()
    {
        Assert.Equal("a.js.map", SourceMappingURLOf("var x = 1;\n//# sourceMappingURL=a.js.map"));
        Assert.Equal(" b.js.map", SourceMappingURLOf("var x = 1;\n//@ sourceMappingURL= b.js.map \n"));
        Assert.Null(SourceMappingURLOf("var x = 1;\n//# sourceMappingURL=c.js.map\n//# other\n"));
        Assert.Null(SourceMappingURLOf("var x = 1;\n//# sourceMappingURL=d.js.map\nvar y = 2;\n"));
        Assert.Equal("e.js.map", SourceMappingURLOf("//# sourceMappingURL=e.js.map\n\n\n"));
        Assert.Null(SourceMappingURLOf("no map here"));
        Assert.Equal("f.js.map", SourceMappingURLOf("//# sourceMappingURL=f.js.map\r\n"));
    }

    private static string? SourceMappingURLOf(string text)
        => SourceMap.TryGetSourceMappingURL(SourceMap.GetLineInfo(text, ScannerUtilities.ComputeLineStarts(text)));

    // ---------------------------------------------------------------------------------------------
    // The document position mapper (sourcemap.ts:654-823)
    // ---------------------------------------------------------------------------------------------

    /// <summary>The mapper answers the closest mapping in both directions, or the position it was given.</summary>
    /// <remarks>
    /// The mappings are <c>AAAA,IAAM,CAAC;AACL;;IACD</c>, which name generated (0,0), (0,4), (0,5), (1,0) and (3,4) and
    /// the source positions (0,0), (0,6), (0,7), (1,2) and (2,1); the generated file and both sources are four
    /// 11-character lines, so every mapped position is a real one. The queries below cover an exact hit, a position
    /// between two mappings (the closest of the two), the first position past the last mapping (answered unchanged), and
    /// a file the map names but has no mappings for.
    /// </remarks>
    [Fact]
    public void CreateDocumentPositionMapper_MapsBothDirections()
    {
        var mapper = CreateMapper("AAAA,IAAM,CAAC;AACL;;IACD", out _);

        Assert.Equal(
            new[] { "/proj/out/a.ts:0", "/proj/out/a.ts:6", "/proj/out/a.ts:7", "/proj/out/a.ts:13", "/proj/out/a.ts:13", "/proj/out/a.ts:23", "/proj/out/a.ts:23", "/proj/out/a.ts:23", "/proj/out/a.ts:23", "/proj/out/old.js:38", "/proj/out/old.js:100" },
            new[] { 0, 4, 5, 7, 11, 13, 20, 36, 37, 38, 100 }.Select(pos => Position(mapper.GetSourcePosition(new DocumentPosition { FileName = "/proj/out/old.js", Pos = pos }))).ToList());

        Assert.Equal(
            new[] { "/proj/out/old.js:0", "/proj/out/old.js:4", "/proj/out/old.js:4", "/proj/out/old.js:5", "/proj/out/old.js:11", "/proj/out/old.js:37", "/proj/out/old.js:37", "/proj/out/old.js:37", "/proj/out/old.js:37", "/proj/out/a.ts:25" },
            new[] { 0, 5, 6, 7, 13, 20, 21, 22, 23, 25 }.Select(pos => Position(mapper.GetGeneratedPosition(new DocumentPosition { FileName = "/proj/out/a.ts", Pos = pos }))).ToList());

        // `b.ts` is named by the map but has no mappings of its own, so the position is answered unchanged.
        Assert.Equal("/proj/out/b.ts:3", Position(mapper.GetGeneratedPosition(new DocumentPosition { FileName = "/proj/out/b.ts", Pos = 3 })));
        Assert.Equal("/proj/src/unknown.ts:3", Position(mapper.GetGeneratedPosition(new DocumentPosition { FileName = "/proj/src/unknown.ts", Pos = 3 })));
    }

    /// <summary>A map that does not decode is logged once and then behaves as an empty map.</summary>
    [Fact]
    public void CreateDocumentPositionMapper_LogsTheDecodeError()
    {
        var mapper = CreateMapper("AA*A", out var logs);

        Assert.Equal("/proj/out/old.js:5", Position(mapper.GetSourcePosition(new DocumentPosition { FileName = "/proj/out/old.js", Pos = 5 })));
        Assert.Equal(new[] { "Encountered error while decoding sourcemap: Invalid character in VLQ" }, logs);
    }

    /// <summary><c>identitySourceMapConsumer</c> answers the position it was given.</summary>
    [Fact]
    public void IdentitySourceMapConsumer_AnswersTheInput()
    {
        Assert.Equal("x:3", Position(SourceMap.IdentitySourceMapConsumer.GetSourcePosition(new DocumentPosition { FileName = "x", Pos = 3 })));
        Assert.Equal("y:4", Position(SourceMap.IdentitySourceMapConsumer.GetGeneratedPosition(new DocumentPosition { FileName = "y", Pos = 4 })));
    }

    /// <summary>Builds the mapper over the measured fixture's two sources and collected logs.</summary>
    private static IDocumentPositionMapper CreateMapper(string mappings, out List<string> logs)
    {
        var files = new Dictionary<string, ISourceFileLike>(StringComparer.Ordinal);
        void AddFile(string name, string text)
        {
            var lineMap = ScannerUtilities.ComputeLineStarts(text);
            files[name] = new SourceMapTestFile(text, lineMap);
        }

        AddFile("/proj/out/old.js", "0123456789\n0123456789\n0123456789\n0123456789\n");
        AddFile("/proj/out/a.ts", "ABCDEFGHIJ\nABCDEFGHIJ\nABCDEFGHIJ\nABCDEFGHIJ\n");
        AddFile("/proj/out/b.ts", "abcdefghij\nabcdefghij\nabcdefghij\nabcdefghij\n");

        var collected = new List<string>();
        var host = new SourceMapTestMapperHost(files, collected);
        var map = new RawSourceMap
        {
            Version = 3,
            File = "old.js",
            Sources = ["a.ts", "b.ts"],
            Names = [],
            Mappings = mappings,
        };

        logs = collected;
        return SourceMap.CreateDocumentPositionMapper(host, map, "/proj/out/old.js.map");
    }

    private static string Position(IDocumentPosition position) => $"{position.FileName}:{position.Pos}";

    /// <summary>The first mapping of a mappings string, which is how the reference's samples are read.</summary>
    private static Mapping FirstMapping(string mappings)
    {
        var decoder = SourceMap.DecodeMappings(mappings);
        decoder.MoveNext();
        return decoder.Current;
    }

    /// <summary>The file-like shape <c>getPositionOfLineAndCharacter</c> reads.</summary>
    private sealed class SourceMapTestFile : ISourceFileLike
    {
        private readonly List<int> _lineMap;

        public SourceMapTestFile(string text, List<int> lineMap)
        {
            Text = text;
            LineMap = lineMap;
            _lineMap = lineMap;
        }

        public string? Text { get; }

        public List<int>? LineMap { get; set; }

        public int GetPositionOfLineAndCharacter(int line, int character, bool? allowEdits) => _lineMap[line] + character;
    }

    /// <summary>The three members <c>createDocumentPositionMapper</c> asks its host for.</summary>
    private sealed class SourceMapTestMapperHost(Dictionary<string, ISourceFileLike> files, List<string> logs) : IDocumentPositionMapperHost
    {
        public ISourceFileLike? GetSourceFileLike(string? fileName) => files.GetValueOrDefault(fileName ?? "");

        public string GetCanonicalFileName(string? path) => path ?? "";

        public void Log(string? text) => logs.Add(text ?? "");
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures shared by the generator tests
    // ---------------------------------------------------------------------------------------------

    /// <summary>A generator over the measured arguments: file <c>out.js</c>, no source root, sources in
    /// <c>/proj/src</c>.</summary>
    private static SourceMapGenerator CreateGenerator(string file = "out.js", string sourceRoot = "", string sourcesDirectoryPath = "/proj/src")
        => SourceMap.CreateSourceMapGenerator(CreateEmitHost(), file, sourceRoot, sourcesDirectoryPath, new SourceMapGeneratorOptions());

    /// <summary>The emit host the printer would run against: a program over the one root file the tests parse.</summary>
    private static IEmitHost CreateEmitHost()
    {
        var options = new FakeCompilerOptions { SuppressOutputPathCheck = true };
        var system = new FakeSystem();
        system.Files["/a.ts"] = "const x = 1;\n";
        var host = Program.CreateCompilerHostWorker(options, /*setParentNodes*/ true, system);
        system.Files[host.GetDefaultLibFileName(options)] = "interface Array<T> { }\n";
        var program = Program.CreateProgram(["/a.ts"], options, host);
        return program.GetEmitHost();
    }

    /// <summary>The decoder's answer as one line: its error, its position and every mapping it produced.</summary>
    /// <remarks>PORT-NOTE: the compact form exists so a test can pin a whole decode as one measured literal; it is a
    /// test-local shape, not part of the port.</remarks>
    private static string DecodeCompact(string mappings)
    {
        var decoder = SourceMap.DecodeMappings(mappings);
        var parts = new List<string>();
        foreach (var mapping in decoder)
        {
            var part = $"{mapping.GeneratedLine}:{mapping.GeneratedCharacter}";
            if (mapping.SourceIndex is not null) part += $"/{mapping.SourceIndex}:{mapping.SourceLine}:{mapping.SourceCharacter}";
            if (mapping.NameIndex is not null) part += $"@{mapping.NameIndex}";
            parts.Add(part);
        }

        return $"error={decoder.Error ?? "-"};pos={decoder.Pos};mappings=[{string.Join(";", parts)}]";
    }

    /// <summary>The parsed map as one line, or <c>-</c> for the reference's <c>undefined</c>.</summary>
    private static string ParseCompact(RawSourceMap? parsed)
    {
        if (parsed is null) return "-";

        static string List(List<string>? values) => values is null ? "-" : "[" + string.Join(",", values.Select(value => value ?? "null")) + "]";
        return $"version={parsed.Version};file={parsed.File};sourceRoot={(parsed.SourceRoot is null ? "-" : "\"" + parsed.SourceRoot + "\"")};sources={List(parsed.Sources)};names={List(parsed.Names)};content={List(parsed.SourcesContent)};mappings=\"{parsed.Mappings}\"";
    }
}
