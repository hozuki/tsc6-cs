#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/declarations.ts (1977 lines) — the whole module. This file states the two
// exported entry points (221-238, 259-311), the closure state and the symbol tracker the factory builds, and
// `transformRoot` (460-604); the members the factory's closures state are split over
// `DeclarationsTransform.Members.cs` (606-1004), `DeclarationsTransform.Statements.cs` (1006-1863) and
// `DeclarationsTransform.Support.cs` (1865-1977 plus the module-level helpers the reference imports).
//
// PORT-NOTE (the factory's closures): TS's `transformDeclarations` is a factory whose body is a table of closures over
// its locals; the port states the factory as `DeclarationsTransform.TransformDeclarations` and the closures as the
// members of `DeclarationTransformer`, the same "closures → classes" treatment `ES2015Transform` records for
// `es2015.ts`. The reference's `symbolTracker` object literal is `DeclarationSymbolTracker`, which forwards to the same
// members (it is the one member whose bodies the reference states inline in the literal).
//
// PORT-NOTE (the union types): `LateVisibilityPaintedStatement` (utilities.ts:2297) and the
// `VariableDeclaration | PropertyDeclaration | …` unions `ensureType`, `ensureNoInitializer` and
// `transformTopLevelDeclaration` take have no C# face; the port states their members with `Node` plus the guard that
// established the union (`Utilities.IsLateVisibilityPaintedStatement` and this family's own `IsProcessedComponent`),
// the treatment `Utilities/Utilities.Checker.cs:3226` records for the first one.
/// <summary>
/// ported from: src/compiler/transformers/declarations.ts:221-311 — the declaration emitter's two entry points:
/// <c>getDeclarationDiagnostics</c>, the member <c>program.ts:3255</c> calls instead of an emitter pass, and
/// <c>transformDeclarations</c>, the transformer factory the declaration transformer list holds.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE (home, §15.15): the reference exports both as module-level functions, so they land on the class named
/// after the module; <c>getDeclarationDiagnostics</c> is what
/// <c>Program.GetDeclarationDiagnosticsFromTransform</c> (<c>Program/Program.Emit.cs:413</c>) must call — see this
/// slice's report for the exact wiring.
/// </para>
/// <para>
/// PORT-NOTE (the builder flags): the reference's two constants (<c>:241-249</c>) are read by the node-builder calls
/// this family makes; the port states them as constants of this class so the members that read them name their home.
/// </para>
/// </remarks>
internal static class DeclarationsTransform
{
    /// <summary>ported from: src/compiler/transformers/declarations.ts:241-247 — <c>declarationEmitNodeBuilderFlags</c>.</summary>
    internal const NodeBuilderFlags DeclarationEmitNodeBuilderFlags =
        NodeBuilderFlags.MultilineObjectLiterals
        | NodeBuilderFlags.WriteClassExpressionAsTypeLiteral
        | NodeBuilderFlags.UseTypeOfFunction
        | NodeBuilderFlags.UseStructuralFallback
        | NodeBuilderFlags.AllowEmptyTuple
        | NodeBuilderFlags.GenerateNamesForShadowedTypeParams
        | NodeBuilderFlags.NoTruncation;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:249 — <c>declarationEmitInternalNodeBuilderFlags</c>.</summary>
    internal const InternalNodeBuilderFlags DeclarationEmitInternalNodeBuilderFlags = InternalNodeBuilderFlags.AllowUnresolvedNames;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:221-238 — <c>getDeclarationDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's answer is <c>DiagnosticWithLocation[] | undefined</c>; the port's is the list the transform
    /// result hands out (<c>null</c> for TS's <c>undefined</c>), which is what
    /// <c>Program.GetDeclarationDiagnosticsForFileNoCache</c> stores. <c>filter(getSourceFilesToEmit(host, file),
    /// isSourceFileNotJson)</c> is <see cref="Core.Filter{T}"/> over <see cref="Utilities.GetSourceFilesToEmit"/>, and
    /// <c>contains</c> is <c>IList&lt;T&gt;.Contains</c> — the same reference-equality test a JS <c>indexOf</c> makes
    /// for nodes.
    /// </remarks>
    public static List<IDiagnosticWithLocation>? GetDeclarationDiagnostics(IEmitHost host, IEmitResolver resolver, SourceFile file)
    {
        var compilerOptions = Utilities.EmitHostCompilerOptions(host);
        var files = Core.Filter(Utilities.GetSourceFilesToEmit(host, file), (SourceFile f) => Utilities.IsSourceFileNotJson(f));
        return files is not null && files.Contains(file)
            ? Transformer.TransformNodes(
                resolver,
                host,
                NodeFactory.Factory,
                compilerOptions,
                [file],
                [TransformDeclarations],
                /*allowDtsFiles*/ false).Diagnostics
            : null;
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:259-311 — <c>transformDeclarations</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>Transformer&lt;SourceFile | Bundle&gt;</c> is the port's erased
    /// <c>Func&lt;object?, object?&gt;</c> (Transformer.cs's file header); the returned member is the reference's own
    /// <c>return transformRoot</c>, staying a method group so no delegate is allocated per call.</remarks>
    public static Func<object?, object?> TransformDeclarations(ITransformerContext context)
        => new DeclarationTransformer(context).TransformSourceFileOrBundle;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:260 — <c>throwDiagnostic</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>Debug.fail</c> is <c>never</c>; the port's <see cref="Debug.Fail{T}"/> is the same
    /// throw in expression position (Core/Debug.cs:113-118).</remarks>
    internal static DeclarationsDiagnostics.SymbolAccessibilityDiagnostic? ThrowDiagnostic()
        => Debug.Fail<DeclarationsDiagnostics.SymbolAccessibilityDiagnostic?>("Diagnostic emitted without context");
}

/// <summary>ported from: src/compiler/transformers/declarations.ts:259-1863 — the body of <c>transformDeclarations</c>.</summary>
/// <remarks>
/// <para>
/// PORT-NOTE (the state): the reference's locals — <c>getSymbolAccessibilityDiagnostic</c>, <c>needsDeclare</c>,
/// <c>isBundledEmit</c>, <c>resultHasExternalModuleIndicator</c>, <c>needsScopeFixMarker</c>,
/// <c>resultHasScopeMarker</c>, <c>enclosingDeclaration</c>, <c>lateMarkedStatements</c>,
/// <c>lateStatementReplacementMap</c>, <c>suppressNewDiagnosticContexts</c>, <c>errorNameNode</c>,
/// <c>errorFallbackNode</c>, <c>currentSourceFile</c>, <c>rawReferencedFiles</c>, <c>rawTypeReferenceDirectives</c>,
/// <c>rawLibReferenceDirectives</c> and <c>restoreFallbackNode</c> — are this class's fields. The reference's
/// <c>Map&lt;NodeId, VisitResult&lt;…&gt;&gt;</c> is the port's <c>Dictionary&lt;int, object?&gt;</c> (<c>NodeId</c> is
/// what <c>getOriginalNodeId</c> answers, <c>int</c> port-wide), and <c>VisitResult&lt;T&gt;</c> — which is
/// <c>T | readonly Node[] | undefined</c> — is <c>object?</c>, discriminated the way the visitors themselves
/// discriminate it (a <c>Node</c>, a node list, or <c>null</c>).
/// </para>
/// <para>
/// PORT-NOTE (<c>factory</c>, <c>host</c> and the resolver): the reference destructures <c>factory</c> out of the
/// context and calls <c>context.getEmitHost()</c>/<c>context.getEmitResolver()</c>/<c>context.getCompilerOptions()</c>;
/// the port keeps the context and reads the same members through it (their bodies are
/// <c>TransformationContext&lt;T&gt;</c>'s, Transformer.cs). <c>transformRoot</c>'s parameter is <c>this</c> class's
/// <c>_rootNode</c> field, because <c>getReferencedFiles</c> reads it after the walk.
/// </para>
/// </remarks>
internal sealed partial class DeclarationTransformer
{
    private readonly ITransformerContext _context;
    private readonly NodeFactory _factory;
    private readonly IEmitHost _host;
    private readonly IEmitResolver _resolver;
    private readonly Func<Node, IDiagnosticWithLocation> _getIsolatedDeclarationError;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:309 — <c>stripInternal</c>.</summary>
    private readonly bool _stripInternal;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:309 — <c>isolatedDeclarations</c>.</summary>
    private readonly bool _isolatedDeclarations;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:261-262 — <c>getSymbolAccessibilityDiagnostic</c>.</summary>
    private DeclarationsDiagnostics.GetSymbolAccessibilityDiagnostic _getSymbolAccessibilityDiagnostic;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:263 — <c>needsDeclare</c>.</summary>
    private bool _needsDeclare = true;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:264 — <c>isBundledEmit</c>.</summary>
    private bool _isBundledEmit;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:265 — <c>resultHasExternalModuleIndicator</c>.</summary>
    private bool _resultHasExternalModuleIndicator;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:266 — <c>needsScopeFixMarker</c>.</summary>
    private bool _needsScopeFixMarker;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:267 — <c>resultHasScopeMarker</c>.</summary>
    private bool _resultHasScopeMarker;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:268 — <c>enclosingDeclaration</c>.</summary>
    private Node? _enclosingDeclaration;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:269 — <c>lateMarkedStatements</c>.</summary>
    private List<Node>? _lateMarkedStatements;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:270 — <c>lateStatementReplacementMap</c>.</summary>
    private Dictionary<int, object?>? _lateStatementReplacementMap;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:271 — <c>suppressNewDiagnosticContexts</c>.</summary>
    private bool _suppressNewDiagnosticContexts;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:468 — <c>restoreFallbackNode</c>.</summary>
    private Action _restoreFallbackNode = () => { };

