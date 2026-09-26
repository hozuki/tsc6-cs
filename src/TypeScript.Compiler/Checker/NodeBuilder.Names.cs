using System.Text.RegularExpressions;

namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:8438-9345 — the name-resolution half of createNodeBuilder
/// <summary>
/// The node builder's "turn a symbol into a name" half: the symbol chain a name is written through, the specifier of a
/// module symbol, and the declaration-type serializers that choose between an existing annotation and the checker's type.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: TS declares everything here inside the single <c>createNodeBuilder</c> closure (checker.ts:6309-11167);
/// the port splits that closure across partial files of <see cref="Checker"/>. Every nested TS function of the range
/// whose body closes over no frame local becomes one <c>internal</c> member here (contract §5); the six that do close
/// over the frame stay local functions — <c>getSymbolChain</c> and its <c>sortByBestName</c> (in
/// <see cref="LookupSymbolChainWorker"/>), <c>createAccessFromSymbolChain</c> (in <see cref="SymbolToTypeNode"/>),
/// <c>createEntityNameFromSymbolChain</c> (in <see cref="SymbolToName"/>), <c>createExpressionFromSymbolChain</c> (in
/// <see cref="SymbolToExpression"/>) and <c>attachSymbolToLeftmostIdentifier</c> (in
/// <see cref="TrackExistingEntityName{T}"/>).
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>EntityName</c> (<c>Identifier | QualifiedName</c>) has no C# type — those two share no base —
/// so the port widens it to <see cref="Node"/>, the same treatment every other <c>EntityName</c> slot in the model gets
/// (docs/architecture.md §12.5), and the two members that answer one are <see cref="SymbolToName"/> and
/// <see cref="SymbolToEntityNameNode"/>.
/// </para>
/// <para>
/// PORT-NOTE: the members this file calls but does not declare are the usual cross-slice references of a red tree: the
/// access/visibility half of the checker (<c>GetAccessibleSymbolChain</c> 5737, <c>GetContainersOfSymbol</c> 5378,
/// <c>NeedsQualification</c> 5841, <c>GetQualifiedLeftMeaning</c> 5732, <c>GetAliasForSymbolInContainer</c> 5472,
/// <c>GetSymbolIfSameReference</c> 5498, <c>GetFileSymbolIfFileSymbolExportEqualsContainer</c> 5466,
/// <c>GetMeaningOfEntityNameReference</c> 6113), the utility half (<c>Utilities.*</c>) and the members the other
/// <c>NodeBuilder.*</c> slices own (<c>TypeParameterToDeclaration</c>, <c>MapToTypeNodes</c>,
/// <c>TypeToTypeNodeHelper</c>, <c>TypePredicateToTypePredicateNodeHelper</c>, <c>GetClonedHashPrivateName</c>,
/// <c>HasNonGlobalAugmentationExternalModuleSymbol</c>, <c>IsSymbolAccessible</c>, <c>GetNameOfSymbolAsWritten</c>).
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:8438-8454 — <c>trackComputedName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's first parameter is the <c>EntityNameOrEntityNameExpression</c> union, which the port widens to
    /// <see cref="Node"/> (§12.5); <c>context.tracker.canTrackSymbol</c> is <see cref="SymbolTrackerImpl.CanTrackSymbol"/>
    /// and the tracker slot is nullable here, so the guard tests the tracker too (§3).
    /// </remarks>
    internal void TrackComputedName(Node accessExpression, Node? enclosingDeclaration, NodeBuilderContext context)
    {
        if (context.Tracker is null || !context.Tracker.CanTrackSymbol)
        {
            return;
        }
        // get symbol of the first identifier of the entityName
        var firstIdentifier = Utilities.GetFirstIdentifier(accessExpression);
        var name = ResolveName(enclosingDeclaration, firstIdentifier.EscapedText!, SymbolFlags.Value | SymbolFlags.ExportValue, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
        if (name is not null)
        {
            context.Tracker.TrackSymbol(name, enclosingDeclaration, SymbolFlags.Value);
        }
        else
        {
            // Name does not resolve at target location, track symbol at dest location (should be inaccessible)
            var fallback = ResolveName(firstIdentifier, firstIdentifier.EscapedText!, SymbolFlags.Value | SymbolFlags.ExportValue, /*nameNotFoundMessage*/ null, /*isUse*/ true, /*excludeGlobals*/ false);
            if (fallback is not null)
            {
                context.Tracker.TrackSymbol(fallback, enclosingDeclaration, SymbolFlags.Value);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:8455-8459 — <c>lookupSymbolChain</c>.</summary>
    internal List<ISymbol> LookupSymbolChain(ISymbol symbol, NodeBuilderContext context, SymbolFlags meaning, bool yieldModuleSymbol = false)
    {
        context.Tracker!.TrackSymbol(symbol, context.EnclosingDeclaration, meaning);
        return LookupSymbolChainWorker(symbol, context, meaning, yieldModuleSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:8460-8547 — <c>lookupSymbolChainWorker</c> (with <c>getSymbolChain</c> 8474-8546 and its nested <c>sortByBestName</c> 8527-8545).</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>getSymbolChain</c> and <c>sortByBestName</c> read <c>context</c>, <c>yieldModuleSymbol</c> and the
    /// frame's <c>parentSpecifiers</c>, so they stay local functions (contract §5). TS's <c>getSymbolChain</c> shadows
    /// the worker's <c>symbol</c>/<c>meaning</c> parameters with its own, which C# permits for a local function's
    /// parameters.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS declares <c>parentSpecifiers: (string | undefined)[]</c> uninitialized and only ever reads it after
    /// assigning the mapped parents; the port initializes it to an empty list, because a captured local must be
    /// definitely assigned before the local function that reads it is declared. The value is never consulted unless
    /// <c>parents</c> was non-empty, i.e. unless it was assigned.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>indices.sort(sortByBestName)</c> is a stable in-place sort of an index array; the port keeps that
    /// through <see cref="Core.StableSort{T}"/> (the BCL's <c>List&lt;T&gt;.Sort</c> is unstable) and adapts the
    /// reference's plain number to the port's <see cref="Comparison"/> through <c>Core.CompareValues</c>.
    /// </para>
    /// </remarks>
    internal List<ISymbol> LookupSymbolChainWorker(ISymbol symbol, NodeBuilderContext context, SymbolFlags meaning, bool yieldModuleSymbol)
    {
        // Try to get qualified name if the symbol is not a type parameter and there is an enclosing declaration.
        List<ISymbol> chain;
        var isTypeParameter = (symbol.Flags & SymbolFlags.TypeParameter) != 0;
        if (!isTypeParameter && (context.EnclosingDeclaration is not null || (context.Flags & NodeBuilderFlags.UseFullyQualifiedType) != 0) && (context.InternalFlags & InternalNodeBuilderFlags.DoNotIncludeSymbolChain) == 0)
        {
            chain = Debug.CheckDefined(GetSymbolChain(symbol, meaning, /*endOfChain*/ true));
            Debug.Assert(chain is not null && chain.Count > 0);
        }
        else
        {
            chain = [symbol];
        }
        return chain;

        /// <param name="endOfChain">Set to false for recursive calls; non-recursive calls should always output something.</param>
        List<ISymbol>? GetSymbolChain(ISymbol symbol, SymbolFlags meaning, bool endOfChain)
        {
            List<ISymbol>? accessibleSymbolChain = GetAccessibleSymbolChain(symbol, context.EnclosingDeclaration, meaning, /*useOnlyExternalAliasing*/ (context.Flags & NodeBuilderFlags.UseOnlyExternalAliasing) != 0);
            List<string?> parentSpecifiers = [];
            if (
                accessibleSymbolChain is null ||
                NeedsQualification(accessibleSymbolChain[0], context.EnclosingDeclaration, accessibleSymbolChain.Count == 1 ? meaning : GetQualifiedLeftMeaning(meaning))
            )
            {
                // Go up and add our parent.
                var parents = GetContainersOfSymbol(accessibleSymbolChain is not null ? accessibleSymbolChain[0] : symbol, context.EnclosingDeclaration, meaning);
                if (Core.Length(parents) > 0)
                {
                    parentSpecifiers = Core.Map(parents, (s, _) => Core.Some<IDeclaration>(s.Declarations, d => HasNonGlobalAugmentationExternalModuleSymbol((Node)d))
                        ? GetSpecifierForModuleSymbol(s, context)
                        : (string?)null)!;
                    var indices = Core.Map<ISymbol, int>(parents, (_, i) => i)!;
                    Core.StableSort(indices, (int a, int b) => Core.CompareValues(SortByBestName(a, b), 0));
                    var sortedParents = Core.Map<int, ISymbol>(indices, (i, _) => parents![i])!;
                    foreach (var parent in sortedParents)
                    {
                        var parentChain = GetSymbolChain(parent, GetQualifiedLeftMeaning(meaning), /*endOfChain*/ false);
                        if (parentChain is not null)
                        {
                            if (
                                parent.Exports is not null && parent.Exports.ContainsKey(InternalSymbolName.ExportEquals.Value) &&
                                GetSymbolIfSameReference(parent.Exports[InternalSymbolName.ExportEquals.Value], symbol) is not null
                            )
                            {
                                // parentChain root _is_ symbol - symbol is a module export=, so it kinda looks like it's own parent
                                // No need to lookup an alias for the symbol in itself
                                accessibleSymbolChain = parentChain;
                                break;
                            }
                            accessibleSymbolChain = Core.Concatenate(parentChain, accessibleSymbolChain ?? new List<ISymbol> { GetAliasForSymbolInContainer(parent, symbol) ?? symbol })?.ToList();
                            break;
                        }
                    }
                }
            }

            if (accessibleSymbolChain is not null)
            {
                return accessibleSymbolChain;
            }
            if (
                // If this is the last part of outputting the symbol, always output. The cases apply only to parent symbols.
                endOfChain ||
                // If a parent symbol is an anonymous type, don't write it.
                (symbol.Flags & (SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) == 0
            )
            {
                // If a parent symbol is an external module, don't write it. (We prefer just `x` vs `"foo/bar".x`.)
                if (!endOfChain && !yieldModuleSymbol && Core.Some<IDeclaration>(symbol.Declarations, d => HasNonGlobalAugmentationExternalModuleSymbol((Node)d)))
                {
                    return null;
                }
                return [symbol];
            }
            return null;

            int SortByBestName(int a, int b)
            {
                var specifierA = parentSpecifiers[a];
                var specifierB = parentSpecifiers[b];
                if (specifierA is not null && specifierB is not null)
                {
                    var isBRelative = Paths.PathIsRelative(specifierB);
                    if (Paths.PathIsRelative(specifierA) == isBRelative)
                    {
                        // Both relative or both non-relative, sort by number of parts
                        return CountPathComponents(specifierA) - CountPathComponents(specifierB);
                    }
                    if (isBRelative)
                    {
                        // A is non-relative, B is relative: prefer A
                        return -1;
                    }
                    // A is relative, B is non-relative: prefer B
                    return 1;
                }
                return 0;
            }

            // PORT-NOTE: loud, named blocker. `moduleSpecifiers.countPathComponents` (checker.ts:8534) lives in
            // `moduleSpecifiers.ts` (1,470 lines), which is deliberately not pulled in (ruling 2, M6-late/M8); the path
            // component count is what decides the order of a module's parents, so guessing it would silently reorder
            // generated qualified names. The reference's `sortByBestName` is otherwise ported in full, which is why the
            // comparison above routes through this member. It is a local function so that its parameter list (and so the
            // call shape) stays the reference's.
            int CountPathComponents(string path) => throw new NotSupportedException(
                "getSymbolChain.sortByBestName: moduleSpecifiers.countPathComponents (checker.ts:8535, moduleSpecifiers.ts) is M6-late/M8 — not ported");
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:8548-8556 — <c>typeParametersToTypeParameterDeclarations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's return type is <c>NodeArray&lt;TypeParameterDeclaration&gt; | undefined</c> and it answers
    /// <c>undefined</c> whenever the target symbol is not a class-like; §7.5 wrote the row without the <c>?</c>, but the
    /// generated <c>ITypeChecker.SymbolToTypeParameterDeclarations</c> is <c>NodeArray&lt;TypeParameterDeclaration&gt;?</c>
    /// and this member's only caller tests it for "was anything produced", so the port keeps the <c>?</c>.
    /// </remarks>
    internal NodeArray<TypeParameterDeclaration>? TypeParametersToTypeParameterDeclarations(ISymbol symbol, NodeBuilderContext context)
    {
        NodeArray<TypeParameterDeclaration>? typeParameterNodes = null;
        var targetSymbol = GetTargetSymbol(symbol);
        if ((targetSymbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface | SymbolFlags.TypeAlias)) != 0)
        {
            typeParameterNodes = _factory.CreateNodeArray(
                Core.Map(GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(symbol), (tp, _) => TypeParameterToDeclaration(tp, context)),
                null);
        }
        return typeParameterNodes;
    }

    /// <summary>ported from: src/compiler/checker.ts:8557-8589 — <c>lookupTypeParameterNodes</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS answers the union <c>readonly TypeNode[] | readonly TypeParameterDeclaration[] | undefined</c>.
    /// <c>TypeParameterDeclaration</c> is a declaration and does not implement <see cref="ITypeNode"/> in the port
    /// (<c>Model/NodeClasses.cs:2428</c>), so the declaration arm cannot live in a <c>IList&lt;ITypeNode&gt;</c>;
    /// §7.5's row therefore reads <c>IList&lt;ITypeNode&gt;?</c> while the port declares <c>IList&lt;Node&gt;?</c>.
    /// Every consumer of the result is in this file, and they are exactly the reference's two shapes: an identifier's
    /// <c>typeArguments</c> (<c>factory.createNodeArray&lt;TypeNode | TypeParameterDeclaration&gt;</c>, typed
    /// <c>NodeArray&lt;Node&gt;</c> by <c>EmitNodes.SetIdentifierTypeArguments</c>, which takes declarations fine) and
    /// <c>ImportTypeNode.typeArguments</c> / <c>TypeReferenceNode.typeArguments</c>, which take
    /// <c>List&lt;ITypeNode&gt;</c> and are projected with <c>Cast&lt;ITypeNode&gt;</c> at those two sites — the
    /// declaration arm cannot reach them, because it only produces anything when the chain's root is a
    /// class/interface/type-alias container, while both sites are on the module-root path whose root is an external
    /// module symbol.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>(nextSymbol as TransientSymbol).links.mapper</c> reads the instantiation mapper, which the port
    /// keeps in the <c>object?</c> slot <c>SymbolLinks.Mapper</c> (§3), hence the cast to <see cref="TypeMapper"/>.
    /// </para>
    /// </remarks>
    internal IList<Node>? LookupTypeParameterNodes(IList<ISymbol> chain, int index, NodeBuilderContext context)
    {
        Debug.Assert(chain is not null && 0 <= index && index < chain.Count);
        var symbol = chain[index];
        var symbolId = Utilities.GetSymbolId(symbol);
        if (context.TypeParameterSymbolList?.Contains(symbolId) == true)
        {
            return null;
        }
        if (context.MustCreateTypeParameterSymbolList)
        {
            context.MustCreateTypeParameterSymbolList = false;
            context.TypeParameterSymbolList = context.TypeParameterSymbolList is null ? [] : new HashSet<int>(context.TypeParameterSymbolList);
        }
        context.TypeParameterSymbolList!.Add(symbolId);
        IList<Node>? typeParameterNodes = null;
        if ((context.Flags & NodeBuilderFlags.WriteTypeParametersInQualifiedName) != 0 && index < chain.Count - 1)
        {
            var parentSymbol = symbol;
            var nextSymbol = chain[index + 1];
            if ((GetCheckFlags(nextSymbol) & CheckFlags.Instantiated) != 0)
            {
                var parameters = GetTypeParametersOfClassOrInterface(
                    (parentSymbol.Flags & SymbolFlags.Alias) != 0 ? ResolveAlias(parentSymbol) : parentSymbol);
                // NOTE: cast to TransientSymbol should be safe because only TransientSymbol can have CheckFlags.Instantiated
                var mapper = (TypeMapper?)((TransientSymbol)nextSymbol).Links!.Mapper;
                var mapped = Core.Map(parameters, (t, _) => GetMappedType(t, mapper!));
                // PORT-NOTE: TS assigns `mapToTypeNodes(...)`'s `TypeNode[] | undefined` result straight into the union
                // variable; the port's `MapToTypeNodes` answers `List<ITypeNode>?`, so the upcast to the widened element
                // type is spelled out (the same projection the two `ImportTypeNode`/`TypeReferenceNode` sites do in the
                // other direction).
                typeParameterNodes = MapToTypeNodes(mapped, context)?.Cast<Node>().ToList();
            }
            else
            {
                // PORT-NOTE: the declaration arm; see the class remarks on the union — `Cast<Node>` is the upcast to the
                // widened element type, not a conversion.
                typeParameterNodes = TypeParametersToTypeParameterDeclarations(symbol, context)?.Cast<Node>().ToList();
            }
        }
        return typeParameterNodes;
    }

    /// <summary>ported from: src/compiler/checker.ts:8590-8596 — <c>getTopmostIndexedAccessType</c>.</summary>
    /// <remarks>Given A[B][C][D], finds A[B].</remarks>
    internal IndexedAccessTypeNode GetTopmostIndexedAccessType(IndexedAccessTypeNode top)
    {
        if (NodeTests.IsIndexedAccessTypeNode((Node)top.ObjectType!))
        {
            return GetTopmostIndexedAccessType((IndexedAccessTypeNode)top.ObjectType!);
        }
        return top;
    }

    /// <summary>ported from: src/compiler/checker.ts:8597-8657 — <c>getSpecifierForModuleSymbol</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: loud, named blocker. The specifier itself comes from <c>moduleSpecifiers.getModuleSpecifiers</c>
    /// (checker.ts:8638), which is not ported (ruling 2: <c>moduleSpecifiers.ts</c>, 1,470 lines, is M6-late/M8). Every
    /// other line of the reference is ported: the amd-name test, the ambient-module fallback, the "no resolver host"
    /// fallback, the resolution-mode computation, the cache lookup and the bundle branch.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>{ ...compilerOptions, baseUrl: … }</c> for the bundle case cannot be written over an interface
    /// in C#; <see cref="CompilerOptions"/>'s copy constructor plus a property initializer is the port's equivalent and
    /// the same pattern the resolver slice uses (<c>ModuleResolution.Model.cs:1138-1146</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>createModeAwareCacheKey</c> (utilities.ts) is landed as
    /// <c>ModuleNameResolver.CreateModeAwareCacheKey</c>, and <c>links.specifierCache</c> is the landed
    /// <c>SymbolLinks.SpecifierCache</c> (<c>Dictionary&lt;object, string&gt;?</c>), so both stay real calls/reads.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>||</c> between the three resolution-mode sources is truthiness over
    /// <c>ResolutionMode = ModuleKind | undefined</c>; the only modes a resolution mode can carry are
    /// <c>CommonJS</c>/<c>ESNext</c>, so the port's <c>is null</c> chain is the same selection.
    /// </para>
    /// </remarks>
    internal string GetSpecifierForModuleSymbol(ISymbol symbol, NodeBuilderContext context, ModuleKind? overrideImportMode = null)
    {
        var file = Utilities.GetDeclarationOfKind<SourceFile>(symbol, SyntaxKind.SourceFile);
        if (file is null)
        {
            var equivalentFileSymbol = Core.FirstDefined<IDeclaration, ISymbol>(
                symbol.Declarations,
                (d, _) => GetFileSymbolIfFileSymbolExportEqualsContainer(d, symbol));
            if (equivalentFileSymbol is not null)
            {
                file = Utilities.GetDeclarationOfKind<SourceFile>(equivalentFileSymbol, SyntaxKind.SourceFile);
            }
        }
        if (file is not null && file.ModuleName is not null)
        {
            // Use the amd name if it is available
            return file.ModuleName;
        }
        if (file is null && AmbientModuleSymbolRegex().IsMatch(symbol.EscapedName!))
        {
            return symbol.EscapedName!.Substring(1, symbol.EscapedName!.Length - 1);
        }
        if (context.EnclosingFile is null || context.Tracker?.ModuleResolverHost is null)
        {
            // If there's no context declaration, we can't lookup a non-ambient specifier, so we just use the symbol name
            if (AmbientModuleSymbolRegex().IsMatch(symbol.EscapedName!))
            {
                return symbol.EscapedName!.Substring(1, symbol.EscapedName!.Length - 1);
            }
            // A resolver may not be provided for baselines and errors - in those cases we use the fileName in full
            return Utilities.GetSourceFileOfNode(Utilities.GetNonAugmentationDeclaration(symbol)!)!.FileName!;
        }
        var enclosingDeclaration = Utilities.GetOriginalNode(context.EnclosingDeclaration);
        var originalModuleSpecifier = Utilities.CanHaveModuleSpecifier(enclosingDeclaration)
            ? Utilities.TryGetModuleSpecifierFromDeclaration(enclosingDeclaration!) as LiteralExpression
            : null;
        var contextFile = context.EnclosingFile;
        ModuleKind? resolutionMode = overrideImportMode;
        if (resolutionMode is null && originalModuleSpecifier is not null)
        {
            resolutionMode = _host.GetModeForUsageLocation(contextFile, originalModuleSpecifier) as ModuleKind?;
        }
        if (resolutionMode is null && contextFile is not null)
        {
            resolutionMode = _host.GetDefaultResolutionModeForFile(contextFile) as ModuleKind?;
        }
        var cacheKey = ModuleNameResolver.CreateModeAwareCacheKey(contextFile!.Path!, resolutionMode);
        var links = GetSymbolLinks(symbol);
        var specifier = links.SpecifierCache?.GetValueOrDefault(cacheKey);
        if (specifier is null)
        {
            var isBundle = _compilerOptions.OutFile is not null;
            // For declaration bundles, we need to generate absolute paths relative to the common source dir for imports,
            // just like how the declaration emitter does for the ambient module declarations - we can easily accomplish this
            // using the `baseUrl` compiler option (which we would otherwise never use in declaration emit) and a non-relative
            // specifier preference
            var moduleResolverHost = context.Tracker!.ModuleResolverHost!;
            specifier = GetModuleSpecifiers(
                symbol,
                this,
                isBundle
                    ? new CompilerOptions(_compilerOptions) { BaseUrl = moduleResolverHost.GetCommonSourceDirectory() }
                    : _compilerOptions,
                contextFile,
                moduleResolverHost,
                isBundle,
                resolutionMode,
                overrideImportMode);
        }
        return specifier!;

        // PORT-NOTE: the reference's `first(moduleSpecifiers.getModuleSpecifiers(symbol, checker,
        // specifierCompilerOptions, contextFile, moduleResolverHost, { importModuleSpecifierPreference: isBundle ?
        // "non-relative" : "project-relative", importModuleSpecifierEnding: isBundle ? "minimal" :
        // resolutionMode === ModuleKind.ESNext ? "js" : undefined }, { overrideImportMode }))`. Only the two preference
        // objects are dropped: `ModuleSpecifierPreferences` and `ModuleSpecifierEnding` are declared in
        // `moduleSpecifiers.ts`, which is not ported, so they have no C# type; the two preference values are carried as
        // `isBundle`/`resolutionMode` instead and the blocker names the member, its line and its owner.
        string GetModuleSpecifiers(
            ISymbol symbol,
            ITypeChecker checker,
            ICompilerOptions options,
            SourceFile? contextFile,
            IModuleSpecifierResolutionHost moduleResolverHost,
            bool isBundle,
            ModuleKind? resolutionMode,
            ModuleKind? overrideImportMode) => throw new NotSupportedException(
                "GetSpecifierForModuleSymbol: moduleSpecifiers.getModuleSpecifiers (checker.ts:8638, moduleSpecifiers.ts) is M6-late/M8 — not ported");
    }

    /// <summary>ported from: src/compiler/checker.ts:8658-8662 — <c>symbolToEntityNameNode</c>.</summary>
    /// <remarks>PORT-NOTE: answers <see cref="Node"/> for TS's <c>EntityName</c> (§12.5); callers pass it straight to the factory.</remarks>
    internal Node SymbolToEntityNameNode(ISymbol symbol)
    {
        var identifier = _factory.CreateIdentifier(Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!));
        return symbol.Parent is not null
            ? _factory.CreateQualifiedName(SymbolToEntityNameNode(symbol.Parent), identifier)
            : identifier;
    }

    /// <summary>ported from: src/compiler/checker.ts:8663-8753 — <c>symbolToTypeNode</c> (with the nested <c>createAccessFromSymbolChain</c> 8754-8820).</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>createAccessFromSymbolChain</c> closes over this member's <c>chain</c>, <c>overrideTypeArguments</c>
    /// and <c>context</c>, so it stays a local function (contract §5); it answers <see cref="Node"/> because TS's
    /// <c>EntityName | IndexedAccessTypeNode</c> union has no C# type and its three arms meet at <c>Node</c>.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>let typeParameterNodes = overrideTypeArguments || lookupTypeParameterNodes(…)</c> is a union of
    /// two type-argument arrays; the port widens it to <c>IList&lt;Node&gt;?</c> (see
    /// <see cref="LookupTypeParameterNodes"/>), projects it to <c>List&lt;ITypeNode&gt;</c> where the factory needs type
    /// nodes, and keeps <c>NodeArray&lt;Node&gt;</c> where an identifier's type arguments are set.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>chain[index - 1]</c> reads <c>undefined</c> in the reference when <c>index</c> is <c>0</c>; C#
    /// numeric indexing would throw, so the parent is read behind an index guard. TS's
    /// <c>context.tracker.reportLikelyUnsafeImportRequiredError</c> test for a present presenter is
    /// <see cref="SymbolTrackerImpl"/>'s job in the port, so the call is unconditional here.
    /// </para>
    /// </remarks>
    internal ITypeNode SymbolToTypeNode(ISymbol symbol, NodeBuilderContext context, SymbolFlags meaning, IList<ITypeNode>? overrideTypeArguments = null)
    {
        // If we're using aliases outside the current scope, dont bother with the module
        var chain = LookupSymbolChain(symbol, context, meaning, (context.Flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) == 0);
        var isTypeOf = meaning == SymbolFlags.Value;
        if (Core.Some<IDeclaration>(chain[0].Declarations, d => HasNonGlobalAugmentationExternalModuleSymbol((Node)d)))
        {
            // module is root, must use `ImportTypeNode`
            var nonRootParts = chain.Count > 1 ? CreateAccessFromSymbolChain(chain, chain.Count - 1, 1) : null;
            IList<Node>? typeParameterNodes = overrideTypeArguments is not null
                ? overrideTypeArguments.Cast<Node>().ToList()
                : LookupTypeParameterNodes(chain, 0, context);
            var contextFile = Utilities.GetSourceFileOfNode(Utilities.GetOriginalNode(context.EnclosingDeclaration));
            var targetFile = Utilities.GetSourceFileOfModule(chain[0]);
            string? specifier = null;
            ImportAttributes? attributes = null;
            if (Utilities.GetEmitModuleResolutionKind(_compilerOptions) is ModuleResolutionKind.Node16 or ModuleResolutionKind.NodeNext)
            {
                // An `import` type directed at an esm format file is only going to resolve in esm mode - set the esm mode assertion
                if ((targetFile?.ImpliedNodeFormat as ModuleKind?) == ModuleKind.ESNext && (targetFile.ImpliedNodeFormat as ModuleKind?) != (contextFile?.ImpliedNodeFormat as ModuleKind?))
                {
                    specifier = GetSpecifierForModuleSymbol(chain[0], context, ModuleKind.ESNext);
                    attributes = _factory.CreateImportAttributes(
                        _factory.CreateNodeArray(
                            new List<ImportAttribute>
                            {
                                _factory.CreateImportAttribute(
                                    _factory.CreateStringLiteral("resolution-mode"),
                                    _factory.CreateStringLiteral("import")),
                            },
                            null),
                        null,
                        null);
                }
            }
            if (specifier is null)
            {
                specifier = GetSpecifierForModuleSymbol(chain[0], context);
            }
            if ((context.Flags & NodeBuilderFlags.AllowNodeModulesRelativePaths) == 0 && Utilities.GetEmitModuleResolutionKind(_compilerOptions) != ModuleResolutionKind.Classic && specifier.Contains("/node_modules/"))
            {
                var oldSpecifier = specifier;
                if (Utilities.GetEmitModuleResolutionKind(_compilerOptions) is ModuleResolutionKind.Node16 or ModuleResolutionKind.NodeNext)
                {
                    // We might be able to write a portable import type using a mode override; try specifier generation again, but with a different mode set
                    var swappedMode = (contextFile?.ImpliedNodeFormat as ModuleKind?) == ModuleKind.ESNext ? ModuleKind.CommonJS : ModuleKind.ESNext;
                    specifier = GetSpecifierForModuleSymbol(chain[0], context, swappedMode);

                    if (specifier.Contains("/node_modules/"))
                    {
                        // Still unreachable :(
                        specifier = oldSpecifier;
                    }
                    else
                    {
                        attributes = _factory.CreateImportAttributes(
                            _factory.CreateNodeArray(
                                new List<ImportAttribute>
                                {
                                    _factory.CreateImportAttribute(
                                        _factory.CreateStringLiteral("resolution-mode"),
                                        _factory.CreateStringLiteral(swappedMode == ModuleKind.ESNext ? "import" : "require")),
                                },
                                null),
                            null,
                            null);
                    }
                }

                if (attributes is null)
                {
                    // If ultimately we can only name the symbol with a reference that dives into a `node_modules` folder, we should error
                    // since declaration files with these kinds of references are liable to fail when published :(
                    context.EncounteredError = true;
                    context.Tracker!.ReportLikelyUnsafeImportRequiredError(oldSpecifier, Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!));
                }
            }
            var lit = _factory.CreateLiteralTypeNode(_factory.CreateStringLiteral(specifier));
            context.ApproximateLength += specifier.Length + 10; // specifier + import("")
            if (nonRootParts is null || Utilities.IsEntityName(nonRootParts))
            {
                if (nonRootParts is not null)
                {
                    var lastId = NodeTests.IsIdentifier(nonRootParts) ? (Identifier)nonRootParts : ((QualifiedName)nonRootParts).Right!;
                    EmitNodes.SetIdentifierTypeArguments(lastId, /*typeArguments*/ null);
                }
                return _factory.CreateImportTypeNode(
                    lit,
                    attributes,
                    nonRootParts,
                    typeParameterNodes?.Cast<ITypeNode>().ToList(),
                    isTypeOf);
            }
            else
            {
                var splitNode = GetTopmostIndexedAccessType((IndexedAccessTypeNode)nonRootParts);
                var qualifier = ((TypeReferenceNode)splitNode.ObjectType!).TypeName;
                return _factory.CreateIndexedAccessTypeNode(
                    _factory.CreateImportTypeNode(
                        lit,
                        attributes,
                        qualifier,
                        typeParameterNodes?.Cast<ITypeNode>().ToList(),
                        isTypeOf),
                    splitNode.IndexType);
            }
        }

        var entityName = CreateAccessFromSymbolChain(chain, chain.Count - 1, 0);
        if (NodeTests.IsIndexedAccessTypeNode(entityName))
        {
            return (IndexedAccessTypeNode)entityName; // Indexed accesses can never be `typeof`
        }
        if (isTypeOf)
        {
            return _factory.CreateTypeQueryNode(entityName, null);
        }
        else
        {
            var lastId = NodeTests.IsIdentifier(entityName) ? (Identifier)entityName : ((QualifiedName)entityName).Right!;
            var lastTypeArgs = EmitNodes.GetIdentifierTypeArguments(lastId);
            EmitNodes.SetIdentifierTypeArguments(lastId, /*typeArguments*/ null);
            // PORT-NOTE: TS's `lastTypeArgs as NodeArray<TypeNode>` is a cast between array element types; the port's
            // `EmitNodes.GetIdentifierTypeArguments` answers `NodeArray<Node>?`, so the projection to `List<ITypeNode>` is
            // spelled out.
            return _factory.CreateTypeReferenceNode(entityName, lastTypeArgs?.Cast<ITypeNode>().ToList());
        }

        Node CreateAccessFromSymbolChain(List<ISymbol> chain, int index, int stopper)
        {
            IList<Node>? typeParameterNodes = index == chain.Count - 1
                ? overrideTypeArguments?.Cast<Node>().ToList()
                : LookupTypeParameterNodes(chain, index, context);
            var symbol = chain[index];
            // PORT-NOTE: TS reads `chain[index - 1]`, which is `undefined` at index 0; see the member remarks.
            var parent = index > 0 ? chain[index - 1] : null;

            string? symbolName = null;
            if (index == 0)
            {
                context.Flags |= NodeBuilderFlags.InInitialEntityName;
                symbolName = GetNameOfSymbolAsWritten(symbol, context);
                context.ApproximateLength += (symbolName is not null ? symbolName.Length : 0) + 1;
                context.Flags ^= NodeBuilderFlags.InInitialEntityName;
            }
            else
            {
                if (parent is not null && GetExportsOfSymbol(parent) is not null)
                {
                    var exports = GetExportsOfSymbol(parent);
                    foreach (var entry in exports)
                    {
                        // PORT-NOTE: TS's `forEachEntry(exports, (ex, name) => …)` breaks out of the walk when the callback
                        // returns true; the port's `SymbolTable.ForEach` cannot, so the reference's early exit is the loop's
                        // `break`. Iteration order is the table's insertion order either way.
                        if (GetSymbolIfSameReference(entry.Value, symbol) is not null && !IsLateBoundName(entry.Key) && entry.Key != InternalSymbolName.ExportEquals.Value)
                        {
                            symbolName = Utilities.UnescapeLeadingUnderscores(entry.Key);
                            break;
                        }
                    }
                }
            }

            if (symbolName is null)
            {
                // PORT-NOTE: TS's `firstDefined(symbol.declarations, getNameOfDeclaration)` passes the narrowed
                // `Declaration`; the port's `Utilities.GetNameOfDeclaration` takes `Node?`, so the upcast is explicit
                // (contract §3 — `IDeclaration` is not `Node`).
                var name = Core.FirstDefined<IDeclaration, Node>(symbol.Declarations, (d, _) => Utilities.GetNameOfDeclaration((Node)d) as Node);
                if (name is not null && NodeTests.IsComputedPropertyName(name) && Utilities.IsEntityName(((ComputedPropertyName)name).Expression!))
                {
                    var LHS = CreateAccessFromSymbolChain(chain, index - 1, stopper);
                    if (Utilities.IsEntityName(LHS))
                    {
                        return _factory.CreateIndexedAccessTypeNode(
                            _factory.CreateParenthesizedType(_factory.CreateTypeQueryNode(LHS, null)),
                            _factory.CreateTypeQueryNode(((ComputedPropertyName)name).Expression!, null));
                    }
                    return LHS;
                }
                symbolName = GetNameOfSymbolAsWritten(symbol, context);
            }
            context.ApproximateLength += symbolName.Length + 1;

            if (
                (context.Flags & NodeBuilderFlags.ForbidIndexedAccessSymbolReferences) == 0 && parent is not null &&
                // PORT-NOTE: TS also tests `getMembersOfSymbol(parent)` for truthiness; the port's member answers a
                // non-null table, so the contained-name test is the whole condition.
                GetMembersOfSymbol(parent).ContainsKey(symbol.EscapedName!) &&
                GetSymbolIfSameReference(GetMembersOfSymbol(parent)[symbol.EscapedName!], symbol) is not null
            )
            {
                // Should use an indexed access
                var LHS = CreateAccessFromSymbolChain(chain, index - 1, stopper);
                if (NodeTests.IsIndexedAccessTypeNode(LHS))
                {
                    // PORT-NOTE: TS's `isIndexedAccessTypeNode(LHS)` is a type guard, so `createIndexedAccessTypeNode`
                    // receives an `IndexedAccessTypeNode` there; a C# predicate narrows nothing, so the class is named.
                    return _factory.CreateIndexedAccessTypeNode(
                        (IndexedAccessTypeNode)LHS,
                        _factory.CreateLiteralTypeNode(_factory.CreateStringLiteral(symbolName)));
                }
                else
                {
                    return _factory.CreateIndexedAccessTypeNode(
                        _factory.CreateTypeReferenceNode(LHS, typeParameterNodes?.Cast<ITypeNode>().ToList()),
                        _factory.CreateLiteralTypeNode(_factory.CreateStringLiteral(symbolName)));
                }
            }

            var identifier = EmitNodes.SetEmitFlags(_factory.CreateIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
            if (typeParameterNodes is not null)
            {
                EmitNodes.SetIdentifierTypeArguments(identifier, _factory.CreateNodeArray<Node>(typeParameterNodes, null));
            }
            identifier.Symbol = symbol;

            if (index > stopper)
            {
                var LHS = CreateAccessFromSymbolChain(chain, index - 1, stopper);
                if (!Utilities.IsEntityName(LHS))
                {
                    Debug.Fail("Impossible construct - an export of an indexed access cannot be reachable");
                }
                return _factory.CreateQualifiedName(LHS, identifier);
            }
            return identifier;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:8821-8828 — <c>typeParameterShadowsOtherTypeParameterInScope</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>escapedName: __String</c> is the port's <see cref="string"/>.</remarks>
    internal bool TypeParameterShadowsOtherTypeParameterInScope(string escapedName, NodeBuilderContext context, ITypeParameter type)
    {
        var result = ResolveName(context.EnclosingDeclaration, escapedName, SymbolFlags.Type, /*nameNotFoundMessage*/ null, /*isUse*/ false, /*excludeGlobals*/ false);
        if (result is not null && (result.Flags & SymbolFlags.TypeParameter) != 0)
        {
            return !ReferenceEquals(result, type.Symbol);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:8829-8871 — <c>typeParameterToName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>result</c> is the <c>EntityName</c> union and is narrowed with the reference's
    /// <c>!(result.kind &amp; SyntaxKind.Identifier)</c> bit test, whose intent (and whose two possible kinds here,
    /// <c>Identifier</c> and <c>QualifiedName</c>) is <see cref="NodeTests.IsIdentifier"/>; the port therefore answers
    /// <see cref="Node"/> from <see cref="SymbolToName"/> and keeps <c>result</c> as a <c>Node</c>, casting at the four
    /// places the reference reads an identifier-only member (<c>escapedText</c>, the name-type cache, the return).
    /// TS's <c>type.symbol?.declarations?.[0]</c> is <see cref="Core.ElementAt{T}"/>.
    /// </remarks>
    internal Identifier TypeParameterToName(ITypeParameter type, NodeBuilderContext context)
    {
        if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && context.TypeParameterNames is not null)
        {
            var cached = context.TypeParameterNames.GetValueOrDefault(GetTypeId(type));
            if (cached is not null)
            {
                return cached;
            }
        }
        var result = SymbolToName(type.Symbol!, context, SymbolFlags.Type, /*expectsIdentifier*/ true);
        if (!NodeTests.IsIdentifier(result))
        {
            return _factory.CreateIdentifier("(Missing type parameter)");
        }
        var decl = Core.ElementAt(type.Symbol?.Declarations, 0);
        if (decl is not null && NodeTests.IsTypeParameterDeclaration((Node)decl))
        {
            result = SetTextRange(context, result, ((TypeParameterDeclaration)decl).Name);
        }
        if ((context.Flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0)
        {
            var rawtext = ((Identifier)result).EscapedText!;
            var i = context.TypeParameterNamesByTextNextNameCount?.GetValueOrDefault(rawtext) ?? 0;
            var text = rawtext;
            while (context.TypeParameterNamesByText?.Contains(text) == true || TypeParameterShadowsOtherTypeParameterInScope(text, context, type))
            {
                i++;
                text = $"{rawtext}_{i}";
            }
            if (text != rawtext)
            {
                var typeArguments = EmitNodes.GetIdentifierTypeArguments(result);
                result = _factory.CreateIdentifier(text);
                EmitNodes.SetIdentifierTypeArguments((Identifier)result, typeArguments);
            }
            if (context.MustCreateTypeParametersNamesLookups)
            {
                context.MustCreateTypeParametersNamesLookups = false;
                // PORT-NOTE: TS's `new Map(existing)` / `new Set(existing)` accept `undefined`; the port's collection
                // constructors do not, so an absent lookup starts as an empty one.
                context.TypeParameterNames = context.TypeParameterNames is null ? [] : new Dictionary<int, Identifier>(context.TypeParameterNames);
                context.TypeParameterNamesByTextNextNameCount = context.TypeParameterNamesByTextNextNameCount is null ? [] : new Dictionary<string, int>(context.TypeParameterNamesByTextNextNameCount);
                context.TypeParameterNamesByText = context.TypeParameterNamesByText is null ? [] : new HashSet<string>(context.TypeParameterNamesByText);
            }
            // avoiding iterations of the above loop turns out to be worth it when `i` starts to get large, so we cache the max
            // `i` we've used thus far, to save work later
            context.TypeParameterNamesByTextNextNameCount![rawtext] = i;
            context.TypeParameterNames![GetTypeId(type)] = (Identifier)result;
            context.TypeParameterNamesByText!.Add(text);
        }
        return (Identifier)result;
    }

    /// <summary>ported from: src/compiler/checker.ts:8872-8905 — <c>symbolToName</c> (the two overload declarations and the implementation; with the nested <c>createEntityNameFromSymbolChain</c> 8886-8904).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two overloads differ only in the literal type of <c>expectsIdentifier</c> (<c>true</c> answers
    /// <c>Identifier</c>, <c>false</c> answers <c>EntityName</c>). C# cannot overload on a literal and has no
    /// <c>EntityName</c> type, so the port declares the one implementation with a <c>bool</c> and answers
    /// <see cref="Node"/> (§7.5 as amended). <c>createEntityNameFromSymbolChain</c> closes over <c>context</c>, so it
    /// stays a local function.
    /// </remarks>
    internal Node SymbolToName(ISymbol symbol, NodeBuilderContext context, SymbolFlags meaning, bool expectsIdentifier)
    {
        var chain = LookupSymbolChain(symbol, context, meaning);

        if (
            expectsIdentifier && chain.Count != 1
            && !context.EncounteredError
            && (context.Flags & NodeBuilderFlags.AllowQualifiedNameInPlaceOfIdentifier) == 0
        )
        {
            context.EncounteredError = true;
        }
        return CreateEntityNameFromSymbolChain(chain, chain.Count - 1);

        Node CreateEntityNameFromSymbolChain(List<ISymbol> chain, int index)
        {
            var typeParameterNodes = LookupTypeParameterNodes(chain, index, context);
            var symbol = chain[index];

            if (index == 0)
            {
                context.Flags |= NodeBuilderFlags.InInitialEntityName;
            }
            var symbolName = GetNameOfSymbolAsWritten(symbol, context);
            if (index == 0)
            {
                context.Flags ^= NodeBuilderFlags.InInitialEntityName;
            }

            var identifier = EmitNodes.SetEmitFlags(_factory.CreateIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
            if (typeParameterNodes is not null)
            {
                EmitNodes.SetIdentifierTypeArguments(identifier, _factory.CreateNodeArray<Node>(typeParameterNodes, null));
            }
            identifier.Symbol = symbol;

            return index > 0
                ? _factory.CreateQualifiedName(CreateEntityNameFromSymbolChain(chain, index - 1), identifier)
                : identifier;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:8906-8963 — <c>symbolToExpression</c> (with the nested <c>createExpressionFromSymbolChain</c> 8911-8962).</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: <c>createExpressionFromSymbolChain</c> closes over <c>context</c>, so it stays a local function.
    /// TS's <c>symbolName.charCodeAt(0)</c> has no port helper outside the scanner, so the character is read through
    /// <c>ScannerUtilities.CharCodeAt</c>, whose out-of-range answer is <c>CharacterCodes.EOF</c> — the reference's
    /// <c>NaN</c> never compares equal to any <c>CharacterCodes</c> constant either.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>stripQuotes(symbolName).replace(/\\./g, s =&gt; s.substring(1))</c> removes the backslash of
    /// every escape in a quoted property name; the port writes the same global replace as
    /// <see cref="Regex.Replace(string, string, MatchEvaluator)"/> over the <c>\\.</c> pattern.
    /// </para>
    /// </remarks>
    internal Expression? SymbolToExpression(ISymbol symbol, NodeBuilderContext context, SymbolFlags meaning)
    {
        var chain = LookupSymbolChain(symbol, context, meaning);

        return CreateExpressionFromSymbolChain(chain, chain.Count - 1);

        Expression CreateExpressionFromSymbolChain(List<ISymbol> chain, int index)
        {
            var typeParameterNodes = LookupTypeParameterNodes(chain, index, context);
            var symbol = chain[index];

            if (index == 0)
            {
                context.Flags |= NodeBuilderFlags.InInitialEntityName;
            }
            var symbolName = GetNameOfSymbolAsWritten(symbol, context);
            if (index == 0)
            {
                context.Flags ^= NodeBuilderFlags.InInitialEntityName;
            }
            var firstChar = ScannerUtilities.CharCodeAt(symbolName, 0);

            if (Utilities.IsSingleOrDoubleQuote(firstChar) && Core.Some<IDeclaration>(symbol.Declarations, d => HasNonGlobalAugmentationExternalModuleSymbol((Node)d)))
            {
                var specifier = GetSpecifierForModuleSymbol(symbol, context);
                context.ApproximateLength += 2 + specifier.Length; // "specifier"
                return _factory.CreateStringLiteral(specifier);
            }
            if (index == 0 || Utilities.CanUsePropertyAccess(symbolName, _languageVersion))
            {
                var identifier = EmitNodes.SetEmitFlags(_factory.CreateIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
                if (typeParameterNodes is not null)
                {
                    EmitNodes.SetIdentifierTypeArguments(identifier, _factory.CreateNodeArray<Node>(typeParameterNodes, null));
                }
                identifier.Symbol = symbol;
                context.ApproximateLength += 1 + symbolName.Length; // .symbolName
                return index > 0
                    ? _factory.CreatePropertyAccessExpression(CreateExpressionFromSymbolChain(chain, index - 1), identifier)
                    : identifier;
            }
            else
            {
                if (firstChar == CharacterCodes.OpenBracket)
                {
                    symbolName = symbolName.Substring(1, symbolName.Length - 1);
                    firstChar = ScannerUtilities.CharCodeAt(symbolName, 0);
                }
                Expression? expression = null;
                if (Utilities.IsSingleOrDoubleQuote(firstChar) && (symbol.Flags & SymbolFlags.EnumMember) == 0)
                {
                    var literalText = Regex.Replace(Utilities.StripQuotes(symbolName), "\\\\.", m => m.Value.Substring(1));
                    context.ApproximateLength += literalText.Length + 2; // "literalText"
                    expression = _factory.CreateStringLiteral(literalText, firstChar == CharacterCodes.SingleQuote);
                }
                else if (Js.NumberToString(Js.ToNumber(symbolName)) == symbolName)
                {
                    context.ApproximateLength += symbolName.Length; // +symbolName
                    expression = _factory.CreateNumericLiteral(Js.ToNumber(symbolName));
                }
                if (expression is null)
                {
                    var identifier = EmitNodes.SetEmitFlags(_factory.CreateIdentifier(symbolName), EmitFlags.NoAsciiEscaping);
                    if (typeParameterNodes is not null)
                    {
                        EmitNodes.SetIdentifierTypeArguments(identifier, _factory.CreateNodeArray<Node>(typeParameterNodes, null));
                    }
                    identifier.Symbol = symbol;
                    context.ApproximateLength += symbolName.Length; // symbolName
                    expression = identifier;
                }
                context.ApproximateLength += 2; // []
                return _factory.CreateElementAccessExpression(CreateExpressionFromSymbolChain(chain, index - 1), expression);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:8964-8979 — <c>isStringNamed</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>Utilities.GetNameOfDeclaration</c> answers <c>DeclarationName</c>, which is the port's
    /// <c>object?</c> (§12.5), so the narrowed node is taken with an explicit cast.
    /// </remarks>
    internal bool IsStringNamed(IDeclaration d)
    {
        var name = Utilities.GetNameOfDeclaration((Node)d);
        if (name is null)
        {
            return false;
        }
        var nameNode = (Node)name;
        if (NodeTests.IsComputedPropertyName(nameNode))
        {
            var type = CheckExpression(((ComputedPropertyName)nameNode).Expression!);
            return (type.Flags & TypeFlags.StringLike) != 0;
        }
        if (NodeTests.IsElementAccessExpression(nameNode))
        {
            var type = CheckExpression(((ElementAccessExpression)nameNode).ArgumentExpression!);
            return (type.Flags & TypeFlags.StringLike) != 0;
        }
        return NodeTests.IsStringLiteral(nameNode);
    }

    /// <summary>ported from: src/compiler/checker.ts:8980-8984 — <c>isSingleQuotedStringNamed</c>.</summary>
    internal bool IsSingleQuotedStringNamed(IDeclaration d)
    {
        var name = Utilities.GetNameOfDeclaration((Node)d);
        return name is not null && NodeTests.IsStringLiteral((Node)name)
            && (((StringLiteral)(Node)name).SingleQuote == true
                || !Utilities.NodeIsSynthesized((Node)name) && Core.StartsWith(Utilities.GetTextOfNode((Node)name, /*includeTrivia*/ false), "'"));
    }

    /// <summary>ported from: src/compiler/checker.ts:8985-9001 — <c>getPropertyNameNodeForSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference declares and calls this with only <c>(symbol, context)</c> (declaration checker.ts:8985;
    /// call sites 7779 and 10838); §7.5 pins a third, defaulted <c>singleQuote</c> parameter. The port declares it as
    /// pinned so the cross-file call shape cannot disagree, and the default keeps the reference's two-argument calls
    /// intact. PORT-NOTE: the reference's local <c>const singleQuote = !!length(symbol.declarations) &amp;&amp;
    /// every(symbol.declarations, isSingleQuotedStringNamed)</c> is the port's <c>isSingleQuoted</c>, because the pinned
    /// parameter owns that name in this scope; the value and its uses are the reference's.
    /// </remarks>
    internal Node? GetPropertyNameNodeForSymbol(ISymbol symbol, NodeBuilderContext context, bool singleQuote = false)
    {
        var hashPrivateName = GetClonedHashPrivateName(symbol);
        if (hashPrivateName is not null)
        {
            return hashPrivateName;
        }
        var stringNamed = Core.Length(symbol.Declarations) > 0 && Core.Every<IDeclaration>(symbol.Declarations, (d, _) => IsStringNamed(d));
        var isSingleQuoted = Core.Length(symbol.Declarations) > 0 && Core.Every<IDeclaration>(symbol.Declarations, (d, _) => IsSingleQuotedStringNamed(d));
        var isMethod = (symbol.Flags & SymbolFlags.Method) != 0;
        var fromNameType = GetPropertyNameNodeForSymbolFromNameType(symbol, context, isSingleQuoted, stringNamed, isMethod);
        if (fromNameType is not null)
        {
            return fromNameType;
        }
        var rawName = Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!);
        return Utilities.CreatePropertyNameNodeForIdentifierOrLiteral(rawName, Utilities.GetEmitScriptTarget(_compilerOptions), isSingleQuoted, stringNamed, isMethod);
    }

    /// <summary>ported from: src/compiler/checker.ts:9002-9020 — <c>getPropertyNameNodeForSymbolFromNameType</c>.</summary>
    /// <remarks>
    /// See <c>getNameForSymbolFromNameType</c> for a stringy equivalent.
    /// <para>
    /// PORT-NOTE: TS's <c>"" + literalType.value</c> coerces a <c>string | number</c> slot to a string;
    /// <c>SymbolLinks.NameType</c> is <c>IType?</c> and the literal classes keep the value in
    /// <c>LiteralType.Value</c> (<c>object?</c>), so the two arms are spelled out (the port's convention for
    /// <c>"" + number</c> is <c>Js.NumberToString</c>). TS's <c>-name</c> over a numeric string is the same coercion.
    /// </para>
    /// </remarks>
    internal Node? GetPropertyNameNodeForSymbolFromNameType(ISymbol symbol, NodeBuilderContext context, bool singleQuote, bool stringNamed, bool isMethod)
    {
        var nameType = GetSymbolLinks(symbol).NameType;
        if (nameType is not null)
        {
            if ((nameType.Flags & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var literalValue = ((LiteralType)nameType).Value;
                var name = literalValue is string literalText ? literalText : Js.NumberToString((double)literalValue!);
                if (!ScannerUtilities.IsIdentifierText(name, Utilities.GetEmitScriptTarget(_compilerOptions)) && (stringNamed || !Utilities.IsNumericLiteralName(name)))
                {
                    return _factory.CreateStringLiteral(name, singleQuote);
                }
                if (Utilities.IsNumericLiteralName(name) && Core.StartsWith(name, "-"))
                {
                    return _factory.CreateComputedPropertyName(
                        _factory.CreatePrefixUnaryExpression(SyntaxKind.MinusToken, _factory.CreateNumericLiteral(-Js.ToNumber(name))));
                }
                return Utilities.CreatePropertyNameNodeForIdentifierOrLiteral(name, Utilities.GetEmitScriptTarget(_compilerOptions), singleQuote, stringNamed, isMethod);
            }
            if ((nameType.Flags & TypeFlags.UniqueESSymbol) != 0)
            {
                return _factory.CreateComputedPropertyName(SymbolToExpression(((UniqueESSymbolType)nameType).Symbol!, context, SymbolFlags.Value));
            }
        }
        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:9021-9051 — <c>cloneNodeBuilderContext</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's name promises a clone but the body mutates <c>context</c> and returns the restore
    /// closure; §7.5's <c>NodeBuilderContext</c> return type is therefore wrong and the port declares
    /// <see cref="Action"/>, which is what every call site does with it (<c>var cleanup = CloneNodeBuilderContext(ctx);
    /// …; cleanup();</c> — checker.ts:8166, 9616). The six saved values are the reference's.
    /// </remarks>
    internal Action CloneNodeBuilderContext(NodeBuilderContext context)
    {
        // Make type parameters created within this context not consume the name outside this context
        // The symbol serializer ends up creating many sibling scopes that all need "separate" contexts when
        // it comes to naming things - within a normal `typeToTypeNode` call, the node builder only ever descends
        // through the type tree, so the only cases where we could have used distinct sibling scopes was when there
        // were multiple generic overloads with similar generated type parameter names
        // The effect:
        // When we write out
        // export const x: <T>(x: T) => T
        // export const y: <T>(x: T) => T
        // we write it out like that, rather than as
        // export const x: <T>(x: T) => T
        // export const y: <T_1>(x: T_1) => T_1
        var oldMustCreateTypeParameterSymbolList = context.MustCreateTypeParameterSymbolList;
        var oldMustCreateTypeParametersNamesLookups = context.MustCreateTypeParametersNamesLookups;
        context.MustCreateTypeParameterSymbolList = true;
        context.MustCreateTypeParametersNamesLookups = true;
        var oldTypeParameterNames = context.TypeParameterNames;
        var oldTypeParameterNamesByText = context.TypeParameterNamesByText;
        var oldTypeParameterNamesByTextNextNameCount = context.TypeParameterNamesByTextNextNameCount;
        var oldTypeParameterSymbolList = context.TypeParameterSymbolList;
        return () =>
        {
            context.TypeParameterNames = oldTypeParameterNames;
            context.TypeParameterNamesByText = oldTypeParameterNamesByText;
            context.TypeParameterNamesByTextNextNameCount = oldTypeParameterNamesByTextNextNameCount;
            context.TypeParameterSymbolList = oldTypeParameterSymbolList;
            context.MustCreateTypeParameterSymbolList = oldMustCreateTypeParameterSymbolList;
            context.MustCreateTypeParametersNamesLookups = oldMustCreateTypeParametersNamesLookups;
        };
    }

    /// <summary>ported from: src/compiler/checker.ts:9052-9055 — <c>getDeclarationWithTypeAnnotation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>symbol.declarations &amp;&amp; find(…)</c> is <see cref="Core.Find{T}"/>, which answers
    /// <c>null</c> for an absent list; the inner <c>findAncestor</c> is <see cref="Utilities.FindAncestor(Node?, Func{Node, bool})"/>.
    /// </remarks>
    internal IDeclaration? GetDeclarationWithTypeAnnotation(ISymbol symbol, Node? enclosingDeclaration = null)
    {
        return Core.Find<IDeclaration>(
            symbol.Declarations,
            (s, _) => GetNonlocalEffectiveTypeAnnotationNode((Node)s) is not null
                && (enclosingDeclaration is null || Utilities.FindAncestor((Node)s, n => ReferenceEquals(n, enclosingDeclaration)) is not null));
    }

    /// <summary>ported from: src/compiler/checker.ts:9056-9068 — <c>existingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's remark <c>getTypeFromTypeReference(existing)</c> is a call kept for the symbol resolution it
    /// performs; the port keeps the bare call.
    /// </remarks>
    internal bool ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(ITypeNode existing, IType type)
    {
        // In JS, you can say something like `Foo` and get a `Foo<any>` implicitly - we don't want to preserve that original `Foo` in these cases, though.
        if ((GetObjectFlags(type) & ObjectFlags.Reference) == 0) return true;
        if (!NodeTests.IsTypeReferenceNode((Node)existing)) return true;
        // `type` is a reference type, and `existing` is a type reference node, but we still need to make sure they refer to the _same_ target type
        // before we go comparing their type argument counts.
        GetTypeFromTypeReference((Node)existing); // call to ensure symbol is resolved
        var symbol = GetNodeLinks((Node)existing).ResolvedSymbol;
        var existingTarget = symbol is not null ? GetDeclaredTypeOfSymbol(symbol) : null;
        if (existingTarget is null || !ReferenceEquals(existingTarget, ((TypeReference)type).Target)) return true;
        return Core.Length(((TypeReferenceNode)existing).TypeArguments) >= GetMinTypeArgumentCount(((TypeReference)type).Target?.TypeParameters);
    }

    /// <summary>ported from: src/compiler/checker.ts:9069-9079 — <c>getEnclosingDeclarationIgnoringFakeScope</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the port's <c>NodeLinks.FakeScopeForSignatureDeclaration</c> is a <c>string?</c> slot rather than TS's
    /// boolean, so the reference's truthiness test is <c>Js.IsTruthy</c> over it.
    /// </remarks>
    internal Node GetEnclosingDeclarationIgnoringFakeScope(Node enclosingDeclaration)
    {
        while (Js.IsTruthy(GetNodeLinks(enclosingDeclaration).FakeScopeForSignatureDeclaration))
        {
            enclosingDeclaration = enclosingDeclaration.Parent!;
        }
        return enclosingDeclaration;
    }

    /// <summary>ported from: src/compiler/checker.ts:9080-9097 — <c>serializeInferredTypeForDeclaration</c>.</summary>
    /// <remarks>
    /// Serialize the checker type for the declaration without trying to go though the syntactic printer. Unlike
    /// <c>typeToTypeNodeHelper</c>, this handles setting up the <c>AllowUniqueESSymbolType</c> flag so a
    /// <c>unique symbol</c> is returned when appropriate for the input symbol, rather than <c>typeof sym</c>.
    /// </remarks>
    internal ITypeNode? SerializeInferredTypeForDeclaration(ISymbol symbol, NodeBuilderContext context, IType type)
    {
        if ((type.Flags & TypeFlags.UniqueESSymbol) != 0 &&
            ReferenceEquals(type.Symbol, symbol) &&
            (context.EnclosingDeclaration is null ||
             Core.Some<IDeclaration>(symbol.Declarations, d => ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), context.EnclosingFile))))
        {
            context.Flags |= NodeBuilderFlags.AllowUniqueESSymbolType;
        }

        return TypeToTypeNodeHelper(type, context);
    }

    /// <summary>ported from: src/compiler/checker.ts:9098-9124 — <c>serializeTypeForDeclaration</c>.</summary>
    /// <remarks>
    /// Serializes the type of the declaration by first trying to use the syntactic printer if possible and falling
    /// back to the checker type if not.
    /// <para>
    /// PORT-NOTE: the reference's doc comment names its parameters (<c>context</c>, <c>declaration</c>, <c>type</c>,
    /// <c>symbol</c>); the port's names and order are the same, per §7.5.
    /// </para>
    /// </remarks>
    internal ITypeNode? SerializeTypeForDeclaration(NodeBuilderContext context, IDeclaration? declaration, IType type, ISymbol symbol)
    {
        ITypeNode? result = null;
        var addUndefinedForParameter = declaration is not null &&
            (NodeTests.IsParameter((Node)declaration) || NodeTests.IsJSDocParameterTag((Node)declaration)) &&
            RequiresAddingImplicitUndefined((Node)declaration, context.EnclosingDeclaration);
        var decl = declaration ?? symbol.ValueDeclaration ?? GetDeclarationWithTypeAnnotation(symbol) ?? Core.ElementAt(symbol.Declarations, 0);
        if (!CanPossiblyExpandType(type, context) && decl is not null)
        {
            var restore = AddSymbolTypeToContext(context, symbol, type);
            if (Utilities.IsAccessor((Node)decl))
            {
                result = SyntacticNodeBuilder.SerializeTypeOfAccessor!((Node)decl, symbol, context);
            }
            else if (Utilities.HasInferredType((Node)decl) &&
                !Utilities.NodeIsSynthesized((Node)decl) &&
                (GetObjectFlags(type) & ObjectFlags.RequiresWidening) == 0)
            {
                // PORT-NOTE: TS's `syntacticNodeBuilder.serializeTypeOfDeclaration(decl, symbol, context)`; the port's
                // generated slot takes `object?` for the declaration (the reference's `HasInferredType` is a type guard
                // whose narrowed type has no C# name), so the `IDeclaration` passes through uncast.
                result = SyntacticNodeBuilder.SerializeTypeOfDeclaration!(decl, symbol, context);
            }
            restore();
        }
        if (result is null)
        {
            if (addUndefinedForParameter)
            {
                type = GetOptionalType(type);
            }
            result = SerializeInferredTypeForDeclaration(symbol, context, type);
        }
        return result ?? _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
    }

    /// <summary>ported from: src/compiler/checker.ts:9125-9143 — <c>typeNodeIsEquivalentToType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(isPropertySignature(d) || isPropertyDeclaration(d)) &amp;&amp; d.questionToken</c> reads the
    /// narrowed slot; C# cannot narrow through a predicate, so the port spells both arms out (contract §3) and keeps
    /// the reference's <c>&amp;&amp;</c>/<c>||</c> grouping.
    /// </remarks>
    internal bool TypeNodeIsEquivalentToType(Node? annotatedDeclaration, IType type, IType typeFromTypeNode)
    {
        if (ReferenceEquals(typeFromTypeNode, type))
        {
            return true;
        }
        if (annotatedDeclaration is null)
        {
            return false;
        }
        if ((NodeTests.IsPropertySignature(annotatedDeclaration) && ((PropertySignature)annotatedDeclaration).QuestionToken is not null) ||
            (NodeTests.IsPropertyDeclaration(annotatedDeclaration) && ((PropertyDeclaration)annotatedDeclaration).QuestionToken is not null))
        {
            return ReferenceEquals(GetTypeWithFacts(type, TypeFacts.NEUndefined), typeFromTypeNode);
        }
        if (NodeTests.IsParameter(annotatedDeclaration) && HasEffectiveQuestionToken(annotatedDeclaration))
        {
            return ReferenceEquals(GetTypeWithFacts(type, TypeFacts.NEUndefined), typeFromTypeNode);
        }
        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:9144-9172 — <c>serializeReturnTypeForSignature</c>.</summary>
    /// <remarks>
    /// Serializes the return type of the signature by first trying to use the syntactic printer if possible and
    /// falling back to the checker type if not.
    /// </remarks>
    internal ITypeNode? SerializeReturnTypeForSignature(NodeBuilderContext context, ISignature signature)
    {
        var suppressAny = (context.Flags & NodeBuilderFlags.SuppressAnyReturnType) != 0;
        var restoreFlags = SaveRestoreFlags(context);
        if (suppressAny) context.Flags &= ~NodeBuilderFlags.SuppressAnyReturnType; // suppress only toplevel `any`s
        ITypeNode? returnTypeNode = null;

        var returnType = GetReturnTypeOfSignature(signature);
        if (!(suppressAny && IsTypeAny(returnType)))
        {
            if (signature.Declaration is not null && !Utilities.NodeIsSynthesized(signature.Declaration) && !CanPossiblyExpandType(returnType, context))
            {
                var declarationSymbol = GetSymbolOfDeclaration((IDeclaration)signature.Declaration);
                var restore = AddSymbolTypeToContext(context, declarationSymbol, returnType);
                returnTypeNode = SyntacticNodeBuilder.SerializeReturnTypeForSignature!(signature.Declaration!, declarationSymbol, context);
                restore();
            }
            if (returnTypeNode is null)
            {
                returnTypeNode = SerializeInferredReturnTypeForSignature(context, signature, returnType);
            }
        }

        if (returnTypeNode is null && !suppressAny)
        {
            returnTypeNode = _factory.CreateKeywordTypeNode(SyntaxKind.AnyKeyword);
        }
        restoreFlags();
        return returnTypeNode;
    }

    /// <summary>ported from: src/compiler/checker.ts:9173-9183 — <c>serializeInferredReturnTypeForSignature</c>.</summary>
    /// <remarks>
    /// Serializes the checker type of the signature (does not use the syntactic printer).
    /// <para>
    /// PORT-NOTE: TS's <c>context.mapper ? instantiateTypePredicate(typePredicate, context.mapper) : typePredicate</c>
    /// reads the mapper out of its <c>object?</c> slot with the landed <c>(TypeMapper?)</c> cast (contract §3).
    /// </para>
    /// </remarks>
    internal ITypeNode? SerializeInferredReturnTypeForSignature(NodeBuilderContext context, ISignature signature, IType returnType)
    {
        var oldSuppressReportInferenceFallback = context.SuppressReportInferenceFallback;
        context.SuppressReportInferenceFallback = true;
        var typePredicate = GetTypePredicateOfSignature(signature);
        var mapper = (TypeMapper?)context.Mapper;
        var returnTypeNode = typePredicate is not null
            ? TypePredicateToTypePredicateNodeHelper(mapper is not null ? InstantiateTypePredicate(typePredicate, mapper) : typePredicate, context)
            : TypeToTypeNodeHelper(returnType, context);
        context.SuppressReportInferenceFallback = oldSuppressReportInferenceFallback;
        return returnTypeNode;
    }

    /// <summary>ported from: src/compiler/checker.ts:9184-9274 — <c>trackExistingEntityName</c> (with the nested <c>attachSymbolToLeftmostIdentifier</c>, checker.ts:9263-9273).</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's default parameter <c>enclosingDeclaration = context.enclosingDeclaration</c> cannot be a C#
    /// default (it is not a constant), so the port declares <c>null</c> and resolves it with <c>??=</c>; the reference
    /// applies its default for an omitted *and* an explicit <c>undefined</c> argument, which is exactly what <c>??=</c>
    /// does.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS answers the record <c>{ introducesError, node, sym? }</c>; §7.5 pinned this member to <c>T</c>, but
    /// the record's <c>introducesError</c> does have a reader in the port — <c>SanitizeJSDocImplements</c> (agent F's
    /// <c>NodeBuilder.Serialize.cs</c>, checker.ts:10271-10276), which must drop the heritage clause it cannot
    /// serialize — so the two fields TS's callers read are kept as the tuple <c>(bool IntroducesError, T Node)</c>.
    /// The record's third field, <c>sym</c>, has no reader at all and is not carried. Every arm's node half and flag are
    /// the reference's: the three early arms answer the original node with <c>true</c>, the tail arms answer the
    /// attached/cloned one with <c>false</c>. The resolver forwarder for the name (checker.ts:6410, the coordinator's
    /// slice) keeps <c>object?</c> over this call, which boxes the tuple and needs no change.
    /// </para>
    /// </remarks>
    internal (bool IntroducesError, T Node) TrackExistingEntityName<T>(T node, NodeBuilderContext context, Node? enclosingDeclaration = null) where T : Node
    {
        enclosingDeclaration ??= context.EnclosingDeclaration;
        var leftmost = Utilities.GetFirstIdentifier(node);
        if (Utilities.IsInJSFile(node) &&
            (Utilities.IsExportsIdentifier(leftmost) ||
             Utilities.IsModuleExportsAccessExpression(leftmost.Parent!) ||
             (NodeTests.IsQualifiedName(leftmost.Parent!) &&
              Utilities.IsModuleIdentifier(((QualifiedName)leftmost.Parent!).Left!) &&
              Utilities.IsExportsIdentifier(((QualifiedName)leftmost.Parent!).Right!))))
        {
            return (true, node);
        }
        var meaning = GetMeaningOfEntityNameReference(node);
        ISymbol? sym;
        if (Utilities.IsThisIdentifier(leftmost))
        {
            // `this` isn't a bindable identifier - skip resolution, find a relevant `this` symbol directly and avoid exhaustive scope traversal
            sym = GetSymbolOfDeclaration((IDeclaration)Utilities.GetThisContainer(leftmost, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false));
            if (IsSymbolAccessible(sym, leftmost, meaning, /*shouldComputeAliasesToMakeVisible*/ false).Accessibility != SymbolAccessibility.Accessible)
            {
                context.Tracker!.ReportInaccessibleThisError();
            }
            return (true, AttachSymbolToLeftmostIdentifier(node));
        }
        sym = ResolveEntityName(leftmost, meaning, /*ignoreErrors*/ true, /*dontResolveAlias*/ true);
        if (enclosingDeclaration is not null &&
            !(sym is not null && (sym.Flags & SymbolFlags.TypeParameter) != 0))
        {
            sym = GetExportSymbolOfValueSymbolIfExported(sym);
            // Some declarations may be transplanted to a new location.
            // When this happens we need to make sure that the name has the same meaning at both locations
            // We also check for the unknownSymbol because when we create a fake scope some parameters may actually not be usable
            // either because they are the expanded rest parameter,
            // or because they are the newly added parameters from the tuple, which might have different meanings in the original context
            var symAtLocation = ResolveEntityName(leftmost, meaning, /*ignoreErrors*/ true, /*dontResolveAlias*/ true, enclosingDeclaration);
            if (
                // Check for unusable parameters symbols
                ReferenceEquals(symAtLocation, _unknownSymbol) ||
                // If the symbol is not found, but was not found in the original scope either we probably have an error, don't reuse the node
                (symAtLocation is null && sym is not null) ||
                // If the symbol is found both in declaration scope and in current scope then it should point to the same reference
                (symAtLocation is not null && sym is not null && GetSymbolIfSameReference(GetExportSymbolOfValueSymbolIfExported(symAtLocation), sym) is null))
            {
                // In isolated declaration we will not do rest parameter expansion so there is no need to report on these.
                if (!ReferenceEquals(symAtLocation, _unknownSymbol))
                {
                    context.Tracker!.ReportInferenceFallback(node);
                }
                return (true, node);
            }
            else
            {
                sym = symAtLocation;
            }
        }

        if (sym is not null)
        {
            // If a parameter is resolvable in the current context it is also visible, so no need to go to symbol accesibility
            if ((sym.Flags & SymbolFlags.FunctionScopedVariable) != 0 && sym.ValueDeclaration is not null)
            {
                if (Utilities.IsPartOfParameterDeclaration((Node)sym.ValueDeclaration) || NodeTests.IsJSDocParameterTag((Node)sym.ValueDeclaration))
                {
                    return (false, AttachSymbolToLeftmostIdentifier(node));
                }
            }
            if ((sym.Flags & SymbolFlags.TypeParameter) == 0 && // Type parameters are visible in the current context if they are are resolvable
                !Utilities.IsDeclarationName(node) &&
                IsSymbolAccessible(sym, enclosingDeclaration, meaning, /*shouldComputeAliasesToMakeVisible*/ false).Accessibility != SymbolAccessibility.Accessible)
            {
                context.Tracker!.ReportInferenceFallback(node);
            }
            else
            {
                context.Tracker!.TrackSymbol(sym, enclosingDeclaration, meaning);
            }
            return (false, AttachSymbolToLeftmostIdentifier(node));
        }

        return (false, node);

        /// <summary>ported from: src/compiler/checker.ts:9263-9273 — <c>attachSymbolToLeftmostIdentifier</c>.</summary>
        /// <remarks>
        /// Attaches a <c>.symbol</c> member to an identifier, cloning it to do so, so symbol information is smuggled
        /// out for symbol display information. TS's <c>attachSymbolToLeftmostIdentifier(node) as T</c> is the port's
        /// <c>(T)(Node)…</c> (contract §3: <c>INode</c> is not <c>Node</c>, and the caller's <c>T</c> is a node class).
        /// </remarks>
        T AttachSymbolToLeftmostIdentifier(Node node)
        {
            if (ReferenceEquals(node, leftmost))
            {
                var type = GetDeclaredTypeOfSymbol(sym!);
                var name = (sym!.Flags & SymbolFlags.TypeParameter) != 0
                    ? TypeParameterToName((ITypeParameter)type, context)
                    : _factory.CloneNode((Identifier)node);
                name.Symbol = sym!; // for quickinfo, which uses identifier symbol information
                return (T)(Node)SetTextRange(context, EmitNodes.SetEmitFlags(name, EmitFlags.NoAsciiEscaping), node);
            }
        // PORT-NOTE: the walker is visitorPublic.ts's visitEachChild (aliased at checker.ts:1134), landed as
        // Utilities.VisitEachChildWorker with the M7 visitor slice.
            var updated = Utilities.VisitEachChildWorker(node, c => AttachSymbolToLeftmostIdentifier(c), /*context*/ null);
            return (T)(Node)SetTextRange(context, updated, node);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:9276-9284 — <c>serializeTypeName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types <c>node</c> as <c>EntityName</c> and answers <c>TypeNode | undefined</c>; the port widens the
    /// parameter to <see cref="Node"/> (§12.5 — <c>EntityName</c> is <c>Identifier | QualifiedName</c>, which have no
    /// C# common type) and keeps <see cref="ITypeNode"/> as the answer.
    /// </remarks>
    internal ITypeNode? SerializeTypeName(NodeBuilderContext context, Node node, bool? isTypeOf = null, IList<ITypeNode>? typeArguments = null)
    {
        var meaning = isTypeOf == true ? SymbolFlags.Value : SymbolFlags.Type;
        var symbol = ResolveEntityName(node, meaning, /*ignoreErrors*/ true);
        if (symbol is null) return null;
        var resolvedSymbol = (symbol.Flags & SymbolFlags.Alias) != 0 ? ResolveAlias(symbol) : symbol;
        if (IsSymbolAccessible(symbol, context.EnclosingDeclaration, meaning, /*shouldComputeAliasesToMakeVisible*/ false).Accessibility != SymbolAccessibility.Accessible) return null;
        return SymbolToTypeNode(resolvedSymbol, context, meaning, typeArguments);
    }

    /// <summary>ported from: src/compiler/checker.ts:9285-9330 — <c>canReuseTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>void getTypeFromImportTypeNode(existing)</c> keeps the bare call whose only purpose is the
    /// resolved-symbol side effect the reference's <c>void</c> documents.
    /// </remarks>
    internal bool CanReuseTypeNode(NodeBuilderContext context, ITypeNode existing)
    {
        var type = GetTypeFromTypeNode(context, existing, /*noMappedTypes*/ true);
        if (type is null)
        {
            return false;
        }
        if (Utilities.IsInJSFile((Node)existing))
        {
            if (Utilities.IsLiteralImportTypeNode((Node)existing))
            {
                // Ensure resolvedSymbol is present
                GetTypeFromImportTypeNode((ImportTypeNode)existing);
                var nodeSymbol = GetNodeLinks((Node)existing).ResolvedSymbol;
                return
                    nodeSymbol is null ||
                    !(
                        // The import type resolved using jsdoc fallback logic
                        (!((ImportTypeNode)existing).IsTypeOf && (nodeSymbol.Flags & SymbolFlags.Type) == 0) ||
                        // The import type had type arguments autofilled by js fallback logic
                        Core.Length(((ImportTypeNode)existing).TypeArguments) < GetMinTypeArgumentCount(GetLocalTypeParametersOfClassOrInterfaceOrTypeAlias(nodeSymbol))
                    );
            }
        }
        if (NodeTests.IsTypeReferenceNode((Node)existing))
        {
            if (Utilities.IsConstTypeReference((Node)existing)) return false;
            var symbol = GetNodeLinks((Node)existing).ResolvedSymbol;
            if (symbol is null) return false;
            if ((symbol.Flags & SymbolFlags.TypeParameter) != 0)
            {
                var declaredType = GetDeclaredTypeOfSymbol(symbol);
                var mapper = (TypeMapper?)context.Mapper;
                return !(mapper is not null && !ReferenceEquals(GetMappedType(declaredType, mapper), declaredType));
            }
            if (Utilities.IsInJSDoc((Node)existing))
            {
                return ExistingTypeNodeIsNotReferenceOrIsReferenceWithCompatibleTypeArgumentCount(existing, type)
                    && GetIntendedTypeFromJSDocTypeReference((TypeReferenceNode)existing) is null // We should probably allow the reuse of JSDoc reference types such as String Number etc
                    && (symbol.Flags & SymbolFlags.Type) != 0; // JSDoc type annotations can reference values (meaning typeof value) as well as types. We only reuse type nodes
            }
        }
        if (
            NodeTests.IsTypeOperatorNode((Node)existing) &&
            ((TypeOperatorNode)existing).Operator == SyntaxKind.UniqueKeyword &&
            ((TypeOperatorNode)existing).Type!.Kind == SyntaxKind.SymbolKeyword
        )
        {
            var effectiveEnclosingContext = context.EnclosingDeclaration is not null ? GetEnclosingDeclarationIgnoringFakeScope(context.EnclosingDeclaration) : null;
            return Utilities.FindAncestor((Node)existing, n => ReferenceEquals(n, effectiveEnclosingContext)) is not null;
        }
        return true;
    }

    /// <summary>ported from: src/compiler/checker.ts:9331-9345 — <c>serializeExistingTypeNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>someType(type, …)</c> takes a non-optional type; the port's
    /// <see cref="GetTypeFromTypeNode"/> answers <c>IType?</c> (the mapped type can be absent), so the call carries the
    /// null-forgiving <c>!</c> the reference's overload resolution supplies.
    /// </remarks>
    internal ITypeNode? SerializeExistingTypeNode(NodeBuilderContext context, ITypeNode typeNode, bool addUndefined)
    {
        var type = GetTypeFromTypeNode(context, typeNode);
        if (
            addUndefined &&
            !SomeType(type!, t => (t.Flags & TypeFlags.Undefined) != 0) &&
            CanReuseTypeNode(context, typeNode)
        )
        {
            var clone = SyntacticNodeBuilder.TryReuseExistingTypeNode!(context, typeNode);
            if (clone is not null)
            {
                return _factory.CreateUnionTypeNode(new List<ITypeNode> { clone, _factory.CreateKeywordTypeNode(SyntaxKind.UndefinedKeyword) });
            }
        }
        return TypeToTypeNodeHelper(type!, context);
    }
}
