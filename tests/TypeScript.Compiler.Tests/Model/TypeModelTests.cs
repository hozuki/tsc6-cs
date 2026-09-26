namespace TypeScript.Compiler.Tests;

/// <summary>
/// M6-0 的类型系统模型：<c>model_gaps[0]</c> 点名的每个类、族的继承形态，以及 <c>Type</c>/<c>Signature</c> 的槽位往返。
/// </summary>
/// <remarks>
/// 这些类是 checker 的唯一硬前置（<c>createType</c> 第一行就是 <c>new Type(checker, flags)</c>）。测的是它们自己的
/// 语义：类是否存在、工厂按 <c>TypeFlags</c> 造出的那个最派生类能不能被 cast 到 checker 会 cast 的每一个面、槽位
/// 是否原样往返、<see cref="SymbolTrackerImpl"/> 的转发与记录行为。接口是否被实现由编译器保证（M6-0 报告里有一个只
/// 编译 <c>src/TypeScript.Compiler</c> 全部源码的 scratch 工程，0 警告 0 错误），这里不再用反射把它重复一遍。
/// </remarks>
public class TypeModelTests
{
    // ---------------------------------------------------------------------------------------------
    // model_gaps[0] 的类名清单
    // ---------------------------------------------------------------------------------------------

    /// <summary>每个类名与它必须实现的主接口（<c>null</c> 表示 TS 里没有对应接口的类）。</summary>
    /// <remarks>
    /// scout 清单里的 <c>IterableOrIteratorType</c>（以及它没列的 <c>PromiseOrAwaitableType</c>、<c>SyntheticDefaultModuleType</c>、
    /// <c>NullableType</c>、<c>FreshableType</c>、<c>FreshableIntrinsicType</c>、<c>DeferredTypeReference</c>、
    /// <c>TupleTypeReference</c>）没有同名类：它们都是"同一个运行时对象的另一个面"（TS 的 <c>as</c> 从不失败），
    /// 在移植版里由链上已有的类实现那个接口即可 —— 造一个永远不会被实例化的类不符合"不留死重量"。每个面都必须在
    /// 后面几条测试里可达，这才是清单真正要守的东西。
    /// </remarks>
    private static readonly (string Class, string? Face)[] s_modelClasses =
    [
        ("Type", "IType"),
        ("Signature", "ISignature"),
        ("IntrinsicType", "IIntrinsicType"),
        ("BooleanLiteralType", null),
        ("LiteralType", "ILiteralType"),
        ("StringLiteralType", "IStringLiteralType"),
        ("NumberLiteralType", "INumberLiteralType"),
        ("BigIntLiteralType", "IBigIntLiteralType"),
        ("UniqueESSymbolType", "IUniqueESSymbolType"),
        ("EnumType", "IEnumType"),
        ("UnionOrIntersectionType", "IUnionOrIntersectionType"),
        ("UnionType", "IUnionType"),
        ("IntersectionType", "IIntersectionType"),
        ("ObjectType", "IObjectType"),
        ("ResolvedType", "IResolvedType"),
        ("TypeReference", "ITypeReference"),
        ("InterfaceType", "IInterfaceType"),
        ("InterfaceTypeWithDeclaredMembers", "IInterfaceTypeWithDeclaredMembers"),
        ("GenericType", "IGenericType"),
        ("TupleType", "ITupleType"),
        ("AnonymousType", "IAnonymousType"),
        ("MappedType", "IMappedType"),
        ("FreshObjectLiteralType", "IFreshObjectLiteralType"),
        ("EvolvingArrayType", "IEvolvingArrayType"),
        ("ReverseMappedType", "IReverseMappedType"),
        ("InstantiationExpressionType", "IInstantiationExpressionType"),
        ("TypeParameter", "ITypeParameter"),
        ("IndexType", "IIndexType"),
        ("IndexedAccessType", "IIndexedAccessType"),
        ("ConditionalType", "IConditionalType"),
        ("ConditionalRoot", "IConditionalRoot"),
        ("TemplateLiteralType", "ITemplateLiteralType"),
        ("StringMappingType", "IStringMappingType"),
        ("SubstitutionType", "ISubstitutionType"),
        ("IndexInfo", "IIndexInfo"),
        ("IterationTypes", "IIterationTypes"),
        ("TypePredicate", "ITypePredicateBase"),
        ("TransientSymbol", "ITransientSymbol"),
        ("SymbolLinks", "ISymbolLinks"),
        ("NodeLinks", "INodeLinks"),
        ("NodeBuilderContext", "ISyntacticTypeNodeBuilderContext"),
        ("SymbolTrackerImpl", "ISymbolTracker"),
    ];

