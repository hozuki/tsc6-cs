using System.Diagnostics;
using System.Text.Json;

namespace TypeScript.Compiler.Tools;

/// <summary>One member of a TS <c>enum</c>.</summary>
internal sealed class TsEnumMember
{
    public string Name { get; set; } = "";

    /// <summary>The value expression as written in TS, or <c>null</c> for an implicit increment.</summary>
    public string? ValueText { get; set; }

    public string? Doc { get; set; }

    public string? Trailing { get; set; }

    /// <summary>Numeric value, filled in by <see cref="TsModel.ComputeEnumValues"/>.</summary>
    public long Value { get; set; }

    /// <summary>Decoded string value for string-valued enums.</summary>
    public string? StringValue { get; set; }

    /// <summary>True when the value expression is a bare reference to another member.</summary>
    public bool IsAlias { get; set; }
}

/// <summary>A TS <c>enum</c> / <c>const enum</c>.</summary>
internal sealed class TsEnum
{
    public string Name { get; set; } = "";

    public bool IsConst { get; set; }

    public string? Source { get; set; }

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    public List<TsEnumMember> Members { get; set; } = [];

    public long MaxValue => Members.Count == 0 ? 0 : Members.Max(static m => m.Value);

    /// <summary>True when every member's value is a string literal — TS's string-valued enum form.</summary>
    public bool IsStringValued => Members.Count != 0 && Members.All(static m => m.StringValue is not null);
}

/// <summary>One property of a TS interface.</summary>
internal sealed class TsMember
{
    public string Name { get; set; } = "";

    /// <summary>Raw TS type expression, not yet mapped to C#. For a method this is the return type.</summary>
    public string Type { get; set; } = "";

    /// <summary>True for a method signature (<c>createIdentifier(name: string): Identifier</c>).</summary>
    public bool IsMethod { get; set; }

    public List<TsParameter> Parameters { get; set; } = [];

    public List<TsTypeParameter> TypeParameters { get; set; } = [];

    public bool IsReadonly { get; set; }

    public bool IsOptional { get; set; }

    public string? Doc { get; set; }

    public string? Trailing { get; set; }
}

/// <summary>A parameter of a method signature.</summary>
internal sealed class TsParameter
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    public bool IsOptional { get; set; }

    public bool IsRest { get; set; }
}

/// <summary>A type parameter of a method signature.</summary>
internal sealed class TsTypeParameter
{
    public string Name { get; set; } = "";

    /// <summary>The raw <c>extends</c> clause, or null.</summary>
    public string? Constraint { get; set; }
}

/// <summary>A TS <c>interface</c>.</summary>
internal sealed class TsInterface
{
    public string Name { get; set; } = "";

    public List<string> TypeParameters { get; set; } = [];

    /// <summary>Raw base type expressions, in source order.</summary>
    public List<string> Extends { get; set; } = [];

    public List<TsMember> Members { get; set; } = [];

    public string? Doc { get; set; }

    public string? Source { get; set; }

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    /// <summary>The pinned <c>SyntaxKind</c> member when the interface is a concrete node.</summary>
    public string? ConcreteKind { get; set; }

    public bool HasConcreteKind => ConcreteKind is not null;
}

/// <summary>A TS <c>export type</c> alias.</summary>
internal sealed class TsTypeAlias
{
    public string Name { get; set; } = "";

    public List<string> TypeParameters { get; set; } = [];

    /// <summary>The raw type expression.</summary>
    public string Value { get; set; } = "";

    public string? Doc { get; set; }

    public string? Source { get; set; }

    public int StartLine { get; set; }

    public int EndLine { get; set; }
}

/// <summary>
/// A data table lifted out of the TS source.
/// </summary>
/// <remarks>
/// The scanner needs several tables that are plain literals in <c>scanner.ts</c> — 4,454 Unicode range
/// endpoints among them — and copying them by hand would be both huge and easy to get subtly wrong. The
/// extractor evaluates them with the compiler's AST (see <c>tableSources</c> in extract.cjs); the value stays a
/// <see cref="JsonElement"/> here so the emitter can walk the shape each table has.
/// </remarks>
internal sealed class TsTable
{
    public string Name { get; set; } = "";

    public string? Source { get; set; }

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    public JsonElement Value { get; set; }
}

/// <summary>One predicate of <c>factory/nodeTests.ts</c>: a name and the kind it tests.</summary>
internal sealed class TsPredicate
{
    public string Name { get; set; } = "";

