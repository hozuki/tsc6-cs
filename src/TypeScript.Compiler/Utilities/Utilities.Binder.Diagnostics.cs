using System;
using System.Collections.Generic;

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts, utilitiesPublic.ts, scanner.ts
/// <summary>
/// The diagnostic, error-span, declaration-name and JSDoc-host helpers of <c>utilities.ts</c> that the binder's
/// strict-mode and JSDoc slices compile against.
/// </summary>
/// <remarks>
/// PORT-NOTE: the port has no module-level functions, so the free functions of <c>utilities.ts</c> land on one partial
/// class as each batch needs them, every member carrying the line it came from (the same arrangement as the M4d slice
/// in <c>Utilities.cs</c> and the parser slice in <c>Utilities.Parser.cs</c>, docs/architecture.md §15.9). A member
/// whose faithful body needs a helper nothing has ported yet brings that helper with it, each with its own
/// <c>ported from</c> line: the error-span slice pulls in <c>getTextOfNode</c>, <c>getEndLinePosition</c> and the two
/// line-start lookups it reads, the container slice pulls in <c>isBlockScope</c> and
/// <c>isFunctionLikeOrClassStaticBlockDeclaration</c>, and the JSDoc-host slice pulls in the five
/// <c>getSourceOf*</c>/<c>getSingle*</c>/<c>getNested*</c> helpers <c>getEffectiveJSDocHost</c> is defined as.
/// <para>
/// PORT-NOTE: TS's <c>T | undefined</c> is <c>T?</c> (§5.1) and its truthiness tests on objects are
/// <c>is not null</c>/<c>is JSDoc</c> patterns (§12.3). TS's <c>SourceFile.text</c> is a non-nullable <c>string</c>;
/// the port's slot is <c>string?</c>, so every read of it here is written <c>?? ""</c> — the same coercion
/// <c>attachFileToDiagnostic</c> already uses.
/// </para>
/// <para>
/// PORT-NOTE: TS's type guards (<c>node is JSDocTypedefTag</c>, <c>getJSDocHost</c>'s <c>HasJSDoc</c>, …) answer
/// <c>bool</c> here because C# cannot narrow through a predicate; each such member names the cast its caller owes it.
/// </para>
/// <para>
/// PORT-NOTE: several node slots these bodies walk are nullable in the port where TS declares them required
/// (<c>BinaryExpression.OperatorToken</c>, <c>ModuleDeclaration.Body</c>, <c>CaseClause.Statements</c>,
/// <c>PropertyDeclaration.Initializer</c>, …), because the generated model makes every reference slot nullable
/// (§12.2). The tests are therefore shapes (<c>is BinaryExpression right</c>, <c>is { Kind: … }</c>) rather than TS's
/// direct reads: a genuinely absent slot answers "not this shape" where TS would throw.
/// </para>
/// <para>
/// PORT-NOTE: four bodies call helpers a sibling M5-0 slice lands in this same partial class —
/// <c>getContainerFlags</c> becomes <see cref="Binder.GetContainerFlags"/>, and <c>getAssignmentDeclarationKind</c> /
/// <c>getRightMostAssignedExpression</c> become <c>GetAssignmentDeclarationKind</c> /
/// <c>GetRightMostAssignedExpression</c> in <c>Utilities.JsExpando.cs</c>, while <c>hasDynamicName</c> reads
/// <c>GetNameOfDeclaration</c> and <c>IsDynamicName</c>, and <c>isBlockScope</c> reads
/// <c>IsFunctionLikeOrClassStaticBlockDeclaration</c>, from <c>Utilities.Binder.cs</c>. Those are compile-time
/// dependencies only.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    // ---------------------------------------------------------------------------------------------
    // Source text and declaration names
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:1286-1288 — <c>getSourceTextOfNodeFromSourceFile</c>.</summary>
    public static string GetSourceTextOfNodeFromSourceFile(SourceFile sourceFile, Node node, bool includeTrivia = false)
        => GetTextOfNodeFromSourceText(sourceFile.Text ?? "", node, includeTrivia);

    /// <summary>ported from: src/compiler/utilities.ts:1337-1339 — <c>getTextOfNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>declarationNameToString</c> is defined in terms of it. TS calls the
    /// non-nullable overload of <c>getSourceFileOfNode</c> here (the one that asserts a source file exists); the port
    /// has only the nullable one, so that assumption is spelled <see cref="Debug.CheckDefined{T}"/> — the same failure,
    /// one call later.
    /// </remarks>
    public static string GetTextOfNode(Node node, bool includeTrivia = false)
        => GetSourceTextOfNodeFromSourceFile(Debug.CheckDefined(GetSourceFileOfNode(node)), node, includeTrivia);

    /// <summary>ported from: src/compiler/utilities.ts:1309-1318 — <c>moduleExportNameIsDefault</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>ModuleExportName</c> alias (<c>Identifier | StringLiteral</c>), which has no
    /// C# counterpart, so the port takes <c>Node</c> and casts on the two kinds that can reach it. TS compares against
    /// the <c>const enum</c> member <c>InternalSymbolName.Default</c>, whose value <em>is</em> the string
    /// <c>"default"</c>; the port's <see cref="InternalSymbolName"/> is a struct, so the comparison is against
    /// <see cref="InternalSymbolName.Default"/>'s <c>Value</c>.
    /// </remarks>
    public static bool ModuleExportNameIsDefault(Node node)
    {
        var text = node.Kind == SyntaxKind.StringLiteral
            ? ((StringLiteral)node).Text
            : ((Identifier)node).EscapedText;
        return text == InternalSymbolName.Default.Value;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2345-2351 — <c>declarationNameToString</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter <c>DeclarationName | QualifiedName | undefined</c> is a union of node types with no C#
    /// counterpart, so the port takes <c>Node?</c>. The operand order of the ternary is kept, so a missing name is
    /// answered before its width is read.
    /// </remarks>
    public static string DeclarationNameToString(Node? name)
        => name is null || GetFullWidth(name) == 0 ? "(Missing)" : GetTextOfNode(name);

    // ---------------------------------------------------------------------------------------------
    // Containers and block scopes
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2201-2229 — <c>isBlockScope</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>getEnclosingBlockScopeContainer</c> is defined in terms of it. TS's
    /// <c>parentNode: Node | undefined</c> is <c>Node?</c>. The <c>isFunctionLikeOrClassStaticBlockDeclaration</c> the
    /// <c>Block</c> case calls is declared by the <c>Utilities.Binder.cs</c> slice (utilitiesPublic.ts:1680), not here.
    /// </remarks>
    public static bool IsBlockScope(Node node, Node? parentNode)
    {
        switch (node.Kind)
        {
            case SyntaxKind.SourceFile:
            case SyntaxKind.CaseBlock:
            case SyntaxKind.CatchClause:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.ForStatement:
            case SyntaxKind.ForInStatement:
            case SyntaxKind.ForOfStatement:
            case SyntaxKind.Constructor:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.ArrowFunction:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.ClassStaticBlockDeclaration:
                return true;

            case SyntaxKind.Block:
                // function block is not considered block-scope container
                // see comment in binder.ts: bind(...), case for SyntaxKind.Block
                return !IsFunctionLikeOrClassStaticBlockDeclaration(parentNode);
        }

        return false;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2324-2327 — <c>getEnclosingContainer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>getContainerFlags</c> lives in <c>binder.ts</c>; the sibling binder slice lands it as
    /// <see cref="Binder.GetContainerFlags"/>, which this body calls.
    /// </remarks>
    public static Node? GetEnclosingContainer(Node node)
        => FindAncestor(node.Parent, n => (Binder.GetContainerFlags(n) & ContainerFlags.IsContainer) != 0);

    /// <summary>ported from: src/compiler/utilities.ts:2329-2334 — <c>getEnclosingBlockScopeContainer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>findAncestor(node.parent, …)!</c>; the port's <c>!</c> on the same call is the same
    /// non-null assertion.
    /// </remarks>
    public static Node GetEnclosingBlockScopeContainer(Node node)
        => FindAncestor(node.Parent, current => IsBlockScope(current, current.Parent))!;

    /// <summary>ported from: src/compiler/utilities.ts:2337-2343 — <c>forEachEnclosingBlockScopeContainer</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>while (container)</c> tests the non-null result of <c>getEnclosingBlockScopeContainer</c>
    /// for truthiness; the port reads it into a local and tests that, which is the same walk.
    /// </remarks>
    public static void ForEachEnclosingBlockScopeContainer(Node node, Action<Node> cb)
    {
        var container = GetEnclosingBlockScopeContainer(node);
        while (container is not null)
        {
            cb(container);
            container = GetEnclosingBlockScopeContainer(container);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Diagnostics and error spans
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:2510-2516 — <c>getSpanOfTokenAtPosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>createScanner(languageVersion, skipTrivia, languageVariant, text, onError, start)</c> is
    /// <see cref="Scanner.Create"/>, whose trailing <c>length</c> parameter TS leaves unset; TS's
    /// <c>/*onError*/ undefined</c> is the null callback.
    /// </remarks>
    public static TextSpan GetSpanOfTokenAtPosition(SourceFile sourceFile, int pos)
    {
        var scanner = Scanner.Create(sourceFile.LanguageVersion, true, sourceFile.LanguageVariant, sourceFile.Text ?? "", null, pos);
        scanner.Scan();
        var start = scanner.GetTokenStart();
        return Core.CreateTextSpanFromBounds(start, scanner.GetTokenEnd());
    }

    /// <summary>ported from: src/compiler/utilities.ts:2525-2537 — <c>getErrorSpanForArrowFunction</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node.body &amp;&amp; node.body.kind === SyntaxKind.Block</c> is the <c>Block</c> pattern, which
    /// folds the undefined test and the kind test into one. TS reads the line of a position through
    /// <c>getLineAndCharacterOfPosition(sourceFile, pos)</c>, whose parameter is <c>SourceFileLike</c>; no ported type
    /// implements <see cref="ISourceFileLike"/> yet (Model is not this slice's to edit), so this file carries the two
    /// narrowed lookups it needs — see <see cref="GetLineStarts"/>.
    /// </remarks>
    private static TextSpan GetErrorSpanForArrowFunction(SourceFile sourceFile, ArrowFunction node)
    {
        var pos = ScannerUtilities.SkipTrivia(sourceFile.Text ?? "", node.Pos);
        if (node.Body is Block body)
        {
            var startLine = GetLineAndCharacterOfPosition(sourceFile, body.Pos).Line;
            var endLine = GetLineAndCharacterOfPosition(sourceFile, body.End).Line;
            if (startLine < endLine)
            {
                // The arrow function spans multiple lines,
                // make the error span be the first line, inclusive.
                return Core.CreateTextSpan(pos, GetEndLinePosition(startLine, sourceFile) - pos + 1);
            }
        }

        return Core.CreateTextSpanFromBounds(pos, node.End);
    }

    /// <summary>ported from: src/compiler/utilities.ts:2539-2630 — <c>getErrorSpanForNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node as NamedDeclaration</c> and <c>node as CaseOrDefaultClause</c> cast to structural shapes.
    /// The port's counterpart of the first is <see cref="INamedDeclaration"/>, spelled as a pattern so a kind outside
    /// the interface answers <c>null</c> (TS's <c>undefined</c>) instead of throwing. The second has no counterpart at
    /// all: <c>CaseClause</c> and <c>DefaultClause</c> share no base class, so the two kinds are read apart — and
    /// <see cref="IStatement"/>, the element type of <c>statements</c>, carries no text range (D1 keeps
    /// <c>Pos</c>/<c>End</c> off <c>INode</c>), so the first statement is narrowed to
    /// <see cref="IReadonlyTextRange"/>.
    /// <para>
    /// PORT-NOTE: TS's kind list and its <c>Debug.assert</c> calls are kept verbatim; TS's <c>Debug.assert</c> is
    /// ungated there too, so no <c>ShouldAssert</c> wrapper is needed around <c>Debug.Assert</c>.
    /// </para>
    /// </remarks>
    public static TextSpan GetErrorSpanForNode(SourceFile sourceFile, Node node)
    {
        var sourceText = sourceFile.Text ?? "";
        Node? errorNode = node;
        switch (node.Kind)
        {
            case SyntaxKind.SourceFile:
            {
                var pos = ScannerUtilities.SkipTrivia(sourceText, 0);
                if (pos == sourceText.Length)
                {
                    // file is empty - return span for the beginning of the file
                    return Core.CreateTextSpan(0, 0);
                }

                return GetSpanOfTokenAtPosition(sourceFile, pos);
            }
            // This list is a work in progress. Add missing node kinds to improve their error
            // spans.
            case SyntaxKind.VariableDeclaration:
            case SyntaxKind.BindingElement:
            case SyntaxKind.ClassDeclaration:
            case SyntaxKind.ClassExpression:
            case SyntaxKind.InterfaceDeclaration:
            case SyntaxKind.ModuleDeclaration:
            case SyntaxKind.EnumDeclaration:
            case SyntaxKind.EnumMember:
            case SyntaxKind.FunctionDeclaration:
            case SyntaxKind.FunctionExpression:
            case SyntaxKind.MethodDeclaration:
            case SyntaxKind.GetAccessor:
            case SyntaxKind.SetAccessor:
            case SyntaxKind.TypeAliasDeclaration:
            case SyntaxKind.PropertyDeclaration:
            case SyntaxKind.PropertySignature:
            case SyntaxKind.NamespaceImport:
                errorNode = node is INamedDeclaration named ? named.Name : null;
                break;

            case SyntaxKind.ArrowFunction:
                return GetErrorSpanForArrowFunction(sourceFile, (ArrowFunction)node);

            case SyntaxKind.CaseClause:
            case SyntaxKind.DefaultClause:
            {
                var start = ScannerUtilities.SkipTrivia(sourceText, node.Pos);
                var statements = node is CaseClause caseClause ? caseClause.Statements : ((DefaultClause)node).Statements;
                var end = statements is { Count: > 0 } && statements[0] is IReadonlyTextRange firstStatement
                    ? firstStatement.Pos
                    : node.End;
                return Core.CreateTextSpanFromBounds(start, end);
            }

            case SyntaxKind.ReturnStatement:
            case SyntaxKind.YieldExpression:
            {
                var pos = ScannerUtilities.SkipTrivia(sourceText, node.Pos);
                return GetSpanOfTokenAtPosition(sourceFile, pos);
            }

            case SyntaxKind.SatisfiesExpression:
            {
                var pos = ScannerUtilities.SkipTrivia(sourceText, ((SatisfiesExpression)node).Expression!.End);
                return GetSpanOfTokenAtPosition(sourceFile, pos);
            }

            case SyntaxKind.JSDocSatisfiesTag:
            {
                var pos = ScannerUtilities.SkipTrivia(sourceText, ((JSDocSatisfiesTag)node).TagName!.Pos);
                return GetSpanOfTokenAtPosition(sourceFile, pos);
            }

            case SyntaxKind.Constructor:
            {
                var start = ScannerUtilities.SkipTrivia(sourceText, node.Pos);
                var scanner = Scanner.Create(sourceFile.LanguageVersion, true, sourceFile.LanguageVariant, sourceText, null, start);
                var token = scanner.Scan();
                while (token != SyntaxKind.ConstructorKeyword && token != SyntaxKind.EndOfFileToken)
                {
                    token = scanner.Scan();
                }

                var end = scanner.GetTokenEnd();
                return Core.CreateTextSpanFromBounds(start, end);
            }
        }

        if (errorNode is null)
        {
            // If we don't have a better node, then just set the error on the first token of
            // construct.
            return GetSpanOfTokenAtPosition(sourceFile, node.Pos);
        }

        Debug.Assert(!NodeTests.IsJSDoc(errorNode));

        var isMissing = NodeIsMissing(errorNode);
        var errorPos = isMissing || NodeTests.IsJsxText(node)
            ? errorNode.Pos
            : ScannerUtilities.SkipTrivia(sourceText, errorNode.Pos);

        // These asserts should all be satisfied for a properly constructed `errorNode`.
        if (isMissing)
        {
            Debug.Assert(errorPos == errorNode.Pos, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
            Debug.Assert(errorPos == errorNode.End, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
        }
        else
        {
            Debug.Assert(errorPos >= errorNode.Pos, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
            Debug.Assert(errorPos <= errorNode.End, "This failure could trigger https://github.com/Microsoft/TypeScript/issues/20809");
        }

        return Core.CreateTextSpanFromBounds(errorPos, errorNode.End);
    }

    /// <summary>ported from: src/compiler/utilities.ts:1028-1054 — <c>getEndLinePosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>getErrorSpanForArrowFunction</c> is defined in terms of it. TS's
    /// parameter is <c>SourceFileLike</c>; <see cref="ISourceFileLike"/> is declared in the port but implemented by
    /// nothing yet, so the parameter is narrowed to the type its one caller holds, <see cref="SourceFile"/> — the same
    /// reason <see cref="GetLineStarts"/> and <see cref="GetLineAndCharacterOfPosition(SourceFile, int)"/> exist here.
    /// <para>
    /// PORT-NOTE: TS's <c>sourceText.charCodeAt(pos)</c> is the private <c>CharCodeAt</c> the parser slice already
    /// carries; like TS's <c>NaN</c> for an out-of-range index it answers "not a line break" there.
    /// </para>
    /// </remarks>
    public static int GetEndLinePosition(int line, SourceFile sourceFile)
    {
        Debug.Assert(line >= 0);
        var lineStarts = GetLineStarts(sourceFile);

        var lineIndex = line;
        var sourceText = sourceFile.Text ?? "";
        if (lineIndex + 1 == lineStarts.Count)
        {
            // last line - return EOF
            return sourceText.Length - 1;
        }
        else
        {
            // current line start
            var start = lineStarts[lineIndex];
            // take the start position of the next line - 1 = it should be some line break
            var pos = lineStarts[lineIndex + 1] - 1;
            Debug.Assert(ScannerUtilities.IsLineBreak(CharCodeAt(sourceText, pos)));
            // walk backwards skipping line breaks, stop the the beginning of current line.
            // i.e:
            // <some text>
            // $ <- end of line for this position should match the start position
            while (start <= pos && ScannerUtilities.IsLineBreak(CharCodeAt(sourceText, pos)))
            {
                pos--;
            }

            return pos;
        }
    }

    /// <summary>ported from: src/compiler/scanner.ts:502-505 — <c>getLineStarts</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list. <c>ScannerUtilities.GetLineStarts</c> <em>is</em> this member with TS's
    /// <c>SourceFileLike</c> parameter, but no ported type implements <see cref="ISourceFileLike"/>, so it cannot be
    /// called with the <see cref="SourceFile"/> its two callers in this file hold. The memoized body is the same one;
    /// the batch that gives <c>SourceFile</c> an <c>ISourceFileLike</c> face can drop this and call that member.
    /// </remarks>
    private static IList<int> GetLineStarts(SourceFile sourceFile)
        => sourceFile.LineMap ?? (sourceFile.LineMap = ScannerUtilities.ComputeLineStarts(sourceFile.Text ?? ""));

    /// <summary>ported from: src/compiler/scanner.ts:547-550 — <c>getLineAndCharacterOfPosition</c>.</summary>
    /// <remarks>PORT-NOTE: the same <c>SourceFileLike</c> narrowing as <see cref="GetLineStarts"/>.</remarks>
    private static LineAndCharacter GetLineAndCharacterOfPosition(SourceFile sourceFile, int position)
        => ScannerUtilities.ComputeLineAndCharacterOfPosition(GetLineStarts(sourceFile), position);

    /// <summary>ported from: src/compiler/utilities.ts:2427-2431 — <c>createDiagnosticForNodeInSourceFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is the <c>DiagnosticWithLocation</c> interface; the port's <see cref="Diagnostic"/>
    /// is the sealed class that implements it, so it is what is returned here — the same treatment
    /// <c>createDetachedDiagnostic</c> got. A <c>Diagnostic</c> is assignable to
    /// <c>IDiagnosticWithLocation</c> slots.
    /// </remarks>
    public static Diagnostic CreateDiagnosticForNodeInSourceFile(SourceFile sourceFile, Node node, DiagnosticMessage message, params object?[] args)
    {
        var span = GetErrorSpanForNode(sourceFile, node);
        return CreateFileDiagnostic(sourceFile, span.Start, span.Length, message, args);
    }

    /// <summary>ported from: src/compiler/utilities.ts:8681-8701 — <c>createFileDiagnostic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>assertDiagnosticLocation</c> this body calls is the private member
    /// <c>Utilities.Parser.cs</c> already carries (the same partial class), so it is reused rather than duplicated.
    /// <para>
    /// PORT-NOTE: TS's return type is <c>DiagnosticWithLocation</c>; see
    /// <see cref="CreateDiagnosticForNodeInSourceFile"/>. TS builds the diagnostic as an object literal — the port's
    /// constructible <see cref="Diagnostic"/> stands in, and unlike <c>createDetachedDiagnostic</c> this literal spells
    /// both report flags out, so both are carried over.
    /// </para>
    /// </remarks>
    public static Diagnostic CreateFileDiagnostic(SourceFile file, int start, int length, DiagnosticMessage message, params object?[] args)
    {
        AssertDiagnosticLocation(file.Text ?? "", start, length);

        var text = GetLocaleSpecificMessage(message);

        if (Core.Some(args))
        {
            text = FormatStringFromArgs(text, args);
        }

        return new Diagnostic(text, message.Category, message.Code, start, length, file: file)
        {
            ReportsUnnecessary = message.ReportsUnnecessary,
            ReportsDeprecated = message.ReportsDeprecated,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // JSDoc type aliases and hosts
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:4474-4479 — <c>isJSDocConstructSignature</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two guards (<c>isJSDocFunctionType</c>, <c>isIdentifier</c>) narrow; the port's answer <c>bool</c>,
    /// so the casts are written out — <c>(JSDocFunctionType)node</c> is guarded by the kind test, and
    /// <c>Core.TryCast</c> is TS's <c>tryCast</c>.
    /// </remarks>
    public static bool IsJSDocConstructSignature(Node node)
    {
        var param = NodeTests.IsJSDocFunctionType(node) ? Core.FirstOrUndefined(((JSDocFunctionType)node).Parameters) : null;
        var name = param is not null ? Core.TryCast<Identifier, Node>(param.Name, NodeTests.IsIdentifier) : null;
        return name is not null && name.EscapedText == "new";
    }

    /// <summary>ported from: src/compiler/utilities.ts:4481-4484 — <c>isJSDocTypeAlias</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>JSDocTypedefTag | JSDocCallbackTag | JSDocEnumTag</c>; the port answers
    /// <c>bool</c>, so a caller that needs the tag casts to it.
    /// </remarks>
    public static bool IsJSDocTypeAlias(Node node)
        => NodeTests.IsJSDocTypedefTag(node) || NodeTests.IsJSDocCallbackTag(node) || NodeTests.IsJSDocEnumTag(node);

    /// <summary>ported from: src/compiler/utilities.ts:4491-4497 — <c>getSourceOfAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>getEffectiveJSDocHost</c> is defined in terms of it. The three
    /// guards of TS's expression are the one property pattern plus the <c>operatorToken</c> test;
    /// <c>getRightMostAssignedExpression</c> is <c>Utilities.JsExpando.cs</c>'s member.
    /// </remarks>
    private static Node? GetSourceOfAssignment(Node node)
        => node is ExpressionStatement { Expression: BinaryExpression binary }
            && binary.OperatorToken is { Kind: SyntaxKind.EqualsToken }
            ? GetRightMostAssignedExpression(binary)
            : null;

    /// <summary>ported from: src/compiler/utilities.ts:4499-4507 — <c>getSourceOfDefaultedAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>getEffectiveJSDocHost</c> is defined in terms of it.
    /// <c>getAssignmentDeclarationKind</c> is <c>Utilities.JsExpando.cs</c>'s member, whose parameter is widened to
    /// <c>Node</c> because TS's <c>BinaryExpression | CallExpression</c> is a union with no C# counterpart.
    /// </remarks>
    private static Node? GetSourceOfDefaultedAssignment(Node node)
        => node is ExpressionStatement { Expression: BinaryExpression binary }
            && GetAssignmentDeclarationKind(binary) != AssignmentDeclarationKind.None
            && binary.Right is BinaryExpression right
            && right.OperatorToken is { Kind: SyntaxKind.BarBarToken or SyntaxKind.QuestionQuestionToken }
            ? right.Right
            : null;

    /// <summary>ported from: src/compiler/utilities.ts:4509-4519 — <c>getSingleInitializerOfVariableStatementOrPropertyDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>getEffectiveJSDocHost</c> is defined in terms of it. TS's
    /// <c>switch</c> falls out with <c>undefined</c>; the switch expression spells that as <c>_ =&gt; null</c>. TS's
    /// <c>v &amp;&amp; v.initializer</c> is <c>?.Initializer</c>, and <c>(node as PropertyDeclaration)</c> is the kind
    /// test that got the case here.
    /// </remarks>
    private static Expression? GetSingleInitializerOfVariableStatementOrPropertyDeclaration(Node node)
        => node.Kind switch
        {
            SyntaxKind.VariableStatement => GetSingleVariableOfVariableStatement(node)?.Initializer,
            SyntaxKind.PropertyDeclaration => ((PropertyDeclaration)node).Initializer,
            SyntaxKind.PropertyAssignment => ((PropertyAssignment)node).Initializer,
            _ => null,
        };

    /// <summary>ported from: src/compiler/utilities.ts:4521-4524 — <c>getSingleVariableOfVariableStatement</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>getEffectiveJSDocHost</c> is defined in terms of it. TS's
    /// <c>firstOrUndefined</c> is <c>Core.FirstOrUndefined</c>, and <c>node as VariableStatement</c> is the kind test.
    /// </remarks>
    public static VariableDeclaration? GetSingleVariableOfVariableStatement(Node node)
        => NodeTests.IsVariableStatement(node)
            ? Core.FirstOrUndefined(((VariableStatement)node).DeclarationList?.Declarations)
            : null;

    /// <summary>ported from: src/compiler/utilities.ts:4526-4531 — <c>getNestedModuleDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>getEffectiveJSDocHost</c> is defined in terms of it. TS's
    /// <c>node.body &amp;&amp; node.body.kind === SyntaxKind.ModuleDeclaration</c> is the single <c>Body</c> pattern.
    /// </remarks>
    private static Node? GetNestedModuleDeclaration(Node node)
        => NodeTests.IsModuleDeclaration(node) && ((ModuleDeclaration)node).Body is { Kind: SyntaxKind.ModuleDeclaration } body
            ? body
            : null;

    /// <summary>ported from: src/compiler/utilities.ts:4766-4777 — <c>getEffectiveContainerForJSDocTemplateTag</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is the union <c>SignatureDeclaration | JSDocTypedefTag | JSDocCallbackTag |
    /// JSDocEnumTag | undefined</c>, which mixes the structural interface with three node classes. The port's
    /// <see cref="INode"/> (<c>Model/Node.cs</c>) exists for exactly such a slot — it is the one type a node class and a
    /// categorisation interface share — so that is the return type and the caller casts to
    /// <see cref="ISignatureDeclarationBase"/> or to the tag it wants. TS's <c>isJSDoc(node.parent) &amp;&amp;
    /// node.parent.tags</c> is the <c>JSDoc</c> pattern, and <c>find(node.parent.tags, isJSDocTypeAlias)</c> is
    /// <c>Core.Find</c> with the guard spelled as a predicate — TS narrows the result through the type guard, the
    /// port's <c>Find&lt;JSDocTag&gt;</c> returns the element type.
    /// </remarks>
    public static INode? GetEffectiveContainerForJSDocTemplateTag(JSDocTemplateTag node)
    {
        if (node.Parent is JSDoc { Tags: not null } jsDoc)
        {
            // A @template tag belongs to any @typedef, @callback, or @enum tags in the same comment block, if they exist.
            var typeAlias = Core.Find(jsDoc.Tags, static (tag, _) => IsJSDocTypeAlias(tag));
            if (typeAlias is not null)
            {
                return typeAlias;
            }
        }

        // otherwise it belongs to the host it annotates
        return GetHostSignatureFromJSDoc(node);
    }

    /// <summary>ported from: src/compiler/utilities.ts:4784-4792 — <c>getHostSignatureFromJSDoc</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>SignatureDeclaration | undefined</c> is <see cref="ISignatureDeclarationBase"/>? here, since
    /// the predicate answers <c>bool</c> instead of narrowing: the first branch's <c>host.type</c> is narrowed to
    /// <c>Node</c> (the slot is <c>ITypeNode</c>, and only a <c>Node</c> has a kind to ask about) and then cast to the
    /// interface, and the second is a safe pattern test rather than the cast TS's <c>isFunctionLike(host)</c> proved.
    /// </remarks>
    public static ISignatureDeclarationBase? GetHostSignatureFromJSDoc(Node node)
    {
        var host = GetEffectiveJSDocHost(node);
        if (host is not null)
        {
            if (host is PropertySignature { Type: Node propertyType } && IsFunctionLike(propertyType))
            {
                return (ISignatureDeclarationBase)propertyType;
            }

            return host is ISignatureDeclarationBase signature ? signature : null;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4794-4805 — <c>getEffectiveJSDocHost</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>||</c> chain is <c>??</c>, which answers the same for the <c>Node | undefined</c> values the
    /// five helpers return; the final <c>|| host</c> is the chain's, and TS's implicit <c>undefined</c> return is the
    /// explicit <c>null</c>.
    /// </remarks>
    public static Node? GetEffectiveJSDocHost(Node node)
    {
        var host = GetJSDocHost(node);
        return host is null
            ? null
            : GetSourceOfDefaultedAssignment(host)
                ?? GetSourceOfAssignment(host)
                ?? GetSingleInitializerOfVariableStatementOrPropertyDeclaration(host)
                ?? GetSingleVariableOfVariableStatement(host)
                ?? GetNestedModuleDeclaration(host)
                ?? host;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4807-4822 — <c>getJSDocHost</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type <c>HasJSDoc | undefined</c> is <c>Node?</c> here — <c>HasJSDoc</c> has no C#
    /// counterpart, so the caller casts to <see cref="IJSDocContainer"/> to read <c>JsDoc</c> (the same note
    /// <c>hasJSDocNodes</c> carries). TS's <c>host &amp;&amp; host.jsDoc</c> is the <c>IJSDocContainer</c> pattern, and
    /// the runtime list is narrowed to the concrete <see cref="JSDocArray"/> — the only implementation of
    /// <see cref="IJSDocArray"/> — so <c>Core.LastOrUndefined</c> can read it, as <c>hasJSDocNodes</c> already does.
    /// </remarks>
    public static Node? GetJSDocHost(Node node)
    {
        var jsDoc = GetJSDocRoot(node);
        if (jsDoc is null)
        {
            return null;
        }

        var host = jsDoc.Parent;
        if (host is IJSDocContainer { JsDoc: JSDocArray docs } && jsDoc == Core.LastOrUndefined(docs))
        {
            return host;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:4824-4827 — <c>getJSDocRoot</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type <c>JSDoc | undefined</c> is <c>Node?</c> here; the caller that needs the <c>JSDoc</c>
    /// members pattern-tests it. <c>findAncestor(node.parent, isJSDoc)</c> is the method-group overload of
    /// <c>FindAncestor</c>.
    /// </remarks>
    public static Node? GetJSDocRoot(Node node) => FindAncestor(node.Parent, NodeTests.IsJSDoc);

    /// <summary>ported from: src/compiler/utilities.ts:5371-5384 — <c>hasDynamicName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's guard narrows to <c>DynamicNamedDeclaration | DynamicNamedBinaryExpression</c>; the port answers
    /// <c>bool</c>.
    /// <para>
    /// PORT-NOTE: TS's <c>getNameOfDeclaration</c> is <c>GetNameOfDeclaration</c> in <c>Utilities.Binder.cs</c> (that
    /// slice owns it together with <c>GetNonAssignedNameOfDeclaration</c>/<c>GetAssignedName</c>/<c>IsDynamicName</c>,
    /// which this body therefore calls rather than declares). It answers <c>object?</c> — TS's <c>DeclarationName</c>
    /// union has no C# counterpart (§12.5) — so the name is narrowed with the <c>Node</c> pattern before
    /// <c>IsDynamicName</c> reads a kind off it; TS's <c>!!name</c> test is that pattern's absence arm, and a value
    /// that is not a node answers the same <c>false</c> there, having no <c>kind</c> to match.
    /// </para>
    /// </remarks>
    public static bool HasDynamicName(Node declaration)
        => GetNameOfDeclaration(declaration) is Node name && IsDynamicName(name);

    // ---------------------------------------------------------------------------------------------
    // File extensions
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:10170 — <c>extensionsToRemove</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>readonly Extension[]</c> holds <c>const enum</c> members whose values <em>are</em> the
    /// extension strings, so it doubles as a <c>readonly string[]</c>. The port's <see cref="Extension"/> is a struct
    /// and <c>Paths</c> is string-based, so the list holds the strings, named through the enum members they come from
    /// (the shape <see cref="SupportedDeclarationExtensions"/> uses).
    /// <para>
    /// PORT-NOTE: TS does not export this constant, but two of its three readers are in other files —
    /// <see cref="RemoveFileExtension"/> and <see cref="TryRemoveExtension"/> here, and
    /// <see cref="TryGetExtensionFromPath"/>, which the M6-1 slice added to <c>Program/Utilities.Program.cs</c>. It is
    /// therefore <c>internal</c> and named after the reference, like every other ported member.
    /// </para>
    /// </remarks>
    internal static readonly IList<string> ExtensionsToRemove =
    [
        Extension.Dts.Value,
        Extension.Dmts.Value,
        Extension.Dcts.Value,
        Extension.Mjs.Value,
        Extension.Mts.Value,
        Extension.Cjs.Value,
        Extension.Cts.Value,
        Extension.Ts.Value,
        Extension.Js.Value,
        Extension.Tsx.Value,
        Extension.Jsx.Value,
        Extension.Json.Value,
    ];

    /// <summary>ported from: src/compiler/utilities.ts:10171-10180 — <c>removeFileExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>extensionless !== undefined</c> is <c>is not null</c>, which is also what tells the empty
    /// string apart from "no match" — as in TS, only an extension actually removed is returned.
    /// </remarks>
    public static string RemoveFileExtension(string path)
    {
        foreach (var ext in ExtensionsToRemove)
        {
            var extensionless = TryRemoveExtension(path, ext);
            if (extensionless is not null)
            {
                return extensionless;
            }
        }

        return path;
    }

    /// <summary>ported from: src/compiler/utilities.ts:10182-10185 — <c>tryRemoveExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>fileExtensionIs</c> is <c>Paths.FileExtensionIs</c> (the port's counterpart of
    /// <c>core.ts</c>'s test — the same <c>path.length &gt; extension.length &amp;&amp; endsWith(path, extension)</c>),
    /// so the separate <c>Paths.GetAnyExtensionFromPath</c>/<c>TryGetExtensionFromPath</c> family is not needed here.
    /// </remarks>
    public static string? TryRemoveExtension(string path, string extension)
        => Paths.FileExtensionIs(path, extension) ? RemoveExtension(path, extension) : null;

    /// <summary>ported from: src/compiler/utilities.ts:10187-10190 — <c>removeExtension</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list; <c>tryRemoveExtension</c> is defined in terms of it. JS's
    /// <c>substring(0, n)</c> clamps a negative end to <c>0</c> rather than throwing, so the slice is clamped with
    /// <c>Math.Max</c> — reachability through <c>tryRemoveExtension</c> already guarantees
    /// <c>extension.Length &lt; path.Length</c>, but the member is exported in TS and callers may pass anything.
    /// </remarks>
    public static string RemoveExtension(string path, string extension)
        => path[..Math.Max(0, path.Length - extension.Length)];
}
