namespace TypeScript.Compiler.Tests;

// ported from: src/compiler/checker.ts:20572-20636 (createTypeMapper … createBackreferenceMapper)
// ported from: src/compiler/checker.ts:21014-21176 (instantiateType, instantiateTypeWithAlias, instantiateTypeWorker,
//              getPermissiveInstantiation, getRestrictiveInstantiation)
// ported from: src/compiler/checker.ts:20918-20920 (getModifiedReadonlyState)
// ported from: src/compiler/checker.ts:1205-1219, 2185, 2200-2216, 45899-45905 (IterationTypesResolver and its cache)
// ported from: src/compiler/utilities.ts:8280-8282 (getObjectFlags)
/// <summary>
/// M6-2 的泛型/条件块：<c>TypeMapper</c> 的代数、<c>instantiateType</c> 的两道守卫、映射类型的 readonly 修饰、
/// <c>getObjectFlags</c> 的四个面，以及迭代协议缓存键到类型槽位的映射。
/// </summary>
/// <remarks>
/// 这些成员是 M6-2 里唯一能在不构造完整类型系统的情况下被独立观测的部分：mapper 的分派只读类型对象自己的槽位，
/// <c>instantiateType</c> 的守卫只读两个计数器，迭代缓存的键到槽位映射是端口对 <c>type[cacheKey]</c> 的等价改写
/// （<c>IterationTypesResolver</c> 的 remarks 说明了原因），而 <c>GetObjectFlags</c> 把 TS 的
/// <c>ObjectFlagsType</c> 联合（types.ts:6596）改成了四个接口的 pattern match。参考实现的行为由
/// <c>checker.ts</c> 的行号在每条的注释里钉住。
/// </remarks>
public class CheckerGenericsTests
{
    // ---------------------------------------------------------------------------------------------
    // 测试宿主：只满足 Checker 构造函数读的两个成员，其余按接口要求给空实现
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <see cref="ITypeCheckerHost"/> 的最小实现：<see cref="Checker"/> 的构造函数只读
    /// <see cref="GetCompilerOptions"/>（<see cref="GetSourceFiles"/> 只在 stable type ordering 时读）。
    /// </summary>
    private sealed class TestCheckerHost : ITypeCheckerHost
    {
        public ICompilerOptions Options { get; } = new FakeCompilerOptions();

        public ICompilerOptions GetCompilerOptions() => Options;

        public List<SourceFile> GetSourceFiles() => [];

        public SourceFile? GetSourceFile(string? fileName) => null;

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

        public string GetCurrentDirectory() => string.Empty;

        public object? GetCanonicalFileName { get; set; }

        public bool UseCaseSensitiveFileNames() => true;

        public bool FileExists(string? path) => false;

        public bool DirectoryExists(string? path) => false;

        public string? ReadFile(string? path) => null;

        public string Realpath(string? path) => path ?? string.Empty;

        public object? GetSymlinkCache() => null;

        public IModuleSpecifierCache GetModuleSpecifierCache() => null!;

        public object? GetPackageJsonInfoCache() => null;

        public string? GetGlobalTypingsCacheLocation() => null;

        public string? GetNearestAncestorDirectoryWithPackageJson(string? fileName, string? rootDir) => null;

        public Dictionary<string, List<string>>? RedirectTargetsMap => null;

        public Dictionary<string, List<IFileIncludeReason>> GetFileIncludeReasons() => [];

        public string GetCommonSourceDirectory() => string.Empty;

        public object? GetDefaultResolutionModeForFile(SourceFile? sourceFile) => null;

        public object? GetModeForResolutionAtIndex(SourceFile? file, int index) => null;

        public object? GetModuleResolutionCache() => null;

