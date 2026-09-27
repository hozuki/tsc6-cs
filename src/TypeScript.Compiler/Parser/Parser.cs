namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:1437-1436 (namespace Parser), 1603-2600 (entry, state, context, errors, tokens)
/// <summary>
/// The recursive-descent parser: source text in, <see cref="SourceFile"/> out.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS models this as a namespace full of module-level <c>var</c>s (one shared scanner, one state per parse,
/// reset by <c>initializeState</c>/<c>clearState</c>). C# has no module-level mutable state worth having, so the same
/// state is an instance: one <see cref="Parser"/> per source file, its <c>initializeState</c> folded into the
/// constructor. That is the same treatment <see cref="Scanner"/> got (§14.2) and it keeps two parses from stepping on
/// each other — which matters because the tests run in parallel.
/// <para>
/// PORT-NOTE: the four <c>objectAllocator.getXConstructor()</c> captures and the <c>countNode</c>-wrapping
/// <c>baseNodeFactory</c> are not ported (§15.2): C# constructs the concrete node class directly, and the counting they
/// exist for is done by the factory's own <c>NodeCreated</c> hook.
/// </para>
/// <para>
/// PORT-NOTE: TS shares one scanner across every parse. The port creates one per parser — the scanner is a plain object
/// here (no compiled closures to amortize), and a per-parse instance removes the shared mutable state the shared one
/// amounts to.
/// </para>
/// <para>
/// PORT-NOTE: the parser's <c>tracing?.push/pop</c> calls (three of them, all around `parseSourceFile`) are omitted:
/// <c>tracing.ts</c> is deferred (§3) and its calls are no-ops unless <c>--generateTrace</c> is on.
/// </para>
/// </remarks>
internal sealed partial class Parser
{
    /// <summary>The flags the parser builds its factory with (parser.ts:1464).</summary>
    /// <remarks>
    /// PORT-NOTE: TS also passes a node-counting <c>baseNodeFactory</c> here; the port's factory reports creations
    /// through <see cref="NodeFactory.NodeCreated"/> instead, which <see cref="Parser"/> wires to <c>nodeCount</c>.
    /// </remarks>
    private const NodeFactoryFlags ParserFactoryFlags =
        NodeFactoryFlags.NoParenthesizerRules | NodeFactoryFlags.NoNodeConverters | NodeFactoryFlags.NoOriginalNode;

    private readonly Scanner _scanner;

    private readonly NodeFactory _factory;

    // ---- per-parse inputs (TS: the `var`s `initializeState` assigns) ----

    private readonly string _fileName;
    private readonly string _sourceText;
    private readonly ScriptTarget _languageVersion;
    private readonly ScriptKind _scriptKind;
    private readonly LanguageVariant _languageVariant;
    private readonly JSDocParsingMode _jsDocParsingMode;

    // ---- mutable parse state ----

    private SyntaxKind _currentToken;
    private NodeFlags _contextFlags;
    private NodeFlags _sourceFlags;
    private List<Diagnostic> _parseDiagnostics = [];
    private List<Diagnostic>? _jsDocDiagnostics;
    private IncrementalParser.ISyntaxCursor? _syntaxCursor;
    private Dictionary<string, string> _identifiers = new(StringComparer.Ordinal);
    private int _identifierCount;
    private int _parsingContexts;
    private HashSet<int>? _notParenthesizedArrow;
    private bool _topLevel = true;
    private bool _parseErrorBeforeNextFinishedNode;
    private bool _hasDeprecatedTag;

