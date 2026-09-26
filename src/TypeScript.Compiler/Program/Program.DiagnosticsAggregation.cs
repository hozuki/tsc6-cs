using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:2778-2793 (getDiagnosticsHelper)
// ported from: src/compiler/program.ts:2794-2797 (getSyntacticDiagnostics)
// ported from: src/compiler/program.ts:2798-2805 (getSemanticDiagnostics)
// ported from: src/compiler/program.ts:2806-2809 (getCachedSemanticDiagnostics)
// ported from: src/compiler/program.ts:2810-2813 (getBindAndCheckDiagnostics)
// ported from: src/compiler/program.ts:2814-2826 (getProgramDiagnostics)
// ported from: src/compiler/program.ts:2831-2842 (getSyntacticDiagnosticsForFile)
// ported from: src/compiler/program.ts:2843-2857 (runWithCancellationToken)
// ported from: src/compiler/program.ts:2858-2868 (getSemanticDiagnosticsForFile)
// ported from: src/compiler/program.ts:2869-2886 (getBindAndCheckDiagnosticsForFile)
// ported from: src/compiler/program.ts:2887-2922 (getBindAndCheckDiagnosticsForFileNoCache)
// ported from: src/compiler/program.ts:2923-2946 (getMergedBindAndCheckDiagnostics)
// ported from: src/compiler/program.ts:2947-2955 (getDiagnosticsWithPrecedingDirectives)
// ported from: src/compiler/program.ts:2956-2964 (getSuggestionDiagnostics)
// ported from: src/compiler/program.ts:2965-2991 (markPrecedingCommentDirectiveLine)
// ported from: src/compiler/program.ts:2992-3243 (getJSSyntacticDiagnosticsForFile)
// ported from: src/compiler/program.ts:3267-3273 (getOptionsDiagnostics)
// ported from: src/compiler/program.ts:3274-3282 (getOptionsDiagnosticsOfConfigFile)
// ported from: src/compiler/program.ts:3283-3286 (getGlobalDiagnostics)
// ported from: src/compiler/program.ts:3287-3290 (getConfigFileParsingDiagnostics)
// ported from: src/compiler/utilities.ts:1209-1237 (createCommentDirectivesMap; in the Utilities partial at the end)
// ported from: src/compiler/types.ts:4488-4491 (CommentDirectivesMap; in the Utilities partial at the end)
// ported from: src/compiler/factory/utilitiesPublic.ts:43-52 (canHaveDecorators; declared here, see the member)
// ported from: src/compiler/utilitiesPublic.ts:1647-1649 (isModifier; declared here, see the member)
// ported from: src/compiler/core.ts:166-175, 198-206 (find/findIndex over the modifier arrays; see FindModifier)
// ported from: src/compiler/utilities.ts:10397-10416 (skipTypeChecking, as this file calls it; see SkipTypeChecking)
/// <summary>
/// The diagnostic aggregation half of <c>program.ts</c>: the <c>Program</c> interface's <c>get*Diagnostics</c> family,
/// the per-file workers they sit on (<c>getSyntacticDiagnosticsForFile</c>, <c>getSemanticDiagnosticsForFile</c>, the
/// bind-and-check cache and the comment-directive suppression pass) and the JavaScript-file syntactic pass.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS's closure functions answer <c>readonly Diagnostic[]</c>/<c>readonly DiagnosticWithLocation[]</c> — one
/// array type each; the port has the generated interfaces (<see cref="IDiagnostic"/>,
/// <see cref="IDiagnosticWithLocation"/>) and <see cref="Diagnostic"/>, the one class that implements both
/// (<c>Core/Diagnostic.cs</c>). The members here are spelled with the *interface* element types, which is what the
/// state file's cache (<c>_cachedBindAndCheckDiagnosticsForFile</c>,
/// <c>Program.State.cs:1540</c>) and the generated <c>SourceFile</c> slots (<c>bindDiagnostics</c>,
/// <c>jsDocDiagnostics</c>, <c>parseDiagnostics</c>) carry, so the two sources merge without a copy. The two members
/// that answer <c>List&lt;Diagnostic&gt;</c> — <see cref="Utilities.SortAndDeduplicateDiagnostics"/> and
/// <see cref="Utilities.DiagnosticCollection"/> — force a widening copy at exactly those boundaries; each carries its
/// own PORT-NOTE. <c>C#</c>'s <c>List&lt;T&gt;</c> is invariant, which is why the conversions exist at all: TS's
/// <c>DiagnosticWithLocation[]</c> is assignable to <c>Diagnostic[]</c> with no copy, <c>List&lt;IDiagnosticWithLocation&gt;</c>
/// is not assignable to <c>List&lt;IDiagnostic&gt;</c>.
/// </para>
/// <para>
/// PORT-NOTE: the nine members the generated <c>IProgram</c> interface declares (<c>GetSyntacticDiagnostics</c>,
/// <c>GetSemanticDiagnostics</c>, <c>GetCachedSemanticDiagnostics</c>, <c>GetBindAndCheckDiagnostics</c>,
/// <c>GetProgramDiagnostics</c>, <c>GetSuggestionDiagnostics</c>, <c>GetOptionsDiagnostics</c>,
/// <c>GetGlobalDiagnostics</c>, <c>GetConfigFileParsingDiagnostics</c>) are implemented here, implicitly, with the
/// interface's own parameter lists and answers; no other file of the <c>Program</c> class declares them. The
/// reference's own declarations are the closure functions with narrower parameter lists (<c>sourceFile</c> is
/// non-optional, <c>getOptionsDiagnostics</c>/<c>getGlobalDiagnostics</c> take no token); each member's remarks name
/// what the interface adds.
/// </para>
/// </remarks>
internal sealed partial class Program
{
    // ---------------------------------------------------------------------------------------------
    // The aggregation entry points (program.ts:2778-2813)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2778-2793 — <c>getDiagnosticsHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>T extends Diagnostic</c> exists so the helper can hand <c>sortAndDeduplicateDiagnostics</c>
    /// (<c>utilitiesPublic.ts:303-305</c>) an array of a narrower diagnostic type; the port's
    /// <see cref="Utilities.SortAndDeduplicateDiagnostics"/> is fixed to <see cref="Diagnostic"/> (its own PORT-NOTE,
    /// <c>Utilities.Diagnostics.cs:188-195</c>), so the element type is kept generic here and the array is narrowed
    /// once with <see cref="ToDiagnosticList"/>, the same bridge <c>Parser/JSDocParser.cs:194</c> uses. The
    /// <c>class</c> constraint is C#'s, not the reference's: the callers' lists are read through the covariant
    /// <c>IEnumerable&lt;IDiagnostic&gt;</c> (<c>List&lt;IDiagnosticWithLocation&gt;</c> is not a
    /// <c>List&lt;IDiagnostic&gt;</c>), and variance needs a reference element type — an unconstrained <c>T</c> could
    /// be an interface-implementing struct.
    /// <para>
    /// PORT-NOTE: TS reads <c>program.getSourceFiles()</c>; the port reads the state file's <c>_files</c>, which is
    /// exactly what that accessor answers (<c>program.ts:1880</c>: <c>getSourceFiles: () =&gt; files</c>).
    /// </para>
    /// </remarks>
    private List<Diagnostic> GetDiagnosticsHelper<T>(
        SourceFile? sourceFile,
        Func<SourceFile, ICancellationToken?, IList<T>> getDiagnostics,
        ICancellationToken? cancellationToken)
        where T : class, IDiagnostic
    {
        if (sourceFile is not null)
        {
            return Utilities.SortAndDeduplicateDiagnostics(ToDiagnosticList(getDiagnostics(sourceFile, cancellationToken)));
        }

        return Utilities.SortAndDeduplicateDiagnostics(ToDiagnosticList(_files.SelectMany(file =>
        {
            if (cancellationToken is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return getDiagnostics(file, cancellationToken);
        })));
    }

    /// <summary>ported from: src/compiler/program.ts:2794-2797 — <c>getSyntacticDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS's closure function and the generated interface declare the same two parameters; the
    /// reference's answer is <c>readonly DiagnosticWithLocation[]</c>, the interface's (and this member's) answer is
    /// the <c>List&lt;IDiagnosticWithLocation&gt;</c> the generated model gives that type, so the sorted
    /// <c>List&lt;Diagnostic&gt;</c> is widened with a copy.</remarks>
    public List<IDiagnosticWithLocation> GetSyntacticDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => [.. GetDiagnosticsHelper(sourceFile, (file, _) => GetSyntacticDiagnosticsForFile(file), cancellationToken)];

    /// <summary>ported from: src/compiler/program.ts:2798-2805 — <c>getSemanticDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS's declaration has the third <c>nodesToCheck</c> parameter; the generated interface does
    /// not, so this member is the interface's shape and forwards to the reference's three-parameter body below. The
    /// answer is widened from the sorted <c>List&lt;Diagnostic&gt;</c> to the interface's
    /// <c>List&lt;IDiagnostic&gt;</c>, as <see cref="GetSyntacticDiagnostics"/> does.</remarks>
    public List<IDiagnostic> GetSemanticDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => [.. GetSemanticDiagnostics(sourceFile, cancellationToken, /*nodesToCheck*/ null)];

    /// <summary>ported from: src/compiler/program.ts:2798-2805 — the three-parameter body of
    /// <c>getSemanticDiagnostics</c>.</summary>
    internal List<Diagnostic> GetSemanticDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken, List<Node>? nodesToCheck)
        => GetDiagnosticsHelper(sourceFile, (file, token) => GetSemanticDiagnosticsForFile(file, token, nodesToCheck), cancellationToken);

