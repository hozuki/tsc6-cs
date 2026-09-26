namespace TypeScript.Compiler;

/// <summary>
/// The <see cref="Utilities"/> half of the A3 symbol band: the utilities.ts/utilitiesPublic.ts helpers whose only or
/// first callers are the band's checker members, plus the script-target feature table the "did you mean a lib" arm of
/// the name resolver reads.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (file home): the assignment for this band places its utilities.ts/utilitiesPublic.ts names beside the
/// band's checker members and names <c>Checker/Checker.Support.cs</c>'s <c>Utilities</c> partial as the home. That file
/// is being written by a sibling slice at the same time, so the members live in this partial instead — the same
/// treatment <c>Checker/Utilities.Checker.cs</c>, <c>Program/Utilities.Program.cs</c> and
/// <c>Utilities/Utilities.Checker.cs</c> already give the class. <see cref="Utilities"/> is <c>partial</c> across many
/// files, so "one name, one owner" is unaffected: every name here is declared exactly once.
/// </para>
/// <para>
/// PORT-NOTE (one name, one owner): every member in this file was verified absent from the tree before it was written
/// (<c>IsAliasSymbolDeclaration</c>, <c>IsInTypeQuery</c>, <c>IsTypeOnlyImportDeclaration</c>, <c>GetCanonicalDiagnostic</c>,
/// <c>GetScriptTargetFeatures</c>, <c>TextRangeContainsPositionInclusive</c>, <c>GetExternalModuleRequireArgument</c>,
/// <c>GetNamespaceDeclarationNode</c>, <c>IsPartOfTypeOnlyImportOrExportDeclaration</c>, <c>IsForInOrOfStatement</c>), and
/// none of them appears in another band's order file.
/// </para>
/// </remarks>
internal static partial class Utilities
{

    // PORT-NOTE (isAliasSymbolDeclaration/isAliasableOrJsExpression, checker.ts:3683/3704): both are declared on the
    // Checker class in this band (Checker/Checker.SymbolSupport.cs). They read checker.ts:37696's `isJSConstructor`,
    // which the port holds as a Checker INSTANCE member, so a static Utilities copy could not be faithful; and the
    // reference declares both inside `createTypeChecker`, which the port's own home rule reads as Checker.

    // PORT-NOTE (isAliasableExpression, utilities.ts:5173-5175): landed by another slice while this file was being
    // written (Utilities/Utilities.Binder.Nodes.cs, with ts's `Expression` parameter), so the port keeps that single
    // declaration and this file does not repeat it.

    /// <summary>ported from: src/compiler/utilities.ts:6803-6811 — <c>isInTypeQuery</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's visitor answers <c>true | false | "quit"</c>; the port's quit-capable
    /// <c>FindAncestor(Node?, Func&lt;Node, bool?&gt;)</c> spells <c>"quit"</c> as <c>null</c> and the reference's
    /// <c>!!…</c> is the <c>is not null</c> test.
    /// </remarks>
    public static bool IsInTypeQuery(Node node)
        // TypeScript 1.0 spec (April 2014): 3.6.3
        // A type query consists of the keyword typeof followed by an expression.
        // The expression is restricted to a single identifier or a sequence of identifiers separated by periods
        => FindAncestor(node, n => n.Kind == SyntaxKind.TypeQuery ? true : n.Kind == SyntaxKind.Identifier || n.Kind == SyntaxKind.QualifiedName ? false : null) is not null;