    /// <summary>
    /// Sets up a parse of <paramref name="sourceText"/> (TS: <c>initializeState</c>, parser.ts:1735).
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS copies the <c>Node</c>/<c>Token</c>/<c>Identifier</c>/<c>PrivateIdentifier</c>/<c>SourceFile</c>
    /// constructors out of the <c>objectAllocator</c> here; the port has one class per kind and nothing to copy
    /// (§15.2). The file name is normalized, the scanner is primed with the text and the script-kind-derived
    /// <see cref="LanguageVariant"/>, and <see cref="NodeFlags.JavaScriptFile"/>/<see cref="NodeFlags.JsonFile"/> are
    /// derived from the script kind — all as TS does.
    /// </remarks>
    private Parser(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        IncrementalParser.ISyntaxCursor? syntaxCursor,
        ScriptKind scriptKind,
        JSDocParsingMode jsDocParsingMode)
    {
        _fileName = Paths.NormalizePath(fileName);
        _sourceText = sourceText;
        _languageVersion = languageVersion;
        _scriptKind = scriptKind;
        _languageVariant = Utilities.GetLanguageVariant(scriptKind);
        _jsDocParsingMode = jsDocParsingMode;
        _syntaxCursor = syntaxCursor;

        _contextFlags = scriptKind switch
        {
            ScriptKind.JS or ScriptKind.JSX => NodeFlags.JavaScriptFile,
            ScriptKind.JSON => NodeFlags.JavaScriptFile | NodeFlags.JsonFile,
            _ => NodeFlags.None,
        };

        _factory = new NodeFactory(ParserFactoryFlags);

        _scanner = Scanner.Create(languageVersion, /*skipTrivia*/ true, _languageVariant, sourceText, ScanError, null, null);
        _scanner.SetScriptTarget(languageVersion);
        _scanner.SetScriptKind(scriptKind);
        _scanner.SetJSDocParsingMode(jsDocParsingMode);
    }

    /// <summary>ported from: src/compiler/parser.ts:2176 — <c>scanError</c>.</summary>
    private void ScanError(DiagnosticMessage message, int length, object? arg0) => ParseErrorAtPosition(_scanner.GetTokenEnd(), length, message, arg0);

    //
    // Entry points
    //

    /// <summary>ported from: src/compiler/parser.ts:1603-1632 — <c>parseSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's signature has defaults for <c>setParentNodes</c>, <c>scriptKind</c>,
    /// <c>setExternalModuleIndicatorOverride</c> and <c>jsDocParsingMode</c>. The port spells every parameter out so
    /// callers cannot silently take a different path than they meant to; <see cref="CreateSourceFile(string, string, CreateSourceFileOptions, bool, ScriptKind?)"/>
    /// is the ergonomic front door.
    /// </remarks>
    internal static SourceFile ParseSourceFile(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        IncrementalParser.ISyntaxCursor? syntaxCursor,
        bool setParentNodes,
        ScriptKind? scriptKind,
        Action<SourceFile>? setExternalModuleIndicator,
        JSDocParsingMode jsDocParsingMode)
    {
        var kind = Utilities.EnsureScriptKind(fileName, scriptKind);
        if (kind == ScriptKind.JSON)
        {
            var result = ParseJsonText(fileName, sourceText, languageVersion, syntaxCursor, setParentNodes);
            // PORT-NOTE: TS runs `convertToJson(result, result.statements[0]?.expression, …, /*returnValue*/ false,
            // /*jsonConversionNotifier*/ undefined)` here, which only *validates* the tree and reports diagnostics for
            // values JSON cannot hold. That function lives in `commandLineParser.ts` and is built on the M8 option model
            // (`CommandLineOption`, `JsonConversionNotifier`), and this call passes no notifier, so the half it reaches
            // from the parser is unreachable: a tree parsed from JSON text holds no value it would reject, and the parse
            // diagnostics it could add are already produced by `parseJsonText`. It lands with M8; see §15.10.
            result.ReferencedFiles = [];
            result.TypeReferenceDirectives = [];
            result.LibReferenceDirectives = [];
            result.AmdDependencies = [];
            result.HasNoDefaultLib = false;
            result.Pragmas = new PragmaMap();
            return result;
        }

        var parser = new Parser(fileName, sourceText, languageVersion, syntaxCursor, kind, jsDocParsingMode);
        var sourceFile = parser.ParseSourceFileWorker(setParentNodes, setExternalModuleIndicator ?? SetExternalModuleIndicatorDefault);
        parser.ClearState();
        return sourceFile;
    }

    /// <summary>ported from: src/compiler/parser.ts:1635-1645 — <c>parseIsolatedEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns <c>EntityName</c> (<c>Identifier | QualifiedName</c>). Those two have no common C# base —
    /// <c>Identifier</c> is a <c>MemberExpression</c> while <c>QualifiedName</c> derives straight from <c>Node</c> — so the
    /// port answers <c>Node?</c>, which is also what the model does with every other <c>EntityName</c> slot (§12.5).
    /// </remarks>
    internal static Node? ParseIsolatedEntityName(string content, ScriptTarget languageVersion)
    {
        // Choice of `isDeclarationFile` should be arbitrary.
        var parser = new Parser("", content, languageVersion, null, ScriptKind.JS, JSDocParsingMode.ParseAll);
        parser.NextToken(); // Prime the scanner.
        var entityName = parser.ParseEntityName(/*allowReservedWords*/ true);
        var isValid = parser.Token() == SyntaxKind.EndOfFileToken && parser._parseDiagnostics.Count == 0;
        parser.ClearState();
        return isValid ? entityName : null;
    }

