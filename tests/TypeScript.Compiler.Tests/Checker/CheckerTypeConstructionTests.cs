namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:5517-5573 (createType … createTypeParameter)
// ported from: src/compiler/checker.ts:2633-2696 (createSymbol, cloneSymbol, recordMergedSymbol)
// ported from: src/compiler/checker.ts:2513-2525 (lookupOrIssueError)
// ported from: src/compiler/checker.ts:5583-5640 (getNamedMembers, isNamedMember)
// ported from: src/compiler/utilities.ts:8040-8042 (getCheckFlags)
/// <summary>
/// The checker's type constructors: which concrete class each <see cref="TypeFlags"/> reaches, how type ids are
/// allocated, and what a synthesized symbol's links look like.
/// </summary>
/// <remarks>
/// The expected values are the reference's own: the class each flag combination must produce is fixed by M6-0's
/// <c>TypeFlags → class</c> chain (the casts the reference performs structurally have to succeed here), the id
/// sequence comes from <c>checker.ts:5518-5521</c> (<c>typeCount++</c> then <c>result.id = typeCount</c>, so ids start
/// at 1 and are contiguous), the symbol's <c>Transient</c> flag and its links' check flags come from
/// <c>checker.ts:2633-2638</c>, and <c>GetCheckFlags</c> reads the same slot <c>utilities.ts:8040-8042</c> reads.
/// </remarks>
public class CheckerTypeConstructionTests
{
    private static Checker CreateChecker(FakeCompilerOptions? options = null) => new(new FakeTypeCheckerHost(options));

    [Fact]
    public void CreateType_DispatchesEachFlagsBitToItsClass()
    {
        var checker = CreateChecker();

        // types.ts:6330-6430 — one bit per shape; the dispatch order is forced by the combinations below.
        Assert.IsType<IntrinsicType>(checker.CreateType(TypeFlags.Any));
        Assert.IsType<IntrinsicType>(checker.CreateType(TypeFlags.Unknown));
        Assert.IsType<IntrinsicType>(checker.CreateType(TypeFlags.String));
        Assert.IsType<IntrinsicType>(checker.CreateType(TypeFlags.Never));
        Assert.IsType<IntrinsicType>(checker.CreateType(TypeFlags.NonPrimitive));
        Assert.IsType<BooleanLiteralType>(checker.CreateType(TypeFlags.BooleanLiteral));

        // checker.ts:20320-20326 — `TypeFlags.EnumLiteral | StringLiteral` / `| NumberLiteral` are created by
        // getEnumLiteralType, and the checker reads the value back through the literal face the bit selects.
        Assert.IsType<StringLiteralType>(checker.CreateType(TypeFlags.StringLiteral));
        Assert.IsType<NumberLiteralType>(checker.CreateType(TypeFlags.NumberLiteral));
        Assert.IsType<BigIntLiteralType>(checker.CreateType(TypeFlags.BigIntLiteral));
        Assert.IsType<StringLiteralType>(checker.CreateType(TypeFlags.EnumLiteral | TypeFlags.StringLiteral));
        Assert.IsType<NumberLiteralType>(checker.CreateType(TypeFlags.EnumLiteral | TypeFlags.NumberLiteral));
        Assert.IsType<LiteralType>(checker.CreateType(TypeFlags.EnumLiteral));

        Assert.IsType<EnumType>(checker.CreateType(TypeFlags.Enum));
        Assert.IsType<UniqueESSymbolType>(checker.CreateType(TypeFlags.UniqueESSymbol));
        Assert.IsType<TypeParameter>(checker.CreateType(TypeFlags.TypeParameter));
        Assert.IsType<IndexType>(checker.CreateType(TypeFlags.Index));
        Assert.IsType<IndexedAccessType>(checker.CreateType(TypeFlags.IndexedAccess));
        Assert.IsType<ConditionalType>(checker.CreateType(TypeFlags.Conditional));
        Assert.IsType<SubstitutionType>(checker.CreateType(TypeFlags.Substitution));
        Assert.IsType<TemplateLiteralType>(checker.CreateType(TypeFlags.TemplateLiteral));
        Assert.IsType<StringMappingType>(checker.CreateType(TypeFlags.StringMapping));
        Assert.IsType<UnionType>(checker.CreateType(TypeFlags.Union));
        Assert.IsType<IntersectionType>(checker.CreateType(TypeFlags.Intersection));
    }

