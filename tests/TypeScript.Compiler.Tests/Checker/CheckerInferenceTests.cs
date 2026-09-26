namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:26774-27547 (inferTypes: the type-variable arm and the candidate bookkeeping)
// ported from: src/compiler/checker.ts:27608-27690 (getInferredType, getInferredTypes)
// ported from: src/compiler/checker.ts:32525-32550 (getContextualTypeForElementExpression, the tuple arm)
/// <summary>
/// M6-4's entry points: one inference path and one contextual-typing path, over the built-in types.
/// </summary>
/// <remarks>
/// The expected values are the reference's own decisions, not the port's:
/// <list type="bullet">
/// <item>Inferring a naked type variable: <c>inferFromTypes</c> takes the <c>TypeFlags.TypeVariable</c> arm
/// (checker.ts:26860-26900) and, with no propagation type and an unfixed inference, appends the source to
/// <c>inference.candidates</c> at the current priority, then clears the cached inference of every type parameter.
/// <c>getInferredType</c> (checker.ts:27608) has neither a contravariant candidate nor a constraint, so it answers
/// <c>getCovariantInference</c>'s widened union of the single candidate — for <c>string</c> that is <c>string</c>
/// itself (<c>inferTypeArguments</c>'s caller then instantiates the signature with it).</item>
/// <item>Inferring the same type variable twice from the same source must not duplicate the candidate:
/// <c>inferFromTypes</c> guards the append with <c>!contains(inference.candidates, candidate)</c>
/// (checker.ts:26891-26896).</item>
/// <item>A contextual tuple element: <c>getContextualTypeForElementExpression</c>'s
/// <c>isTupleType(t) &amp;&amp; index &lt; t.target.fixedLength</c> arm (checker.ts:32530-32532) answers the element type at
/// that index, with <c>removeMissingType</c> applied for an optional element.</item>
/// </list>
/// </remarks>
public class CheckerInferenceTests
{
    private static Checker CreateCheckerWithBuiltins()
    {
        var checker = new Checker(new FakeTypeCheckerHost());
        checker.InitializeBuiltinTypes();
        return checker;
    }

    [Fact]
    public void InferTypes_InfersANakedTypeVariableFromItsSource()
    {
        var checker = CreateCheckerWithBuiltins();
        var typeParameter = checker.CreateTypeParameter();
        var context = checker.CreateInferenceContext([typeParameter], /*signature*/ null, InferenceFlags.None);

        checker.InferTypes(context.Inferences!, checker._stringType, typeParameter);

        var inference = context.Inferences![0];
        Assert.NotNull(inference.Candidates);
        Assert.Single(inference.Candidates!);
        Assert.Same(checker._stringType, inference.Candidates![0]);
        Assert.Empty(inference.ContraCandidates ?? []);

        var inferred = checker.GetInferredTypes(context);
        Assert.Single(inferred);
        Assert.Same(checker._stringType, inferred[0]);
    }

    [Fact]
    public void InferTypes_DoesNotDuplicateARepeatedCandidate()
    {
        var checker = CreateCheckerWithBuiltins();
        var typeParameter = checker.CreateTypeParameter();
        var context = checker.CreateInferenceContext([typeParameter], /*signature*/ null, InferenceFlags.None);

        checker.InferTypes(context.Inferences!, checker._numberType, typeParameter);
        checker.InferTypes(context.Inferences!, checker._numberType, typeParameter);

        var candidates = context.Inferences![0].Candidates!;
        Assert.Single(candidates);
        Assert.Same(checker._numberType, candidates[0]);
    }

    [Fact]
    public void ContextualTypeForElementExpression_ReadsAFixedTupleElement()
    {
        var checker = CreateCheckerWithBuiltins();
        var tupleType = checker.CreateTupleType([checker._stringType, checker._numberType]);

        Assert.Same(checker._stringType, checker.GetContextualTypeForElementExpression(tupleType, 0));
        Assert.Same(checker._numberType, checker.GetContextualTypeForElementExpression(tupleType, 1));
    }
}
