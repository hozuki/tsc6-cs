namespace TypeScript.Compiler;

// ported from: src/compiler/emitter.ts:1147-1185 (notImplementedResolver), src/compiler/types.ts:3072-3075
// (SourceMapEmitResult), src/compiler/types.ts:4930-4934 (BuildInfo), src/compiler/types.ts:4596-4606
// (WriteFileCallbackData) and src/compiler/utilities.ts:6614-6620 (EmitFileNames).
//
// PORT-NOTE: TS fills each of these shapes in with an object literal (and `notImplementedResolver` is the emitter's
// own literal), so the port needs a constructible type for each — the same treatment <see cref="EmitResult"/> got in
// <c>Program.Host.cs</c>.
internal static partial class Emitter
{
    /// <summary>ported from: src/compiler/types.ts:3072-3075 — <c>SourceMapEmitResult</c>.</summary>
    internal sealed class SourceMapEmitResult : ISourceMapEmitResult
    {
        public List<string>? InputSourceFileNames { get; set; }

        public IRawSourceMap? SourceMap { get; set; }
    }

    /// <summary>ported from: src/compiler/types.ts:4930-4934 — <c>BuildInfo</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>BuildInfo</c> is a <c>Map</c>-like object with an index signature plus <c>version</c>; the
    /// generated interface keeps <c>Version</c> only (the index signature has no C# counterpart, so <c>tools/Gen</c>
    /// drops it), and the port's builder (M8) carries the rest of the state. This class is what the emitter's
    /// <c>host.getBuildInfo() || { version }</c> fallback needs, and what <c>readJsonOrUndefined</c> is cast to in
    /// <see cref="Emitter.GetBuildInfo(string, string)"/>.
    /// </remarks>
    internal sealed class BuildInfo : IBuildInfo
    {
        public string? Version { get; set; }
    }

    /// <summary>ported from: src/compiler/types.ts:4596-4606 — <c>WriteFileCallbackData</c>.</summary>
    internal sealed class WriteFileCallbackData : IWriteFileCallbackData
    {
        public int? SourceMapUrlPos { get; set; }

        public IBuildInfo? BuildInfo { get; set; }

        public List<IDiagnosticWithLocation>? Diagnostics { get; set; }

        public bool? DiffersOnlyInMap { get; set; }

        public bool? SkippedDtsWrite { get; set; }
    }

    /// <summary>ported from: src/compiler/utilities.ts:6614-6620 — <c>EmitFileNames</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the interface is declared in <c>Program/Program.Model.cs</c> (M6's boundary needed it before the
    /// emitter landed, and that file's own note records that M7 must reuse it); this is the constructible face the
    /// reference's <c>{ jsFilePath, sourceMapFilePath, declarationFilePath, declarationMapPath }</c> and
    /// <c>{ buildInfoPath }</c> literals need.
    /// </remarks>
    internal sealed class EmitFileNames : IEmitFileNames
    {
        public string? JsFilePath { get; set; }

        public string? SourceMapFilePath { get; set; }

        public string? DeclarationFilePath { get; set; }

        public string? DeclarationMapPath { get; set; }

        public string? BuildInfoPath { get; set; }
    }

    /// <summary>ported from: src/compiler/emitter.ts:1147-1185 — <c>notImplementedResolver</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's literal is <c>notImplemented</c> in every slot but <c>isLateBound</c>, which answers
    /// <c>false</c> (<c>(_node): _node is LateBoundDeclaration =&gt; false</c>) — the guard the declaration transformer
    /// reads, and the one member whose answer is a value rather than a failure. <c>notImplemented</c> is
    /// <see cref="Core.NotImplemented"/>, which is <c>void</c>-typed, so the value-returning slots repeat its body
    /// (<c>throw new NotImplementedException("Not implemented")</c>) through <see cref="NotImplemented{T}"/>. The
    /// resolver is used by <c>emitBuildInfo</c>, which never transforms anything.
    /// </remarks>
    private sealed class NotImplementedEmitResolver : IEmitResolver
    {
        private static T NotImplemented<T>() => throw new NotImplementedException("Not implemented");

        public bool HasGlobalName(string? name) => NotImplemented<bool>();

        public Node? GetReferencedExportContainer(Identifier? node, bool? prefixLocals) => NotImplemented<Node?>();

        public IDeclaration? GetReferencedImportDeclaration(Identifier? node) => NotImplemented<IDeclaration?>();

        public IDeclaration? GetReferencedDeclarationWithCollidingName(Identifier? node) => NotImplemented<IDeclaration?>();

        public bool IsDeclarationWithCollidingName(IDeclaration? node) => NotImplemented<bool>();

        public bool IsValueAliasDeclaration(Node? node) => NotImplemented<bool>();