    /// <summary>ported from: src/compiler/program.ts:2806-2809 — <c>getCachedSemanticDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: the generated interface widens TS's non-optional <c>sourceFile</c> to <c>SourceFile?</c>;
    /// a missing file has no cache entry and answers <c>null</c>, the port's <c>undefined</c>.</remarks>
    public List<IDiagnostic>? GetCachedSemanticDiagnostics(SourceFile? sourceFile)
        => sourceFile is not null
            && _cachedBindAndCheckDiagnosticsForFile is not null
            && _cachedBindAndCheckDiagnosticsForFile.TryGetValue(sourceFile.Path ?? "", out var cached)
                ? cached
                : null;

    /// <summary>ported from: src/compiler/program.ts:2810-2813 — <c>getBindAndCheckDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: the generated interface widens TS's non-optional <c>sourceFile</c> to <c>SourceFile?</c>;
    /// the reference's body forwards that file to <c>getBindAndCheckDiagnosticsForFile</c>, which needs it, so the
    /// member states the reference's expectation with an assert.</remarks>
    public List<IDiagnostic> GetBindAndCheckDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
    {
        Debug.AssertIsDefined(sourceFile, "`getBindAndCheckDiagnostics` needs the file whose diagnostics are asked for.");
        return GetBindAndCheckDiagnosticsForFile(sourceFile!, cancellationToken, /*nodesToCheck*/ null);
    }

    /// <summary>ported from: src/compiler/program.ts:2814-2826 — <c>getProgramDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: the generated interface declares a <c>cancellationToken</c> the reference's closure function
    /// does not take (it reads cached diagnostics only); the parameter is unused, as in the interface's declaration of
    /// <c>getGlobalDiagnostics</c>/<c>getOptionsDiagnostics</c>. <c>sourceFile</c> is widened to <c>SourceFile?</c> for
    /// the same reason <see cref="GetBindAndCheckDiagnostics"/>'s is.
    /// <para>
    /// PORT-NOTE: the combined collection answers <c>List&lt;Diagnostic&gt;</c>
    /// (<c>Program/Program.ProgramDiagnostics.cs</c>) while this member answers the interface's
    /// <c>List&lt;IDiagnostic&gt;</c>; the early return widens with a copy.
    /// </para>
    /// </remarks>
    public List<IDiagnostic> GetProgramDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
    {
        Debug.AssertIsDefined(sourceFile, "`getProgramDiagnostics` needs the file whose diagnostics are asked for.");

        if (SkipTypeChecking(sourceFile!))
        {
            return [];
        }

        var programDiagnosticsInFile = _programDiagnostics.GetCombinedDiagnostics(this).GetDiagnostics(sourceFile!.FileName);
        var commentDirectives = sourceFile.CommentDirectives;
        if (commentDirectives is not { Count: > 0 })
        {
            return [.. programDiagnosticsInFile];
        }

        return GetDiagnosticsWithPrecedingDirectives(sourceFile, commentDirectives, programDiagnosticsInFile).Diagnostics;
    }

    // ---------------------------------------------------------------------------------------------
    // The per-file workers (program.ts:2831-2955)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2831-2842 — <c>getSyntacticDiagnosticsForFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference calls <c>isSourceFileJS(sourceFile)</c>, whose body is <c>isInJSFile(file)</c>
    /// (<c>utilities.ts:3799-3801</c>); the port has the latter (<c>Utilities.IsInJSFile</c>), so the test is that
    /// member.
    /// <para>
    /// PORT-NOTE: TS's <c>parseDiagnostics</c>/<c>additionalSyntacticDiagnostics</c> are declared non-optional, but the
    /// generated <c>SourceFile</c> slots (<c>Model/NodeClasses.cs:2130,2134</c>) are nullable like every reference
    /// member, so they are read with <c>?? []</c>. The reference's <c>concatenate</c> answers the first array when the
    /// second is empty, which <c>Core.Concatenate</c> also does (its own note, <c>Core/Core.Arrays.cs:706-716</c>).
    /// </para>
    /// </remarks>
    private IList<IDiagnosticWithLocation> GetSyntacticDiagnosticsForFile(SourceFile sourceFile)
    {
        // For JavaScript files, we report semantic errors for using TypeScript-only
        // constructs from within a JavaScript file as syntactic errors.
        if (Utilities.IsInJSFile(sourceFile))
        {
            var additionalSyntacticDiagnostics = sourceFile.AdditionalSyntacticDiagnostics
                ??= GetJSSyntacticDiagnosticsForFile(sourceFile);
            return Core.Concatenate(additionalSyntacticDiagnostics, sourceFile.ParseDiagnostics ?? []) ?? [];
        }

        return sourceFile.ParseDiagnostics ?? [];
    }

