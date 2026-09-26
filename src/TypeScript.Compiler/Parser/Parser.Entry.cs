using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/parser.ts:1344-1436 (module-level entries), 1647-1733 (JSON path), 462-503 (module detection),
//              1862-1968 (reparseTopLevelAwait), 10556-10800 (pragma processing), 10800-10823 (tagNamesAreEquivalent)
/// <summary>
/// The parser's module-level entry points and the pragma/reparse machinery that sits outside the naive parse.
/// </summary>
internal sealed partial class Parser
{
    //
    // Entry points
    //

    /// <summary>ported from: src/compiler/parser.ts:1344-1370 — <c>createSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS takes <c>ScriptTarget | CreateSourceFileOptions</c> in one parameter and discriminates with
    /// <c>typeof … === "object"</c>; C# gets two overloads instead. The <c>tracing?.push/pop</c> and
    /// <c>performance.mark/measure</c> calls around the parse are kept as the port's own hooks: <c>Performance</c> is
    /// ported (M2c) while <c>tracing.ts</c> is deferred (§3).
    /// </remarks>
    public static SourceFile CreateSourceFile(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        bool setParentNodes = false,
        ScriptKind? scriptKind = null)
        => CreateSourceFile(fileName, sourceText, new CreateSourceFileOptions { LanguageVersion = languageVersion }, setParentNodes, scriptKind);

    /// <summary>ported from: src/compiler/parser.ts:1344-1370 — the <c>CreateSourceFileOptions</c> overload.</summary>
    public static SourceFile CreateSourceFile(
        string fileName,
        string sourceText,
        CreateSourceFileOptions options,
        bool setParentNodes = false,
        ScriptKind? scriptKind = null)
    {
        Performance.Mark("beforeParse");
        SourceFile result;

        var languageVersion = options.LanguageVersion;
        if (languageVersion == ScriptTarget.JSON)
        {
            result = ParseSourceFile(
                fileName,
                sourceText,
                languageVersion,
                /*syntaxCursor*/ null,
                setParentNodes,
                ScriptKind.JSON,
                FileMetadataNoop,
                options.JsDocParsingMode);
        }
        else
        {
            var format = options.ImpliedNodeFormat;
            var setIndicator = format is null
                ? options.SetExternalModuleIndicator
                : file =>
                {
                    file.ImpliedNodeFormat = format;
                    (options.SetExternalModuleIndicator ?? SetExternalModuleIndicatorDefault)(file);
                };
            result = ParseSourceFile(
                fileName,
                sourceText,
                languageVersion,
                /*syntaxCursor*/ null,
                setParentNodes,
                scriptKind,
                setIndicator,
                options.JsDocParsingMode);
        }

        Performance.Mark("afterParse");
        Performance.Measure("Parse", "beforeParse", "afterParse");
        return result;
    }

    /// <summary>The <c>noop</c> TS passes where a file must not be marked as a module.</summary>
    private static void FileMetadataNoop(SourceFile file)
    {
    }

    /// <summary>ported from: src/compiler/parser.ts:1381-1383 — <c>parseJsonText</c>.</summary>
    public static SourceFile ParseJsonText(string fileName, string sourceText)
        => ParseJsonText(fileName, sourceText, ScriptTarget.ES2015, null, false);

    /// <summary>ported from: src/compiler/parser.ts:1647-1733 — <c>parseJsonText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's version is a member of the <c>Parser</c> namespace, so it reads and writes the shared parse
    /// state; the port splits it into this static front door (which owns the state, as a <see cref="Parser"/> instance)
    /// and <c>ParseJsonTextWorker</c>.
    /// </remarks>
    public static SourceFile ParseJsonText(
        string fileName,
        string sourceText,
        ScriptTarget languageVersion,
        IncrementalParser.ISyntaxCursor? syntaxCursor,
        bool setParentNodes)
    {
        var parser = new Parser(fileName, sourceText, languageVersion, syntaxCursor, ScriptKind.JSON, JSDocParsingMode.ParseAll);
        var result = parser.ParseJsonTextWorker(setParentNodes);
        parser.ClearState();
        return result;
    }