    [Fact]
    public void CreateType_ObjectFamilyLoadsTheLeafSoEveryFamilyCastSucceeds()
    {
        var checker = CreateChecker();
        var type = checker.CreateType(TypeFlags.Object);

        // M6-0's Model/ObjectType.cs: the family's factories instantiate the leaf, which is what makes the casts the
        // reference performs on the same object (`as ObjectType`, `as TypeReference`, `as GenericType`, `as AnonymousType`,
        // `as ResolvedType`, `as MappedType`) succeed here.
        Assert.IsType<InstantiationExpressionType>(type);
        Assert.IsAssignableFrom<ObjectType>(type);
        Assert.IsAssignableFrom<TypeReference>(type);
        Assert.IsAssignableFrom<GenericType>(type);
        Assert.IsAssignableFrom<AnonymousType>(type);
        Assert.IsAssignableFrom<ResolvedType>(type);
        Assert.IsAssignableFrom<MappedType>(type);
        Assert.IsAssignableFrom<UnionOrIntersectionType>(type);
    }

    [Fact]
    public void CreateType_AllocatesContiguousIdsFromOneAndCreateOriginTypeDoesNot()
    {
        var checker = CreateChecker();
        Assert.Equal(0, checker._typeCount);

        var first = checker.CreateType(TypeFlags.Any);
        var second = checker.CreateType(TypeFlags.String);

        // checker.ts:5518-5521 — `typeCount++` runs before the assignment, so the first type is 1.
        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal(2, checker._typeCount);

        // checker.ts:5531-5533 — `createOriginType` is `new Type(checker, flags)` and never touches `typeCount`.
        var origin = checker.CreateOriginType(TypeFlags.String);
        Assert.Equal(0, origin.Id);
        Assert.Equal(2, checker._typeCount);

        // checker.ts:5525-5529 — `createTypeWithSymbol` goes through `createType`, so it *does* consume an id.
        var withSymbol = checker.CreateTypeWithSymbol(TypeFlags.Object, checker.CreateSymbol(SymbolFlags.Class, "C"));
        Assert.Equal(3, withSymbol.Id);
        Assert.NotNull(withSymbol.Symbol);
    }

    [Fact]
    public void InitializeBuiltinTypes_BuildsTheIntrinsicsInTheReferenceOrder()
    {
        var checker = CreateChecker();
        checker.InitializeBuiltinTypes();

        // checker.ts:2078 — the first intrinsic is `anyType`; checker.ts:2087-2092 — `undefined`/`null` are created
        // before every type that can contain them (the comment at checker.ts:2073-2075 says why).
        Assert.Equal(1, checker._anyType.Id);
        Assert.Equal("any", ((IIntrinsicType)checker._anyType).IntrinsicName);
        Assert.True(checker._undefinedType.Id < checker._booleanType.Id);
        Assert.True(checker._nullType.Id < checker._booleanType.Id);

        // checker.ts:2097-2110 — the four boolean literals point at each other and `boolean` is their union.
        Assert.Same(checker._regularTrueType, checker._trueType.RegularType);
        Assert.Same(checker._trueType, checker._trueType.FreshType);
        Assert.IsType<UnionType>(checker._booleanType);
        Assert.Equal([checker._regularFalseType, checker._regularTrueType], ((IUnionType)checker._booleanType).Types);

        // checker.ts:2108-2109 — `undefinedWideningType = strictNullChecks ? undefinedType : <widening intrinsic>` and
        // `nullWideningType` likewise. utilities.ts:9369-9371 — `getStrictOptionValue` answers `strict !== false` for
        // an option that is *not set*, so the fake host's all-unset options are strict and both widening slots are the
        // very same objects (checked against lib/typescript.js: `getStrictOptionValue({}, "strictNullChecks")` is
        // `true`, `{ strictNullChecks: false }` is `false`).
        Assert.Same(checker._undefinedType, checker._undefinedWideningType);
        Assert.Same(checker._nullType, checker._nullWideningType);

        // The widening arm is what an explicitly non-strict option reaches, and then the two pairs are distinct
        // intrinsics (the ids differ too, since the widening types are created in the same ordered block).
        var loose = CreateChecker(new FakeCompilerOptions { StrictNullChecks = false });
        loose.InitializeBuiltinTypes();
        Assert.NotSame(loose._undefinedType, loose._undefinedWideningType);
        Assert.NotSame(loose._nullType, loose._nullWideningType);
    }