    public List<string> Kinds { get; set; } = [];

    public int StartLine { get; set; }
}

/// <summary>
/// The type model of a TS source file, obtained from the TypeScript compiler's own parser.
/// </summary>
/// <remarks>
/// The C# side used to scan <c>types.ts</c> line by line. That worked, but it broke on several ordinary TS
/// constructs (a member with no trailing <c>;</c>, nested generics in a parameter list, a one-line
/// interface, a <c>}</c> that also opens the next declaration, a value expression spanning lines), and each
/// fix was another special case. The extractor now runs <c>tools/Gen/extract.cjs</c> — which loads the
/// TypeScript compiler from the checkout's own <c>lib/typescript.js</c> — and this class just reads its JSON.
/// <para>
/// The <c>node</c> dependency is confined to table generation; nothing under <c>src/</c> depends on it.
/// </para>
/// </remarks>
/// <summary>The fixed-shape members of <c>factory/nodeFactory.ts</c> (docs/architecture.md §15.7).</summary>
/// <remarks>
/// Only members whose body is a <c>createBase*</c> call plus assignments, or a single comparison chain forwarding to
/// <c>update</c>, are described here. Everything else — the helpers, the members with real logic, the module-level
/// functions — stays hand-written, and <see cref="TsFactoryMember.Supported"/> is what separates the two.
/// </remarks>
internal sealed class TsFactory
{
    public string Source { get; set; } = "";

    public List<TsFactoryMember> Members { get; set; } = [];
}

/// <summary>One <c>create*</c>/<c>update*</c> member of <c>createNodeFactory</c>.</summary>
internal sealed class TsFactoryMember
{
    public string Name { get; set; } = "";

    /// <summary><c>create</c> or <c>update</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>False when the body did not match a fixed shape; <see cref="Reason"/> says why.</summary>
    public bool Supported { get; set; }

    public string? Reason { get; set; }

    public string ReturnType { get; set; } = "void";

    public List<TsTypeParameter> TypeParameters { get; set; } = [];

    public List<TsParameter> Parameters { get; set; } = [];

    /// <summary>The <c>createBase*</c> function the body starts with.</summary>
    public string? Base { get; set; }

    /// <summary>Its type argument — <c>QualifiedName</c> in <c>createBaseNode&lt;QualifiedName&gt;</c>.</summary>
    public string? TypeArgument { get; set; }

    /// <summary>Its first argument — <c>SyntaxKind.QualifiedName</c>.</summary>
    public string? KindExpression { get; set; }

    /// <summary>What the body returns: <c>node</c>, or an <c>update(…)</c> call.</summary>
    public string? Tail { get; set; }

    public List<TsFactoryAssignment> Assignments { get; set; } = [];

    /// <summary>Non-assignment statements in the body (<c>setTextRange(node, …)</c>).</summary>
    public List<TsExpression> Calls { get; set; } = [];

    public List<TsFactoryCompare> Compares { get; set; } = [];

    /// <summary>The <c>createX(…)</c> call an <c>update*</c> member forwards to, as a <c>call</c> expression.</summary>
    public TsExpression? UpdateCreate { get; set; }
}

/// <summary>One <c>node.field = value</c> statement (also <c>|=</c> and <c>&amp;=</c>).</summary>
internal sealed class TsFactoryAssignment
{
    public string Target { get; set; } = "";

    public string Op { get; set; } = "=";

    public TsExpression? Value { get; set; }

    /// <summary>The guard when TS wrote <c>if (condition) node.field = value</c>, otherwise <c>null</c>.</summary>
    public TsExpression? Condition { get; set; }
}

/// <summary>One <c>node.field !== value</c> test in an <c>update*</c> member's comparison chain.</summary>
internal sealed class TsFactoryCompare
{
    public string Target { get; set; } = "";

    public TsExpression? Value { get; set; }
}

/// <summary>
/// A TS expression, reduced to the shapes the fixed-shape members use.
/// </summary>
/// <remarks>
/// <see cref="Form"/> says which members are set: <c>identifier</c> (name), <c>property</c> (target + name),
/// <c>call</c> (name + args), <c>method</c> (receiver + name + args), <c>binary</c> (op + left + right),
/// <c>prefix</c> (op + operand), <c>paren</c> and <c>nonNull</c> (inner), <c>conditional</c> (condition + whenTrue +
/// whenFalse), <c>array</c> (items), <c>string</c> and <c>number</c> (value), <c>boolean</c> (boolean), <c>null</c>,
/// or <c>unsupported</c> (text) — which the emitter refuses to translate rather than guessing.
/// </remarks>
internal sealed class TsExpression
{
    public string Form { get; set; } = "";

