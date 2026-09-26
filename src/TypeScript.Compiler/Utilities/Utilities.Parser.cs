using System.Globalization;
using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts, utilitiesPublic.ts, scanner.ts, core.ts, factory/utilitiesPublic.ts
/// <summary>
/// The Layer-5 helpers that <c>parser.ts</c> calls out of <c>utilities.ts</c> and its neighbours.
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no module-level functions, so the free functions of <c>utilities.ts</c> and
/// <c>utilitiesPublic.ts</c> land on one partial class as each batch needs them, every member carrying the line it
/// came from (the same arrangement as the M4d slice in <c>Utilities.cs</c>, docs/architecture.md §15.9). Everything
/// reachable from <c>parser.ts</c> is here, together with the handful of one-line helpers those functions are defined
/// in terms of — <c>setTextRangeEnd</c>, <c>isPunctuation</c>, <c>isParameterPropertyModifier</c>,
/// <c>getScriptKindFromFileName</c> and <c>isJSDocNode</c> — because a faithful body needs them and no other batch has
/// claimed them. The M5 batch that ports the rest of <c>utilities.ts</c> can therefore see exactly what exists.
/// <para>
/// PORT-NOTE: TS's <c>T | undefined</c> is <c>T?</c> (§5.1), its <c>readonly T[]</c> is <c>IList&lt;T&gt;</c> (§10.2),
/// and its <c>node is HasJSDoc</c>-style type guards answer <c>bool</c> — C# cannot narrow through a predicate, so the
/// caller casts to the interface it needs (noted per member).
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // Text ranges and positions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:10656 — <c>setTextRangeEnd</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of <c>parser-imports.txt</c>; <c>setTextRangePosEnd</c> is defined in terms of it, so the
    /// body needs it either way.
    /// </remarks>
    public static T SetTextRangeEnd<T>(T range, int end)
        where T : ITextRange
    {
        range.End = end;
        return range;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10644 — <c>setTextRangePos</c>.</summary>
    public static T SetTextRangePos<T>(T range, int pos)
        where T : ITextRange
    {
        range.Pos = pos;
        return range;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10664 — <c>setTextRangePosEnd</c>.</summary>
    public static T SetTextRangePosEnd<T>(T range, int pos, int end)
        where T : ITextRange
        => SetTextRangeEnd(SetTextRangePos(range, pos), end);

    /// <summary>ported from: src/compiler/utilities.ts:10674 — <c>setTextRangePosWidth</c>.</summary>
    public static T SetTextRangePosWidth<T>(T range, int pos, int width)
        where T : ITextRange
        => SetTextRangePosEnd(range, pos, pos + width);

    /// <summary>ported from: src/compiler/utilities.ts:798 — <c>getFullWidth</c>.</summary>
    public static int GetFullWidth(Node node) => node.End - node.Pos;

    /// <summary>ported from: src/compiler/utilities.ts:1079 — <c>nodeIsMissing</c>.</summary>
    public static bool NodeIsMissing(Node? node)
        => node is null
            || (node.Pos == node.End && node.Pos >= 0 && node.Kind != SyntaxKind.EndOfFileToken);

    /// <summary>ported from: src/compiler/utilities.ts:1088 — <c>nodeIsPresent</c>.</summary>
    public static bool NodeIsPresent(Node? node) => !NodeIsMissing(node);

    // ---------------------------------------------------------------------------------------------
    // JSDoc adjacency
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2517 — <c>isJSDocNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of <c>parser-imports.txt</c>; <c>setParentRecursive</c> dispatches on it, so it arrives
    /// with this batch rather than with M5's JSDoc utilities.
    /// </remarks>
    public static bool IsJSDocNode(Node node)
        => node.Kind >= SyntaxKind.FirstJSDocNode && node.Kind <= SyntaxKind.LastJSDocNode;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2552 — <c>hasJSDocNodes</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows <c>node</c> to <c>HasJSDoc</c> so the caller can read <c>node.jsDoc</c>; the port
    /// answers <c>bool</c> and the caller casts to <see cref="IJSDocContainer"/>. <c>JSDocArray</c> is the concrete
    /// list the parser builds (<c>Model/JSDocArray.cs</c>), which is what makes <c>length</c> readable here.
    /// </remarks>
    public static bool HasJSDocNodes(Node node)
        => CanHaveJSDoc(node) && node is IJSDocContainer { JsDoc: JSDocArray { Count: > 0 } };

    /// <summary>ported from: src/compiler/utilities.ts:4561 — <c>canHaveJSDoc</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows <c>node</c> to <c>HasJSDoc</c>; the port answers <c>bool</c> and the caller casts
    /// to <see cref="IJSDocContainer"/> (the same treatment as <see cref="HasJSDocNodes"/>).
    /// </remarks>
    public static bool CanHaveJSDoc(Node node) => node.Kind switch
    {
        SyntaxKind.ArrowFunction
            or SyntaxKind.BinaryExpression
            or SyntaxKind.Block
            or SyntaxKind.BreakStatement
            or SyntaxKind.CallSignature
            or SyntaxKind.CaseClause
            or SyntaxKind.ClassDeclaration
            or SyntaxKind.ClassExpression
            or SyntaxKind.ClassStaticBlockDeclaration
            or SyntaxKind.Constructor
            or SyntaxKind.ConstructorType
            or SyntaxKind.ConstructSignature
            or SyntaxKind.ContinueStatement
            or SyntaxKind.DebuggerStatement
            or SyntaxKind.DoStatement
            or SyntaxKind.ElementAccessExpression
            or SyntaxKind.EmptyStatement
            or SyntaxKind.EndOfFileToken
            or SyntaxKind.EnumDeclaration
            or SyntaxKind.EnumMember
            or SyntaxKind.ExportAssignment
            or SyntaxKind.ExportDeclaration
            or SyntaxKind.ExportSpecifier
            or SyntaxKind.ExpressionStatement
            or SyntaxKind.ForInStatement
            or SyntaxKind.ForOfStatement
            or SyntaxKind.ForStatement
            or SyntaxKind.FunctionDeclaration
            or SyntaxKind.FunctionExpression
            or SyntaxKind.FunctionType
            or SyntaxKind.GetAccessor
            or SyntaxKind.Identifier
            or SyntaxKind.IfStatement
            or SyntaxKind.ImportDeclaration
            or SyntaxKind.ImportEqualsDeclaration
            or SyntaxKind.IndexSignature
            or SyntaxKind.InterfaceDeclaration
            or SyntaxKind.JSDocFunctionType
            or SyntaxKind.JSDocSignature
            or SyntaxKind.LabeledStatement
            or SyntaxKind.MethodDeclaration
            or SyntaxKind.MethodSignature
            or SyntaxKind.ModuleDeclaration
            or SyntaxKind.NamedTupleMember
            or SyntaxKind.NamespaceExportDeclaration
            or SyntaxKind.ObjectLiteralExpression
            or SyntaxKind.Parameter
            or SyntaxKind.ParenthesizedExpression
            or SyntaxKind.PropertyAccessExpression
            or SyntaxKind.PropertyAssignment
            or SyntaxKind.PropertyDeclaration
            or SyntaxKind.PropertySignature
            or SyntaxKind.ReturnStatement
            or SyntaxKind.SemicolonClassElement
            or SyntaxKind.SetAccessor
            or SyntaxKind.ShorthandPropertyAssignment
            or SyntaxKind.SpreadAssignment
            or SyntaxKind.SwitchStatement
            or SyntaxKind.ThrowStatement
            or SyntaxKind.TryStatement
            or SyntaxKind.TypeAliasDeclaration
            or SyntaxKind.TypeParameter
            or SyntaxKind.VariableDeclaration
            or SyntaxKind.VariableStatement
            or SyntaxKind.WhileStatement
            or SyntaxKind.WithStatement => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilities.ts:2763 — <c>getJSDocCommentRanges</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads <c>text.charCodeAt(i)</c>, which answers <c>NaN</c> past the end and so fails the
    /// comparison; C# would throw, hence <see cref="CharCodeAt"/> (the same treatment as <c>Paths</c>' private
    /// helper, docs/architecture.md §10.2).
    /// </remarks>
    public static IList<CommentRange>? GetJSDocCommentRanges(Node node, string text)
    {
        var commentRanges = node.Kind is SyntaxKind.Parameter
            or SyntaxKind.TypeParameter
            or SyntaxKind.FunctionExpression
            or SyntaxKind.ArrowFunction
            or SyntaxKind.ParenthesizedExpression
            or SyntaxKind.VariableDeclaration
            or SyntaxKind.ExportSpecifier
            ? Core.Concatenate(ScannerUtilities.GetTrailingCommentRanges(text, node.Pos), ScannerUtilities.GetLeadingCommentRanges(text, node.Pos))
            : ScannerUtilities.GetLeadingCommentRanges(text, node.Pos);

        // True if the comment starts with '/**' but not if it is '/**/'
        return Core.Filter(commentRanges, comment =>
            comment.End <= node.End
            && CharCodeAt(text, comment.Pos + 1) == CharacterCodes.Asterisk
            && CharCodeAt(text, comment.Pos + 2) == CharacterCodes.Asterisk
            && CharCodeAt(text, comment.Pos + 3) != CharacterCodes.Slash);
    }

    /// <summary>JS's <c>charCodeAt</c>: an index outside the string answers <c>NaN</c>, which is equal to no
    /// character code. <c>-1</c> is that same "equal to nothing" answer here.</summary>
    private static int CharCodeAt(string text, int index) => (uint)index < (uint)text.Length ? text[index] : -1;

    /// <summary>ported from: src/compiler/utilities.ts:1321 — <c>getTextOfNodeFromSourceText</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: JS's <c>substring</c> clamps both ends instead of throwing, so a node whose range runs past the end
    /// of the text (or one whose trivia-skipped start lands after its end) yields the clamped slice rather than an
    /// exception, as it does in TS.
    /// <para>
    /// PORT-NOTE: TS splits on the regular expression <c>\r\n|\n|\r</c>; splitting on the three literals in that order
    /// is the same partition, because the first separator that matches at a position wins. The trailing
    /// <c>trimStart</c> is <see cref="string.TrimStart()"/>.
    /// </para>
    /// </remarks>
    public static string GetTextOfNodeFromSourceText(string sourceText, Node node, bool includeTrivia = false)
    {
        if (NodeIsMissing(node))
        {
            return "";
        }

        var start = includeTrivia ? node.Pos : ScannerUtilities.SkipTrivia(sourceText, node.Pos);
        var end = Math.Min(node.End, sourceText.Length);
        var text = end > start ? sourceText[Math.Max(start, 0)..end] : "";

        if (IsJSDocTypeExpressionOrChild(node))
        {
            // strip space + asterisk at line start
            text = string.Join(
                "\n",
                text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                    .Select(static line => AsteriskPrefixRegEx().Replace(line, "").TrimStart()));
        }

        return text;
    }

    [GeneratedRegex(@"^\s*\*", RegexOptions.CultureInvariant)]
    private static partial Regex AsteriskPrefixRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:1330 — <c>isJSDocTypeExpressionOrChild</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>!!findAncestor(node, isJSDocTypeExpression)</c>. <c>findAncestor</c>
    /// (utilities.ts:5477) has no counterpart in the port yet, so the walk it performs is spelled out here; the M5
    /// batch that ports <c>findAncestor</c> can collapse this member into a call.
    /// </remarks>
    private static bool IsJSDocTypeExpressionOrChild(Node node)
    {
        for (Node? current = node; current is not null; current = current.Parent)
        {
            if (NodeTests.IsJSDocTypeExpression(current)) return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Token and keyword classification
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:5266 — <c>isKeyword</c>.</summary>
    public static bool IsKeyword(SyntaxKind token)
        => token >= SyntaxKind.FirstKeyword && token <= SyntaxKind.LastKeyword;

    /// <summary>ported from: src/compiler/utilities.ts:5271 — <c>isPunctuation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of <c>parser-imports.txt</c>; <c>isKeywordOrPunctuation</c> is defined in terms of it.
    /// </remarks>
    public static bool IsPunctuation(SyntaxKind token)
        => token >= SyntaxKind.FirstPunctuation && token <= SyntaxKind.LastPunctuation;

    /// <summary>ported from: src/compiler/utilities.ts:5276 — <c>isKeywordOrPunctuation</c>.</summary>
    public static bool IsKeywordOrPunctuation(SyntaxKind token) => IsKeyword(token) || IsPunctuation(token);

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1612 — <c>isModifierKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>Modifier["kind"]</c>, which has no C# counterpart; the port answers
    /// <c>bool</c>. The kind list is spelled out rather than read off <see cref="ModifierToFlag"/>: that table also
    /// answers <c>Decorator</c>, which is not a modifier token.
    /// </remarks>
    public static bool IsModifierKind(SyntaxKind token) => token switch
    {
        SyntaxKind.AbstractKeyword
            or SyntaxKind.AccessorKeyword
            or SyntaxKind.AsyncKeyword
            or SyntaxKind.ConstKeyword
            or SyntaxKind.DeclareKeyword
            or SyntaxKind.DefaultKeyword
            or SyntaxKind.ExportKeyword
            or SyntaxKind.InKeyword
            or SyntaxKind.PublicKeyword
            or SyntaxKind.PrivateKeyword
            or SyntaxKind.ProtectedKeyword
            or SyntaxKind.ReadonlyKeyword
            or SyntaxKind.StaticKeyword
            or SyntaxKind.OutKeyword
            or SyntaxKind.OverrideKeyword => true,
        _ => false,
    };

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1636 — <c>isParameterPropertyModifier</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of <c>parser-imports.txt</c>; <c>isClassMemberModifier</c> is defined in terms of it.
    /// </remarks>
    public static bool IsParameterPropertyModifier(SyntaxKind kind)
        => (ModifierToFlag(kind) & ModifierFlags.ParameterPropertyModifier) != 0;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1640 — <c>isClassMemberModifier</c>.</summary>
    public static bool IsClassMemberModifier(SyntaxKind idToken)
        => IsParameterPropertyModifier(idToken)
            || idToken == SyntaxKind.StaticKeyword
            || idToken == SyntaxKind.OverrideKeyword
            || idToken == SyntaxKind.AccessorKeyword;

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1518 — <c>isTemplateLiteralKind</c>.</summary>
    public static bool IsTemplateLiteralKind(SyntaxKind kind)
        => kind >= SyntaxKind.FirstTemplateToken && kind <= SyntaxKind.LastTemplateToken;

    // ---------------------------------------------------------------------------------------------
    // Script kind and language variant
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:8948 — <c>getLanguageVariant</c>.</summary>
    public static LanguageVariant GetLanguageVariant(ScriptKind scriptKind)
        // .tsx and .jsx files are treated as jsx language variant.
        => scriptKind is ScriptKind.TSX or ScriptKind.JSX or ScriptKind.JS or ScriptKind.JSON
            ? LanguageVariant.JSX
            : LanguageVariant.Standard;

    /// <summary>ported from: src/compiler/utilities.ts:9952 — <c>getScriptKindFromFileName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not an entry of <c>parser-imports.txt</c>; <c>ensureScriptKind</c> is defined in terms of it. TS
    /// spells the extension through <c>fileName.substr(fileName.lastIndexOf("."))</c>, whose <c>-1</c> index means the
    /// *last character* of the name, so an extension-less name is compared as its final character (never a match).
    /// </remarks>
    public static ScriptKind GetScriptKindFromFileName(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        var ext = IcuCaseFolding.ToLower(dot < 0 ? fileName[^1..] : fileName[dot..]);

        if (ext == Extension.Js.Value || ext == Extension.Cjs.Value || ext == Extension.Mjs.Value) return ScriptKind.JS;
        if (ext == Extension.Jsx.Value) return ScriptKind.JSX;
        if (ext == Extension.Ts.Value || ext == Extension.Cts.Value || ext == Extension.Mts.Value) return ScriptKind.TS;
        if (ext == Extension.Tsx.Value) return ScriptKind.TSX;
        if (ext == Extension.Json.Value) return ScriptKind.JSON;
        return ScriptKind.Unknown;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9934 — <c>ensureScriptKind</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>scriptKind || …</c> relies on JS truthiness, where <c>ScriptKind.Unknown</c> is <c>0</c> and
    /// therefore also falls through to the file name; the port tests that explicitly.
    /// </remarks>
    public static ScriptKind EnsureScriptKind(string fileName, ScriptKind? scriptKind)
    {
        if (scriptKind is not null && scriptKind.Value != ScriptKind.Unknown) return scriptKind.Value;

        var fromFileName = GetScriptKindFromFileName(fileName);
        return fromFileName != ScriptKind.Unknown ? fromFileName : ScriptKind.TS;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9982 — <c>supportedDeclarationExtensions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types this <c>readonly Extension[]</c>, where <c>Extension</c> is a const enum whose values *are*
    /// the extension strings, so it doubles as a <c>readonly string[]</c> — which is how <c>parser.ts:10562</c> hands it
    /// to <c>getAnyExtensionFromPath</c>. The port's <see cref="Extension"/> is a struct and <c>Paths</c> is
    /// string-based, so the list holds the strings, named through the enum members they come from.
    /// </remarks>
    public static readonly IList<string> SupportedDeclarationExtensions =
    [
        Extension.Dts.Value,
        Extension.Dcts.Value,
        Extension.Dmts.Value,
    ];

    // ---------------------------------------------------------------------------------------------
    // Diagnostics
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:8587 — <c>localizedDiagnosticMessages</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>MapLike&lt;string&gt;</c> is an object indexed by key; a dictionary answers the same lookups.
    /// The field is module-private in TS and there is no getter, so there is none here either.
    /// </remarks>
    private static Dictionary<string, string>? s_localizedDiagnosticMessages;

    /// <summary>Determines whether a diagnostic's location lies inside the text it points into.</summary>
    private static void AssertDiagnosticLocation(string sourceText, int start, int length)
    {
        // PORT-NOTE: TS's assertGreaterThanOrEqual/assertLessThanOrEqual are gated at AssertionLevel.Normal; the port
        // cannot swap its own methods out, so the gate is written out at the call site (§10.2).
        if (!Debug.ShouldAssert(AssertionLevel.Normal)) return;

        Debug.AssertGreaterThanOrEqual(start, 0);
        Debug.AssertGreaterThanOrEqual(length, 0);
        Debug.AssertLessThanOrEqual(start, sourceText.Length);
        Debug.AssertLessThanOrEqual(start + length, sourceText.Length);
    }

    /// <summary>ported from: src/compiler/utilities.ts:8590 — <c>setLocalizedDiagnosticMessages</c>.</summary>
    public static void SetLocalizedDiagnosticMessages(Dictionary<string, string>? messages)
        => s_localizedDiagnosticMessages = messages;

    /// <summary>ported from: src/compiler/utilities.ts:8597 — <c>maybeSetLocalizedDiagnosticMessages</c>.</summary>
    public static void MaybeSetLocalizedDiagnosticMessages(Func<Dictionary<string, string>?>? getMessages)
    {
        if (s_localizedDiagnosticMessages is null && getMessages is not null)
        {
            s_localizedDiagnosticMessages = getMessages();
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:8604 — <c>getLocaleSpecificMessage</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>messages &amp;&amp; messages[key] || message.message</c> — JS truthiness, so an empty
    /// localized string also falls back to the English message.
    /// </remarks>
    public static string GetLocaleSpecificMessage(DiagnosticMessage message)
    {
        if (s_localizedDiagnosticMessages is not null
            && s_localizedDiagnosticMessages.TryGetValue(message.Key, out var localized)
            && Js.IsTruthy(localized))
        {
            return localized;
        }

        return message.Message;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8583 — <c>formatStringFromArgs</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types <c>args</c> as <c>DiagnosticArguments</c> (<c>(string | number | undefined)[]</c>), a type
    /// alias with no C# counterpart, so the port takes <c>object?[]</c>. JS's <c>\d</c> is ASCII-only while .NET's is
    /// Unicode-aware (§10.2), hence the explicit <c>[0-9]</c> character class. An index past the end of <c>args</c> is
    /// JS's <c>undefined</c>, which concatenates as <c>"undefined"</c>.
    /// </remarks>
    public static string FormatStringFromArgs(string text, params object?[] args)
        => FormatStringFromArgsRegEx().Replace(text, match =>
        {
            var index = int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : -1;

            if (Debug.ShouldAssert(AssertionLevel.Normal))
            {
                Debug.Assert(index >= 0 && index < args.Length, "Diagnostic message argument index out of range");
            }

            var value = index >= 0 && index < args.Length ? args[index] : null;
            return value is null ? "undefined" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        });

    [GeneratedRegex(@"\{([0-9]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex FormatStringFromArgsRegEx();

    /// <summary>ported from: src/compiler/utilities.ts:8609 — <c>createDetachedDiagnostic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds this as an object literal, which the port's constructible <see cref="Diagnostic"/>
    /// (<c>Core/Diagnostic.cs</c>) stands in for. The literal omits <c>reportsDeprecated</c>; that is upstream's shape
    /// and is kept as it is (docs/architecture.md §10.3).
    /// </remarks>
    public static Diagnostic CreateDetachedDiagnostic(
        string fileName,
        string sourceText,
        int start,
        int length,
        DiagnosticMessage message,
        params object?[] args)
    {
        if (start + length > sourceText.Length)
        {
            length = sourceText.Length - start;
        }

        AssertDiagnosticLocation(sourceText, start, length);
        var text = GetLocaleSpecificMessage(message);

        if (Core.Some(args))
        {
            text = FormatStringFromArgs(text, args);
        }

        return new Diagnostic(text, message.Category, message.Code, start, length, fileName)
        {
            ReportsUnnecessary = message.ReportsUnnecessary,
        };
    }

    /// <summary>ported from: src/compiler/utilities.ts:8621 — <c>attachFileToDiagnostic</c>.</summary>
    private static Diagnostic AttachFileToDiagnostic(Diagnostic diagnostic, SourceFile file)
    {
        var fileName = file.FileName ?? "";
        var length = (file.Text ?? "").Length;

        if (Debug.ShouldAssert(AssertionLevel.Normal))
        {
            Debug.AssertEqual(diagnostic.FileName, fileName);
            Debug.AssertLessThanOrEqual(diagnostic.Start ?? 0, length);
            Debug.AssertLessThanOrEqual((diagnostic.Start ?? 0) + (diagnostic.Length ?? 0), length);
        }

        var diagnosticWithLocation = new Diagnostic(
            diagnostic.MessageText,
            diagnostic.Category,
            diagnostic.Code,
            diagnostic.Start,
            diagnostic.Length,
            file: file)
        {
            ReportsUnnecessary = diagnostic.ReportsUnnecessary,
        };

        if (diagnostic.RelatedInformation is not null)
        {
            diagnosticWithLocation.RelatedInformation = [];
            foreach (var related in diagnostic.RelatedInformation)
            {
                if (related is IDiagnosticWithDetachedLocation { File: null, Start: not null, Length: not null, FileName: not null } detached
                    && detached.FileName == fileName)
                {
                    if (Debug.ShouldAssert(AssertionLevel.Normal))
                    {
                        Debug.AssertLessThanOrEqual(detached.Start!.Value, length);
                        Debug.AssertLessThanOrEqual(detached.Start!.Value + detached.Length!.Value, length);
                    }

                    diagnosticWithLocation.RelatedInformation.Add(AttachFileToDiagnostic((Diagnostic)related, file));
                }
                else
                {
                    diagnosticWithLocation.RelatedInformation.Add(related);
                }
            }
        }

        return diagnosticWithLocation;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8673 — <c>attachFileToDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS returns <c>DiagnosticWithLocation[]</c>; the port answers the element interface of the list it is
    /// assigned to (<c>SourceFile.ParseDiagnostics</c> is <c>List&lt;IDiagnosticWithLocation&gt;?</c>), because a
    /// <c>List&lt;Diagnostic&gt;</c> is not assignable to it.
    /// </remarks>
    public static List<IDiagnosticWithLocation> AttachFileToDiagnostics(IEnumerable<Diagnostic> diagnostics, SourceFile file)
    {
        var diagnosticsWithLocation = new List<IDiagnosticWithLocation>();
        foreach (var diagnostic in diagnostics)
        {
            diagnosticsWithLocation.Add(AttachFileToDiagnostic(diagnostic, file));
        }

        return diagnosticsWithLocation;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10349 — <c>addRelatedInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asserts <c>diagnostic.relatedInformation !== emptyArray</c> before pushing — a debug check that
    /// only makes sense while a shared empty array exists. The port has none (<c>emptyArray</c> is not ported: one
    /// <c>IList&lt;T&gt;</c> instance cannot serve slots of several element types, §10.2), so there is nothing to assert
    /// against and the check is dropped.
    /// </remarks>
    public static Diagnostic AddRelatedInfo(Diagnostic diagnostic, params IDiagnosticRelatedInformation[] relatedInformation)
    {
        if (relatedInformation.Length == 0)
        {
            return diagnostic;
        }

        diagnostic.RelatedInformation ??= [];
        diagnostic.RelatedInformation.AddRange(relatedInformation);
        return diagnostic;
    }

    /// <summary>ported from: src/compiler/utilities.ts:954 — <c>containsParseError</c>.</summary>
    public static bool ContainsParseError(Node node)
    {
        AggregateChildData(node);
        return (node.Flags & NodeFlags.ThisNodeOrAnySubNodesHasError) != 0;
    }

    /// <summary>ported from: src/compiler/utilities.ts:959 — <c>aggregateChildData</c>.</summary>
    private static void AggregateChildData(Node node)
    {
        if ((node.Flags & NodeFlags.HasAggregatedChildData) != 0) return;

        // A node is considered to contain a parse error if:
        //  a) the parser explicitly marked that it had an error
        //  b) any of it's children reported that it had an error.
        var thisNodeOrAnySubNodesHasError =
            (node.Flags & NodeFlags.ThisNodeHasError) != 0
            || ForEachChildTable.ForEachChild<bool>(node, child => ContainsParseError(child)) == true;

        // If so, mark ourselves accordingly.
        if (thisNodeOrAnySubNodesHasError)
        {
            node.Flags |= NodeFlags.ThisNodeOrAnySubNodesHasError;
        }

        // Also mark that we've propagated the child information to this node.  This way we can
        // always consult the bit directly on this node without needing to check its children
        // again.
        node.Flags |= NodeFlags.HasAggregatedChildData;
    }

    // ---------------------------------------------------------------------------------------------
    // Tree walking
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:8295 — <c>getLastChild</c>.</summary>
    public static Node? GetLastChild(Node node)
    {
        Node? lastChild = null;
        ForEachChildTable.ForEachChild<object?>(
            node,
            child =>
            {
                if (NodeIsPresent(child)) lastChild = child;
                return null;
            },
            children =>
            {
                // As an optimization, jump straight to the end of the list.
                // PORT-NOTE: the walker hands the array over as INodeArray<INode>, so an element arrives as INode and
                // the Node the members here take needs the downcast TS's NodeArray<Node> did not need (§12.9).
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    if (NodeIsPresent(children[i] as Node))
                    {
                        lastChild = children[i] as Node;
                        break;
                    }
                }

                return null;
            });
        return lastChild;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10718 — <c>setParentRecursive</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's overloads take <c>T | undefined</c>; the port's single generic member takes the non-nullable
    /// root, since a caller with a maybe-root writes <c>node is null ? null : SetParentRecursive(node, …)</c>.
    /// TS returns the <c>"skip"</c> sentinel to stop <c>forEachChildRecursively</c> from descending into a subtree whose
    /// parent pointers are already right; the port uses the walker's own <c>Skip</c> sentinel.
    /// </remarks>
    public static T SetParentRecursive<T>(T rootNode, bool incremental)
        where T : Node
    {
        ForEachChildTable.ForEachChildRecursively(
            rootNode,
            IsJSDocNode(rootNode) ? BindParentToChildIgnoringJSDoc : BindParentToChild);
        return rootNode;

        object? BindParentToChildIgnoringJSDoc(Node child, Node parent)
        {
            if (incremental && ReferenceEquals(child.Parent, parent))
            {
                return ForEachChildTable.Skip;
            }

            SetParent(child, parent);
            return null;
        }

        object? BindJSDoc(Node child)
        {
            if (child is IJSDocContainer { JsDoc: JSDocArray jsDoc })
            {
                foreach (var doc in jsDoc)
                {
                    BindParentToChildIgnoringJSDoc(doc, child);
                    ForEachChildTable.ForEachChildRecursively(doc, BindParentToChildIgnoringJSDoc);
                }
            }

            return null;
        }

        object? BindParentToChild(Node child, Node parent)
        {
            var result = BindParentToChildIgnoringJSDoc(child, parent);
            return result is null ? BindJSDoc(child) : result;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Pragmas
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/types.ts:10240 — <c>commentPragmas</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes this as an object literal typed <c>ConcretePragmaSpecs</c> and reads it two ways —
    /// <c>commentPragmas[name]</c> (answering <c>undefined</c> for an unknown name) and <c>Object.keys</c> over it;
    /// a dictionary answers both. <c>ConcretePragmaSpecs</c> is a mapped type with no C# counterpart, so the value
    /// type is the generated <see cref="IPragmaDefinition{T1,T2,T3,T4}"/> at its TS default type arguments, and the
    /// concrete type that fills it is <see cref="PragmaDefinition"/> (<c>Core/Pragmas.cs</c>), with <c>Args</c> read
    /// through a cast because the generated slot is <c>object?</c>.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IPragmaDefinition<string, string, string, string>> CommentPragmas =
        new Dictionary<string, IPragmaDefinition<string, string, string, string>>(StringComparer.Ordinal)
        {
            ["reference"] = new PragmaDefinition
            {
                Args =
                [
                    new PragmaArgumentSpecification { Name = "types", Optional = true, CaptureSpan = true },
                    new PragmaArgumentSpecification { Name = "lib", Optional = true, CaptureSpan = true },
                    new PragmaArgumentSpecification { Name = "path", Optional = true, CaptureSpan = true },
                    new PragmaArgumentSpecification { Name = "no-default-lib", Optional = true },
                    new PragmaArgumentSpecification { Name = "resolution-mode", Optional = true },
                    new PragmaArgumentSpecification { Name = "preserve", Optional = true },
                ],
                Kind = PragmaKindFlags.TripleSlashXML,
            },
            ["amd-dependency"] = new PragmaDefinition
            {
                Args =
                [
                    new PragmaArgumentSpecification { Name = "path" },
                    new PragmaArgumentSpecification { Name = "name", Optional = true },
                ],
                Kind = PragmaKindFlags.TripleSlashXML,
            },
            ["amd-module"] = new PragmaDefinition
            {
                Args =
                [
                    new PragmaArgumentSpecification { Name = "name" },
                ],
                Kind = PragmaKindFlags.TripleSlashXML,
            },
            ["ts-check"] = new PragmaDefinition
            {
                Kind = PragmaKindFlags.SingleLine,
            },
            ["ts-nocheck"] = new PragmaDefinition
            {
                Kind = PragmaKindFlags.SingleLine,
            },
            ["jsx"] = new PragmaDefinition
            {
                Args =
                [
                    new PragmaArgumentSpecification { Name = "factory" },
                ],
                Kind = PragmaKindFlags.MultiLine,
            },
            ["jsxfrag"] = new PragmaDefinition
            {
                Args =
                [
                    new PragmaArgumentSpecification { Name = "factory" },
                ],
                Kind = PragmaKindFlags.MultiLine,
            },
            ["jsximportsource"] = new PragmaDefinition
            {
                Args =
                [
                    new PragmaArgumentSpecification { Name = "factory" },
                ],
                Kind = PragmaKindFlags.MultiLine,
            },
            ["jsxruntime"] = new PragmaDefinition
            {
                Args =
                [
                    new PragmaArgumentSpecification { Name = "factory" },
                ],
                Kind = PragmaKindFlags.MultiLine,
            },
        };
}
