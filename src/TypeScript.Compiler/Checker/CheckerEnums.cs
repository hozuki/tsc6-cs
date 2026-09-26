namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:1157-1450 (the module-level enums of checker.ts)
//
// The enums checker.ts declares outside createTypeChecker: the three the module exports (TypeFacts, CheckMode,
// SignatureCheckMode) plus the ones internal to the checker.
//
// PORT-NOTE: TS calls them all `const enum`, i.e. inlined numbers with no runtime object. The port states them as
// ordinary C# enums with the values written out, exactly as it does for the types.ts flag enums (§4.1, §4.5).
//
// PORT-NOTE: the underlying type is `int`, not the `uint` the generated flag enums use. TS's numbers are
// JavaScript's, i.e. int32, and TypeFacts is built with `~` (`AndFactsMask = All & ~OrFactsMask`); the port therefore
// keeps signed 32-bit arithmetic so the expressions below are literal copies of the reference rather than
// recomputed values. The four `~` members are consequently written as `int` expressions with the enum type spelled
// out for each operand: C# defines `~` for integer types only (not for enum types), and an enum member initializer
// may hold an `int` constant but not a cast back to the enum type.
//
// PORT-NOTE: IterationTypesResolver (checker.ts:1205-1219) sits in the same block but is an interface of closures
// over the checker's state, not an enum, so it belongs to the slice that ports its users — the iteration-types
// machinery at checker.ts:2200-2216 and its consumers at :46021-46497. The same goes for the module-level
// `ambientModuleSymbolRegex`/`anon` (:1153-1155), `typeofNEFacts` (:1313-1322), `isNotOverloadAndNotAccessor`
// (:1410) and `intrinsicTypeKinds` (:1443-1450): they are data, not enums, and their only readers are the checker
// members that come with their slices.

// ported from: src/compiler/checker.ts:1157-1167 — const enum ReferenceHint
/// <summary>
/// What a name being resolved turned out to be, so the resolver can order its candidates.
/// </summary>
internal enum ReferenceHint
{
    /// <summary>No usable hint.</summary>
    Unspecified,
    /// <summary>A bare identifier.</summary>
    Identifier,
    /// <summary>A property access.</summary>
    Property,
    /// <summary>An export assignment.</summary>
    ExportAssignment,
    /// <summary>A JSX attribute or tag name.</summary>
    Jsx,
    /// <summary>The name of an async function.</summary>
    AsyncFunction,
    /// <summary><c>import x = require("…")</c>.</summary>
    ExportImportEquals,
    /// <summary>An element of an export specifier list.</summary>
    ExportSpecifier,
    /// <summary>A decorator expression.</summary>
    Decorator,
}

// ported from: src/compiler/checker.ts:1174-1197 — const enum IterationUse
/// <summary>
/// Which iteration protocols and input shapes are acceptable, for the iteration-type queries.
/// </summary>
/// <remarks>
/// PORT-NOTE: the members below the eight flags are aliases combining them (<c>Element</c>, <c>ForOf</c>, …); C#
/// allows the same aliasing inside one enum.
/// </remarks>
[Flags]
internal enum IterationUse
{
    AllowsSyncIterablesFlag = 1 << 0,
    AllowsAsyncIterablesFlag = 1 << 1,
    AllowsStringInputFlag = 1 << 2,
    ForOfFlag = 1 << 3,
    YieldStarFlag = 1 << 4,
    SpreadFlag = 1 << 5,
    DestructuringFlag = 1 << 6,
    PossiblyOutOfBounds = 1 << 7,

    // Spread, Destructuring, Array element assignment
    Element = AllowsSyncIterablesFlag,
    Spread = AllowsSyncIterablesFlag | SpreadFlag,
    Destructuring = AllowsSyncIterablesFlag | DestructuringFlag,

    ForOf = AllowsSyncIterablesFlag | AllowsStringInputFlag | ForOfFlag,
    ForAwaitOf = AllowsSyncIterablesFlag | AllowsAsyncIterablesFlag | AllowsStringInputFlag | ForOfFlag,

