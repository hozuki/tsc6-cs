using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/sourcemap.ts:1-436, 808-823 — the source map writer's entry point, the
// `sourceMappingURL` comment helpers and the `RawSourceMap` guard/parser.
//
// PORT-NOTE: the reference is one module of closures, object literals and module-level constants; the port keeps that
// shape as a partial static class spread over the file's four concerns:
//   - this file: `createSourceMapGenerator`, the three comment/map regular expressions, `LineInfo`/`getLineInfo`,
//     `tryGetSourceMappingURL`, `isRawSourceMap`/`tryParseRawSourceMap` and the `RawSourceMap` value;
//   - `SourceMap.Generator.cs`: `SourceMapGenerator` itself (sourcemap.ts:36-360);
//   - `SourceMap.Mappings.cs`: the base64 VLQ codec and `decodeMappings` (sourcemap.ts:436-652);
//   - `SourceMap.DocumentPosition.cs`: `createDocumentPositionMapper`/`identitySourceMapConsumer` (sourcemap.ts:654-823).

/// <summary>ported from: src/compiler/sourcemap.ts:31-33 — <c>SourceMapGeneratorOptions</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS declares this as an interface satisfied structurally by the emitter's own <c>SourceMapOptions</c>;
/// C# has no structural typing, so the port states the one member the generator reads as a constructible class (the
/// same treatment <c>PrinterOptions</c>/<c>PrintHandlers</c> record for the printer's option/handler literals).
/// </remarks>
internal sealed class SourceMapGeneratorOptions
{
    /// <summary>ported from: src/compiler/sourcemap.ts:32 — <c>extendedDiagnostics</c>.</summary>
    public bool? ExtendedDiagnostics { get; set; }
}

/// <summary>ported from: src/compiler/sourcemap.ts:370-374 — <c>LineInfo</c>.</summary>
/// <remarks>PORT-NOTE: TS needs only the shape; the port keeps the interface for the shape and states
/// <see cref="SourceMap.LineInfo"/> as the constructible implementation (the split every <c>I*</c>/concrete pair in
/// this port uses).</remarks>
internal interface ILineInfo
{
    /// <summary>ported from: src/compiler/sourcemap.ts:371 — <c>getLineCount</c>.</summary>
    int GetLineCount();

    /// <summary>ported from: src/compiler/sourcemap.ts:372 — <c>getLineText</c>.</summary>
    string GetLineText(int line);
}

/// <summary>ported from: src/compiler/sourcemap.ts:36-84, 362-434 — the source map module's entry points and
/// text-level members.</summary>
internal static partial class SourceMap
{
    // ---------------------------------------------------------------------------------------------
    // The generator (sourcemap.ts:36-84)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sourcemap.ts:36 — <c>createSourceMapGenerator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns the object literal whose members are the closures of this function; the port returns the
    /// generator that holds the same state (<see cref="SourceMapGenerator"/>), so this member is the reference's entry
    /// with its arguments passed through.
    /// </remarks>
    internal static SourceMapGenerator CreateSourceMapGenerator(
        IEmitHost host,
        string file,
        string sourceRoot,
        string sourcesDirectoryPath,
        SourceMapGeneratorOptions generatorOptions)
        => new(host, file, sourceRoot, sourcesDirectoryPath, generatorOptions);

    // ---------------------------------------------------------------------------------------------
    // The comment helpers (sourcemap.ts:362-402)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sourcemap.ts:362-363 — <c>sourceMapCommentRegExpDontCareLineStart</c>.</summary>
    /// <remarks>
    /// <para>Sometimes tools can see the following line as a source mapping url comment, so we mangle it a bit (the
    /// <c>[M]</c>).</para>
    /// <para>PORT-NOTE: JS's <c>$</c> without the <c>m</c> flag matches only at the end of the input; .NET's <c>$</c>
    /// also matches before a final <c>\n</c>, which would accept a line the reference rejects, so the pattern states
    /// <c>\z</c> for the reference's end-of-input anchor. JS's <c>.</c> is <c>[^\n\r\u2028\u2029]</c> while .NET's is
    /// <c>[^\n]</c>, so the capture states JS's class — the same set, written out. (JS's <c>\/</c> escapes need no
    /// backslash in .NET.) The same substitutions are made in the two patterns below.</para>
    /// </remarks>
    [GeneratedRegex(@"//[@#] source[M]appingURL=([^\n\r\u2028\u2029]+)\r?\n?\z")]
    internal static partial Regex SourceMapCommentRegExpDontCareLineStart();