    /// <summary>ported from: src/compiler/transformers/declarations.ts:287 — <c>errorNameNode</c>.</summary>
    /// <remarks>PORT-NOTE: TS's type is <c>DeclarationName</c>, whose members are all nodes in this model, so the slot
    /// is <see cref="Node"/>. It is written by <c>ensureType</c>, <c>transformTopLevelDeclaration</c> and
    /// <c>checkName</c>, which is why it is a field and not a member parameter.</remarks>
    private Node? _errorNameNode;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:288 — <c>errorFallbackNode</c>.</summary>
    private Node? _errorFallbackNode;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:290 — <c>currentSourceFile</c>.</summary>
    private SourceFile _currentSourceFile = null!;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:291 — <c>rawReferencedFiles</c>.</summary>
    private List<(SourceFile File, IFileReference Reference)> _rawReferencedFiles = [];

    /// <summary>ported from: src/compiler/transformers/declarations.ts:292 — <c>rawTypeReferenceDirectives</c>.</summary>
    private List<IFileReference> _rawTypeReferenceDirectives = [];

    /// <summary>ported from: src/compiler/transformers/declarations.ts:293 — <c>rawLibReferenceDirectives</c>.</summary>
    private List<IFileReference> _rawLibReferenceDirectives = [];

    /// <summary>ported from: src/compiler/transformers/declarations.ts:460 — the <c>node</c> <c>transformRoot</c> holds.</summary>
    private Node? _rootNode;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:294-308 — <c>symbolTracker</c>.</summary>
    private readonly ISymbolTracker _symbolTracker;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:259-311 — the factory's prologue.</summary>
    public DeclarationTransformer(ITransformerContext context)
    {
        _context = context;
        _factory = context.Factory;
        _host = context.GetEmitHost();
        _resolver = context.GetEmitResolver()!;
        var options = context.GetCompilerOptions();
        _getIsolatedDeclarationError = DeclarationsDiagnostics.CreateGetIsolatedDeclarationErrors(_resolver);
        _stripInternal = Js.IsTruthy(options.StripInternal);
        _isolatedDeclarations = Js.IsTruthy(options.IsolatedDeclarations);
        _getSymbolAccessibilityDiagnostic = _ => DeclarationsTransform.ThrowDiagnostic();
        _symbolTracker = new DeclarationSymbolTracker(this);
    }