    /// <summary>ported from: src/compiler/program.ts:2843-2857 — <c>runWithCancellationToken</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS catches every exception and tests <c>e instanceof OperationCanceledException</c> — the empty
    /// class <c>types.ts:4586</c> its cancellation tokens throw. The port has no such class: its
    /// <see cref="ICancellationToken"/> is a .NET-shaped interface and the token implementations (none has landed
    /// yet) are what choose the exception, so the catch filters on <see cref="System.OperationCanceledException"/>,
    /// the exception <c>CancellationToken.ThrowIfCancellationRequested</c> throws. A different exception type in a
    /// future token implementation would not reset <c>_typeChecker</c>.
    /// <para>
    /// PORT-NOTE: TS rethrows the caught object (<c>throw e</c>); the port rethrows with <c>throw;</c> so the original
    /// stack survives — <c>throw e</c> inside a catch is CA2200, which this project's analyzers report as an error.
    /// </para>
    /// </remarks>
    private T RunWithCancellationToken<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (System.OperationCanceledException)
        {
            // We were canceled while performing the operation.  Because our type checker
            // might be a bad state, we need to throw it away.
            _typeChecker = null;
            throw;
        }
    }

    /// <summary>ported from: src/compiler/program.ts:2858-2868 — <c>getSemanticDiagnosticsForFile</c>.</summary>
    /// <remarks>PORT-NOTE: <c>Utilities.FilterSemanticDiagnostics</c> answers the same
    /// <c>List&lt;IDiagnostic&gt;</c> this member's inputs and output use, and the concatenation is TS's
    /// <c>concatenate</c> (see <see cref="GetSyntacticDiagnosticsForFile"/>).</remarks>
    private IList<IDiagnostic> GetSemanticDiagnosticsForFile(SourceFile sourceFile, ICancellationToken? cancellationToken, List<Node>? nodesToCheck)
        => Core.Concatenate(
            FilterSemanticDiagnostics(GetBindAndCheckDiagnosticsForFile(sourceFile, cancellationToken, nodesToCheck), _options),
            GetProgramDiagnostics(sourceFile, cancellationToken))!;

    /// <summary>ported from: src/compiler/program.ts:2869-2886 — <c>getBindAndCheckDiagnosticsForFile</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>let result = cached…?.get(path); if (!result)</c> treats a cached empty array as
    /// present (a JS array is truthy) and only <c>undefined</c> as absent, which is what <c>TryGetValue</c> tests. The
    /// reference also assigns the freshly computed array inside the <c>set</c> call; the port computes it first and
    /// stores it, which is the same order because nothing reads the cache in between.</remarks>
    private List<IDiagnostic> GetBindAndCheckDiagnosticsForFile(SourceFile sourceFile, ICancellationToken? cancellationToken, List<Node>? nodesToCheck)
    {
        if (nodesToCheck is not null)
        {
            return GetBindAndCheckDiagnosticsForFileNoCache(sourceFile, cancellationToken, nodesToCheck);
        }

        if (_cachedBindAndCheckDiagnosticsForFile is not null
            && _cachedBindAndCheckDiagnosticsForFile.TryGetValue(sourceFile.Path ?? "", out var cached))
        {
            return cached;
        }

        var result = GetBindAndCheckDiagnosticsForFileNoCache(sourceFile, cancellationToken);
        _cachedBindAndCheckDiagnosticsForFile ??= [];
        _cachedBindAndCheckDiagnosticsForFile[sourceFile.Path ?? ""] = result;
        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:2887-2922 — <c>getBindAndCheckDiagnosticsForFileNoCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>bindDiagnostics</c>/<c>checkDiagnostics</c> locals are both <c>readonly Diagnostic[]</c>;
    /// their sources are a <c>DiagnosticWithLocation[]</c> (the binder's) and a <c>Diagnostic[]</c> (the checker's), so
    /// the port keeps each at its source's element type (<c>IList&lt;IDiagnosticWithLocation&gt;</c>/
    /// <c>IList&lt;IDiagnostic&gt;</c>) and lets <see cref="GetMergedBindAndCheckDiagnostics"/> read them through the
    /// covariant <c>IEnumerable&lt;IDiagnostic&gt;</c> view. <c>filter</c> is <c>Core.Filter</c>, whose answer type is
    /// the input's (it hands the input back when nothing was filtered, <c>Core/Core.Arrays.cs:285-300</c>).
    /// <para>
    /// PORT-NOTE: the reference's <c>plainJSErrors.has(d.code)</c> is <c>Utilities.PlainJSErrors.Contains(d.Code)</c>
    /// (<c>program.ts:1370-1467</c>, <c>Program/Program.ReferenceTables.cs</c>).
    /// </para>
    /// </remarks>
    private List<IDiagnostic> GetBindAndCheckDiagnosticsForFileNoCache(SourceFile sourceFile, ICancellationToken? cancellationToken, List<Node>? nodesToCheck = null)
        => RunWithCancellationToken(() =>
        {
            if (SkipTypeChecking(sourceFile))
            {
                return [];
            }

            var typeChecker = GetTypeChecker();

            Debug.Assert(sourceFile.BindDiagnostics is not null);

            var isJs = sourceFile.ScriptKind is ScriptKind.JS or ScriptKind.JSX;
            var isPlainJs = Utilities.IsPlainJsFile(sourceFile, _options.CheckJs);
            var isCheckJs = isJs && Js.IsTruthy(Utilities.IsCheckJsEnabledForFile(sourceFile, _options));

            IList<IDiagnosticWithLocation> bindDiagnostics = sourceFile.BindDiagnostics!;
            IList<IDiagnostic> checkDiagnostics = typeChecker.GetDiagnostics(sourceFile, cancellationToken, nodesToCheck);
            if (isPlainJs)
            {
                bindDiagnostics = Core.Filter(bindDiagnostics, d => Utilities.PlainJSErrors.Contains(d.Code))!;
                checkDiagnostics = Core.Filter(checkDiagnostics, d => Utilities.PlainJSErrors.Contains(d.Code))!;
            }

            // skip ts-expect-error errors in plain JS files, and skip JSDoc errors except in checked JS
            return GetMergedBindAndCheckDiagnostics(
                sourceFile,
                !isPlainJs,
                nodesToCheck is not null,
                bindDiagnostics,
                checkDiagnostics,
                isCheckJs ? sourceFile.JsDocDiagnostics : null);
        });

    /// <summary>ported from: src/compiler/program.ts:2923-2946 — <c>getMergedBindAndCheckDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's rest parameter is <c>(readonly Diagnostic[] | undefined)[]</c> and <c>flatten</c> also accepts a
    /// non-array element; every call site passes arrays or <c>undefined</c>, so the port's parameter is the array of
    /// optional sequences the call sites need and the flatten is its loop, without the boxed list
    /// <c>Core.Flatten</c> takes (<c>Core/Core.Arrays.cs:388</c>).
    /// </remarks>
    private List<IDiagnostic> GetMergedBindAndCheckDiagnostics(
        SourceFile sourceFile,
        bool includeBindAndCheckDiagnostics,
        bool partialCheck,
        params IEnumerable<IDiagnostic>?[] allDiagnostics)
    {
        List<IDiagnostic> flatDiagnostics = [];
        foreach (var diagnostics in allDiagnostics)
        {
            if (diagnostics is not null)
            {
                flatDiagnostics.AddRange(diagnostics);
            }
        }

        var commentDirectives = sourceFile.CommentDirectives;
        if (!includeBindAndCheckDiagnostics || commentDirectives is not { Count: > 0 })
        {
            return flatDiagnostics;
        }

        var (diagnosticsList, directives) = GetDiagnosticsWithPrecedingDirectives(sourceFile, commentDirectives, flatDiagnostics);

        // When doing a partial check, we can't be sure a directive is unused.
        if (partialCheck)
        {
            return diagnosticsList;
        }

        foreach (var errorExpectation in directives.GetUnusedExpectations())
        {
            diagnosticsList.Add(Utilities.CreateDiagnosticForRange(
                sourceFile,
                (TextRange)errorExpectation.Range!,
                Diagnostics.Unused_ts_expect_error_directive));
        }

        return diagnosticsList;
    }

    /// <summary>ported from: src/compiler/program.ts:2947-2955 — <c>getDiagnosticsWithPrecedingDirectives</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns the object literal <c>{ diagnostics, directives }</c>; the port answers the named tuple
    /// the parser's similar returns use (<c>Parser/Parser.Entry.cs:266</c>). The filter's input is
    /// <c>IEnumerable&lt;IDiagnostic&gt;</c> rather than an <c>IList</c> because the two call sites hand in lists of
    /// different (widening) element types — the combined collection's and the flatten's — so the filter is the loop
    /// <c>Core.Filter</c> would run.
    /// <para>
    /// PORT-NOTE: the element's <c>range</c> is read as the concrete <see cref="TextRange"/> the port's
    /// <see cref="CommentDirective"/> carries; the generated <c>ICommentDirective</c> widens the slot to
    /// <c>ITextRange?</c> (<c>Core/CommentDirective.cs</c>).
    /// </para>
    /// </remarks>
    private (List<IDiagnostic> Diagnostics, ICommentDirectivesMap Directives) GetDiagnosticsWithPrecedingDirectives(
        SourceFile sourceFile,
        List<ICommentDirective> commentDirectives,
        IEnumerable<IDiagnostic> flatDiagnostics)
    {
        // Diagnostics are only reported if there is no comment directive preceding them
        // This will modify the directives map by marking "used" ones with a corresponding diagnostic
        var directives = Utilities.CreateCommentDirectivesMap(sourceFile, commentDirectives);
        List<IDiagnostic> diagnostics = [];
        foreach (var diagnostic in flatDiagnostics)
        {
            if (MarkPrecedingCommentDirectiveLine(diagnostic, directives) == -1)
            {
                diagnostics.Add(diagnostic);
            }
        }

        return (diagnostics, directives);
    }

    // ---------------------------------------------------------------------------------------------
    // Suggestion diagnostics and the comment-directive pass (program.ts:2956-2991)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2956-2964 — <c>getSuggestionDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares both parameters non-optional; the generated interface widens them to
    /// <c>SourceFile?</c>/<c>ICancellationToken?</c>, which the checker's own member accepts, so they are forwarded
    /// as they arrive.</remarks>
    public List<IDiagnosticWithLocation> GetSuggestionDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken)
        => RunWithCancellationToken(() => GetTypeChecker().GetSuggestionDiagnostics(sourceFile, cancellationToken));

    /// <summary>ported from: src/compiler/program.ts:2965-2991 — <c>markPrecedingCommentDirectiveLine</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>diagnostic.start!</c> is an asserted position; the generated <c>IDiagnostic.Start</c> is
    /// <c>int?</c>, so the port reads <c>.Value</c> after the same assertion. <c>lineStarts[line + 1]</c> is
    /// <c>undefined</c> for the last line and <c>String.prototype.slice(start, undefined)</c> then runs to the end of
    /// the text; the port clamps to the text length. The regex literal is a <c>[GeneratedRegex]</c> partial method
    /// (<c>docs/architecture.md</c> §9.7).
    /// <para>
    /// PORT-NOTE: <c>getLineStarts</c> (scanner.ts:502-505) is inlined — it is the memo
    /// <c>Core/Tracing.cs:405</c> also writes inline. <c>ScannerUtilities.GetLineStarts</c> takes an
    /// <c>ISourceFileLike</c>, which no ported type implements, and the two <c>SourceFile</c>-shaped restatements in
    /// the port (<c>Utilities/Utilities.Binder.Diagnostics.cs:366-379</c>, <c>Program/Program.Host.cs</c>) are private
    /// to their own classes, so a <c>Program</c> member cannot reach either.
    /// </para>
    /// </remarks>
    private static int MarkPrecedingCommentDirectiveLine(IDiagnostic diagnostic, ICommentDirectivesMap directives)
    {
        var file = diagnostic.File;
        if (file is null)
        {
            return -1;
        }

        // Start out with the line just before the text
        var lineStarts = file.LineMap ?? (file.LineMap = ScannerUtilities.ComputeLineStarts(file.Text ?? ""));
        var text = file.Text ?? "";
        var line = ScannerUtilities.ComputeLineAndCharacterOfPosition(lineStarts, diagnostic.Start!.Value).Line - 1;
        while (line >= 0)
        {
            // As soon as that line is known to have a comment directive, use that
            if (directives.MarkUsed(line))
            {
                return line;
            }

            // Stop searching if the line is not empty and not a comment
            var lineEnd = line < lineStarts.Count - 1 ? lineStarts[line + 1] : text.Length;
            var lineText = text[lineStarts[line]..lineEnd].Trim();
            if (lineText != "" && !SingleLineCommentRegEx().IsMatch(lineText))
            {
                return -1;
            }

            line--;
        }

        return -1;
    }

    /// <summary>ported from: src/compiler/program.ts:2984 — the <c>/^\s*\/\/.*$/</c> literal of
    /// <c>markPrecedingCommentDirectiveLine</c>.</summary>
    [GeneratedRegex(@"^\s*\/\/.*$")]
    private static partial Regex SingleLineCommentRegEx();

    // ---------------------------------------------------------------------------------------------
    // The JavaScript-file syntactic pass (program.ts:2992-3243)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:2992-3243 — <c>getJSSyntacticDiagnosticsForFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reaches <c>parent.modifiers</c>, <c>parent.typeParameters</c>, <c>parent.typeArguments</c>,
    /// <c>parent.questionToken</c>, <c>parent.type</c> and <c>node.body</c> through the <c>HasIllegalDecorators</c>,
    /// <c>HasDecorators</c>, <c>DeclarationWithTypeParameterChildren</c>, <c>NodeWithTypeArguments</c>,
    /// <c>FunctionLikeDeclaration</c> and <c>VariableDeclaration</c> unions. The generated model declares no
    /// <c>HasIllegalDecorators</c>/<c>HasDecorators</c> interface and only some of those slots on capability
    /// interfaces, so the five accesses are the small switch accessors at the bottom of this file
    /// (<see cref="ModifiersOf"/>, <c>Utilities.TypeParametersOf</c>, <see cref="TypeArgumentsOf"/>,
    /// <see cref="QuestionTokenOf"/>, <see cref="TypeOf"/>) — each names the union the reference reads. <c>body</c> is
    /// on the ported <see cref="IFunctionLikeDeclarationBase"/>, which all three kinds implement.
    /// <para>
    /// PORT-NOTE: TS's first switch falls through from the question-token group into the type-annotation group, and the
    /// second falls through from the type-parameter group into the variable-statement group; the port states each
    /// fall-through as the two tests it is (the groups share no body, only the second group's guard). The nested
    /// functions are the port's local functions, as in <c>Utilities.SetParentRecursive</c>
    /// (<c>Utilities/Utilities.Parser.cs:625-690</c>), and they answer <c>ForEachChildTable.Skip</c> for TS's
    /// <c>"skip"</c> string.
    /// </para>
    /// </remarks>
    private List<IDiagnosticWithLocation> GetJSSyntacticDiagnosticsForFile(SourceFile sourceFile)
        => RunWithCancellationToken(() =>
        {
            List<IDiagnosticWithLocation> diagnostics = [];
            Walk(sourceFile, sourceFile);
            ForEachChildTable.ForEachChildRecursively(sourceFile, Walk, WalkArray);

            return diagnostics;

            object? Walk(Node node, Node parent)
            {
                // Return directly from the case if the given node doesnt want to visit each child
                // Otherwise break to visit each child

                var isQuestionTokenOwner = parent.Kind is SyntaxKind.Parameter or SyntaxKind.PropertyDeclaration or SyntaxKind.MethodDeclaration;
                if (isQuestionTokenOwner && ReferenceEquals(QuestionTokenOf(parent), node))
                {
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.The_0_modifier_can_only_be_used_in_TypeScript_files, "?"));
                    return ForEachChildTable.Skip;
                }

                // falls through (the reference's first three cases continue into this group)
                if ((isQuestionTokenOwner
                        || parent.Kind is SyntaxKind.MethodSignature or SyntaxKind.Constructor or SyntaxKind.GetAccessor
                            or SyntaxKind.SetAccessor or SyntaxKind.FunctionExpression or SyntaxKind.FunctionDeclaration
                            or SyntaxKind.ArrowFunction or SyntaxKind.VariableDeclaration)
                    && ReferenceEquals(TypeOf(parent), node))
                {
                    // type annotation
                    diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Type_annotations_can_only_be_used_in_TypeScript_files));
                    return ForEachChildTable.Skip;
                }

                switch (node.Kind)
                {
                    case SyntaxKind.ImportClause when ((ImportClause)node).IsTypeOnly:
                        diagnostics.Add(CreateDiagnosticForNode(parent, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, "import type"));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.ExportDeclaration when ((ExportDeclaration)node).IsTypeOnly:
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, "export type"));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.ImportSpecifier when ((ImportSpecifier)node).IsTypeOnly:
                    case SyntaxKind.ExportSpecifier when ((ExportSpecifier)node).IsTypeOnly:
                        diagnostics.Add(CreateDiagnosticForNode(
                            node,
                            Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files,
                            NodeTests.IsImportSpecifier(node) ? "import...type" : "export...type"));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.ImportEqualsDeclaration:
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.import_can_only_be_used_in_TypeScript_files));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.ExportAssignment when ((ExportAssignment)node).IsExportEquals is true:
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.export_can_only_be_used_in_TypeScript_files));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.HeritageClause when ((HeritageClause)node).Token == SyntaxKind.ImplementsKeyword:
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.implements_clauses_can_only_be_used_in_TypeScript_files));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.InterfaceDeclaration:
                        var interfaceKeyword = ScannerUtilities.TokenToString(SyntaxKind.InterfaceKeyword);
                        Debug.AssertIsDefined(interfaceKeyword);
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, interfaceKeyword));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.ModuleDeclaration:
                        var moduleKeyword = (node.Flags & NodeFlags.Namespace) != 0
                            ? ScannerUtilities.TokenToString(SyntaxKind.NamespaceKeyword)
                            : ScannerUtilities.TokenToString(SyntaxKind.ModuleKeyword);
                        Debug.AssertIsDefined(moduleKeyword);
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, moduleKeyword));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.TypeAliasDeclaration:
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Type_aliases_can_only_be_used_in_TypeScript_files));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.Constructor:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.FunctionDeclaration:
                        if (((IFunctionLikeDeclarationBase)node).Body is null)
                        {
                            diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Signature_declarations_can_only_be_used_in_TypeScript_files));
                            return ForEachChildTable.Skip;
                        }

                        return null;
                    case SyntaxKind.EnumDeclaration:
                        var enumKeyword = Debug.CheckDefined(ScannerUtilities.TokenToString(SyntaxKind.EnumKeyword));
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics._0_declarations_can_only_be_used_in_TypeScript_files, enumKeyword));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.NonNullExpression:
                        diagnostics.Add(CreateDiagnosticForNode(node, Diagnostics.Non_null_assertions_can_only_be_used_in_TypeScript_files));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.AsExpression:
                        diagnostics.Add(CreateDiagnosticForNode((Node)((AsExpression)node).Type!, Diagnostics.Type_assertion_expressions_can_only_be_used_in_TypeScript_files));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.SatisfiesExpression:
                        diagnostics.Add(CreateDiagnosticForNode((Node)((SatisfiesExpression)node).Type!, Diagnostics.Type_satisfaction_expressions_can_only_be_used_in_TypeScript_files));
                        return ForEachChildTable.Skip;
                    case SyntaxKind.TypeAssertionExpression:
                        Debug.Fail(); // Won't parse these in a JS file anyway, as they are interpreted as JSX.
                        break;
                }

                return null;
            }

            object? WalkArray(INodeArray<INode> nodes, Node parent)
            {
                var modifiers = ModifiersOf(parent);
                if (FactoryUtilities.CanHaveIllegalDecorators(parent))
                {
                    var decorator = FindModifier(modifiers, NodeTests.IsDecorator);
                    if (decorator is not null)
                    {
                        // report illegal decorator
                        diagnostics.Add(CreateDiagnosticForNode(decorator, Diagnostics.Decorators_are_not_valid_here));
                    }
                }
                else if (Utilities.CanHaveDecorators(parent) && modifiers is not null)
                {
                    var decoratorIndex = FindModifierIndex(modifiers, NodeTests.IsDecorator);
                    if (decoratorIndex >= 0)
                    {
                        if (NodeTests.IsParameter(parent) && !Js.IsTruthy(_options.ExperimentalDecorators))
                        {
                            // report illegall decorator on parameter
                            diagnostics.Add(CreateDiagnosticForNode(modifiers[decoratorIndex], Diagnostics.Decorators_are_not_valid_here));
                        }
                        else if (NodeTests.IsClassDeclaration(parent))
                        {
                            var exportIndex = FindModifierIndex(modifiers, NodeTests.IsExportModifier);
                            if (exportIndex >= 0)
                            {
                                var defaultIndex = FindModifierIndex(modifiers, NodeTests.IsDefaultModifier);
                                if (decoratorIndex > exportIndex && defaultIndex >= 0 && decoratorIndex < defaultIndex)
                                {
                                    // report illegal decorator between `export` and `default`
                                    diagnostics.Add(CreateDiagnosticForNode(modifiers[decoratorIndex], Diagnostics.Decorators_are_not_valid_here));
                                }
                                else if (exportIndex >= 0 && decoratorIndex < exportIndex)
                                {
                                    var trailingDecoratorIndex = FindModifierIndex(modifiers, NodeTests.IsDecorator, exportIndex);
                                    if (trailingDecoratorIndex >= 0)
                                    {
                                        diagnostics.Add(Utilities.AddRelatedInfo(
                                            CreateDiagnosticForNode(modifiers[trailingDecoratorIndex], Diagnostics.Decorators_may_not_appear_after_export_or_export_default_if_they_also_appear_before_export),
                                            CreateDiagnosticForNode(modifiers[decoratorIndex], Diagnostics.Decorator_used_before_export_here)));
                                    }
                                }
                            }
                        }
                    }
                }

                var isTypeParameterOwner = parent.Kind is SyntaxKind.ClassDeclaration or SyntaxKind.ClassExpression
                    or SyntaxKind.MethodDeclaration or SyntaxKind.Constructor or SyntaxKind.GetAccessor or SyntaxKind.SetAccessor
                    or SyntaxKind.FunctionExpression or SyntaxKind.FunctionDeclaration or SyntaxKind.ArrowFunction;
                if (isTypeParameterOwner && ReferenceEquals(nodes, Utilities.TypeParametersOf(parent)))
                {
                    // Check type parameters
                    diagnostics.Add(CreateDiagnosticForNodeArray(nodes, Diagnostics.Type_parameter_declarations_can_only_be_used_in_TypeScript_files));
                    return ForEachChildTable.Skip;
                }

                // falls through (the reference's type-parameter group continues into the variable-statement case)
                if ((isTypeParameterOwner || parent.Kind == SyntaxKind.VariableStatement) && ReferenceEquals(nodes, modifiers))
                {
                    // Check modifiers
                    CheckModifiers(modifiers!, parent.Kind == SyntaxKind.VariableStatement);
                    return ForEachChildTable.Skip;
                }

                switch (parent.Kind)
                {
                    case SyntaxKind.PropertyDeclaration:
                        // Check modifiers of property declaration
                        if (ReferenceEquals(nodes, modifiers))
                        {
                            foreach (var child in nodes)
                            {
                                var modifier = (Node)child;
                                if (IsModifier(modifier)
                                    && modifier.Kind != SyntaxKind.StaticKeyword
                                    && modifier.Kind != SyntaxKind.AccessorKeyword)
                                {
                                    diagnostics.Add(CreateDiagnosticForNode(
                                        modifier,
                                        Diagnostics.The_0_modifier_can_only_be_used_in_TypeScript_files,
                                        ScannerUtilities.TokenToString(modifier.Kind)));
                                }
                            }

                            return ForEachChildTable.Skip;
                        }

                        break;
                    case SyntaxKind.Parameter:
                        // Check modifiers of parameter declaration
                        if (ReferenceEquals(nodes, modifiers) && nodes.Any(child => IsModifier((Node)child)))
                        {
                            diagnostics.Add(CreateDiagnosticForNodeArray(nodes, Diagnostics.Parameter_modifiers_can_only_be_used_in_TypeScript_files));
                            return ForEachChildTable.Skip;
                        }

                        break;
                    case SyntaxKind.CallExpression:
                    case SyntaxKind.NewExpression:
                    case SyntaxKind.ExpressionWithTypeArguments:
                    case SyntaxKind.JsxSelfClosingElement:
                    case SyntaxKind.JsxOpeningElement:
                    case SyntaxKind.TaggedTemplateExpression:
                        // Check type arguments
                        if (ReferenceEquals(nodes, TypeArgumentsOf(parent)))
                        {
                            diagnostics.Add(CreateDiagnosticForNodeArray(nodes, Diagnostics.Type_arguments_can_only_be_used_in_TypeScript_files));
                            return ForEachChildTable.Skip;
                        }

                        break;
                }

                return null;
            }

            void CheckModifiers(IReadOnlyList<Node> modifiers, bool isConstValid)
            {
                foreach (var modifier in modifiers)
                {
                    switch (modifier.Kind)
                    {
                        case SyntaxKind.ConstKeyword when isConstValid:
                            continue;
                        // to report error,
                        // falls through
                        case SyntaxKind.ConstKeyword:
                        case SyntaxKind.PublicKeyword:
                        case SyntaxKind.PrivateKeyword:
                        case SyntaxKind.ProtectedKeyword:
                        case SyntaxKind.ReadonlyKeyword:
                        case SyntaxKind.DeclareKeyword:
                        case SyntaxKind.AbstractKeyword:
                        case SyntaxKind.OverrideKeyword:
                        case SyntaxKind.InKeyword:
                        case SyntaxKind.OutKeyword:
                            diagnostics.Add(CreateDiagnosticForNode(
                                modifier,
                                Diagnostics.The_0_modifier_can_only_be_used_in_TypeScript_files,
                                ScannerUtilities.TokenToString(modifier.Kind)));
                            break;

                        // These are all legal modifiers.
                        case SyntaxKind.StaticKeyword:
                        case SyntaxKind.ExportKeyword:
                        case SyntaxKind.DefaultKeyword:
                        case SyntaxKind.AccessorKeyword:
                            break;
                    }
                }
            }

            IDiagnosticWithLocation CreateDiagnosticForNodeArray(INodeArray<INode> nodes, DiagnosticMessage message, params object?[] args)
            {
                var start = nodes.Pos;
                return Utilities.CreateFileDiagnostic(sourceFile, start, nodes.End - start, message, args);
            }

            // Since these are syntactic diagnostics, parent might not have been set
            // this means the sourceFile cannot be infered from the node
            Diagnostic CreateDiagnosticForNode(Node node, DiagnosticMessage message, params object?[] args)
                => Utilities.CreateDiagnosticForNodeInSourceFile(sourceFile, node, message, args);
        });

    // ---------------------------------------------------------------------------------------------
    // The program-wide diagnostics (program.ts:3267-3290)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:3267-3273 — <c>getOptionsDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS's closure function takes no parameters; the generated interface declares a
    /// <c>cancellationToken</c> it does not use, and the reference's answer type <c>SortedReadonlyArray&lt;Diagnostic&gt;</c>
    /// is the port's sorted <c>List&lt;Diagnostic&gt;</c> widened to the interface's <c>List&lt;IDiagnostic&gt;</c>.
    /// The combined collection's global diagnostics are a <c>List&lt;Diagnostic&gt;</c>
    /// (<c>Program/Program.ProgramDiagnostics.cs</c>) while this file's are <c>List&lt;IDiagnostic&gt;</c>, hence the
    /// one widening copy before the concatenation — TS's <c>concatenate</c> has no such mixed case.</remarks>
    public List<IDiagnostic> GetOptionsDiagnostics(ICancellationToken? cancellationToken)
    {
        var globalDiagnostics = _programDiagnostics.GetCombinedDiagnostics(this).GetGlobalDiagnostics();
        var diagnostics = Core.Concatenate<IDiagnostic>([.. globalDiagnostics], GetOptionsDiagnosticsOfConfigFile())!;
        return [.. Utilities.SortAndDeduplicateDiagnostics(ToDiagnosticList(diagnostics))];
    }

    /// <summary>ported from: src/compiler/program.ts:3274-3282 — <c>getOptionsDiagnosticsOfConfigFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>options.configFile</c> is a <c>TsConfigSourceFile</c>, i.e. a <c>SourceFile</c> with two extra
    /// slots; the generated <c>ITsConfigSourceFile</c> has only its own two slots and the <c>Node</c> face
    /// (<c>Model/NodeInterfaces.cs:3466</c>), so it carries no <c>fileName</c>. The only producer of a config file,
    /// <c>Parser.ParseJsonText</c>, answers a <c>SourceFile</c> (<c>Parser/Parser.Entry.cs:87</c>), which is where the
    /// name is read from.
    /// <para>
    /// <c>forEachResolvedProjectReference</c> is the project-reference slice's member
    /// (<c>Program/Program.ProjectReferences.cs</c>); TS's callback returns <c>void</c>, the port's takes the generic's
    /// <c>T?</c>, hence the explicit type argument and the <c>null</c> answer.
    /// </para>
    /// </remarks>
    private List<IDiagnostic> GetOptionsDiagnosticsOfConfigFile()
    {
        var configFile = (SourceFile?)_options.ConfigFile;
        if (configFile is null)
        {
            return [];
        }

        var diagnostics = _programDiagnostics.GetCombinedDiagnostics(this).GetDiagnostics(configFile.FileName);
        ForEachResolvedProjectReference<object?>(resolvedRef =>
        {
            diagnostics = [.. Core.Concatenate(
                diagnostics,
                _programDiagnostics.GetCombinedDiagnostics(this).GetDiagnostics(resolvedRef.SourceFile!.FileName))!];
            return null;
        });
        return [.. diagnostics];
    }

    /// <summary>ported from: src/compiler/program.ts:3283-3286 — <c>getGlobalDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS's closure function takes no parameters; the generated interface declares a
    /// <c>cancellationToken</c> it does not use. <c>getGlobalDiagnostics().slice()</c> sorts a *copy* — the checker's
    /// own answer is the live list whose identity a caller compares (<c>Utilities.Diagnostics.cs:374-379</c>) — so the
    /// copy is <see cref="ToDiagnosticList"/>, which is also the narrowing the sort needs. <c>emptyArray</c> is the
    /// empty list.</remarks>
    public List<IDiagnostic> GetGlobalDiagnostics(ICancellationToken? cancellationToken)
        => _rootNames.Count != 0
            ? [.. Utilities.SortAndDeduplicateDiagnostics(ToDiagnosticList(GetTypeChecker().GetGlobalDiagnostics()))]
            : [];

    /// <summary>ported from: src/compiler/program.ts:3287-3290 — <c>getConfigFileParsingDiagnostics</c>.</summary>
    /// <remarks>PORT-NOTE: TS answers <c>configFileParsingDiagnostics || emptyArray</c> — the array itself when it is
    /// there, the shared empty array otherwise; the port answers the state field (already
    /// <c>List&lt;IDiagnostic&gt;</c>) or a fresh empty list.</remarks>
    public List<IDiagnostic> GetConfigFileParsingDiagnostics()
        => _configFileParsingDiagnostics ?? [];

    // ---------------------------------------------------------------------------------------------
    // The helpers this slice's bodies need (see the file header for what each is)
    // ---------------------------------------------------------------------------------------------

    // CreateCommentDirectivesMap/CommentDirectivesMap are utilities.ts/types.ts members: they live in the
    // `Utilities` partial at the bottom of this file.

    /// <summary>ported from: src/compiler/utilities.ts:10397-10416 — <c>skipTypeChecking</c> and
    /// <c>skipTypeCheckingWorker</c> as this file calls them.</summary>
    /// <remarks>PORT-NOTE: <see cref="Utilities.SkipTypeChecking"/> is the reference's whole chain, its two host
    /// questions taken as the delegates its assigned signature declares
    /// (<c>Program/Utilities.Program.cs</c>); the program is the host the reference passes
    /// (<c>program.ts:2815,2893</c>), so both method groups come from this partial.</remarks>
    private bool SkipTypeChecking(SourceFile sourceFile)
        => Utilities.SkipTypeChecking(sourceFile, _options, IsSourceFileDefaultLibrary, IsSourceOfProjectReferenceRedirect);

    // getLineStarts/getLineAndCharacterOfPosition (scanner.ts:502-505, 547-550) are not declared on Program: the
    // two SourceFile-shaped restatements the port has (Utilities/Utilities.Binder.Diagnostics.cs:366-379 and
    // Program/Program.Host.cs) are private to their own classes, so MarkPrecedingCommentDirectiveLine memoizes the
    // line starts inline instead (see its remarks) and Utilities.CreateCommentDirectivesMap uses the Utilities twin.

    /// <summary>
    /// The narrowing bridge the sorted-diagnostics boundary needs.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the workers' answers are the generated interfaces' <c>List&lt;IDiagnostic&gt;</c>/
    /// <c>List&lt;IDiagnosticWithLocation&gt;</c> (see the class remarks), while
    /// <see cref="Utilities.SortAndDeduplicateDiagnostics"/> is fixed to <see cref="Diagnostic"/>
    /// (<c>Utilities.Diagnostics.cs:194</c>); TS narrows an array of a subtype to <c>Diagnostic[]</c> with no copy, C#
    /// cannot narrow an invariant <c>List</c>, so the copy happens here. The element type is the concrete
    /// <see cref="Diagnostic"/> at run time — every diagnostic the parser, the binder, the checker and the program's
    /// own collection build is one (<c>Core/Diagnostic.cs</c>) — so the cast cannot fail. The same bridge is
    /// <c>Parser/JSDocParser.cs:194-204</c>.
    /// </remarks>
    private static List<Diagnostic> ToDiagnosticList(IEnumerable<IDiagnostic> diagnostics)
    {
        var result = new List<Diagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            result.Add((Diagnostic)diagnostic);
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // The union accesses and predicates getJSSyntacticDiagnosticsForFile needs
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1647-1649 — <c>isModifier</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's type predicate narrows to <c>Modifier</c>, which the generated model spells
    /// as the concrete keyword classes; the kind test is the ported <see cref="Utilities.IsModifierKind"/>
    /// (<c>Utilities/Utilities.Parser.cs:275</c>). Declared here, private, because this pass is its only caller and
    /// <c>utilitiesPublic.ts</c> has no other ported owner for it yet; the family's one member a second caller reached —
    /// <see cref="Utilities.CanHaveDecorators"/> — lives on <c>Utilities</c> (§15.15's one-name-one-home rule).</remarks>
    private static bool IsModifier(Node node) => Utilities.IsModifierKind(node.Kind);

    /// <summary>ported from: src/compiler/types.ts:1590 — the <c>modifiers</c> slot of the
    /// <c>HasIllegalDecorators</c>/<c>HasDecorators</c> unions, as <c>getJSSyntacticDiagnosticsForFile</c> reads it.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS reaches <c>parent.modifiers</c> through a union whose every member declares the slot; the
    /// generated model declares it on the concrete classes only, and with two element types —
    /// <c>NodeArray&lt;Node&gt;</c> for most kinds and <c>NodeArray&lt;ModifierToken&gt;</c> for
    /// <c>FunctionExpression</c>/<c>ArrowFunction</c>/<c>MethodSignature</c> — so the accessor answers the covariant
    /// <c>IReadOnlyList&lt;Node&gt;</c> view, which accepts both. The identity comparisons that keep
    /// <c>walkArray</c>'s <c>nodes === parent.modifiers</c> tests see the array itself through the view, unchanged.
    /// The kinds named are those this pass can reach: the two decorator families, the type-parameter group, the
    /// variable statement, the property/parameter modifier checks and the type-argument group's siblings.
    /// </remarks>
    private static IReadOnlyList<Node>? ModifiersOf(Node node) => node switch
    {
        ParameterDeclaration n => n.Modifiers,
        PropertyDeclaration n => n.Modifiers,
        MethodDeclaration n => n.Modifiers,
        GetAccessorDeclaration n => n.Modifiers,
        SetAccessorDeclaration n => n.Modifiers,
        ClassExpression n => n.Modifiers,
        ClassDeclaration n => n.Modifiers,
        FunctionExpression n => n.Modifiers,
        ArrowFunction n => n.Modifiers,
        ConstructorDeclaration n => n.Modifiers,
        FunctionDeclaration n => n.Modifiers,
        VariableStatement n => n.Modifiers,
        PropertyAssignment n => n.Modifiers,
        ShorthandPropertyAssignment n => n.Modifiers,
        IndexSignatureDeclaration n => n.Modifiers,
        ClassStaticBlockDeclaration n => n.Modifiers,
        MissingDeclaration n => n.Modifiers,
        InterfaceDeclaration n => n.Modifiers,
        TypeAliasDeclaration n => n.Modifiers,
        EnumDeclaration n => n.Modifiers,
        ModuleDeclaration n => n.Modifiers,
        ImportEqualsDeclaration n => n.Modifiers,
        ImportDeclaration n => n.Modifiers,
        NamespaceExportDeclaration n => n.Modifiers,
        ExportDeclaration n => n.Modifiers,
        ExportAssignment n => n.Modifiers,
        _ => null,
    };

    // The `parent.typeParameters` read of `getJSSyntacticDiagnosticsForFile` is `Utilities.TypeParametersOf` — the
    // `DeclarationWithTypeParameterChildren` slot (types.ts:3536-3541), whose single home that member is
    // (docs/architecture.md §15.15's "one name, one owner, one home").

    /// <summary>ported from: src/compiler/types.ts:1590 — the <c>typeArguments</c> slot of the
    /// <c>NodeWithTypeArguments</c> union, as <c>getJSSyntacticDiagnosticsForFile</c> reads it.</summary>
    /// <remarks>PORT-NOTE: the reference's cast is to <c>NodeWithTypeArguments</c>; the generated model puts that
    /// interface on <c>ExpressionWithTypeArguments</c> only, while <c>CallExpression</c>, <c>NewExpression</c>,
    /// <c>JsxSelfClosingElement</c>, <c>JsxOpeningElement</c> and <c>TaggedTemplateExpression</c> declare the slot on
    /// the class, so the six kinds are named.</remarks>
    private static NodeArray<ITypeNode>? TypeArgumentsOf(Node node) => node switch
    {
        CallExpression n => n.TypeArguments,
        NewExpression n => n.TypeArguments,
        ExpressionWithTypeArguments n => n.TypeArguments,
        JsxSelfClosingElement n => n.TypeArguments,
        JsxOpeningElement n => n.TypeArguments,
        TaggedTemplateExpression n => n.TypeArguments,
        _ => null,
    };

    /// <summary>ported from: src/compiler/types.ts:1590 — the <c>questionToken</c> slot of the
    /// <c>parameter | property declaration | method declaration</c> union, as <c>getJSSyntacticDiagnosticsForFile</c>
    /// reads it.</summary>
    /// <remarks>PORT-NOTE: the generated interface that carries the slot (<c>ITypeElement</c>) is implemented by
    /// neither <c>ParameterDeclaration</c> nor <c>PropertyDeclaration</c>, so the three kinds are named; the
    /// comparator is a reference comparison (<c>parent.questionToken === node</c>).</remarks>
    private static PunctuationToken? QuestionTokenOf(Node parent) => parent switch
    {
        ParameterDeclaration n => n.QuestionToken,
        PropertyDeclaration n => n.QuestionToken,
        MethodDeclaration n => n.QuestionToken,
        _ => null,
    };

    /// <summary>ported from: src/compiler/types.ts:1590 — the <c>type</c> slot of the
    /// <c>FunctionLikeDeclaration | VariableDeclaration | ParameterDeclaration | PropertyDeclaration</c> union, as
    /// <c>getJSSyntacticDiagnosticsForFile</c> reads it.</summary>
    /// <remarks>PORT-NOTE: <c>ParameterDeclaration</c>, <c>PropertyDeclaration</c> and <c>VariableDeclaration</c>
    /// implement none of the generated type-carrying capability interfaces, so the eleven kinds are named; the
    /// comparator is a reference comparison (<c>parent.type === node</c>).</remarks>
    private static ITypeNode? TypeOf(Node parent) => parent switch
    {
        ParameterDeclaration n => n.Type,
        PropertyDeclaration n => n.Type,
        MethodDeclaration n => n.Type,
        MethodSignature n => n.Type,
        ConstructorDeclaration n => n.Type,
        GetAccessorDeclaration n => n.Type,
        SetAccessorDeclaration n => n.Type,
        FunctionExpression n => n.Type,
        FunctionDeclaration n => n.Type,
        ArrowFunction n => n.Type,
        VariableDeclaration n => n.Type,
        _ => null,
    };

    /// <summary>ported from: src/compiler/core.ts:166-175 — <c>find</c>, over the modifier arrays this pass reads.
    /// </summary>
    /// <remarks>PORT-NOTE: <c>Core.Find</c> takes an <c>IList&lt;T&gt;</c>; the modifier arrays are read through the
    /// covariant <c>IReadOnlyList&lt;Node&gt;</c> view (see <see cref="ModifiersOf"/>), so the search is spelled here,
    /// with the reference's <c>startIndex</c> default.</remarks>
    private static Node? FindModifier(IReadOnlyList<Node>? modifiers, Func<Node, bool> predicate, int startIndex = 0)
    {
        if (modifiers is null)
        {
            return null;
        }

        for (var i = startIndex; i < modifiers.Count; i++)
        {
            var value = modifiers[i];
            if (predicate(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/core.ts:198-206 — <c>findIndex</c>, over the modifier arrays this pass
    /// reads.</summary>
    /// <remarks>PORT-NOTE: the same <c>IReadOnlyList</c> narrowing as <see cref="FindModifier"/>.</remarks>
    private static int FindModifierIndex(IReadOnlyList<Node>? modifiers, Func<Node, bool> predicate, int startIndex = 0)
    {
        if (modifiers is null)
        {
            return -1;
        }

        for (var i = startIndex; i < modifiers.Count; i++)
        {
            if (predicate(modifiers[i]))
            {
                return i;
            }
        }

        return -1;
    }
}

// ---------------------------------------------------------------------------------------------
// The comment-directive map (utilities.ts:1209-1237, types.ts:4488-4491)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The <c>utilities.ts</c> member this file's comment-directive pass needs, and the class that fills the generated
/// <see cref="ICommentDirectivesMap"/> for it.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS writes both as module-level members of the compiler namespace; the port has no module-level
/// functions, so they land on the <c>Utilities</c> partial (the placement <c>Program/Program.ReferenceTables.cs</c>
/// and <c>Program/Utilities.Program.cs</c> already use).
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:1209-1237 — <c>createCommentDirectivesMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the map with <c>new Map(commentDirectives.map(…))</c>, keyed by the *decimal string* of
    /// each directive's end line, and <c>markUsed</c> looks the same string up; the port keys by the line number
    /// itself, which is one-to-one for integers. <c>new Map</c> keeps the *first* position of a duplicated key and the
    /// *last* value; the port reproduces that with <c>indexByLine</c>, since its replacement
    /// (<see cref="CommentDirectivesMap"/>) keeps its entries in a list so that <c>getUnusedExpectations</c> answers in
    /// insertion order, as a JS Map does — the order the diagnostics the caller appends follow.
    /// <para>
    /// PORT-NOTE: the line is read through <c>GetLineAndCharacterOfPosition</c>, the private <c>SourceFile</c> twin
    /// <c>Utilities.Binder.Diagnostics.cs:376-379</c> already keeps for the <c>ScannerUtilities</c> member's
    /// <c>ISourceFileLike</c> parameter; a part of the same partial class reaches it.
    /// </para>
    /// </remarks>
    public static ICommentDirectivesMap CreateCommentDirectivesMap(SourceFile sourceFile, List<ICommentDirective> commentDirectives)
    {
        Dictionary<int, int> indexByLine = [];
        List<(int Line, ICommentDirective Directive)> directivesByLine = [];
        foreach (var commentDirective in commentDirectives)
        {
            var line = GetLineAndCharacterOfPosition(sourceFile, commentDirective.Range!.End).Line;
            if (indexByLine.TryGetValue(line, out var index))
            {
                directivesByLine[index] = (line, commentDirective);
            }
            else
            {
                indexByLine[line] = directivesByLine.Count;
                directivesByLine.Add((line, commentDirective));
            }
        }

        return new CommentDirectivesMap(directivesByLine);
    }

    /// <summary>ported from: src/compiler/types.ts:4488-4491 — <c>CommentDirectivesMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS fills this interface with the object literal <c>{ getUnusedExpectations, markUsed }</c> over two
    /// captured maps; the port needs a constructible class, the same treatment the other object-literal models got
    /// (§5 rule 6). See <see cref="CreateCommentDirectivesMap"/> for why the entry list keeps its order.
    /// </remarks>
    internal sealed class CommentDirectivesMap : ICommentDirectivesMap
    {
        /// <summary>ported from: src/compiler/utilities.ts:1210-1216 — <c>directivesByLine</c>.</summary>
        private readonly List<(int Line, ICommentDirective Directive)> _directivesByLine;

        /// <summary>ported from: src/compiler/utilities.ts:1216 — the keys of <c>directivesByLine</c>.</summary>
        private readonly HashSet<int> _directiveLines;

        /// <summary>ported from: src/compiler/utilities.ts:1218 — <c>usedLines</c>.</summary>
        private readonly HashSet<int> _usedLines = [];

        internal CommentDirectivesMap(List<(int Line, ICommentDirective Directive)> directivesByLine)
        {
            _directivesByLine = directivesByLine;
            _directiveLines = [.. directivesByLine.Select(entry => entry.Line)];
        }

        /// <summary>ported from: src/compiler/utilities.ts:1221-1225 — <c>getUnusedExpectations</c>.</summary>
        /// <remarks>PORT-NOTE: TS filters <c>arrayFrom(directivesByLine.entries())</c> and maps each surviving pair to
        /// its directive; the port answers the directives in the same order without the intermediate array.</remarks>
        public List<ICommentDirective> GetUnusedExpectations()
        {
            List<ICommentDirective> result = [];
            foreach (var (line, directive) in _directivesByLine)
            {
                if (directive.Type == CommentDirectiveType.ExpectError && !_usedLines.Contains(line))
                {
                    result.Add(directive);
                }
            }

            return result;
        }

        /// <summary>ported from: src/compiler/utilities.ts:1227-1234 — <c>markUsed</c>.</summary>
        public bool MarkUsed(int matchedLine)
        {
            if (!_directiveLines.Contains(matchedLine))
            {
                return false;
            }

            _usedLines.Add(matchedLine);
            return true;
        }
    }
}