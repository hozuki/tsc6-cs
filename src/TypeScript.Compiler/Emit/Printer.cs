namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:1211-1446 (createPrinter's closure state — the fields live in
// Printer.State.cs —, the eight public members, the writer/source-file setters and reset), 1447-1555 (the emit family
// and the pipeline), 1556-2031 (the SyntaxKind dispatch), 4640-4860 (list formatting), 4861-5232 (the writer wrappers
// and the line-formatting engine), plus the module-level pieces those ranges use and no other slice owns:
// emitter.ts:6327-6343 (<c>createBracketsMap</c>/<c>getOpeningBracket</c>/<c>getClosingBracket</c>),
// emitter.ts:6355-6378 (<c>ParenthesizerRule</c>, <c>EmitFunction</c>, <c>getEmitListItem</c> — the port states the
// union as two overloads and folds the arity dispatch into <c>EmitNodeListItems</c>), transformer.ts:227-234
// (<c>noEmitSubstitution</c>/<c>noEmitNotification</c>), utilities.ts:1370 (<c>getInternalEmitFlags</c>),
// utilities.ts:7828-7836 (<c>getNewLineCharacter</c>), utilities.ts:7915-8000 (the range/line-difference helpers),
// utilities.ts:9293 (<c>getEmitModuleKind</c>), utilitiesPublic.ts:1471 (<c>isTokenKind</c>) and
// utilitiesPublic.ts:2603-2621 (<c>guessIndentation</c>).
internal sealed partial class Printer : IPrinter
{
    // ---------------------------------------------------------------------------------------------
    // The state this slice adds to Printer.State.cs (emitter.ts:1229, 1217-1219, 1275)
    // ---------------------------------------------------------------------------------------------

    private readonly IPrinterOptions _printerOptions;

    /// <summary>ported from: src/compiler/emitter.ts:1217 — <c>onEmitNode !== noEmitNotification</c>.</summary>
    private readonly bool _hasEmitNotification;

    /// <summary>ported from: src/compiler/emitter.ts:1219 — <c>substituteNode !== noEmitSubstitution</c>.</summary>
    private readonly bool _hasEmitSubstitution;

    private readonly Action _enterComment;
    private readonly Action _exitComment;

    /// <summary>ported from: src/compiler/emitter.ts:1211 — <c>createPrinter</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>createPrinter(printerOptions, handlers)</c> declares the closure's <c>var</c>s and returns the
    /// object literal of eight methods; the port's partial class is that closure, so the declaration and the
    /// destructuring of <c>handlers</c> are the constructor and the returned object literal is the public surface
    /// below. The four configured wrappers built on this factory are <see cref="PrinterFactory"/>'s.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's two handler defaults (<c>onEmitNode = noEmitNotification</c>, <c>substituteNode =
    /// noEmitSubstitution</c>, emitter.ts:1217-1219) are what <see cref="PrintHandlers.Default"/> supplies, because the
    /// generated <see cref="IPrintHandlers"/> states every member as required. The reference's two identity tests
    /// against those defaults (emitter.ts:1502, 1507, 1911) are therefore identity tests against that handler set.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>var write = writeBase</c> (emitter.ts:1254) is <c>_write = WriteBase</c>; <c>preserveSourceNewlines</c>
    /// keeps the reference's <c>boolean | undefined</c> in the <c>bool?</c> field <c>_preserveSourceNewlines</c>, so
    /// the reference's truthiness tests read <c>_preserveSourceNewlines == true</c>.
    /// </para>
    /// </remarks>
    internal Printer(IPrinterOptions? printerOptions = null, IPrintHandlers? handlers = null)
    {
        _printerOptions = printerOptions ?? new PrinterOptions();
        _handlers = handlers ?? PrintHandlers.Default;

        _extendedDiagnostics = _printerOptions.ExtendedDiagnostics == true;
        _omitBraceSourcePositions = _printerOptions.OmitBraceSourceMapPositions == true;
        _newLine = GetNewLineCharacter(_printerOptions);
        _moduleKind = GetEmitModuleKind(_printerOptions);
        _commentsDisabled = _printerOptions.RemoveComments == true;
        // The five pipeline-phase delegates `GetPipelinePhase` answers — see the PORT-NOTE on that member's field block.
        _pipelineEmitWithNotification = PipelineEmitWithNotification;
        _pipelineEmitWithSubstitution = PipelineEmitWithSubstitution;
        _pipelineEmitWithComments = PipelineEmitWithComments;
        _pipelineEmitWithSourceMaps = PipelineEmitWithSourceMaps;
        _pipelineEmitWithHint = PipelineEmitWithHint;
        _preserveSourceNewlines = _printerOptions.PreserveSourceNewlines;

        _hasEmitNotification = !ReferenceEquals(_handlers, PrintHandlers.Default);
        _hasEmitSubstitution = !ReferenceEquals(_handlers, PrintHandlers.Default);

        _parenthesizer = NodeFactory.Factory.Parenthesizer!;
        _typeArgumentParenthesizerRuleSelector = new OrdinalParenthesizerRuleSelector<ITypeNode>(
            index => index == 0 ? _parenthesizer.ParenthesizeLeadingTypeArgument : null);

        // emitter.ts:1275 — `performance.createTimerIf(extendedDiagnostics, "commentTime", "beforeComment", "afterComment")`.
        var commentTimer = Performance.CreateTimerIf(_extendedDiagnostics, "commentTime", "beforeComment", "afterComment");
        _enterComment = commentTimer.Enter;
        _exitComment = commentTimer.Exit;

        _emitBinaryExpression = CreateEmitBinaryExpression();
        _write = WriteBase;

        Reset();
    }

    // ---------------------------------------------------------------------------------------------
    // Public API (emitter.ts:1298-1393)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:1298-1318 — <c>printNode</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>node</c> is a <c>Node</c>; the generated <see cref="IPrinter.PrintNode"/>
    /// declares it <c>Node?</c>, so the port's null-forgiving uses are the generated face's, not the reference's.</remarks>
    public string PrintNode(EmitHint hint, Node? node, SourceFile? sourceFile)
    {
        switch (hint)
        {
            case EmitHint.SourceFile:
                Debug.Assert(NodeTests.IsSourceFile(node!), "Expected a SourceFile node.");
                break;
            case EmitHint.IdentifierName:
                Debug.Assert(NodeTests.IsIdentifier(node!), "Expected an Identifier node.");
                break;
            case EmitHint.Expression:
                Debug.Assert(Utilities.IsExpression(node!), "Expected an Expression node.");
                break;
        }
        switch (node!.Kind)
        {
            case SyntaxKind.SourceFile:
                return PrintFile((SourceFile)node);
            case SyntaxKind.Bundle:
                return PrintBundle((Bundle)node);
        }
        WriteNode(hint, node, sourceFile, BeginPrint());
        return EndPrint();
    }

    /// <summary>ported from: src/compiler/emitter.ts:1320-1323 — <c>printList</c>.</summary>
    /// <remarks>PORT-NOTE: the constraint is the generated <see cref="IPrinter"/>'s (<c>T : Node</c>); the list
    /// machinery's own constraint is <see cref="INode"/>, which a <c>T : Node</c> instantiation satisfies.</remarks>
    public string PrintList<T>(ListFormat format, NodeArray<T>? nodes, SourceFile? sourceFile) where T : Node
    {
        WriteList(format, nodes, sourceFile, BeginPrint());
        return EndPrint();
    }

    /// <summary>ported from: src/compiler/emitter.ts:1325-1328 — <c>printBundle</c>.</summary>
    public string PrintBundle(Bundle? bundle)
    {
        WriteBundle(bundle, BeginPrint(), /*sourceMapGenerator*/ null);
        return EndPrint();
    }

    /// <summary>ported from: src/compiler/emitter.ts:1330-1333 — <c>printFile</c>.</summary>
    public string PrintFile(SourceFile? sourceFile)
    {
        WriteFile(sourceFile, BeginPrint(), /*sourceMapGenerator*/ null);
        return EndPrint();
    }

    /// <summary>ported from: src/compiler/emitter.ts:1335-1345 — <c>writeNode</c>.</summary>
    /// <remarks>PORT-NOTE: the reference has two overloads that differ only in whether <c>sourceFile</c> may be
    /// omitted, and an assertion documents the relationship between the two; the generated
    /// <see cref="IPrinter.WriteNode"/> has one shape, which is the second overload's.</remarks>
    public void WriteNode(EmitHint hint, Node? node, SourceFile? sourceFile, IEmitTextWriter? output)
    {
        var previousWriter = _writer;
        SetWriter(output, /*_sourceMapGenerator*/ null);
        Print(hint, node!, sourceFile);
        Reset();
        _writer = previousWriter;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1348-1356 — <c>writeList</c>.</summary>
    public void WriteList<T>(ListFormat format, NodeArray<T>? nodes, SourceFile? sourceFile, IEmitTextWriter? output) where T : Node
    {
        var previousWriter = _writer;
        SetWriter(output, /*_sourceMapGenerator*/ null);
        if (sourceFile is not null)
        {
            SetSourceFile(sourceFile);
        }
        EmitList(/*parentNode*/ null, nodes, format);
        Reset();
        _writer = previousWriter;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1359-1372 — <c>writeBundle</c>.</summary>
    public void WriteBundle(Bundle? bundle, IEmitTextWriter? output, ISourceMapGenerator? sourceMapGenerator)
    {
        _isOwnFileEmit = false;
        var previousWriter = _writer;
        SetWriter(output, sourceMapGenerator);
        EmitShebangIfNeeded(bundle!);
        EmitPrologueDirectivesIfNeeded(bundle!);
        EmitHelpers(bundle!);
        EmitSyntheticTripleSlashReferencesIfNeeded(bundle!);
        foreach (var sourceFile in bundle!.SourceFiles!)
        {
            Print(EmitHint.SourceFile, sourceFile, sourceFile);
        }
        Reset();
        _writer = previousWriter;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1374-1383 — <c>writeFile</c>.</summary>
    public void WriteFile(SourceFile? sourceFile, IEmitTextWriter? output, ISourceMapGenerator? sourceMapGenerator)
    {
        _isOwnFileEmit = true;
        var previousWriter = _writer;
        SetWriter(output, sourceMapGenerator);
        EmitShebangIfNeeded(sourceFile!);
        EmitPrologueDirectivesIfNeeded(sourceFile!);
        Print(EmitHint.SourceFile, sourceFile!, sourceFile);
        Reset();
        _writer = previousWriter;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1385-1387 — <c>beginPrint</c>.</summary>
    private IEmitTextWriter BeginPrint() => _ownWriter ??= Utilities.CreateTextWriter(_newLine);

    /// <summary>ported from: src/compiler/emitter.ts:1389-1393 — <c>endPrint</c>.</summary>
    private string EndPrint()
    {
        var text = _ownWriter.GetText();
        _ownWriter.Clear();
        return text;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1395-1401 — <c>print</c>.</summary>
    private void Print(EmitHint hint, Node node, SourceFile? sourceFile)
    {
        if (sourceFile is not null)
        {
            SetSourceFile(sourceFile);
        }

        PipelineEmit(hint, node, /*parenthesizerRule*/ null);
    }

    /// <summary>ported from: src/compiler/emitter.ts:1403-1410 — <c>setSourceFile</c>.</summary>
    private void SetSourceFile(SourceFile? sourceFile)
    {
        _currentSourceFile = sourceFile;
        _currentLineMap = null;
        _detachedCommentsInfo = null;
        if (sourceFile is not null)
        {
            SetSourceMapSource(MapSourceOf(sourceFile));
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1407 — the <c>SourceMapSource</c> of the file being printed.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>SourceMapSource</c> (types.ts:8302-8307) is structural — <c>setSourceMapSource(sourceFile)</c>
    /// passes the source file itself, because a <c>SourceFile</c> has the three members it names. The port's generated
    /// <see cref="ISourceMapSource"/> is nominal, and the port's <see cref="SourceFile"/> does not implement it (nothing
    /// does), so the port states the same view as an adapter over the file. The adapter is kept for the file it wraps so
    /// that <c>setSourceMapSource</c>'s most-recently-added identity test (emitter.ts:6295-6300) keeps seeing one
    /// source; it costs nothing while source maps are disabled, which its early return already guards
    /// (emitter.ts:6289-6291). <c>SourceFile</c> implementing <see cref="ISourceMapSource"/> would remove the adapter.
    /// </remarks>
    private SourceFileMapSource MapSourceOf(SourceFile sourceFile)
        => _sourceFileMapSource is not null && ReferenceEquals(_sourceFileMapSource.SourceFile, sourceFile)
            ? _sourceFileMapSource
            : _sourceFileMapSource = new SourceFileMapSource(sourceFile);

    private SourceFileMapSource? _sourceFileMapSource;

    /// <summary>The adapter <see cref="MapSourceOf"/> returns.</summary>
    private sealed class SourceFileMapSource(SourceFile sourceFile) : ISourceMapSource
    {
        internal SourceFile SourceFile { get; } = sourceFile;

        public string? FileName
        {
            get => SourceFile.FileName;
            set => SourceFile.FileName = value;
        }

        public string? Text
        {
            get => SourceFile.Text;
            set => SourceFile.Text = value;
        }

        public List<int>? LineMap
        {
            get => SourceFile.LineMap;
            set => SourceFile.LineMap = value;
        }

        public Func<int, int>? SkipTrivia { get; set; }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1412-1420 — <c>setWriter</c>.</summary>
    private void SetWriter(IEmitTextWriter? writer, ISourceMapGenerator? sourceMapGenerator)
    {
        if (writer is not null && _printerOptions.OmitTrailingSemicolon == true)
        {
            writer = Utilities.GetTrailingSemicolonDeferringWriter(writer);
        }

        _writer = writer!; // TODO: GH#18217
        _sourceMapGenerator = sourceMapGenerator;
        _sourceMapsDisabled = writer is null || sourceMapGenerator is null;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1422-1441 — <c>reset</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference replaces the four stack locals with fresh empty arrays
    /// (<c>tempFlagsStack = []</c>, …); the port's are the readonly stacks of <c>Printer.State.cs</c>, so the reset
    /// clears them — the same state, the same observation, as a printer is not reentered between <c>reset</c> and the
    /// next print.
    /// </remarks>
    private void Reset()
    {
        _nodeIdToGeneratedName = [];
        _nodeIdToGeneratedPrivateName = [];
        _autoGeneratedIdToGeneratedName = [];
        _generatedNames = [];
        _formattedNameTempFlagsStack.Clear();
        _formattedNameTempFlags = [];
        _privateNameTempFlagsStack.Clear();
        _privateNameTempFlags = TempFlags.Auto;
        _tempFlagsStack.Clear();
        _tempFlags = TempFlags.Auto;
        _reservedNamesStack.Clear();
        _reservedNames = null;
        _reservedPrivateNamesStack.Clear();
        _reservedPrivateNames = null;
        _currentSourceFile = null;
        _currentLineMap = null;
        _detachedCommentsInfo = null;
        SetWriter(/*output*/ null, /*_sourceMapGenerator*/ null);
    }

    /// <summary>ported from: src/compiler/emitter.ts:1443-1445 — <c>getCurrentLineMap</c>.</summary>
    /// <remarks>PORT-NOTE: TS passes the source file to <c>getLineStarts</c>; the port's
    /// <c>ScannerUtilities.GetLineStarts</c> takes an <c>ISourceFileLike</c>, which <c>SourceFile</c> does not
    /// implement, so the port calls its own memo over the source file (<see cref="GetLineStarts(SourceFile)"/>).</remarks>
    internal IList<int> GetCurrentLineMap()
        => _currentLineMap ??= GetLineStarts(Debug.CheckDefined(_currentSourceFile));

    // ---------------------------------------------------------------------------------------------
    // The emit family and the pipeline (emitter.ts:1447-1554)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:1447-1452 — <c>emit</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's three signatures are the two overloads TS needs for its optional argument and the
    /// implementation; the port states the implementation's shape, as the rules for this slice require
    /// (<c>emit&lt;T extends Node&gt;</c> becomes <c>Emit(INode?, Func&lt;Node, Node&gt;?)</c>). The parameter is the
    /// model's <see cref="INode"/> rather than <see cref="Node"/> for the same reason the list machinery's constraint
    /// is: the port's unions are interfaces, so the reference's <c>emit(node.type)</c> call sites pass an
    /// <c>ITypeNode</c>. Every node in the tree is a <see cref="Node"/> at run time, which is the cast the pipeline
    /// needs.
    /// </remarks>
    internal void Emit(INode? node, Func<Node, Node>? parenthesizerRule = null)
    {
        if (node is null) return;
        PipelineEmit(EmitHint.Unspecified, (Node)node, parenthesizerRule);
    }

    /// <summary>ported from: src/compiler/emitter.ts:1454-1459 — <c>emitIdentifierName</c>.</summary>
    internal void EmitIdentifierName(INode? node)
    {
        if (node is null) return;
        PipelineEmit(EmitHint.IdentifierName, (Node)node, /*parenthesizerRule*/ null);
    }

    /// <summary>ported from: src/compiler/emitter.ts:1461-1466 — <c>emitExpression</c>.</summary>
    internal void EmitExpression(INode? node, Func<Node, Node>? parenthesizerRule = null)
    {
        if (node is null) return;
        PipelineEmit(EmitHint.Expression, (Node)node, parenthesizerRule);
    }

    /// <summary>ported from: src/compiler/emitter.ts:1468-1470 — <c>emitJsxAttributeValue</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>JsxAttributeValue</c> is the union <c>StringLiteral | JsxExpression</c>, which the
    /// port's model states as no single class; the body only tests <c>isStringLiteral</c>, so the parameter is the
    /// model's <see cref="INode"/>.</remarks>
    internal void EmitJsxAttributeValue(INode node)
        => PipelineEmit(NodeTests.IsStringLiteral((Node)node) ? EmitHint.JsxAttributeValue : EmitHint.Unspecified, (Node)node);

    /// <summary>ported from: src/compiler/emitter.ts:1472-1476 — <c>beforeEmitNode</c>.</summary>
    internal void BeforeEmitNode(Node node)
    {
        if (_preserveSourceNewlines == true && (GetInternalEmitFlags(node) & InternalEmitFlags.IgnoreSourceNewlines) != 0)
        {
            _preserveSourceNewlines = false;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1478-1480 — <c>afterEmitNode</c>.</summary>
    internal void AfterEmitNode(bool? savedPreserveSourceNewlines) => _preserveSourceNewlines = savedPreserveSourceNewlines;

    /// <summary>ported from: src/compiler/emitter.ts:1482-1487 — <c>pipelineEmit</c>.</summary>
    internal void PipelineEmit(EmitHint emitHint, Node node, Func<Node, Node>? parenthesizerRule = null)
    {
        _currentParenthesizerRule = parenthesizerRule;
        var pipelinePhase = GetPipelinePhase(PipelinePhase.Notification, emitHint, node);
        pipelinePhase(emitHint, node);
        _currentParenthesizerRule = null;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1489-1491 — <c>shouldEmitComments</c>.</summary>
    internal bool ShouldEmitComments(Node node) => !_commentsDisabled && !NodeTests.IsSourceFile(node);

    /// <summary>ported from: src/compiler/emitter.ts:1493-1497 — <c>shouldEmitSourceMaps</c>.</summary>
    internal bool ShouldEmitSourceMaps(Node node)
        => !_sourceMapsDisabled
            && !NodeTests.IsSourceFile(node)
            && !Utilities.IsInJsonFile(node);

    /// <summary>ported from: src/compiler/emitter.ts:1499-1533 — <c>getPipelinePhase</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference returns one of the five phase functions, and its <c>onEmitNode !== noEmitNotification</c>
    /// / <c>substituteNode !== noEmitSubstitution</c> tests compare the destructured handler against the module-level
    /// defaults; the port's handlers are the generated interface's members, so those two tests are the identity tests
    /// against <see cref="PrintHandlers.Default"/> that the constructor computed (<c>_hasEmitNotification</c>,
    /// <c>_hasEmitSubstitution</c>). The reference's <c>!isEmitNotificationEnabled || isEmitNotificationEnabled(node)</c>
    /// is the port's <c>IsEmitNotificationEnabled(node)</c>, whose default implementation answers <c>true</c> — the same
    /// answer the reference reaches when that optional handler is absent (<see cref="PrintHandlers"/>).
    /// </remarks>
    // PORT-NOTE (performance, measured): the reference's `getPipelinePhase` answers one of five module-level functions,
    // and a JavaScript function reference is free. C# method-group conversions are not: each `return PipelineEmitWithX;`
    // builds a new delegate over `this`, and this member runs once per emitted node — the template-literal shard's
    // 844 files emit ~2.5M nodes, so those delegates were ~212 MiB of the emit phase's 250 MiB allocation (the whole
    // of its remaining 254 KB/file; docs §18.4n). The five delegates are built once per printer here instead; the
    // memoized printers of PrinterFactory build them once per program.
    private readonly Action<EmitHint, Node> _pipelineEmitWithNotification;

    private readonly Action<EmitHint, Node> _pipelineEmitWithSubstitution;

    private readonly Action<EmitHint, Node> _pipelineEmitWithComments;

    private readonly Action<EmitHint, Node> _pipelineEmitWithSourceMaps;

    private readonly Action<EmitHint, Node> _pipelineEmitWithHint;

    internal Action<EmitHint, Node> GetPipelinePhase(PipelinePhase phase, EmitHint emitHint, Node node)
    {
        switch (phase)
        {
            case PipelinePhase.Notification:
                if (_hasEmitNotification && _handlers.IsEmitNotificationEnabled(node))
                {
                    return _pipelineEmitWithNotification;
                }
                // falls through
                goto case PipelinePhase.Substitution;
            case PipelinePhase.Substitution:
                var substitution = _hasEmitSubstitution ? _handlers.SubstituteNode(emitHint, node) : null;
                if (_hasEmitSubstitution && substitution is not null)
                {
                    _lastSubstitution = substitution;
                    if (_lastSubstitution != node)
                    {
                        if (_currentParenthesizerRule is not null)
                        {
                            _lastSubstitution = _currentParenthesizerRule(_lastSubstitution);
                        }
                        return _pipelineEmitWithSubstitution;
                    }
                }
                // falls through
                goto case PipelinePhase.Comments;
            case PipelinePhase.Comments:
                if (ShouldEmitComments(node))
                {
                    return _pipelineEmitWithComments;
                }
                // falls through
                goto case PipelinePhase.SourceMaps;
            case PipelinePhase.SourceMaps:
                if (ShouldEmitSourceMaps(node))
                {
                    return _pipelineEmitWithSourceMaps;
                }
                // falls through
                goto case PipelinePhase.Emit;
            case PipelinePhase.Emit:
                return _pipelineEmitWithHint;
            default:
                Debug.AssertNever(phase);
                return null!;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1535-1537 — <c>getNextPipelinePhase</c>.</summary>
    /// <remarks>PORT-NOTE: <c>currentPhase + 1</c> is the reference's <c>PipelinePhase</c> arithmetic, which the port
    /// states as the ordinal increment of the enum (the members are declared in that order in
    /// <c>Printer.State.cs</c>).</remarks>
    internal Action<EmitHint, Node> GetNextPipelinePhase(PipelinePhase currentPhase, EmitHint emitHint, Node node)
        => GetPipelinePhase((PipelinePhase)((int)currentPhase + 1), emitHint, node);

    /// <summary>ported from: src/compiler/emitter.ts:1540-1554 — <c>pipelineEmitWithNotification</c>.</summary>
    internal void PipelineEmitWithNotification(EmitHint hint, Node node)
    {
        var pipelinePhase = GetNextPipelinePhase(PipelinePhase.Notification, hint, node);
        _handlers.OnEmitNode(hint, node, pipelinePhase);
    }

    /// <summary>ported from: src/compiler/emitter.ts:1540-1554 — <c>pipelineEmitWithHint</c>.</summary>
    internal void PipelineEmitWithHint(EmitHint hint, Node node)
    {
        _handlers.OnBeforeEmitNode?.Invoke(node);
        if (_preserveSourceNewlines == true)
        {
            var savedPreserveSourceNewlines = _preserveSourceNewlines;
            BeforeEmitNode(node);
            PipelineEmitWithHintWorker(hint, node);
            AfterEmitNode(savedPreserveSourceNewlines);
        }
        else
        {
            PipelineEmitWithHintWorker(hint, node);
        }
        _handlers.OnAfterEmitNode?.Invoke(node);
        // clear the parenthesizer rule as we ascend
        _currentParenthesizerRule = null;
    }

    /// <summary>ported from: src/compiler/emitter.ts:1556-2031 — <c>pipelineEmitWithHintWorker</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's arms are <c>return emitX(node as X)</c>. <c>return voidCall();</c> is CS0127 in C#, so every arm
    /// is the call followed by <c>return;</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: five arms pass a TS union (<c>NumericLiteral | BigIntLiteral</c>, <c>RestTypeNode |
    /// JSDocVariadicType</c>, <c>JSDocImplementsTag | JSDocAugmentsTag</c>, <c>JsxOpeningElement</c> with
    /// <c>JsxOpeningFragment</c>, <c>JsxClosingElement</c> with <c>JsxClosingFragment</c>, the accessor pair and the
    /// JSDoc-tag unions). The port's model has no single class for those, so the callee's parameter is
    /// <see cref="Node"/> and the arm passes the node itself; the two literal arms pass the union's common class.
    /// </para>
    /// <para>
    /// PORT-NOTE: the template-literal arms cast to <c>LiteralExpression</c> in TS because template parts are literals
    /// there; in the port's model they are <see cref="TemplateHead"/>/<see cref="TemplateMiddle"/>/
    /// <see cref="TemplateTail"/> (siblings of <c>LiteralExpression</c> under <c>ILiteralLikeNode</c>), so the cast
    /// names that interface — which is also the callee's parameter type.
    /// </para>
    /// </remarks>
    internal void PipelineEmitWithHintWorker(EmitHint hint, Node node, bool allowSnippets = true)
    {
        if (allowSnippets)
        {
            var snippet = EmitNodes.GetSnippetElement(node);
            if (snippet is not null)
            {
                EmitSnippetNode(hint, node, snippet);
                return;
            }
        }
        if (hint == EmitHint.SourceFile)
        {
            EmitSourceFile(Core.Cast<SourceFile, Node>(node, NodeTests.IsSourceFile));
            return;
        }
        if (hint == EmitHint.IdentifierName)
        {
            EmitIdentifier(Core.Cast<Identifier, Node>(node, NodeTests.IsIdentifier));
            return;
        }
        if (hint == EmitHint.JsxAttributeValue)
        {
            EmitLiteral((ILiteralLikeNode)node, /*jsxAttributeEscape*/ true);
            return;
        }
        if (hint == EmitHint.MappedTypeParameter)
        {
            EmitMappedTypeParameter(Core.Cast<TypeParameterDeclaration, Node>(node, NodeTests.IsTypeParameterDeclaration));
            return;
        }
        if (hint == EmitHint.ImportTypeNodeAttributes)
        {
            EmitImportTypeNodeAttributes(Core.Cast<ImportAttributes, Node>(node, NodeTests.IsImportAttributes));
            return;
        }
        if (hint == EmitHint.EmbeddedStatement)
        {
            Debug.AssertNode(node, NodeTests.IsEmptyStatement);
            EmitEmptyStatement(/*isEmbeddedStatement*/ true);
            return;
        }
        if (hint == EmitHint.Unspecified)
        {
            switch (node.Kind)
            {
                // Pseudo-literals
                case SyntaxKind.TemplateHead:
                case SyntaxKind.TemplateMiddle:
                case SyntaxKind.TemplateTail:
                    EmitLiteral((ILiteralLikeNode)node, /*jsxAttributeEscape*/ false);
                    return;

                // Identifiers
                case SyntaxKind.Identifier:
                    EmitIdentifier((Identifier)node);
                    return;

                // PrivateIdentifiers
                case SyntaxKind.PrivateIdentifier:
                    EmitPrivateIdentifier((PrivateIdentifier)node);
                    return;

                // Parse tree nodes
                // Names
                case SyntaxKind.QualifiedName:
                    EmitQualifiedName((QualifiedName)node);
                    return;
                case SyntaxKind.ComputedPropertyName:
                    EmitComputedPropertyName((ComputedPropertyName)node);
                    return;

                // Signature elements
                case SyntaxKind.TypeParameter:
                    EmitTypeParameter((TypeParameterDeclaration)node);
                    return;
                case SyntaxKind.Parameter:
                    EmitParameter((ParameterDeclaration)node);
                    return;
                case SyntaxKind.Decorator:
                    EmitDecorator((Decorator)node);
                    return;

                // Type members
                case SyntaxKind.PropertySignature:
                    EmitPropertySignature((PropertySignature)node);
                    return;
                case SyntaxKind.PropertyDeclaration:
                    EmitPropertyDeclaration((PropertyDeclaration)node);
                    return;
                case SyntaxKind.MethodSignature:
                    EmitMethodSignature((MethodSignature)node);
                    return;
                case SyntaxKind.MethodDeclaration:
                    EmitMethodDeclaration((MethodDeclaration)node);
                    return;
                case SyntaxKind.ClassStaticBlockDeclaration:
                    EmitClassStaticBlockDeclaration((ClassStaticBlockDeclaration)node);
                    return;
                case SyntaxKind.Constructor:
                    EmitConstructor((ConstructorDeclaration)node);
                    return;
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    EmitAccessorDeclaration(node);
                    return;
                case SyntaxKind.CallSignature:
                    EmitCallSignature((CallSignatureDeclaration)node);
                    return;
                case SyntaxKind.ConstructSignature:
                    EmitConstructSignature((ConstructSignatureDeclaration)node);
                    return;
                case SyntaxKind.IndexSignature:
                    EmitIndexSignature((IndexSignatureDeclaration)node);
                    return;

                // Types
                case SyntaxKind.TypePredicate:
                    EmitTypePredicate((TypePredicateNode)node);
                    return;
                case SyntaxKind.TypeReference:
                    EmitTypeReference((TypeReferenceNode)node);
                    return;
                case SyntaxKind.FunctionType:
                    EmitFunctionType((FunctionTypeNode)node);
                    return;
                case SyntaxKind.ConstructorType:
                    EmitConstructorType((ConstructorTypeNode)node);
                    return;
                case SyntaxKind.TypeQuery:
                    EmitTypeQuery((TypeQueryNode)node);
                    return;
                case SyntaxKind.TypeLiteral:
                    EmitTypeLiteral((TypeLiteralNode)node);
                    return;
                case SyntaxKind.ArrayType:
                    EmitArrayType((ArrayTypeNode)node);
                    return;
                case SyntaxKind.TupleType:
                    EmitTupleType((TupleTypeNode)node);
                    return;
                case SyntaxKind.OptionalType:
                    EmitOptionalType((OptionalTypeNode)node);
                    return;
                // SyntaxKind.RestType is handled below
                case SyntaxKind.UnionType:
                    EmitUnionType((UnionTypeNode)node);
                    return;
                case SyntaxKind.IntersectionType:
                    EmitIntersectionType((IntersectionTypeNode)node);
                    return;
                case SyntaxKind.ConditionalType:
                    EmitConditionalType((ConditionalTypeNode)node);
                    return;
                case SyntaxKind.InferType:
                    EmitInferType((InferTypeNode)node);
                    return;
                case SyntaxKind.ParenthesizedType:
                    EmitParenthesizedType((ParenthesizedTypeNode)node);
                    return;
                case SyntaxKind.ExpressionWithTypeArguments:
                    EmitExpressionWithTypeArguments((ExpressionWithTypeArguments)node);
                    return;
                case SyntaxKind.ThisType:
                    EmitThisType();
                    return;
                case SyntaxKind.TypeOperator:
                    EmitTypeOperator((TypeOperatorNode)node);
                    return;
                case SyntaxKind.IndexedAccessType:
                    EmitIndexedAccessType((IndexedAccessTypeNode)node);
                    return;
                case SyntaxKind.MappedType:
                    EmitMappedType((MappedTypeNode)node);
                    return;
                case SyntaxKind.LiteralType:
                    EmitLiteralType((LiteralTypeNode)node);
                    return;
                case SyntaxKind.NamedTupleMember:
                    EmitNamedTupleMember((NamedTupleMember)node);
                    return;
                case SyntaxKind.TemplateLiteralType:
                    EmitTemplateType((TemplateLiteralTypeNode)node);
                    return;
                case SyntaxKind.TemplateLiteralTypeSpan:
                    EmitTemplateTypeSpan((TemplateLiteralTypeSpan)node);
                    return;
                case SyntaxKind.ImportType:
                    EmitImportTypeNode((ImportTypeNode)node);
                    return;

                // Binding patterns
                case SyntaxKind.ObjectBindingPattern:
                    EmitObjectBindingPattern((ObjectBindingPattern)node);
                    return;
                case SyntaxKind.ArrayBindingPattern:
                    EmitArrayBindingPattern((ArrayBindingPattern)node);
                    return;
                case SyntaxKind.BindingElement:
                    EmitBindingElement((BindingElement)node);
                    return;

                // Misc
                case SyntaxKind.TemplateSpan:
                    EmitTemplateSpan((TemplateSpan)node);
                    return;
                case SyntaxKind.SemicolonClassElement:
                    EmitSemicolonClassElement();
                    return;

                // Statements
                case SyntaxKind.Block:
                    EmitBlock((Block)node);
                    return;
                case SyntaxKind.VariableStatement:
                    EmitVariableStatement((VariableStatement)node);
                    return;
                case SyntaxKind.EmptyStatement:
                    EmitEmptyStatement(/*isEmbeddedStatement*/ false);
                    return;
                case SyntaxKind.ExpressionStatement:
                    EmitExpressionStatement((ExpressionStatement)node);
                    return;
                case SyntaxKind.IfStatement:
                    EmitIfStatement((IfStatement)node);
                    return;
                case SyntaxKind.DoStatement:
                    EmitDoStatement((DoStatement)node);
                    return;
                case SyntaxKind.WhileStatement:
                    EmitWhileStatement((WhileStatement)node);
                    return;
                case SyntaxKind.ForStatement:
                    EmitForStatement((ForStatement)node);
                    return;
                case SyntaxKind.ForInStatement:
                    EmitForInStatement((ForInStatement)node);
                    return;
                case SyntaxKind.ForOfStatement:
                    EmitForOfStatement((ForOfStatement)node);
                    return;
                case SyntaxKind.ContinueStatement:
                    EmitContinueStatement((ContinueStatement)node);
                    return;
                case SyntaxKind.BreakStatement:
                    EmitBreakStatement((BreakStatement)node);
                    return;
                case SyntaxKind.ReturnStatement:
                    EmitReturnStatement((ReturnStatement)node);
                    return;
                case SyntaxKind.WithStatement:
                    EmitWithStatement((WithStatement)node);
                    return;
                case SyntaxKind.SwitchStatement:
                    EmitSwitchStatement((SwitchStatement)node);
                    return;
                case SyntaxKind.LabeledStatement:
                    EmitLabeledStatement((LabeledStatement)node);
                    return;
                case SyntaxKind.ThrowStatement:
                    EmitThrowStatement((ThrowStatement)node);
                    return;
                case SyntaxKind.TryStatement:
                    EmitTryStatement((TryStatement)node);
                    return;
                case SyntaxKind.DebuggerStatement:
                    EmitDebuggerStatement((DebuggerStatement)node);
                    return;

                // Declarations
                case SyntaxKind.VariableDeclaration:
                    EmitVariableDeclaration((VariableDeclaration)node);
                    return;
                case SyntaxKind.VariableDeclarationList:
                    EmitVariableDeclarationList((VariableDeclarationList)node);
                    return;
                case SyntaxKind.FunctionDeclaration:
                    EmitFunctionDeclaration((FunctionDeclaration)node);
                    return;
                case SyntaxKind.ClassDeclaration:
                    EmitClassDeclaration((ClassDeclaration)node);
                    return;
                case SyntaxKind.InterfaceDeclaration:
                    EmitInterfaceDeclaration((InterfaceDeclaration)node);
                    return;
                case SyntaxKind.TypeAliasDeclaration:
                    EmitTypeAliasDeclaration((TypeAliasDeclaration)node);
                    return;
                case SyntaxKind.EnumDeclaration:
                    EmitEnumDeclaration((EnumDeclaration)node);
                    return;
                case SyntaxKind.ModuleDeclaration:
                    EmitModuleDeclaration((ModuleDeclaration)node);
                    return;
                case SyntaxKind.ModuleBlock:
                    EmitModuleBlock((ModuleBlock)node);
                    return;
                case SyntaxKind.CaseBlock:
                    EmitCaseBlock((CaseBlock)node);
                    return;
                case SyntaxKind.NamespaceExportDeclaration:
                    EmitNamespaceExportDeclaration((NamespaceExportDeclaration)node);
                    return;
                case SyntaxKind.ImportEqualsDeclaration:
                    EmitImportEqualsDeclaration((ImportEqualsDeclaration)node);
                    return;
                case SyntaxKind.ImportDeclaration:
                    EmitImportDeclaration((ImportDeclaration)node);
                    return;
                case SyntaxKind.ImportClause:
                    EmitImportClause((ImportClause)node);
                    return;
                case SyntaxKind.NamespaceImport:
                    EmitNamespaceImport((NamespaceImport)node);
                    return;
                case SyntaxKind.NamespaceExport:
                    EmitNamespaceExport((NamespaceExport)node);
                    return;
                case SyntaxKind.NamedImports:
                    EmitNamedImports((NamedImports)node);
                    return;
                case SyntaxKind.ImportSpecifier:
                    EmitImportSpecifier((ImportSpecifier)node);
                    return;
                case SyntaxKind.ExportAssignment:
                    EmitExportAssignment((ExportAssignment)node);
                    return;
                case SyntaxKind.ExportDeclaration:
                    EmitExportDeclaration((ExportDeclaration)node);
                    return;
                case SyntaxKind.NamedExports:
                    EmitNamedExports((NamedExports)node);
                    return;
                case SyntaxKind.ExportSpecifier:
                    EmitExportSpecifier((ExportSpecifier)node);
                    return;
                case SyntaxKind.ImportAttributes:
                    EmitImportAttributes((ImportAttributes)node);
                    return;
                case SyntaxKind.ImportAttribute:
                    EmitImportAttribute((ImportAttribute)node);
                    return;
                case SyntaxKind.MissingDeclaration:
                    return;

                // Module references
                case SyntaxKind.ExternalModuleReference:
                    EmitExternalModuleReference((ExternalModuleReference)node);
                    return;

                // JSX (non-expression)
                case SyntaxKind.JsxText:
                    EmitJsxText((JsxText)node);
                    return;
                case SyntaxKind.JsxOpeningElement:
                case SyntaxKind.JsxOpeningFragment:
                    EmitJsxOpeningElementOrFragment(node);
                    return;
                case SyntaxKind.JsxClosingElement:
                case SyntaxKind.JsxClosingFragment:
                    EmitJsxClosingElementOrFragment(node);
                    return;
                case SyntaxKind.JsxAttribute:
                    EmitJsxAttribute((JsxAttribute)node);
                    return;
                case SyntaxKind.JsxAttributes:
                    EmitJsxAttributes((JsxAttributes)node);
                    return;
                case SyntaxKind.JsxSpreadAttribute:
                    EmitJsxSpreadAttribute((JsxSpreadAttribute)node);
                    return;
                case SyntaxKind.JsxExpression:
                    EmitJsxExpression((JsxExpression)node);
                    return;
                case SyntaxKind.JsxNamespacedName:
                    EmitJsxNamespacedName((JsxNamespacedName)node);
                    return;

                // Clauses
                case SyntaxKind.CaseClause:
                    EmitCaseClause((CaseClause)node);
                    return;
                case SyntaxKind.DefaultClause:
                    EmitDefaultClause((DefaultClause)node);
                    return;
                case SyntaxKind.HeritageClause:
                    EmitHeritageClause((HeritageClause)node);
                    return;
                case SyntaxKind.CatchClause:
                    EmitCatchClause((CatchClause)node);
                    return;

                // Property assignments
                case SyntaxKind.PropertyAssignment:
                    EmitPropertyAssignment((PropertyAssignment)node);
                    return;
                case SyntaxKind.ShorthandPropertyAssignment:
                    EmitShorthandPropertyAssignment((ShorthandPropertyAssignment)node);
                    return;
                case SyntaxKind.SpreadAssignment:
                    EmitSpreadAssignment((SpreadAssignment)node);
                    return;

                // Enum
                case SyntaxKind.EnumMember:
                    EmitEnumMember((EnumMember)node);
                    return;

                // Top-level nodes
                case SyntaxKind.SourceFile:
                    EmitSourceFile((SourceFile)node);
                    return;
                case SyntaxKind.Bundle:
                    Debug.Fail("Bundles should be printed using printBundle");
                    return;

                // JSDoc nodes (only used in codefixes currently)
                case SyntaxKind.JSDocTypeExpression:
                    EmitJSDocTypeExpression((JSDocTypeExpression)node);
                    return;
                case SyntaxKind.JSDocNameReference:
                    EmitJSDocNameReference((JSDocNameReference)node);
                    return;
                case SyntaxKind.JSDocAllType:
                    WritePunctuation("*");
                    return;
                case SyntaxKind.JSDocUnknownType:
                    WritePunctuation("?");
                    return;
                case SyntaxKind.JSDocNullableType:
                    EmitJSDocNullableType((JSDocNullableType)node);
                    return;
                case SyntaxKind.JSDocNonNullableType:
                    EmitJSDocNonNullableType((JSDocNonNullableType)node);
                    return;
                case SyntaxKind.JSDocOptionalType:
                    EmitJSDocOptionalType((JSDocOptionalType)node);
                    return;
                case SyntaxKind.JSDocFunctionType:
                    EmitJSDocFunctionType((JSDocFunctionType)node);
                    return;
                case SyntaxKind.RestType:
                case SyntaxKind.JSDocVariadicType:
                    EmitRestOrJSDocVariadicType(node);
                    return;
                case SyntaxKind.JSDocNamepathType:
                    return;
                case SyntaxKind.JSDoc:
                    EmitJSDoc((JSDoc)node);
                    return;
                case SyntaxKind.JSDocTypeLiteral:
                    EmitJSDocTypeLiteral((JSDocTypeLiteral)node);
                    return;
                case SyntaxKind.JSDocSignature:
                    EmitJSDocSignature((JSDocSignature)node);
                    return;
                case SyntaxKind.JSDocTag:
                case SyntaxKind.JSDocClassTag:
                case SyntaxKind.JSDocOverrideTag:
                    EmitJSDocSimpleTag((JSDocTag)node);
                    return;
                case SyntaxKind.JSDocAugmentsTag:
                case SyntaxKind.JSDocImplementsTag:
                    EmitJSDocHeritageTag(node);
                    return;
                case SyntaxKind.JSDocAuthorTag:
                case SyntaxKind.JSDocDeprecatedTag:
                    return;
                // SyntaxKind.JSDocClassTag (see JSDocTag, above)
                case SyntaxKind.JSDocPublicTag:
                case SyntaxKind.JSDocPrivateTag:
                case SyntaxKind.JSDocProtectedTag:
                case SyntaxKind.JSDocReadonlyTag:
                    return;
                case SyntaxKind.JSDocCallbackTag:
                    EmitJSDocCallbackTag((JSDocCallbackTag)node);
                    return;
                case SyntaxKind.JSDocOverloadTag:
                    EmitJSDocOverloadTag((JSDocOverloadTag)node);
                    return;
                // SyntaxKind.JSDocEnumTag (see below)
                case SyntaxKind.JSDocParameterTag:
                case SyntaxKind.JSDocPropertyTag:
                    EmitJSDocPropertyLikeTag(node);
                    return;
                case SyntaxKind.JSDocEnumTag:
                case SyntaxKind.JSDocReturnTag:
                case SyntaxKind.JSDocThisTag:
                case SyntaxKind.JSDocTypeTag:
                case SyntaxKind.JSDocThrowsTag:
                case SyntaxKind.JSDocSatisfiesTag:
                    EmitJSDocSimpleTypedTag(node);
                    return;
                case SyntaxKind.JSDocTemplateTag:
                    EmitJSDocTemplateTag((JSDocTemplateTag)node);
                    return;
                case SyntaxKind.JSDocTypedefTag:
                    EmitJSDocTypedefTag((JSDocTypedefTag)node);
                    return;
                case SyntaxKind.JSDocSeeTag:
                    EmitJSDocSeeTag((JSDocSeeTag)node);
                    return;
                case SyntaxKind.JSDocImportTag:
                    EmitJSDocImportTag((JSDocImportTag)node);
                    return;
                // SyntaxKind.JSDocPropertyTag (see JSDocParameterTag, above)

                // Transformation nodes
                case SyntaxKind.NotEmittedStatement:
                case SyntaxKind.NotEmittedTypeElement:
                    return;
            }
            if (Utilities.IsExpression(node))
            {
                hint = EmitHint.Expression;
                if (_hasEmitSubstitution)
                {
                    var substitute = _handlers.SubstituteNode(hint, node) ?? node;
                    if (substitute != node)
                    {
                        node = substitute;
                        if (_currentParenthesizerRule is not null)
                        {
                            node = _currentParenthesizerRule(node);
                        }
                    }
                }
            }
        }
        if (hint == EmitHint.Expression)
        {
            switch (node.Kind)
            {
                // Literals
                case SyntaxKind.NumericLiteral:
                case SyntaxKind.BigIntLiteral:
                    EmitNumericOrBigIntLiteral((LiteralExpression)node);
                    return;

                case SyntaxKind.StringLiteral:
                case SyntaxKind.RegularExpressionLiteral:
                case SyntaxKind.NoSubstitutionTemplateLiteral:
                    EmitLiteral((ILiteralLikeNode)node, /*jsxAttributeEscape*/ false);
                    return;

                // Identifiers
                case SyntaxKind.Identifier:
                    EmitIdentifier((Identifier)node);
                    return;
                case SyntaxKind.PrivateIdentifier:
                    EmitPrivateIdentifier((PrivateIdentifier)node);
                    return;

                // Expressions
                case SyntaxKind.ArrayLiteralExpression:
                    EmitArrayLiteralExpression((ArrayLiteralExpression)node);
                    return;
                case SyntaxKind.ObjectLiteralExpression:
                    EmitObjectLiteralExpression((ObjectLiteralExpression)node);
                    return;
                case SyntaxKind.PropertyAccessExpression:
                    EmitPropertyAccessExpression((PropertyAccessExpression)node);
                    return;
                case SyntaxKind.ElementAccessExpression:
                    EmitElementAccessExpression((ElementAccessExpression)node);
                    return;
                case SyntaxKind.CallExpression:
                    EmitCallExpression((CallExpression)node);
                    return;
                case SyntaxKind.NewExpression:
                    EmitNewExpression((NewExpression)node);
                    return;
                case SyntaxKind.TaggedTemplateExpression:
                    EmitTaggedTemplateExpression((TaggedTemplateExpression)node);
                    return;
                case SyntaxKind.TypeAssertionExpression:
                    EmitTypeAssertionExpression((TypeAssertion)node);
                    return;
                case SyntaxKind.ParenthesizedExpression:
                    EmitParenthesizedExpression((ParenthesizedExpression)node);
                    return;
                case SyntaxKind.FunctionExpression:
                    EmitFunctionExpression((FunctionExpression)node);
                    return;
                case SyntaxKind.ArrowFunction:
                    EmitArrowFunction((ArrowFunction)node);
                    return;
                case SyntaxKind.DeleteExpression:
                    EmitDeleteExpression((DeleteExpression)node);
                    return;
                case SyntaxKind.TypeOfExpression:
                    EmitTypeOfExpression((TypeOfExpression)node);
                    return;
                case SyntaxKind.VoidExpression:
                    EmitVoidExpression((VoidExpression)node);
                    return;
                case SyntaxKind.AwaitExpression:
                    EmitAwaitExpression((AwaitExpression)node);
                    return;
                case SyntaxKind.PrefixUnaryExpression:
                    EmitPrefixUnaryExpression((PrefixUnaryExpression)node);
                    return;
                case SyntaxKind.PostfixUnaryExpression:
                    EmitPostfixUnaryExpression((PostfixUnaryExpression)node);
                    return;
                case SyntaxKind.BinaryExpression:
                    _emitBinaryExpression((BinaryExpression)node);
                    return;
                case SyntaxKind.ConditionalExpression:
                    EmitConditionalExpression((ConditionalExpression)node);
                    return;
                case SyntaxKind.TemplateExpression:
                    EmitTemplateExpression((TemplateExpression)node);
                    return;
                case SyntaxKind.YieldExpression:
                    EmitYieldExpression((YieldExpression)node);
                    return;
                case SyntaxKind.SpreadElement:
                    EmitSpreadElement((SpreadElement)node);
                    return;
                case SyntaxKind.ClassExpression:
                    EmitClassExpression((ClassExpression)node);
                    return;
                case SyntaxKind.OmittedExpression:
                    return;
                case SyntaxKind.AsExpression:
                    EmitAsExpression((AsExpression)node);
                    return;
                case SyntaxKind.NonNullExpression:
                    EmitNonNullExpression((NonNullExpression)node);
                    return;
                case SyntaxKind.ExpressionWithTypeArguments:
                    EmitExpressionWithTypeArguments((ExpressionWithTypeArguments)node);
                    return;
                case SyntaxKind.SatisfiesExpression:
                    EmitSatisfiesExpression((SatisfiesExpression)node);
                    return;
                case SyntaxKind.MetaProperty:
                    EmitMetaProperty((MetaProperty)node);
                    return;
                case SyntaxKind.SyntheticExpression:
                    Debug.Fail("SyntheticExpression should never be printed.");
                    return;
                case SyntaxKind.MissingDeclaration:
                    return;

                // JSX
                case SyntaxKind.JsxElement:
                    EmitJsxElement((JsxElement)node);
                    return;
                case SyntaxKind.JsxSelfClosingElement:
                    EmitJsxSelfClosingElement((JsxSelfClosingElement)node);
                    return;
                case SyntaxKind.JsxFragment:
                    EmitJsxFragment((JsxFragment)node);
                    return;

                // Synthesized list
                case SyntaxKind.SyntaxList:
                    Debug.Fail("SyntaxList should not be printed");
                    return;

                // Transformation nodes
                case SyntaxKind.NotEmittedStatement:
                    return;
                case SyntaxKind.PartiallyEmittedExpression:
                    EmitPartiallyEmittedExpression((PartiallyEmittedExpression)node);
                    return;
                case SyntaxKind.CommaListExpression:
                    EmitCommaList((CommaListExpression)node);
                    return;
                case SyntaxKind.SyntheticReferenceExpression:
                    Debug.Fail("SyntheticReferenceExpression should not be printed");
                    return;
            }
        }
        if (Utilities.IsKeyword(node.Kind))
        {
            WriteTokenNode(node, WriteKeyword);
            return;
        }
        if (IsTokenKind(node.Kind))
        {
            WriteTokenNode(node, WritePunctuation);
            return;
        }
        Debug.Fail($"Unhandled SyntaxKind: {Debug.FormatSyntaxKind(node.Kind)}.");
    }

    // ---------------------------------------------------------------------------------------------
    // List formatting (emitter.ts:4640-4860)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:4640-4661 — <c>writeDelimiter</c>.</summary>
    private void WriteDelimiter(ListFormat format)
    {
        switch (format & ListFormat.DelimitersMask)
        {
            case ListFormat.None:
                break;
            case ListFormat.CommaDelimited:
                WritePunctuation(",");
                break;
            case ListFormat.BarDelimited:
                WriteSpace();
                WritePunctuation("|");
                break;
            case ListFormat.AsteriskDelimited:
                WriteSpace();
                WritePunctuation("*");
                WriteSpace();
                break;
            case ListFormat.AmpersandDelimited:
                WriteSpace();
                WritePunctuation("&");
                break;
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4663-4673 — <c>emitList</c>, with the reference's
    /// <c>ParenthesizerRule</c> arm of the fourth parameter.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's fourth parameter is the union <c>ParenthesizerRuleOrSelector&lt;Child&gt;</c>
    /// (emitter.ts:6357). C# will not convert a lambda to a class that has an <c>implicit operator</c> from a delegate,
    /// so the port states the union's two arms as this overload and the <c>OrdinalParenthesizerRuleSelector</c>
    /// overload below. A call site that passes an explicit <c>null</c> rule together with a start or count must name
    /// the parameter (<c>parenthesizerRule: null</c>) to disambiguate the two.
    /// </remarks>
    internal void EmitList<T>(Node? parentNode, NodeArray<T>? children, ListFormat format, Func<Node, Node>? parenthesizerRule = null, int? start = null, int? count = null) where T : INode
        => EmitNodeList(
            Emit,
            parentNode,
            children,
            format | (parentNode is not null && (Utilities.GetEmitFlags(parentNode) & EmitFlags.MultiLine) != 0 ? ListFormat.PreferNewLine : ListFormat.None),
            parenthesizerRule,
            /*parenthesizerRuleSelector*/ null,
            start,
            count);

    /// <summary>ported from: src/compiler/emitter.ts:4663-4673 — <c>emitList</c>, with the reference's
    /// <c>OrdinalParentheizerRuleSelector</c> arm of the fourth parameter (emitter.ts:6351-6353).</summary>
    internal void EmitList<T>(Node? parentNode, NodeArray<T>? children, ListFormat format, OrdinalParenthesizerRuleSelector<T> parenthesizerRuleSelector, int? start = null, int? count = null) where T : INode
        => EmitNodeList(
            Emit,
            parentNode,
            children,
            format | (parentNode is not null && (Utilities.GetEmitFlags(parentNode) & EmitFlags.MultiLine) != 0 ? ListFormat.PreferNewLine : ListFormat.None),
            /*parenthesizerRule*/ null,
            parenthesizerRuleSelector,
            start,
            count);

    /// <summary>ported from: src/compiler/emitter.ts:4675-4677 — <c>emitExpressionList</c>.</summary>
    internal void EmitExpressionList<T>(Node? parentNode, NodeArray<T>? children, ListFormat format, Func<Node, Node>? parenthesizerRule = null, int? start = null, int? count = null) where T : INode
        => EmitNodeList(EmitExpression, parentNode, children, format, parenthesizerRule, /*parenthesizerRuleSelector*/ null, start, count);

    /// <summary>ported from: src/compiler/emitter.ts:4675-4677 — <c>emitExpressionList</c>, selector arm.</summary>
    internal void EmitExpressionList<T>(Node? parentNode, NodeArray<T>? children, ListFormat format, OrdinalParenthesizerRuleSelector<T> parenthesizerRuleSelector, int? start = null, int? count = null) where T : INode
        => EmitNodeList(EmitExpression, parentNode, children, format, /*parenthesizerRule*/ null, parenthesizerRuleSelector, start, count);

    /// <summary>ported from: src/compiler/emitter.ts:4679-4727 — <c>emitNodeList</c>.</summary>
    private void EmitNodeList<T>(
        Action<Node, Func<Node, Node>?> emit,
        Node? parentNode,
        NodeArray<T>? children,
        ListFormat format,
        Func<Node, Node>? parenthesizerRule,
        OrdinalParenthesizerRuleSelector<T>? parenthesizerRuleSelector,
        int? start = null,
        int? count = null) where T : INode
    {
        start ??= 0;
        count ??= children is not null ? children.Count - start.Value : 0;

        var isUndefined = children is null;
        if (isUndefined && (format & ListFormat.OptionalIfUndefined) != 0)
        {
            return;
        }

        var isEmpty = children is null || start.Value >= children.Count || count.Value == 0;
        if (isEmpty && (format & ListFormat.OptionalIfEmpty) != 0)
        {
            OnBeforeEmitNodeArray(children);
            OnAfterEmitNodeArray(children);
            return;
        }

        if ((format & ListFormat.BracketsMask) != 0)
        {
            WritePunctuation(GetOpeningBracket(format));
            if (isEmpty && children is not null)
            {
                EmitTrailingCommentsOfPosition(children.Pos, /*prefixSpace*/ true); // Emit comments within empty bracketed lists
            }
        }

        OnBeforeEmitNodeArray(children);

        if (isEmpty)
        {
            // Write a line terminator if the parent node was multi-line
            if ((format & ListFormat.MultiLine) != 0
                && !(_preserveSourceNewlines == true && (parentNode is null || (_currentSourceFile is not null && RangeIsOnSingleLine(parentNode, _currentSourceFile)))))
            {
                WriteLine();
            }
            else if ((format & ListFormat.SpaceBetweenBraces) != 0 && (format & ListFormat.NoSpaceIfEmpty) == 0)
            {
                WriteSpace();
            }
        }
        else
        {
            EmitNodeListItems(emit, parentNode, children!, format, parenthesizerRule, parenthesizerRuleSelector, start.Value, count.Value, children!.HasTrailingComma, children);
        }

        OnAfterEmitNodeArray(children);

        if ((format & ListFormat.BracketsMask) != 0)
        {
            if (isEmpty && children is not null)
            {
                EmitLeadingCommentsOfPosition(children.End); // Emit leading comments within empty lists
            }
            WritePunctuation(GetClosingBracket(format));
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4729-4860 — <c>emitNodeListItems</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference takes the union <c>ParenthesizerRuleOrSelector&lt;Child&gt;</c> and resolves it per
    /// element through <c>getEmitListItem</c> (emitter.ts:6374-6378), which chooses between three module-level
    /// functions by the emit function's arity and the rule's own type. The port states the union as two parameters
    /// (exactly one non-null), and the arity test disappears because the port's emit functions are not generic: the
    /// plain rule is passed through unchanged, and the selector's typed rule is adapted at the element
    /// (<see cref="AdaptRule{T}"/>) — the reference does not need that adapter because its <c>emit</c> is generic over
    /// the child type.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference is generic over <c>Child extends Node</c>; the port's constraint is
    /// <see cref="INode"/>, because the lists the printer emits are the model's interface-typed unions (for instance
    /// the type-argument list is a <c>NodeArray&lt;ITypeNode&gt;</c>). Every element is a <see cref="Node"/> at run
    /// time, which is what the <c>(Node)</c> casts on each element state.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>children[start]</c> and <c>children[start + count - 1]</c> are <c>undefined</c> for TS when out of
    /// range (the callers of this method rely on that for the leading-element lookup); a C# indexer throws, so the
    /// port states the same "absent" answer with a bounds test.
    /// </para>
    /// </remarks>
    internal void EmitNodeListItems<T>(
        Action<Node, Func<Node, Node>?> emit,
        Node? parentNode,
        IList<T> children,
        ListFormat format,
        Func<Node, Node>? parenthesizerRule,
        OrdinalParenthesizerRuleSelector<T>? parenthesizerRuleSelector,
        int start,
        int count,
        bool hasTrailingComma,
        ITextRange? childrenTextRange) where T : INode
    {
        // Write the opening line terminator or leading whitespace.
        var mayEmitInterveningComments = (format & ListFormat.NoInterveningComments) == 0;
        var shouldEmitInterveningComments = mayEmitInterveningComments;

        var leadingLineTerminatorCount = GetLeadingLineTerminatorCount(parentNode, start < children.Count ? (Node)(object)children[start] : null, format);
        if (leadingLineTerminatorCount != 0)
        {
            WriteLine(leadingLineTerminatorCount);
            shouldEmitInterveningComments = false;
        }
        else if ((format & ListFormat.SpaceBetweenBraces) != 0)
        {
            WriteSpace();
        }

        // Increase the indent, if requested.
        if ((format & ListFormat.Indented) != 0)
        {
            IncreaseIndent();
        }

        // Emit each child.
        Node? previousSibling = null;
        var shouldDecreaseIndentAfterEmit = false;
        for (var i = 0; i < count; i++)
        {
            var child = (Node)(object)children[start + i]!;

            // Write the delimiter if this is not the first node.
            if ((format & ListFormat.AsteriskDelimited) != 0)
            {
                // always write JSDoc in the format "\n *"
                WriteLine();
                WriteDelimiter(format);
            }
            else if (previousSibling is not null)
            {
                // i.e
                //      function commentedParameters(
                //          /* Parameter a */
                //          a
                //          /* End of parameter a */ -> this comment isn't considered to be trailing comment of parameter "a" due to newline
                //          ,
                if ((format & ListFormat.DelimitersMask) != 0 && previousSibling.End != (parentNode is not null ? parentNode.End : -1))
                {
                    var previousSiblingEmitFlags = Utilities.GetEmitFlags(previousSibling);
                    if ((previousSiblingEmitFlags & EmitFlags.NoTrailingComments) == 0)
                    {
                        EmitLeadingCommentsOfPosition(previousSibling.End);
                    }
                }

                WriteDelimiter(format);

                // Write either a line terminator or whitespace to separate the elements.
                var separatingLineTerminatorCount = GetSeparatingLineTerminatorCount(previousSibling, child, format);
                if (separatingLineTerminatorCount > 0)
                {
                    // If a synthesized node in a single-line list starts on a new
                    // line, we should increase the indent.
                    if ((format & (ListFormat.LinesMask | ListFormat.Indented)) == ListFormat.SingleLine)
                    {
                        IncreaseIndent();
                        shouldDecreaseIndentAfterEmit = true;
                    }

                    if (shouldEmitInterveningComments && (format & ListFormat.DelimitersMask) != 0 && !ScannerUtilities.PositionIsSynthesized(child.Pos))
                    {
                        var commentRange = EmitNodes.GetCommentRange(child);
                        EmitTrailingCommentsOfPosition(commentRange.Pos, /*prefixSpace*/ (format & ListFormat.SpaceBetweenSiblings) != 0, /*forceNoNewline*/ true);
                    }

                    WriteLine(separatingLineTerminatorCount);
                    shouldEmitInterveningComments = false;
                }
                else if ((format & ListFormat.SpaceBetweenSiblings) != 0)
                {
                    WriteSpace();
                }
            }

            // Emit this child.
            if (shouldEmitInterveningComments)
            {
                var commentRange = EmitNodes.GetCommentRange(child);
                EmitTrailingCommentsOfPosition(commentRange.Pos);
            }
            else
            {
                shouldEmitInterveningComments = mayEmitInterveningComments;
            }

            _nextListElementPos = child.Pos;
            emit(child, parenthesizerRuleSelector is not null ? AdaptRule(parenthesizerRuleSelector.Select(i)) : parenthesizerRule);

            if (shouldDecreaseIndentAfterEmit)
            {
                DecreaseIndent();
                shouldDecreaseIndentAfterEmit = false;
            }

            previousSibling = child;
        }

        // Write a trailing comma, if requested.
        var emitFlags = previousSibling is not null ? Utilities.GetEmitFlags(previousSibling) : EmitFlags.None;
        var skipTrailingComments = _commentsDisabled || (emitFlags & EmitFlags.NoTrailingComments) != 0;
        var emitTrailingComma = hasTrailingComma && (format & ListFormat.AllowTrailingComma) != 0 && (format & ListFormat.CommaDelimited) != 0;
        if (emitTrailingComma)
        {
            if (previousSibling is not null && !skipTrailingComments)
            {
                EmitTokenWithComment(SyntaxKind.CommaToken, previousSibling.End, WritePunctuation, previousSibling);
            }
            else
            {
                WritePunctuation(",");
            }
        }

        // Emit any trailing comment of the last element in the list
        // i.e
        //       var array = [...
        //          2
        //          /* end of element 2 */
        //       ];
        if (previousSibling is not null
            && (parentNode is not null ? parentNode.End : -1) != previousSibling.End
            && (format & ListFormat.DelimitersMask) != 0
            && !skipTrailingComments)
        {
            EmitLeadingCommentsOfPosition(emitTrailingComma && childrenTextRange is not null && childrenTextRange.End != 0 ? childrenTextRange.End : previousSibling.End);
        }

        // Decrease the indent, if requested.
        if ((format & ListFormat.Indented) != 0)
        {
            DecreaseIndent();
        }

        // Write the closing line terminator or closing whitespace.
        var lastChildIndex = start + count - 1;
        var closingLineTerminatorCount = GetClosingLineTerminatorCount(
            parentNode,
            lastChildIndex >= 0 && lastChildIndex < children.Count ? (Node)(object)children[lastChildIndex] : null,
            format,
            childrenTextRange);
        if (closingLineTerminatorCount != 0)
        {
            WriteLine(closingLineTerminatorCount);
        }
        else if ((format & (ListFormat.SpaceAfterList | ListFormat.SpaceBetweenBraces)) != 0)
        {
            WriteSpace();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1215-1226 — <c>onBeforeEmitNodeArray</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types both array hooks over <c>NodeArray&lt;any&gt;</c> (types.ts:9857-9858) and so passes any
    /// <c>NodeArray&lt;T&gt;</c> — including <c>undefined</c> — to them. The port's generated
    /// <see cref="IPrintHandlers"/> states them as <c>Action&lt;NodeArray&lt;object?&gt;&gt;</c>, and C# generics are
    /// invariant, so the port hands the hook a <c>NodeArray&lt;object?&gt;</c> carrying the same range and the same
    /// element references. The argument is only built when a hook is installed (the emit path installs none before
    /// M8), because the null-conditional invocation short-circuits.
    /// </remarks>
    internal void OnBeforeEmitNodeArray<T>(NodeArray<T>? nodes) where T : INode
        => _handlers.OnBeforeEmitNodeArray?.Invoke(AsObjectNodeArray(nodes)!);

    /// <summary>ported from: src/compiler/emitter.ts:1215-1226 — <c>onAfterEmitNodeArray</c>.</summary>
    internal void OnAfterEmitNodeArray<T>(NodeArray<T>? nodes) where T : INode
        => _handlers.OnAfterEmitNodeArray?.Invoke(AsObjectNodeArray(nodes)!);

    /// <summary>The conversion <see cref="OnBeforeEmitNodeArray{T}"/> and <see cref="OnAfterEmitNodeArray{T}"/> share.</summary>
    private static NodeArray<object?>? AsObjectNodeArray<T>(NodeArray<T>? nodes) where T : INode
        => nodes is null
            ? null
            : new NodeArray<object?>(nodes.Cast<object?>())
            {
                Pos = nodes.Pos,
                End = nodes.End,
                HasTrailingComma = nodes.HasTrailingComma,
            };

    /// <summary>ported from: src/compiler/emitter.ts:6355-6373 — <c>ParenthesizerRule&lt;T&gt;</c>'s adapter.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>emitListItemWithParenthesizerRuleSelector</c> (emitter.ts:6366-6368) passes the selector's
    /// typed rule straight to the generic <c>emit</c>; the port's <see cref="Emit"/> takes a <c>Func&lt;Node,
    /// Node&gt;</c>, so the typed rule is adapted per element — one delegate for the element, which is the only
    /// allocation the reference avoids by being generic. TS's <c>emitListItemNoParenthesizer</c> (emitter.ts:6362-6364)
    /// and <c>getEmitListItem</c>'s arity dispatch (emitter.ts:6374-6378) have no port counterpart: the port's emit
    /// functions are not generic, so <see cref="EmitNodeListItems{T}"/> states the one call they collapse to.
    /// </remarks>
    private static Func<Node, Node>? AdaptRule<T>(Func<T, T>? rule) where T : INode
        => rule is null ? null : node => (Node)(object)rule((T)(object)node);

    // ---------------------------------------------------------------------------------------------
    // Writers (emitter.ts:4861-4950)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:4861-4863 — <c>writeLiteral</c>.</summary>
    private void WriteLiteral(string? s) => _writer.WriteLiteral(s);

    /// <summary>ported from: src/compiler/emitter.ts:4865-4867 — <c>writeStringLiteral</c>.</summary>
    private void WriteStringLiteral(string? s) => _writer.WriteStringLiteral(s);

    /// <summary>ported from: src/compiler/emitter.ts:4869-4871 — <c>writeBase</c>.</summary>
    private void WriteBase(string? s) => _writer.Write(s);

    /// <summary>ported from: src/compiler/emitter.ts:1254 — <c>write</c>, whose value is <c>writeBase</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>write</c> is the closure's <c>Action&lt;string&gt;</c>-valued local bound to
    /// <c>writeBase</c>; the port keeps that binding in the <c>_write</c> field (assigned in the constructor beside the
    /// other closure initializations) and states the local as this method, so the reference's <c>write(...)</c> call sites
    /// — this file's and the other slices' — read the same.</remarks>
    private void Write(string? s) => _write(s);

    /// <summary>ported from: src/compiler/emitter.ts:4873-4875 — <c>writeSymbol</c>.</summary>
    private void WriteSymbol(string? s, ISymbol? sym) => _writer.WriteSymbol(s, sym);

    /// <summary>ported from: src/compiler/emitter.ts:4877-4879 — <c>writePunctuation</c>.</summary>
    private void WritePunctuation(string? s) => _writer.WritePunctuation(s);

    /// <summary>ported from: src/compiler/emitter.ts:4881-4883 — <c>writeTrailingSemicolon</c>.</summary>
    private void WriteTrailingSemicolon() => _writer.WriteTrailingSemicolon(";");

    /// <summary>ported from: src/compiler/emitter.ts:4885-4887 — <c>writeKeyword</c>.</summary>
    private void WriteKeyword(string? s) => _writer.WriteKeyword(s);

    /// <summary>ported from: src/compiler/emitter.ts:4889-4891 — <c>writeOperator</c>.</summary>
    private void WriteOperator(string? s) => _writer.WriteOperator(s);

    /// <summary>ported from: src/compiler/emitter.ts:4893-4895 — <c>writeParameter</c>.</summary>
    private void WriteParameter(string? s) => _writer.WriteParameter(s);

    /// <summary>ported from: src/compiler/emitter.ts:4897-4899 — <c>writeComment</c>.</summary>
    private void WriteComment(string? s) => _writer.WriteComment(s);

    /// <summary>ported from: src/compiler/emitter.ts:4901-4903 — <c>writeSpace</c>.</summary>
    private void WriteSpace() => _writer.WriteSpace(" ");

    /// <summary>ported from: src/compiler/emitter.ts:4905-4907 — <c>writeProperty</c>.</summary>
    private void WriteProperty(string? s) => _writer.WriteProperty(s);

    /// <summary>ported from: src/compiler/emitter.ts:4909-4916 — <c>nonEscapingWrite</c>.</summary>
    /// <remarks>PORT-NOTE: TS tests <c>writer.nonEscapingWrite</c> for presence and falls back to <c>writer.write</c>
    /// when it is absent (the reference's own comment says the member belongs to a snippet-escaping writer). The port's
    /// <see cref="IEmitTextWriter"/> always declares the member and <see cref="EmitTextWriter"/> implements it as
    /// <see cref="IEmitTextWriter.Write"/> — exactly the reference's fallback (its own PORT-NOTE says so) — so the call
    /// is unconditional.</remarks>
    private void NonEscapingWrite(string? s)
    {
        // This should be defined in a snippet-escaping text writer.
        _writer.NonEscapingWrite(s);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4919-4923 — <c>writeLine</c>.</summary>
    private void WriteLine(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _writer.WriteLine(i > 0);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4925-4927 — <c>increaseIndent</c>.</summary>
    private void IncreaseIndent() => _writer.IncreaseIndent();

    /// <summary>ported from: src/compiler/emitter.ts:4929-4931 — <c>decreaseIndent</c>.</summary>
    private void DecreaseIndent() => _writer.DecreaseIndent();

    /// <summary>ported from: src/compiler/emitter.ts:4933-4937 — <c>writeToken</c>.</summary>
    private int WriteToken(SyntaxKind token, int pos, Action<string> writer, Node? contextNode = null)
        => !_sourceMapsDisabled
            ? EmitTokenWithSourceMap(contextNode, token, writer, pos, WriteTokenText)
            : WriteTokenText(token, writer, pos);

    /// <summary>ported from: src/compiler/emitter.ts:4939-4947 — <c>writeTokenNode</c>.</summary>
    private void WriteTokenNode(Node node, Action<string> writer)
    {
        _handlers.OnBeforeEmitToken?.Invoke(node);
        writer(ScannerUtilities.TokenToString(node.Kind)!);
        _handlers.OnAfterEmitToken?.Invoke(node);
    }

    /// <summary>ported from: src/compiler/emitter.ts:4949-4955 — <c>writeTokenText</c>, the two-argument overload.</summary>
    /// <remarks>PORT-NOTE: TS declares this overload <c>void</c> — its call sites (emitter.ts:2486, 2808, 2836, 4031)
    /// discard the value — while the implementation's answer for an omitted position is <c>NaN</c>; the port answers
    /// <c>0</c>. The three-argument overload below is the one that converts to the reference's <c>emitCallback</c>
    /// (<c>Func&lt;SyntaxKind, Action&lt;string&gt;, int, int&gt;</c>, emitter.ts:4934-4936), which is why the position is
    /// a plain <c>int</c> there.</remarks>
    private int WriteTokenText(SyntaxKind token, Action<string> writer)
    {
        writer(ScannerUtilities.TokenToString(token)!);
        return 0;
    }

    /// <summary>ported from: src/compiler/emitter.ts:4950-4955 — <c>writeTokenText</c>.</summary>
    private int WriteTokenText(SyntaxKind token, Action<string> writer, int pos)
    {
        var tokenString = ScannerUtilities.TokenToString(token)!;
        writer(tokenString);
        return pos < 0 ? pos : pos + tokenString.Length;
    }

    // ---------------------------------------------------------------------------------------------
    // The line/formatting engine (emitter.ts:4957-5231)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:4957-4968 — <c>writeLineOrSpace</c>.</summary>
    private void WriteLineOrSpace(Node parentNode, Node prevChildNode, Node nextChildNode)
    {
        if ((Utilities.GetEmitFlags(parentNode) & EmitFlags.SingleLine) != 0)
        {
            WriteSpace();
        }
        else if (_preserveSourceNewlines == true)
        {
            var lines = GetLinesBetweenNodes(parentNode, prevChildNode, nextChildNode);
            if (lines != 0)
            {
                WriteLine(lines);
            }
            else
            {
                WriteSpace();
            }
        }
        else
        {
            WriteLine();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4975-4985 — <c>writeLines</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>lineText.slice(indentation)</c> is JS's clamping slice — a line shorter than the
    /// guessed indentation (the empty lines of a helper's text, which <c>guessIndentation</c> skips but this loop does
    /// not) answers the empty string, where C#'s <c>Substring</c> throws <c>ArgumentOutOfRangeException</c>. The guard
    /// states the clamp.
    /// </remarks>
    private void WriteLines(string text)
    {
        var lines = s_lineTerminatorSplitter.Split(text);
        var indentation = GuessIndentation(lines);
        foreach (var lineText in lines)
        {
            var line = indentation is not null && indentation != 0
                ? lineText.Length <= indentation.Value ? "" : lineText.Substring(indentation.Value)
                : lineText;
            if (line.Length != 0)
            {
                WriteLine();
                Write(line);
            }
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4977 — <c>text.split(/\r\n?|\n/)</c>.</summary>
    private static readonly System.Text.RegularExpressions.Regex s_lineTerminatorSplitter = new(@"\r\n?|\n");

    /// <summary>ported from: src/compiler/emitter.ts:4987-4995 — <c>writeLinesAndIndent</c>.</summary>
    private void WriteLinesAndIndent(int lineCount, bool writeSpaceIfNotIndenting)
    {
        if (lineCount != 0)
        {
            IncreaseIndent();
            WriteLine(lineCount);
        }
        else if (writeSpaceIfNotIndenting)
        {
            WriteSpace();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4997-5008 — <c>decreaseIndentIf</c>, the line-count arm.</summary>
    /// <remarks>PORT-NOTE: TS's first parameter is <c>boolean | number | undefined</c> — the reference's call sites pass
    /// either a line count or a boolean, and the second parameter is only ever a number. C# has no such union, so the
    /// port states the two shapes as two overloads, this one and <see cref="DecreaseIndentIf(bool)"/>.</remarks>
    private void DecreaseIndentIf(int value1, int value2 = 0)
    {
        if (value1 != 0)
        {
            DecreaseIndent();
        }
        if (value2 != 0)
        {
            DecreaseIndent();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:4997-5008 — <c>decreaseIndentIf</c>, the boolean arm.</summary>
    private void DecreaseIndentIf(bool value1)
    {
        if (value1)
        {
            DecreaseIndent();
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5010-5065 — <c>getLeadingLineTerminatorCount</c>.</summary>
    private int GetLeadingLineTerminatorCount(Node? parentNode, Node? firstChild, ListFormat format)
    {
        if ((format & ListFormat.PreserveLines) != 0 || _preserveSourceNewlines == true)
        {
            if ((format & ListFormat.PreferNewLine) != 0)
            {
                return 1;
            }

            if (firstChild is null)
            {
                return parentNode is null || (_currentSourceFile is not null && RangeIsOnSingleLine(parentNode, _currentSourceFile)) ? 0 : 1;
            }
            if (firstChild.Pos == _nextListElementPos)
            {
                // If this child starts at the beginning of a list item in a parent list, its leading
                // line terminators have already been written as the separating line terminators of the
                // parent list. Example:
                //
                // class Foo {
                //   constructor() {}
                //   public foo() {}
                // }
                //
                // The outer list is the list of class members, with one line terminator between the
                // constructor and the method. The constructor is written, the separating line terminator
                // is written, and then we start emitting the method. Its modifiers ([public]) constitute an inner
                // list, so we look for its leading line terminators. If we didn't know that we had already
                // written a newline as part of the parent list, it would appear that we need to write a
                // leading newline to start the modifiers.
                return 0;
            }
            if (firstChild.Kind == SyntaxKind.JsxText)
            {
                // JsxText will be written with its leading whitespace, so don't add more manually.
                return 0;
            }
            if (
                _currentSourceFile is not null && parentNode is not null &&
                !ScannerUtilities.PositionIsSynthesized(parentNode.Pos) &&
                !Utilities.NodeIsSynthesized(firstChild) &&
                (firstChild.Parent is null || Utilities.GetOriginalNode(firstChild.Parent) == Utilities.GetOriginalNode(parentNode))
            )
            {
                if (_preserveSourceNewlines == true)
                {
                    return GetEffectiveLines(
                        includeComments =>
                            GetLinesBetweenPositionAndPrecedingNonWhitespaceCharacter(
                                firstChild.Pos,
                                parentNode!.Pos,
                                _currentSourceFile!,
                                includeComments));
                }
                return RangeStartPositionsAreOnSameLine(parentNode!, firstChild, _currentSourceFile!) ? 0 : 1;
            }
            if (SynthesizedNodeStartsOnNewLine(firstChild, format))
            {
                return 1;
            }
        }
        return (format & ListFormat.MultiLine) != 0 ? 1 : 0;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5067-5108 — <c>getSeparatingLineTerminatorCount</c>.</summary>
    private int GetSeparatingLineTerminatorCount(Node? previousNode, Node nextNode, ListFormat format)
    {
        if ((format & ListFormat.PreserveLines) != 0 || _preserveSourceNewlines == true)
        {
            if (previousNode is null || nextNode is null)
            {
                return 0;
            }
            if (nextNode.Kind == SyntaxKind.JsxText)
            {
                // JsxText will be written with its leading whitespace, so don't add more manually.
                return 0;
            }
            else if (_currentSourceFile is not null && !Utilities.NodeIsSynthesized(previousNode) && !Utilities.NodeIsSynthesized(nextNode))
            {
                if (_preserveSourceNewlines == true && SiblingNodePositionsAreComparable(previousNode, nextNode))
                {
                    return GetEffectiveLines(
                        includeComments =>
                            GetLinesBetweenRangeEndAndRangeStart(
                                previousNode,
                                nextNode,
                                _currentSourceFile!,
                                includeComments));
                }
                // If `preserveSourceNewlines` is `false` we do not intend to preserve the effective lines between the
                // previous and next node. Instead we naively check whether nodes are on separate lines within the
                // same node parent. If so, we intend to preserve a single line terminator. This is less precise and
                // expensive than checking with `preserveSourceNewlines` as above, but the goal is not to preserve the
                // effective source lines between two sibling nodes.
                else if (_preserveSourceNewlines != true && OriginalNodesHaveSameParent(previousNode, nextNode))
                {
                    return RangeEndIsOnSameLineAsRangeStart(previousNode, nextNode, _currentSourceFile) ? 0 : 1;
                }
                // If the two nodes are not comparable, add a line terminator based on the format that can indicate
                // whether new lines are preferred or not.
                return (format & ListFormat.PreferNewLine) != 0 ? 1 : 0;
            }
            else if (SynthesizedNodeStartsOnNewLine(previousNode, format) || SynthesizedNodeStartsOnNewLine(nextNode, format))
            {
                return 1;
            }
        }
        else if (EmitNodes.GetStartsOnNewLine(nextNode) == true)
        {
            return 1;
        }
        return (format & ListFormat.MultiLine) != 0 ? 1 : 0;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5110-5142 — <c>getClosingLineTerminatorCount</c>.</summary>
    private int GetClosingLineTerminatorCount(Node? parentNode, Node? lastChild, ListFormat format, ITextRange? childrenTextRange)
    {
        if ((format & ListFormat.PreserveLines) != 0 || _preserveSourceNewlines == true)
        {
            if ((format & ListFormat.PreferNewLine) != 0)
            {
                return 1;
            }

            if (lastChild is null)
            {
                return parentNode is null || (_currentSourceFile is not null && RangeIsOnSingleLine(parentNode, _currentSourceFile)) ? 0 : 1;
            }
            if (_currentSourceFile is not null && parentNode is not null && !ScannerUtilities.PositionIsSynthesized(parentNode.Pos) && !Utilities.NodeIsSynthesized(lastChild) && (lastChild.Parent is null || lastChild.Parent == parentNode))
            {
                if (_preserveSourceNewlines == true)
                {
                    var end = childrenTextRange is not null && !ScannerUtilities.PositionIsSynthesized(childrenTextRange.End) ? childrenTextRange.End : lastChild.End;
                    return GetEffectiveLines(
                        includeComments =>
                            GetLinesBetweenPositionAndNextNonWhitespaceCharacter(
                                end,
                                parentNode!.End,
                                _currentSourceFile!,
                                includeComments));
                }
                return RangeEndPositionsAreOnSameLine(parentNode!, lastChild, _currentSourceFile!) ? 0 : 1;
            }
            if (SynthesizedNodeStartsOnNewLine(lastChild, format))
            {
                return 1;
            }
        }
        if ((format & ListFormat.MultiLine) != 0 && (format & ListFormat.NoTrailingNewLine) == 0)
        {
            return 1;
        }
        return 0;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5144-5166 — <c>getEffectiveLines</c>.</summary>
    private int GetEffectiveLines(Func<bool, int> getLineDifference)
    {
        // If 'preserveSourceNewlines' is disabled, we should never call this function
        // because it could be more expensive than alternative approximations.
        Debug.Assert(_preserveSourceNewlines == true);
        // We start by measuring the line difference from a position to its adjacent comments,
        // so that this is counted as a one-line difference, not two:
        //
        //   node1;
        //   // NODE2 COMMENT
        //   node2;
        var lines = getLineDifference(/*includeComments*/ true);
        if (lines == 0)
        {
            // However, if the line difference considering comments was 0, we might have this:
            //
            //   node1; // NODE2 COMMENT
            //   node2;
            //
            // in which case we should be ignoring node2's comment, so this too is counted as
            // a one-line difference, not zero.
            return getLineDifference(/*includeComments*/ false);
        }
        return lines;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5168-5174 — <c>writeLineSeparatorsAndIndentBefore</c>.</summary>
    private bool WriteLineSeparatorsAndIndentBefore(Node node, Node? parent)
    {
        var leadingNewlines = _preserveSourceNewlines == true ? GetLeadingLineTerminatorCount(parent, node, ListFormat.None) : 0;
        if (leadingNewlines != 0)
        {
            WriteLinesAndIndent(leadingNewlines, /*writeSpaceIfNotIndenting*/ false);
        }
        return leadingNewlines != 0;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5176-5181 — <c>writeLineSeparatorsAfter</c>.</summary>
    private void WriteLineSeparatorsAfter(Node node, Node? parent)
    {
        var trailingNewlines = _preserveSourceNewlines == true ? GetClosingLineTerminatorCount(parent, node, ListFormat.None, /*childrenTextRange*/ null) : 0;
        if (trailingNewlines != 0)
        {
            WriteLine(trailingNewlines);
        }
    }

    /// <summary>ported from: src/compiler/emitter.ts:5183-5194 — <c>synthesizedNodeStartsOnNewLine</c>.</summary>
    private bool SynthesizedNodeStartsOnNewLine(Node node, ListFormat format)
    {
        if (Utilities.NodeIsSynthesized(node))
        {
            var startsOnNewLine = EmitNodes.GetStartsOnNewLine(node);
            if (startsOnNewLine is null)
            {
                return (format & ListFormat.PreferNewLine) != 0;
            }

            return startsOnNewLine == true;
        }

        return (format & ListFormat.PreferNewLine) != 0;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5196-5226 — <c>getLinesBetweenNodes</c>.</summary>
    private int GetLinesBetweenNodes(Node parent, Node node1, Node node2)
    {
        if ((Utilities.GetEmitFlags(parent) & EmitFlags.NoIndentation) != 0)
        {
            return 0;
        }

        parent = SkipSynthesizedParentheses(parent);
        node1 = SkipSynthesizedParentheses(node1);
        node2 = SkipSynthesizedParentheses(node2);

        // Always use a newline for synthesized code if the synthesizer desires it.
        if (EmitNodes.GetStartsOnNewLine(node2) == true)
        {
            return 1;
        }

        if (_currentSourceFile is not null && !Utilities.NodeIsSynthesized(parent) && !Utilities.NodeIsSynthesized(node1) && !Utilities.NodeIsSynthesized(node2))
        {
            if (_preserveSourceNewlines == true)
            {
                return GetEffectiveLines(
                    includeComments =>
                        GetLinesBetweenRangeEndAndRangeStart(
                            node1,
                            node2,
                            _currentSourceFile!,
                            includeComments));
            }
            return RangeEndIsOnSameLineAsRangeStart(node1, node2, _currentSourceFile) ? 0 : 1;
        }

        return 0;
    }

    /// <summary>ported from: src/compiler/emitter.ts:5228-5231 — <c>isEmptyBlock</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>BlockLike</c> (types.ts:3366-3370: <c>SourceFile | Block |
    /// ModuleBlock | CaseOrDefaultClause</c>, every member of which has <c>statements</c>); the port has no name for the
    /// union, so the member is read through <see cref="StatementsOfBlockLike"/> — the same treatment the other inline
    /// unions get. Both call sites pass one of the union's arms (emitter.ts:3037's <c>Block</c> and :3653's
    /// <c>ModuleBlock</c>), and an absent array counts as empty, as it does in the reference's <c>length</c> test.
    /// <para>
    /// PORT-NOTE (the range test): the second conjunct is not decoration — it is what keeps an <em>empty</em> body
    /// written across two lines (<c>namespace M {</c> … <c>}</c>, or a function body the declaration transform emptied)
    /// printing as <c>{</c> newline <c>}</c> rather than <c>{ }</c>. Dropping it is what the two call sites' shared
    /// helper did before this member took the union.
    /// </para>
    /// </remarks>
    internal bool IsEmptyBlock(Node block)
        => (StatementsOfBlockLike(block)?.Count ?? 0) == 0
            && (_currentSourceFile is null || RangeEndIsOnSameLineAsRangeStart(block, block, _currentSourceFile));

    // ---------------------------------------------------------------------------------------------
    // Module-level pieces (emitter.ts:6327-6378, transformer.ts:227-234, and the utilities the ranges import)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/emitter.ts:6327-6334 — <c>createBracketsMap</c>.</summary>
    /// <remarks>PORT-NOTE: TS builds a sparse array indexed by the bracket flag (the largest key is
    /// <c>ListFormat.SquareBrackets</c>); the port allocates the array at that length.</remarks>
    private static string[]?[] CreateBracketsMap()
    {
        var brackets = new string[]?[(int)ListFormat.SquareBrackets + 1];
        brackets[(int)ListFormat.Braces] = ["{", "}"];
        brackets[(int)ListFormat.Parenthesis] = ["(", ")"];
        brackets[(int)ListFormat.AngleBrackets] = ["<", ">"];
        brackets[(int)ListFormat.SquareBrackets] = ["[", "]"];
        return brackets;
    }

    /// <summary>ported from: src/compiler/emitter.ts:6330 — the <c>brackets</c> table.</summary>
    private static readonly string[]?[] s_brackets = CreateBracketsMap();

    /// <summary>ported from: src/compiler/emitter.ts:6336-6338 — <c>getOpeningBracket</c>.</summary>
    private static string GetOpeningBracket(ListFormat format) => s_brackets[(int)(format & ListFormat.BracketsMask)]![0];

    /// <summary>ported from: src/compiler/emitter.ts:6340-6342 — <c>getClosingBracket</c>.</summary>
    private static string GetClosingBracket(ListFormat format) => s_brackets[(int)(format & ListFormat.BracketsMask)]![1];

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:1471-1473 — <c>isTokenKind</c>.</summary>
    private static bool IsTokenKind(SyntaxKind kind) => kind >= SyntaxKind.FirstToken && kind <= SyntaxKind.LastToken;

    /// <summary>ported from: src/compiler/utilities.ts:1370-1373 — <c>getInternalEmitFlags</c>.</summary>
    private static InternalEmitFlags GetInternalEmitFlags(Node node) => node.EmitNode?.InternalFlags ?? InternalEmitFlags.None;

    /// <summary>ported from: src/compiler/transformer.ts:227-229 — <c>noEmitSubstitution</c>.</summary>
    /// <remarks>PORT-NOTE: TS imports this from <c>./transformer</c> (emitter.ts:321) and uses it as the default value of
    /// the destructured <c>substituteNode</c> handler; the port states it here, and <see cref="PrintHandlers.Default"/>
    /// is the handler set that supplies it.</remarks>
    internal static Node NoEmitSubstitution(EmitHint hint, Node? node) => node!;

    /// <summary>ported from: src/compiler/transformer.ts:232-234 — <c>noEmitNotification</c>.</summary>
    internal static void NoEmitNotification(EmitHint hint, Node? node, Action<EmitHint, Node>? callback) => callback?.Invoke(hint, node!);

    /// <summary>ported from: src/compiler/utilities.ts:7828-7836 — <c>getNewLineCharacter</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>CompilerOptions | PrinterOptions</c> and reads only <c>newLine</c>; the
    /// port's <see cref="Utilities.GetNewLineCharacter"/> takes the generated <see cref="ICompilerOptions"/>, which is
    /// not a supertype of <see cref="IPrinterOptions"/>, so the printer states the same switch over its own options.
    /// </remarks>
    private static string GetNewLineCharacter(IPrinterOptions? options)
    {
        switch (options?.NewLine)
        {
            case NewLineKind.CarriageReturnLineFeed:
                return "\r\n";
            case NewLineKind.LineFeed:
            case null:
                return "\n";
            default:
                Debug.Fail("unknown newline kind");
                return "\n";
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:9048-9054 — the <c>target</c> entry of
    /// <c>createComputedCompilerOptions</c>, exported as <c>getEmitScriptTarget</c> (utilities.ts:9317).</summary>
    private static ScriptTarget GetEmitScriptTarget(IPrinterOptions? options)
    {
        var target = options?.Target == ScriptTarget.ES3 ? (ScriptTarget?)null : options?.Target;
        return target ?? ScriptTarget.LatestStandard;
    }

    /// <summary>ported from: src/compiler/utilities.ts:9293 — <c>getEmitModuleKind</c> (its <c>module</c> entry,
    /// utilities.ts:9060-9079).</summary>
    /// <remarks>PORT-NOTE: TS types <c>getEmitModuleKind</c> as <c>(compilerOptions: Pick&lt;CompilerOptions, "module" |
    /// "target"&gt;)</c> and the reference's call at emitter.ts:1231 passes the printer options; the port's
    /// <see cref="Utilities.GetEmitModuleKind"/> takes the generated <see cref="ICompilerOptions"/>, so the printer
    /// states the same computation over <see cref="IPrinterOptions"/>.</remarks>
    private static ModuleKind GetEmitModuleKind(IPrinterOptions? options)
    {
        if (options?.Module is not null)
        {
            return options.Module.Value;
        }

        var target = GetEmitScriptTarget(options);
        if (target == ScriptTarget.ESNext)
        {
            return ModuleKind.ESNext;
        }

        if (target >= ScriptTarget.ES2022)
        {
            return ModuleKind.ES2022;
        }

        if (target >= ScriptTarget.ES2020)
        {
            return ModuleKind.ES2020;
        }

        return target >= ScriptTarget.ES2015 ? ModuleKind.ES2015 : ModuleKind.CommonJS;
    }

    /// <summary>ported from: src/compiler/scanner.ts:503-505 — <c>getLineStarts</c>.</summary>
    /// <remarks>PORT-NOTE: <c>ScannerUtilities.GetLineStarts</c> takes an <c>ISourceFileLike</c>, which the port's
    /// <c>SourceFile</c> does not implement, so the memo is stated here over the source file — the shape
    /// <c>Checker.Check.Grammar.cs:1152</c>, <c>Program/Program.DiagnosticsAggregation.cs</c> and
    /// <c>Core/Tracing.cs</c> already use.</remarks>
    private static IList<int> GetLineStarts(SourceFile sourceFile)
        => sourceFile.LineMap ?? (sourceFile.LineMap = ScannerUtilities.ComputeLineStarts(sourceFile.Text ?? string.Empty));

    /// <summary>ported from: src/compiler/scanner.ts:537-546 — <c>getLinesBetweenPositions</c>.</summary>
    /// <remarks>PORT-NOTE: the port's <c>ScannerUtilities.GetLinesBetweenPositions</c> takes an <c>ISourceFileLike</c>,
    /// which the port's <c>SourceFile</c> does not implement; this is the same computation over the same memoized line
    /// map.</remarks>
    private static int GetLinesBetweenPositions(SourceFile sourceFile, int pos1, int pos2)
    {
        if (pos1 == pos2) return 0;
        var lineStarts = GetLineStarts(sourceFile);
        var lower = Math.Min(pos1, pos2);
        var isNegative = lower == pos2;
        var upper = isNegative ? pos1 : pos2;
        var lowerLine = ScannerUtilities.ComputeLineOfPosition(lineStarts, lower);
        var upperLine = ScannerUtilities.ComputeLineOfPosition(lineStarts, upper, lowerLine);
        return isNegative ? lowerLine - upperLine : upperLine - lowerLine;
    }

    /// <summary>ported from: src/compiler/utilities.ts:7915-7917 — <c>rangeIsOnSingleLine</c>.</summary>
    private static bool RangeIsOnSingleLine(ITextRange range, SourceFile sourceFile) => RangeStartIsOnSameLineAsRangeEnd(range, range, sourceFile);

    /// <summary>ported from: src/compiler/utilities.ts:7920-7926 — <c>rangeStartPositionsAreOnSameLine</c>.</summary>
    private static bool RangeStartPositionsAreOnSameLine(ITextRange range1, ITextRange range2, SourceFile sourceFile)
        => PositionsAreOnSameLine(
            GetStartPositionOfRange(range1, sourceFile, /*includeComments*/ false),
            GetStartPositionOfRange(range2, sourceFile, /*includeComments*/ false),
            sourceFile);

    /// <summary>ported from: src/compiler/utilities.ts:7929-7931 — <c>rangeEndPositionsAreOnSameLine</c>.</summary>
    private static bool RangeEndPositionsAreOnSameLine(ITextRange range1, ITextRange range2, SourceFile sourceFile)
        => PositionsAreOnSameLine(range1.End, range2.End, sourceFile);

    /// <summary>ported from: src/compiler/utilities.ts:7934-7936 — <c>rangeStartIsOnSameLineAsRangeEnd</c>.</summary>
    private static bool RangeStartIsOnSameLineAsRangeEnd(ITextRange range1, ITextRange range2, SourceFile sourceFile)
        => PositionsAreOnSameLine(GetStartPositionOfRange(range1, sourceFile, /*includeComments*/ false), range2.End, sourceFile);

    /// <summary>ported from: src/compiler/utilities.ts:7939-7941 — <c>rangeEndIsOnSameLineAsRangeStart</c>.</summary>
    private static bool RangeEndIsOnSameLineAsRangeStart(ITextRange range1, ITextRange range2, SourceFile sourceFile)
        => PositionsAreOnSameLine(range1.End, GetStartPositionOfRange(range2, sourceFile, /*includeComments*/ false), sourceFile);

    /// <summary>ported from: src/compiler/utilities.ts:7944-7947 — <c>getLinesBetweenRangeEndAndRangeStart</c>.</summary>
    private static int GetLinesBetweenRangeEndAndRangeStart(ITextRange range1, ITextRange range2, SourceFile sourceFile, bool includeSecondRangeComments)
    {
        var range2Start = GetStartPositionOfRange(range2, sourceFile, includeSecondRangeComments);
        return GetLinesBetweenPositions(sourceFile, range1.End, range2Start);
    }

    /// <summary>ported from: src/compiler/utilities.ts:7961-7963 — <c>positionsAreOnSameLine</c>.</summary>
    private static bool PositionsAreOnSameLine(int pos1, int pos2, SourceFile sourceFile)
        => GetLinesBetweenPositions(sourceFile, pos1, pos2) == 0;

    /// <summary>ported from: src/compiler/utilities.ts:7966-7968 — <c>getStartPositionOfRange</c>.</summary>
    private static int GetStartPositionOfRange(ITextRange range, SourceFile sourceFile, bool includeComments)
        => ScannerUtilities.PositionIsSynthesized(range.Pos)
            ? -1
            : ScannerUtilities.SkipTrivia(sourceFile.Text!, range.Pos, /*stopAfterLineBreak*/ false, includeComments);

    /// <summary>ported from: src/compiler/utilities.ts:7970-7975 — <c>getLinesBetweenPositionAndPrecedingNonWhitespaceCharacter</c>.</summary>
    private static int GetLinesBetweenPositionAndPrecedingNonWhitespaceCharacter(int pos, int stopPos, SourceFile sourceFile, bool includeComments)
    {
        var startPos = ScannerUtilities.SkipTrivia(sourceFile.Text!, pos, /*stopAfterLineBreak*/ false, includeComments);
        var prevPos = GetPreviousNonWhitespacePosition(startPos, stopPos, sourceFile);
        return GetLinesBetweenPositions(sourceFile, prevPos ?? stopPos, startPos);
    }

    /// <summary>ported from: src/compiler/utilities.ts:7977-7981 — <c>getLinesBetweenPositionAndNextNonWhitespaceCharacter</c>.</summary>
    private static int GetLinesBetweenPositionAndNextNonWhitespaceCharacter(int pos, int stopPos, SourceFile sourceFile, bool includeComments)
    {
        var nextPos = ScannerUtilities.SkipTrivia(sourceFile.Text!, pos, /*stopAfterLineBreak*/ false, includeComments);
        return GetLinesBetweenPositions(sourceFile, pos, Math.Min(stopPos, nextPos));
    }

    /// <summary>ported from: src/compiler/utilities.ts:7992-7999 — <c>getPreviousNonWhitespacePosition</c>.</summary>
    private static int? GetPreviousNonWhitespacePosition(int pos, int stopPos, SourceFile sourceFile)
    {
        while (pos-- > stopPos)
        {
            if (!ScannerUtilities.IsWhiteSpaceLike(sourceFile.Text![pos]))
            {
                return pos;
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/utilitiesPublic.ts:2603-2621 — <c>guessIndentation</c>.</summary>
    private static int? GuessIndentation(IList<string> lines)
    {
        // ported from: src/compiler/utilitiesPublic.ts:2602 — MAX_SMI_X86
        const int maxSmiX86 = 0x3fff_ffff;
        var indentation = maxSmiX86;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }
            var i = 0;
            for (; i < line.Length && i < indentation; i++)
            {
                if (!ScannerUtilities.IsWhiteSpaceLike(line[i]))
                {
                    break;
                }
            }
            if (i < indentation)
            {
                indentation = i;
            }
            if (indentation == 0)
            {
                return 0;
            }
        }
        return indentation == maxSmiX86 ? null : indentation;
    }
}