    [Fact]
    public void EveryClassTheScoutListed_ExistsAsAConcreteClassImplementingItsFace()
    {
        var assembly = typeof(Type).Assembly;
        var broken = new List<string>();

        foreach (var (name, face) in s_modelClasses)
        {
            var type = assembly.GetType($"TypeScript.Compiler.{name}");
            if (type is null || !type.IsClass || type.IsAbstract)
            {
                broken.Add($"{name}: no concrete class");
                continue;
            }

            if (face is null)
            {
                continue;
            }

            var faceType = assembly.GetType($"TypeScript.Compiler.{face}");
            if (faceType is null || !faceType.IsInterface || !faceType.IsAssignableFrom(type))
            {
                broken.Add($"{name}: does not implement {face}");
            }
        }

        Assert.Equal("", string.Join(", ", broken));
    }

    /// <summary>
    /// TS 的 <c>TypePredicate</c> 是四个 variant 接口的 union（types.ts:5723），checker 按 <c>kind</c> 收窄后读
    /// <c>parameterName</c>/<c>parameterIndex</c>；一个类实现全部四个面，那些读取才不会是抛异常的转换。
    /// </summary>
    [Fact]
    public void TypePredicate_ImplementsAllFourVariantFaces()
    {
        AssertEveryFace(typeof(TypePredicate), [
            "ITypePredicateBase", "IIdentifierTypePredicate", "IAssertsIdentifierTypePredicate",
            "IThisTypePredicate", "IAssertsThisTypePredicate",
        ]);

        var identifier = new TypePredicate(TypePredicateKind.Identifier, "x", 1, null);
        Assert.Equal(TypePredicateKind.Identifier, identifier.Kind);
        Assert.Equal("x", ((IIdentifierTypePredicate)identifier).ParameterName);
        Assert.Equal(1, ((IIdentifierTypePredicate)identifier).ParameterIndex);

        // This 面的两个槽在 TS 里就是 `undefined`，移植版存 null。
        var thisPredicate = new TypePredicate(TypePredicateKind.This, null, null, null);
        Assert.Null(((IThisTypePredicate)thisPredicate).ParameterName);
        Assert.Null(((IThisTypePredicate)thisPredicate).ParameterIndex);

        // 两个面共用一个物理槽：拿 kind 说不通的那个面去读会抛，而不是给出另一个面的值。
        Assert.Throws<InvalidCastException>(() => ((IThisTypePredicate)identifier).ParameterName);
    }