    YieldStar = AllowsSyncIterablesFlag | YieldStarFlag,
    AsyncYieldStar = AllowsSyncIterablesFlag | AllowsAsyncIterablesFlag | YieldStarFlag,

    GeneratorReturnType = AllowsSyncIterablesFlag,
    AsyncGeneratorReturnType = AllowsAsyncIterablesFlag,
}

// ported from: src/compiler/checker.ts:1199-1203 — const enum IterationTypeKind
/// <summary>Which of the three iteration types of an iterator is being asked for.</summary>
internal enum IterationTypeKind
{
    /// <summary>The yielded type.</summary>
    Yield,
    /// <summary>The returned type.</summary>
    Return,
    /// <summary>The type passed to <c>next</c>.</summary>
    Next,
}

// ported from: src/compiler/checker.ts:1221-1226 — const enum WideningKind
/// <summary>
/// The context a literal type is being widened in; the return and yield cases widen differently from a plain
/// expression.
/// </summary>
internal enum WideningKind
{
    /// <summary>An ordinary widening.</summary>
    Normal,
    /// <summary>A function's return type.</summary>
    FunctionReturn,
    /// <summary>The type passed to a generator's <c>next</c>.</summary>
    GeneratorNext,
    /// <summary>A generator's yielded type.</summary>
    GeneratorYield,
}

// ported from: src/compiler/checker.ts:1230-1311 — const enum TypeFacts
/// <summary>
/// The facts known about a type at a control-flow test: what a <c>typeof</c> comparison, an equality comparison or
/// a truthiness test can say about it.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS marks each member with the comparison it stands for; they are bit sets and the port keeps the
/// values verbatim, including the derived ones (<c>BaseStringFacts</c> … <c>ObjectFacts</c>). The last two masks use
/// <c>~</c>, which in C# needs the underlying type spelled out because <c>~</c> is not defined on an enum type.
/// </remarks>
[Flags]
internal enum TypeFacts
{
    None = 0,
    // typeof x === "…"
    TypeofEQString = 1 << 0,
    TypeofEQNumber = 1 << 1,
    TypeofEQBigInt = 1 << 2,
    TypeofEQBoolean = 1 << 3,
    TypeofEQSymbol = 1 << 4,
    TypeofEQObject = 1 << 5,
    TypeofEQFunction = 1 << 6,
    TypeofEQHostObject = 1 << 7,
    // typeof x !== "…"
    TypeofNEString = 1 << 8,
    TypeofNENumber = 1 << 9,
    TypeofNEBigInt = 1 << 10,
    TypeofNEBoolean = 1 << 11,
    TypeofNESymbol = 1 << 12,
    TypeofNEObject = 1 << 13,
    TypeofNEFunction = 1 << 14,
    TypeofNEHostObject = 1 << 15,
    // x ===/!== undefined / null
    EQUndefined = 1 << 16,
    EQNull = 1 << 17,
    EQUndefinedOrNull = 1 << 18,
    NEUndefined = 1 << 19,
    NENull = 1 << 20,
    NEUndefinedOrNull = 1 << 21,
    // x / !x
    Truthy = 1 << 22,
    Falsy = 1 << 23,
    // Contains undefined/null or intersection with them
    IsUndefined = 1 << 24,
    IsNull = 1 << 25,
    IsUndefinedOrNull = IsUndefined | IsNull,
    All = (1 << 27) - 1,