    /// <summary>ported from: src/compiler/sourcemap.ts:364-365 — <c>sourceMapCommentRegExp</c>.</summary>
    /// <inheritdoc cref="SourceMapCommentRegExpDontCareLineStart"/>
    [GeneratedRegex(@"^//[@#] source[M]appingURL=([^\n\r\u2028\u2029]+)\r?\n?\z")]
    internal static partial Regex SourceMapCommentRegExp();

    /// <summary>ported from: src/compiler/sourcemap.ts:367-368 — <c>whitespaceOrMapCommentRegExp</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the port states both classes the pattern uses explicitly, because neither is the same in the two
    /// engines. JS's <c>.</c> excludes <c>\n</c>, <c>\r</c>, <c>\u2028</c> and <c>\u2029</c>; .NET's excludes only
    /// <c>\n</c>. JS's <c>\s</c> is the WhiteSpace + LineTerminator set; .NET's also contains <c>\u0085</c> and lacks
    /// <c>\uFEFF</c>. The stated classes are the reference's.
    /// </remarks>
    [GeneratedRegex(@"^[\t\n\u000B\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]*(//[@#] [^\n\r\u2028\u2029]*)?\z")]
    internal static partial Regex WhitespaceOrMapCommentRegExp();

    /// <summary>ported from: src/compiler/sourcemap.ts:370-374 — <c>LineInfo</c>.</summary>
    /// <remarks>PORT-NOTE: TS builds the object literal over the two closures; the port states the same pair of
    /// members over the text and the line starts it was built with.</remarks>
    internal sealed class LineInfo(string text, IList<int> lineStarts) : ILineInfo
    {
        /// <summary>ported from: src/compiler/sourcemap.ts:378 — <c>getLineCount</c>.</summary>
        public int GetLineCount() => lineStarts.Count;

        /// <summary>ported from: src/compiler/sourcemap.ts:379 — <c>getLineText</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS is <c>text.substring(lineStarts[line], lineStarts[line + 1])</c> and relies on the
        /// <c>substring</c> special case that an <c>undefined</c> end index runs to the end of the string (the last line
        /// has no next start); the port states that case explicitly, which is also why a line includes its terminator.
        /// </remarks>
        public string GetLineText(int line)
        {
            var start = lineStarts[line];
            var end = line + 1 < lineStarts.Count ? lineStarts[line + 1] : text.Length;
            return text.Substring(start, end - start);
        }
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:376-382 — <c>getLineInfo</c>.</summary>
    internal static ILineInfo GetLineInfo(string text, IList<int> lineStarts) => new LineInfo(text, lineStarts);

