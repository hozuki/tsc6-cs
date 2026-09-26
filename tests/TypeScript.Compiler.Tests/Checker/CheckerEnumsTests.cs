namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:1157-1450 (the module-level enums of checker.ts)
/// <summary>
/// The checker's module-level enums: the masks TS builds with <c>~</c>, the alias members, and the order of the
/// sequence enums.
/// </summary>
/// <remarks>
/// These are transcriptions of <c>checker.ts</c>, and the port had to restate four of <see cref="TypeFacts"/>'s
/// members as <c>int</c> expressions because C# defines <c>~</c> for integer types rather than enum types. The
/// numbers below are what the reference expressions evaluate to, so a typo in a member or in a mask fails here
/// rather than in a control-flow narrowing decision much later.
/// </remarks>
public class CheckerEnumsTests
{
    [Fact]
    public void TypeFacts_MasksHaveTheReferenceValues()
    {
        // checker.ts:1246 `All = (1 << 27) - 1`.
        Assert.Equal(134217727, (int)TypeFacts.All);

        // checker.ts:1249 `IsUndefinedOrNull = IsUndefined | IsNull`.
        Assert.Equal(TypeFacts.IsUndefined | TypeFacts.IsNull, TypeFacts.IsUndefinedOrNull);
        Assert.Equal(50331648, (int)TypeFacts.IsUndefinedOrNull);

        // checker.ts:1306-1309, the four members written with `~`:
        //   OrFactsMask = TypeofEQFunction | TypeofNEObject
        //   AndFactsMask = All & ~OrFactsMask
        //   EmptyObjectStrictFacts = All & ~(EQUndefined | EQNull | EQUndefinedOrNull | IsUndefinedOrNull)
        //   EmptyObjectFacts = UnknownFacts = All & ~IsUndefinedOrNull
        Assert.Equal((int)TypeFacts.TypeofEQFunction | (int)TypeFacts.TypeofNEObject, (int)TypeFacts.OrFactsMask);
        Assert.Equal(8256, (int)TypeFacts.OrFactsMask);
        Assert.Equal(83427327, (int)TypeFacts.EmptyObjectStrictFacts);
        Assert.Equal(83886079, (int)TypeFacts.EmptyObjectFacts);
        Assert.Equal(83886079, (int)TypeFacts.UnknownFacts);
        Assert.Equal(134209471, (int)TypeFacts.AndFactsMask);
    }

    [Fact]
    public void TypeFacts_MasksPartitionTheWholeSet()
    {
        // The two masks are meant to be disjoint and to cover `All` (checker.ts:1306-1309), which is what lets
        // `getTypeFacts` split a set of facts into the "or" and the "and" halves.
        Assert.Equal(0, (int)TypeFacts.OrFactsMask & (int)TypeFacts.AndFactsMask);
        Assert.Equal(TypeFacts.All, TypeFacts.OrFactsMask | TypeFacts.AndFactsMask);

        // The empty-object facts are exactly the ones that exclude undefined/null.
        Assert.Equal(0, (int)TypeFacts.EmptyObjectStrictFacts & (int)TypeFacts.IsUndefinedOrNull);
        Assert.Equal(0, (int)TypeFacts.EmptyObjectStrictFacts & (int)TypeFacts.EQUndefinedOrNull);
        Assert.Equal((int)TypeFacts.EQUndefinedOrNull, (int)TypeFacts.EmptyObjectFacts & (int)TypeFacts.EQUndefinedOrNull);
    }

    [Fact]
    public void FlagEnums_ComposeTheSameAliasesAsTheReference()
    {
        // checker.ts:1186-1196
        Assert.Equal(
            IterationUse.AllowsSyncIterablesFlag | IterationUse.AllowsAsyncIterablesFlag | IterationUse.AllowsStringInputFlag | IterationUse.ForOfFlag,
            IterationUse.ForAwaitOf);
        Assert.Equal(IterationUse.AllowsSyncIterablesFlag, IterationUse.Element);
        Assert.Equal(IterationUse.AllowsSyncIterablesFlag | IterationUse.SpreadFlag, IterationUse.Spread);

        // checker.ts:1355-1362
        Assert.Equal(SignatureCheckMode.BivariantCallback | SignatureCheckMode.StrictCallback, SignatureCheckMode.Callback);

        // checker.ts:1341 — `Normal = 0`, so a default-initialised mode is a plain check.
        Assert.Equal(0, (int)CheckMode.Normal);
        Assert.Equal(0, (int)TypeFacts.None);
        Assert.Equal(0, (int)SignatureCheckMode.None);
        Assert.Equal(0, (int)IntersectionState.None);
        Assert.Equal(0, (int)RecursionFlags.None);
        Assert.Equal(0, (int)ExpandingFlags.None);
        Assert.Equal(0, (int)DeclarationSpaces.None);
        Assert.Equal(0, (int)MinArgumentCountFlags.None);
    }

    [Fact]
    public void SequenceEnums_KeepTheReferenceOrder()
    {
        Assert.Equal(
            ["Unspecified", "Identifier", "Property", "ExportAssignment", "Jsx", "AsyncFunction", "ExportImportEquals", "ExportSpecifier", "Decorator"],
            Enum.GetNames<ReferenceHint>());
        Assert.Equal(["Yield", "Return", "Next"], Enum.GetNames<IterationTypeKind>());
        Assert.Equal(["Normal", "FunctionReturn", "GeneratorNext", "GeneratorYield"], Enum.GetNames<WideningKind>());
        Assert.Equal(
            [
                "Type",
                "ResolvedBaseConstructorType",
                "DeclaredType",
                "ResolvedReturnType",
                "ImmediateBaseConstraint",
                "ResolvedTypeArguments",
                "ResolvedBaseTypes",
                "WriteType",
                "ParameterInitializerContainsUndefined",
            ],
            Enum.GetNames<TypeSystemPropertyName>());
        Assert.Equal(["None", "Filtering", "Remapping"], Enum.GetNames<MappedTypeNameTypeKind>());
        Assert.Equal(["Local", "Parameter"], Enum.GetNames<UnusedKind>());
        Assert.Equal(["Uppercase", "Lowercase", "Capitalize", "Uncapitalize", "NoInfer"], Enum.GetNames<IntrinsicTypeKind>());
    }

}