    [Fact]
    public void CreateSymbol_SetsTheTransientFlagAndItsLinksCheckFlags()
    {
        var checker = CreateChecker();

        var symbol = checker.CreateSymbol(SymbolFlags.Property, "p", CheckFlags.Readonly);

        // checker.ts:2633-2638 — `new Symbol(flags | SymbolFlags.Transient, name)`, a fresh SymbolLinks, and
        // `links.checkFlags = checkFlags || CheckFlags.None`.
        Assert.Equal(SymbolFlags.Property | SymbolFlags.Transient, symbol.Flags);
        Assert.Equal("p", symbol.EscapedName);
        Assert.NotNull(symbol.Links);

        // utilities.ts:8040-8042 — getCheckFlags answers `links.checkFlags` for a transient symbol and 0 otherwise.
        Assert.Equal(CheckFlags.Readonly, checker.GetCheckFlags(symbol));
        Assert.Equal(CheckFlags.None, checker.GetCheckFlags(new Symbol(SymbolFlags.Property, "q")));

        // checker.ts:2634 — `symbolCount++`.
        Assert.Equal(1, checker._symbolCount);
    }

    [Fact]
    public void GetSymbolLinks_ReusesOneLinksObjectPerNonTransientSymbol()
    {
        var checker = CreateChecker();
        var symbol = new Symbol(SymbolFlags.Class, "C");

        // checker.ts:2932-2936 — the table is indexed by getSymbolId and `??=` means one object per symbol.
        var first = checker.GetSymbolLinks(symbol);
        var second = checker.GetSymbolLinks(symbol);
        Assert.Same(first, second);

        // A transient symbol answers its own links, which createSymbol assigned (checker.ts:2635).
        var transient = checker.CreateSymbol(SymbolFlags.Property, "t");
        Assert.Same(transient.Links, checker.GetSymbolLinks(transient));
    }

    [Fact]
    public void GetMergedSymbol_FollowsTheRecordedClone()
    {
        var checker = CreateChecker();
        var source = new Symbol(SymbolFlags.Class, "C") { Declarations = [] };

        Assert.Same(source, checker.GetMergedSymbol(source));

        // checker.ts:2682-2696 — `cloneSymbol` copies the flags/name/declarations and calls
        // `recordMergedSymbol(result, symbol)` (checker.ts:2674-2680), which gives the *source* the merge id and points
        // `mergedSymbols[source.mergeId]` at the clone. The clone is not itself recorded (it keeps the merge id
        // `createSymbol` left it with), so `getMergedSymbol` (checker.ts:5307-5312) answers the clone for the clone and
        // the clone for the source. The reference's own `getMergedSymbol` reads `symbol.mergeId && …`, i.e. absence and
        // zero are the same answer.
        var clone = checker.CloneSymbol(source);
        Assert.True(source.MergeId > 0, "cloneSymbol records the *source* symbol, not the clone");
        Assert.Same(clone, checker.GetMergedSymbol(source));
        Assert.Same(clone, checker.GetMergedSymbol(clone));
    }

