using System.Text;
using System.Text.Json;

namespace TypeScript.Compiler.Tools;

/// <summary>
/// Generates the <c>Diagnostics</c> table from the checkout's <c>diagnosticMessages.json</c>.
/// </summary>
/// <remarks>
/// The source table is keyed by the message text itself; the C# member name is that text run through TS's own
/// mangling (<c>scripts/processDiagnosticMessages.mjs</c> → <c>convertPropertyName</c>). Reproducing that mangling
/// exactly is the point of the exercise — ported code refers to <c>Diagnostics.Unexpected_character</c>, so any
/// other spelling would fail to compile at every use site.
/// <para>
/// Like the rest of the generator this refuses to guess. TS's script checks that codes are unique; the port also
/// checks member names, because a C# class with two members of the same name is a compile error where a JS object
/// literal would silently keep the last one.
/// </para>
/// </remarks>
internal static class DiagnosticEmitter
{
    /// <summary>The diagnostic table, relative to the TypeScript checkout root.</summary>
    public const string SourcePath = "src/compiler/diagnosticMessages.json";

    /// <summary>The categories the table may use, which are also <see cref="DiagnosticCategory"/>'s members.</summary>
    private static readonly string[] s_categories = ["Error", "Warning", "Suggestion", "Message"];

    /// <summary>Length cap TS applies to the member name when building the localization key.</summary>
    private const int KeyLengthCap = 100;

