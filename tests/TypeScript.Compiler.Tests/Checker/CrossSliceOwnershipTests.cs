using System.Reflection;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// Two of the port's declared homes must not name the same member: <see cref="Utilities"/> (TS's module-level
/// statics) and <see cref="Checker"/> (TS's <c>createTypeChecker</c> closure).
/// </summary>
/// <remarks>
/// This exists because the M6 fleet hit the failure mode twice in one day, in both directions: a member declared on
/// both sides (CS0111, which kills the whole partial class), and a member declared on neither because the slice that
/// was told to drop it believed someone else would re-add it. The rule that came out of those rounds is in
/// <c>docs/architecture.md</c> §15.15: <b>one name, one owner, one home</b>, and the deciding question is which class
/// the reference declares it in — <c>utilities.ts</c>/<c>utilitiesPublic.ts</c> go to <see cref="Utilities"/>,
/// everything inside <c>createTypeChecker</c> stays on <see cref="Checker"/>.
/// <para>
/// The guard is deliberately about the *pair* rather than about a list of names, so it keeps working as both classes
/// grow (the checker alone is a few thousand members by the time M6-5 lands). It asserts the intersection is exactly
/// <see cref="PinnedOverlaps"/>: there is no reason for the same member name to exist on both unless the reference
/// declares it in both places, and the one name that qualifies is pinned there with its reference lines. The three
/// recorded deviations (<c>GetObjectFlags</c>/<c>GetCheckFlags</c> on the checker, <c>GetNodeId</c> on the utilities)
/// are single-sided by construction.
/// </para>
/// </remarks>
public class CrossSliceOwnershipTests
{
    /// <summary>
    /// The one member name both classes may declare, with the reference lines that require each home.
    /// </summary>
    /// <remarks>
    /// <c>setTextRange</c> is declared twice in the reference — <c>factory/utilitiesPublic.ts:10-12</c> (the plain
    /// pos/end setter, which the port's <see cref="Utilities"/> owns, callers qualified) and <c>checker.ts:6517-6538</c>
    /// (the node builder's copy, which refuses to copy ranges across files and also plants an <c>original</c> pointer;
    /// the reference even imports the utilities one under a different name, <c>setTextRangeWorker</c>, at checker.ts:1010).
    /// §15.15's deciding question — which class the reference declares it in — therefore admits *both* homes, and C#
    /// cannot rename the local function to keep the reference's name. Any further entry here needs the same treatment.
    /// </remarks>
    private static readonly string[] PinnedOverlaps = ["SetTextRange"];

    [Fact]
    public void NoMemberNameIsDeclaredOnBothUtilitiesAndChecker()
    {
        var shared = MemberNames(typeof(Utilities)).Intersect(MemberNames(typeof(Checker)), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(string.Join(", ", PinnedOverlaps.Order(StringComparer.Ordinal)), string.Join(", ", shared));
    }

    /// <summary>
    /// Every member name the type declares — methods and properties and fields alike, the three shapes the port uses
    /// for what TS spells as a function, a parameter property and a module-level constant.
    /// </summary>
    /// <remarks>
    /// <c>DeclaredOnly</c> is the point of the guard: <c>GetMembers</c> without it also hands back what both classes
    /// *inherit* from <see cref="object"/> (<c>Equals</c>, <c>Finalize</c>, <c>GetHashCode</c>, <c>GetType</c>,
    /// <c>MemberwiseClone</c>, <c>ToString</c>), so the intersection could never be empty and the failure message listed
    /// those six names first — which is how the real overlap (a member of the same name declared on both sides) stayed
    /// hidden until the pin above was written.
    /// </remarks>
    private static HashSet<string> MemberNames(System.Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in type.GetMembers(All))
        {
            // Constructors (.ctor/.cctor) have no name of their own; type parameters and interfaces cannot collide
            // across these two types.
            if (member.MemberType is MemberTypes.Constructor or MemberTypes.NestedType) continue;
            names.Add(member.Name);
        }

        return names;
    }
}