        public void Trace(string? s)
        {
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 夹具
    // ---------------------------------------------------------------------------------------------

    private static Checker NewChecker() => new(new TestCheckerHost());

    private static TypeParameter NewTypeParameter()
    {
        var typeParameter = new TypeParameter(null!, TypeFlags.TypeParameter);
        return typeParameter;
    }

    private static IntrinsicType NewIntrinsicType(string name, TypeFlags flags)
        => new(null!, flags) { IntrinsicName = name };

    // ---------------------------------------------------------------------------------------------
    // TypeMapper：五种形状的判别（Model/TypeMapper.cs）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TypeMapper_CarriesTheSlotsOfItsKindAndLeavesTheOthersNull()
    {
        var mapper = new TypeMapper { Kind = TypeMapKind.Simple };

        Assert.Equal(TypeMapKind.Simple, mapper.Kind);
        Assert.Null(mapper.Source);
        Assert.Null(mapper.Target);
        Assert.Null(mapper.Sources);
        Assert.Null(mapper.Targets);
        Assert.Null(mapper.TargetFactories);
        Assert.Null(mapper.Func);
        Assert.Null(mapper.Mapper1);
        Assert.Null(mapper.Mapper2);
    }

    // ---------------------------------------------------------------------------------------------
    // getMappedType：五种 kind 的分派（checker.ts:20576-20608）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetMappedType_Simple_MapsTheSourceAndPassesEverythingElseThrough()
    {
        var checker = NewChecker();
        var source = NewTypeParameter();
        var target = NewIntrinsicType("string", TypeFlags.String);
        var other = NewIntrinsicType("number", TypeFlags.Number);

        var mapper = checker.MakeUnaryTypeMapper(source, target);

        Assert.Same(target, checker.GetMappedType(source, mapper));
        Assert.Same(other, checker.GetMappedType(other, mapper));
    }

    [Fact]
    public void GetMappedType_Array_AnswersAnyWhenTheMapperCarriesNoTargets()
    {
        var checker = NewChecker();
        var source = NewTypeParameter();
        var anyType = NewIntrinsicType("any", TypeFlags.Any);
        checker._anyType = anyType;

        var erased = checker.MakeArrayTypeMapper([source], null);
        Assert.Same(anyType, checker.GetMappedType(source, erased));

        // 有 targets 时按位置取；不在 sources 里的类型原样返回。
        var target = NewIntrinsicType("string", TypeFlags.String);
        var mapped = checker.MakeArrayTypeMapper([source], [target]);
        Assert.Same(target, checker.GetMappedType(source, mapped));
        Assert.Same(anyType, checker.GetMappedType(anyType, mapped));
    }

    [Fact]
    public void GetMappedType_Deferred_CallsTheTargetFactory()
    {
        var checker = NewChecker();
        var source = NewTypeParameter();
        var target = NewIntrinsicType("number", TypeFlags.Number);
        var calls = 0;

        var mapper = checker.MakeDeferredTypeMapper([source], [() => { calls++; return target; }]);

        Assert.Same(target, checker.GetMappedType(source, mapper));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetMappedType_Composite_InstantiatesThroughTheSecondMapperWhileMergedDoesNot()
    {
        var checker = NewChecker();
        checker._errorType = NewIntrinsicType("error", TypeFlags.Any);
        checker._wildcardType = NewIntrinsicType("any", TypeFlags.Any);

        var first = NewTypeParameter();
        var second = NewTypeParameter();
        var number = NewIntrinsicType("number", TypeFlags.Number);

        var firstMapper = checker.MakeUnaryTypeMapper(first, second);
        var secondMapper = checker.MakeUnaryTypeMapper(second, number);

        // Composite：t1 !== type，且 kind 是 Composite，于是 instantiateType(t1, mapper2) 走一趟 instantiateTypeWorker。
        var composite = checker.MakeCompositeTypeMapper(TypeMapKind.Composite, firstMapper, secondMapper);
        Assert.Same(number, checker.GetMappedType(first, composite));

        // Merged：同一条链，但直接把 t1 交给第二个 mapper 的一次 getMappedType，不实例化。
        var merged = checker.MakeCompositeTypeMapper(TypeMapKind.Merged, firstMapper, secondMapper);
        Assert.Same(number, checker.GetMappedType(first, merged));

        // 第一个 mapper 不命中时直接进第二个。
        Assert.Same(number, checker.GetMappedType(second, composite));
    }

    // ---------------------------------------------------------------------------------------------
    // instantiateType 的两道守卫（checker.ts:21020-21038）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void InstantiateType_WithoutAMapper_ReturnsTheTypeItself()
    {
        var checker = NewChecker();
        var type = NewTypeParameter();

        Assert.Same(type, checker.InstantiateType(type, null));
        Assert.Null(checker.InstantiateType(null, null));
    }

    [Fact]
    public void InstantiateType_DepthLimit_YieldsTheErrorType()
    {
        var checker = NewChecker();
        var errorType = NewIntrinsicType("error", TypeFlags.Any);
        checker._errorType = errorType;
        checker._instantiationDepth = 100;

        var type = NewTypeParameter();
        var mapper = checker.MakeUnaryTypeMapper(type, NewIntrinsicType("string", TypeFlags.String));

        Assert.Same(errorType, checker.InstantiateType(type, mapper));
    }

    [Fact]
    public void InstantiateType_CountLimit_YieldsTheErrorType()
    {
        var checker = NewChecker();
        var errorType = NewIntrinsicType("error", TypeFlags.Any);
        checker._errorType = errorType;
        checker._instantiationCount = 5_000_000;

        var type = NewTypeParameter();
        var mapper = checker.MakeUnaryTypeMapper(type, NewIntrinsicType("string", TypeFlags.String));

        Assert.Same(errorType, checker.InstantiateType(type, mapper));
    }

    [Fact]
    public void InstantiateType_CachesPerActiveMapperAndClearsOnPop()
    {
        var checker = NewChecker();
        var source = NewTypeParameter();
        var target = NewIntrinsicType("string", TypeFlags.String);
        var mapper = checker.MakeUnaryTypeMapper(source, target);

        // 第一次调用把 mapper 压栈、算出结果后弹栈并清空该层的缓存（checker.ts:21047-21053）。
        Assert.Same(target, checker.InstantiateType(source, mapper));
        Assert.Equal(0, checker._activeTypeMappersCount);
        Assert.Equal(1, checker._totalInstantiationCount);

        // 顶层调用每次都是未命中：mapper 不再活跃，所以缓存那一层根本查不到（这正是 TS 的形状）。
        Assert.Same(target, checker.InstantiateType(source, mapper));
        Assert.Equal(2, checker._totalInstantiationCount);
        Assert.Equal(0, checker._instantiationDepth);
    }

    // ---------------------------------------------------------------------------------------------
    // instantiateList 的写时复制（checker.ts:20540-20556）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void InstantiateList_ReturnsTheInputListWhenNothingMoves()
    {
        var checker = NewChecker();
        var first = NewTypeParameter();
        var second = NewTypeParameter();
        var list = new List<IType> { first, second };
        var mapper = checker.MakeUnaryTypeMapper(first, second);

        var unchanged = checker.InstantiateList(list, mapper, (item, _) => item);
        Assert.Same(list, unchanged);

        var mapped = checker.InstantiateList(list, mapper, (item, m) => checker.GetMappedType(item, m));
        Assert.NotSame(list, mapped);
        Assert.Equal<IType>([second, second], mapped!);
        // 原列表没有被就地改写（TS 的 result = i === 0 ? [] : items.slice(0, i)）。
        Assert.Equal<IType>([first, second], list);
    }

    // ---------------------------------------------------------------------------------------------
    // getModifiedReadonlyState（checker.ts:20918-20920）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetModifiedReadonlyState_FollowsTheMappedTypeModifiers()
    {
        Assert.True(Checker.GetModifiedReadonlyState(false, MappedTypeModifiers.IncludeReadonly));
        Assert.False(Checker.GetModifiedReadonlyState(true, MappedTypeModifiers.ExcludeReadonly));
        Assert.True(Checker.GetModifiedReadonlyState(true, (MappedTypeModifiers)0));
        Assert.False(Checker.GetModifiedReadonlyState(false, (MappedTypeModifiers)0));
        // IncludeOptional 与 readonly 无关，原状态原样返回。
        Assert.True(Checker.GetModifiedReadonlyState(true, MappedTypeModifiers.IncludeOptional));
    }

    // ---------------------------------------------------------------------------------------------
    // getObjectFlags：TS 的 ObjectFlagsType 联合（types.ts:6596）在端口里的四个面
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetObjectFlags_ReadsTheFourFacesThatDeclareTheSlot()
    {
        var intrinsic = new IntrinsicType(null!, TypeFlags.Null) { ObjectFlags = ObjectFlags.NonInferrableType };
        Assert.Equal(ObjectFlags.NonInferrableType, Checker.GetObjectFlags(intrinsic));

        // types.ts:6976 has `SubstitutionType` declare the `objectFlags` slot too, but utilities.ts:8281 guards the
        // read with `type.flags & TypeFlags.ObjectFlagsType`, and `TypeFlags.Substitution` (1 << 24, types.ts:6355) is
        // not in that mask (types.ts:6402 = Any | Nullable | Never | Object | Union | Intersection) — while
        // `getOrCreateSubstitutionType` builds the type as exactly `createType(TypeFlags.Substitution)`
        // (checker.ts:17246). So the slot is unreachable for a substitution type and the answer is 0, exactly as the
        // reference's single structural cast answers (checked against lib/typescript.js:
        // `TypeFlags.Substitution & TypeFlags.ObjectFlagsType` is 0).
        var substitution = new SubstitutionType(null!, TypeFlags.Substitution) { ObjectFlags = ObjectFlags.Instantiated };
        Assert.Equal(ObjectFlags.None, Checker.GetObjectFlags(substitution));

        var union = new UnionType(null!, TypeFlags.Union) { ObjectFlags = ObjectFlags.ContainsWideningType };
        Assert.Equal(ObjectFlags.ContainsWideningType, Checker.GetObjectFlags(union));

        var unionOrIntersection = (IUnionOrIntersectionType)union;
        Assert.Equal(ObjectFlags.ContainsWideningType, Checker.GetObjectFlags(unionOrIntersection));

        // 没有该槽位的类型回答 0，等价于 TS 的 `undefined` 在位测试里的行为。
        Assert.Equal(ObjectFlags.None, Checker.GetObjectFlags(new Type(null!, TypeFlags.Never)));
    }

    // ---------------------------------------------------------------------------------------------
    // IterationTypesResolver：键、两个实例的惰性创建（checker.ts:1205-1219, 2200-2216）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void IterationTypesResolvers_AreCreatedOnceAndCarryTheReferenceKeys()
    {
        var checker = NewChecker();

        var sync = checker.SyncIterationTypesResolver;
        Assert.Same(sync, checker.SyncIterationTypesResolver);
        Assert.Equal("iterationTypesOfIterable", sync.IterableCacheKey);
        Assert.Equal("iterationTypesOfIterator", sync.IteratorCacheKey);
        Assert.Equal("iterator", sync.IteratorSymbolName);
        Assert.Same(Diagnostics.An_iterator_must_have_a_next_method, sync.MustHaveANextMethodDiagnostic);
        Assert.Same(Diagnostics.The_0_property_of_an_iterator_must_be_a_method, sync.MustBeAMethodDiagnostic);
        Assert.Same(Diagnostics.The_type_returned_by_the_0_method_of_an_iterator_must_have_a_value_property, sync.MustHaveAValueDiagnostic);

        var asyncResolver = checker.AsyncIterationTypesResolver;
        Assert.Same(asyncResolver, checker.AsyncIterationTypesResolver);
        Assert.NotSame(sync, asyncResolver);
        Assert.Equal("iterationTypesOfAsyncIterable", asyncResolver.IterableCacheKey);
        Assert.Equal("iterationTypesOfAsyncIterator", asyncResolver.IteratorCacheKey);
        Assert.Equal("asyncIterator", asyncResolver.IteratorSymbolName);
        Assert.Same(Diagnostics.An_async_iterator_must_have_a_next_method, asyncResolver.MustHaveANextMethodDiagnostic);
        Assert.Same(Diagnostics.The_0_property_of_an_async_iterator_must_be_a_method, asyncResolver.MustBeAMethodDiagnostic);
        Assert.Same(
            Diagnostics.The_type_returned_by_the_0_method_of_an_async_iterator_must_be_a_promise_for_a_type_with_a_value_property,
            asyncResolver.MustHaveAValueDiagnostic);
    }

    // ---------------------------------------------------------------------------------------------
    // 迭代缓存的键到槽位映射（checker.ts:45899-45905 的端口等价改写）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void IterationTypesCache_KeysMapToDistinctSlots()
    {
        var type = new UnionType(null!, TypeFlags.Object);
        var iterationTypes = new IterationTypes(
            NewIntrinsicType("string", TypeFlags.String),
            NewIntrinsicType("number", TypeFlags.Number),
            NewIntrinsicType("any", TypeFlags.Any));

        Checker.SetCachedIterationTypes(type, "iterationTypesOfIterable", iterationTypes);
        Assert.Same(iterationTypes, Checker.GetCachedIterationTypes(type, "iterationTypesOfIterable"));

        // 四个键各自独立：写一个不会出现在另一个槽里。
        Assert.Null(Checker.GetCachedIterationTypes(type, "iterationTypesOfAsyncIterable"));
        Assert.Null(Checker.GetCachedIterationTypes(type, "iterationTypesOfIterator"));
        Assert.Null(Checker.GetCachedIterationTypes(type, "iterationTypesOfAsyncIterator"));

        var asyncIterationTypes = new IterationTypes(
            NewIntrinsicType("string", TypeFlags.String),
            NewIntrinsicType("number", TypeFlags.Number),
            NewIntrinsicType("unknown", TypeFlags.Unknown));
        Checker.SetCachedIterationTypes(type, "iterationTypesOfAsyncIterator", asyncIterationTypes);
        Assert.Same(asyncIterationTypes, Checker.GetCachedIterationTypes(type, "iterationTypesOfAsyncIterator"));
        Assert.Same(iterationTypes, Checker.GetCachedIterationTypes(type, "iterationTypesOfIterable"));
    }

    [Fact]
    public void IterationTypesCache_UnknownKeyIsALoudFailure()
    {
        var type = new UnionType(null!, TypeFlags.Object);

        // PORT-NOTE 指出的那处改写只对四个已知键有定义；其余键按端口惯例 debug.fail（TS 会读到 undefined）。
        Assert.Throws<InvalidOperationException>(() => Checker.GetCachedIterationTypes(type, "iterationTypesOfSomethingElse"));
        Assert.Throws<InvalidOperationException>(() => Checker.SetCachedIterationTypes(
            type,
            "iterationTypesOfSomethingElse",
            new IterationTypes(null, null, null)));
    }

    // ---------------------------------------------------------------------------------------------
    // 受限实例化：原始类型原样返回（checker.ts:21157-21172）
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetRestrictiveInstantiation_PassesPrimitivesThroughWithoutInstantiating()
    {
        var checker = NewChecker();
        var primitive = NewIntrinsicType("string", TypeFlags.String);

        Assert.Same(primitive, checker.GetRestrictiveInstantiation(primitive));
        Assert.Same(primitive, checker.GetPermissiveInstantiation(primitive));
        Assert.Equal(0, checker._totalInstantiationCount);
    }
}
