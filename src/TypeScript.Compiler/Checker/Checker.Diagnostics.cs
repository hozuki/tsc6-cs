namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:2377-2378 (diagnostics, suggestionDiagnostics)
// ported from: src/compiler/checker.ts:2506-2510 (getEmitResolver)
// ported from: src/compiler/checker.ts:2513-2631 (lookupOrIssueError … addDeprecatedSuggestionWithSignature)
/// <summary>
/// The checker's diagnostics single point: every diagnostic the checker reports goes through
/// <see cref="LookupOrIssueError"/>/<see cref="Error"/> into the two collections, so a duplicate report is folded into
/// the first one and the order the collections hand back is the one the baselines see.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares <c>diagnostics</c>/<c>suggestionDiagnostics</c> with
/// <c>createDiagnosticCollection()</c> (utilities.ts:6060) at checker.ts:2377-2378, i.e. mid-closure; the port creates
/// them with their field initializers because the factory is a pure <c>Utilities</c> member and nothing between the
/// closure's open and that line can observe their absence.
/// </para>
/// <para>
/// PORT-NOTE: <c>errorOrSuggestion</c>'s parameter is TS's union
/// <c>DiagnosticMessage | DiagnosticMessageChain</c>, discriminated by <c>"message" in message</c>; the port takes
/// <c>object</c> and tests for <see cref="IDiagnosticMessageChain"/>, the only arm that is not a
/// <see cref="DiagnosticMessage"/>.
/// </para>
/// <para>
/// PORT-NOTE: <c>getDiagnostics</c> (checker.ts:49744) and <c>getCombinedNodeFlagsCached</c> (checker.ts:50947) are the
/// two members this file calls that it does not declare: the first belongs to M6-5's
/// <c>checkSourceFile</c>/diagnostic-collection block and the second to M6-5's flag-caching block. Both are named in
/// the slice report.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:2377 — <c>diagnostics</c>.</summary>
    internal readonly DiagnosticCollection _diagnostics = Utilities.CreateDiagnosticCollection();

    /// <summary>ported from: src/compiler/checker.ts:2378 — <c>suggestionDiagnostics</c>.</summary>
    internal readonly DiagnosticCollection _suggestionDiagnostics = Utilities.CreateDiagnosticCollection();

    /// <summary>
    /// ported from: src/compiler/checker.ts:2506-2510 — <c>getEmitResolver</c>.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS's return value is the <c>emitResolver</c> the assembly member <c>createResolver()</c> built
    /// (M6-6); until that member runs the slot is null, which is why the assertion is here rather than in the
    /// declaration.
    /// </remarks>
    internal IEmitResolver GetEmitResolver(SourceFile? sourceFile, ICancellationToken? cancellationToken, bool? skipDiagnostics = null)
    {
        // Ensure we have all the type information in place for this file so that all the
        // emitter questions of this resolver will return the right information.
        if (skipDiagnostics != true) GetDiagnostics(sourceFile!, cancellationToken!);
        return Debug.CheckDefined(_emitResolver);
    }

    /// <summary>ported from: src/compiler/checker.ts:2513-2525 — <c>lookupOrIssueError</c>.</summary>
    internal Diagnostic LookupOrIssueError(Node? location, DiagnosticMessage message, params object?[] args)
    {
        var diagnostic = location is not null
            ? Utilities.CreateDiagnosticForNode(location, message, args)
            : Utilities.CreateCompilerDiagnostic(message, args);
        var existing = _diagnostics.Lookup(diagnostic);
        if (existing is not null)
        {
            return existing;
        }
        else
        {
            _diagnostics.Add(diagnostic);
            return diagnostic;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2527-2531 — <c>errorSkippedOn</c>.</summary>
    /// <remarks>PORT-NOTE: TS types <paramref name="key"/> as <c>keyof CompilerOptions</c>; the port takes the option's name, which is what <c>Diagnostic.skippedOn</c> holds.</remarks>
    internal Diagnostic ErrorSkippedOn(string key, Node? location, DiagnosticMessage message, params object?[] args)
    {
        var diagnostic = Error(location, message, args);
        diagnostic.SkippedOn = key;
        return diagnostic;
    }

    /// <summary>ported from: src/compiler/checker.ts:2533-2537 — <c>createError</c>.</summary>
    internal Diagnostic CreateError(Node? location, DiagnosticMessage message, params object?[] args)
        => location is not null
            ? Utilities.CreateDiagnosticForNode(location, message, args)
            : Utilities.CreateCompilerDiagnostic(message, args);

    /// <summary>ported from: src/compiler/checker.ts:2539-2543 — <c>error</c>.</summary>
    internal Diagnostic Error(Node? location, DiagnosticMessage message, params object?[] args)
    {
        var diagnostic = CreateError(location, message, args);
        _diagnostics.Add(diagnostic);
        return diagnostic;
    }

    /// <summary>ported from: src/compiler/checker.ts:2545-2557 — <c>getVerbatimModuleSyntaxErrorMessage</c>.</summary>
    internal static DiagnosticMessage GetVerbatimModuleSyntaxErrorMessage(Node node)
    {
        var sourceFile = Utilities.GetSourceFileOfNode(node);
        var fileName = sourceFile!.FileName ?? "";

        // Check if the file is .cts or .cjs (CommonJS-specific extensions)
        if (Paths.FileExtensionIsOneOf(fileName, [Extension.Cts.Value, Extension.Cjs.Value]))
        {
            return Diagnostics.ECMAScript_imports_and_exports_cannot_be_written_in_a_CommonJS_file_under_verbatimModuleSyntax;
        }
        else
        {
            // For .ts, .tsx, .js, etc.
            return Diagnostics.ECMAScript_imports_and_exports_cannot_be_written_in_a_CommonJS_file_under_verbatimModuleSyntax_Adjust_the_type_field_in_the_nearest_package_json_to_make_this_file_an_ECMAScript_module_or_adjust_your_verbatimModuleSyntax_module_and_moduleResolution_settings_in_TypeScript;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2559-2566 — <c>addErrorOrSuggestion</c>.</summary>
    internal void AddErrorOrSuggestion(bool isError, Diagnostic diagnostic)
    {
        if (isError)
        {
            _diagnostics.Add(diagnostic);
        }
        else
        {
            // PORT-NOTE: TS adds a *copy* whose category is overridden (`{ ...diagnostic, category: Suggestion }`), so
            // the caller's diagnostic is untouched and both collections can hold it; the port builds that copy by
            // hand, sharing the related-information list exactly as the spread does.
            _suggestionDiagnostics.Add(new Diagnostic(diagnostic.MessageText, DiagnosticCategory.Suggestion, diagnostic.Code, diagnostic.Start, diagnostic.Length, diagnostic.FileName, diagnostic.File)
            {
                Source = diagnostic.Source,
                RelatedInformation = diagnostic.RelatedInformation,
                ReportsUnnecessary = diagnostic.ReportsUnnecessary,
                ReportsDeprecated = diagnostic.ReportsDeprecated,
                SkippedOn = diagnostic.SkippedOn,
                CanonicalHead = diagnostic.CanonicalHead,
            });
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2567-2579 — <c>errorOrSuggestion</c>.</summary>
    internal void ErrorOrSuggestion(bool isError, Node location, object message, params object?[] args)
    {
        // Pseudo-synthesized input node
        if (location.Pos < 0 || location.End < 0)
        {
            if (!isError)
            {
                return; // Drop suggestions (we have no span to suggest on)
            }

            // Issue errors globally
            var file = Utilities.GetSourceFileOfNode(location)!;
            AddErrorOrSuggestion(isError, message is not IDiagnosticMessageChain
                ? Utilities.CreateFileDiagnostic(file, 0, 0, (DiagnosticMessage)message, args)
                : Utilities.CreateDiagnosticForFileFromMessageChain(file, (IDiagnosticMessageChain)message));
            return;
        }

        AddErrorOrSuggestion(isError, message is not IDiagnosticMessageChain
            ? Utilities.CreateDiagnosticForNode(location, (DiagnosticMessage)message, args)
            : Utilities.CreateDiagnosticForNodeFromMessageChain(Utilities.GetSourceFileOfNode(location)!, location, (IDiagnosticMessageChain)message));
    }

    /// <summary>ported from: src/compiler/checker.ts:2581-2592 — <c>errorAndMaybeSuggestAwait</c>.</summary>
    internal Diagnostic ErrorAndMaybeSuggestAwait(Node location, bool maybeMissingAwait, DiagnosticMessage message, params object?[] args)
    {
        var diagnostic = Error(location, message, args);
        if (maybeMissingAwait)
        {
            var related = Utilities.CreateDiagnosticForNode(location, Diagnostics.Did_you_forget_to_use_await);
            Utilities.AddRelatedInfo(diagnostic, related);
        }

        return diagnostic;
    }

    /// <summary>ported from: src/compiler/checker.ts:2595-2606 — <c>addDeprecatedSuggestionWorker</c>.</summary>
    internal Diagnostic AddDeprecatedSuggestionWorker(IList<Node> declarations, Diagnostic diagnostic)
    {
        var deprecatedTag = Core.Length(declarations) > 0 ? Core.ForEach(declarations, (Node declaration, int _) => Utilities.GetJSDocDeprecatedTag(declaration)) : null;
        if (deprecatedTag is not null)
        {
            Utilities.AddRelatedInfo(
                diagnostic,
                Utilities.CreateDiagnosticForNode(deprecatedTag, Diagnostics.The_declaration_was_marked_as_deprecated_here));
        }

        // We call `addRelatedInfo()` before adding the diagnostic to prevent duplicates.
        _suggestionDiagnostics.Add(diagnostic);
        return diagnostic;
    }

    /// <summary>ported from: src/compiler/checker.ts:2608-2615 — <c>isDeprecatedSymbol</c>.</summary>
    internal bool IsDeprecatedSymbol(ISymbol symbol)
    {
        var parentSymbol = GetParentOfSymbol(symbol);
        if (parentSymbol is not null && Core.Length(symbol.Declarations) > 1)
        {
            return (parentSymbol.Flags & SymbolFlags.Interface) != 0
                ? Core.Some(symbol.Declarations, declaration => IsDeprecatedDeclaration(declaration))
                : Core.Every(symbol.Declarations, (IDeclaration declaration, int _) => IsDeprecatedDeclaration(declaration));
        }

        return (symbol.ValueDeclaration is not null && IsDeprecatedDeclaration(symbol.ValueDeclaration))
            || (Core.Length(symbol.Declarations) > 0 && Core.Every(symbol.Declarations, (IDeclaration declaration, int _) => IsDeprecatedDeclaration(declaration)));
    }

    /// <summary>ported from: src/compiler/checker.ts:2617-2619 — <c>isDeprecatedDeclaration</c>.</summary>
    internal bool IsDeprecatedDeclaration(IDeclaration declaration)
        => (GetCombinedNodeFlagsCached((Node)declaration) & NodeFlags.Deprecated) != 0;

    /// <summary>ported from: src/compiler/checker.ts:2621-2624 — <c>addDeprecatedSuggestion</c>.</summary>
    internal Diagnostic AddDeprecatedSuggestion(Node location, IList<Node> declarations, string deprecatedEntity)
    {
        var diagnostic = Utilities.CreateDiagnosticForNode(location, Diagnostics._0_is_deprecated, deprecatedEntity);
        return AddDeprecatedSuggestionWorker(declarations, diagnostic);
    }

    /// <summary>ported from: src/compiler/checker.ts:2626-2631 — <c>addDeprecatedSuggestionWithSignature</c>.</summary>
    internal Diagnostic AddDeprecatedSuggestionWithSignature(Node location, Node declaration, string? deprecatedEntity, string signatureString)
    {
        var diagnostic = deprecatedEntity is not null
            ? Utilities.CreateDiagnosticForNode(location, Diagnostics.The_signature_0_of_1_is_deprecated, signatureString, deprecatedEntity)
            : Utilities.CreateDiagnosticForNode(location, Diagnostics._0_is_deprecated, signatureString);
        return AddDeprecatedSuggestionWorker([declaration], diagnostic);
    }
}