    // The following members encode facts about particular kinds of types for use in the getTypeFacts function.
    // The presence of a particular fact means that the given test is true for some (and possibly all) values
    // of that kind of type.
    BaseStringStrictFacts = TypeofEQString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseStringFacts = BaseStringStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    StringStrictFacts = BaseStringStrictFacts | Truthy | Falsy,
    StringFacts = BaseStringFacts | Truthy,
    EmptyStringStrictFacts = BaseStringStrictFacts | Falsy,
    EmptyStringFacts = BaseStringFacts,
    NonEmptyStringStrictFacts = BaseStringStrictFacts | Truthy,
    NonEmptyStringFacts = BaseStringFacts | Truthy,
    BaseNumberStrictFacts = TypeofEQNumber | TypeofNEString | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseNumberFacts = BaseNumberStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    NumberStrictFacts = BaseNumberStrictFacts | Truthy | Falsy,
    NumberFacts = BaseNumberFacts | Truthy,
    ZeroNumberStrictFacts = BaseNumberStrictFacts | Falsy,
    ZeroNumberFacts = BaseNumberFacts,
    NonZeroNumberStrictFacts = BaseNumberStrictFacts | Truthy,
    NonZeroNumberFacts = BaseNumberFacts | Truthy,
    BaseBigIntStrictFacts = TypeofEQBigInt | TypeofNEString | TypeofNENumber | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseBigIntFacts = BaseBigIntStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    BigIntStrictFacts = BaseBigIntStrictFacts | Truthy | Falsy,
    BigIntFacts = BaseBigIntFacts | Truthy,
    ZeroBigIntStrictFacts = BaseBigIntStrictFacts | Falsy,
    ZeroBigIntFacts = BaseBigIntFacts,
    NonZeroBigIntStrictFacts = BaseBigIntStrictFacts | Truthy,
    NonZeroBigIntFacts = BaseBigIntFacts | Truthy,
    BaseBooleanStrictFacts = TypeofEQBoolean | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseBooleanFacts = BaseBooleanStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    BooleanStrictFacts = BaseBooleanStrictFacts | Truthy | Falsy,
    BooleanFacts = BaseBooleanFacts | Truthy,
    FalseStrictFacts = BaseBooleanStrictFacts | Falsy,
    FalseFacts = BaseBooleanFacts,
    TrueStrictFacts = BaseBooleanStrictFacts | Truthy,
    TrueFacts = BaseBooleanFacts | Truthy,
    SymbolStrictFacts = TypeofEQSymbol | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    SymbolFacts = SymbolStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    ObjectStrictFacts = TypeofEQObject | TypeofEQHostObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEFunction | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    ObjectFacts = ObjectStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    FunctionStrictFacts = TypeofEQFunction | TypeofEQHostObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    FunctionFacts = FunctionStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    VoidFacts = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | EQUndefined | EQUndefinedOrNull | NENull | Falsy,
    UndefinedFacts = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | EQUndefined | EQUndefinedOrNull | NENull | Falsy | IsUndefined,
    NullFacts = TypeofEQObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEFunction | TypeofNEHostObject | EQNull | EQUndefinedOrNull | NEUndefined | Falsy | IsNull,
    EmptyObjectStrictFacts = (int)All & ~(int)(EQUndefined | EQNull | EQUndefinedOrNull | IsUndefinedOrNull),
    EmptyObjectFacts = (int)All & ~(int)IsUndefinedOrNull,
    UnknownFacts = (int)All & ~(int)IsUndefinedOrNull,
    AllTypeofNE = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | NEUndefined,

    // Masks
    OrFactsMask = TypeofEQFunction | TypeofNEObject,
    AndFactsMask = (int)All & ~(int)OrFactsMask,
}

// ported from: src/compiler/checker.ts:1326-1336 — const enum TypeSystemPropertyName
/// <summary>
/// Which cached property of a type is being resolved, for the resolution stack that detects cycles.
/// </summary>
internal enum TypeSystemPropertyName
{
    /// <summary>The type itself.</summary>
    Type,
    /// <summary>The resolved base constructor type.</summary>
    ResolvedBaseConstructorType,
    /// <summary>The declared type.</summary>
    DeclaredType,
    /// <summary>The resolved return type of a signature.</summary>
    ResolvedReturnType,
    /// <summary>The immediate base constraint.</summary>
    ImmediateBaseConstraint,
    /// <summary>The resolved type arguments.</summary>
    ResolvedTypeArguments,
    /// <summary>The resolved base types.</summary>
    ResolvedBaseTypes,
    /// <summary>The write type.</summary>
    WriteType,
    /// <summary>Whether a parameter initializer contains <c>undefined</c>.</summary>
    ParameterInitializerContainsUndefined,
}