    /// <summary>The <c>host</c> the factory's <c>symbolTracker.moduleResolverHost</c> names (declarations.ts:303).</summary>
    internal IEmitHost Host => _host;

    /// <summary>ported from: src/compiler/transformers/declarations.ts:311 — the returned <c>transformRoot</c>.</summary>
    /// <remarks>PORT-NOTE: the reference hands <c>transformRoot</c> straight to <c>transformer.ts</c>'s declaration
    /// transformer list, whose wrapper is the identity (<c>transformer.ts:222-224</c>); the port's erased slot is
    /// <c>Func&lt;object?, object?&gt;</c>, so the two arms of the reference's own first <c>switch</c> are stated here
    /// and <c>TransformRoot</c> takes the node.</remarks>
    public object? TransformSourceFileOrBundle(object? node)
        => NodeTests.IsBundle((Node)node!)
            ? TransformRoot((Bundle)node!)
            : TransformRoot((SourceFile)node!);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:460-604 — <c>transformRoot</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference returns <c>SourceFile | Bundle</c>; the port's erased transformer face is
    /// <c>object?</c>, so the same two values come back boxed. Its nested <c>collectFileReferences</c>,
    /// <c>copyFileReferenceAsSynthetic</c>, <c>getTypeReferences</c>, <c>getLibReferences</c> and
    /// <c>getReferencedFiles</c> are private members of this class because the bundle arm and the single-file arm both
    /// call them.
    /// </remarks>
    public object? TransformRoot(Node node)
    {
        _rootNode = node;
        if (node.Kind == SyntaxKind.SourceFile && ((SourceFile)node).IsDeclarationFile)
        {
            return node;
        }