    /// <summary>ported from: src/compiler/parser.ts:1970-1972 — <c>fixupParentReferences</c>.</summary>
    /// <remarks>
    /// Normally parent references are set during binding; this sets them for clients that only need a syntax tree, so
    /// that the binder's work is not needed just to walk up.
    /// </remarks>
    internal static void FixupParentReferences(Node rootNode) => Utilities.SetParentRecursive(rootNode, /*incremental*/ true);

    /// <summary>ported from: src/compiler/parser.ts:1392 — <c>setExternalModuleIndicator</c>.</summary>
    private static void SetExternalModuleIndicatorDefault(SourceFile sourceFile)
        => sourceFile.ExternalModuleIndicator = IsFileProbablyExternalModule(sourceFile);

    /// <summary>ported from: src/compiler/parser.ts:1803-1809 — the head of <c>parseSourceFileWorker</c>.</summary>
    private SourceFile ParseSourceFileWorker(bool setParentNodes, Action<SourceFile> setExternalModuleIndicator)
    {
        using var allocProbe = AllocationProbe.Enter("Parser.ParseSourceFile");
        var isDeclarationFile = IsDeclarationFileName(_fileName);
        if (isDeclarationFile)
        {
            _contextFlags |= NodeFlags.Ambient;
        }

        _sourceFlags = _contextFlags;

        // Prime the scanner.
        NextToken();

        var statements = ParseList(ParsingContext.SourceElements, ParseStatement);
        Debug.Assert(Token() == SyntaxKind.EndOfFileToken);
        var endHasJSDoc = HasPrecedingJSDocComment();
        var endOfFileToken = WithJSDoc(ParseTokenNode<Token>(), endHasJSDoc);

        var sourceFile = CreateSourceFile(
            _fileName,
            _languageVersion,
            _scriptKind,
            isDeclarationFile,
            statements,
            endOfFileToken,
            _sourceFlags,
            setExternalModuleIndicator);

        // A member of ReadonlyArray<T> isn't assignable to a member of T[] (and prevents a direct cast) — but this is
        // where we set up those members so they can be readonly in the future.
        ProcessCommentPragmas(sourceFile, _sourceText);
        ProcessPragmasIntoFields(sourceFile, ReportPragmaDiagnostic);

        // PORT-NOTE: the scanner hands back `IList<ICommentDirective>` while `SourceFile.CommentDirectives` is a
        // `List<ICommentDirective>?`; the port copies into that shape rather than widening the generated slot.
        sourceFile.CommentDirectives = _scanner.GetCommentDirectives() is { } commentDirectives ? [.. commentDirectives] : null;
        // PORT-NOTE: TS counts every node its base factory creates, through the `countNode` wrapper around
        // `baseNodeFactory` — which the port does not have (§15.2). The number feeds `--extendedDiagnostics` only
        // (no baseline compares it), so it stays 0 until a creation hook is worth adding; `identifierCount` is
        // counted by the parser itself and is exact.
        sourceFile.NodeCount = 0;
        sourceFile.IdentifierCount = _identifierCount;
        sourceFile.Identifiers = _identifiers;
        sourceFile.ParseDiagnostics = Utilities.AttachFileToDiagnostics(_parseDiagnostics, sourceFile);
        sourceFile.JsDocParsingMode = _jsDocParsingMode;
        if (_jsDocDiagnostics is not null)
        {
            sourceFile.JsDocDiagnostics = Utilities.AttachFileToDiagnostics(_jsDocDiagnostics, sourceFile);
        }

        if (setParentNodes)
        {
            FixupParentReferences(sourceFile);
        }

        return sourceFile;

        void ReportPragmaDiagnostic(int pos, int end, DiagnosticMessage diagnostic)
        {
            _parseDiagnostics.Add(Utilities.CreateDetachedDiagnostic(_fileName, _sourceText, pos, end, diagnostic));
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:1780-1800 — <c>clearState</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS clears the shared state so a long-lived process does not keep a source file alive through it. The
    /// port's state belongs to this instance, so the only thing left to do is drop the references and let the instance
    /// go — which is what this does (the scanner included, since it holds the text).
    /// </remarks>
    private void ClearState()
    {
        _scanner.ClearCommentDirectives();
        _scanner.SetText("", null, null);
        _scanner.SetOnError(null);
        _scanner.SetScriptKind(ScriptKind.Unknown);
        _scanner.SetJSDocParsingMode(JSDocParsingMode.ParseAll);
        _syntaxCursor = null;
        _parseDiagnostics = [];
        _jsDocDiagnostics = null;
        _identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>ported from: src/compiler/parser.ts:1978-2015 — <c>createSourceFile</c>.</summary>
    /// <remarks>
    /// The body of TS's <c>createNode</c> is inlined here the same way, because a source file is created once per parse
    /// while <c>createNode</c> runs for every node.
    /// </remarks>
    private SourceFile CreateSourceFile(
        string fileName,
        ScriptTarget languageVersion,
        ScriptKind scriptKind,
        bool isDeclarationFile,
        List<IStatement> statements,
        Token endOfFileToken,
        NodeFlags flags,
        Action<SourceFile> setExternalModuleIndicator)
    {
        var sourceFile = _factory.CreateSourceFile(statements, endOfFileToken, flags);
        Utilities.SetTextRangePosWidth(sourceFile, 0, _sourceText.Length);
        SetFields(sourceFile);

        // If we parsed this as an external module, it may contain top-level await.
        if (!isDeclarationFile
            && IsExternalModule(sourceFile)
            && (sourceFile.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait) != 0)
        {
            var oldSourceFile = sourceFile;
            sourceFile = ReparseTopLevelAwait(sourceFile);
            if (!ReferenceEquals(oldSourceFile, sourceFile)) SetFields(sourceFile);
        }

        return sourceFile;

        void SetFields(SourceFile file)
        {
            file.Text = _sourceText;
            file.BindDiagnostics = [];
            file.BindSuggestionDiagnostics = null;
            file.LanguageVersion = languageVersion;
            file.FileName = fileName;
            file.LanguageVariant = Utilities.GetLanguageVariant(scriptKind);
            file.IsDeclarationFile = isDeclarationFile;
            file.ScriptKind = scriptKind;

            setExternalModuleIndicator(file);
            file.SetExternalModuleIndicator = setExternalModuleIndicator;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:1847-1861 — <c>withJSDoc</c>.</summary>
    private T WithJSDoc<T>(T node, bool hasJSDoc)
        where T : Node, IJSDocContainer
    {
        if (!hasJSDoc)
        {
            return node;
        }

        Debug.Assert(node.JsDoc is null); // Should only be called once per node
        var jsDoc = Core.MapDefined(
            Utilities.GetJSDocCommentRanges(node, _sourceText),
            (comment, _) => JSDocParser.ParseJSDocComment(this, node, comment.Pos, comment.End - comment.Pos));
        if (jsDoc.Count > 0) node.JsDoc = new JSDocArray(jsDoc);
        if (_hasDeprecatedTag)
        {
            _hasDeprecatedTag = false;
            node.Flags |= NodeFlags.Deprecated;
        }

        return node;
    }

    //
    // Context flags
    //

    /// <summary>ported from: src/compiler/parser.ts:2018-2025 — <c>setContextFlag</c>.</summary>
    private void SetContextFlag(bool val, NodeFlags flag)
    {
        if (val)
        {
            _contextFlags |= flag;
        }
        else
        {
            _contextFlags &= ~flag;
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:2043-2091 — <c>doOutsideOfContext</c> and friends.</summary>
    /// <remarks>
    /// Only the flags that are actually set are cleared and restored, so the cached flags the incremental parser relies
    /// on (<c>ThisNodeHasError</c>, <c>ThisNodeOrAnySubNodesHasError</c>, <c>HasAggregatedChildData</c>) are never
    /// mutated.
    /// </remarks>
    private T DoOutsideOfContext<T>(NodeFlags context, Func<T> func)
    {
        var contextFlagsToClear = context & _contextFlags;
        if (contextFlagsToClear != 0)
        {
            SetContextFlag(/*val*/ false, contextFlagsToClear);
            var result = func();
            SetContextFlag(/*val*/ true, contextFlagsToClear);
            return result;
        }

        return func();
    }

    /// <summary>ported from: src/compiler/parser.ts:2064-2077 — <c>doInsideOfContext</c>.</summary>
    private T DoInsideOfContext<T>(NodeFlags context, Func<T> func)
    {
        var contextFlagsToSet = context & ~_contextFlags;
        if (contextFlagsToSet != 0)
        {
            SetContextFlag(/*val*/ true, contextFlagsToSet);
            var result = func();
            SetContextFlag(/*val*/ false, contextFlagsToSet);
            return result;
        }

        return func();
    }

    private T AllowInAnd<T>(Func<T> func) => DoOutsideOfContext(NodeFlags.DisallowInContext, func);

    private T DisallowInAnd<T>(Func<T> func) => DoInsideOfContext(NodeFlags.DisallowInContext, func);

    private T AllowConditionalTypesAnd<T>(Func<T> func) => DoOutsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);

    private T DisallowConditionalTypesAnd<T>(Func<T> func) => DoInsideOfContext(NodeFlags.DisallowConditionalTypesContext, func);

    private T DoInYieldContext<T>(Func<T> func) => DoInsideOfContext(NodeFlags.YieldContext, func);

    private T DoInDecoratorContext<T>(Func<T> func) => DoInsideOfContext(NodeFlags.DecoratorContext, func);

    private T DoInAwaitContext<T>(Func<T> func) => DoInsideOfContext(NodeFlags.AwaitContext, func);

    private T DoOutsideOfAwaitContext<T>(Func<T> func) => DoOutsideOfContext(NodeFlags.AwaitContext, func);

    private T DoInYieldAndAwaitContext<T>(Func<T> func) => DoInsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);

    private T DoOutsideOfYieldAndAwaitContext<T>(Func<T> func) => DoOutsideOfContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, func);

    private bool InContext(NodeFlags flags) => (_contextFlags & flags) != 0;

    private bool InYieldContext() => InContext(NodeFlags.YieldContext);

    private bool InDisallowInContext() => InContext(NodeFlags.DisallowInContext);

    private bool InDisallowConditionalTypesContext() => InContext(NodeFlags.DisallowConditionalTypesContext);

    private bool InDecoratorContext() => InContext(NodeFlags.DecoratorContext);

    private bool InAwaitContext() => InContext(NodeFlags.AwaitContext);

    //
    // Errors
    //

    /// <summary>ported from: src/compiler/parser.ts:2149-2151 — <c>parseErrorAtCurrentToken</c>.</summary>
    private Diagnostic? ParseErrorAtCurrentToken(DiagnosticMessage message, params object?[] args)
        => ParseErrorAt(_scanner.GetTokenStart(), _scanner.GetTokenEnd(), message, args);

    /// <summary>ported from: src/compiler/parser.ts:2153-2166 — <c>parseErrorAtPosition</c>.</summary>
    /// <remarks>
    /// A second error at the same position as the last one is dropped, and either way the parse is marked as having had
    /// an error before the next finished node, so that node cannot be reused incrementally.
    /// </remarks>
    private Diagnostic? ParseErrorAtPosition(int start, int length, DiagnosticMessage message, params object?[] args)
    {
        var lastError = Core.LastOrUndefined(_parseDiagnostics);
        Diagnostic? result = null;
        if (lastError is null || start != lastError.Start)
        {
            result = Utilities.CreateDetachedDiagnostic(_fileName, _sourceText, start, length, message, args);
            _parseDiagnostics.Add(result);
        }

        _parseErrorBeforeNextFinishedNode = true;
        return result;
    }

    /// <summary>ported from: src/compiler/parser.ts:2168-2170 — <c>parseErrorAt</c>.</summary>
    private Diagnostic? ParseErrorAt(int start, int end, DiagnosticMessage message, params object?[] args)
        => ParseErrorAtPosition(start, end - start, message, args);

    /// <summary>ported from: src/compiler/parser.ts:2172-2174 — <c>parseErrorAtRange</c>.</summary>
    private void ParseErrorAtRange(ITextRange range, DiagnosticMessage message, params object?[] args)
        => ParseErrorAt(range.Pos, range.End, message, args);

    //
    // Tokens
    //

    /// <summary>ported from: src/compiler/parser.ts:2180-2182 — <c>getNodePos</c>.</summary>
    private int GetNodePos() => _scanner.GetTokenFullStart();

    /// <summary>ported from: src/compiler/parser.ts:2184-2186 — <c>hasPrecedingJSDocComment</c>.</summary>
    private bool HasPrecedingJSDocComment() => _scanner.HasPrecedingJSDocComment();

    /// <summary>ported from: src/compiler/parser.ts:2194-2196 — <c>token</c>.</summary>
    /// <remarks>
    /// TS reads the current token through this function rather than the variable, so that a call to <c>nextToken</c>
    /// cannot be reasoned away by flow analysis. The port keeps the accessor for the same reason — and because the
    /// area files read better as <c>Token() == SyntaxKind.X</c>.
    /// </remarks>
    private SyntaxKind Token() => _currentToken;

    /// <summary>ported from: src/compiler/parser.ts:2198-2200 — <c>nextTokenWithoutCheck</c>.</summary>
    private SyntaxKind NextTokenWithoutCheck() => _currentToken = _scanner.Scan();

    /// <summary>ported from: src/compiler/parser.ts:2202-2205 — <c>nextTokenAnd</c>.</summary>
    private T NextTokenAnd<T>(Func<T> func)
    {
        NextToken();
        return func();
    }

    /// <summary>ported from: src/compiler/parser.ts:2207-2214 — <c>nextToken</c>.</summary>
    private SyntaxKind NextToken()
    {
        // If the keyword had an escape, issue a parse error for it.
        if (Utilities.IsKeyword(_currentToken) && (_scanner.HasUnicodeEscape() || _scanner.HasExtendedUnicodeEscape()))
        {
            ParseErrorAt(_scanner.GetTokenStart(), _scanner.GetTokenEnd(), Diagnostics.Keywords_cannot_contain_escape_characters);
        }

        return NextTokenWithoutCheck();
    }

    /// <summary>ported from: src/compiler/parser.ts:2216-2218 — <c>nextTokenJSDoc</c>.</summary>
    private SyntaxKind NextTokenJSDoc() => _currentToken = _scanner.ScanJsDocToken();

    /// <summary>ported from: src/compiler/parser.ts:2220-2222 — <c>nextJSDocCommentTextToken</c>.</summary>
    private SyntaxKind NextJSDocCommentTextToken(bool inBackticks) => _currentToken = _scanner.ScanJSDocCommentTextToken(inBackticks);

    /// <summary>ported from: src/compiler/parser.ts:2224-2254 — the <c>reScan*</c>/<c>scanJsx*</c> wrappers.</summary>
    private SyntaxKind ReScanGreaterToken() => _currentToken = _scanner.ReScanGreaterToken();

    private SyntaxKind ReScanSlashToken() => _currentToken = _scanner.ReScanSlashToken();

    private SyntaxKind ReScanTemplateToken(bool isTaggedTemplate) => _currentToken = _scanner.ReScanTemplateToken(isTaggedTemplate);

    private SyntaxKind ReScanLessThanToken() => _currentToken = _scanner.ReScanLessThanToken();

    private SyntaxKind ReScanHashToken() => _currentToken = _scanner.ReScanHashToken();

    private SyntaxKind ScanJsxIdentifier() => _currentToken = _scanner.ScanJsxIdentifier();

    /// <remarks>
    /// PORT-NOTE: TS's `scanJsxText` calls <c>scanner.scanJsxToken()</c> — the *forward* scan — while
    /// <c>scanner.reScanJsxToken()</c> first rewinds to the token's full start. The two differ at every call site, which
    /// runs just after a <c>&gt;</c> or a <c>}</c>: a re-scan would swallow that character into the JSX text.
    /// </remarks>
    private SyntaxKind ScanJsxText() => _currentToken = _scanner.ScanJsxToken();

    private SyntaxKind ScanJsxAttributeValue() => _currentToken = _scanner.ScanJsxAttributeValue();

    //
    // Speculation
    //

    /// <summary>ported from: src/compiler/parser.ts:2256-2293 — <c>speculationHelper</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>if (!result || …)</c> — a *truthiness* test, so a callback that answered <c>false</c>,
    /// <c>0</c> or <c>""</c> makes the parse roll back too. The port keeps that test (<see cref="Js.IsTruthy"/>) rather
    /// than comparing against null, and the type parameter is left unconstrained because TS calls
    /// <c>lookAhead(() =&gt; someBoolean())</c> as well as <c>lookAhead(() =&gt; parseSomething())</c>.
    /// </remarks>
    private T SpeculationHelper<T>(Func<T> callback, SpeculationKind speculationKind)
    {
        // Keep track of the state we'll need to roll back to if lookahead fails (or if the caller asked us to always
        // reset our state).
        var saveToken = _currentToken;
        var saveParseDiagnosticsLength = _parseDiagnostics.Count;
        var saveParseErrorBeforeNextFinishedNode = _parseErrorBeforeNextFinishedNode;

        // Note: it is not actually necessary to save/restore the context flags here. That's because the
        // saving/restoring of these flags happens naturally through the recursive descent nature of our parser.
        // However, we still store this here just so we can assert that invariant holds.
        var saveContextFlags = _contextFlags;

        // If we're only looking ahead, then tell the scanner to only look ahead as well. Otherwise, if we're actually
        // speculatively parsing, then tell the scanner to do the same.
        var result = speculationKind != SpeculationKind.TryParse
            ? _scanner.LookAhead(callback)
            : _scanner.TryScan(callback);

        Debug.Assert(saveContextFlags == _contextFlags);

        // If our callback returned something 'falsy' or we're just looking ahead, then unconditionally restore us to
        // where we were.
        if (!Js.IsTruthy(result) || speculationKind != SpeculationKind.TryParse)
        {
            _currentToken = saveToken;
            if (speculationKind != SpeculationKind.Reparse)
            {
                _parseDiagnostics.RemoveRange(saveParseDiagnosticsLength, _parseDiagnostics.Count - saveParseDiagnosticsLength);
            }

            _parseErrorBeforeNextFinishedNode = saveParseErrorBeforeNextFinishedNode;
        }

        return result;
    }

    /// <summary>
    /// Invokes the callback and then unconditionally restores the parser state (TS: <c>lookAhead</c>, parser.ts:2295).
    /// </summary>
    private T LookAhead<T>(Func<T> callback) => SpeculationHelper(callback, SpeculationKind.Lookahead);

    /// <summary>
    /// Invokes the callback, restoring the parser state unless it returned something truthy (TS: <c>tryParse</c>,
    /// parser.ts:2304).
    /// </summary>
    private T TryParse<T>(Func<T> callback) => SpeculationHelper(callback, SpeculationKind.TryParse);

    /// <summary>ported from: src/compiler/parser.ts:2308-2316 — <c>isBindingIdentifier</c>.</summary>
    private bool IsBindingIdentifier()
    {
        if (Token() == SyntaxKind.Identifier)
        {
            return true;
        }

        // `let await`/`let yield` in [Yield] or [Await] are allowed here and disallowed in the binder.
        return Token() > SyntaxKind.LastReservedWord;
    }

    /// <summary>ported from: src/compiler/parser.ts:2318-2337 — <c>isIdentifier</c>.</summary>
    /// <remarks>
    /// The strict-mode flag is deliberately ignored: the type checker reports that, and the parser must not depend on
    /// context it does not have.
    /// </remarks>
    private bool IsIdentifier()
    {
        if (Token() == SyntaxKind.Identifier)
        {
            return true;
        }

        // If we have a 'yield' keyword, and we're in the [yield] context, then 'yield' is considered a keyword and is
        // not an identifier.
        if (Token() == SyntaxKind.YieldKeyword && InYieldContext())
        {
            return false;
        }

        // If we have an 'await' keyword, and we're in the [Await] context, then 'await' is considered a keyword and is
        // not an identifier.
        if (Token() == SyntaxKind.AwaitKeyword && InAwaitContext())
        {
            return false;
        }

        return Token() > SyntaxKind.LastReservedWord;
    }

    /// <summary>ported from: src/compiler/parser.ts:2338-2362 — <c>parseExpected</c>.</summary>
    private bool ParseExpected(SyntaxKind kind, DiagnosticMessage? diagnosticMessage = null, bool shouldAdvance = true)
    {
        if (Token() == kind)
        {
            if (shouldAdvance)
            {
                NextToken();
            }

            return true;
        }

        // Report the specific message if one was provided. Otherwise, report the generic fallback message.
        if (diagnosticMessage is not null)
        {
            ParseErrorAtCurrentToken(diagnosticMessage);
        }
        else
        {
            ParseErrorAtCurrentToken(Diagnostics._0_expected, ScannerUtilities.TokenToString(kind));
        }

        return false;
    }

    /// <summary>ported from: src/compiler/parser.ts:2366 — <c>viableKeywordSuggestions</c>.</summary>
    private static readonly List<string> s_viableKeywordSuggestions =
        [.. ScannerTables.TextToKeyword.Where(static keyword => keyword.Text.Length > 2).Select(static keyword => keyword.Text)];
}
