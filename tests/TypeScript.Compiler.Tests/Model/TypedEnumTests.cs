using System.Reflection;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// Acceptance for the generated <c>TypedEnum</c> members (<c>SyntaxKind</c> as types — docs/architecture.md §16).
/// </summary>
public class TypedEnumTests
{
    /// <summary>Every <c>SyntaxKind</c> member that is not an alias or the <c>Count</c> sentinel has a type.</summary>
    private static List<Type> MemberTypes() =>
        [.. typeof(TypedEnum).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITypedEnum).IsAssignableFrom(t))];

    [Fact]
    public void MemberTypesCoverEveryKindValueThatIsNotTheSentinel()
    {
        Assert.Equal(359, MemberTypes().Count);

        // The lookup matches by *value*: 396 declared members hold only 359 distinct values (the rest are aliases
        // or the Count sentinel sharing a value), and every one of those values is answered — which is why 359
        // member types suffice. Count has no type of its own; its value belongs to a real kind.
        var values = Enum.GetValues<SyntaxKind>()
            .Select(kind => (int)kind)
            .Where(value => TypedEnum.FromSyntaxKind((SyntaxKind)value) is not null)
            .Distinct()
            .ToList();

        Assert.Equal(359, values.Count);
        Assert.Null(typeof(TypedEnum).GetNestedType("Count", BindingFlags.Public | BindingFlags.NonPublic));
    }

    /// <summary>
    /// The static half: a member type answers for itself without an instance, which is what lets it be used in
    /// generic and overloaded signatures.
    /// </summary>
    [Fact]
    public void EachMemberTypeReportsItsOwnKind()
    {
        Assert.Equal(SyntaxKind.EndOfFileToken, TypedEnum.EndOfFileToken.StaticSyntaxKind);
        Assert.Equal(SyntaxKind.CommaToken, TypedEnum.CommaToken.StaticSyntaxKind);
        Assert.Equal(SyntaxKind.EndOfFileToken, TypedEnum.EndOfFileToken.Instance.SyntaxKind);

        Assert.Same(TypedEnum.EndOfFileToken.Instance, TypedEnum.EndOfFileToken.Instance);
    }

    /// <summary><c>Instance</c> is the only instance: the member types seal themselves and take a private constructor.</summary>
    [Fact]
    public void MemberTypesCannotBeConstructedFromOutside()
    {
        var type = typeof(TypedEnum.CommaToken);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>The dynamic half: a value whose kind was not known at compile time resolves back to its member.</summary>
    [Fact]
    public void FromSyntaxKind_RoundTripsEveryKind()
    {
        static SyntaxKind RoundTrip(SyntaxKind kind) =>
            TypedEnum.FromSyntaxKind(kind)!.SyntaxKind;

        Assert.Equal(SyntaxKind.CommaToken, RoundTrip(SyntaxKind.CommaToken));
        Assert.Equal(SyntaxKind.EndOfFileToken, RoundTrip(SyntaxKind.EndOfFileToken));
        Assert.Equal(SyntaxKind.FirstNode, RoundTrip(SyntaxKind.FirstNode));

        // Every value the lookup answers for round-trips to itself.
        foreach (var kind in Enum.GetValues<SyntaxKind>())
        {
            if (TypedEnum.FromSyntaxKind(kind) is { } instance)
            {
                Assert.Equal(kind, instance.SyntaxKind);
            }
        }
    }

    /// <summary>
    /// Aliases and the sentinel have no type of their own — that is what keeps the reverse lookup's switch free of
    /// duplicate arms.
    /// </summary>
    [Fact]
    public void AliasesHaveNoMemberTypeButResolveToTheirTarget()
    {
        // An alias is the same kind under a second name, so it gets no type of its own (a type would make the
        // reverse lookup ambiguous) ...
        Assert.Null(typeof(TypedEnum).GetNestedType("FirstAssignment", BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(typeof(TypedEnum).GetNestedType("LastAssignment", BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(typeof(TypedEnum).GetNestedType("LastBinaryOperator", BindingFlags.Public | BindingFlags.NonPublic));

        // ... and because the lookup matches by value, asking about the alias answers with the target's member.
        Assert.Same(TypedEnum.EqualsToken.Instance, TypedEnum.FromSyntaxKind(SyntaxKind.FirstAssignment));
        Assert.Same(TypedEnum.EqualsToken.Instance, TypedEnum.FromSyntaxKind(SyntaxKind.EqualsToken));

        // Count is a sentinel rather than a kind, so nothing answers for it.
        Assert.Null(TypedEnum.FromSyntaxKind(SyntaxKind.Count));
    }

    /// <summary>
    /// All 36 aliases collapse onto their targets, which is why 396 declared members yield 359 types.
    /// </summary>
    [Fact]
    public void MemberTypeCountMatchesTheNonAliasMembers()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "TypeScript.Compiler", "Model", "TypedEnum.Members.cs"));

        var classes = System.Text.RegularExpressions.Regex.Matches(source, @"internal sealed class \w+ :")
            .Count;
        var arms = System.Text.RegularExpressions.Regex.Matches(source, @"SyntaxKind\.\w+ => \w+\.Instance,")
            .Count;

        Assert.Equal(classes, arms);
        Assert.Equal(MemberTypes().Count, classes);

        // 396 members in the TS enum: 359 types + 36 aliases + Count.
        Assert.Equal(396, classes + 36 + 1);
    }

    /// <summary>
    /// The pattern the whole mechanism exists for: a member type is usable as a generic argument, so a factory can
    /// overload on it.
    /// </summary>
    [Fact]
    public void MemberTypesWorkAsGenericArguments()
    {
        static SyntaxKind KindOf<TToken>()
            where TToken : ITypedEnum<TToken> => TToken.StaticSyntaxKind;

        static TToken Self<TToken>(TToken token)
            where TToken : ITypedEnum<TToken> => TToken.Instance;

        Assert.Equal(SyntaxKind.CommaToken, KindOf<TypedEnum.CommaToken>());
        Assert.Equal(SyntaxKind.EndOfFileToken, KindOf<TypedEnum.EndOfFileToken>());
        Assert.Same(TypedEnum.CommaToken.Instance, Self(TypedEnum.CommaToken.Instance));

        // And two member types are distinct types, which is what makes overload resolution possible.
        Assert.NotEqual(typeof(TypedEnum.CommaToken), typeof(TypedEnum.EndOfFileToken));
        Assert.False(typeof(TypedEnum.CommaToken).IsAssignableTo(typeof(TypedEnum.EndOfFileToken)));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tsc-cs.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