        if (node.Kind == SyntaxKind.Bundle)
        {
            _isBundledEmit = true;
            _rawReferencedFiles = [];
            _rawTypeReferenceDirectives = [];
            _rawLibReferenceDirectives = [];
            var bundle = (Bundle)node;
            var newBundle = _factory.CreateBundle(Core.Map(bundle.SourceFiles!, (SourceFile sourceFile, int _) =>
            {
                if (sourceFile.IsDeclarationFile) return null!; // Omit declaration files from bundle results, too // TODO: GH#18217
                _currentSourceFile = sourceFile;
                _enclosingDeclaration = sourceFile;
                _lateMarkedStatements = null;
                _suppressNewDiagnosticContexts = false;
                _lateStatementReplacementMap = [];
                _getSymbolAccessibilityDiagnostic = _ => DeclarationsTransform.ThrowDiagnostic();
                _needsScopeFixMarker = false;
                _resultHasScopeMarker = false;
                CollectFileReferences(sourceFile);
                if (Utilities.IsExternalOrCommonJsModule(sourceFile) || Utilities.IsJsonSourceFile(sourceFile))
                {
                    _resultHasExternalModuleIndicator = false; // unused in external module bundle emit (all external modules are within module blocks, therefore are known to be modules)
                    _needsDeclare = false;
                    var statements = Utilities.IsSourceFileJS(sourceFile)
                        ? _factory.CreateNodeArray(TransformDeclarationsForJS(sourceFile), null)
                        : Utilities.VisitNodes(sourceFile.Statements, VisitDeclarationStatements, Utilities.IsStatement)!;
                    var newFile = _factory.UpdateSourceFile(
                        sourceFile,
                        [
                            _factory.CreateModuleDeclaration(
                                [_factory.CreateModifier(SyntaxKind.DeclareKeyword)],
                                (MemberExpression)_factory.CreateStringLiteral(
                                    DeclarationsTransformSupport.GetResolvedExternalModuleName(_host, sourceFile)),
                                _factory.CreateModuleBlock(Utilities.SetTextRange(
                                    _factory.CreateNodeArray(TransformAndReplaceLatePaintedStatements(statements), null),
                                    sourceFile.Statements)),
                                /*flags*/ null),
                        ],
                        /*isDeclarationFile*/ true,
                        /*referencedFiles*/ null,
                        /*typeReferenceDirectives*/ null,
                        /*hasNoDefaultLib*/ false,
                        /*libReferenceDirectives*/ null);
                    return newFile;
                }
                _needsDeclare = true;
                var updated = Utilities.IsSourceFileJS(sourceFile)
                    ? _factory.CreateNodeArray(TransformDeclarationsForJS(sourceFile), null)
                    : Utilities.VisitNodes(sourceFile.Statements, VisitDeclarationStatements, Utilities.IsStatement)!;
                return _factory.UpdateSourceFile(
                    sourceFile,
                    TransformAndReplaceLatePaintedStatements(updated),
                    /*isDeclarationFile*/ true,
                    /*referencedFiles*/ null,
                    /*typeReferenceDirectives*/ null,
                    /*hasNoDefaultLib*/ false,
                    /*libReferenceDirectives*/ null);
            })!);
            var outputFilePath = Paths.GetDirectoryPath(Paths.NormalizeSlashes(
                Emitter.GetOutputPathsFor(node, _host, /*forceDtsPaths*/ true).DeclarationFilePath!));
            newBundle.SyntheticFileReferences = GetReferencedFiles(outputFilePath);
            newBundle.SyntheticTypeReferences = GetTypeReferences();
            newBundle.SyntheticLibReferences = GetLibReferences();
            return newBundle;
        }