    /// <summary>ported from: src/compiler/parser.ts:1647-1733 — the body of <c>parseJsonText</c>.</summary>
    private SourceFile ParseJsonTextWorker(bool setParentNodes)
    {
        _sourceFlags = _contextFlags;

        // Prime the scanner.
        NextToken();
        var pos = GetNodePos();
        List<IStatement> statements;
        Token endOfFileToken;
        if (Token() == SyntaxKind.EndOfFileToken)
        {
            statements = [.. CreateNodeArray<IStatement>([], pos, pos)];
            endOfFileToken = ParseTokenNode<Token>();
        }
        else
        {
            // Loop and synthesize an ArrayLiteralExpression if there is more than one top-level expression, so that all
            // input text is consumed.
            List<Expression>? expressionList = null;
            Expression? singleExpression = null;
            while (Token() != SyntaxKind.EndOfFileToken)
            {
                Expression expression;
                switch (Token())
                {
                    case SyntaxKind.OpenBracketToken:
                        expression = ParseArrayLiteralExpression();
                        break;

                    case SyntaxKind.TrueKeyword:
                    case SyntaxKind.FalseKeyword:
                    case SyntaxKind.NullKeyword:
                        // PORT-NOTE: TS writes `parseTokenNode<BooleanLiteral | NullLiteral>()`; the three classes share
                        // no C# base narrower than MemberExpression, which is what the union maps to (§12.5).
                        expression = ParseTokenNode<MemberExpression>();
                        break;

                    case SyntaxKind.MinusToken:
                        if (LookAhead(() => NextToken() == SyntaxKind.NumericLiteral && NextToken() != SyntaxKind.ColonToken))
                        {
                            expression = ParsePrefixUnaryExpression();
                        }
                        else
                        {
                            expression = ParseObjectLiteralExpression();
                        }

                        break;

                    case SyntaxKind.NumericLiteral:
                    case SyntaxKind.StringLiteral:
                        if (LookAhead(() => NextToken() != SyntaxKind.ColonToken))
                        {
                            expression = ParseLiteralNode();
                            break;
                        }

                        // falls through
                        goto default;

                    default:
                        expression = ParseObjectLiteralExpression();
                        break;
                }

                // Error recovery: collect multiple top-level expressions.
                if (expressionList is not null)
                {
                    expressionList.Add(expression);
                }
                else if (singleExpression is not null)
                {
                    expressionList = [singleExpression, expression];
                }
                else
                {
                    singleExpression = expression;
                    if (Token() != SyntaxKind.EndOfFileToken)
                    {
                        ParseErrorAtCurrentToken(Diagnostics.Unexpected_token);
                    }
                }
            }

            var expressionNode = expressionList is not null
                ? FinishNode(_factory.CreateArrayLiteralExpression(expressionList, null), pos)
                : Debug.CheckDefined(singleExpression);
            var statement = _factory.CreateExpressionStatement(expressionNode);
            FinishNode(statement, pos);
            statements = [statement];
            endOfFileToken = (Token)ParseExpectedToken(SyntaxKind.EndOfFileToken, Diagnostics.Unexpected_token)!;
        }

        // Set the source file so that errors are reported with this file name.
        var sourceFile = CreateSourceFile(
            _fileName,
            ScriptTarget.ES2015,
            ScriptKind.JSON,
            /*isDeclarationFile*/ false,
            statements,
            endOfFileToken,
            _sourceFlags,
            FileMetadataNoop);

        if (setParentNodes)
        {
            FixupParentReferences(sourceFile);
        }

        sourceFile.NodeCount = 0; // see the PORT-NOTE in ParseSourceFileWorker
        sourceFile.IdentifierCount = _identifierCount;
        sourceFile.Identifiers = _identifiers;
        sourceFile.ParseDiagnostics = Utilities.AttachFileToDiagnostics(_parseDiagnostics, sourceFile);
        if (_jsDocDiagnostics is not null)
        {
            sourceFile.JsDocDiagnostics = Utilities.AttachFileToDiagnostics(_jsDocDiagnostics, sourceFile);
        }

        return sourceFile;
    }

    /// <summary>ported from: src/compiler/parser.ts:1386-1388 — <c>isExternalModule</c>.</summary>
    /// <remarks>See also <c>isExternalOrCommonJsModule</c> in utilities.ts.</remarks>
    public static bool IsExternalModule(SourceFile file) => file.ExternalModuleIndicator is not null;

    /// <summary>ported from: src/compiler/parser.ts:1399-1407 — <c>updateSourceFile</c>.</summary>
    /// <remarks>
    /// This mutates the nodes of <paramref name="sourceFile"/>: the tree is reused in place, so the old file must not be
    /// used afterwards.
    /// </remarks>
    public static SourceFile UpdateSourceFile(SourceFile sourceFile, string newText, ITextChangeRange textChangeRange, bool aggressiveChecks = false)
    {
        var newSourceFile = IncrementalParser.UpdateSourceFile(sourceFile, newText, textChangeRange, aggressiveChecks);

        // Because a new source file node is created, it may not have the PossiblyContainDynamicImport flag — that happens
        // when no edit added a dynamic import. Port the flag over by hand.
        newSourceFile.Flags |= sourceFile.Flags & NodeFlags.PermanentlySetIncrementalFlags;
        return newSourceFile;
    }

