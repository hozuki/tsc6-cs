namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:2232-2244 (DuplicateInfoForSymbol, DuplicateInfoForFiles, amalgamatedDuplicates)
// ported from: src/compiler/checker.ts:2356 (mergedSymbols)
// ported from: src/compiler/checker.ts:2633-2930 (createSymbol … addUndefinedToGlobalsOrErrorOnRedeclaration)
// ported from: src/compiler/checker.ts:2943-2980 (getSymbol, getSymbolsOfParameterPropertyDeclaration)
// ported from: src/compiler/checker.ts:5307-5312 (getMergedSymbol)
// ported from: src/compiler/utilities.ts:8040-8042 (getCheckFlags)
/// <summary>
/// The symbols the checker synthesizes: <c>createSymbol</c> and the merge machinery that folds a declaration's
/// symbol into an existing one.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: <c>mergeSymbol</c> is the checker's counterpart of the binder's own merge
/// (<c>Binder/Binder.Symbols.cs</c>): the binder merges within one file, the checker across files and module
/// augmentations. It reads the diagnostics single point (<c>Checker.Diagnostics.cs</c>) and the symbol printers
/// (<c>Checker.NodeBuilder.cs</c>, M6-6) — which is why this file's members call <c>SymbolToString</c> and
/// <c>Utilities.*</c> names this slice does not declare; the slice report lists each one with its owner.
/// </para>
/// <para>
/// PORT-NOTE: <c>getCheckFlags</c> is a <c>utilities.ts</c> member upstream (utilities.ts:8040-8042) but is stated
/// here as a member of this class, because every call site is inside the checker and writes an unqualified
/// <c>getCheckFlags(…)</c>; a <c>Utilities.GetCheckFlags</c> would make every ported call site read differently from
/// the reference. M6-3/M6-4/M6-5 call it as <c>GetCheckFlags(…)</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>nextMergeId</c> is module-level in the reference (<c>let nextMergeId = 1</c>, checker.ts:1171) — it is
/// shared by every checker instance, exactly like <c>getSymbolId</c>'s counter — so the port declares it
/// <c>static</c>. <c>mergedSymbols</c> (checker.ts:2356) and <c>amalgamatedDuplicates</c> (checker.ts:2244) are
/// instance state and are declared here because <c>recordMergedSymbol</c>/<c>mergeSymbol</c> are their writers.
/// </para>
/// <para>
/// PORT-NOTE: the reference's <c>symbol.mergeId</c> is <c>number | undefined</c> and its tests are truthiness tests
/// (<c>if (!source.mergeId)</c>, <c>symbol.mergeId &amp;&amp; …</c>); the port's <see cref="Symbol.MergeId"/> is a plain
/// <c>int</c> whose 0 is TS's <c>undefined</c> (ids start at 1), so every test here is against 0.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:1171 — <c>nextMergeId</c>.</summary>
    /// <remarks>PORT-NOTE: module-level (hence shared) in the reference; <c>static</c> here, and atomic — see
    /// <c>RecordMergedSymbol</c>. TS's <c>1</c> is an assign-then-increment first id, so the counter starts at
    /// <c>0</c>.</remarks>
    private static int s_nextMergeId;

    /// <summary>ported from: src/compiler/checker.ts:2356 — <c>mergedSymbols</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's sparse <c>Symbol[]</c> indexed by <c>mergeId</c>; the port pads a <c>List</c> with <c>null</c>
    /// up to the id, the same hole-punching <c>Checker.cs</c> uses for the two link tables.
    /// </remarks>
    private readonly List<ISymbol?> _mergedSymbols = [];

    /// <summary>ported from: src/compiler/checker.ts:2244 — <c>amalgamatedDuplicates</c>.</summary>
    /// <remarks>PORT-NOTE: keyed by <c>"/path/to/a.ts|/path/to/b.ts"</c>; the inner map is keyed by symbol name.</remarks>
    internal Dictionary<string, DuplicateInfoForFiles>? _amalgamatedDuplicates;

    /// <summary>ported from: src/compiler/checker.ts:2232-2236 — <c>DuplicateInfoForSymbol</c>.</summary>
    /// <remarks>PORT-NOTE: TS declares an interface with <c>readonly</c> slots and builds it as an object literal; the port needs a constructible class, the same treatment <c>IndexInfo</c> got.</remarks>
    internal sealed class DuplicateInfoForSymbol
    {
        public List<Node> FirstFileLocations { get; } = [];
        public List<Node> SecondFileLocations { get; } = [];
        public bool IsBlockScoped { get; set; }
    }

    /// <summary>ported from: src/compiler/checker.ts:2237-2243 — <c>DuplicateInfoForFiles</c>.</summary>
    /// <remarks>PORT-NOTE: as <see cref="DuplicateInfoForSymbol"/>; the two files are set once at the literal.</remarks>
    internal sealed class DuplicateInfoForFiles
    {
        public required SourceFile FirstFile { get; init; }
        public required SourceFile SecondFile { get; init; }

        /// <summary>Key is symbol name.</summary>
        public Dictionary<string, DuplicateInfoForSymbol> ConflictingSymbols { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>ported from: src/compiler/checker.ts:2633-2638 — <c>createSymbol</c>.</summary>
    internal TransientSymbol CreateSymbol(SymbolFlags flags, string name, CheckFlags checkFlags = CheckFlags.None)
    {
        _symbolCount++;
        var symbol = new TransientSymbol(flags | SymbolFlags.Transient, name);
        symbol.Links = new SymbolLinks();
        // PORT-NOTE: TS writes `checkFlags || CheckFlags.None`, i.e. a zero/absent flag becomes None; the port's
        // default parameter already is None, so the two are the same value here.
        symbol.Links.CheckFlags = checkFlags;
        return symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:2641-2645 — <c>createParameter</c>.</summary>
    internal ISymbol CreateParameter(string name, IType type)
    {
        var symbol = CreateSymbol(SymbolFlags.FunctionScopedVariable, name);
        GetSymbolLinks(symbol).Type = type;
        return symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:2647-2651 — <c>createProperty</c>.</summary>
    internal ISymbol CreateProperty(string name, IType type)
    {
        var symbol = CreateSymbol(SymbolFlags.Property, name);
        GetSymbolLinks(symbol).Type = type;
        return symbol;
    }

    /// <summary>ported from: src/compiler/checker.ts:2653-2665 — <c>getExcludedSymbolFlags</c>.</summary>
    internal static SymbolFlags GetExcludedSymbolFlags(SymbolFlags flags)
    {
        var result = SymbolFlags.None;
        if ((flags & SymbolFlags.BlockScopedVariable) != 0) result |= SymbolFlags.BlockScopedVariableExcludes;
        if ((flags & SymbolFlags.FunctionScopedVariable) != 0) result |= SymbolFlags.FunctionScopedVariableExcludes;
        if ((flags & SymbolFlags.Property) != 0) result |= SymbolFlags.PropertyExcludes;
        if ((flags & SymbolFlags.EnumMember) != 0) result |= SymbolFlags.EnumMemberExcludes;
        if ((flags & SymbolFlags.Function) != 0) result |= SymbolFlags.FunctionExcludes;
        if ((flags & SymbolFlags.Class) != 0) result |= SymbolFlags.ClassExcludes;
        if ((flags & SymbolFlags.Interface) != 0) result |= SymbolFlags.InterfaceExcludes;
        if ((flags & SymbolFlags.RegularEnum) != 0) result |= SymbolFlags.RegularEnumExcludes;
        if ((flags & SymbolFlags.ConstEnum) != 0) result |= SymbolFlags.ConstEnumExcludes;
        if ((flags & SymbolFlags.ValueModule) != 0) result |= SymbolFlags.ValueModuleExcludes;
        if ((flags & SymbolFlags.Method) != 0) result |= SymbolFlags.MethodExcludes;
        if ((flags & SymbolFlags.GetAccessor) != 0) result |= SymbolFlags.GetAccessorExcludes;
        if ((flags & SymbolFlags.SetAccessor) != 0) result |= SymbolFlags.SetAccessorExcludes;
        if ((flags & SymbolFlags.TypeParameter) != 0) result |= SymbolFlags.TypeParameterExcludes;
        if ((flags & SymbolFlags.TypeAlias) != 0) result |= SymbolFlags.TypeAliasExcludes;
        if ((flags & SymbolFlags.Alias) != 0) result |= SymbolFlags.AliasExcludes;
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:2674-2680 — <c>recordMergedSymbol</c>.</summary>
    internal void RecordMergedSymbol(ISymbol target, ISymbol source)
    {
        // PORT-NOTE (concurrency): `source.mergeId ??= nextMergeId++` (checker.ts:2676-2679) assumes one thread; a
        // merged symbol is reachable from every shard, so the id comes from an atomic counter and the slot write is a
        // compare-exchange, exactly as <c>Utilities.GetSymbolId</c> does. Each destination symbol's id still indexes
        // this checker's own <c>_mergedSymbols</c> table, which is why the id must be assigned once, globally.
        var mergeId = source.MergeId;
        if (mergeId == 0)
        {
            mergeId = Interlocked.Increment(ref s_nextMergeId);
            if (source is Symbol concrete)
            {
                var winner = Interlocked.CompareExchange(ref concrete.MergeIdSlot, mergeId, 0);
                if (winner != 0) mergeId = winner;
            }
            else
            {
                source.MergeId = mergeId;
            }
        }

        while (_mergedSymbols.Count <= mergeId)
        {
            _mergedSymbols.Add(null);
        }

        _mergedSymbols[mergeId] = target;
    }

    /// <summary>ported from: src/compiler/checker.ts:2682-2696 — <c>cloneSymbol</c>.</summary>
    internal TransientSymbol CloneSymbol(ISymbol symbol)
    {
        var result = CreateSymbol(symbol.Flags, symbol.EscapedName ?? "");
        result.Declarations = symbol.Declarations is null ? [] : [.. symbol.Declarations];
        result.Parent = symbol.Parent;
        if (symbol.ValueDeclaration is not null) result.ValueDeclaration = symbol.ValueDeclaration;
        if (symbol.ConstEnumOnlyModule == true) result.ConstEnumOnlyModule = true;
        if (symbol.Members is not null) result.Members = new SymbolTable(symbol.Members);
        if (symbol.Exports is not null) result.Exports = new SymbolTable(symbol.Exports);
        RecordMergedSymbol(result, symbol);
        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:5307-5312 — <c>getMergedSymbol</c>.</summary>
    internal ISymbol? GetMergedSymbol(ISymbol? symbol)
        => symbol is not null && symbol.MergeId != 0 && _mergedSymbols.Count > symbol.MergeId && _mergedSymbols[symbol.MergeId] is { } merged
            ? merged
            : symbol;

    /// <summary>
    /// ported from: src/compiler/checker.ts:2698-2798 — <c>mergeSymbol</c>.
    /// </summary>
    /// <remarks>
    /// Note: if target is transient, then it is mutable, and mergeSymbol with both mutate and return it.
    /// If target is not transient, mergeSymbol will produce a transient clone, mutate that and return it.
    /// <para>
    /// PORT-NOTE: the reference writes the declarations with <c>addRange(target.declarations, source.declarations)</c>;
    /// the port states the same two steps against the generated nullable slot (<c>Symbol.Declarations</c> is
    /// <c>List&lt;IDeclaration&gt;?</c>) — a null target list is materialized first, which is the only case where the two
    /// spellings could differ and TS's own <c>addRange</c> would throw there.
    /// </para>
    /// </remarks>
    internal ISymbol MergeSymbol(ISymbol target, ISymbol source, bool unidirectional = false)
    {
        if ((target.Flags & GetExcludedSymbolFlags(source.Flags)) == 0
            || ((source.Flags | target.Flags) & SymbolFlags.Assignment) != 0)
        {
            if (ReferenceEquals(source, target))
            {
                // This can happen when an export assigned namespace exports something also erroneously exported at the
                // top level. See `declarationFileNoCrashOnExtraExportModifier` for an example.
                return target;
            }

            if ((target.Flags & SymbolFlags.Transient) == 0)
            {
                var resolvedTarget = ResolveSymbol(target);
                if (ReferenceEquals(resolvedTarget, _unknownSymbol))
                {
                    return source;
                }

                if ((resolvedTarget!.Flags & GetExcludedSymbolFlags(source.Flags)) == 0
                    || ((source.Flags | resolvedTarget.Flags) & SymbolFlags.Assignment) != 0)
                {
                    target = CloneSymbol(resolvedTarget);
                }
                else
                {
                    ReportMergeSymbolError(target, source);
                    return source;
                }
            }

            // Javascript static-property-assignment declarations always merge, even though they are also values
            if ((source.Flags & SymbolFlags.ValueModule) != 0 && (target.Flags & SymbolFlags.ValueModule) != 0 && target.ConstEnumOnlyModule == true && source.ConstEnumOnlyModule != true)
            {
                // reset flag when merging instantiated module into value module that has only const enums
                target.ConstEnumOnlyModule = false;
            }

            target.Flags |= source.Flags;
            if (source.ValueDeclaration is not null)
            {
                Utilities.SetValueDeclaration(target, source.ValueDeclaration);
            }

            target.Declarations ??= [];
            if (source.Declarations is not null)
            {
                target.Declarations.AddRange(source.Declarations);
            }

            if (source.Members is not null)
            {
                target.Members ??= Utilities.CreateSymbolTable();
                MergeSymbolTable(target.Members, source.Members, unidirectional);
            }

            if (source.Exports is not null)
            {
                target.Exports ??= Utilities.CreateSymbolTable();
                MergeSymbolTable(target.Exports, source.Exports, unidirectional, target);
            }

            if (!unidirectional)
            {
                RecordMergedSymbol(target, source);
            }
        }
        else if ((target.Flags & SymbolFlags.NamespaceModule) != 0)
        {
            // Do not report an error when merging `var globalThis` with the built-in `globalThis`,
            // as we will already report a "Declaration name conflicts..." error, and this error
            // won't make much sense.
            if (!ReferenceEquals(target, _globalThisSymbol))
            {
                // PORT-NOTE (A″): printer-gated stringifier arguments are deferred — see Core/Diagnostic.cs's `DiagnosticArgument`.
                Error(
                    source.Declarations is not null ? (Node?)Utilities.GetNameOfDeclaration((Node)source.Declarations[0]) : null,
                    Diagnostics.Cannot_augment_module_0_with_value_exports_because_it_resolves_to_a_non_module_entity,
                    Diagnostic.DeferArg(() => SymbolToString(target)));
            }
        }
        else
        {
            ReportMergeSymbolError(target, source);
        }

        return target;

        void ReportMergeSymbolError(ISymbol target, ISymbol source)
        {
            var isEitherEnum = (target.Flags & SymbolFlags.Enum) != 0 || (source.Flags & SymbolFlags.Enum) != 0;
            var isEitherBlockScoped = (target.Flags & SymbolFlags.BlockScopedVariable) != 0 || (source.Flags & SymbolFlags.BlockScopedVariable) != 0;
            var message = isEitherEnum ? Diagnostics.Enum_declarations_can_only_merge_with_namespace_or_other_enum_declarations
                : isEitherBlockScoped ? Diagnostics.Cannot_redeclare_block_scoped_variable_0
                : Diagnostics.Duplicate_identifier_0;
            var sourceSymbolFile = source.Declarations is not null ? Utilities.GetSourceFileOfNode((Node)source.Declarations[0]) : null;
            var targetSymbolFile = target.Declarations is not null ? Utilities.GetSourceFileOfNode((Node)target.Declarations[0]) : null;

            var isSourcePlainJs = Utilities.IsPlainJsFile(sourceSymbolFile, _compilerOptions.CheckJs);
            var isTargetPlainJs = Utilities.IsPlainJsFile(targetSymbolFile, _compilerOptions.CheckJs);
            var symbolName = SymbolToString(source);

            // Collect top-level duplicate identifier errors into one mapping, so we can then merge their diagnostics if there are a bunch
            if (sourceSymbolFile is not null && targetSymbolFile is not null && _amalgamatedDuplicates is not null && !isEitherEnum && !ReferenceEquals(sourceSymbolFile, targetSymbolFile))
            {
                var firstFile = Paths.ComparePaths(sourceSymbolFile.Path ?? "", targetSymbolFile.Path ?? "") == Comparison.LessThan ? sourceSymbolFile : targetSymbolFile;
                var secondFile = ReferenceEquals(firstFile, sourceSymbolFile) ? targetSymbolFile : sourceSymbolFile;
                var filesDuplicates = Core.GetOrUpdate(_amalgamatedDuplicates, $"{firstFile.Path}|{secondFile.Path}", () => new DuplicateInfoForFiles { FirstFile = firstFile, SecondFile = secondFile });
                var conflictingSymbolInfo = Core.GetOrUpdate(filesDuplicates.ConflictingSymbols, symbolName, () => new DuplicateInfoForSymbol { IsBlockScoped = isEitherBlockScoped });
                if (!isSourcePlainJs) AddDuplicateLocations(conflictingSymbolInfo.FirstFileLocations, source);
                if (!isTargetPlainJs) AddDuplicateLocations(conflictingSymbolInfo.SecondFileLocations, target);
            }
            else
            {
                if (!isSourcePlainJs) AddDuplicateDeclarationErrorsForSymbols(source, message, symbolName, target);
                if (!isTargetPlainJs) AddDuplicateDeclarationErrorsForSymbols(target, message, symbolName, source);
            }
        }

        static void AddDuplicateLocations(List<Node> locs, ISymbol symbol)
        {
            if (symbol.Declarations is not null)
            {
                foreach (var decl in symbol.Declarations)
                {
                    Core.PushIfUnique(locs, (Node)decl);
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2800-2804 — <c>addDuplicateDeclarationErrorsForSymbols</c>.</summary>
    internal void AddDuplicateDeclarationErrorsForSymbols(ISymbol target, DiagnosticMessage message, string symbolName, ISymbol source)
    {
        Core.ForEach(target.Declarations, (IDeclaration node, int _) =>
        {
            AddDuplicateDeclarationError(node, message, symbolName, source.Declarations);
            return (object?)null;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:2806-2818 — <c>addDuplicateDeclarationError</c>.</summary>
    internal void AddDuplicateDeclarationError(IDeclaration node, DiagnosticMessage message, string symbolName, IList<IDeclaration>? relatedNodes)
    {
        var errorNode = (Node?)(Utilities.GetExpandoInitializer((Node)node, /*isPrototypeAssignment*/ false) is not null ? Utilities.GetNameOfExpando((Node)node) : Utilities.GetNameOfDeclaration((Node)node)) ?? (Node)node;
        var err = LookupOrIssueError(errorNode, message, symbolName);
        foreach (var relatedNode in relatedNodes ?? [])
        {
            var adjustedNode = (Node?)(Utilities.GetExpandoInitializer((Node)relatedNode, /*isPrototypeAssignment*/ false) is not null ? Utilities.GetNameOfExpando((Node)relatedNode) : Utilities.GetNameOfDeclaration((Node)relatedNode)) ?? (Node)relatedNode;
            if (ReferenceEquals(adjustedNode, errorNode)) continue;
            err.RelatedInformation ??= [];
            var leadingMessage = Utilities.CreateDiagnosticForNode(adjustedNode, Diagnostics._0_was_also_declared_here, symbolName);
            var followOnMessage = Utilities.CreateDiagnosticForNode(adjustedNode, Diagnostics.and_here);
            if (Core.Length(err.RelatedInformation) >= 5
                || Core.Some(err.RelatedInformation, r => Utilities.CompareDiagnostics((Diagnostic)r, followOnMessage) == Comparison.EqualTo || Utilities.CompareDiagnostics((Diagnostic)r, leadingMessage) == Comparison.EqualTo))
            {
                continue;
            }

            Utilities.AddRelatedInfo(err, Core.Length(err.RelatedInformation) == 0 ? leadingMessage : followOnMessage);
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2820-2827 — <c>combineSymbolTables</c>.</summary>
    internal SymbolTable? CombineSymbolTables(SymbolTable? first, SymbolTable? second)
    {
        if (first is null || first.Count == 0) return second;
        if (second is null || second.Count == 0) return first;
        var combined = Utilities.CreateSymbolTable();
        MergeSymbolTable(combined, first);
        MergeSymbolTable(combined, second);
        return combined;
    }

    /// <summary>ported from: src/compiler/checker.ts:2829-2855 — <c>mergeSymbolTable</c>.</summary>
    internal void MergeSymbolTable(SymbolTable target, SymbolTable source, bool unidirectional = false, ISymbol? mergedParent = null)
    {
        using var allocProbe = AllocationProbe.Enter("Checker.MergeSymbolTable");
        source.ForEach((sourceSymbol, id) =>
        {
            var targetSymbol = target.GetValueOrDefault(id);
            var merged = targetSymbol is not null ? MergeSymbol(targetSymbol, sourceSymbol, unidirectional) : GetMergedSymbol(sourceSymbol)!;
            if (mergedParent is not null && targetSymbol is not null)
            {
                // If a merge was performed on the target symbol, set its parent to the merged parent that initiated the
                // merge of its exports. Otherwise, `merged` came only from `sourceSymbol` and can keep its parent: when
                // merging a module augmentation into the augmented file, a symbol that was itself merged gets the
                // merged module symbol as its parent, while one with a single declaration keeps the augmentation
                // symbol, which contains its only declaration.
                if ((merged.Flags & SymbolFlags.Transient) != 0)
                {
                    merged.Parent = mergedParent;
                }
            }

            target[id] = merged;
        });
    }

    /// <summary>ported from: src/compiler/checker.ts:2898 — <c>patternAmbientModuleAugmentations</c>.</summary>
    /// <remarks>PORT-NOTE: TS types it <c>Map&lt;string, Symbol&gt;</c> and creates it lazily; the port's field starts null and is created on first write.</remarks>
    internal Dictionary<string, ISymbol>? _patternAmbientModuleAugmentations;

    /// <summary>ported from: src/compiler/checker.ts:2857-2913 — <c>mergeModuleAugmentation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>StringLiteral | Identifier</c> (the augmentation's name); the port
    /// takes <see cref="Node"/> and casts where the reference casts, because the two members it reads (<c>parent</c>,
    /// <c>text</c>) live on different faces.
    /// <para>
    /// PORT-NOTE: the module-resolution half (<c>resolveExternalModuleNameWorker</c>, <c>resolveExternalModuleSymbol</c>)
    /// and the <c>patternAmbientModules</c> list belong to the module/program slices; they are referenced, not declared,
    /// so this member only runs once those land.
    /// </para>
    /// </remarks>
    internal void MergeModuleAugmentation(Node moduleName)
    {
        var moduleAugmentation = (ModuleDeclaration)moduleName.Parent!;
        if (!ReferenceEquals(moduleAugmentation.Symbol!.Declarations?[0], moduleAugmentation))
        {
            // this is a combined symbol for multiple augmentations within the same file.
            // its symbol already has accumulated information for all declarations
            // so we need to add it just once - do the work only for first declaration
            Debug.Assert(moduleAugmentation.Symbol.Declarations!.Count > 1);
            return;
        }

        if (Utilities.IsGlobalScopeAugmentation(moduleAugmentation))
        {
            MergeSymbolTable(_globals, moduleAugmentation.Symbol.Exports!);
        }
        else
        {
            // find a module that about to be augmented
            // do not validate names of augmentations that are defined in ambient context
            var moduleNotFoundError = (((Node)moduleName.Parent!.Parent!).Flags & NodeFlags.Ambient) == 0
                ? Diagnostics.Invalid_module_name_in_augmentation_module_0_cannot_be_found
                : null;
            var mainModule = ResolveExternalModuleNameWorker(moduleName, (Expression)moduleName, moduleNotFoundError, /*ignoreErrors*/ false, /*isForAugmentation*/ true);
            if (mainModule is null)
            {
                return;
            }

            // obtain item referenced by 'export='
            mainModule = ResolveExternalModuleSymbol(mainModule);
            if ((mainModule!.Flags & SymbolFlags.Namespace) != 0)
            {
                // If we're merging an augmentation to a pattern ambient module, we want to
                // perform the merge unidirectionally from the augmentation ('a.foo') to
                // the pattern ('*.foo'), so that 'getMergedSymbol()' on a.foo gives you
                // all the exports both from the pattern and from the augmentation, but
                // 'getMergedSymbol()' on *.foo only gives you exports from *.foo.
                if (Core.Some(_patternAmbientModules, (IPatternAmbientModule module) => ReferenceEquals(mainModule, module.Symbol)))
                {
                    var merged = MergeSymbol(moduleAugmentation.Symbol, mainModule, /*unidirectional*/ true);
                    _patternAmbientModuleAugmentations ??= new Dictionary<string, ISymbol>(StringComparer.Ordinal);
                    // moduleName will be a StringLiteral since this is not `declare global`.
                    _patternAmbientModuleAugmentations[((StringLiteral)moduleName).Text ?? ""] = merged;
                }
                else
                {
                    if (mainModule.Exports?.GetValueOrDefault(InternalSymbolName.ExportStar.Value) is not null && (moduleAugmentation.Symbol.Exports?.Count ?? 0) != 0)
                    {
                        // We may need to merge the module augmentation's exports into the target symbols of the resolved exports
                        var resolvedExports = GetResolvedMembersOrExportsOfSymbol(mainModule, MembersOrExportsResolutionKind.ResolvedExports);
                        foreach (var (key, value) in moduleAugmentation.Symbol.Exports!)
                        {
                            if (resolvedExports.ContainsKey(key) && mainModule.Exports?.ContainsKey(key) != true)
                            {
                                MergeSymbol(resolvedExports[key], value);
                            }
                        }
                    }

                    MergeSymbol(mainModule, moduleAugmentation.Symbol);
                }
            }
            else
            {
                // moduleName will be a StringLiteral since this is not `declare global`.
                Error(moduleName, Diagnostics.Cannot_augment_module_0_because_it_resolves_to_a_non_module_entity, ((StringLiteral)moduleName).Text);
            }
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2916-2930 — <c>addUndefinedToGlobalsOrErrorOnRedeclaration</c>.</summary>
    internal void AddUndefinedToGlobalsOrErrorOnRedeclaration()
    {
        var undefinedSymbol = _undefinedSymbol!;
        var name = undefinedSymbol.EscapedName ?? "";
        var targetSymbol = _globals.GetValueOrDefault(name);
        if (targetSymbol is not null)
        {
            Core.ForEach(targetSymbol.Declarations, (IDeclaration declaration, int _) =>
            {
                // checkTypeNameIsReserved will have added better diagnostics for type declarations.
                // PORT-NOTE: `isTypeDeclaration` is a utilities.ts member (utilities.ts:11025), so it is called qualified.
                if (!Utilities.IsTypeDeclaration((Node)declaration))
                {
                    _diagnostics.Add(Utilities.CreateDiagnosticForNode((Node)declaration, Diagnostics.Declaration_name_conflicts_with_built_in_global_identifier_0, Utilities.UnescapeLeadingUnderscores(name)));
                }

                return (object?)null;
            });
        }
        else
        {
            _globals[name] = undefinedSymbol;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:2943-2966 — <c>getSymbol</c>.</summary>
    internal ISymbol? GetSymbol(SymbolTable symbols, string name, SymbolFlags meaning)
    {
        if (meaning != SymbolFlags.None)
        {
            var symbol = GetMergedSymbol(symbols.GetValueOrDefault(name));
            if (symbol is not null)
            {
                if ((symbol.Flags & meaning) != 0)
                {
                    return symbol;
                }

                if ((symbol.Flags & SymbolFlags.Alias) != 0)
                {
                    var targetFlags = GetSymbolFlags(symbol);
                    // `targetFlags` will be `SymbolFlags.All` if an error occurred in alias resolution; this avoids cascading errors
                    if ((targetFlags & meaning) != 0)
                    {
                        return symbol;
                    }
                }
            }
        }

        // return undefined if we can't find a symbol.
        return null;
    }

    /// <summary>
    /// ported from: src/compiler/checker.ts:2968-2980 — <c>getSymbolsOfParameterPropertyDeclaration</c>.
    /// </summary>
    /// <param name="parameter">a parameterDeclaration node</param>
    /// <param name="parameterName">a name of the parameter to get the symbols for.</param>
    /// <returns>the parameter's symbol first, then the property's.</returns>
    /// <remarks>
    /// PORT-NOTE: TS declares the return type as the tuple <c>[Symbol, Symbol]</c>; the generated <c>ITypeChecker</c>
    /// slot is <c>List&lt;ISymbol&gt;</c> (a tuple degrades to a list), so the port states that — the two elements are
    /// in the reference's order.
    /// </remarks>
    internal List<ISymbol> GetSymbolsOfParameterPropertyDeclaration(ParameterDeclaration? parameter, string? parameterName)
    {
        var constructorDeclaration = (ConstructorDeclaration)parameter!.Parent!;
        var classDeclaration = (ClassDeclaration)constructorDeclaration.Parent!;

        var parameterSymbol = GetSymbol(((ILocalsContainer)constructorDeclaration).Locals!, parameterName!, SymbolFlags.Value);
        var propertySymbol = GetSymbol(GetMembersOfSymbol(GetSymbolOfDeclaration(classDeclaration)), parameterName!, SymbolFlags.Value);

        if (parameterSymbol is not null && propertySymbol is not null)
        {
            return [parameterSymbol, propertySymbol];
        }

        return Debug.Fail<List<ISymbol>>("There should exist two symbols, one as property declaration and one as parameter declaration");
    }

    /// <summary>ported from: src/compiler/utilities.ts:8040-8042 — <c>getCheckFlags</c>.</summary>
    internal CheckFlags GetCheckFlags(ISymbol symbol)
        => (symbol.Flags & SymbolFlags.Transient) != 0 ? ((ITransientSymbol)symbol).Links?.CheckFlags ?? CheckFlags.None : CheckFlags.None;
}
