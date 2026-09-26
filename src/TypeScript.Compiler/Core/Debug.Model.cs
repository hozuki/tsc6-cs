using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace TypeScript.Compiler;

// ported from: src/compiler/debug.ts:206-569
/// <summary>
/// The model-dependent half of TS's <c>Debug</c> namespace: the syntax-node assertions
/// (<c>failBadSyntaxKind</c>, <c>assertNode</c>, <c>assertNotNode</c>), the enum/flag formatters
/// (<c>formatEnum</c> + <c>getEnumMembers</c>, <c>formatSyntaxKind</c>, <c>formatFlowFlags</c>) and
/// <c>attachFlowNodeDebugInfo</c>. <c>Debug.cs</c> keeps the Core-only plumbing (leveling, logging, <c>fail</c>,
/// the non-node assertions).
/// </summary>
/// <remarks>
/// PORT-NOTE: TS keeps all of <c>debug.ts</c> in one module because its functions live in one scope; the port splits
/// it along the layer boundary that matters here — everything in this file needs <c>Node</c>/<c>SyntaxKind</c>/
/// <c>FlowNode</c> from the model layer, everything in <c>Debug.cs</c> does not. Same arrangement as
/// <c>Utilities.cs</c>/<c>Utilities.Parser.cs</c> (docs/architecture.md §12.3).
/// <para>
/// PORT-NOTE: the leveling convention shared with <c>Debug.cs</c> (each level-gated assertion tests
/// <see cref="ShouldAssert"/> on entry instead of being swapped out for <c>noop</c>) is stated in that file's class
/// remarks; this file states the per-member deviations: the enum reflection, the collapsed overloads of the node
/// assertions, and the fact that flow-node debug info is declared rather than attached.
/// </para>
/// </remarks>
internal static partial class Debug
{
    // ---------------------------------------------------------------------------------------------
    // Node assertions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/debug.ts:206-212 — <c>failBadSyntaxKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares this <c>never</c> (it returns <c>fail(...)</c>), which is what lets a caller write it
    /// in expression position. C# has no bottom type, so the port declares it <c>void</c> with
    /// <see cref="DoesNotReturnAttribute"/>, as <see cref="Fail"/> does; a caller that needs expression position
    /// uses <see cref="Fail{T}"/>. The <c>stackCrawlMark</c> parameter is dropped for the same reason it is dropped
    /// in <see cref="Fail"/>: it only chose where V8 pointed the stack trace.
    /// </remarks>
    [DoesNotReturn]
    public static void FailBadSyntaxKind(Node node, string? message = null)
        => Fail($"{message ?? "Unexpected node."}\r\nNode {FormatSyntaxKind(node.Kind)} was unexpected.");

    /// <summary>ported from: src/compiler/debug.ts:294-305 — <c>assertNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS gates on <c>shouldAssertFunction(AssertionLevel.Normal, "assertNode")</c> (debug.ts:187-194),
    /// which replaces the function with <c>noop</c> while assertions are off; C# cannot reassign its own methods, so
    /// this member tests <see cref="ShouldAssert"/> on entry instead — the same condition, since
    /// <c>shouldAssertFunction</c> returns true exactly when <c>shouldAssert(level)</c>. The name argument TS passes
    /// exists only to key its <c>assertionCache</c>, so it has no counterpart here. TS's
    /// <c>message || "Unexpected node."</c> also falls back for an empty message; the port writes <c>??</c>, as
    /// <see cref="Assert"/> does.
    /// <para>
    /// PORT-NOTE: TS declares two overloads so that an <c>asserts node is U</c> call narrows the node afterwards. C#
    /// cannot narrow through a <c>Func&lt;Node, bool&gt;</c>, so the overloads collapse into this single <c>void</c>
    /// member and the caller casts to the type it asserted. <c>test</c>'s <c>undefined</c> is the optional
    /// <c>Func&lt;Node, bool&gt;?</c>, and the <c>stackCrawlMark</c> parameter is dropped as in
    /// <see cref="FailBadSyntaxKind"/>.
    /// </para>
    /// </remarks>
    public static void AssertNode(Node? node, Func<Node, bool>? test = null, string? message = null)
    {
        if (ShouldAssert(AssertionLevel.Normal))
        {
            Assert(
                node is not null && (test is null || test(node)),
                message ?? "Unexpected node.",
                () => $"Node {FormatSyntaxKind(node?.Kind)} did not pass test '{GetFunctionName(test)}'.");
        }
    }