    [Fact]
    public void GetExcludedSymbolFlags_MapsEachFlagToItsExcludesMask()
    {
        // checker.ts:2653-2665 — the reference's own table; the two checks below are the ones the member's first two
        // lines encode (BlockScopedVariableExcludes is `Value`, FunctionScopedVariableExcludes is `Value & ~FunctionScopedVariable`).
        Assert.Equal(SymbolFlags.Value, Checker.GetExcludedSymbolFlags(SymbolFlags.BlockScopedVariable));
        Assert.Equal(SymbolFlags.Alias, Checker.GetExcludedSymbolFlags(SymbolFlags.Alias));
        Assert.Equal(SymbolFlags.None, Checker.GetExcludedSymbolFlags(SymbolFlags.None));
    }

    [Fact]
    public void LookupOrIssueError_ReturnsTheFirstEquivalentDiagnostic()
    {
        var checker = CreateChecker();

        // checker.ts:2513-2525 — the first call adds to the collection and returns the new diagnostic; a second call
        // with an equivalent one (same code, same message, no file) returns the *existing* instance.
        var first = checker.LookupOrIssueError(null, Diagnostics.Cannot_find_name_0, "x");
        var second = checker.LookupOrIssueError(null, Diagnostics.Cannot_find_name_0, "x");
        Assert.Same(first, second);
        Assert.Single(checker._diagnostics.GetGlobalDiagnostics());

        // A different argument is a different message text, hence a second entry.
        var third = checker.LookupOrIssueError(null, Diagnostics.Cannot_find_name_0, "y");
        Assert.NotSame(first, third);
        Assert.Equal(2, checker._diagnostics.GetGlobalDiagnostics().Count);
    }

    [Fact]
    public void GetNamedMembers_KeepsTheSymbolTablesInsertionOrder()
    {
        var checker = CreateChecker();
        var members = Utilities.CreateSymbolTable();

        // The ordered table is M6-0's contract (Model/SymbolTable.cs): `b` then `a` must come back `b, a` — a
        // Dictionary<string, ISymbol> would hand them back in hash order.
        members["b"] = checker.CreateSymbol(SymbolFlags.Property, "b");
        members["a"] = checker.CreateSymbol(SymbolFlags.Property, "a");

        // checker.ts:5583-5595 — the non-stable-ordering arm walks the table and keeps what isNamedMember accepts
        // (symbolIsValue and not a reserved `__` name).
        Assert.Equal(["b", "a"], checker.GetNamedMembers(members, null).Select(s => s.EscapedName));

        // checker.ts:5575-5580 — a name starting with `__` is reserved, so `__index` never reaches the property list.
        members["__index"] = checker.CreateSymbol(SymbolFlags.Property, "__index");
        Assert.Equal(["b", "a"], checker.GetNamedMembers(members, null).Select(s => s.EscapedName));

        // checker.ts:5510-5515 — symbolIsValue is what decides; an Interface symbol is not a value and is dropped.
        members["c"] = checker.CreateSymbol(SymbolFlags.Interface, "c");
        Assert.Equal(["b", "a"], checker.GetNamedMembers(members, null).Select(s => s.EscapedName));
    }

    [Fact]
    public void CreateAnonymousType_SetsTheStructuredSlotsAndTheNamedProperties()
    {
        var checker = CreateChecker();
        var symbol = checker.CreateSymbol(SymbolFlags.TypeLiteral, "__type");
        var member = checker.CreateSymbol(SymbolFlags.Property, "m");
        var members = Utilities.CreateSymbolTable();
        members["m"] = member;

        // checker.ts:5660-5662 — createAnonymousType is setStructuredTypeMembers over an ObjectFlags.Anonymous object
        // type; checker.ts:5649-5662 assigns properties from the same table when it is not `emptySymbols`.
        var type = checker.CreateAnonymousType(symbol, members, [], [], []);

        Assert.Same(members, type.Members);
        Assert.Equal([member], type.Properties);
        Assert.Empty(type.CallSignatures!);
        Assert.Empty(type.ConstructSignatures!);
        Assert.Empty(type.IndexInfos!);
        Assert.Equal(ObjectFlags.Anonymous, type.ObjectFlags);

        // checker.ts:5656-5658 — the shared empty table skips the property computation.
        var empty = checker.CreateAnonymousType(null, checker._emptySymbols, [], [], []);
        Assert.Same(checker._emptySymbols, empty.Members);
        Assert.Empty(empty.Properties!);
    }