        // Single source file
        var sourceFile0 = (SourceFile)node;
        _needsDeclare = true;
        _needsScopeFixMarker = false;
        _resultHasScopeMarker = false;
        _enclosingDeclaration = node;
        _currentSourceFile = sourceFile0;
        _getSymbolAccessibilityDiagnostic = _ => DeclarationsTransform.ThrowDiagnostic();
        _isBundledEmit = false;
        _resultHasExternalModuleIndicator = false;
        _suppressNewDiagnosticContexts = false;
        _lateMarkedStatements = null;
        _lateStatementReplacementMap = [];
        _rawReferencedFiles = [];
        _rawTypeReferenceDirectives = [];
        _rawLibReferenceDirectives = [];
        CollectFileReferences(_currentSourceFile);
        NodeArray<IStatement> combinedStatements;
        if (Utilities.IsSourceFileJS(_currentSourceFile))
        {
            combinedStatements = _factory.CreateNodeArray(TransformDeclarationsForJS((SourceFile)node), null);
        }
        else
        {
            var statements = Utilities.VisitNodes(sourceFile0.Statements, VisitDeclarationStatements, Utilities.IsStatement)!;
            combinedStatements = Utilities.SetTextRange(
                _factory.CreateNodeArray(TransformAndReplaceLatePaintedStatements(statements), null),
                sourceFile0.Statements);
            if (Parser.IsExternalModule(sourceFile0) && (!_resultHasExternalModuleIndicator || (_needsScopeFixMarker && !_resultHasScopeMarker)))
            {
                combinedStatements = Utilities.SetTextRange(
                    _factory.CreateNodeArray([.. combinedStatements, FactoryUtilities.CreateEmptyExports(_factory)], null),
                    combinedStatements);
            }
        }
        var outputFilePath0 = Paths.GetDirectoryPath(Paths.NormalizeSlashes(
            Emitter.GetOutputPathsFor(node, _host, /*forceDtsPaths*/ true).DeclarationFilePath!));
        return _factory.UpdateSourceFile(
            sourceFile0,
            combinedStatements,
            /*isDeclarationFile*/ true,
            GetReferencedFiles(outputFilePath0),
            GetTypeReferences(),
            /*hasNoDefaultLib*/ false,
            GetLibReferences());
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:527-531 — <c>collectFileReferences</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>concatenate</c> answers a fresh array when its left side is non-empty;
    /// the port pushes onto the fields, which is the same order and the same list contents.</remarks>
    private void CollectFileReferences(SourceFile sourceFile)
    {
        foreach (var reference in sourceFile.ReferencedFiles!)
        {
            _rawReferencedFiles.Add((sourceFile, reference));
        }
        _rawTypeReferenceDirectives.AddRange(sourceFile.TypeReferenceDirectives!);
        _rawLibReferenceDirectives.AddRange(sourceFile.LibReferenceDirectives!);
    }

