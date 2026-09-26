using System.Text;

namespace TypeScript.Compiler.Tools;

/// <summary>Helpers for reading the TS comments the generators use as documentation.</summary>
internal static class TsComments
{
    /// <summary>Strips the delimiters and decorative asterisks from a block comment, collapsing it to one line.</summary>
    public static string Clean(string block)
    {
        var body = block;

        var start = body.IndexOf("/*", StringComparison.Ordinal);
        if (start >= 0) body = body[(start + 2)..];

        var end = body.IndexOf("*/", StringComparison.Ordinal);
        if (end >= 0) body = body[..end];

        return string.Join(
            " ",
            body.Split('\n')
                .Select(static l => l.Trim().TrimStart('*').Trim())
                .Where(static l => l.Length != 0));
    }

    /// <summary>
    /// Reads a block comment starting at <paramref name="i"/> (whose line contains the opener). The
    /// returned remainder is whatever followed <c>*/</c> on the closing line; <paramref name="i"/> is left
    /// on that line.
    /// </summary>
    public static (string Doc, string Remainder) ReadBlock(string[] lines, ref int i, string firstLine)
    {
        var sb = new StringBuilder();
        sb.Append(firstLine);

        while (true)
        {
            var end = lines[i].IndexOf("*/", StringComparison.Ordinal);
            if (end >= 0) return (Clean(sb.ToString()), lines[i][(end + 2)..]);

            i++;
            if (i >= lines.Length) return (Clean(sb.ToString()), "");
            sb.Append('\n').Append(lines[i]);
        }
    }
}