// ported from: src/compiler/checker.ts:1340-1351 — const enum CheckMode
/// <summary>
/// What kind of check a call to <c>checkExpression</c> is performing, which decides caching and diagnostics.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS exports this from <c>checker.ts</c> (services read it through
/// <c>expressionToTypeNode</c>'s checker calls); the port keeps it in the checker's file set for the same reason
/// (M6-0's contract item 5).
/// </remarks>
[Flags]
internal enum CheckMode
{
    /// <summary>Normal type checking.</summary>
    Normal = 0,
    /// <summary>Explicitly assigned contextual type, therefore not cacheable.</summary>
    Contextual = 1 << 0,
    /// <summary>Inferential typing.</summary>
    Inferential = 1 << 1,
    /// <summary>Skip context sensitive function expressions.</summary>
    SkipContextSensitive = 1 << 2,
    /// <summary>Skip single signature generic functions.</summary>
    SkipGenericFunctions = 1 << 3,
    /// <summary>Call resolution for purposes of signature help.</summary>
    IsForSignatureHelp = 1 << 4,
    /// <summary>
    /// Checking a type that is going to be used to determine the type of a rest binding element, e.g. in
    /// <c>const { a, ...rest } = foo</c>; generic types must be preserved instead of substituted for constraints.
    /// </summary>
    RestBindingElement = 1 << 5,
    /// <summary>Called from <c>getTypeOfExpression</c>; diagnostics may be omitted.</summary>
    TypeOnly = 1 << 6,
}

// ported from: src/compiler/checker.ts:1354-1362 — const enum SignatureCheckMode
/// <summary>How two signatures are compared, for the assignability relation.</summary>
/// <remarks>
/// PORT-NOTE: exported upstream like <see cref="CheckMode"/>.
/// </remarks>
[Flags]
internal enum SignatureCheckMode
{
    None = 0,
    BivariantCallback = 1 << 0,
    StrictCallback = 1 << 1,
    IgnoreReturnTypes = 1 << 2,
    StrictArity = 1 << 3,
    StrictTopSignature = 1 << 4,
    Callback = BivariantCallback | StrictCallback,
}

// ported from: src/compiler/checker.ts:1364-1368 — const enum IntersectionState
/// <summary>Which side of a comparison is a constituent of an outer intersection.</summary>
[Flags]
internal enum IntersectionState
{
    None = 0,
    /// <summary>Source type is a constituent of an outer intersection.</summary>
    Source = 1 << 0,
    /// <summary>Target type is a constituent of an outer intersection.</summary>
    Target = 1 << 1,
}

// ported from: src/compiler/checker.ts:1370-1375 — const enum RecursionFlags
/// <summary>Which side of a recursive relation comparison is being re-entered.</summary>
[Flags]
internal enum RecursionFlags
{
    None = 0,
    Source = 1 << 0,
    Target = 1 << 1,
    Both = Source | Target,
}

// ported from: src/compiler/checker.ts:1377-1382 — const enum MappedTypeModifiers
/// <summary>The <c>+readonly</c>/<c>-readonly</c>/<c>+?</c>/<c>-?</c> modifiers of a mapped type declaration.</summary>
[Flags]
internal enum MappedTypeModifiers
{
    IncludeReadonly = 1 << 0,
    ExcludeReadonly = 1 << 1,
    IncludeOptional = 1 << 2,
    ExcludeOptional = 1 << 3,
}

// ported from: src/compiler/checker.ts:1384-1388 — const enum MappedTypeNameTypeKind
/// <summary>What the name type of a mapped type's members is doing — <c>as</c> filtering or remapping.</summary>
internal enum MappedTypeNameTypeKind
{
    /// <summary>The name type is not a conditional type.</summary>
    None,
    /// <summary>The name type filters members that do not match.</summary>
    Filtering,
    /// <summary>The name type remaps members.</summary>
    Remapping,
}