    [Fact]
    public void CreateIntrinsicType_RejectsADuplicateName()
    {
        var checker = CreateChecker();

        // checker.ts:5544-5550 — `checkIntrinsicName` fails loudly on a repeated (name, debugName) pair.
        checker.CreateIntrinsicType(TypeFlags.Any, "any");
        Assert.ThrowsAny<Exception>(() => checker.CreateIntrinsicType(TypeFlags.Any, "any"));
        checker.CreateIntrinsicType(TypeFlags.Any, "any", ObjectFlags.None, "second");
    }
}

/// <summary>
/// The smallest host the checker's constructor and the constructors above read: no files, no options.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>createTypeChecker</c> takes a real <c>Program</c>; M6-1 lands that. This fake exists so the
/// type constructors can be exercised before the program does, and it throws for every member it does not need.
/// </remarks>
internal sealed class FakeTypeCheckerHost : ITypeCheckerHost
{
    private readonly FakeCompilerOptions _options;

    /// <summary>Creates a host over <paramref name="options"/>, or over the all-unset fake when none is given.</summary>
    public FakeTypeCheckerHost(FakeCompilerOptions? options = null) => _options = options ?? new FakeCompilerOptions();

    public ICompilerOptions GetCompilerOptions() => _options;

    public List<SourceFile> GetSourceFiles() => [];

    public SourceFile? GetSourceFile(string? fileName) => null;

    public bool UseCaseSensitiveFileNames() => true;

    public bool FileExists(string? path) => false;

    public string GetCurrentDirectory() => "/";

    public bool DirectoryExists(string? path) => false;

    public string? ReadFile(string? path) => null;

    public string Realpath(string? path) => path ?? "";

    public object? GetSymlinkCache() => null;

    public IModuleSpecifierCache GetModuleSpecifierCache() => throw new NotImplementedException();

    public object? GetPackageJsonInfoCache() => null;

    public string? GetGlobalTypingsCacheLocation() => null;

    public string? GetNearestAncestorDirectoryWithPackageJson(string? fileName, string? rootDir) => null;

    public object? GetEmitSyntaxForUsageLocation(SourceFile? file, LiteralExpression? usage) => null;

    public IResolvedRefAndSource? GetRedirectFromOutput(string? filePath) => null;

    public object? GetModeForUsageLocation(SourceFile? file, LiteralExpression? usage) => null;

    public object? GetImpliedNodeFormatForEmit(SourceFile? sourceFile) => null;

    public ModuleKind GetEmitModuleFormatOfFile(SourceFile? sourceFile) => ModuleKind.None;

    public IResolvedModuleWithFailedLookupLocations? GetResolvedModule(SourceFile? f, string? moduleName, object? mode) => null;

    public bool TypesPackageExists(string? packageName) => false;

    public bool PackageBundlesTypes(string? packageName) => false;

    public bool IsSourceFileDefaultLibrary(SourceFile? file) => false;

    public bool IsSourceFileFromExternalLibrary(SourceFile? file) => false;

    public IResolvedRefAndOutputDts? GetRedirectFromSourceFile(string? fileName) => null;

    public bool IsSourceOfProjectReferenceRedirect(string? fileName) => false;

    public Dictionary<string, List<IFileIncludeReason>> GetFileIncludeReasons() => [];

    public string GetCommonSourceDirectory() => "";

    public object? GetDefaultResolutionModeForFile(SourceFile? sourceFile) => null;

    public object? GetModeForResolutionAtIndex(SourceFile? file, int index) => null;

    public object? GetModuleResolutionCache() => null;

    public Dictionary<string, List<string>>? RedirectTargetsMap => null;

    public void Trace(string? s) { }

    public object? GetCanonicalFileName { get; set; }
}
