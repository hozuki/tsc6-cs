namespace TypeScript.Compiler;

// ported from: src/compiler/utilities.ts:2416-2419 (createDiagnosticForNode)
// ported from: src/compiler/utilities.ts:2422-2426 (createDiagnosticForNodeArray)
// ported from: src/compiler/utilities.ts:2434-2438 (createDiagnosticForNodeFromMessageChain)
// ported from: src/compiler/utilities.ts:2440-2443 (createDiagnosticForNodeArrayFromMessageChain)
// ported from: src/compiler/utilities.ts:2453-2466 (createFileDiagnosticFromMessageChain)
// ported from: src/compiler/utilities.ts:2468-2480 (createDiagnosticForFileFromMessageChain)
// ported from: src/compiler/utilities.ts:2482-2489 (createDiagnosticMessageChainFromDiagnostic)
// ported from: src/compiler/utilities.ts:2491-2500 (createDiagnosticForRange)
// ported from: src/compiler/utilities.ts:6060-6132 (createDiagnosticCollection)
// ported from: src/compiler/utilities.ts:8716-8734 (createCompilerDiagnostic)
// ported from: src/compiler/utilities.ts:8737-8749 (createCompilerDiagnosticFromMessageChain)
// ported from: src/compiler/utilities.ts:8751-8765 (chainDiagnosticMessages)
// ported from: src/compiler/utilities.ts:8767-8775 (concatenateDiagnosticMessageChains)
// ported from: src/compiler/utilities.ts:8777-8948 (the diagnostic comparers)
// ported from: src/compiler/utilitiesPublic.ts:303-305 (sortAndDeduplicateDiagnostics)
// ported from: src/compiler/types.ts:7320-7323 (diagnosticCategoryName)
/// <summary>
/// The diagnostic plumbing Layer 5 needs: the factories that build a <see cref="Diagnostic"/> at a location, the
/// collection the checker and the program accumulate into, and the order/equality the collection is maintained by.
/// </summary>
/// <remarks>
/// PORT-NOTE: these are the <c>utilities.ts</c> members M6-2 landed ahead of the rest of that file because the
/// checker's diagnostics single point (<c>Checker.Diagnostics.cs</c>) and the program's diagnostic aggregation need
/// them; they live in their own partial file the same way <c>Utilities.Binder.Diagnostics.cs</c> holds the ones the
/// binder needed. <c>createFileDiagnostic</c> itself is already on
/// <c>Utilities.Binder.Diagnostics.cs</c> (M5) and is <em>not</em> repeated here.
/// <para>
/// PORT-NOTE: <c>assertDiagnosticLocation</c> (utilities.ts:2445-2451) already exists as a private member of this
/// class in <c>Utilities/Utilities.Parser.cs:382</c>; a second private declaration of the same name in another
/// partial file would be a duplicate member, so the members here call that one, exactly as
/// <c>Utilities.Binder.Diagnostics.cs</c> does.
/// </para>
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:2416-2419 — <c>createDiagnosticForNode</c>.</summary>
    public static Diagnostic CreateDiagnosticForNode(Node node, DiagnosticMessage message, params object?[] args)
    {
        var sourceFile = GetSourceFileOfNode(node);
        return CreateDiagnosticForNodeInSourceFile(Debug.CheckDefined(sourceFile), node, message, args);
    }

    /// <summary>ported from: src/compiler/utilities.ts:2422-2426 — <c>createDiagnosticForNodeArray</c>.</summary>
    public static Diagnostic CreateDiagnosticForNodeArray(SourceFile sourceFile, NodeArray<Node> nodes, DiagnosticMessage message, params object?[] args)
    {
        var start = ScannerUtilities.SkipTrivia(sourceFile.Text ?? "", nodes.Pos);
        return CreateFileDiagnostic(sourceFile, start, nodes.End - start, message, args);
    }

    /// <summary>ported from: src/compiler/utilities.ts:2434-2438 — <c>createDiagnosticForNodeFromMessageChain</c>.</summary>
    public static Diagnostic CreateDiagnosticForNodeFromMessageChain(SourceFile sourceFile, Node node, IDiagnosticMessageChain messageChain, IList<IDiagnosticRelatedInformation>? relatedInformation = null)
    {
        var span = GetErrorSpanForNode(sourceFile, node);
        return CreateFileDiagnosticFromMessageChain(sourceFile, span.Start, span.Length, messageChain, relatedInformation);
    }

    /// <summary>ported from: src/compiler/utilities.ts:2440-2443 — <c>createDiagnosticForNodeArrayFromMessageChain</c>.</summary>
    public static Diagnostic CreateDiagnosticForNodeArrayFromMessageChain(SourceFile sourceFile, NodeArray<Node> nodes, IDiagnosticMessageChain messageChain, IList<IDiagnosticRelatedInformation>? relatedInformation = null)
    {
        var start = ScannerUtilities.SkipTrivia(sourceFile.Text ?? "", nodes.Pos);
        return CreateFileDiagnosticFromMessageChain(sourceFile, start, nodes.End - start, messageChain, relatedInformation);
    }

    /// <summary>ported from: src/compiler/utilities.ts:2453-2466 — <c>createFileDiagnosticFromMessageChain</c>.</summary>
    public static Diagnostic CreateFileDiagnosticFromMessageChain(SourceFile file, int start, int length, IDiagnosticMessageChain messageChain, IList<IDiagnosticRelatedInformation>? relatedInformation = null)
    {
        AssertDiagnosticLocation(file.Text ?? "", start, length);
        return new Diagnostic(
            messageChain.Next is null ? messageChain.MessageText : messageChain,
            messageChain.Category,
            messageChain.Code,
            start,
            length,
            file: file)
        {
            RelatedInformation = relatedInformation is null ? null : [.. relatedInformation],
            CanonicalHead = messageChain.CanonicalHead,
        };
    }

    /// <summary>ported from: src/compiler/utilities.ts:2468-2480 — <c>createDiagnosticForFileFromMessageChain</c>.</summary>
    public static Diagnostic CreateDiagnosticForFileFromMessageChain(SourceFile sourceFile, IDiagnosticMessageChain messageChain, IList<IDiagnosticRelatedInformation>? relatedInformation = null)
    {
        return new Diagnostic(
            messageChain.Next is null ? messageChain.MessageText : messageChain,
            messageChain.Category,
            messageChain.Code,
            0,
            0,
            file: sourceFile)
        {
            RelatedInformation = relatedInformation is null ? null : [.. relatedInformation],
        };
    }

    /// <summary>ported from: src/compiler/utilities.ts:2482-2489 — <c>createDiagnosticMessageChainFromDiagnostic</c>.</summary>
    public static IDiagnosticMessageChain CreateDiagnosticMessageChainFromDiagnostic(IDiagnosticRelatedInformation diagnostic)
    {
        return diagnostic.MessageText is string text
            ? new DiagnosticMessageChain(text, diagnostic.Category, diagnostic.Code, (diagnostic as IDiagnosticMessageChain)?.Next)
            : (IDiagnosticMessageChain)diagnostic.MessageText!;
    }

    /// <summary>ported from: src/compiler/utilities.ts:2491-2500 — <c>createDiagnosticForRange</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is an object with <c>pos</c>/<c>end</c>; the port takes the <see cref="TextRange"/>
    /// it declares those two slots on (<c>Core/TextSpan.cs</c>) so the call site reads the same.
    /// </remarks>
    public static Diagnostic CreateDiagnosticForRange(SourceFile sourceFile, TextRange range, DiagnosticMessage message)
    {
        return new Diagnostic(message.Message, message.Category, message.Code, range.Pos, range.End - range.Pos, file: sourceFile);
    }

    /// <summary>ported from: src/compiler/utilities.ts:6060-6132 — <c>createDiagnosticCollection</c>.</summary>
    public static DiagnosticCollection CreateDiagnosticCollection() => new();

    /// <summary>ported from: src/compiler/utilities.ts:8716-8734 — <c>createCompilerDiagnostic</c>.</summary>
    public static Diagnostic CreateCompilerDiagnostic(DiagnosticMessage message, params object?[] args)
    {
        // PORT-NOTE: ruling A′ — the reference formats here (utilities.ts:8717-8723); the port defers the same pair to
        // the first read of `Diagnostic.MessageText` (see `Diagnostic.Deferred`).
        var diagnostic = Diagnostic.Deferred(message, args);
        diagnostic.ReportsUnnecessary = message.ReportsUnnecessary;
        diagnostic.ReportsDeprecated = message.ReportsDeprecated;
        return diagnostic;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8737-8749 — <c>createCompilerDiagnosticFromMessageChain</c>.</summary>
    public static Diagnostic CreateCompilerDiagnosticFromMessageChain(IDiagnosticMessageChain chain, IList<IDiagnosticRelatedInformation>? relatedInformation = null)
    {
        return new Diagnostic(
            chain.Next is null ? chain.MessageText : chain,
            chain.Category,
            chain.Code)
        {
            RelatedInformation = relatedInformation is null ? null : [.. relatedInformation],
        };
    }

    /// <summary>ported from: src/compiler/utilities.ts:8751-8765 — <c>chainDiagnosticMessages</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types <paramref name="details"/> as the union
    /// <c>DiagnosticMessageChain | DiagnosticMessageChain[] | undefined</c> and tests it with
    /// <c>Array.isArray</c>. C# needs one parameter for both arms, so it stays <c>object?</c> and is dispatched at
    /// run time: a <c>List&lt;IDiagnosticMessageChain&gt;</c> is the array arm, an
    /// <see cref="IDiagnosticMessageChain"/> is the single-chain arm, and <c>null</c> is <c>undefined</c>.
    /// </remarks>
    public static DiagnosticMessageChain ChainDiagnosticMessages(object? details, DiagnosticMessage message, params object?[] args)
    {
        var text = GetLocaleSpecificMessage(message);
        if (Core.Some(args))
        {
            text = FormatStringFromArgs(text, args);
        }

        return new DiagnosticMessageChain(
            text,
            message.Category,
            message.Code,
            details switch
            {
                null => null,
                List<IDiagnosticMessageChain> list => list,
                IDiagnosticMessageChain chain => [chain],
                _ => Debug.Fail<List<IDiagnosticMessageChain>>($"chainDiagnosticMessages: unexpected details of type {details.GetType()}"),
            });
    }

    /// <summary>ported from: src/compiler/utilities.ts:8767-8775 — <c>concatenateDiagnosticMessageChains</c>.</summary>
    public static void ConcatenateDiagnosticMessageChains(DiagnosticMessageChain headChain, DiagnosticMessageChain tailChain)
    {
        var lastChain = headChain;
        while (lastChain.Next is { Count: > 0 })
        {
            lastChain = (DiagnosticMessageChain)lastChain.Next[0];
        }

        lastChain.Next = [tailChain];
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:303-305 — <c>sortAndDeduplicateDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>T extends Diagnostic</c> is instantiated with <see cref="Diagnostic"/> here because the two
    /// comparers take a <c>Diagnostic</c> — <see cref="Diagnostic"/> is the port's only diagnostic class
    /// (<c>Core/Diagnostic.cs</c> implements all three declared faces).
    /// </remarks>
    public static List<Diagnostic> SortAndDeduplicateDiagnostics(IList<Diagnostic> diagnostics)
        => [.. Core.SortAndDeduplicate(diagnostics, CompareDiagnostics, DiagnosticsEqualityComparer)];

    /// <summary>ported from: src/compiler/types.ts:7320-7323 — <c>diagnosticCategoryName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the enum's reverse mapping (<c>DiagnosticCategory[d.category]</c>); C# spells that
    /// <see cref="Enum.GetName{TEnum}(TEnum)"/>. The four names are pure ASCII, so the case mapping cannot differ
    /// from JS's <c>toLowerCase</c> here.
    /// </remarks>
    public static string DiagnosticCategoryName(DiagnosticCategory category, bool lowerCase = true)
    {
        var name = Enum.GetName(category) ?? Debug.Fail<string>($"unknown DiagnosticCategory {(int)category}");
        return lowerCase ? name.ToLowerInvariant() : name;
    }

    /// <summary>ported from: src/compiler/types.ts:7320 — <c>diagnosticCategoryName</c> over a diagnostic.</summary>
    public static string DiagnosticCategoryName(Diagnostic diagnostic, bool lowerCase = true)
        => DiagnosticCategoryName(diagnostic.Category, lowerCase);

    // ---------------------------------------------------------------------------------------------
    // The order and equality the collection above is maintained by (utilities.ts:8777-8948)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/utilities.ts:8781-8785 — <c>compareDiagnostics</c>.</summary>
    public static Comparison CompareDiagnostics(Diagnostic d1, Diagnostic d2)
    {
        var result = CompareDiagnosticsSkipRelatedInformation(d1, d2);
        return result != Comparison.EqualTo ? result : CompareRelatedInformation(d1, d2);
    }

    /// <summary>ported from: src/compiler/utilities.ts:8787-8794 — <c>compareDiagnosticsSkipRelatedInformation</c>.</summary>
    public static Comparison CompareDiagnosticsSkipRelatedInformation(Diagnostic d1, Diagnostic d2)
    {
        var code1 = GetDiagnosticCode(d1);
        var code2 = GetDiagnosticCode(d2);
        var result = Core.CompareStringsCaseSensitive(GetDiagnosticFilePath(d1), GetDiagnosticFilePath(d2));
        if (result != Comparison.EqualTo) return result;
        result = Core.CompareValues(d1.Start, d2.Start);
        if (result != Comparison.EqualTo) return result;
        result = Core.CompareValues(d1.Length, d2.Length);
        if (result != Comparison.EqualTo) return result;
        result = Core.CompareValues(code1, code2);
        if (result != Comparison.EqualTo) return result;
        return CompareMessageText(d1, d2);
    }

    /// <summary>ported from: src/compiler/utilities.ts:8796-8805 — <c>compareRelatedInformation</c>.</summary>
    private static Comparison CompareRelatedInformation(Diagnostic d1, Diagnostic d2)
    {
        if (d1.RelatedInformation is null && d2.RelatedInformation is null) return Comparison.EqualTo;
        if (d1.RelatedInformation is not null && d2.RelatedInformation is not null)
        {
            var result = Core.CompareValues(d2.RelatedInformation.Count, d1.RelatedInformation.Count);
            if (result != Comparison.EqualTo) return result;
            for (var index = 0; index < d1.RelatedInformation.Count; index++)
            {
                // TS's `forEach` returns the first truthy callback result, and EqualTo is 0 — i.e. falsy.
                result = CompareDiagnostics((Diagnostic)d1.RelatedInformation[index], (Diagnostic)d2.RelatedInformation[index]);
                if (result != Comparison.EqualTo) return result;
            }

            return Comparison.EqualTo;
        }

        return d1.RelatedInformation is not null ? Comparison.LessThan : Comparison.GreaterThan;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8807-8846 — <c>compareMessageText</c>.</summary>
    private static Comparison CompareMessageText(Diagnostic d1, Diagnostic d2)
    {
        var headMsg1 = HeadMessageTextOf(GetDiagnosticMessage(d1));
        var headMsg2 = HeadMessageTextOf(GetDiagnosticMessage(d2));
        var chain1 = d1.MessageText as IDiagnosticMessageChain;
        var chain2 = d2.MessageText as IDiagnosticMessageChain;

        var res = Core.CompareStringsCaseSensitive(headMsg1, headMsg2);
        if (res != Comparison.EqualTo) return res;

        res = CompareMessageChain(chain1?.Next, chain2?.Next);
        if (res != Comparison.EqualTo) return res;

        if (d1.CanonicalHead is not null && d2.CanonicalHead is null) return Comparison.LessThan;
        if (d2.CanonicalHead is not null && d1.CanonicalHead is null) return Comparison.GreaterThan;

        return Comparison.EqualTo;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8828 — the <c>typeof … === "string" ? … : ….messageText</c> fold.</summary>
    private static string HeadMessageTextOf(object? message)
        => message is string text ? text : ((IDiagnosticMessageChain)message!).MessageText ?? "";

    /// <summary>ported from: src/compiler/utilities.ts:8848-8864 — <c>compareMessageChain</c>.</summary>
    private static Comparison CompareMessageChain(IList<IDiagnosticMessageChain>? c1, IList<IDiagnosticMessageChain>? c2)
    {
        if (c1 is null && c2 is null) return Comparison.EqualTo;
        if (c1 is null) return Comparison.GreaterThan;
        if (c2 is null) return Comparison.LessThan;

        var result = CompareMessageChainSize(c1, c2);
        return result != Comparison.EqualTo ? result : CompareMessageChainContent(c1, c2);
    }

    /// <summary>ported from: src/compiler/utilities.ts:8866-8892 — <c>compareMessageChainSize</c>.</summary>
    private static Comparison CompareMessageChainSize(IList<IDiagnosticMessageChain>? c1, IList<IDiagnosticMessageChain>? c2)
    {
        if (c1 is null && c2 is null) return Comparison.EqualTo;
        if (c1 is null) return Comparison.GreaterThan;
        if (c2 is null) return Comparison.LessThan;

        var res = Core.CompareValues(c2.Count, c1.Count);
        if (res != Comparison.EqualTo) return res;

        for (var i = 0; i < c2.Count; i++)
        {
            res = CompareMessageChainSize(c1[i].Next, c2[i].Next);
            if (res != Comparison.EqualTo) return res;
        }

        return Comparison.EqualTo;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8895-8914 — <c>compareMessageChainContent</c>.</summary>
    private static Comparison CompareMessageChainContent(IList<IDiagnosticMessageChain> c1, IList<IDiagnosticMessageChain> c2)
    {
        for (var i = 0; i < c2.Count; i++)
        {
            var res = Core.CompareStringsCaseSensitive(c1[i].MessageText, c2[i].MessageText);
            if (res != Comparison.EqualTo) return res;
            if (c1[i].Next is null) continue;
            res = CompareMessageChainContent(c1[i].Next!, c2[i].Next!);
            if (res != Comparison.EqualTo) return res;
        }

        return Comparison.EqualTo;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8917-8926 — <c>diagnosticsEqualityComparer</c>.</summary>
    public static bool DiagnosticsEqualityComparer(Diagnostic d1, Diagnostic d2)
    {
        var code1 = GetDiagnosticCode(d1);
        var code2 = GetDiagnosticCode(d2);
        var msg1 = GetDiagnosticMessage(d1);
        var msg2 = GetDiagnosticMessage(d2);
        return Core.CompareStringsCaseSensitive(GetDiagnosticFilePath(d1), GetDiagnosticFilePath(d2)) == Comparison.EqualTo
            && Core.CompareValues(d1.Start, d2.Start) == Comparison.EqualTo
            && Core.CompareValues(d1.Length, d2.Length) == Comparison.EqualTo
            && Core.CompareValues(code1, code2) == Comparison.EqualTo
            && MessageTextEqualityComparer(msg1, msg2);
    }

    /// <summary>ported from: src/compiler/utilities.ts:8928-8930 — <c>getDiagnosticCode</c>.</summary>
    private static int GetDiagnosticCode(Diagnostic d) => d.CanonicalHead?.Code ?? d.Code;

    /// <summary>ported from: src/compiler/utilities.ts:8932-8934 — <c>getDiagnosticMessage</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>||</c> is a truthiness test, so an empty canonical message text falls through to the
    /// diagnostic's own message; the port states that as <c>Length &gt; 0</c>.
    /// </remarks>
    private static object? GetDiagnosticMessage(Diagnostic d)
    {
        var canonical = d.CanonicalHead?.MessageText;
        return canonical is { Length: > 0 } ? canonical : d.MessageText;
    }

    /// <summary>ported from: src/compiler/utilities.ts:8936-8940 — <c>messageTextEqualityComparer</c>.</summary>
    private static bool MessageTextEqualityComparer(object? m1, object? m2)
        => Core.CompareStringsCaseSensitive(HeadMessageTextOf(m1), HeadMessageTextOf(m2)) == Comparison.EqualTo;
}

// ported from: src/compiler/types.ts:10080-10094 (the DiagnosticCollection interface)
// ported from: src/compiler/utilities.ts:6060-6132 (createDiagnosticCollection's object literal)
/// <summary>
/// The diagnostic collection <c>createDiagnosticCollection</c> returns: diagnostics kept sorted, per file, with a
/// shared array for the ones that belong to no file.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS keeps the arrays as <c>SortedArray&lt;T&gt;</c> — a branded plain array — and re-sorts on write via
/// <c>insertSorted</c>. The port keeps <see cref="List{T}"/> and calls <c>Core.InsertSorted</c>, the same function.
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>getGlobalDiagnostics</c> returns the <em>same</em> array instance on every call, and the
/// checker compares two of those results by reference to detect that new global diagnostics were appended
/// (<c>checker.ts:49729-49741</c>). The typed members below return the live list for that reason; the
/// <see cref="IDiagnosticCollection"/> members project, which allocates, so the checker must call the typed ones.
/// </para>
/// </remarks>
internal sealed class DiagnosticCollection : IDiagnosticCollection
{
    private List<Diagnostic> _nonFileDiagnostics = [];
    private readonly List<string> _filesWithDiagnostics = [];
    private readonly Dictionary<string, List<Diagnostic>> _fileDiagnostics = new(StringComparer.Ordinal);
    private bool _hasReadNonFileDiagnostics;

    /// <summary>ported from: src/compiler/utilities.ts:6083-6087 — <c>lookup</c>'s diagnostic-to-array choice.</summary>
    private List<Diagnostic>? DiagnosticsOf(Diagnostic diagnostic)
        => diagnostic.File is not null
            ? _fileDiagnostics.GetValueOrDefault(diagnostic.File.FileName ?? "")
            : _nonFileDiagnostics;

    /// <summary>ported from: src/compiler/utilities.ts:6069-6081 — <c>lookup</c>.</summary>
    public Diagnostic? Lookup(Diagnostic diagnostic)
    {
        var diagnostics = DiagnosticsOf(diagnostic);
        if (diagnostics is null) return null;
        var result = Core.BinarySearch(diagnostics, diagnostic, static d => d, Utilities.CompareDiagnosticsSkipRelatedInformation);
        if (result >= 0) return diagnostics[result];
        if (~result > 0 && Utilities.DiagnosticsEqualityComparer(diagnostic, diagnostics[~result - 1]))
        {
            return diagnostics[~result - 1];
        }

        return null;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6089-6113 — <c>add</c>.</summary>
    public void Add(Diagnostic diagnostic)
    {
        List<Diagnostic> diagnostics;
        if (diagnostic.File is not null)
        {
            var fileName = diagnostic.File.FileName ?? "";
            if (!_fileDiagnostics.TryGetValue(fileName, out var existing))
            {
                existing = [];
                _fileDiagnostics[fileName] = existing;
                Core.InsertSorted(_filesWithDiagnostics, fileName, Core.CompareStringsCaseSensitive);
            }

            diagnostics = existing;
        }
        else
        {
            // If we've already read the non-file diagnostics, do not modify the existing array.
            if (_hasReadNonFileDiagnostics)
            {
                _hasReadNonFileDiagnostics = false;
                _nonFileDiagnostics = [.. _nonFileDiagnostics];
            }

            diagnostics = _nonFileDiagnostics;
        }

        Core.InsertSorted(diagnostics, diagnostic, Utilities.CompareDiagnosticsSkipRelatedInformation, Utilities.DiagnosticsEqualityComparer);
    }

    /// <summary>ported from: src/compiler/utilities.ts:6124-6128 — <c>getGlobalDiagnostics</c>.</summary>
    public List<Diagnostic> GetGlobalDiagnostics()
    {
        _hasReadNonFileDiagnostics = true;
        return _nonFileDiagnostics;
    }

    /// <summary>ported from: src/compiler/utilities.ts:6119-6122 — <c>getDiagnostics(fileName)</c>.</summary>
    public List<Diagnostic> GetDiagnostics(string? fileName)
        => fileName is null ? [] : _fileDiagnostics.GetValueOrDefault(fileName) ?? [];

    /// <summary>ported from: src/compiler/utilities.ts:6130-6140 — <c>getDiagnostics()</c>.</summary>
    public List<Diagnostic> GetDiagnostics()
    {
        // PORT-NOTE: TS's `flatMapToMutable` over an empty list yields `[]`, not `undefined`; the port states the
        // same accumulation directly.
        var fileDiags = new List<Diagnostic>();
        foreach (var f in _filesWithDiagnostics)
        {
            fileDiags.AddRange(_fileDiagnostics[f]);
        }

        if (_nonFileDiagnostics.Count == 0)
        {
            return fileDiags;
        }

        fileDiags.InsertRange(0, _nonFileDiagnostics);
        return fileDiags;
    }

    // The declared interface (types.ts:10080-10094) is not what the checker calls; the typed members above are. The
    // explicit implementations exist so this class can be handed to anything typed `DiagnosticCollection`.
    void IDiagnosticCollection.Add(IDiagnostic? diagnostic) => Add((Diagnostic)diagnostic!);

    IDiagnostic? IDiagnosticCollection.Lookup(IDiagnostic? diagnostic) => Lookup((Diagnostic)diagnostic!);

    List<IDiagnostic> IDiagnosticCollection.GetGlobalDiagnostics() => [.. _nonFileDiagnostics];

    List<IDiagnostic> IDiagnosticCollection.GetDiagnostics() => [.. GetDiagnostics()];

    List<IDiagnosticWithLocation> IDiagnosticCollection.GetDiagnostics(string? fileName)
        => [.. GetDiagnostics(fileName).Cast<IDiagnosticWithLocation>()];

    // ported from: src/compiler/utilities.ts:6120 — the `DiagnosticWithLocation[]` overload of `getDiagnostics`.
    /// <summary>The <c>DiagnosticWithLocation</c> overload of <see cref="GetDiagnostics(string?)"/>.</summary>
    public List<Diagnostic> GetDiagnosticsWithLocation(string fileName) => GetDiagnostics(fileName);
}
