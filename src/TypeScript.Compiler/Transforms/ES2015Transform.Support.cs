#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:1151-1180 (insertStatement(s)After*Prologue), :1370-1373
//             (getInternalEmitFlags), :3055-3064 (unwrapInnermostStatementOfLabel), :5297-5300
//             (isIdentifierANonContextualKeyword), :6832-6885 (getAllAccessorDeclarations), :7858-7860
//             (moveRangeEnd), :7939-7941 (rangeEndIsOnSameLineAsRangeStart) with :7961-7968, :10757-10760
//             (isPackedArrayLiteral); src/compiler/scanner.ts:537-546 (getLinesBetweenPositions).
/// <summary>
/// The helpers <c>transformers/es2015.ts</c> calls that no landed slice of the port states yet.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): every member here belongs to a file another slice owns — <c>Utilities.cs</c> for the eight
/// <c>utilities.ts</c> helpers, <c>Printer</c> for the position helpers (its own copies are private,
/// Emit/Printer.cs:2138-2168) and <c>TransformationUtilities</c> for <c>getAllAccessorDeclarations</c> (its copy is
/// private, Transforms/TransformationUtilities.cs:1046-1092). They are stated here, beside their only reader, so this
/// slice is runnable; the batch report names the moves.
/// </para>
/// <para>
/// PORT-NOTE (<c>getAllAccessorDeclarations</c>): TS's first parameter is <c>readonly Declaration[] | undefined</c> and
/// its answer the <c>AllAccessorDeclarations</c> interface; the port answers the same four slots as a tuple because
/// every caller destructures them (the shape <c>TransformationUtilities</c>'s own copy records).
/// </para>
/// <para>
/// PORT-NOTE (<c>ITextRange</c>): <c>moveRangeEnd</c> and <c>rangeEndIsOnSameLineAsRangeStart</c> take
/// <see cref="ITextRange"/>, which every port node implements (<c>Node : ITextRange</c>, Model/Node.cs:49).
/// </para>
/// </remarks>
// ported from: src/compiler/types.ts:5880-5887 — the `{ value, get, set, enumerable, writable, configurable }`
// object literal `createPropertyDescriptor` reads.
/// <summary>ported from: <c>types.ts</c>'s <c>PropertyDescriptorAttributes</c> — the constructible
/// <see cref="IPropertyDescriptorAttributes"/>.</summary>
/// <remarks>PORT-NOTE: the model carries the interface but no implementation (its only reader,
/// <c>NodeFactory.CreatePropertyDescriptor</c>, was ported without one); the reference's object literal is this class.
/// The three boolean slots stay <c>object?</c> (the interface's shape), and the factory's own <c>AsExpression</c> turns
/// each <c>bool</c> into a literal.</remarks>
internal sealed class PropertyDescriptorAttributes : IPropertyDescriptorAttributes
{
    /// <summary>ported from: types.ts:5881 — <c>enumerable</c>.</summary>
    public object? Enumerable { get; set; }

    /// <summary>ported from: types.ts:5882 — <c>configurable</c>.</summary>
    public object? Configurable { get; set; }

    /// <summary>ported from: types.ts:5883 — <c>writable</c>.</summary>
    public object? Writable { get; set; }

    /// <summary>ported from: types.ts:5884 — <c>value</c>.</summary>
    public Expression? Value { get; set; }

    /// <summary>ported from: types.ts:5885 — <c>get</c>.</summary>
    public Expression? Get { get; set; }

    /// <summary>ported from: types.ts:5886 — <c>set</c>.</summary>
    public Expression? Set { get; set; }
}