    /// <summary>
    /// TS 在 release-6.0 的 <c>types.ts</c> 里没有 <c>BooleanLiteralType</c> 声明：boolean 字面量类型是
    /// <c>createIntrinsicType</c> 造出来的 intrinsic（checker.ts:2097-2100），枚举字面量类型则是带
    /// <c>TypeFlags.EnumLiteral</c> 的 <c>StringLiteralType</c>/<c>NumberLiteralType</c>（checker.ts:20324）。移植版
    /// 给前者一个名字（<c>TypeFlags → class</c> 分派需要），这条测试钉住它只是个名字。
    /// </summary>
    [Fact]
    public void BooleanLiteralType_AddsNothingToTheIntrinsicFace()
    {
        Assert.Equal(typeof(IntrinsicType), typeof(BooleanLiteralType).BaseType);
        Assert.Empty(typeof(BooleanLiteralType).GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
    }

    // ---------------------------------------------------------------------------------------------
    // 族的形态：工厂造出的最派生类必须能被 cast 到 checker 的每一个面
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// TS 里 <c>createObjectType</c>/<c>createAnonymousType</c> 造的是同一个运行时对象，checker 用 <c>as</c> 把它看
    /// 成 <c>ObjectType</c>、<c>ResolvedType</c>、<c>TypeReference</c>、<c>GenericType</c>、<c>MappedType</c>…… 移植版把
    /// 这些面做成一条继承链，链尾（<see cref="InstantiationExpressionType"/>）就是工厂实例化的那个类，所以每个
    /// <c>as</c> 都是成功的上下行转换。链一旦被重排，编译仍然通过，只会在 checker 里抛 —— 这条测试就是钉住它。
    /// </summary>
    [Fact]
    public void TheObjectFamilyLeaf_IsCastableToEveryFaceTheCheckerCastsTo()
    {
        AssertEveryFace(typeof(InstantiationExpressionType), [
            "IObjectType", "IResolvedType", "ITypeReference", "IDeferredTypeReference", "ITupleTypeReference",
            "IInterfaceType", "IInterfaceTypeWithDeclaredMembers", "IGenericType", "ITupleType", "IAnonymousType",
            "IMappedType", "IFreshObjectLiteralType", "IEvolvingArrayType", "IReverseMappedType",
            "IInstantiationExpressionType", "IIterableOrIteratorType", "IPromiseOrAwaitableType",
            "ISyntheticDefaultModuleType",
        ]);
    }

    /// <summary>
    /// 联合/交叉类型同样要从对象面看过去（<c>IterableOrIteratorType</c> 与 <c>PromiseOrAwaitableType</c> 都是
    /// <c>extends ObjectType, UnionType</c>），而 TS 的 <c>StructuredType = ObjectType | UnionType | IntersectionType</c>
    /// 在移植版里由 <see cref="UnionOrIntersectionType"/> 一个基类承担 —— 于是
    /// <c>resolveStructuredTypeMembers(type as StructuredType)</c> 这类调用点可以收这个类型。
    /// </summary>
    [Fact]
    public void TheStructuredLeafs_AreCastableToTheObjectUnionAndCacheFaces()
    {
        string[] faces =
        [
            "IUnionOrIntersectionType", "IUnionType", "IObjectType", "IIterableOrIteratorType",
            "IPromiseOrAwaitableType", "ISyntheticDefaultModuleType",
        ];

        AssertEveryFace(typeof(UnionType), faces);
        AssertEveryFace(typeof(IntersectionType), faces);

        Assert.True(typeof(UnionOrIntersectionType).IsAssignableFrom(typeof(InstantiationExpressionType)));
        Assert.True(typeof(UnionOrIntersectionType).IsAssignableFrom(typeof(UnionType)));
        Assert.True(typeof(UnionOrIntersectionType).IsAssignableFrom(typeof(IntersectionType)));
    }

    /// <summary>
    /// 字面量与可实例化族：TS 一个函数造一个对象、checker 按位看不同面，各面都得从那个类可达。
    /// </summary>
    [Fact]
    public void TheLiteralAndInstantiableLeaves_AreCastableToTheirFaces()
    {
        AssertEveryFace(typeof(StringLiteralType), ["ILiteralType", "IStringLiteralType", "INumberLiteralType", "IBigIntLiteralType", "IFreshableType"]);
        AssertEveryFace(typeof(NumberLiteralType), ["ILiteralType", "INumberLiteralType"]);
        AssertEveryFace(typeof(BigIntLiteralType), ["ILiteralType", "IBigIntLiteralType"]);
        AssertEveryFace(typeof(IntrinsicType), ["IIntrinsicType", "INullableType", "IFreshableIntrinsicType", "IFreshableType"]);
        AssertEveryFace(typeof(EnumType), ["IEnumType", "IFreshableType"]);
        AssertEveryFace(typeof(TypeParameter), ["IInstantiableType", "ITypeParameter"]);
        AssertEveryFace(typeof(IndexType), ["IInstantiableType", "IIndexType"]);
        AssertEveryFace(typeof(IndexedAccessType), ["IInstantiableType", "IIndexedAccessType"]);
        AssertEveryFace(typeof(ConditionalType), ["IInstantiableType", "IConditionalType"]);
        AssertEveryFace(typeof(SubstitutionType), ["IInstantiableType", "ISubstitutionType"]);
        AssertEveryFace(typeof(TemplateLiteralType), ["IInstantiableType", "ITemplateLiteralType"]);
        AssertEveryFace(typeof(StringMappingType), ["IInstantiableType", "IStringMappingType"]);
        AssertEveryFace(typeof(TypeParameter), ["IInstantiableType"]);
        AssertEveryFace(typeof(UniqueESSymbolType), ["IUniqueESSymbolType"]);
    }

    // ---------------------------------------------------------------------------------------------
    // Type / Signature 的槽位往返
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Type_RoundTripsEverySlot()
    {
        var symbol = new Symbol(SymbolFlags.None, "S");
        var alias = new Symbol(SymbolFlags.None, "A");
        var argument = new Type(null!, TypeFlags.String);
        var type = new Type(null!, (TypeFlags)0)
        {
            Id = 7,
            Symbol = symbol,
            AliasSymbol = alias,
            AliasTypeArguments = [argument],
            PermissiveInstantiation = argument,
            RestrictiveInstantiation = argument,
            ImmediateBaseConstraint = argument,
            Widened = argument,
        };

        // `checker` 只在 Debug.isDebugging 或 tracing 打开时被填（utilities.ts:8492）。
        Assert.Equal((TypeFlags)0, type.Flags);
        Assert.Equal(7, type.Id);
        Assert.Null(type.Checker);
        Assert.Same(symbol, type.Symbol);
        Assert.Null(type.Pattern);
        Assert.Same(alias, type.AliasSymbol);
        Assert.Same(argument, Assert.Single(type.AliasTypeArguments!));
        Assert.Same(argument, type.PermissiveInstantiation);
        Assert.Same(argument, type.RestrictiveInstantiation);
        Assert.Same(argument, type.ImmediateBaseConstraint);
        Assert.Same(argument, type.Widened);
    }

    [Fact]
    public void Signature_RoundTripsEverySlot()
    {
        var symbol = new Symbol(SymbolFlags.None, "S");
        var other = new Signature(null!, SignatureFlags.None);
        var parameter = new TypeParameter(null!, TypeFlags.TypeParameter);
        var returnType = new Type(null!, TypeFlags.String);
        var predicate = new TypePredicate(TypePredicateKind.Identifier, "x", 0, returnType);
        var mapper = new object();
        var optionalCallCache = new object();
        var isolated = new ObjectType(null!, TypeFlags.Object);
        var instantiations = new Dictionary<string, ISignature> { ["0"] = other };

        var signature = new Signature(null!, SignatureFlags.HasRestParameter)
        {
            Declaration = null,
            TypeParameters = [parameter],
            Parameters = [symbol],
            ThisParameter = symbol,
            ResolvedReturnType = returnType,
            ResolvedTypePredicate = predicate,
            MinArgumentCount = 2,
            ResolvedMinArgumentCount = 1,
            Target = other,
            Mapper = mapper,
            CompositeSignatures = [other],
            CompositeKind = TypeFlags.Union,
            ErasedSignatureCache = other,
            CanonicalSignatureCache = other,
            BaseSignatureCache = other,
            OptionalCallSignatureCache = optionalCallCache,
            IsolatedSignatureType = isolated,
            Instantiations = instantiations,
        };

        Assert.Equal(SignatureFlags.HasRestParameter, signature.Flags);
        Assert.Null(signature.Checker);
        Assert.Null(signature.Declaration);
        Assert.Same(parameter, Assert.Single(signature.TypeParameters!));
        Assert.Same(symbol, Assert.Single(signature.Parameters!));
        Assert.Same(symbol, signature.ThisParameter);
        Assert.Same(returnType, signature.ResolvedReturnType);
        Assert.Same(predicate, signature.ResolvedTypePredicate);
        Assert.Equal(2, signature.MinArgumentCount);
        Assert.Equal(1, signature.ResolvedMinArgumentCount);
        Assert.Same(other, signature.Target);
        Assert.Same(mapper, signature.Mapper);
        Assert.Same(other, Assert.Single(signature.CompositeSignatures!));
        Assert.Equal(TypeFlags.Union, signature.CompositeKind);
        Assert.Same(other, signature.ErasedSignatureCache);
        Assert.Same(other, signature.CanonicalSignatureCache);
        Assert.Same(other, signature.BaseSignatureCache);
        Assert.Same(optionalCallCache, signature.OptionalCallSignatureCache);
        Assert.Same(isolated, signature.IsolatedSignatureType);
        Assert.Same(instantiations, signature.Instantiations);
    }

    /// <summary>
    /// 一条 <c>as</c> 的两种方向都在这里：TS 的 <c>createAnonymousType(…) as ObjectType as GenericType</c>
    /// （checker.ts:2154）是上行，<c>(type as MappedType)</c> 是下行，链上都必须成立。
    /// </summary>
    [Fact]
    public void OneObjectTypeInstance_CanBeViewedThroughEveryFace()
    {
        var type = new InstantiationExpressionType(null!, TypeFlags.Object) { ObjectFlags = ObjectFlags.Reference };
        type.Target = type;
        type.ResolvedTypeArguments = [];
        ((IGenericType)type).Instantiations = new Dictionary<string, ITypeReference> { ["1"] = type };
        ((IAnonymousType)type).Target = type;

        Assert.Same(type, ((IGenericType)type).Target);
        Assert.Same(type, ((IAnonymousType)type).Target);
        Assert.Same(type, ((IGenericType)type).Instantiations!["1"]);
        Assert.Equal(ObjectFlags.Reference, ((IObjectType)type).ObjectFlags);
    }

    /// <summary>
    /// <c>instantiations</c> 在 TS 里是一个槽、两个含义（<c>GenericType</c> 的 <c>Map&lt;string, TypeReference&gt;</c> 与
    /// <c>AnonymousType</c> 的 <c>Map&lt;string, Type&gt;</c>，types.ts:6685 与 :6770）。移植版共用一个物理槽、两面各自
    /// reinterpret，所以跨面读会抛而不是悄悄给出另一个缓存里的条目 —— 这条测试钉住"响亮失败"这个选择。
    /// </summary>
    [Fact]
    public void Instantiations_SharedSlotIsReinterpretedPerFace_AndMixingThrows()
    {
        var anonymous = new InstantiationExpressionType(null!, TypeFlags.Object);
        ((IAnonymousType)anonymous).Instantiations = new Dictionary<string, IType> { ["1"] = anonymous };
        Assert.Same(anonymous, ((IAnonymousType)anonymous).Instantiations!["1"]);
        Assert.Throws<InvalidCastException>(() => ((IGenericType)anonymous).Instantiations);

        var generic = new InstantiationExpressionType(null!, TypeFlags.Object);
        ((IGenericType)generic).Instantiations = new Dictionary<string, ITypeReference> { ["1"] = generic };
        Assert.Same(generic, ((IGenericType)generic).Instantiations!["1"]);
        Assert.Throws<InvalidCastException>(() => ((IAnonymousType)generic).Instantiations);
    }

    // ---------------------------------------------------------------------------------------------
    // SymbolTrackerImpl
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SymbolTrackerImpl_ForwardsToThePresenter_AndRecordsTheDiagnostic()
    {
        var context = new NodeBuilderContext();
        var presenter = new RecordingTracker();
        var tracker = new SymbolTrackerImpl(context, presenter, moduleResolverHost: null);
        var symbol = new Symbol(SymbolFlags.Property, "p");

        Assert.True(tracker.CanTrackSymbol);
        Assert.False(tracker.TrackSymbol(symbol, null, SymbolFlags.Property));
        Assert.Equal(1, presenter.TrackSymbolCalls);
        Assert.Same(symbol, Assert.Single(context.TrackedSymbols!).Symbol);
        Assert.False(context.ReportedDiagnostic);

        presenter.TrackSymbolResult = true;
        Assert.True(tracker.TrackSymbol(symbol, null, SymbolFlags.Property));
        Assert.True(context.ReportedDiagnostic);

        tracker.ReportInaccessibleThisError();
        tracker.ReportPrivateInBaseOfClassExpression("p");
        tracker.ReportInaccessibleUniqueSymbolError();
        tracker.ReportCyclicStructureError();
        tracker.ReportLikelyUnsafeImportRequiredError("\"m\"", "s");
        tracker.ReportTruncationError();
        tracker.ReportNonSerializableProperty("p");
        Assert.Equal(7, presenter.ReportedCount);
    }

    /// <summary>
    /// 类型参数的符号不进 <c>trackedSymbols</c>（checker.ts:54356 的注释：它们不参与 late painted statements），
    /// 而 <c>disableTrackSymbol</c> 关掉的只是转发那一半。
    /// </summary>
    [Fact]
    public void SymbolTrackerImpl_SkipsTypeParameters_AndHonoursDisableTrackSymbol()
    {
        var context = new NodeBuilderContext();
        var tracker = new SymbolTrackerImpl(context, new RecordingTracker(), moduleResolverHost: null);

        tracker.TrackSymbol(new Symbol(SymbolFlags.TypeParameter, "T"), null, SymbolFlags.TypeParameter);
        Assert.Null(context.TrackedSymbols);

        tracker.DisableTrackSymbol = true;
        tracker.TrackSymbol(new Symbol(SymbolFlags.Property, "p"), null, SymbolFlags.Property);
        Assert.Null(context.TrackedSymbols);
        Assert.True(tracker.CanTrackSymbol);
    }

    /// <summary>
    /// 嵌套的 tracker 在构造时被拆平（checker.ts:54338-54340），所以转发永远只走一层；以及
    /// <c>reportInferenceFallback</c> 受 <c>context.suppressReportInferenceFallback</c> 约束。
    /// </summary>
    [Fact]
    public void SymbolTrackerImpl_UnwrapsNestedImpls_AndHonoursSuppressReportInferenceFallback()
    {
        var innerPresenter = new RecordingTracker();
        var innerContext = new NodeBuilderContext();
        var inner = new SymbolTrackerImpl(innerContext, innerPresenter, moduleResolverHost: null);

        var outerContext = new NodeBuilderContext();
        var outer = new SymbolTrackerImpl(outerContext, inner, moduleResolverHost: null);

        // 构造时拆平：外层直接拿最内层的 presenter，中间那层实例不再参与转发（TS 的同名 while 循环）。
        Assert.Same(innerPresenter, outer.Inner);
        outer.ReportTruncationError();
        Assert.Equal(1, innerPresenter.ReportedCount);
        Assert.True(outerContext.ReportedDiagnostic);
        Assert.False(innerContext.ReportedDiagnostic);

        outerContext.SuppressReportInferenceFallback = true;
        outer.ReportInferenceFallback(null);
        Assert.Equal(1, innerPresenter.ReportedCount);

        outerContext.SuppressReportInferenceFallback = false;
        outer.ReportInferenceFallback(null);
        Assert.Equal(2, innerPresenter.ReportedCount);
    }

    [Fact]
    public void ACallerWithoutAPresenter_TracksNothingAndReportsNothing()
    {
        var context = new NodeBuilderContext();
        var tracker = new SymbolTrackerImpl(context, tracker: null, moduleResolverHost: null);

        Assert.False(tracker.CanTrackSymbol);
        Assert.False(tracker.TrackSymbol(new Symbol(SymbolFlags.Property, "p"), null, SymbolFlags.Property));
        tracker.ReportTruncationError();
        tracker.ReportInferenceFallback(null);
        Assert.Null(context.TrackedSymbols);
        Assert.False(context.ReportedDiagnostic);
    }

    /// <summary>
    /// <see cref="IIterationTypes"/> 的三个成员是只读的（types.ts:6823-6825），所以移植版用构造函数接收三个值 ——
    /// TS 的对象字面量 <c>{ yieldType, returnType, nextType }</c>（checker.ts:45859）就是这三个实参。
    /// </summary>
    [Fact]
    public void IterationTypes_TakesTheThreeTypesFromTheConstructor()
    {
        var yield = new Type(null!, TypeFlags.String);
        var @return = new Type(null!, TypeFlags.Number);
        var next = new Type(null!, TypeFlags.Any);
        var iterationTypes = new IterationTypes(yield, @return, next);

        Assert.Same(yield, iterationTypes.YieldType);
        Assert.Same(@return, iterationTypes.ReturnType);
        Assert.Same(next, iterationTypes.NextType);
    }

    private static void AssertEveryFace(System.Type type, string[] faces)
    {
        var assembly = type.Assembly;
        var missing = faces.Where(face => !assembly.GetType($"TypeScript.Compiler.{face}")!.IsAssignableFrom(type)).ToList();
        Assert.Equal("", string.Join(", ", missing));
    }

    /// <summary>记录调用的 <see cref="ISymbolTracker"/>：测的是 <see cref="SymbolTrackerImpl"/> 的转发，不是它本身。</summary>
    private sealed class RecordingTracker : ISymbolTracker
    {
        public bool TrackSymbolResult { get; set; }

        public int TrackSymbolCalls { get; private set; }

        public int ReportedCount { get; private set; }

        public IModuleSpecifierResolutionHost? ModuleResolverHost { get; set; }

        public bool TrackSymbol(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning)
        {
            TrackSymbolCalls++;
            return TrackSymbolResult;
        }

        public void ReportInaccessibleThisError() => ReportedCount++;

        public void ReportPrivateInBaseOfClassExpression(string? propertyName) => ReportedCount++;

        public void ReportInaccessibleUniqueSymbolError() => ReportedCount++;

        public void ReportCyclicStructureError() => ReportedCount++;

        public void ReportLikelyUnsafeImportRequiredError(string? specifier, string? symbolName) => ReportedCount++;

        public void ReportTruncationError() => ReportedCount++;

        public void ReportNonlocalAugmentation(SourceFile? containingFile, ISymbol? parentSymbol, ISymbol? augmentingSymbol) => ReportedCount++;

        public void ReportNonSerializableProperty(string? propertyName) => ReportedCount++;

        public void ReportInferenceFallback(Node? node) => ReportedCount++;

        public void PushErrorFallbackNode(IDeclaration? node)
        {
        }

        public void PopErrorFallbackNode()
        {
        }
    }
}