        public bool IsReferencedAliasDeclaration(Node? node, bool? checkChildren) => NotImplemented<bool>();

        public bool IsTopLevelValueImportEqualsWithEntityName(ImportEqualsDeclaration? node) => NotImplemented<bool>();

        public bool HasNodeCheckFlag(Node? node, object? flags) => NotImplemented<bool>();

        public bool IsDeclarationVisible(Node? node) => NotImplemented<bool>();

        /// <summary>ported from: src/compiler/emitter.ts:1164 — <c>isLateBound: (_node) =&gt; false</c>.</summary>
        public object? IsLateBound(IDeclaration? node) => false;

        public List<Node>? CollectLinkedAliases(MemberExpression? node, bool? setVisibility) => NotImplemented<List<Node>?>();

        /// <remarks>PORT-NOTE: <c>notImplemented</c> answers <c>never</c>; this is the one slot with no value to return.</remarks>
        public void MarkLinkedReferences(Node? node) => throw new NotImplementedException("Not implemented");

        public bool? IsImplementationOfOverload(Node? node) => NotImplemented<bool?>();

        public bool RequiresAddingImplicitUndefined(ParameterDeclaration? node, Node? enclosingDeclaration) => NotImplemented<bool>();

        public bool IsExpandoFunctionDeclaration(Node? node) => NotImplemented<bool>();

        public List<ISymbol> GetPropertiesOfContainerFunction(IDeclaration? node) => NotImplemented<List<ISymbol>>();

        public ITypeNode? CreateTypeOfDeclaration(object? declaration, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker) => NotImplemented<ITypeNode?>();

        public ITypeNode? CreateReturnTypeOfSignatureDeclaration(Node? signatureDeclaration, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker) => NotImplemented<ITypeNode?>();

        public ITypeNode? CreateTypeOfExpression(Expression? expr, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker) => NotImplemented<ITypeNode?>();

        public Expression CreateLiteralConstValue(Node? node, ISymbolTracker? tracker) => NotImplemented<Expression>();

        public ISymbolAccessibilityResult IsSymbolAccessible(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags? meaning, bool shouldComputeAliasToMarkVisible) => NotImplemented<ISymbolAccessibilityResult>();

        public ISymbolVisibilityResult IsEntityNameVisible(Node? entityName, Node? enclosingDeclaration) => NotImplemented<ISymbolVisibilityResult>();

        public object? GetConstantValue(Node? node) => NotImplemented<object?>();

        public IEvaluatorResult<object?>? GetEnumMemberValue(EnumMember? node) => NotImplemented<IEvaluatorResult<object?>?>();

        public IDeclaration? GetReferencedValueDeclaration(Identifier? reference) => NotImplemented<IDeclaration?>();

        public List<IDeclaration>? GetReferencedValueDeclarations(Identifier? reference) => NotImplemented<List<IDeclaration>?>();

        public TypeReferenceSerializationKind GetTypeReferenceSerializationKind(Node? typeName, Node? location) => NotImplemented<TypeReferenceSerializationKind>();

        public bool IsOptionalParameter(ParameterDeclaration? node) => NotImplemented<bool>();

        public bool IsArgumentsLocalBinding(Identifier? node) => NotImplemented<bool>();

        public SourceFile? GetExternalModuleFileFromDeclaration(Node? declaration) => NotImplemented<SourceFile?>();

        public bool IsLiteralConstDeclaration(Node? node) => NotImplemented<bool>();

        public Node? GetJsxFactoryEntity(Node? location) => NotImplemented<Node?>();

        public Node? GetJsxFragmentFactoryEntity(Node? location) => NotImplemented<Node?>();

        public bool IsBindingCapturedByNode(Node? node, Node? decl) => NotImplemented<bool>();

        public List<IStatement>? GetDeclarationStatementsForSourceFile(SourceFile? node, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker) => NotImplemented<List<IStatement>?>();

        public bool IsImportRequiredByAugmentation(ImportDeclaration? decl) => NotImplemented<bool>();

        public bool IsDefinitelyReferenceToGlobalSymbolObject(Node? node) => NotImplemented<bool>();

        public List<Node>? CreateLateBoundIndexSignatures(Node? cls, Node? enclosingDeclaration, NodeBuilderFlags flags, InternalNodeBuilderFlags internalFlags, ISymbolTracker? tracker) => NotImplemented<List<Node>?>();

        public List<IDeclaration> SymbolToDeclarations(ISymbol? symbol, SymbolFlags meaning, NodeBuilderFlags flags, int? maximumLength, int? verbosityLevel, IWriterContextOut? @out) => NotImplemented<List<IDeclaration>>();
    }
}