    public string? Name { get; set; }

    public string? Value { get; set; }

    public bool? Boolean { get; set; }

    public string? Op { get; set; }

    public string? Text { get; set; }

    public string? Receiver { get; set; }

    public TsExpression? Target { get; set; }

    public TsExpression? Left { get; set; }

    public TsExpression? Right { get; set; }

    public TsExpression? Operand { get; set; }

    public TsExpression? Inner { get; set; }

    public TsExpression? Condition { get; set; }

    public TsExpression? WhenTrue { get; set; }

    public TsExpression? WhenFalse { get; set; }

    public List<TsExpression> Args { get; set; } = [];

    public List<TsExpression> Items { get; set; } = [];
}

internal sealed class TsModel
{
    public string? Source { get; set; }

    public List<TsEnum> Enums { get; set; } = [];

    public List<TsInterface> Interfaces { get; set; } = [];

    public List<TsTypeAlias> Aliases { get; set; } = [];

    /// <summary>Data tables extracted alongside the type model, in the order the extractor evaluated them.
    /// </summary>
public List<TsTable> Tables { get; set; } = [];

    /// <summary>The node predicates of <c>factory/nodeTests.ts</c>, in source order.</summary>
    public List<TsPredicate> Predicates { get; set; } = [];

    /// <summary>
    /// The fixed-shape members of <c>factory/nodeFactory.ts</c>, in source order (docs/architecture.md §15.7).
    /// </summary>
    public TsFactory Factory { get; set; } = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Runs the extractor against <paramref name="tsRoot"/> and returns the parsed model.
    /// </summary>
    /// <param name="repoRoot">Used to locate <c>tools/Gen/extract.cjs</c> and the output path.</param>
    /// <param name="outputPath">Where the extractor writes its JSON (a build artifact, not committed).</param>
    public static TsModel Extract(string repoRoot, string tsRoot, string outputPath, IReadOnlyList<string> sources)
    {
        var script = Path.Combine(repoRoot, "tools", "Gen", "extract.cjs");
        if (!File.Exists(script))
        {
            throw new FileNotFoundException($"extractor script not found: {script}", script);
        }

        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add(tsRoot);
        startInfo.ArgumentList.Add(outputPath);
        foreach (var source in sources) startInfo.ArgumentList.Add(source);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not start node; is it on PATH?");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"extractor failed ({process.ExitCode}): {stderr.Trim()}");
        }

        if (stdout.Trim().Length != 0) Console.WriteLine($"  {stdout.Trim()}");

        return Load(outputPath);
    }

    public static TsModel Load(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var model = JsonSerializer.Deserialize<TsModel>(json, s_jsonOptions)
            ?? throw new InvalidOperationException($"could not read the type model from {jsonPath}");

        foreach (var tsEnum in model.Enums) ComputeEnumValues(tsEnum);
        return model;
    }

    /// <summary>
    /// Fills in each member's numeric/string value from its TS expression.
    /// </summary>
    /// <remarks>
    /// The extractor hands over expressions verbatim (that is what keeps the generated C# readable next to
    /// the TS source); evaluating them stays on the C# side, where <see cref="ExpressionEvaluator"/> and
    /// <see cref="TsStringLiteral"/> already reproduce JS's 32-bit bitwise semantics and its escape rules.
    /// The values are needed to pick each enum's underlying type and to detect members that do not fit.
    /// </remarks>
    private static void ComputeEnumValues(TsEnum tsEnum)
    {
        long nextImplicit = 0;
        var values = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var member in tsEnum.Members)
        {
            if (member.ValueText is null)
            {
                member.Value = nextImplicit;
            }
            else if (TsStringLiteral.TryParse(member.ValueText, out var stringValue))
            {
                member.StringValue = stringValue;
            }
            else
            {
                member.Value = ExpressionEvaluator.Evaluate(member.ValueText, values);
                member.IsAlias = IsIdentifier(member.ValueText);
            }

            nextImplicit = member.Value + 1;
            values[member.Name] = member.Value;
        }
    }

    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0) return false;
        if (!char.IsLetter(text[0]) && text[0] != '_' && text[0] != '$') return false;
        return text.All(static c => char.IsLetterOrDigit(c) || c is '_' or '$');
    }
}