internal static class ES2015TransformSupport
{
    // ---------------------------------------------------------------------------------------------
    // utilities.ts:1370-1373 — getInternalEmitFlags
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:1370-1373 — <c>getInternalEmitFlags</c>.</summary>
    /// <remarks>PORT-NOTE: the port reaches the emit node's internal flags through <c>Node.EmitNode</c> (the same read
    /// <c>EmitNodes.SetInternalEmitFlags</c> makes); TS's <c>emitNode &amp;&amp; emitNode.internalFlags || 0</c> is the
    /// <c>&lt;known&gt; ?? 0</c> pair.</remarks>
    public static InternalEmitFlags GetInternalEmitFlags(Node node) => node.EmitNode?.InternalFlags ?? InternalEmitFlags.None;

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:7858-7860 — moveRangeEnd
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7858-7860 — <c>moveRangeEnd</c>.</summary>
    /// <remarks>PORT-NOTE: <c>createRange</c> is <see cref="TypeScriptTransformSupport.CreateRange"/>; the reference's
    /// <c>TextRange</c> object literal is the port's constructible class.</remarks>
    public static TextRange MoveRangeEnd(ITextRange range, int end) => TypeScriptTransformSupport.CreateRange(range.Pos, end);

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:3055-3064 — unwrapInnermostStatementOfLabel
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:3055-3064 — <c>unwrapInnermostStatementOfLabel</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>node.statement</c> narrowing (the <c>while</c> loop re-assigns a
    /// <c>LabeledStatement</c>) is the same kind test and cast here.</remarks>
    public static IStatement UnwrapInnermostStatementOfLabel(
        LabeledStatement node,
        Action<LabeledStatement>? beforeUnwrapLabelCallback = null)
    {
        while (true)
        {
            beforeUnwrapLabelCallback?.Invoke(node);
            if (!NodeTests.IsLabeledStatement((Node)node.Statement!))
            {
                return node.Statement!;
            }
            node = (LabeledStatement)node.Statement!;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:1124-1180 — the custom-prologue inserts
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:1171-1177 — <c>insertStatementAfterCustomPrologue</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's generic <c>&lt;T extends Statement&gt;</c> is <see cref="IStatement"/>, the
    /// element type every caller here uses.</remarks>
    public static List<IStatement> InsertStatementAfterCustomPrologue(List<IStatement> to, IStatement? statement)
        => InsertStatementAfterPrologue(to, statement, IsAnyPrologueDirective);

    /// <summary>ported from: src/compiler/utilities.ts:1156-1158 — <c>insertStatementsAfterCustomPrologue</c>.</summary>
    public static List<IStatement> InsertStatementsAfterCustomPrologue(List<IStatement> to, List<IStatement>? from)
        => InsertStatementsAfterPrologue(to, from, IsAnyPrologueDirective);

    /// <summary>ported from: src/compiler/utilities.ts:1139-1150 — <c>insertStatementAfterPrologue</c>.</summary>
    /// <remarks>PORT-NOTE: <c>to.splice(statementIndex, 0, statement)</c> is <see cref="List{T}.Insert"/>.</remarks>
    private static List<IStatement> InsertStatementAfterPrologue(
        List<IStatement> to,
        IStatement? statement,
        Func<Node, bool> isPrologueDirective)
    {
        if (statement is null) return to;
        var statementIndex = 0;
        // skip all prologue directives to insert at the correct position
        for (; statementIndex < to.Count; ++statementIndex)
        {
            if (!isPrologueDirective((Node)to[statementIndex]))
            {
                break;
            }
        }

        to.Insert(statementIndex, statement);
        return to;
    }

    /// <summary>ported from: src/compiler/utilities.ts:1124-1134 — <c>insertStatementsAfterPrologue</c>.</summary>
    /// <remarks>PORT-NOTE: <c>to.splice(statementIndex, 0, ...from)</c> is <see cref="List{T}.InsertRange"/>.</remarks>
    private static List<IStatement> InsertStatementsAfterPrologue(
        List<IStatement> to,
        List<IStatement>? from,
        Func<Node, bool> isPrologueDirective)
    {
        if (from is null || from.Count == 0) return to;
        var statementIndex = 0;
        // skip all prologue directives to insert at the correct position
        for (; statementIndex < to.Count; ++statementIndex)
        {
            if (!isPrologueDirective((Node)to[statementIndex]))
            {
                break;
            }
        }

        to.InsertRange(statementIndex, from);
        return to;
    }

    /// <summary>ported from: src/compiler/utilities.ts:1131-1133 — <c>isAnyPrologueDirective</c>.</summary>
    private static bool IsAnyPrologueDirective(Node node)
        => Utilities.IsPrologueDirective(node) || (Utilities.GetEmitFlags(node) & EmitFlags.CustomPrologue) != 0;

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:7929-7941 / :7961-7968 — the same-line range tests
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:7939-7941 — <c>rangeEndIsOnSameLineAsRangeStart</c>.</summary>
    public static bool RangeEndIsOnSameLineAsRangeStart(ITextRange range1, ITextRange range2, SourceFile sourceFile)
        => PositionsAreOnSameLine(range1.End, GetStartPositionOfRange(range2, sourceFile, /*includeComments*/ false), sourceFile);

    /// <summary>ported from: src/compiler/utilities.ts:7961-7963 — <c>positionsAreOnSameLine</c>.</summary>
    private static bool PositionsAreOnSameLine(int pos1, int pos2, SourceFile sourceFile)
        => GetLinesBetweenPositions(sourceFile, pos1, pos2) == 0;

    /// <summary>ported from: src/compiler/utilities.ts:7966-7968 — <c>getStartPositionOfRange</c>.</summary>
    private static int GetStartPositionOfRange(ITextRange range, SourceFile sourceFile, bool includeComments)
        => ScannerUtilities.PositionIsSynthesized(range.Pos)
            ? -1
            : ScannerUtilities.SkipTrivia(sourceFile.Text!, range.Pos, /*stopAfterLineBreak*/ false, includeComments);

    /// <summary>ported from: src/compiler/scanner.ts:537-546 — <c>getLinesBetweenPositions</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <c>ScannerUtilities.GetLinesBetweenPositions</c> takes an <c>ISourceFileLike</c>,
    /// which the port's <c>SourceFile</c> does not implement; this is the same computation over the same memoized line
    /// map (the shape <c>Printer</c>'s private copy records).</remarks>
    private static int GetLinesBetweenPositions(SourceFile sourceFile, int pos1, int pos2)
    {
        if (pos1 == pos2) return 0;
        var lineStarts = sourceFile.LineMap ?? (sourceFile.LineMap = ScannerUtilities.ComputeLineStarts(sourceFile.Text ?? string.Empty));
        var lower = Math.Min(pos1, pos2);
        var isNegative = lower == pos2;
        var upper = isNegative ? pos1 : pos2;
        var lowerLine = ScannerUtilities.ComputeLineOfPosition(lineStarts, lower);
        var upperLine = ScannerUtilities.ComputeLineOfPosition(lineStarts, upper, lowerLine);
        return isNegative ? lowerLine - upperLine : upperLine - lowerLine;
    }

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:5297-5300 / :10757-10760 — the two remaining predicates
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:5297-5300 — <c>isIdentifierANonContextualKeyword</c>.</summary>
    /// <remarks>PORT-NOTE: <c>identifierToKeywordKind</c> is <see cref="Utilities.IdentifierToKeywordKind"/> and
    /// <c>isContextualKeyword</c> is the private helper below (its only reader here).</remarks>
    public static bool IsIdentifierANonContextualKeyword(Node node)
    {
        var originalKeywordKind = Utilities.IdentifierToKeywordKind((Identifier)node);
        return originalKeywordKind is not null && !IsContextualKeyword(originalKeywordKind.Value);
    }

    /// <summary>ported from: src/compiler/utilities.ts:5281-5283 — <c>isContextualKeyword</c>.</summary>
    private static bool IsContextualKeyword(SyntaxKind token)
        => SyntaxKind.FirstContextualKeyword <= token && token <= SyntaxKind.LastContextualKeyword;

    /// <summary>ported from: src/compiler/utilities.ts:10757-10760 — <c>isPackedArrayLiteral</c>.</summary>
    /// <remarks>PORT-NOTE: <c>isPackedElement</c> (utilities.ts:10748-10750) is inlined as the predicate, since its only
    /// reader is this member.</remarks>
    public static bool IsPackedArrayLiteral(Node expression)
        => NodeTests.IsArrayLiteralExpression(expression)
            && Core.Every(((ArrayLiteralExpression)expression).Elements, (e, _) => !NodeTests.IsOmittedExpression(e));

    // ---------------------------------------------------------------------------------------------
    // utilities.ts:6832-6885 — getAllAccessorDeclarations
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:6832-6885 — <c>getAllAccessorDeclarations</c>.</summary>
    /// <remarks>PORT-NOTE: the second copy of that reference body in the port (see this class's header); its first, in
    /// <c>Utilities</c>, is a local function and unreachable, and <c>TransformationUtilities</c>'s is private. The four
    /// slots get and set accessors are read from are returned as a tuple because the callers destructure them. TS's
    /// <c>readonly Declaration[]</c> is <c>IEnumerable&lt;INode&gt;</c> here because the class and object-literal
    /// callers hold arrays of two different element interfaces that share only <see cref="INode"/>.</remarks>
    public static (Node? FirstAccessor, Node? SecondAccessor, GetAccessorDeclaration? GetAccessor, SetAccessorDeclaration? SetAccessor)
        GetAllAccessorDeclarations(IEnumerable<INode>? declarations, IFunctionLikeDeclarationBase accessor)
    {
        Node? firstAccessor = null;
        Node? secondAccessor = null;
        GetAccessorDeclaration? getAccessor = null;
        SetAccessorDeclaration? setAccessor = null;
        if (Utilities.HasDynamicName((Node)accessor))
        {
            firstAccessor = (Node)accessor;
            if (accessor.Kind == SyntaxKind.GetAccessor)
            {
                getAccessor = (GetAccessorDeclaration)accessor;
            }
            else if (accessor.Kind == SyntaxKind.SetAccessor)
            {
                setAccessor = (SetAccessorDeclaration)accessor;
            }
            else
            {
                Debug.Fail("Accessor has wrong kind");
            }
        }
        else
        {
            foreach (var member in declarations ?? [])
            {
                var m = (Node)member;
                if (!Utilities.IsAccessor(m) || Utilities.IsStatic(m) != Utilities.IsStatic((Node)accessor))
                {
                    continue;
                }
                var memberName = Utilities.GetPropertyNameForPropertyNameNode(((INamedDeclaration)member).Name!);
                var accessorName = Utilities.GetPropertyNameForPropertyNameNode(accessor.Name!);
                if (memberName == accessorName)
                {
                    if (firstAccessor is null)
                    {
                        firstAccessor = m;
                    }
                    else if (secondAccessor is null)
                    {
                        secondAccessor = m;
                    }

                    if (m.Kind == SyntaxKind.GetAccessor && getAccessor is null)
                    {
                        getAccessor = (GetAccessorDeclaration)m;
                    }

                    if (m.Kind == SyntaxKind.SetAccessor && setAccessor is null)
                    {
                        setAccessor = (SetAccessorDeclaration)m;
                    }
                }
            }
        }
        return (firstAccessor, secondAccessor, getAccessor, setAccessor);
    }
}