    /// <summary>ported from: src/compiler/debug.ts:307-318 — <c>assertNotNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: leveling, the collapsed overloads and <c>stackCrawlMark</c> are as in <see cref="AssertNode"/>. TS's
    /// verbose lambda writes <c>node!.kind</c> where <see cref="AssertNode"/>'s writes <c>node?.kind</c>, and the
    /// non-forgiving form is kept: the lambda only runs when the assertion failed, which for this member implies
    /// <c>node</c> and <c>test</c> are both non-null.
    /// </remarks>
    public static void AssertNotNode(Node? node, Func<Node, bool>? test = null, string? message = null)
    {
        if (ShouldAssert(AssertionLevel.Normal))
        {
            Assert(
                node is null || test is null || !test(node),
                message ?? "Unexpected node.",
                () => $"Node {FormatSyntaxKind(node!.Kind)} should not have passed test '{GetFunctionName(test)}'.");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Enum and flag formatting
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/debug.ts:389-418 — <c>formatEnum</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS takes the runtime <c>enumObject</c>, because that object is where the member names live; C# enums
    /// have no runtime object, so the type parameter takes the object's place and the members come from reflection
    /// (see <see cref="GetEnumMembers"/>). <c>isFlags</c> being optional-and-falsy in TS is the default parameter here.
    /// <para>
    /// PORT-NOTE: TS's <c>value = 0</c> default fires only for callers that pass <c>undefined</c> (TS types the
    /// parameter <c>SyntaxKind | undefined</c>); <c>TEnum</c> is not nullable, so those callers write <c>?? 0</c>
    /// themselves (<see cref="FormatSyntaxKind"/>, <see cref="FormatFlowFlags"/>).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS computes in JS numbers; this reads the enum through <see cref="ToInt32"/> instead of
    /// <c>Convert.ToInt32</c>, because the port's bit-31 flag members are generated as <c>uint</c> and would throw
    /// where TS stores <c>1 &lt;&lt; 31</c>, i.e. the negative 32-bit value. <see cref="ToInt32"/> yields exactly that
    /// value, so the comparisons, the <c>&amp;</c>/<c>~</c> masking and <c>value.toString()</c> all see what TS saw
    /// (docs/architecture.md §4.5).
    /// </para>
    /// </remarks>
    public static string FormatEnum<TEnum>(TEnum value, bool isFlags = false)
        where TEnum : struct, Enum
    {
        var members = GetEnumMembers(typeof(TEnum));
        var numericValue = ToInt32(value);
        if (numericValue == 0)
        {
            return members.Count > 0 && members[0].Value == 0 ? members[0].Name : "0";
        }

        if (isFlags)
        {
            var result = new List<string>();
            var remainingFlags = numericValue;
            foreach (var member in members)
            {
                if (member.Value > numericValue)
                {
                    break;
                }

                if (member.Value != 0 && (member.Value & numericValue) != 0)
                {
                    result.Add(member.Name);
                    remainingFlags &= ~member.Value;
                }
            }

            if (remainingFlags == 0)
            {
                return string.Join("|", result);
            }
        }
        else
        {
            foreach (var member in members)
            {
                if (member.Value == numericValue)
                {
                    return member.Name;
                }
            }
        }

        return numericValue.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>ported from: src/compiler/debug.ts:420 — <c>enumMemberCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's cache is keyed by the enum object; here it is keyed by the enum type, since that is what
    /// reflection is asked about. The lock is defensive — TS's <c>Map</c> is only ever touched on one thread, but a
    /// debug entry point in the port is callable from any.
    /// </remarks>
    private static readonly Dictionary<Type, IList<EnumMember>> s_enumMemberCache = new();

    /// <summary>ported from: src/compiler/debug.ts:422-442 — <c>getEnumMembers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS walks the enum object with <c>for (const name in enumObject)</c> and keeps the entries whose value
    /// is a number, which is the members in declaration order, aliases included. C# has no such object, so the table is
    /// built from the enum type's <c>Public | Static</c> fields and their <c>GetRawConstantValue()</c>.
    /// <c>Enum.GetValues</c>/<c>Enum.GetName</c> are deliberately avoided: with a duplicated value (the port has
    /// aliases such as <c>SyntaxKind.FirstAssignment == SyntaxKind.EqualsToken</c>) .NET's name lookup picks an
    /// unspecified one of the aliases, whereas field order is declaration order, exactly as in TS.
    /// <para>
    /// PORT-NOTE: TS sorts with <c>toSorted(result, (x, y) =&gt; compareValues(x[0], y[0]))</c>;
    /// <see cref="Core.ToSorted{T}"/> is that same stable sort, so members that share a value keep declaration order
    /// and the first of them is the one a non-flags lookup returns.
    /// </para>
    /// <para>
    /// PORT-NOTE: a member's value is read with <see cref="ToInt32"/>, the same 32-bit conversion
    /// <see cref="FormatEnum{TEnum}"/> uses, so a <c>uint</c> member with bit 31 set is read as the negative value TS
    /// stores rather than overflowing.
    /// </para>
    /// </remarks>
    private static IList<EnumMember> GetEnumMembers(Type enumType)
    {
        lock (s_enumMemberCache)
        {
            if (s_enumMemberCache.TryGetValue(enumType, out var cached))
            {
                return cached;
            }

            var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            var result = new List<EnumMember>(fields.Length);
            foreach (var field in fields)
            {
                result.Add(new EnumMember(ToInt32(field.GetRawConstantValue()!), field.Name));
            }

            IList<EnumMember> members = Core.ToSorted(result, static (x, y) => Core.CompareValues(x.Value, y.Value));
            s_enumMemberCache.Add(enumType, members);
            return members;
        }
    }

    /// <summary>ported from: src/compiler/debug.ts:431-439 — TS's <c>[number, string]</c> enum-member tuple.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds these as tuples in <c>getEnumMembers</c> and sorts them; a record struct is the same pair
    /// with names the lookup reads better against.
    /// </remarks>
    private readonly record struct EnumMember(int Value, string Name);

    // NOT ported from TS — this has no counterpart there.
    /// <summary>
    /// The 32-bit value of an enum member, or of a raw constant read back from an enum field.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS needs no such helper because the value is already a JS number there. It exists because the port's
    /// flag enums are <c>uint</c>-backed while TS writes their bit-31 members as <c>1 &lt;&lt; 31</c>
    /// (types.ts:6365, 8251, 8644), which is the negative 32-bit number at runtime — so the signed 32-bit read is what
    /// reproduces TS's value, where <c>Convert.ToInt32</c> would throw instead. Every enum in the port is 4 bytes
    /// (docs/architecture.md §4.5), so the read is lossless.
    /// </remarks>
    private static int ToInt32(object value) => unchecked((int)Convert.ToInt64(value, CultureInfo.InvariantCulture));

    /// <summary>ported from: src/compiler/debug.ts:444-446 — <c>formatSyntaxKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>SyntaxKind | undefined</c>, and <c>formatEnum</c>'s <c>value = 0</c> default
    /// turns that <c>undefined</c> into <c>0</c>, which <c>formatEnum</c> reports as the enum's zero member —
    /// <c>"Unknown"</c>. <c>?? 0</c> reproduces that exactly, so a null kind is not a distinct string.
    /// </remarks>
    public static string FormatSyntaxKind(SyntaxKind? kind) => FormatEnum(kind ?? 0, isFlags: false);

    /// <summary>ported from: src/compiler/debug.ts:492-494 — <c>formatFlowFlags</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as in <see cref="FormatSyntaxKind"/>, TS's <c>undefined</c> reaches <c>formatEnum</c>'s default and
    /// formats as <c>"0"</c> (FlowFlags has no zero member); <c>?? 0</c> reproduces that.
    /// </remarks>
    public static string FormatFlowFlags(FlowFlags? flags) => FormatEnum(flags ?? 0, isFlags: true);

    // ---------------------------------------------------------------------------------------------
    // Flow node debug info
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/debug.ts:552-569 — <c>attachFlowNodeDebugInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS installs <c>__tsDebuggerDisplay</c>, <c>__debugFlowFlags</c> and <c>__debugToString</c> on the node
    /// — through a shared prototype when <c>Object.setPrototypeOf</c> exists, directly otherwise — and only when its
    /// <c>isDebugInfoEnabled</c> flag is set. A C# object's members cannot be added at run time, and
    /// <see cref="FlowNode"/> already declares the first two as real members (<c>DebugDisplay</c>,
    /// <c>DebugFlowFlags</c>), with <see cref="System.Diagnostics.DebuggerDisplayAttribute"/> on the class standing in
    /// for the attachment. That is the faithful counterpart of what TS does, not a stub: the debug surface exists, it
    /// is declared on the class instead of attached, so there is nothing left to install and this member returns its
    /// argument unchanged — the same object TS returns. The <c>isDebugInfoEnabled</c> gate has no counterpart either,
    /// since with the members always declared the flag would guard nothing.
    /// <para>
    /// PORT-NOTE: TS's third property, <c>__debugToString</c>, is a call to <c>formatControlFlowGraph</c>, which the
    /// port has not reached; there is nothing to attach it to yet.
    /// </para>
    /// </remarks>
    public static FlowNode AttachFlowNodeDebugInfo(FlowNode flowNode) => flowNode;
}
