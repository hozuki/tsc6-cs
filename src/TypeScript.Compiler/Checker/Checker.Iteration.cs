namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1205-1219 (IterationTypesResolver)
// ported from: src/compiler/checker.ts:2185 (iterationTypesCache)
// ported from: src/compiler/checker.ts:2200-2216 (asyncIterationTypesResolver, syncIterationTypesResolver)
// ported from: src/compiler/checker.ts:45899-45905 (getCachedIterationTypes, setCachedIterationTypes)
/// <summary>
/// The two iteration-protocol resolvers — TS's <c>IterationTypesResolver</c> interface (checker.ts:1205-1219) and its
/// two object literals (checker.ts:2200-2216).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares this as an interface of closures over the checker's state, built as two object literals
/// right after <c>noIterationTypes</c>/<c>anyIterationTypes</c>. The port states it as a class of delegates (the
/// <see cref="NodeBuilderContext"/>/<c>SymbolTrackerImpl</c> precedent, §15.9) and builds the two instances lazily,
/// because every arm of the literals is a member of <see cref="Checker"/> and some of them
/// (<c>getGlobalAsyncIteratorType</c> and friends) create types through the checker's caches.
/// </para>
/// <para>
/// PORT-NOTE: the reference's three string members are <c>"iterationTypesOfAsyncIterable" | "iterationTypesOfIterable"</c>,
/// <c>"iterationTypesOfAsyncIterator" | "iterationTypesOfIterator"</c> and <c>"asyncIterator" | "iterator"</c>, and
/// <c>getCachedIterationTypes</c>/<c>setCachedIterationTypes</c> (checker.ts:45899-45905) use the first two to index
/// the type object directly (<c>type[cacheKey]</c>). C# cannot index an object by a string, so the two accessors
/// below switch on the same strings over the four slots the generated
/// <see cref="IIterableOrIteratorType"/> declares — the same slots, in the same order, that the four keys select
/// upstream.
/// </para>
/// </remarks>
internal sealed class IterationTypesResolver
{
    /// <summary>ported from: src/compiler/checker.ts:1206 — <c>iterableCacheKey</c>.</summary>
    public required string IterableCacheKey { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1207 — <c>iteratorCacheKey</c>.</summary>
    public required string IteratorCacheKey { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1208 — <c>iteratorSymbolName</c>.</summary>
    public required string IteratorSymbolName { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1209 — <c>getGlobalIteratorType</c>.</summary>
    public required Func<bool, IGenericType> GetGlobalIteratorType { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1210 — <c>getGlobalIterableType</c>.</summary>
    public required Func<bool, IGenericType> GetGlobalIterableType { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1211 — <c>getGlobalIterableIteratorType</c>.</summary>
    public required Func<bool, IGenericType> GetGlobalIterableIteratorType { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1212 — <c>getGlobalIteratorObjectType</c>.</summary>
    public required Func<bool, IGenericType> GetGlobalIteratorObjectType { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1213 — <c>getGlobalGeneratorType</c>.</summary>
    public required Func<bool, IGenericType> GetGlobalGeneratorType { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1214 — <c>getGlobalBuiltinIteratorTypes</c>.</summary>
    public required Func<List<IGenericType>> GetGlobalBuiltinIteratorTypes { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1215 — <c>resolveIterationType</c>.</summary>
    public required Func<IType, Node?, IType?> ResolveIterationType { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1216 — <c>mustHaveANextMethodDiagnostic</c>.</summary>
    public required DiagnosticMessage MustHaveANextMethodDiagnostic { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1217 — <c>mustBeAMethodDiagnostic</c>.</summary>
    public required DiagnosticMessage MustBeAMethodDiagnostic { get; init; }

    /// <summary>ported from: src/compiler/checker.ts:1218 — <c>mustHaveAValueDiagnostic</c>.</summary>
    public required DiagnosticMessage MustHaveAValueDiagnostic { get; init; }
}

/// <summary>
/// The iteration-protocol machinery the two resolvers above are the key selectors for (checker.ts:2185, :2200-2216).
/// </summary>
/// <remarks>
/// PORT-NOTE: the reference's <c>createIterationTypes</c>/<c>noIterationTypes</c>/<c>anyIterationTypes</c>
/// (checker.ts:2186-2199, :45842-45868) and their <c>_iterationTypesCache</c> live in the same state block; the M6-2
/// type-constructor slice declares the first three, and this file declares the cache itself (the two resolvers are
/// its only key producers).
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:2185 — <c>iterationTypesCache</c>.</summary>
    private readonly Dictionary<string, IterationTypes> _iterationTypesCache = [];

    /// <summary>ported from: src/compiler/checker.ts:2200-2215 — <c>asyncIterationTypesResolver</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds this literal eagerly in the state block; the port builds it on first use, because its
    /// arms are members of this class. Every arm is a plain method group, so the instance the checked state sees is
    /// always the same one.
    /// </remarks>
    private IterationTypesResolver? _asyncIterationTypesResolver;

    /// <summary>ported from: src/compiler/checker.ts:2205-2209 — <c>syncIterationTypesResolver</c>.</summary>
    private IterationTypesResolver? _syncIterationTypesResolver;

    /// <summary>ported from: src/compiler/checker.ts:2200-2215 — <c>asyncIterationTypesResolver</c>.</summary>
    internal IterationTypesResolver AsyncIterationTypesResolver => _asyncIterationTypesResolver ??= new IterationTypesResolver
    {
        IterableCacheKey = "iterationTypesOfAsyncIterable",
        IteratorCacheKey = "iterationTypesOfAsyncIterator",
        IteratorSymbolName = "asyncIterator",
        GetGlobalIteratorType = GetGlobalAsyncIteratorType,
        GetGlobalIterableType = GetGlobalAsyncIterableType,
        GetGlobalIterableIteratorType = GetGlobalAsyncIterableIteratorType,
        GetGlobalIteratorObjectType = GetGlobalAsyncIteratorObjectType,
        GetGlobalGeneratorType = GetGlobalAsyncGeneratorType,
        GetGlobalBuiltinIteratorTypes = GetGlobalBuiltinAsyncIteratorTypes,
        ResolveIterationType = (type, errorNode) => GetAwaitedType(type, errorNode, Diagnostics.Type_of_await_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member),
        MustHaveANextMethodDiagnostic = Diagnostics.An_async_iterator_must_have_a_next_method,
        MustBeAMethodDiagnostic = Diagnostics.The_0_property_of_an_async_iterator_must_be_a_method,
        MustHaveAValueDiagnostic = Diagnostics.The_type_returned_by_the_0_method_of_an_async_iterator_must_be_a_promise_for_a_type_with_a_value_property,
    };

    /// <summary>ported from: src/compiler/checker.ts:2205-2215 — <c>syncIterationTypesResolver</c>.</summary>
    internal IterationTypesResolver SyncIterationTypesResolver => _syncIterationTypesResolver ??= new IterationTypesResolver
    {
        IterableCacheKey = "iterationTypesOfIterable",
        IteratorCacheKey = "iterationTypesOfIterator",
        IteratorSymbolName = "iterator",
        GetGlobalIteratorType = GetGlobalIteratorType,
        GetGlobalIterableType = GetGlobalIterableType,
        GetGlobalIterableIteratorType = GetGlobalIterableIteratorType,
        GetGlobalIteratorObjectType = GetGlobalIteratorObjectType,
        GetGlobalGeneratorType = GetGlobalGeneratorType,
        GetGlobalBuiltinIteratorTypes = GetGlobalBuiltinIteratorTypes,
        ResolveIterationType = (type, _errorNode) => type,
        MustHaveANextMethodDiagnostic = Diagnostics.An_iterator_must_have_a_next_method,
        MustBeAMethodDiagnostic = Diagnostics.The_0_property_of_an_iterator_must_be_a_method,
        MustHaveAValueDiagnostic = Diagnostics.The_type_returned_by_the_0_method_of_an_iterator_must_have_a_value_property,
    };

    /// <summary>ported from: src/compiler/checker.ts:45899-45901 — <c>getCachedIterationTypes</c>.</summary>
    /// <remarks>
    /// <para>PORT-NOTE: TS indexes the type with the resolver's cache key; the port switches on the same key over the slots <see cref="IIterableOrIteratorType"/> declares.</para>
    /// <para>PORT-NOTE: the reference's <c>(type as IterableOrIteratorType)[cacheKey]</c> is a plain expando read on any
    /// type object, so a type that is not an instance of the port's <see cref="UnionOrIntersectionType"/> family (an
    /// intrinsic or a type parameter, say) simply has nothing cached. The port answers the same <c>null</c> "absent"
    /// through a pattern match instead of casting, because these two slots are caches only: every reader recomputes the
    /// iteration types when they are absent (checker.ts:45956, :46085).</para>
    /// </remarks>
    internal static IterationTypes? GetCachedIterationTypes(IType type, string cacheKey)
    {
        if (type is not IIterableOrIteratorType iterable) return null;
        return cacheKey switch
        {
            "iterationTypesOfIterable" => (IterationTypes?)iterable.IterationTypesOfIterable,
            "iterationTypesOfAsyncIterable" => (IterationTypes?)iterable.IterationTypesOfAsyncIterable,
            "iterationTypesOfIterator" => (IterationTypes?)iterable.IterationTypesOfIterator,
            "iterationTypesOfAsyncIterator" => (IterationTypes?)iterable.IterationTypesOfAsyncIterator,
            "iterationTypesOfGeneratorReturnType" => (IterationTypes?)iterable.IterationTypesOfGeneratorReturnType,
            "iterationTypesOfAsyncGeneratorReturnType" => (IterationTypes?)iterable.IterationTypesOfAsyncGeneratorReturnType,
            "iterationTypesOfIteratorResult" => (IterationTypes?)iterable.IterationTypesOfIteratorResult,
            _ => Debug.Fail<IterationTypes?>($"unknown iteration types cache key: {cacheKey}"),
        };
    }

    /// <summary>ported from: src/compiler/checker.ts:45903-45905 — <c>setCachedIterationTypes</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's write is a plain expando write on any type object; a type that is not an
    /// instance of the port's <see cref="UnionOrIntersectionType"/> family loses only that cache — its next reader
    /// recomputes the iteration types (see <see cref="GetCachedIterationTypes"/>) — so the write is left out rather than
    /// casting.</remarks>
    internal static void SetCachedIterationTypes(IType type, string cacheKey, IterationTypes cachedTypes)
    {
        if (type is not IIterableOrIteratorType iterable) return;
        switch (cacheKey)
        {
            case "iterationTypesOfIterable":
                iterable.IterationTypesOfIterable = cachedTypes;
                break;
            case "iterationTypesOfAsyncIterable":
                iterable.IterationTypesOfAsyncIterable = cachedTypes;
                break;
            case "iterationTypesOfIterator":
                iterable.IterationTypesOfIterator = cachedTypes;
                break;
            case "iterationTypesOfAsyncIterator":
                iterable.IterationTypesOfAsyncIterator = cachedTypes;
                break;
            case "iterationTypesOfGeneratorReturnType":
                iterable.IterationTypesOfGeneratorReturnType = cachedTypes;
                break;
            case "iterationTypesOfAsyncGeneratorReturnType":
                iterable.IterationTypesOfAsyncGeneratorReturnType = cachedTypes;
                break;
            case "iterationTypesOfIteratorResult":
                iterable.IterationTypesOfIteratorResult = cachedTypes;
                break;
            default:
                Debug.Fail($"unknown iteration types cache key: {cacheKey}");
                break;
        }
    }
}