    /// <summary>Tries to find the <c>sourceMappingURL</c> comment at the end of a file.</summary>
    /// <remarks>ported from: src/compiler/sourcemap.ts:384-402 — <c>tryGetSourceMappingURL</c>.</remarks>
    internal static string? TryGetSourceMappingURL(ILineInfo lineInfo)
    {
        for (var index = lineInfo.GetLineCount() - 1; index >= 0; index--)
        {
            var line = lineInfo.GetLineText(index);
            var comment = SourceMapCommentRegExp().Match(line);
            if (comment.Success)
            {
                return TrimEnd(comment.Groups[1].Value);
            }
            // If we see a non-whitespace/map comment-like line, break, to avoid scanning up the entire file
            else if (!WhitespaceOrMapCommentRegExp().IsMatch(line))
            {
                break;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/sourcemap.ts:396 — <c>comment[1].trimEnd()</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>trimEnd</c> removes the WhiteSpace + LineTerminator set, which is not the CLR's
    /// (char.IsWhiteSpace) set — the CLR's also contains <c>\u0085</c> and lacks <c>\uFEFF</c> — so the reference's set
    /// is stated here instead of calling <c>string.TrimEnd()</c>.
    /// </remarks>
    private static string TrimEnd(string text)
    {
        var end = text.Length;
        while (end > 0 && IsJsWhitespaceOrLineTerminator(text[end - 1]))
        {
            end--;
        }

        return text[..end];
    }

    /// <summary>The WhiteSpace + LineTerminator set of a non-<c>u</c> JS regular expression.</summary>
    private static bool IsJsWhitespaceOrLineTerminator(char ch)
        => ch is '\t' or '\n' or '\u000B' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF'
            || ch is >= '\u2000' and <= '\u200A';

    // ---------------------------------------------------------------------------------------------
    // The map guard and parser (sourcemap.ts:404-434)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sourcemap.ts:404-406 — <c>isStringOrNull</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>any</c> and it is handed a parsed array's element, so the port states it
    /// over the element the guard reads (the shape <see cref="IsRawSourceMap"/> uses).</remarks>
    private static bool IsStringOrNull(JsonElement x) => x.ValueKind is JsonValueKind.String or JsonValueKind.Null;

    /// <summary>ported from: src/compiler/sourcemap.ts:408-419 — <c>isRawSourceMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>any</c> and the predicate narrows it to <c>RawSourceMap</c>; the port states the
    /// same shape over the parsed JSON element, so the guard reads the document rather than a boxed value.
    /// </remarks>
    private static bool IsRawSourceMap(JsonElement x)
    {
        if (x.ValueKind != JsonValueKind.Object) return false;
        if (!x.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetDouble(out var versionNumber) || versionNumber != 3) return false;
        if (!x.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.String) return false;
        if (!x.TryGetProperty("mappings", out var mappings) || mappings.ValueKind != JsonValueKind.String) return false;
        if (!x.TryGetProperty("sources", out var sources) || !IsStringArray(sources)) return false;
        if (!IsOptionalString(x, "sourceRoot")) return false;
        if (!IsOptionalArray(x, "sourcesContent", allowNulls: true)) return false;
        if (!IsOptionalArray(x, "names", allowNulls: false)) return false;
        return true;
    }

    /// <summary>The <c>isArray(x) &amp;&amp; every(x, isString)</c> half of the guard.</summary>
    private static bool IsStringArray(JsonElement x)
        => x.ValueKind == JsonValueKind.Array
            && x.EnumerateArray().All(static element => element.ValueKind == JsonValueKind.String);

    /// <summary>The <c>undefined | null | typeof string</c> arm of the guard.</summary>
    private static bool IsOptionalString(JsonElement x, string name)
        => !x.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.String;

    /// <summary>The <c>undefined | null | isArray &amp;&amp; every(isStringOrNull)</c> arm of the guard.</summary>
    private static bool IsOptionalArray(JsonElement x, string name, bool allowNulls)
        => !x.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null
            || value.ValueKind == JsonValueKind.Array
                && value.EnumerateArray().All(element => allowNulls
                    ? IsStringOrNull(element)
                    : element.ValueKind == JsonValueKind.String);

    /// <summary>ported from: src/compiler/sourcemap.ts:421-434 — <c>tryParseRawSourceMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS is <c>JSON.parse</c> inside a <c>try</c>. The port's <see cref="JsonDocument"/> is the same parse
    /// (both reject the same malformed input) and the guard above reads its root element; the parsed document is
    /// rebuilt into the port's <see cref="RawSourceMap"/> because TS's cast does not change the parsed object but the
    /// port's callers read a typed value.
    /// </remarks>
    internal static RawSourceMap? TryParseRawSourceMap(string text)
    {
        try
        {
            var parsed = JsonDocument.Parse(text).RootElement;
            if (IsRawSourceMap(parsed))
            {
                return new RawSourceMap
                {
                    Version = parsed.GetProperty("version").GetInt32(),
                    File = parsed.GetProperty("file").GetString(),
                    SourceRoot = parsed.TryGetProperty("sourceRoot", out var sourceRoot) && sourceRoot.ValueKind == JsonValueKind.String ? sourceRoot.GetString() : null,
                    Sources = ReadStrings(parsed.GetProperty("sources")),
                    Names = parsed.TryGetProperty("names", out var names) && names.ValueKind != JsonValueKind.Null ? ReadStrings(names) : null,
                    Mappings = parsed.GetProperty("mappings").GetString(),
                    SourcesContent = parsed.TryGetProperty("sourcesContent", out var content) && content.ValueKind != JsonValueKind.Null ? ReadStrings(content) : null,
                };
            }
        }
        catch (JsonException)
        {
            // empty
        }

        return null;
    }

    /// <summary>The string (and <c>null</c>) elements of a guarded array.</summary>
    /// <remarks>PORT-NOTE: <see cref="IRawSourceMap.SourcesContent"/> is the generated <c>List&lt;string&gt;?</c>
    /// (TS's <c>(string | null)[]</c> loses the null in the generated model), so a JSON <c>null</c> element is stored as
    /// the <c>null</c> the reference keeps in the array.</remarks>
    private static List<string> ReadStrings(JsonElement array)
    {
        var result = new List<string>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            result.Add(element.ValueKind == JsonValueKind.String ? element.GetString()! : null!);
        }

        return result;
    }
}

/// <summary>ported from: src/compiler/types.ts:3076-3090 — <c>RawSourceMap</c>.</summary>
/// <remarks>
/// PORT-NOTE: TS builds this value as an object literal and serializes it with <c>JSON.stringify</c>
/// (<c>SourceMapGenerator.toString</c>, sourcemap.ts:52). The port needs a constructible type, and it carries that
/// serialization: <see cref="ToString"/> writes the literal's members in the literal's order with <c>JSON.stringify</c>'s
/// string escaping, which is what makes the written <c>.js.map</c> byte-for-byte the reference's.
/// </remarks>
internal sealed class RawSourceMap : IRawSourceMap
{
    /// <summary>ported from: src/compiler/types.ts:3077 — <c>version</c>.</summary>
    public int Version { get; set; }

    /// <summary>ported from: src/compiler/types.ts:3078 — <c>file</c>.</summary>
    public string? File { get; set; }

    /// <summary>ported from: src/compiler/types.ts:3079 — <c>sourceRoot</c>.</summary>
    public string? SourceRoot { get; set; }

    /// <summary>ported from: src/compiler/types.ts:3080 — <c>sources</c>.</summary>
    public List<string>? Sources { get; set; }

    /// <summary>ported from: src/compiler/types.ts:3081 — <c>sourcesContent</c>.</summary>
    /// <remarks>PORT-NOTE: TS's element type is <c>string | null</c>; the generated interface's is <c>string</c>, so a
    /// <c>null</c> element is held as the <c>null</c> the reference's array holds (never read as present content).</remarks>
    public List<string>? SourcesContent { get; set; }

    /// <summary>ported from: src/compiler/types.ts:3082 — <c>mappings</c>.</summary>
    public string? Mappings { get; set; }

    /// <summary>ported from: src/compiler/types.ts:3083 — <c>names</c>.</summary>
    public List<string>? Names { get; set; }

    /// <summary>ported from: src/compiler/sourcemap.ts:52 — <c>toString: () =&gt; JSON.stringify(toJSON())</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: the CLR has no <c>JSON.stringify</c> (the same gap <c>Emitter.GetBuildInfoText</c> records), and
    /// the reference's text is not any general-purpose serializer's: the members are written in the object literal's
    /// order (<c>version</c>, <c>file</c>, <c>sourceRoot</c>, <c>sources</c>, <c>names</c>, <c>mappings</c>,
    /// <c>sourcesContent</c> — which is not this type's declaration order) and strings are escaped by
    /// <c>JSON.stringify</c>'s rules. Both are stated here.</para>
    /// <para>PORT-NOTE: an absent member is the reference's <c>undefined</c>, and <c>JSON.stringify</c> omits an
    /// <c>undefined</c>-valued property — only <c>sourcesContent</c> is optional in <c>toJSON</c>, so it is the one member
    /// this writer omits when it is <c>null</c>; the rest always carry a value the generator set.</para>
    /// </remarks>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("{\"version\":").Append(Version.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"file\":"); AppendJsonString(builder, File);
        builder.Append(",\"sourceRoot\":"); AppendJsonString(builder, SourceRoot);
        builder.Append(",\"sources\":"); AppendJsonStringArray(builder, Sources);
        builder.Append(",\"names\":"); AppendJsonStringArray(builder, Names);
        builder.Append(",\"mappings\":"); AppendJsonString(builder, Mappings);
        if (SourcesContent is not null)
        {
            builder.Append(",\"sourcesContent\":"); AppendJsonStringArray(builder, SourcesContent);
        }

        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>Writes one JSON string the way <c>JSON.stringify</c> does.</summary>
    /// <remarks>
    /// PORT-NOTE: this is ECMA-262's <c>QuoteJSONString</c>: <c>"</c>, <c>\</c>, the five short escapes, and
    /// <c>\uXXXX</c> (lower-case hex) for the other code units below <c>U+0020</c> and for unpaired surrogates; every
    /// other code unit — including non-ASCII and U+2028/U+2029 — is written as itself.
    /// </remarks>
    private static void AppendJsonString(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        builder.Append(ch).Append(value[++i]);
                    }
                    else if (char.IsSurrogate(ch) || ch < ' ')
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    /// <summary>Writes a JSON array of strings (or <c>null</c>) the way <c>JSON.stringify</c> does.</summary>
    private static void AppendJsonStringArray(StringBuilder builder, List<string>? values)
    {
        if (values is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) builder.Append(',');
            AppendJsonString(builder, values[i]);
        }

        builder.Append(']');
    }
}
