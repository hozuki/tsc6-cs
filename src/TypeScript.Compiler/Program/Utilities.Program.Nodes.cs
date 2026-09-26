using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:2276-2284 (isAnyImportSyntax), 2320-2322 (isAnyImportOrReExport), 2706-2714 (isImportCall),
//              3799-3801 (isSourceFileJS), 4407-4424 (getExternalModuleName),
//              6661-6688 (sourceFileMayBeEmitted), 6695-6700 (getSourceFilePathInNewDirWorker),
//              8958-8961 (walkTreeForJSXTags), 8963-8967 (isFileModuleFromUsingJSXTag),
//              8973-8978 (isFileForcedToBeModuleByFormat), 8981-9007 (getSetExternalModuleIndicator),
//              9412-9427 (getJSXImplicitImportBase), 9430-9432 (getJSXRuntimeImport),
//              12094-12152 (unprefixedNodeCoreModules), 12156-12162 (exclusivelyPrefixedNodeCoreModules),
//              12172-12199 (forEachDynamicImportOrRequireCall), 12201-12216 (getNodeAtPosition)
// ported from: src/compiler/utilitiesPublic.ts:2489-2493 (isJsxOpeningLikeElement)
/// <summary>
/// The node- and file-level free functions of <c>utilities.ts</c> that the program slices call: the
/// import/re-export navigators, the dynamic-import scan, module detection, the JSX runtime name, the
/// may-be-emitted gate and the node core module tables.
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no module-level functions, so these land on the <c>Utilities</c> partial class as each batch
/// needs them, every member carrying the line it came from (docs/architecture.md §15.9). A member whose faithful body
/// needs a helper nothing has ported brings that helper with it, each with its own <c>ported from</c> line —
/// <c>isAnyImportSyntax</c>, <c>isImportCall</c>, <c>isJsxOpeningLikeElement</c>, <c>getNodeAtPosition</c>,
/// <c>walkTreeForJSXTags</c>, <c>isFileModuleFromUsingJSXTag</c>, <c>isFileForcedToBeModuleByFormat</c>,
/// <c>getSourceFilePathInNewDirWorker</c> and the two pragma readers <c>getJSXImplicitImportBase</c> is written in
/// terms of.
/// <para>
/// PORT-NOTE: TS's <c>T | undefined</c> is <c>T?</c> (§5.1) and its object truthiness is an
/// <c>is not null</c>/<c>is T</c> pattern (§12.3); where the reference tests a <em>string</em> for truthiness
/// (<c>getJSXRuntimeImport</c>'s base, a pragma's factory argument) the port calls <c>Js.IsTruthy</c>, which is the
/// same test. The generated option slots are <c>string?</c>/<c>bool?</c>, and the string ones are read with
/// <c>is not null</c> — the spelling the emitter-boundary slice and <c>Utilities.Program.cs</c> already use, so an
/// option that carries the empty string counts as present.
/// </para>
/// <para>
/// PORT-NOTE: the members of this batch that the port already owns elsewhere are <em>called</em>, never redeclared (a
/// second declaration is a CS0102/CS0111 duplicate): <c>externalHelpersModuleNameText</c> (utilities.ts:615) is
/// <c>Utilities.ExternalHelpersModuleNameText</c> in the diagnostics slice's <c>Program/Program.ProgramDiagnostics.cs:1060</c>
/// — declared there as <c>static readonly string</c> where the reference (and this batch's table) has a <c>const</c>,
/// which is that slice's spelling to align; <c>isExternalModuleNameRelative</c> (utilitiesPublic.ts:296-301) is the
/// module-resolution slice's (<c>Program/Utilities.ModuleResolution.cs:116</c>, widened to <c>string?</c> for the
/// callers here); <c>isPlainJsFile</c> (utilities.ts:997-999) and <c>isLiteralImportTypeNode</c> (utilities.ts:2724-2726)
/// are the checker slice's (<c>Checker/Utilities.Checker.cs:90</c> and <c>:111</c>) — this file's
/// <c>getExternalModuleName</c> and <c>forEachDynamicImportOrRequireCall</c> read the second through the casts that
/// guard licenses, as the reference does; <c>isCheckJsEnabledForFile</c> (utilities.ts:10307-10309) is
/// <see cref="Utilities.IsCheckJsEnabledForFile"/> (<c>Program/Utilities.Program.cs:647</c>); and
/// <c>changeExtension</c> (utilities.ts:10193-10195) is <see cref="Utilities.ChangeExtension"/>
/// (<c>Program/Utilities.Program.cs:496</c>, landed by the M6-1 owner after Main's adjudication: the reference declares it in
/// <c>utilities.ts</c>, not <c>path.ts</c>; <c>Core/Paths.cs</c> has <c>ChangeAnyExtension</c>, which is its implementation).
/// <c>getTypesPackageName</c>, <c>nodeModulesPathPart</c> and <c>inferredTypesContainingFile</c> belong to the
/// module-resolution and program-host slices, and <c>changesAffectingProgramStructure</c>/
/// <c>changesAffectModuleResolution</c> to the project-reference slice, per the batch's assignment.
/// </para>
/// <para>
/// PORT-NOTE: <c>skipTypeChecking</c> (utilities.ts:10397-10404), <c>skipTypeCheckingIgnoringNoCheck</c> and
/// <c>canIncludeBindAndCheckDiagnostics</c> (utilities.ts:10430-10447) are <em>not</em> declared here either: the
/// diagnostics slice landed them in <c>Program/Utilities.Program.cs:589</c> and <c>:623</c> with the reference's host
/// questions taken as delegates (<c>Func&lt;SourceFile, bool&gt;</c> / <c>Func&lt;string, bool&gt;</c>), which is
/// where the batch's assigned two-parameter <c>SkipTypeChecking</c> row now lives — with both
/// <c>skipDefaultLibCheck</c> and source-of-project-reference-redirect legs intact, so nothing is dropped.
/// </para>
/// <para>
/// PORT-NOTE: <c>sourceFileMayBeEmitted</c> is declared with the assigned signature, which is narrower than the
/// reference's: TS's third parameter is <c>forceDtsEmit?: boolean</c> (utilities.ts:6661) and its body short-circuits
/// on it, so that leg is not evaluated and the M7 emit paths that pass it must extend the signature
/// (<c>docs/architecture.md</c> §15.15).
/// </para>
/// <para>
/// PORT-NOTE: <c>getSetExternalModuleIndicator</c>'s <c>ModuleDetectionKind.Auto</c> arm reaches
/// <c>getImpliedNodeFormatForEmitWorker</c> (program.ts:4855) through <c>isFileForcedToBeModuleByFormat</c>; that
/// member is M6ProgHost's (<c>Program/Program.Host.cs:1818</c>, <c>internal static ModuleKind? GetImpliedNodeFormatForEmitWorker(SourceFile?, ICompilerOptions?)</c>), called across the
/// two classes exactly as the reference's import does. The <c>host.getCanonicalFileName</c> reads in
/// <c>sourceFileMayBeEmitted</c> cast the generated <c>object?</c> slot back to
/// <see cref="Core.GetCanonicalFileName"/> (the delegate type is declared outside the extracted sources, so the
/// generator could not type the slot — <c>Model/NodeInterfaces.cs:3067</c>).
/// </para>
/// <para>
/// PORT-NOTE: TS's exhaustive <c>switch</c> statements here end in <c>Debug.assertNever</c>; C# needs a
/// <c>default</c> arm even when the enum is covered, so each one is <c>Debug.Fail&lt;T&gt;</c>, the port's throwing
/// unreachable arm (<c>Parser/FactoryUtilities.cs:1711</c>).
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // Module name tables (utilities.ts:615, 12094-12162)
    // ---------------------------------------------------------------------------------------------

    // `externalHelpersModuleNameText` (utilities.ts:615) is `Utilities.ExternalHelpersModuleNameText`, declared by the
    // diagnostics slice — see this file's remarks.

    /// <summary>ported from: src/compiler/utilities.ts:12094-12152 — <c>unprefixedNodeCoreModules</c>.</summary>
    /// <remarks>
    /// TS builds the set from <c>unprefixedNodeCoreModulesList</c>, the value of
    /// <c>require('module').builtinModules.filter(x =&gt; !x.match(/^(?:_|node:)/))</c> at the time the compiler was
    /// written; the port keeps the same membership, with the ordinal comparer TS's <c>Set</c> uses.
    /// </remarks>
    public static readonly HashSet<string> UnprefixedNodeCoreModules = new(StringComparer.Ordinal)
    {
        "assert",
        "assert/strict",
        "async_hooks",
        "buffer",
        "child_process",
        "cluster",
        "console",
        "constants",
        "crypto",
        "dgram",
        "diagnostics_channel",
        "dns",
        "dns/promises",
        "domain",
        "events",
        "fs",
        "fs/promises",
        "http",
        "http2",
        "https",
        "inspector",
        "inspector/promises",
        "module",
        "net",
        "os",
        "path",
        "path/posix",
        "path/win32",
        "perf_hooks",
        "process",
        "punycode",
        "querystring",
        "readline",
        "readline/promises",
        "repl",
        "stream",
        "stream/consumers",
        "stream/promises",
        "stream/web",
        "string_decoder",
        "sys",
        "timers",
        "timers/promises",
        "tls",
        "trace_events",
        "tty",
        "url",
        "util",
        "util/types",
        "v8",
        "vm",
        "wasi",
        "worker_threads",
        "zlib",
    };

    /// <summary>ported from: src/compiler/utilities.ts:12156-12162 — <c>exclusivelyPrefixedNodeCoreModules</c>.</summary>
    /// <remarks>
    /// TS's comment gives the source: <c>require('module').builtinModules.filter(x =&gt; x.startsWith('node:'))</c>.
    /// The reference's <c>nodeCoreModules</c> union (utilities.ts:12165-12171) is not declared: no member of this
    /// batch reads it.
    /// </remarks>
    public static readonly HashSet<string> ExclusivelyPrefixedNodeCoreModules = new(StringComparer.Ordinal)
    {
        "node:quic",
        "node:sea",
        "node:sqlite",
        "node:test",
        "node:test/reporters",
    };

    // ---------------------------------------------------------------------------------------------
    // Import and re-export navigation (utilities.ts:2276-2322, 3799-3801, 4407-4424)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3799-3801 — <c>isSourceFileJS</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is non-null; the assigned slot is <c>SourceFile?</c>, and
    /// <see cref="Utilities.IsInJSFile"/> answers <c>false</c> for a missing file, so an absent file is "not a JS file"
    /// rather than a throw.
    /// </remarks>
    public static bool IsSourceFileJS(SourceFile? file) => IsInJSFile(file);

    /// <summary>ported from: src/compiler/utilities.ts:2276-2284 — <c>isAnyImportSyntax</c>.</summary>
    public static bool IsAnyImportSyntax(Node node)
        => node.Kind is SyntaxKind.ImportDeclaration or SyntaxKind.ImportEqualsDeclaration;

    /// <summary>ported from: src/compiler/utilities.ts:2320-2322 — <c>isAnyImportOrReExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard is <c>node is AnyImportOrReExport</c>; C# cannot narrow through a predicate, so the
    /// member answers <c>bool</c> and a caller that needs the narrowed shape casts. The parameter is the assigned
    /// <c>Node?</c> — TS's is non-null — so a missing node answers <c>false</c>.
    /// </remarks>
    public static bool IsAnyImportOrReExport(Node? node)
        => node is not null && (IsAnyImportSyntax(node) || NodeTests.IsExportDeclaration(node));

    // `isLiteralImportTypeNode` is declared by the checker slice (Checker/Utilities.Checker.cs:105) — see this
    // file's remarks.

    /// <summary>ported from: src/compiler/utilities.ts:4407-4424 — <c>getExternalModuleName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's return type is <c>Expression | undefined</c>, which is wider than the
    /// <c>LiteralExpression?</c> the batch's table proposed (a dynamic import call's first argument may be any
    /// expression, e.g. <c>import(name)</c>), so the port keeps the reference's type. The parameter is the assigned
    /// <c>Node?</c>; TS's switch covers the five kinds it accepts and asserts otherwise, so a null node fails in that
    /// same arm rather than answering a value.
    /// </remarks>
    public static Expression? GetExternalModuleName(Node? node)
    {
        switch (node?.Kind)
        {
            case SyntaxKind.ImportDeclaration:
                return ((ImportDeclaration)node).ModuleSpecifier;
            case SyntaxKind.ExportDeclaration:
                return ((ExportDeclaration)node).ModuleSpecifier;
            case SyntaxKind.JSDocImportTag:
                return ((JSDocImportTag)node).ModuleSpecifier;
            case SyntaxKind.ImportEqualsDeclaration:
                return ((ImportEqualsDeclaration)node).ModuleReference is ExternalModuleReference reference
                    ? reference.Expression
                    : null;
            case SyntaxKind.ImportType:
                // TS: isLiteralImportTypeNode(node) ? node.argument.literal : undefined
                return IsLiteralImportTypeNode(node) ? ((LiteralTypeNode)((ImportTypeNode)node).Argument!).Literal : null;
            case SyntaxKind.CallExpression:
                // TS: node.arguments[0] — a missing argument list is the reference's `undefined` element.
                return ((CallExpression)node).Arguments?[0];
            case SyntaxKind.ModuleDeclaration:
                return ((ModuleDeclaration)node).Name is StringLiteral name ? name : null;
            default:
                return Debug.Fail<Expression?>($"Unexpected node kind {node?.Kind.ToString() ?? "null"}");
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:2706-2714 — <c>isImportCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard is <c>n is ImportCall</c>; the port answers <c>bool</c>. The `defer` arm is
    /// <c>import.defer(...)</c>, a meta property whose name is the text <c>defer</c>.
    /// </remarks>
    public static bool IsImportCall(Node node)
    {
        if (!NodeTests.IsCallExpression(node)) return false;

        var expression = ((CallExpression)node).Expression;
        if (expression is null) return false;

        return expression.Kind == SyntaxKind.ImportKeyword
            || (NodeTests.IsMetaProperty(expression)
                && ((MetaProperty)expression).KeywordToken == SyntaxKind.ImportKeyword
                && ((MetaProperty)expression).Name?.EscapedText == "defer");
    }

    // ---------------------------------------------------------------------------------------------
    // The dynamic import/require scan (utilities.ts:12172-12216)
    // ---------------------------------------------------------------------------------------------

    /// <summary>the reference's <c>/import|require/g</c> (utilities.ts:12177).</summary>
    [GeneratedRegex("import|require", RegexOptions.CultureInvariant)]
    private static partial Regex ImportOrRequireRegExp();

    /// <summary>ported from: src/compiler/utilities.ts:12172-12199 — <c>forEachDynamicImportOrRequireCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS walks the text with a stateful global regex (<c>while (r.exec(file.text) !== null)</c>) and asks
    /// <c>getNodeAtPosition</c> for the node at <c>r.lastIndex</c>; the port enumerates the same non-overlapping
    /// matches and uses <c>match.Index + match.Length</c>, which is that <c>lastIndex</c>.
    /// <para>
    /// PORT-NOTE: <c>SourceFile.Text</c> is <c>string?</c> in the port where TS declares it a <c>string</c>, so the
    /// scanned text is <c>?? ""</c> — the same coercion <c>Utilities.Binder.Diagnostics.cs</c> uses for that slot.
    /// </para>
    /// <para>
    /// PORT-NOTE: the assigned signature carries a <c>shouldSkipChild</c> predicate the reference has no counterpart
    /// for (utilities.ts:12172-12176 declares four parameters) and its body never descends children itself — the
    /// descent belongs to <c>getNodeAtPosition</c>, which the reference also gives no skip hook. The slot is accepted
    /// so the call sites keep their shape and is otherwise unread; nothing passes it today
    /// (<c>Program/Program.Graph.cs:93-100</c> passes four arguments).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's two callbacks are typed by the literal type arguments (<c>IncludeTypeSpaceImports</c> /
    /// <c>RequireStringLiteralLikeArgument</c>); the assigned <see cref="Action{T1,T2}"/> of <see cref="Node"/> is the
    /// port's spelling — the argument is a <c>StringLiteralLike</c> or the literal of an import type, and callers cast
    /// it to the shape they asked for.
    /// </para>
    /// </remarks>
    public static void ForEachDynamicImportOrRequireCall(
        SourceFile? file,
        bool includeTypeSpaceImports,
        bool requireStringLiteralLikeArgument,
        Action<Node, Node> callback,
        Func<Node, bool>? shouldSkipChild = null)
    {
        if (file is null) return;

        var isJavaScriptFile = IsInJSFile(file);
        foreach (Match match in ImportOrRequireRegExp().Matches(file.Text ?? ""))
        {
            var node = GetNodeAtPosition(file, match.Index + match.Length, includeTypeSpaceImports);
            if (isJavaScriptFile && IsRequireCall(node, requireStringLiteralLikeArgument))
            {
                // isRequireCall guarantees exactly one argument.
                callback(node, ((CallExpression)node).Arguments![0]);
            }
            else if (IsImportCall(node)
                && ((CallExpression)node).Arguments is { Count: >= 1 } arguments
                && (!requireStringLiteralLikeArgument || IsStringLiteralLike(arguments[0])))
            {
                callback(node, arguments[0]);
            }
            else if (includeTypeSpaceImports && IsLiteralImportTypeNode(node))
            {
                callback(node, ((LiteralTypeNode)((ImportTypeNode)node).Argument!).Literal!);
            }
            else if (includeTypeSpaceImports
                && NodeTests.IsJSDocImportTag(node)
                && GetExternalModuleName(node) is StringLiteral moduleNameExpr
                && Js.IsTruthy(moduleNameExpr.Text))
            {
                callback(node, moduleNameExpr);
            }
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:12201-12216 — <c>getNodeAtPosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>forEach(current.jsDoc, getContainingChild)</c> returns the first non-undefined mapped value;
    /// the port scans the <c>JSDocArray</c> for it. <c>hasJSDocNodes</c> answers <c>bool</c> and the jsDoc list is read
    /// through the pattern the guard proves (§12.3), and <c>forEachChild</c> is
    /// <see cref="ForEachChildTable.ForEachChild{T}"/> with <c>T</c> = <see cref="Node"/>.
    /// </remarks>
    private static Node GetNodeAtPosition(SourceFile sourceFile, int position, bool includeJSDoc)
    {
        // TS: (child) => child.pos <= position && (position < child.end || (position === child.end && child.kind === SyntaxKind.EndOfFileToken))
        Node? GetContainingChild(Node child)
            => child.Pos <= position
                && (position < child.End || (position == child.End && child.Kind == SyntaxKind.EndOfFileToken))
                    ? child
                    : null;

        var isJavaScriptFile = IsInJSFile(sourceFile);
        Node current = sourceFile;
        while (true)
        {
            // TS: isJavaScriptFile && includeJSDoc && hasJSDocNodes(current) && forEach(current.jsDoc, getContainingChild) || forEachChild(current, getContainingChild)
            Node? child = null;
            if (isJavaScriptFile && includeJSDoc && HasJSDocNodes(current) && ((IJSDocContainer)current).JsDoc is JSDocArray jsDoc)
            {
                foreach (var jsDocNode in jsDoc)
                {
                    child = GetContainingChild(jsDocNode);
                    if (child is not null) break;
                }
            }

            child ??= ForEachChildTable.ForEachChild(current, GetContainingChild);
            if (child is null || NodeTests.IsMetaProperty(child))
            {
                return current;
            }

            current = child;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Module detection (utilities.ts:8958-9007, 2489-2493)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2489-2493 — <c>isJsxOpeningLikeElement</c>.</summary>
    /// <remarks>PORT-NOTE: TS's guard answers <c>bool</c> here; a caller that needs the narrowed shape casts.</remarks>
    public static bool IsJsxOpeningLikeElement(Node node)
        => node.Kind is SyntaxKind.JsxOpeningElement or SyntaxKind.JsxSelfClosingElement;

    /// <summary>ported from: src/compiler/utilities.ts:8958-8961 — <c>walkTreeForJSXTags</c>.</summary>
    private static Node? WalkTreeForJSXTags(Node node)
    {
        if ((node.TransformFlags & TransformFlags.ContainsJsx) == 0) return null;
        return IsJsxOpeningLikeElement(node) || NodeTests.IsJsxFragment(node)
            ? node
            : ForEachChildTable.ForEachChild(node, WalkTreeForJSXTags);
    }

    /// <summary>ported from: src/compiler/utilities.ts:8963-8967 — <c>isFileModuleFromUsingJSXTag</c>.</summary>
    /// <remarks>
    /// TS's comment: excludes declaration files — they still require an explicit <c>export {}</c> or the like for
    /// back-compat purposes (not that declaration files should contain JSX tags!).
    /// </remarks>
    private static Node? IsFileModuleFromUsingJSXTag(SourceFile file)
        => !file.IsDeclarationFile ? WalkTreeForJSXTags(file) : null;

    /// <summary>ported from: src/compiler/utilities.ts:8973-8978 — <c>isFileForcedToBeModuleByFormat</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the result is TS's <c>true | undefined</c>, so the port's slot is <c>object?</c> — the shape
    /// <c>externalModuleIndicator</c> takes. <c>SourceFile.FileName</c> is <c>string?</c> in the port where TS declares
    /// it a <c>string</c>, so the extension test reads <c>?? ""</c> (and <c>Extension</c> is a value type whose string
    /// is <c>.Value</c>, <c>Core/Paths.cs</c> style).
    /// </remarks>
    private static object? IsFileForcedToBeModuleByFormat(SourceFile file, ICompilerOptions options)
        => (Program.GetImpliedNodeFormatForEmitWorker(file, options) == ModuleKind.ESNext
                || Paths.FileExtensionIsOneOf(
                    file.FileName ?? "",
                    [Extension.Cjs.Value, Extension.Cts.Value, Extension.Mjs.Value, Extension.Mts.Value]))
            && !file.IsDeclarationFile
                ? true
                : null;

    /// <summary>ported from: src/compiler/utilities.ts:8981-9007 — <c>getSetExternalModuleIndicator</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>switch</c> returns the callback for each kind and its <c>Auto</c> arm builds the checks with
    /// <c>or(...checks)</c> (core.ts:2463-2473), which returns the first <em>truthy</em> result of
    /// <c>(file, options)</c> calls; the loop below spells that out, because the port's <c>Core.Or</c> is the
    /// <c>Func&lt;T, bool&gt;</c> combinator and cannot carry a <c>Node | true</c> result. The Force arm's
    /// <c>isFileProbablyExternalModule(file) || !file.isDeclarationFile || undefined</c> is the conditional below,
    /// with <c>object?</c> as the assignment's type.
    /// </remarks>
    public static Action<SourceFile> GetSetExternalModuleIndicator(ICompilerOptions? options)
    {
        // TODO: Should this callback be cached?
        switch (GetEmitModuleDetectionKind(options))
        {
            case ModuleDetectionKind.Force:
                // All non-declaration files are modules, declaration files still do the usual isFileProbablyExternalModule
                return file =>
                {
                    var indicator = Parser.IsFileProbablyExternalModule(file);
                    file.ExternalModuleIndicator = indicator is not null
                        ? indicator
                        : file.IsDeclarationFile ? null : (object?)true;
                };
            case ModuleDetectionKind.Legacy:
                // Files are modules if they have imports, exports, or import.meta
                return file => file.ExternalModuleIndicator = Parser.IsFileProbablyExternalModule(file);
            case ModuleDetectionKind.Auto:
                // If module is nodenext or node16, all esm format files are modules
                // If jsx is react-jsx or react-jsxdev then jsx tags force module-ness
                // otherwise, the presence of import or export statments (or import.meta) implies module-ness
                var compilerOptions = options!;
                var checks = new List<Func<SourceFile, ICompilerOptions, object?>>
                {
                    (f, _) => Parser.IsFileProbablyExternalModule(f),
                };
                if (compilerOptions.Jsx == JsxEmit.ReactJSX || compilerOptions.Jsx == JsxEmit.ReactJSXDev)
                {
                    checks.Add((f, _) => IsFileModuleFromUsingJSXTag(f));
                }

                checks.Add((f, o) => IsFileForcedToBeModuleByFormat(f, o));

                return file =>
                {
                    object? result = null;
                    foreach (var check in checks)
                    {
                        result = check(file, compilerOptions);
                        if (Js.IsTruthy(result)) break;
                    }

                    file.ExternalModuleIndicator = result;
                };
            default:
                // TS's switch covers every ModuleDetectionKind; this is the arm C# needs.
                return Debug.Fail<Action<SourceFile>>($"Unexpected module detection kind: {GetEmitModuleDetectionKind(options)}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The JSX runtime import (utilities.ts:9412-9432)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:9412-9427 — <c>getJSXImplicitImportBase</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>file.pragmas.get(name)</c>, whose value is "one entry or a list of them", and
    /// <c>isArray(x) ? x[x.length - 1] : x</c> — the parser stores the port's shapes the same way
    /// (<c>Parser/Parser.Entry.cs:447-461</c> stores a <c>Parser.PragmaPseudoMapEntry</c> or a
    /// <c>List&lt;Parser.PragmaPseudoMapEntry&gt;</c>), so the two private readers below mirror both steps. The pragma
    /// argument itself is read the way the parser reads it (a plain string, or a <see cref="Parser.PragmaArgumentSpan"/> when
    /// the pragma's definition asked to capture the span).
    /// </remarks>
    public static string? GetJsxImplicitImportBase(ICompilerOptions? compilerOptions, SourceFile? file)
    {
        var jsxImportSourcePragmas = file?.Pragmas?.Get("jsximportsource");
        var jsxImportSourcePragma = GetLastPragmaEntry(jsxImportSourcePragmas);
        var jsxRuntimePragmas = file?.Pragmas?.Get("jsxruntime");
        var jsxRuntimePragma = GetLastPragmaEntry(jsxRuntimePragmas);
        if (GetPragmaArgumentText(jsxRuntimePragma, "factory") == "classic")
        {
            return null;
        }

        var options = compilerOptions!;
        var jsxImportSourcePragmaBase = GetPragmaArgumentText(jsxImportSourcePragma, "factory");
        return options.Jsx == JsxEmit.ReactJSX
                || options.Jsx == JsxEmit.ReactJSXDev
                || Js.IsTruthy(options.JsxImportSource)
                || jsxImportSourcePragma is not null
                || GetPragmaArgumentText(jsxRuntimePragma, "factory") == "automatic"
            ? Js.IsTruthy(jsxImportSourcePragmaBase)
                ? jsxImportSourcePragmaBase
                : Js.IsTruthy(options.JsxImportSource) ? options.JsxImportSource : "react"
            : null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9430-9432 — <c>getJSXRuntimeImport</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>base ? … : undefined</c> is a string truthiness test, so an empty base answers null.</remarks>
    public static string? GetJsxRuntimeImport(string? @base, ICompilerOptions? options)
    {
        var compilerOptions = options!;
        return Js.IsTruthy(@base)
            ? $"{@base}/{(compilerOptions.Jsx == JsxEmit.ReactJSXDev ? "jsx-dev-runtime" : "jsx-runtime")}"
            : null;
    }

    /// <summary>The one pragma entry a pragma-map lookup holds, last-wins when the pragma appeared more than once.</summary>
    /// <remarks>
    /// PORT-NOTE: this is TS's <c>isArray(x) ? x[x.length - 1] : x</c> with the port's two container shapes; answers
    /// null for an absent lookup, which is TS's <c>undefined</c>.
    /// </remarks>
    private static Parser.PragmaPseudoMapEntry? GetLastPragmaEntry(object? entryOrList)
        => entryOrList switch
        {
            List<Parser.PragmaPseudoMapEntry> { Count: > 0 } entries => entries[^1],
            Parser.PragmaPseudoMapEntry single => single,
            _ => null,
        };

    /// <summary>Reads one named argument out of a pragma entry; TS's <c>entry.arguments[name]</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the parser's reader of the same shape (<c>Parser/Parser.Entry.cs:604-607</c>) is private to
    /// <c>Parser</c>, so this is the <c>Utilities</c> copy: a captured span's text, else the plain string, else null.
    /// </remarks>
    private static string? GetPragmaArgumentText(Parser.PragmaPseudoMapEntry? entry, string name)
        => entry is not null && entry.Arguments.TryGetValue(name, out var value)
            ? value switch
            {
                Parser.PragmaArgumentSpan span => span.Value,
                _ => value as string,
            }
            : null;

    // ---------------------------------------------------------------------------------------------
    // JS-file classification (utilities.ts:997-999)
    // ---------------------------------------------------------------------------------------------

    // `isPlainJsFile` is declared by the checker slice (Checker/Utilities.Checker.cs:84) — see this file's remarks.

    // ---------------------------------------------------------------------------------------------
    // Emit gating (utilities.ts:6661-6700)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:6661-6688 — <c>sourceFileMayBeEmitted</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's third parameter is <c>forceDtsEmit?: boolean</c> (utilities.ts:6661) and its body
    /// short-circuits on it ("forcing dts emit =&gt; file needs to be emitted"); the M7 emit path that passes it is
    /// <c>Utilities.GetSourceFilesToEmit</c>, so the parameter is stated here in the reference's position.
    /// <para>
    /// PORT-NOTE: the option string slots (<c>outFile</c>, <c>outDir</c>, <c>rootDir</c>, <c>configFilePath</c>) are
    /// tested with <c>is not null</c>, the spelling the emitter-boundary slice fixed for <c>rootDir</c> and
    /// <c>configFilePath</c>; <c>noEmitForJsFiles</c> is a <c>bool?</c> and reads through <c>Js.IsTruthy</c>.
    /// <c>SourceFile.FileName</c> is <c>string?</c> where TS declares it a <c>string</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>getCommonSourceDirectory(options, () =&gt; [], host.getCurrentDirectory(),
    /// host.getCanonicalFileName)</c> is <c>emitter.ts</c>'s member, declared as
    /// <see cref="Program.GetCommonSourceDirectoryFromOptions"/> (the two helper arguments are this call's
    /// <c>() =&gt; []</c> and the host's canonical-name delegate).
    /// </para>
    /// </remarks>
    public static bool SourceFileMayBeEmitted(SourceFile? sourceFile, ISourceFileMayBeEmittedHost? host, bool? forceDtsEmit = null)
    {
        var file = sourceFile!;
        var emitHost = host!;
        var options = emitHost.GetCompilerOptions();
        // Js files are emitted only if option is enabled
        if (Js.IsTruthy(options.NoEmitForJsFiles) && IsSourceFileJS(file)) return false;
        // Declaration files are not emitted
        if (file.IsDeclarationFile) return false;
        // Source file from node_modules are not emitted
        if (emitHost.IsSourceFileFromExternalLibrary(file)) return false;
        // forcing dts emit => file needs to be emitted
        if (Js.IsTruthy(forceDtsEmit)) return true;
        // Check other conditions for file emit
        // Source files from referenced projects are not emitted
        if (emitHost.IsSourceOfProjectReferenceRedirect(file.FileName)) return false;
        // Any non json file should be emitted
        if (!IsJsonSourceFile(file)) return true;
        if (emitHost.GetRedirectFromSourceFile(file.FileName) is not null) return false;
        // Emit json file if outFile is specified
        if (options.OutFile is not null) return true;
        // Json file is not emitted if outDir is not specified
        if (options.OutDir is null) return false;
        // Otherwise if rootDir or composite config file, we know common sourceDir and can check if file would be emitted in same location
        if (options.RootDir is not null || options.ConfigFilePath is not null)
        {
            // PORT-NOTE: the generated host slot is `object?` (the delegate type is declared outside the extracted
            // sources), so the canonical-name delegate is recovered by a cast.
            var getCanonicalFileName = (Core.GetCanonicalFileName)emitHost.GetCanonicalFileName!;
            var currentDirectory = emitHost.GetCurrentDirectory();
            var commonDir = Paths.GetNormalizedAbsolutePath(
                Program.GetCommonSourceDirectoryFromOptions(options, () => [], currentDirectory, getCanonicalFileName),
                currentDirectory);
            var outputPath = GetSourceFilePathInNewDirWorker(
                file.FileName ?? "",
                options.OutDir!,
                currentDirectory,
                commonDir,
                getCanonicalFileName);
            if (Paths.ComparePaths(file.FileName ?? "", outputPath, currentDirectory, !emitHost.UseCaseSensitiveFileNames()) == Comparison.EqualTo) return false;
        }

        return true;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6695-6700 — <c>getSourceFilePathInNewDirWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>indexOf</c> is ordinal, so the port's <see cref="string.IndexOf(string, StringComparison)"/>
    /// is called with <see cref="StringComparison.Ordinal"/>; an empty common directory therefore matches at 0 here
    /// too, exactly as the reference's string search does.
    /// </remarks>
    private static string GetSourceFilePathInNewDirWorker(
        string fileName,
        string newDirPath,
        string currentDirectory,
        string commonSourceDirectory,
        Core.GetCanonicalFileName getCanonicalFileName)
    {
        var sourceFilePath = Paths.GetNormalizedAbsolutePath(fileName, currentDirectory);
        var isSourceFileInCommonSourceDirectory =
            getCanonicalFileName(sourceFilePath).IndexOf(getCanonicalFileName(commonSourceDirectory), StringComparison.Ordinal) == 0;
        sourceFilePath = isSourceFileInCommonSourceDirectory ? sourceFilePath.Substring(commonSourceDirectory.Length) : sourceFilePath;
        return Paths.CombinePaths(newDirPath, sourceFilePath);
    }
}