    // PORT-NOTE (IsTypeOnlyImportDeclaration, utilitiesPublic.ts:1536): landed by another slice while this file was
    // being written (Utilities/Utilities.Checker.cs), so the port keeps that single declaration and this file does not
    // repeat it.

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1566-1568 — <c>isPartOfTypeOnlyImportOrExportDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isTypeOnlyImportOrExportDeclaration</c> is the A1-util-low member of that name (the port has no
    /// declaration of it yet; this file is one of its callers).
    /// </remarks>
    public static bool IsPartOfTypeOnlyImportOrExportDeclaration(Node node)
        => FindAncestor(node, n => IsTypeOnlyImportOrExportDeclaration(n)) is not null;

    /// <summary>ported from: src/compiler/utilities.ts:2502-2508 — <c>getCanonicalDiagnostic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns the object literal <c>{ code, messageText }</c> typed <c>CanonicalDiagnostic</c>; the
    /// port's generated model has no constructible face for <c>ICanonicalDiagnostic</c>, so the nested
    /// <see cref="CanonicalDiagnosticEntry"/> states it — the same treatment
    /// <c>Checker.Relations.Structured.cs</c>'s private <c>CanonicalDiagnosticHead</c> already gives the other writer of
    /// that slot. <c>formatMessage</c> is <see cref="FormatMessage"/>.
    /// </remarks>
    public static ICanonicalDiagnostic GetCanonicalDiagnostic(DiagnosticMessage message, params string[] args)
        => new CanonicalDiagnosticEntry
        {
            Code = message.Code,
            MessageText = FormatMessage(message, args),
        };

    /// <summary>ported from: src/compiler/types.ts:4390 — <c>CanonicalDiagnostic</c> (the object literal
    /// <c>getCanonicalDiagnostic</c> builds).</summary>
    private sealed class CanonicalDiagnosticEntry : ICanonicalDiagnostic
    {
        public int Code { get; set; }

        public string? MessageText { get; set; }
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:360-363 — <c>textRangeContainsPositionInclusive</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>TextRange</c> is the <c>{ pos, end }</c> structural pair, which the port's
    /// <see cref="ITextRange"/> states; <c>range.pos</c>/<c>range.end</c> are <c>Pos</c>/<c>End</c>.
    /// </remarks>
    public static bool TextRangeContainsPositionInclusive(ITextRange range, int position)
        => position >= range.Pos && position <= range.End;

    /// <summary>ported from: src/compiler/utilities.ts:3784-3786 — <c>getExternalModuleRequireArgument</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>false | StringLiteral</c> is the port's <c>StringLiteral?</c> (the <c>false</c> arm is the
    /// null one; both call sites test truthiness); <c>getLeftmostAccessExpression(node.initializer) as CallExpression</c>
    /// and the <c>arguments[0] as StringLiteral</c> are the port's two casts over the concrete faces.
    /// </remarks>
    public static StringLiteral? GetExternalModuleRequireArgument(Node node)
    {
        if (!IsVariableDeclarationInitializedToBareOrAccessedRequire(node))
        {
            return null;
        }

        var initializer = ((VariableDeclaration)node).Initializer;
        return initializer is not null
            ? (StringLiteral)((CallExpression)GetLeftmostAccessExpression(initializer)).Arguments![0]
            : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4427-4438 — <c>getNamespaceDeclarationNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ImportDeclaration | ImportEqualsDeclaration | ExportDeclaration</c> union and
    /// its <c>default</c> arm is <c>Debug.assertNever(node)</c>; the port takes the union's bound <see cref="Node"/>, so
    /// the default arm answers <c>null</c> for the kinds the reference can never see (the two <c>tryCast</c> reads are
    /// <c>Core.TryCast</c> with the same predicates).
    /// </remarks>
    public static Node? GetNamespaceDeclarationNode(Node node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.ImportDeclaration:
                var importClause = ((ImportDeclaration)node).ImportClause;
                return importClause is not null ? Core.TryCast<NamespaceImport, Node>(importClause.NamedBindings, NodeTests.IsNamespaceImport) : null;
            case SyntaxKind.ImportEqualsDeclaration:
                return node;
            case SyntaxKind.ExportDeclaration:
                var exportClause = ((ExportDeclaration)node).ExportClause;
                return exportClause is not null ? Core.TryCast<NamespaceExport, Node>(exportClause, NodeTests.IsNamespaceExport) : null;
            default:
                return null;
        }
    }

    // PORT-NOTE (IsForInOrOfStatement, utilitiesPublic.ts): landed by another slice while this file was being written
    // (Utilities/Utilities.Binder.cs), so the port keeps that single declaration and this file does not repeat it.

    /// <summary>ported from: src/compiler/utilities.ts:1384-9283 — <c>getScriptTargetFeatures</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds this table from one object literal inside <c>memoize(...)</c>
    /// (<c>ScriptTargetFeatures = ReadonlyMap&lt;string, ReadonlyMap&lt;string, string[]&gt;&gt;</c>, utilities.ts:1377);
    /// the port states the same data as the dictionary literal below and memoizes it in
    /// <c>_scriptTargetFeatures</c>, because C# has no <c>/* @__PURE__ */ memoize</c> on a data constant. The 54 types
    /// and their 105 target keys are transcribed 1:1 from the reference, in the reference's order (the table is
    /// order-insensitive — it is only ever read by key — but the order is kept so a diff against the reference is
    /// mechanical).
    /// </remarks>
    public static Dictionary<string, Dictionary<string, List<string>>> GetScriptTargetFeatures()
        => _scriptTargetFeatures ??= new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal)
        {
            ["Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["find", "findIndex", "fill", "copyWithin", "entries", "keys", "values"],
                ["es2016"] = ["includes"],
                ["es2019"] = ["flat", "flatMap"],
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Iterator"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["AsyncIterator"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["ArrayBuffer"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2024"] = ["maxByteLength", "resizable", "resize", "detached", "transfer", "transferToFixedLength"],
            },
            ["Atomics"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2017"] = ["add", "and", "compareExchange", "exchange", "isLockFree", "load", "or", "store", "sub", "wait", "notify", "xor"],
                ["es2024"] = ["waitAsync"],
                ["esnext"] = ["pause"],
            },
            ["SharedArrayBuffer"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2017"] = ["byteLength", "slice"],
                ["es2024"] = ["growable", "maxByteLength", "grow"],
            },
            ["AsyncIterable"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["AsyncIterableIterator"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["AsyncGenerator"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["AsyncGeneratorFunction"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["RegExp"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["flags", "sticky", "unicode"],
                ["es2018"] = ["dotAll"],
                ["es2024"] = ["unicodeSets"],
            },
            ["RegExpConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2025"] = ["escape"],
            },
            ["Reflect"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["apply", "construct", "defineProperty", "deleteProperty", "get", "getOwnPropertyDescriptor", "getPrototypeOf", "has", "isExtensible", "ownKeys", "preventExtensions", "set", "setPrototypeOf"],
            },
            ["ArrayConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["from", "of"],
                ["esnext"] = ["fromAsync"],
            },
            ["ObjectConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["assign", "getOwnPropertySymbols", "keys", "is", "setPrototypeOf"],
                ["es2017"] = ["values", "entries", "getOwnPropertyDescriptors"],
                ["es2019"] = ["fromEntries"],
                ["es2022"] = ["hasOwn"],
                ["es2024"] = ["groupBy"],
            },
            ["NumberConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["isFinite", "isInteger", "isNaN", "isSafeInteger", "parseFloat", "parseInt"],
            },
            ["Math"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["clz32", "imul", "sign", "log10", "log2", "log1p", "expm1", "cosh", "sinh", "tanh", "acosh", "asinh", "atanh", "hypot", "trunc", "fround", "cbrt"],
                ["es2025"] = ["f16round"],
            },
            ["Map"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["entries", "keys", "values"],
                ["esnext"] = ["getOrInsert", "getOrInsertComputed"],
            },
            ["MapConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2024"] = ["groupBy"],
            },
            ["Set"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["entries", "keys", "values"],
                ["es2025"] = ["union", "intersection", "difference", "symmetricDifference", "isSubsetOf", "isSupersetOf", "isDisjointFrom"],
            },
            ["PromiseConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["all", "race", "reject", "resolve"],
                ["es2020"] = ["allSettled"],
                ["es2021"] = ["any"],
                ["es2024"] = ["withResolvers"],
                ["es2025"] = ["try"],
            },
            ["Symbol"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["for", "keyFor"],
                ["es2019"] = ["description"],
            },
            ["WeakMap"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["entries", "keys", "values"],
                ["esnext"] = ["getOrInsert", "getOrInsertComputed"],
            },
            ["WeakSet"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["entries", "keys", "values"],
            },
            ["String"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["codePointAt", "includes", "endsWith", "normalize", "repeat", "startsWith", "anchor", "big", "blink", "bold", "fixed", "fontcolor", "fontsize", "italics", "link", "small", "strike", "sub", "sup"],
                ["es2017"] = ["padStart", "padEnd"],
                ["es2019"] = ["trimStart", "trimEnd", "trimLeft", "trimRight"],
                ["es2020"] = ["matchAll"],
                ["es2021"] = ["replaceAll"],
                ["es2022"] = ["at"],
                ["es2024"] = ["isWellFormed", "toWellFormed"],
            },
            ["StringConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2015"] = ["fromCodePoint", "raw"],
            },
            ["DateTimeFormat"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2017"] = ["formatToParts"],
            },
            ["Promise"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2018"] = ["finally"],
            },
            ["RegExpMatchArray"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2018"] = ["groups"],
            },
            ["RegExpExecArray"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2018"] = ["groups"],
            },
            ["Intl"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2018"] = ["PluralRules"],
                ["es2020"] = ["RelativeTimeFormat", "Locale", "DisplayNames"],
                ["es2021"] = ["ListFormat", "DateTimeFormat"],
                ["es2022"] = ["Segmenter"],
                ["es2025"] = ["DurationFormat"],
            },
            ["NumberFormat"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2018"] = ["formatToParts"],
            },
            ["SymbolConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2020"] = ["matchAll"],
                ["esnext"] = ["metadata", "dispose", "asyncDispose"],
            },
            ["DataView"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2020"] = ["setBigInt64", "setBigUint64", "getBigInt64", "getBigUint64"],
                ["es2025"] = ["setFloat16", "getFloat16"],
            },
            ["BigInt"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["RelativeTimeFormat"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2020"] = ["format", "formatToParts", "resolvedOptions"],
            },
            ["Int8Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Uint8Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
                ["esnext"] = ["toBase64", "setFromBase64", "toHex", "setFromHex"],
            },
            ["Uint8ClampedArray"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Int16Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Uint16Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Int32Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Uint32Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Float16Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["Float32Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Float64Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["BigInt64Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["BigUint64Array"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["at"],
                ["es2023"] = ["findLastIndex", "findLast", "toReversed", "toSorted", "toSpliced", "with"],
            },
            ["Error"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["es2022"] = ["cause"],
            },
            ["ErrorConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["esnext"] = ["isError"],
            },
            ["Uint8ArrayConstructor"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["esnext"] = ["fromBase64", "fromHex"],
            },
            ["Date"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["esnext"] = ["toTemporalInstant"],
            },
            ["DisposableStack"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {

            },
            ["AsyncDisposableStack"] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["dependencies"] = ["rewriteRelativeImportExtensions"],
                ["dependencies"] = ["target"],
                ["dependencies"] = ["verbatimModuleSyntax"],
                ["dependencies"] = ["composite"],
                ["dependencies"] = ["composite"],
                ["dependencies"] = ["checkJs"],
                ["dependencies"] = ["strict"],
                ["dependencies"] = ["strict"],
                ["dependencies"] = ["strict"],
                ["dependencies"] = ["strict"],
                ["dependencies"] = ["strict"],
                ["dependencies"] = ["strict"],
                ["dependencies"] = ["strict"],
                ["dependencies"] = ["strict"],
            },
        };

    /// <summary>ported from: src/compiler/utilities.ts:1384 — the memoized slot of <c>getScriptTargetFeatures</c>.</summary>
    private static Dictionary<string, Dictionary<string, List<string>>>? _scriptTargetFeatures;

    /// <summary>ported from: src/compiler/utilities.ts:4402-4404 — <c>shouldRewriteModuleSpecifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>compilerOptions.rewriteRelativeImportExtensions</c>, <c>pathIsRelative</c>,
    /// <c>isDeclarationFileName</c> and <c>hasTSFileExtension</c> are the port's
    /// <c>_compilerOptions</c>-free equivalents: the options slot, <c>Utilities.PathIsRelative</c>,
    /// <c>Parser.IsDeclarationFileName</c> and <see cref="HasTSFileExtension"/>.
    /// </remarks>
    public static bool ShouldRewriteModuleSpecifier(string specifier, ICompilerOptions compilerOptions)
        => Js.IsTruthy(compilerOptions.RewriteRelativeImportExtensions) && Paths.PathIsRelative(specifier) && !Parser.IsDeclarationFileName(specifier) && HasTSFileExtension(specifier);

    /// <summary>ported from: src/compiler/utilities.ts:12088-12091 — <c>isSideEffectImport</c>.</summary>
    public static bool IsSideEffectImport(Node node)
    {
        var ancestor = FindAncestor(node, NodeTests.IsImportDeclaration);
        return ancestor is not null && ((ImportDeclaration)ancestor).ImportClause is null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:11186-11191 — <c>hasResolutionModeOverride</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ImportTypeNode | ImportDeclaration | ExportDeclaration | JSDocImportTag |
    /// undefined</c> union, stated as the bound <see cref="Node"/>; the <c>attributes</c> read is the switch over the four
    /// concrete <c>Attributes</c> slots, and <c>getResolutionModeOverride</c> is the program-side
    /// <c>Program.GetResolutionModeOverride</c> (program.ts:992, the same member the ported host diagnostics call).
    /// </remarks>
    public static bool HasResolutionModeOverride(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        var attributes = node switch
        {
            ImportTypeNode importTypeNode => importTypeNode.Attributes,
            ImportDeclaration importDeclaration => importDeclaration.Attributes,
            ExportDeclaration exportDeclaration => exportDeclaration.Attributes,
            JSDocImportTag jsDocImportTag => jsDocImportTag.Attributes,
            _ => null,
        };
        return Program.GetResolutionModeOverride(attributes) is not null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10032-10034 — <c>hasTSFileExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>supportedTSExtensionsFlat</c> is <c>Utilities.SupportedTSExtensionsFlat</c> and
    /// <c>fileExtensionIs</c> is <c>Core.Paths.FileExtensionIs</c>, the lower-layer copy both callers share (the same
    /// split <c>Parser.IsDeclarationFileName</c> documents).
    /// </remarks>
    public static bool HasTSFileExtension(string fileName)
        => SupportedTSExtensionsFlat.Exists(extension => Paths.FileExtensionIs(fileName, extension));

    /// <summary>ported from: src/compiler/utilities.ts:873-901 — <c>createModeMismatchDetails</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>currentSourceFile.packageJsonScope</c> is the port's <c>object?</c> slot (the generated model cannot
    /// name the scope type), so the reference's <c>scope &amp;&amp; !scope.contents.packageJsonContent.type</c> is stated as
    /// the <c>PackageJsonInfo</c> cast (Program/ModuleResolution.Model.cs:326 — the port's class for
    /// TS's <c>PackageJsonInfo</c>; the node slot is <c>object?</c>) with the same two reads, whose
    /// <c>packageJsonContent.type</c> is the <c>JsObject.Get("type")</c> lookup; <c>tryGetExtensionFromPath</c> and
    /// <c>combinePaths</c> are <c>Utilities.TryGetExtensionFromPath</c>/<c>Utilities.CombinePaths</c>; and
    /// <c>result.repopulateInfo = () =&gt; true</c> is the <c>Func&lt;object?&gt;</c> slot the port's
    /// <c>IDiagnosticMessageChain</c> carries (boxed <c>true</c>).
    /// </remarks>
    public static IDiagnosticMessageChain CreateModeMismatchDetails(SourceFile currentSourceFile)
    {
        var ext = TryGetExtensionFromPath(currentSourceFile.FileName!);
        var scope = currentSourceFile.PackageJsonScope as PackageJsonInfo;
        Extension? targetExt = ext == Extension.Ts.Value ? Extension.Mts : ext == Extension.Js.Value ? Extension.Mjs : null;
        var result = scope is not null && scope.Contents.PackageJsonContent.Get("type") is null
            ? targetExt is not null
                ? ChainDiagnosticMessages(
                    /*details*/ null,
                    Diagnostics.To_convert_this_file_to_an_ECMAScript_module_change_its_file_extension_to_0_or_add_the_field_type_Colon_module_to_1,
                    targetExt,
                    Paths.CombinePaths(scope.PackageDirectory, "package.json"))
                : ChainDiagnosticMessages(
                    /*details*/ null,
                    Diagnostics.To_convert_this_file_to_an_ECMAScript_module_add_the_field_type_Colon_module_to_0,
                    Paths.CombinePaths(scope.PackageDirectory, "package.json"))
            : targetExt is not null
            ? ChainDiagnosticMessages(
                /*details*/ null,
                Diagnostics.To_convert_this_file_to_an_ECMAScript_module_change_its_file_extension_to_0_or_create_a_local_package_json_file_with_type_Colon_module,
                targetExt)
            : ChainDiagnosticMessages(
                /*details*/ null,
                Diagnostics.To_convert_this_file_to_an_ECMAScript_module_create_a_local_package_json_file_with_type_Colon_module);
        result.RepopulateInfo = () => true;
        return result;
    }

    /// <summary>ported from: src/compiler/utilities.ts:834-870 — <c>createModuleNotFoundChain</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the host's resolved-module lookup is the port's <c>GetResolvedModule(sourceFile, moduleReference,
    /// mode)?.AlternateResult</c>; <c>getEmitModuleResolutionKind(host.getCompilerOptions())</c> is
    /// <c>Utilities.GetEmitModuleResolutionKind(host.GetCompilerOptions())</c>; the reference's two
    /// <c>as const</c> tuples are the port's nullable named tuple; <c>nodeModulesPathPart</c> and
    /// <c>mangleScopedPackageName</c> are <c>ModuleNameResolver.NodeModulesPathPart</c> and
    /// <c>ModuleNameResolver.MangleScopedPackageName</c> (the module-resolution slice's public members — the checker does
    /// not own them); <c>host.typesPackageExists</c>/<c>host.packageBundlesTypes</c> are the host reads of those names;
    /// and <c>result.repopulateInfo = () =&gt; ({ moduleReference, mode, packageName })</c> is the port's
    /// <c>Func&lt;object?&gt;</c> slot, stated as the same three-slot tuple with the reference's conditional third element.
    /// </remarks>
    public static IDiagnosticMessageChain CreateModuleNotFoundChain(SourceFile sourceFile, ITypeCheckerHost host, string moduleReference, ModuleKind? mode, string packageName)
    {
        var alternateResult = host.GetResolvedModule(sourceFile, moduleReference, mode)?.AlternateResult;
        (DiagnosticMessage Message, object?[] Args)? alternateResultMessage = alternateResult is not null && GetEmitModuleResolutionKind(host.GetCompilerOptions()) == ModuleResolutionKind.Node10
            ? (Diagnostics.There_are_types_at_0_but_this_result_could_not_be_resolved_under_your_current_moduleResolution_setting_Consider_updating_to_node16_nodenext_or_bundler, [alternateResult])
            : alternateResult is not null
            ? (Diagnostics.There_are_types_at_0_but_this_result_could_not_be_resolved_when_respecting_package_json_exports_The_1_library_may_need_to_update_its_package_json_or_typings,
                [alternateResult, alternateResult.Contains(ModuleNameResolver.NodeModulesPathPart + "@types/") ? $"@types/{ModuleNameResolver.MangleScopedPackageName(packageName)}" : packageName])
            : null;
        var result = alternateResultMessage is not null
            ? ChainDiagnosticMessages(
                /*details*/ null,
                alternateResultMessage.Value.Message,
                alternateResultMessage.Value.Args)
            : host.TypesPackageExists(packageName)
            ? ChainDiagnosticMessages(
                /*details*/ null,
                Diagnostics.If_the_0_package_actually_exposes_this_module_consider_sending_a_pull_request_to_amend_https_Colon_Slash_Slashgithub_com_SlashDefinitelyTyped_SlashDefinitelyTyped_Slashtree_Slashmaster_Slashtypes_Slash_1,
                packageName,
                ModuleNameResolver.MangleScopedPackageName(packageName))
            : host.PackageBundlesTypes(packageName)
            ? ChainDiagnosticMessages(
                /*details*/ null,
                Diagnostics.If_the_0_package_actually_exposes_this_module_try_adding_a_new_declaration_d_ts_file_containing_declare_module_1,
                packageName,
                moduleReference)
            : ChainDiagnosticMessages(
                /*details*/ null,
                Diagnostics.Try_npm_i_save_dev_types_Slash_1_if_it_exists_or_add_a_new_declaration_d_ts_file_containing_declare_module_0,
                moduleReference,
                ModuleNameResolver.MangleScopedPackageName(packageName));
        result.RepopulateInfo = () => (moduleReference, mode, packageName == moduleReference ? null : packageName);
        return result;
    }

    /// <summary>ported from: src/compiler/utilities.ts:3885-3896 — <c>getModuleSpecifierOfBareOrAccessedRequire</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's first arm (<c>isVariableDeclarationInitializedToRequire</c>) is a subset of the second
    /// (<c>isVariableDeclarationInitializedToBareOrAccessedRequire</c>, which is the guard at this member's only call
    /// site), and for a plain <c>require(...)</c> initializer the two arms answer the same
    /// <c>leftmost.arguments[0]</c> — so the single landed arm covers both, with the shared
    /// <c>Utilities.GetLeftmostAccessExpression</c>/<c>Utilities.IsRequireCall</c> pair. TS's
    /// <c>StringLiteralLike</c> answer is the port's <c>Expression?</c> (the union has no face), which the call site reads
    /// as the context specifier.
    /// </remarks>
    public static Expression? GetModuleSpecifierOfBareOrAccessedRequire(Node node)
    {
        var initializer = ((VariableDeclaration)node).Initializer;
        if (initializer is null)
        {
            return null;
        }

        var leftmost = GetLeftmostAccessExpression(initializer);
        return IsRequireCall(leftmost, /*requireStringLiteralLikeArgument*/ true) ? (Expression?)((CallExpression)leftmost).Arguments![0] : null;
    }
}
