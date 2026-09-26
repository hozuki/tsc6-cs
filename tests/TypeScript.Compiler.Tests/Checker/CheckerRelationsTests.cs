namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:21271-21286 (isTypeIdenticalTo, compareTypesIdentical, compareTypesAssignable, compareTypesSubtypeOf)
// ported from: src/compiler/checker.ts:21287-21336 (isTypeSubtypeOf … areTypesComparable)
// ported from: src/compiler/checker.ts:22282-22312 (isTypeRelatedTo, the relation-cache lookup/write)
// ported from: src/compiler/checker.ts:2385-2389 (the five relation caches)
/// <summary>
/// M6-3's entry points: the identity/assignable/subtype relations over the built-in types, and the relation cache
/// they go through.
/// </summary>
/// <remarks>
/// The expected values are the reference's own decisions, not the port's:
/// <list type="bullet">
/// <item><c>isTypeRelatedTo</c> answers <c>True</c> for a source that *is* the target (checker.ts:22282-22288,
/// <c>if (source === target) return Ternary.True;</c>), so identity is reflexive for every built-in.</item>
/// <item><c>any</c> is assignable in both directions (checker.ts:22228-22280, <c>isSimpleTypeRelatedTo</c>'s
/// <c>TypeFacts.All</c> arm for <c>TypeFlags.Any</c>), a primitive is not assignable to a different primitive, and
/// <c>never</c> is assignable to everything while nothing but <c>never</c> is assignable to it.</item>
/// <item>a constituent is assignable to the union that contains it (checker.ts:22380+'s
/// <c>unionOrIntersectionRelatedTo</c>).</item>
/// <item><c>isTypeRelatedTo</c> writes the relation table only for a key it has not seen (checker.ts:22295-22310),
/// so a repeated query must not grow the table.</item>
/// </list>
/// </remarks>
public class CheckerRelationsTests
{
    private static Checker CreateCheckerWithBuiltins()
    {
        var checker = new Checker(new FakeTypeCheckerHost());
        checker.InitializeBuiltinTypes();
        return checker;
    }

    [Fact]
    public void Identity_IsReflexiveOverTheBuiltinsAndDistinguishesPrimitives()
    {
        var checker = CreateCheckerWithBuiltins();

        // checker.ts:21271-21273 — `isTypeIdenticalTo` is `isTypeRelatedTo(source, target, identityRelation)`.
        Assert.True(checker.IsTypeIdenticalTo(checker._stringType, checker._stringType));
        Assert.True(checker.IsTypeIdenticalTo(checker._anyType, checker._anyType));
        Assert.False(checker.IsTypeIdenticalTo(checker._stringType, checker._numberType));
        Assert.False(checker.IsTypeIdenticalTo(checker._stringType, checker._undefinedType));

        // checker.ts:21275-21277 — `compareTypesIdentical` is the Ternary face of the same question.
        Assert.Equal(Ternary.True, checker.CompareTypesIdentical(checker._numberType, checker._numberType));
        Assert.Equal(Ternary.False, checker.CompareTypesIdentical(checker._numberType, checker._stringType));

        // checker.ts:21279-21285 — the assignable/subtype comparers are the other two Ternary faces.
        Assert.Equal(Ternary.True, checker.CompareTypesAssignable(checker._stringType, checker._anyType));
        Assert.Equal(Ternary.True, checker.CompareTypesSubtypeOf(checker._neverType, checker._stringType));
    }

    [Fact]
    public void Assignable_HandlesAnyNeverAndDistinctPrimitives()
    {
        var checker = CreateCheckerWithBuiltins();

        // checker.ts:21295-21307 — the public predicate goes through `assignableRelation` after the
        // `isFreshLiteralType` normalization.
        Assert.True(checker.IsTypeAssignableTo(checker._anyType, checker._stringType));
        Assert.True(checker.IsTypeAssignableTo(checker._stringType, checker._anyType));

        // checker.ts:22228-22280 — `never` is the bottom type: assignable to everything, and nothing else to it.
        Assert.True(checker.IsTypeAssignableTo(checker._neverType, checker._stringType));
        Assert.True(checker.IsTypeAssignableTo(checker._neverType, checker._numberType));
        Assert.False(checker.IsTypeAssignableTo(checker._stringType, checker._neverType));

        // Distinct primitive domains are not assignable to each other (TypeFacts.EQString vs EQNumber).
        Assert.False(checker.IsTypeAssignableTo(checker._stringType, checker._numberType));
        Assert.False(checker.IsTypeAssignableTo(checker._numberType, checker._stringType));
        Assert.False(checker.IsTypeAssignableTo(checker._stringType, checker._bigintType));
    }

    [Fact]
    public void Assignable_IsADirectedRelationOverUnionsAndUndefined()
    {
        var checker = CreateCheckerWithBuiltins();

        // A constituent is assignable to the union that contains it (unionOrIntersectionRelatedTo), and the union is
        // not assignable to the single constituent.
        var stringOrNumber = checker.GetUnionType([checker._stringType, checker._numberType]);
        Assert.True(checker.IsTypeAssignableTo(checker._stringType, stringOrNumber));
        Assert.True(checker.IsTypeAssignableTo(checker._numberType, stringOrNumber));
        Assert.False(checker.IsTypeAssignableTo(stringOrNumber, checker._stringType));

        // `undefined` as a *target* accepts only `undefined`/`void`/`any` (the reference's TypeFacts arms for
        // TypeFlags.Undefined), so a primitive is not assignable to it. (The opposite direction depends on
        // strictNullChecks and is deliberately not pinned here.)
        Assert.False(checker.IsTypeAssignableTo(checker._stringType, checker._undefinedType));
        Assert.True(checker.IsTypeAssignableTo(checker._undefinedType, checker._undefinedType));
    }