    /// <summary>ported from: src/compiler/transformers/declarations.ts:533-539 — <c>copyFileReferenceAsSynthetic</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>{ ...ref }</c> copies whatever else the object carries;
    /// <see cref="FileReference"/> declares the four slots the reference writes, so the copy states them.</remarks>
    private static IFileReference CopyFileReferenceAsSynthetic(IFileReference @ref) => new FileReference(@ref.FileName!, -1, -1)
    {
        ResolutionMode = @ref.ResolutionMode,
        Preserve = @ref.Preserve,
    };

    /// <summary>ported from: src/compiler/transformers/declarations.ts:541-547 — <c>getTypeReferences</c>.</summary>
    private List<IFileReference> GetTypeReferences()
        => Core.MapDefined(_rawTypeReferenceDirectives, (IFileReference @ref, int _) => Js.IsTruthy(@ref.Preserve) ? CopyFileReferenceAsSynthetic(@ref) : null);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:549-555 — <c>getLibReferences</c>.</summary>
    private List<IFileReference> GetLibReferences()
        => Core.MapDefined(_rawLibReferenceDirectives, (IFileReference @ref, int _) => Js.IsTruthy(@ref.Preserve) ? CopyFileReferenceAsSynthetic(@ref) : null);

    /// <summary>ported from: src/compiler/transformers/declarations.ts:557-594 — <c>getReferencedFiles</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's bare <c>return</c> inside the <c>mapDefined</c> callback (for a source that
    /// is being merged into the bundle) answers <c>undefined</c> — the same "omit this element" the port states as
    /// <c>null</c>.</remarks>
    private List<IFileReference> GetReferencedFiles(string outputFilePath)
        => Core.MapDefined(_rawReferencedFiles, ((SourceFile File, IFileReference Reference) entry, int _) =>
        {
            var (sourceFile, @ref) = entry;
            if (!Js.IsTruthy(@ref.Preserve)) return null;

            var file = _host.GetSourceFileFromReference!(sourceFile, @ref);
            if (file is null)
            {
                return null;
            }

            string? declFileName;
            if (file.IsDeclarationFile)
            {
                // Neither decl files or js should have their refs changed
                declFileName = file.FileName;
            }
            else
            {
                if (_isBundledEmit && ((Bundle)_rootNode!).SourceFiles!.Contains(file)) return null; // Omit references to files which are being merged
                var paths = Emitter.GetOutputPathsFor(file, _host, /*forceDtsPaths*/ true);
                declFileName = paths.DeclarationFilePath ?? paths.JsFilePath ?? file.FileName;
            }

            if (declFileName is null) return null;

            var fileName = Paths.GetRelativePathToDirectoryOrUrl(
                outputFilePath,
                declFileName,
                Utilities.EmitHostCurrentDirectory(_host),
                (Core.GetCanonicalFileName)_host.GetCanonicalFileName!,
                /*isAbsolutePathAnUrl*/ false);

            var newRef = CopyFileReferenceAsSynthetic(@ref);
            newRef.FileName = fileName;
            return newRef;
        });

    // ported from: src/compiler/transformers/declarations.ts:294-308 — the `symbolTracker` object literal.
    /// <summary>
    /// The tracker the declaration transformer hands every node-builder call: TS's object literal whose members are the
    /// factory's own closures (<c>trackSymbol</c>, the six <c>report*</c> callbacks, <c>moduleResolverHost</c>) plus the
    /// two <c>errorFallbackNode</c> stack members the literal states inline.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: the two inline members (<c>pushErrorFallbackNode</c>/<c>popErrorFallbackNode</c>) save and restore
    /// <c>errorFallbackNode</c> through the factory's own <c>restoreFallbackNode</c> slot, so this class holds the
    /// transformer rather than a copy of its state — the "closures → classes" treatment the file header records. A nested
    /// class reaches the enclosing instance's private members, which is what makes the field writes the same ones.
    /// </remarks>
    internal sealed class DeclarationSymbolTracker(DeclarationTransformer transformer) : ISymbolTracker
    {
        /// <summary>ported from: src/compiler/transformers/declarations.ts:369-373 — <c>trackSymbol</c>.</summary>
        public bool TrackSymbol(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning)
            => transformer.TrackSymbol(symbol!, enclosingDeclaration, meaning);