    /// <summary>ported from: src/compiler/parser.ts:1414-1425 — <c>parseIsolatedJSDocComment</c>.</summary>
    public static JsDocWithDiagnostics? ParseIsolatedJSDocComment(string content, int? start = null, int? length = null)
    {
        var result = JSDocParser.ParseIsolatedJSDocComment(content, start, length);
        if (result is not null && result.JsDoc is not null)
        {
            // The JSDoc comment was parsed out of the source file, so it might not be covered by the
            // fixupParentReferences the file-level parse runs.
            FixupParentReferences(result.JsDoc);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/parser.ts:1427-1435 — <c>parseJSDocTypeExpressionForTests</c>.</summary>
    /// <remarks>Exposed for testing only, as in TS.</remarks>
    public static (JSDocTypeExpression JsDocTypeExpression, List<Diagnostic> Diagnostics)? ParseJSDocTypeExpressionForTests(
        string content,
        int? start = null,
        int? length = null)
        => JSDocParser.ParseJSDocTypeExpressionForTests(content, start, length);

    //
    // Module detection
    //

    /// <summary>ported from: src/compiler/parser.ts:462-467 — <c>isJSDocLikeText</c>.</summary>
    public static bool IsJSDocLikeText(string text, int start)
        => CharCodeAt(text, start + 1) == CharacterCodes.Asterisk
            && CharCodeAt(text, start + 2) == CharacterCodes.Asterisk
            && CharCodeAt(text, start + 3) != CharacterCodes.Slash;

    /// <summary>ported from: src/compiler/parser.ts:469-476 — <c>isFileProbablyExternalModule</c>.</summary>
    /// <remarks>
    /// The first top-level import/export decides it when there is one; otherwise the tree is searched for an
    /// <c>import.meta</c>, which only happens when the parser recorded that the file might contain one.
    /// </remarks>
    public static Node? IsFileProbablyExternalModule(SourceFile sourceFile)
    {
        if (sourceFile.Statements is { } statements)
        {
            foreach (var statement in statements)
            {
                if (IsAnExternalModuleIndicatorNode(statement) is { } indicator)
                {
                    return indicator;
                }
            }
        }

        return GetImportMetaIfNecessary(sourceFile);
    }

    /// <summary>ported from: src/compiler/parser.ts:478-483 — <c>isAnExternalModuleIndicatorNode</c>.</summary>
    private static Node? IsAnExternalModuleIndicatorNode(IStatement node)
    {
        if (node is not Node candidate) return null;

        bool isIndicator =
            (Utilities.CanHaveModifiers(candidate) && HasModifierOfKind(candidate, SyntaxKind.ExportKeyword))
            || (NodeTests.IsImportEqualsDeclaration(candidate)
                && NodeTests.IsExternalModuleReference(((ImportEqualsDeclaration)candidate).ModuleReference!))
            || NodeTests.IsImportDeclaration(candidate)
            || NodeTests.IsExportAssignment(candidate)
            || NodeTests.IsExportDeclaration(candidate);

        return isIndicator ? candidate : null;
    }

    /// <summary>ported from: src/compiler/parser.ts:485-489 — <c>getImportMetaIfNecessary</c>.</summary>
    private static Node? GetImportMetaIfNecessary(SourceFile sourceFile)
        => (sourceFile.Flags & NodeFlags.PossiblyContainsImportMeta) != 0 ? WalkTreeForImportMeta(sourceFile) : null;

    /// <summary>ported from: src/compiler/parser.ts:491-493 — <c>walkTreeForImportMeta</c>.</summary>
    private static Node? WalkTreeForImportMeta(Node node)
        => IsImportMeta(node) ? node : ForEachChildTable.ForEachChild<Node>(node, WalkTreeForImportMeta);

    /// <summary>ported from: src/compiler/parser.ts:496-498 — <c>hasModifierOfKind</c>.</summary>
    /// <remarks>
    /// Do not use <c>hasModifier</c> inside the parser: it relies on parent pointers.
    /// <para>
    /// PORT-NOTE: TS reaches this through the <c>HasModifiers</c> alias — a union of ~30 types that each declare
    /// <c>modifiers</c> — and the port has no interface for it, because the alias has no C# counterpart and the mapper
    /// put the member on each class. The switch here is that union written out; it covers exactly the kinds
    /// <c>Utilities.CanHaveModifiers</c> answers true for, and a kind that can have modifiers but is missing here would
    /// silently look like it has none — §15.10 records the alternative (a generated <c>IHasModifiers</c> anchor, like the
    /// <c>INode</c> one §12.9 added).
    /// </para>
    /// </remarks>
    private static bool HasModifierOfKind(Node node, SyntaxKind kind)
        => HasModifier(ModifiersOf(node), kind);

    /// <summary>Whether one of <paramref name="modifiers"/> is <paramref name="kind"/>.</summary>
    private static bool HasModifier(IEnumerable<Node>? modifiers, SyntaxKind kind)
    {
        if (modifiers is null) return false;

        foreach (var modifier in modifiers)
        {
            if (modifier.Kind == kind) return true;
        }

        return false;
    }

    /// <summary>The <c>modifiers</c> slot of the nodes that have one (see <see cref="HasModifierOfKind"/>).</summary>
    private static IEnumerable<Node>? ModifiersOf(Node node) => node switch
    {
        TypeParameterDeclaration n => n.Modifiers,
        ParameterDeclaration n => n.Modifiers,
        PropertySignature n => n.Modifiers,
        PropertyDeclaration n => n.Modifiers,
        MethodSignature n => n.Modifiers,
        MethodDeclaration n => n.Modifiers,
        ConstructorDeclaration n => n.Modifiers,
        GetAccessorDeclaration n => n.Modifiers,
        SetAccessorDeclaration n => n.Modifiers,
        IndexSignatureDeclaration n => n.Modifiers,
        FunctionExpression n => n.Modifiers,
        ArrowFunction n => n.Modifiers,
        ClassExpression n => n.Modifiers,
        VariableStatement n => n.Modifiers,
        FunctionDeclaration n => n.Modifiers,
        ClassDeclaration n => n.Modifiers,
        InterfaceDeclaration n => n.Modifiers,
        TypeAliasDeclaration n => n.Modifiers,
        EnumDeclaration n => n.Modifiers,
        ModuleDeclaration n => n.Modifiers,
        ImportEqualsDeclaration n => n.Modifiers,
        ImportDeclaration n => n.Modifiers,
        ExportAssignment n => n.Modifiers,
        ExportDeclaration n => n.Modifiers,
        _ => null,
    };

    /// <summary>ported from: src/compiler/parser.ts:500-502 — <c>isImportMeta</c>.</summary>
    private static bool IsImportMeta(Node node)
        => node is MetaProperty meta
            && meta.KeywordToken == SyntaxKind.ImportKeyword
            && ((Identifier)meta.Name!).EscapedText == "meta";

    /// <summary>ported from: src/compiler/utilities.ts — the <c>charCodeAt</c> read the JSDoc check needs.</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>charCodeAt</c> answers <c>NaN</c> past the end, which compares unequal to every character code;
    /// the port's <see cref="ScannerUtilities"/> has the same mapping but is not public for a plain string read, so the
    /// private helper is repeated here rather than widening that type.
    /// </remarks>
    private static int CharCodeAt(string text, int index) => index >= 0 && index < text.Length ? text[index] : -1;

    //
    // Declaration file names
    //

    /// <summary>ported from: src/compiler/parser.ts:10556-10558 — <c>isDeclarationFileName</c>.</summary>
    public static bool IsDeclarationFileName(string fileName) => Paths.GetDeclarationFileExtension(fileName) is not null;

    /// <remarks>
    /// PORT-NOTE: the body lives in <c>Core/Paths.GetDeclarationFileExtension</c>, not here. TS's <c>path.ts</c> imports
    /// <c>getDeclarationFileExtension</c> from <c>parser.ts</c> (path.ts:12, for <c>changeFullExtension</c>), an upward
    /// edge a C# assembly cannot express, so the single copy sits in the lower layer and both callers share it. The M4e
    /// slice had a second copy here, and it tested <c>is not null</c> against a helper that answers <c>""</c> for "no
    /// extension" — which made <b>every</b> file a declaration file, so every parsed file's nodes were ambient,
    /// implicitly exported and unbound from their bodies. See §15.10.
    /// </remarks>

    /// <summary>ported from: src/compiler/parser.ts:10577-10589 — <c>parseResolutionMode</c>.</summary>
    private static object? ParseResolutionMode(string? mode, int pos, int end, PragmaDiagnosticReporter reportDiagnostic)
    {
        if (string.IsNullOrEmpty(mode)) return null;
        if (mode == "import") return ModuleKind.ESNext;
        if (mode == "require") return ModuleKind.CommonJS;

        reportDiagnostic(pos, end - pos, Diagnostics.resolution_mode_should_be_either_require_or_import);
        return null;
    }

    //
    // Comment pragmas
    //

    /// <summary>ported from: src/compiler/parser.ts:10592-10613 — <c>processCommentPragmas</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>PragmaContext</c> and its call site casts a source file into it
    /// (<c>sourceFile as {} as PragmaContext</c>), because a <c>SourceFile</c> is not declared as one. In the port
    /// <c>SourceFile</c> implements <c>IReadonlyPragmaContext</c> — the interface that actually carries the eight
    /// settable slots this function touches — so the parameter is that type and no cast is needed.
    /// </remarks>
    public static void ProcessCommentPragmas(IReadonlyPragmaContext context, string sourceText)
    {
        var pragmas = new List<PragmaPseudoMapEntry>();

        foreach (var range in ScannerUtilities.GetLeadingCommentRanges(sourceText, 0) ?? [])
        {
            var comment = sourceText[range.Pos..range.End];
            ExtractPragmas(pragmas, range, comment);
        }

        var pragmaMap = new PragmaMap();
        context.Pragmas = pragmaMap;
        foreach (var pragma in pragmas)
        {
            var currentValue = pragmaMap.Get(pragma.Name);
            if (currentValue is null)
            {
                pragmaMap.Set(pragma.Name, pragma);
            }
            else if (currentValue is List<PragmaPseudoMapEntry> existing)
            {
                existing.Add(pragma);
            }
            else
            {
                pragmaMap.Set(pragma.Name, new List<PragmaPseudoMapEntry> { (PragmaPseudoMapEntry)currentValue, pragma });
            }
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:10619-10679 — <c>processPragmasIntoFields</c>.</summary>
    /// <remarks>
    /// Everything a triple-slash or `// @` pragma can set on a source file is assigned here: the reference directives,
    /// the AMD module name and dependencies, and the `@ts-check`/`@ts-nocheck` winner. The `jsx*` pragmas are read
    /// directly by the compiler options, so this skips them.
    /// </remarks>
    public static void ProcessPragmasIntoFields(IReadonlyPragmaContext context, PragmaDiagnosticReporter reportDiagnostic)
    {
        context.CheckJsDirective = null;
        context.ReferencedFiles = [];
        context.TypeReferenceDirectives = [];
        context.LibReferenceDirectives = [];
        context.AmdDependencies = [];

        // PORT-NOTE: TS iterates the map with `context.pragmas.forEach((entryOrList, key) => …)`; the generated
        // `IReadonlyPragmaMap.ForEach` takes its callback as `object?` (TS's generic callback has no C# counterpart), so
        // the delegate shape is spelled out here — `PragmaMap.ForEach` fails loudly on anything else.
        context.Pragmas ??= new PragmaMap();
        context.Pragmas.ForEach((Action<object?, string, PragmaMap>)((entryOrList, key, _) =>
        {
            switch (key)
            {
                case "reference":
                {
                    var referencedFiles = context.ReferencedFiles!;
                    var typeReferenceDirectives = context.TypeReferenceDirectives!;
                    var libReferenceDirectives = context.LibReferenceDirectives!;
                    foreach (var arg in ToPragmaEntries(entryOrList))
                    {
                        var arguments = arg.Arguments;
                        var typesSpan = GetPragmaArgument(arguments, "types");
                        var libSpan = GetPragmaArgument(arguments, "lib");
                        var pathSpan = GetPragmaArgument(arguments, "path");
                        var preserve = GetPragmaArgumentText(arguments, "preserve") == "true" ? true : (bool?)null;
                        if (GetPragmaArgumentText(arguments, "no-default-lib") == "true")
                        {
                            // This has been removed; parse it but ignore it.
                        }
                        else if (typesSpan is { } types)
                        {
                            var parsed = ParseResolutionMode(GetPragmaArgumentText(arguments, "resolution-mode"), types.Pos, types.End, reportDiagnostic);
                            typeReferenceDirectives.Add(new FileReference(types.Value, types.Pos, types.End)
                            {
                                ResolutionMode = parsed,
                                Preserve = preserve,
                            });
                        }
                        else if (libSpan is { } lib)
                        {
                            libReferenceDirectives.Add(new FileReference(lib.Value, lib.Pos, lib.End)
                            {
                                Preserve = preserve,
                            });
                        }
                        else if (pathSpan is { } path)
                        {
                            referencedFiles.Add(new FileReference(path.Value, path.Pos, path.End)
                            {
                                Preserve = preserve,
                            });
                        }
                        else
                        {
                            reportDiagnostic(arg.Range.Pos, arg.Range.End - arg.Range.Pos, Diagnostics.Invalid_reference_directive_syntax);
                        }
                    }

                    break;
                }

                case "amd-dependency":
                    context.AmdDependencies = [.. ToPragmaEntries(entryOrList)
                        .Select(entry => new AmdDependency(
                            GetPragmaArgumentText(entry.Arguments, "path"),
                            GetPragmaArgumentText(entry.Arguments, "name")))];
                    break;

                case "amd-module":
                    if (entryOrList is IEnumerable<PragmaPseudoMapEntry> entries)
                    {
                        foreach (var entry in entries)
                        {
                            if (context.ModuleName is not null)
                            {
                                reportDiagnostic(entry.Range.Pos, entry.Range.End - entry.Range.Pos, Diagnostics.An_AMD_module_cannot_have_multiple_name_assignments);
                            }

                            context.ModuleName = GetPragmaArgumentText(entry.Arguments, "name");
                        }
                    }
                    else if (entryOrList is PragmaPseudoMapEntry single)
                    {
                        context.ModuleName = GetPragmaArgumentText(single.Arguments, "name");
                    }

                    break;

                case "ts-nocheck":
                case "ts-check":
                    // The last of either in a file is the winner.
                    foreach (var entry in ToPragmaEntries(entryOrList))
                    {
                        if (context.CheckJsDirective is null || entry.Range.Pos > context.CheckJsDirective.Pos)
                        {
                            context.CheckJsDirective = new CheckJsDirective(key == "ts-check", entry.Range.Pos, entry.Range.End);
                        }
                    }

                    break;

                case "jsx":
                case "jsxfrag":
                case "jsximportsource":
                case "jsxruntime":
                    break; // Accessed directly.

                default:
                    Debug.Fail("Unhandled pragma kind");
                    break;
            }
        }));
    }

    /// <summary>The `toArray` a pragma entry-or-list needs (TS: `forEach(toArray(entryOrList), …)`).</summary>
    private static IEnumerable<PragmaPseudoMapEntry> ToPragmaEntries(object? entryOrList) => entryOrList switch
    {
        null => [],
        PragmaPseudoMapEntry single => [single],
        IEnumerable<PragmaPseudoMapEntry> many => many,
        _ => Debug.Fail<IEnumerable<PragmaPseudoMapEntry>>("Unexpected pragma entry shape"),
    };

    /// <summary>Reads one named argument out of a pragma entry, span and all.</summary>
    private static PragmaArgumentSpan? GetPragmaArgument(Dictionary<string, object?>? arguments, string name)
        => arguments is not null && arguments.TryGetValue(name, out var value) && value is PragmaArgumentSpan span ? span : null;

    /// <summary>Reads one named argument out of a pragma entry as plain text.</summary>
    private static string? GetPragmaArgumentText(Dictionary<string, object?>? arguments, string name)
        => GetPragmaArgument(arguments, name) is { } span
            ? span.Value
            : arguments is not null && arguments.TryGetValue(name, out var value) ? value as string : null;

    /// <summary>ported from: src/compiler/parser.ts:10709-10719 — <c>getNamedArgRegEx</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the pattern is built from an argument's name at runtime, so it cannot be a
    /// <c>[GeneratedRegex]</c> partial method (§9 rule 7 allows <c>new Regex</c> exactly here). The cache is TS's, kept
    /// so the same argument name is compiled once per process.
    /// </remarks>
    private static readonly Dictionary<string, Regex> s_namedArgRegExCache = new(StringComparer.Ordinal);

    private static Regex GetNamedArgRegEx(string name)
    {
        if (s_namedArgRegExCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var result = new Regex($"(\\s{name}\\s*=\\s*)(?:(?:'([^']*)')|(?:\"([^\"]*)\"))", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        s_namedArgRegExCache[name] = result;
        return result;
    }

    /// <summary>ported from: src/compiler/parser.ts:10710 — <c>tripleSlashXMLCommentStartRegEx</c>.</summary>
    [GeneratedRegex("^///\\s*<(\\S+)\\s.*?/>", RegexOptions.Multiline)]
    private static partial Regex TripleSlashXmlCommentStartRegEx();

    /// <summary>ported from: src/compiler/parser.ts:10711 — <c>singleLinePragmaRegEx</c>.</summary>
    [GeneratedRegex("^///?\\s*@([^\\s:]+)((?:[^\\S\\r\\n]|:).*)?$", RegexOptions.Multiline)]
    private static partial Regex SingleLinePragmaRegEx();

    /// <summary>ported from: src/compiler/parser.ts:10714-10761 — <c>extractPragmas</c>.</summary>
    private static void ExtractPragmas(List<PragmaPseudoMapEntry> pragmas, CommentRange range, string text)
    {
        var tripleSlash = range.Kind == SyntaxKind.SingleLineCommentTrivia ? TripleSlashXmlCommentStartRegEx().Match(text) : Match.Empty;
        if (tripleSlash.Success)
        {
            var name = tripleSlash.Groups[1].Value.ToLowerInvariant();
            if (!Utilities.CommentPragmas.TryGetValue(name, out var pragmaValue)
                || (PragmaKindOf((IPragmaDefinition<string, string, string, string>)pragmaValue) & PragmaKindFlags.TripleSlashXML) == 0)
            {
                return;
            }

            var pragma = (IPragmaDefinition<string, string, string, string>)pragmaValue;
            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (PragmaArgsOf(pragma) is { } pragmaArgs)
            {
                foreach (var arg in pragmaArgs)
                {
                    var matchResult = GetNamedArgRegEx(arg.Name!).Match(text);
                    if (!matchResult.Success && arg.Optional != true)
                    {
                        return; // Missing a required argument, so do not parse the pragma.
                    }

                    if (!matchResult.Success) continue;

                    var value = matchResult.Groups[2].Value.Length > 0 ? matchResult.Groups[2].Value : matchResult.Groups[3].Value;
                    if (arg.CaptureSpan == true)
                    {
                        var startPos = range.Pos + matchResult.Index + matchResult.Groups[1].Value.Length + 1;
                        arguments[arg.Name!] = new PragmaArgumentSpan(value, startPos, startPos + value.Length);
                    }
                    else
                    {
                        arguments[arg.Name!] = value;
                    }
                }
            }

            pragmas.Add(new PragmaPseudoMapEntry(name, arguments, range));
            return;
        }

        var singleLine = range.Kind == SyntaxKind.SingleLineCommentTrivia ? SingleLinePragmaRegEx().Match(text) : Match.Empty;
        if (singleLine.Success)
        {
            AddPragmaForMatch(pragmas, range, PragmaKindFlags.SingleLine, singleLine);
            return;
        }

        if (range.Kind == SyntaxKind.MultiLineCommentTrivia)
        {
            // The pattern is defined here because TS's version carries the "g" flag, which keeps a persistent index for
            // iteration; `Match.NextMatch()` is the port's way to walk the same text.
            var multiLinePragmaRegEx = new Regex("@(\\S+)(\\s+(?:\\S.*)?)?$", RegexOptions.Multiline);
            for (var multiLineMatch = multiLinePragmaRegEx.Match(text); multiLineMatch.Success; multiLineMatch = multiLineMatch.NextMatch())
            {
                AddPragmaForMatch(pragmas, range, PragmaKindFlags.MultiLine, multiLineMatch);
            }
        }
    }

    /// <summary>ported from: src/compiler/parser.ts:10763-10777 — <c>addPragmaForMatch</c>.</summary>
    private static void AddPragmaForMatch(List<PragmaPseudoMapEntry> pragmas, CommentRange range, PragmaKindFlags kind, Match match)
    {
        if (!match.Success) return;

        var name = match.Groups[1].Value.ToLowerInvariant();
        if (!Utilities.CommentPragmas.TryGetValue(name, out var pragmaValue)
            || (PragmaKindOf((IPragmaDefinition<string, string, string, string>)pragmaValue) & kind) == 0)
        {
            return;
        }

        var pragma = (IPragmaDefinition<string, string, string, string>)pragmaValue;

        var argument = GetNamedPragmaArguments(pragma, match.Groups[2].Value); // Split on spaces and match up positionally.
        if (argument is null) return; // Missing a required argument, so do not parse the pragma.

        pragmas.Add(new PragmaPseudoMapEntry(name, argument, range));
    }

    /// <summary>ported from: src/compiler/parser.ts:10779-10797 — <c>getNamedPragmaArguments</c>.</summary>
    /// <remarks>Answers null where TS answers <c>"fail"</c> — the port's spelling of "do not parse this pragma".</remarks>
    private static Dictionary<string, object?>? GetNamedPragmaArguments(IPragmaDefinition<string, string, string, string> pragma, string? text)
    {
        if (string.IsNullOrEmpty(text) || PragmaArgsOf(pragma) is null) return new Dictionary<string, object?>(StringComparer.Ordinal);

        var args = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var argMap = new Dictionary<string, object?>(StringComparer.Ordinal);
        var pragmaArgs = PragmaArgsOf(pragma)!;
        for (var i = 0; i < pragmaArgs.Count; i++)
        {
            var argument = pragmaArgs[i];
            if (i >= args.Length && argument.Optional != true)
            {
                return null;
            }

            if (argument.CaptureSpan == true)
            {
                Debug.Fail("Capture spans not yet implemented for non-xml pragmas");
            }

            argMap[argument.Name!] = i < args.Length ? args[i] : null;
        }

        return argMap;
    }

    /// <summary>ported from: src/compiler/parser.ts:10800-10823 — <c>tagNamesAreEquivalent</c>.</summary>
    public static bool TagNamesAreEquivalent(Node lhs, Node rhs)
    {
        if (lhs.Kind != rhs.Kind)
        {
            return false;
        }

        if (lhs.Kind == SyntaxKind.Identifier)
        {
            return ((Identifier)lhs).EscapedText == ((Identifier)rhs).EscapedText;
        }

        if (lhs.Kind == SyntaxKind.ThisKeyword)
        {
            return true;
        }

        if (lhs.Kind == SyntaxKind.JsxNamespacedName)
        {
            return ((JsxNamespacedName)lhs).Namespace!.EscapedText == ((JsxNamespacedName)rhs).Namespace!.EscapedText
                && ((JsxNamespacedName)lhs).Name!.EscapedText == ((JsxNamespacedName)rhs).Name!.EscapedText;
        }

        // At this point the tag name can only be a PropertyAccessExpression, because a JSX tag name is an identifier,
        // `this`, a namespaced name, or another property access — see parseJsxElementName.
        return Utilities.IdText(((PropertyAccessExpression)lhs).Name!) == Utilities.IdText(((PropertyAccessExpression)rhs).Name!)
            && TagNamesAreEquivalent(((PropertyAccessExpression)lhs).Expression!, ((PropertyAccessExpression)rhs).Expression!);
    }

    //
    // Reparsing top-level await
    //

    /// <summary>ported from: src/compiler/parser.ts:1862-1968 — <c>reparseTopLevelAwait</c>.</summary>
    /// <remarks>
    /// A file that turned out to be an external module may contain top-level <c>await</c>, which the first pass parsed
    /// without the [Await] context. This re-parses exactly the statements that could contain one, reusing the rest of the
    /// tree through the incremental cursor, and keeps the diagnostics that belonged to the copied statements.
    /// </remarks>
    private SourceFile ReparseTopLevelAwait(SourceFile sourceFile)
    {
        var savedSyntaxCursor = _syntaxCursor;
        var baseSyntaxCursor = IncrementalParser.CreateSyntaxCursor(sourceFile);
        _syntaxCursor = new ReparseCursor(this, baseSyntaxCursor);

        var statements = new List<IStatement>();
        var savedParseDiagnostics = _parseDiagnostics;
        _parseDiagnostics = [];

        var pos = 0;
        var start = FindNextStatementWithAwait(sourceFile.Statements!, 0);
        while (start != -1)
        {
            // Append every statement between pos and start.
            var prevStatement = sourceFile.Statements![pos];
            var nextStatement = sourceFile.Statements[start];
            Core.AddRange(statements, sourceFile.Statements, pos, start);
            pos = FindNextStatementWithoutAwait(sourceFile.Statements, start);

            // Append the diagnostics that belong to the copied range.
            var prevStatementPos = ((Node)prevStatement).Pos;
            var nextStatementPos = ((Node)nextStatement).Pos;
            var diagnosticStart = Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= prevStatementPos);
            var diagnosticEnd = diagnosticStart >= 0
                ? Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= nextStatementPos, diagnosticStart)
                : -1;
            if (diagnosticStart >= 0)
            {
                Core.AddRange(_parseDiagnostics, savedParseDiagnostics, diagnosticStart, diagnosticEnd >= 0 ? diagnosticEnd : null);
            }

            // Reparse every statement between start and pos. Existing diagnostics for the same range are skipped, and the
            // parser is free to produce new ones.
            SpeculationHelper<object?>(() =>
            {
                var savedContextFlags = _contextFlags;
                _contextFlags |= NodeFlags.AwaitContext;
                _scanner.ResetTokenState(nextStatementPos);
                NextToken();

                while (Token() != SyntaxKind.EndOfFileToken)
                {
                    var startPos = _scanner.GetTokenFullStart();
                    var statement = ParseListElement<IStatement>(ParsingContext.SourceElements, ParseStatement);
                    statements.Add(statement!);
                    if (startPos == _scanner.GetTokenFullStart())
                    {
                        NextToken();
                    }

                    if (pos >= 0)
                    {
                        var nonAwaitStatement = sourceFile.Statements![pos];
                        var statementEnd = ((Node)statement!).End;
                        if (statementEnd == ((Node)nonAwaitStatement).Pos)
                        {
                            // Done reparsing this section.
                            break;
                        }

                        if (statementEnd > ((Node)nonAwaitStatement).Pos)
                        {
                            // We ate into the next statement, so it must be reparsed too.
                            pos = FindNextStatementWithoutAwait(sourceFile.Statements, pos + 1);
                        }
                    }
                }

                _contextFlags = savedContextFlags;
                return null;
            }, SpeculationKind.Reparse);

            // Find the next statement containing an `await`.
            start = pos >= 0 ? FindNextStatementWithAwait(sourceFile.Statements, pos) : -1;
        }

        // Append every statement between pos and the end of the list.
        if (pos >= 0)
        {
            var prevStatement = sourceFile.Statements![pos];
            var prevStatementPos = ((Node)prevStatement).Pos;
            Core.AddRange(statements, sourceFile.Statements, pos, null);

            // Append the diagnostics that belong to the copied range.
            var trailingDiagnosticStart = Core.FindIndex(savedParseDiagnostics, (diagnostic, _) => diagnostic.Start >= prevStatementPos);
            if (trailingDiagnosticStart >= 0)
            {
                Core.AddRange(_parseDiagnostics, savedParseDiagnostics, trailingDiagnosticStart, null);
            }
        }

        _syntaxCursor = savedSyntaxCursor;

        // PORT-NOTE: TS's `updateSourceFile` defaults the five trailing members to the node's own; the port's generated
        // signature takes them explicitly, so the source file's current values are passed back unchanged.
        return _factory.UpdateSourceFile(
            sourceFile,
            Utilities.SetTextRange(_factory.CreateNodeArray(statements, null), sourceFile.Statements),
            sourceFile.IsDeclarationFile,
            sourceFile.ReferencedFiles,
            sourceFile.TypeReferenceDirectives,
            sourceFile.HasNoDefaultLib,
            sourceFile.LibReferenceDirectives);

        bool ContainsPossibleTopLevelAwait(Node node)
            => (node.Flags & NodeFlags.AwaitContext) == 0
                && (node.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait) != 0;

        int FindNextStatementWithAwait(List<IStatement> statementList, int from)
        {
            for (var i = from; i < statementList.Count; i++)
            {
                if (ContainsPossibleTopLevelAwait((Node)statementList[i])) return i;
            }

            return -1;
        }

        int FindNextStatementWithoutAwait(List<IStatement> statementList, int from)
        {
            for (var i = from; i < statementList.Count; i++)
            {
                if (!ContainsPossibleTopLevelAwait((Node)statementList[i])) return i;
            }

            return -1;
        }
    }

    /// <summary>The <c>kind</c> of a comment pragma's definition, defaulting the way TS does.</summary>
    private static PragmaKindFlags PragmaKindOf(IPragmaDefinition<string, string, string, string> pragma)
        => pragma.Kind ?? PragmaKindFlags.Default;

    /// <summary>The <c>args</c> of a comment pragma's definition, when it declares any.</summary>
    private static IList<IPragmaArgumentSpecification<string>>? PragmaArgsOf(IPragmaDefinition<string, string, string, string> pragma)
        => pragma.Args as IList<IPragmaArgumentSpecification<string>>;

    /// <summary>ported from: src/compiler/parser.ts:10616 — <c>PragmaDiagnosticReporter</c>.</summary>
    internal delegate void PragmaDiagnosticReporter(int pos, int length, DiagnosticMessage message);

    /// <summary>The cursor <c>ReparseTopLevelAwait</c> installs: it delegates to the file's own cursor and marks a node that
    /// could still contain a top-level <c>await</c> as intersecting the change.
    /// </summary>
    private sealed class ReparseCursor(Parser parser, IncrementalParser.ISyntaxCursor baseCursor) : IncrementalParser.ISyntaxCursor
    {
        public Node? CurrentNode(int position)
        {
            var node = baseCursor.CurrentNode(position);
            if (parser._topLevel && node is not null && ContainsPossibleTopLevelAwait(node))
            {
                IncrementalParser.MarkAsIntersectingIncrementalChange(node);
            }

            return node;

            static bool ContainsPossibleTopLevelAwait(Node node)
                => (node.Flags & NodeFlags.AwaitContext) == 0
                    && (node.TransformFlags & TransformFlags.ContainsPossibleTopLevelAwait) != 0;
        }
    }

// ported from: src/compiler/types.ts:10413 (PragmaPseudoMapEntry) and parser.ts:10714 (`extractPragmas`)
/// <summary>
/// One pragma the parser found in a leading comment: its name, its named arguments, and where it was.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS declares this as a mapped-type alias over <c>PragmaPseudoMap</c>, so it has no C# counterpart and is
/// built as an object literal at the two places that produce one. The port needs a constructible type; the argument map's
/// values are either a plain string or a <see cref="PragmaArgumentSpan"/> when the pragma's definition asked to capture
/// the span (the triple-slash reference pragmas do).
/// </remarks>
internal sealed class PragmaPseudoMapEntry
{
    public PragmaPseudoMapEntry(string name, Dictionary<string, object?> arguments, CommentRange range)
    {
        Name = name;
        Arguments = arguments;
        Range = range;
    }

    public string Name { get; }

    public Dictionary<string, object?> Arguments { get; }

    public CommentRange Range { get; }
}

/// <summary>A pragma argument whose position was captured, for the diagnostics that point at it.</summary>
internal sealed class PragmaArgumentSpan
{
    public PragmaArgumentSpan(string value, int pos, int end)
    {
        Value = value;
        Pos = pos;
        End = end;
    }

    public string Value { get; }

    public int Pos { get; }

    public int End { get; }
}
}