// ported from: src/compiler/checker.ts:1390-1395 — const enum ExpandingFlags
/// <summary>Which side of a relation comparison is being expanded for display (the circularity guard).</summary>
[Flags]
internal enum ExpandingFlags
{
    None = 0,
    Source = 1,
    Target = 1 << 1,
    Both = Source | Target,
}

// ported from: src/compiler/checker.ts:1397-1400 — const enum MembersOrExportsResolutionKind
/// <summary>Which of a symbol's two resolved tables <c>getResolvedMembersOrExportsOfSymbol</c> is asked for.</summary>
/// <remarks>
/// PORT-NOTE: TS's members are the strings <c>"resolvedExports"</c>/<c>"resolvedMembers"</c> because it uses the
/// enum value as an index into the symbol's links (<c>links[resolutionKind]</c>, checker.ts:13951/13960). C# cannot
/// index a class by string, so the port keeps the discriminator as an ordinary enum and the readers select the
/// <c>ResolvedExports</c>/<c>ResolvedMembers</c> slot (the same two slots, in the same order).
/// </remarks>
internal enum MembersOrExportsResolutionKind
{
    /// <summary>The early- and late-bound static members (<c>links.resolvedExports</c>).</summary>
    ResolvedExports,
    /// <summary>The early- and late-bound instance members (<c>links.resolvedMembers</c>).</summary>
    ResolvedMembers,
}

// ported from: src/compiler/checker.ts:1402-1405 — const enum UnusedKind
/// <summary>Whether an unused declaration is a local or a parameter, which decides the diagnostic.</summary>
internal enum UnusedKind
{
    /// <summary>An unreferenced local variable.</summary>
    Local,
    /// <summary>An unreferenced parameter.</summary>
    Parameter,
}

// ported from: src/compiler/checker.ts:1412-1420 — const enum DeclarationMeaning
/// <summary>Which kinds of declaration an identifier is used to refer to.</summary>
[Flags]
internal enum DeclarationMeaning
{
    GetAccessor = 1,
    SetAccessor = 2,
    PropertyAssignment = 4,
    Method = 8,
    PrivateStatic = 16,
    GetOrSetAccessor = GetAccessor | SetAccessor,
    PropertyAssignmentOrMethod = PropertyAssignment | Method,
}

// ported from: src/compiler/checker.ts:1422-1427 — const enum DeclarationSpaces
/// <summary>
/// Which declaration spaces a declaration occupies; <c>hasNonGlobalAugmentationExternalModuleSymbol</c> and the
/// duplicate-identifier checks read it.
/// </summary>
[Flags]
internal enum DeclarationSpaces
{
    None = 0,
    ExportValue = 1 << 0,
    ExportType = 1 << 1,
    ExportNamespace = 1 << 2,
}

// ported from: src/compiler/checker.ts:1429-1433 — const enum MinArgumentCountFlags
/// <summary>How <c>getMinArgumentCount</c> treats untyped JS and <c>void</c>.</summary>
[Flags]
internal enum MinArgumentCountFlags
{
    None = 0,
    /// <summary>An untyped JS function must still be called with the arguments it declares.</summary>
    StrongArityForUntypedJS = 1 << 0,
    /// <summary>A <c>void</c> parameter is not optional.</summary>
    VoidIsNonOptional = 1 << 1,
}

// ported from: src/compiler/checker.ts:1435-1441 — const enum IntrinsicTypeKind
/// <summary>The intrinsic string-manipulation types.</summary>
internal enum IntrinsicTypeKind
{
    /// <summary><c>Uppercase&lt;T&gt;</c>.</summary>
    Uppercase,
    /// <summary><c>Lowercase&lt;T&gt;</c>.</summary>
    Lowercase,
    /// <summary><c>Capitalize&lt;T&gt;</c>.</summary>
    Capitalize,
    /// <summary><c>Uncapitalize&lt;T&gt;</c>.</summary>
    Uncapitalize,
    /// <summary><c>NoInfer&lt;T&gt;</c>.</summary>
    NoInfer,
}
