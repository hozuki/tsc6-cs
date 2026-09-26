using System.Text;

namespace TypeScript.Compiler.Tools;

internal sealed record EnumSpec(
    /// <summary>Enum name, identical in TS and C#.</summary>
    string Name,
    /// <summary>TS source file relative to the TS repo root.</summary>
    string TsRelativePath,
    /// <summary>
    /// C# underlying type (<c>int</c>/<c>uint</c>/<c>ulong</c>) for numeric enums,
    /// or <c>null</c> when the TS enum is string-valued and becomes a constants class.
    /// </summary>
    string? UnderlyingType,
    /// <summary>Whether to apply <c>[Flags]</c>. Only meaningful for numeric enums.</summary>
    bool Flags,
    string Summary);

/// <summary>Renders a parsed <see cref="TsEnum"/> as C# source.</summary>
internal static class EnumEmitter
{
    public static string Emit(TsEnum tsEnum, EnumSpec spec)
        => spec.UnderlyingType is null ? EmitStringEnum(tsEnum, spec) : EmitNumericEnum(tsEnum, spec);

    /// <summary>
    /// Numeric enum: <c>internal enum Name : T</c> with explicit values.
    /// </summary>
    private static string EmitNumericEnum(TsEnum tsEnum, EnumSpec spec)
    {
        var memberValues = tsEnum.Members.ToDictionary(static m => m.Name, static m => m.Value, StringComparer.Ordinal);

        var sb = Header(tsEnum, spec);
        if (spec.Flags) sb.AppendLine("[Flags]");
        sb.AppendLine($"internal enum {spec.Name} : {spec.UnderlyingType}");
        sb.AppendLine("{");

        foreach (var member in tsEnum.Members)
        {
            AppendDoc(sb, member);

            // Keep the TS expression rather than its numeric value: expressions stay readable next to the
            // source, and aliases (`JSDocComment = JSDoc`) keep pointing at their target. Members with no
            // explicit value are implicit increments and are emitted as plain numbers.
            var literal = member.ValueText is null
                ? member.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : ExpressionEvaluator.Rewrite(member.ValueText, memberValues, spec.UnderlyingType!);

            var line = $"    {member.Name} = {literal},";
            if (member.Trailing is { Length: > 0 }) line += "  // " + member.Trailing;
            sb.AppendLine(line);
        }

        sb.AppendLine("}");
        return Finish(sb);
    }

    /// <summary>
    /// String-valued enum: a <c>readonly struct</c> wrapping the string, with one static readonly field
    /// per member.
    /// </summary>
    /// <remarks>
    /// TS's string enums are consumed as plain strings at every call site, so a bare C# enum is not an
    /// option — it cannot carry <c>"__call"</c>, <c>"export="</c> or <c>"\u001b[90m"</c> as its value. But
    /// using <c>const string</c> would throw away the nominal type entirely, leaving nothing to
    /// distinguish an <c>Extension</c> from any other string in the signatures of helpers like
    /// <c>fileExtensionIs</c>. The wrapper keeps both: the underlying string is reachable through
    /// <see cref="Value"/>, while parameters and fields can still be typed as the enum.
    /// <para>
    /// No implicit conversion to <c>string</c> is generated: the whole point of the wrapper is that a
    /// call site has to say <c>.Value</c> when it really wants a string, so the type keeps carrying
    /// information instead of decaying on assignment.
    /// </para>
    /// </remarks>
    private static string EmitStringEnum(TsEnum tsEnum, EnumSpec spec)
    {
        var sb = Header(tsEnum, spec);
        sb.AppendLine($"internal readonly struct {spec.Name} : IEquatable<{spec.Name}>");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly string _value;");
        sb.AppendLine();
        sb.AppendLine($"    private {spec.Name}(string value) => _value = value;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The underlying string value, i.e. the value TS gives this enum member.</summary>");
        sb.AppendLine("    public string Value => _value ?? string.Empty;");
        sb.AppendLine();

        foreach (var member in tsEnum.Members)
        {
            AppendDoc(sb, member);

            var line = $"    public static readonly {spec.Name} {member.Name} = new({TsStringLiteral.ToCSharp(member.StringValue!)});";
            if (member.Trailing is { Length: > 0 }) line += "  // " + member.Trailing;
            sb.AppendLine(line);
        }

        sb.AppendLine();
        sb.AppendLine($"    public bool Equals({spec.Name} other) => string.Equals(_value, other._value, StringComparison.Ordinal);");
        sb.AppendLine();
        sb.AppendLine($"    public override bool Equals(object? obj) => obj is {spec.Name} other && Equals(other);");
        sb.AppendLine();
        sb.AppendLine("    public override int GetHashCode() => _value is null ? 0 : string.GetHashCode(_value, StringComparison.Ordinal);");
        sb.AppendLine();
        sb.AppendLine($"    public static bool operator ==({spec.Name} left, {spec.Name} right) => left.Equals(right);");
        sb.AppendLine();
        sb.AppendLine($"    public static bool operator !=({spec.Name} left, {spec.Name} right) => !left.Equals(right);");
        sb.AppendLine();
        sb.AppendLine("    public override string ToString() => Value;");
        sb.AppendLine("}");
        return Finish(sb);
    }

    private static StringBuilder Header(TsEnum tsEnum, EnumSpec spec)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//   Generated from {spec.TsRelativePath}:{tsEnum.StartLine}-{tsEnum.EndLine} by tools/Gen — DO NOT EDIT.");
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
        sb.AppendLine($"/// <summary>{spec.Summary}</summary>");
        return sb;
    }

    private static void AppendDoc(StringBuilder sb, TsEnumMember member)
    {
        if (member.Doc is { Length: > 0 })
        {
            sb.AppendLine($"    /// <summary>{Escape(member.Doc)}</summary>");
        }
    }

    private static string Finish(StringBuilder sb)
    {
        // The repository is LF-only (.gitattributes: eol=lf); AppendLine emits CRLF on Windows and would
        // make the committed files differ from what the generator produces after checkout.
        return sb.ToString().Replace("\r\n", "\n");
    }

    /// <summary>
    /// Escapes text for use inside an XML doc comment, collapsing it to one line: TS doc comments are
    /// routinely multi-paragraph, and a raw newline would end the <c>///</c> comment mid-sentence.
    /// </summary>
    private static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
}