    public static GeneratedFile Emit(string tsRoot, string outputRoot)
    {
        var inputPath = Path.Combine(tsRoot, SourcePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"the diagnostic message table is missing: '{inputPath}'");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(inputPath));
        return new GeneratedFile($"{outputRoot}/Model/Diagnostics.cs", Render(Read(document.RootElement)));
    }

    private static List<DiagnosticEntry> Read(JsonElement root)
    {
        var entries = new List<DiagnosticEntry>();
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        var byCode = new Dictionary<int, string>();

        foreach (var property in root.EnumerateObject())
        {
            var message = property.Name;
            var details = property.Value;
            var code = details.GetProperty("code").GetInt32();
            var category = details.GetProperty("category").GetString()!;

            if (!s_categories.Contains(category, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"diagnostic '{message}' has unknown category '{category}'");
            }

            var name = ConvertPropertyName(message);
            if (name.Length == 0)
            {
                throw new InvalidOperationException($"diagnostic '{message}' mangles to an empty member name");
            }

            // TS's table is an object literal, where `1st line` would be a fine key; a C# member cannot start
            // with a digit. Refuse rather than emit something that will not compile.
            if (!IsIdentifier(name))
            {
                throw new InvalidOperationException($"diagnostic '{message}' mangles to '{name}', which is not a valid C# identifier");
            }

            if (byName.TryGetValue(name, out var nameOwner))
            {
                throw new InvalidOperationException($"'{name}' is the member name for both '{nameOwner}' and '{message}'");
            }

            byName[name] = message;

            if (byCode.TryGetValue(code, out var codeOwner))
            {
                throw new InvalidOperationException($"diagnostic code {code} is used by both '{codeOwner}' and '{message}'");
            }

            byCode[code] = message;

            entries.Add(new DiagnosticEntry(
                name,
                code,
                category,
                message,
                HasFlag(details, "reportsUnnecessary"),
                HasFlag(details, "elidedInCompatabilityPyramid"),
                HasFlag(details, "reportsDeprecated")));
        }

        return entries;
    }

    /// <summary>TS marks these with <c>true</c>; the port carries the same three flags.</summary>
    private static bool HasFlag(JsonElement details, string name)
        => details.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Port of TS's <c>convertPropertyName</c>: non-word characters become underscores, with three named
    /// exceptions, and the run of underscores is then tidied up.
    /// </summary>
    private static string ConvertPropertyName(string message)
    {
        var sb = new StringBuilder(message.Length);
        foreach (var c in message)
        {
            switch (c)
            {
                case '*': sb.Append("_Asterisk"); break;
                case '/': sb.Append("_Slash"); break;
                case ':': sb.Append("_Colon"); break;
                default:
                    // JS's \w is [A-Za-z0-9_] while .NET's is Unicode-aware, so the class is spelled out.
                    sb.Append(c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' ? c : '_');
                    break;
            }
        }

        var result = CollapseUnderscores(sb.ToString());

        // "remove any leading underscore, unless it is followed by a number"
        if (result.Length >= 2 && result[0] == '_' && !char.IsAsciiDigit(result[1]))
        {
            result = result[1..];
        }

        // "get rid of all trailing underscores" — TS's regex has no /g, so it removes exactly one.
        if (result.EndsWith('_')) result = result[..^1];

        return result;
    }

    /// <summary>TS: <c>result.replace(/_+/g, "_")</c>.</summary>
    private static string CollapseUnderscores(string text)
    {
        if (!text.Contains("__", StringComparison.Ordinal)) return text;

        var sb = new StringBuilder(text.Length);
        var previousWasUnderscore = false;
        foreach (var c in text)
        {
            if (c == '_')
            {
                if (previousWasUnderscore) continue;
                previousWasUnderscore = true;
            }
            else
            {
                previousWasUnderscore = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Whether a mangled name can be spelled as a C# member (letters, digits and underscores, not
    /// starting with a digit).</summary>
    private static bool IsIdentifier(string name)
    {
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }

        return true;
    }

    private static string Render(List<DiagnosticEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//   Generated from {SourcePath} by tools/Gen — DO NOT EDIT.");
        sb.AppendLine("//   Regenerate with:");
        sb.AppendLine("//     dotnet run --project tools/Gen -- --out src/TypeScript.Compiler");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        // Roslyn does not apply the project's nullable context to files marked <auto-generated>,
        // so generated code must opt in explicitly.
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace TypeScript.Compiler;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Every diagnostic the compiler can report — one member per entry of TS's");
        sb.AppendLine("/// <c>diagnosticMessages.json</c>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.AppendLine("/// PORT-NOTE: member names are TS's own mangling of the message text (see");
        sb.AppendLine("/// <c>scripts/processDiagnosticMessages.mjs</c>), so ported call sites keep reading");
        sb.AppendLine("/// <c>Diagnostics.Unexpected_character</c>. <c>key</c> is the localization key TS derives from the");
        sb.AppendLine("/// name (capped at 100 characters) plus the code; the port only implements English, so nothing");
        sb.AppendLine("/// resolves through it yet (D6).");
        sb.AppendLine("/// </remarks>");
        sb.AppendLine("internal static partial class Diagnostics");
        sb.AppendLine("{");
        sb.AppendLine("    private static DiagnosticMessage Diag(");
        sb.AppendLine("        int code,");
        sb.AppendLine("        DiagnosticCategory category,");
        sb.AppendLine("        string key,");
        sb.AppendLine("        string message,");
        sb.AppendLine("        bool reportsUnnecessary = false,");
        sb.AppendLine("        bool elidedInCompatabilityPyramid = false,");
        sb.AppendLine("        bool reportsDeprecated = false)");
        sb.AppendLine("        => new(code, category, key, message, reportsUnnecessary, elidedInCompatabilityPyramid, reportsDeprecated);");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            sb.AppendLine(
                $"    public static readonly DiagnosticMessage {entry.Name} = Diag(" +
                $"{entry.Code}, DiagnosticCategory.{entry.Category}, {TsStringLiteral.ToCSharp(entry.Key)}, " +
                $"{TsStringLiteral.ToCSharp(entry.Message)}{entry.OptionalArguments});");
        }

        sb.AppendLine("}");

        // The repository is LF-only (.gitattributes: eol=lf); AppendLine emits CRLF on Windows.
        return sb.ToString().Replace("\r\n", "\n");
    }

    private sealed record DiagnosticEntry(
        string Name,
        int Code,
        string Category,
        string Message,
        bool ReportsUnnecessary,
        bool ElidedInCompatabilityPyramid,
        bool ReportsDeprecated)
    {
        /// <summary>TS: <c>name.slice(0, 100) + "_" + code</c>.</summary>
        public string Key => Name.Length <= KeyLengthCap ? $"{Name}_{Code}" : $"{Name[..KeyLengthCap]}_{Code}";

        /// <summary>The trailing named arguments, for the flags this message sets.</summary>
        public string OptionalArguments
        {
            get
            {
                var parts = new List<string>();
                if (ReportsUnnecessary) parts.Add("reportsUnnecessary: true");
                if (ElidedInCompatabilityPyramid) parts.Add("elidedInCompatabilityPyramid: true");
                if (ReportsDeprecated) parts.Add("reportsDeprecated: true");
                return parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
            }
        }
    }
}