    [Fact]
    public void Subtype_IsStricterThanAssignability()
    {
        var checker = CreateCheckerWithBuiltins();

        // checker.ts:21287-21289 — `isTypeSubtypeOf` uses `subtypeRelation`, whose simple-type arm is
        // `isSimpleTypeRelatedTo`'s: `never` is its bottom element (checker.ts:22231, `s & TypeFlags.Never`) and so a
        // subtype of every non-unknown target. `any` is *not* one: its arm is `if (s & TypeFlags.Any) return true`
        // inside `if (relation === assignableRelation || relation === comparableRelation)` (checker.ts:22265-22266), and
        // the subtype relation is neither — after the simple arm declines, `isTypeRelatedTo` finds `Any` neither
        // `Object` (checker.ts:22302) nor `StructuredOrInstantiable` (22308) and answers false at 22311.
        Assert.True(checker.IsTypeSubtypeOf(checker._neverType, checker._stringType));
        Assert.False(checker.IsTypeSubtypeOf(checker._anyType, checker._stringType));
        Assert.False(checker.IsTypeSubtypeOf(checker._stringType, checker._neverType));
        Assert.True(checker.IsTypeSubtypeOf(checker._stringType, checker._stringType));
        // …while `any` *is* the assignable/comparable arm's answer, which is what makes the two predicates differ.
        Assert.True(checker.IsTypeAssignableTo(checker._anyType, checker._stringType));

        // checker.ts:21291-21293 — `isTypeStrictSubtypeOf` is `isTypeRelatedTo(source, target, strictSubtypeRelation)`,
        // and `isTypeRelatedTo`'s very first test after the fresh-literal normalization is `source === target → true`
        // (checker.ts:22289-22291), which every relation shares. "Strict" is the *relation's* own simple-arm rule (the
        // `t & TypeFlags.Unknown` arm at 22232, `!(relation === strictSubtypeRelation && s & TypeFlags.Any)`), not an
        // identity exclusion — so a type is a strict subtype of itself here.
        Assert.True(checker.IsTypeStrictSubtypeOf(checker._stringType, checker._stringType));
        Assert.True(checker.IsTypeStrictSubtypeOf(checker._neverType, checker._stringType));
    }

    [Fact]
    public void IsTypeRelatedTo_CachesTheAnswerPerRelationAndDoesNotGrowOnRepeat()
    {
        // checker.ts:22282-22312 — the table is consulted and written only on the structured paths. A query between two
        // primitives answers from `isSimpleTypeRelatedTo` and leaves the function at `return false` (checker.ts:22311)
        // without ever touching the map: neither `String` nor `Number` is `Object` (22302) or
        // `StructuredOrInstantiable` (22308).
        var primitives = CreateCheckerWithBuiltins();
        Assert.Empty(primitives._assignableRelation);
        Assert.False(primitives.IsTypeAssignableTo(primitives._stringType, primitives._numberType));
        Assert.Empty(primitives._assignableRelation);

        // Two object types *do* reach `checkTypeRelatedTo`, which ends by writing into the relation every key its
        // traversal pushed (checker.ts:21334-21336), so the second query finds the answer instead of recomputing it.
        var checker = CreateCheckerWithBuiltins();
        var source = checker.CreateAnonymousType(null, checker._emptySymbols, [], [], []);
        var target = checker.CreateAnonymousType(null, checker._emptySymbols, [], [], []);

        var first = checker.IsTypeAssignableTo(source, target);
        var countAfterFirst = checker._assignableRelation.Count;
        var second = checker.IsTypeAssignableTo(source, target);

        Assert.True(first);
        Assert.True(second);
        Assert.True(countAfterFirst > 0);
        Assert.Equal(countAfterFirst, checker._assignableRelation.Count);

        // The identity relation has its own cache (checker.ts:2385-2389) and the two predicates do not share keys:
        // an identity query adds to `identityRelation`, not to `assignableRelation`.
        Assert.Empty(checker._identityRelation);
        Assert.True(checker.IsTypeIdenticalTo(source, target));
        Assert.True(checker._identityRelation.Count > 0);
        Assert.Equal(countAfterFirst, checker._assignableRelation.Count);
    }

    [Fact]
    public void Comparable_AcceptsWhatAssignableAcceptsForTheBuiltins()
    {
        var checker = CreateCheckerWithBuiltins();

        // checker.ts:21330-21336 — `isTypeComparableTo`/`areTypesComparable` use `comparableRelation`, which for
        // primitives answers exactly what the assignable relation answers.
        Assert.True(checker.IsTypeComparableTo(checker._stringType, checker._stringType));
        Assert.True(checker.IsTypeComparableTo(checker._anyType, checker._stringType));
        Assert.False(checker.IsTypeComparableTo(checker._stringType, checker._numberType));
        Assert.True(checker.AreTypesComparable(checker._numberType, checker._numberType));
        Assert.False(checker.AreTypesComparable(checker._numberType, checker._stringType));
    }
}