        /// <summary>ported from: src/compiler/transformers/declarations.ts:405-409 — <c>reportInaccessibleThisError</c>.</summary>
        public void ReportInaccessibleThisError() => transformer.ReportInaccessibleThisError();

        /// <summary>ported from: src/compiler/transformers/declarations.ts:375-384 — <c>reportPrivateInBaseOfClassExpression</c>.</summary>
        public void ReportPrivateInBaseOfClassExpression(string? propertyName)
            => transformer.ReportPrivateInBaseOfClassExpression(propertyName!);

        /// <summary>ported from: src/compiler/transformers/declarations.ts:393-397 — <c>reportInaccessibleUniqueSymbolError</c>.</summary>
        public void ReportInaccessibleUniqueSymbolError() => transformer.ReportInaccessibleUniqueSymbolError();

        /// <summary>ported from: src/compiler/transformers/declarations.ts:399-403 — <c>reportCyclicStructureError</c>.</summary>
        public void ReportCyclicStructureError() => transformer.ReportCyclicStructureError();

        /// <summary>ported from: src/compiler/transformers/declarations.ts:411-420 — <c>reportLikelyUnsafeImportRequiredError</c>.</summary>
        public void ReportLikelyUnsafeImportRequiredError(string? specifier, string? symbolName)
            => transformer.ReportLikelyUnsafeImportRequiredError(specifier!, symbolName);

        /// <summary>ported from: src/compiler/transformers/declarations.ts:422-426 — <c>reportTruncationError</c>.</summary>
        public void ReportTruncationError() => transformer.ReportTruncationError();

        /// <summary>ported from: src/compiler/transformers/declarations.ts:303 — <c>moduleResolverHost: host</c>.</summary>
        public IModuleSpecifierResolutionHost? ModuleResolverHost { get; set; } = transformer.Host;

        /// <summary>ported from: src/compiler/transformers/declarations.ts:428-439 — <c>reportNonlocalAugmentation</c>.</summary>
        public void ReportNonlocalAugmentation(SourceFile? containingFile, ISymbol? parentSymbol, ISymbol? augmentingSymbol)
            => transformer.ReportNonlocalAugmentation(containingFile!, parentSymbol!, augmentingSymbol!);

        /// <summary>ported from: src/compiler/transformers/declarations.ts:441-445 — <c>reportNonSerializableProperty</c>.</summary>
        public void ReportNonSerializableProperty(string? propertyName)
            => transformer.ReportNonSerializableProperty(propertyName!);

        /// <summary>ported from: src/compiler/transformers/declarations.ts:327-335 — <c>reportInferenceFallback</c>.</summary>
        public void ReportInferenceFallback(Node? node) => transformer.ReportInferenceFallback(node!);

        /// <summary>ported from: src/compiler/transformers/declarations.ts:304-311 — <c>pushErrorFallbackNode</c>.</summary>
        public void PushErrorFallbackNode(IDeclaration? node)
        {
            var currentFallback = transformer._errorFallbackNode;
            var currentRestore = transformer._restoreFallbackNode;
            transformer._restoreFallbackNode = () =>
            {
                transformer._restoreFallbackNode = currentRestore;
                transformer._errorFallbackNode = currentFallback;
            };
            transformer._errorFallbackNode = (Node?)node;
        }

        /// <summary>ported from: src/compiler/transformers/declarations.ts:312-314 — <c>popErrorFallbackNode</c>.</summary>
        public void PopErrorFallbackNode() => transformer._restoreFallbackNode();
    }
}
