namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:9346-9899 (the head of `symbolTableToDeclarationStatements`: its frame, its
//              local cleanups, the symbol walk, and the per-symbol dispatch that hands each symbol meaning to the
//              serializers in `NodeBuilder.Serialize.cs`)
/// <summary>
/// The declaration-statement emitter of the node builder: it turns a symbol table into the statements a declaration
/// file would contain — <c>symbolTableToDeclarationStatements</c> (checker.ts:9346) with the whole nest of helpers it
/// owns through <c>addResult</c> (checker.ts:9899).
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: the reference's frame state is a set of closure variables of <c>symbolTableToDeclarationStatements</c>
/// (<c>results</c>, <c>visitedSymbols</c>, <c>deferredPrivatesStack</c>, <c>enclosingDeclaration</c>,
/// <c>addingDeclare</c>, and the reassigned <c>context</c>). The port keeps them as locals of the entry method and
/// threads the ones a helper reads through explicit parameters, because a C# local function cannot be called from a
/// sibling partial file and agent F's serializers (<c>NodeBuilder.Serialize.cs</c>, checker.ts:9900-11167) call
/// <c>addResult</c>, <c>includePrivateSymbol</c>, <c>visitSymbolTable</c> and <c>removeExportModifier</c> directly
/// (checker.ts:10070, :10222, :10234, :10413, :10671 and the twenty-one <c>addResult</c> sites). Those four are
/// therefore <c>internal</c> members; every helper called only from this file stays a local function
/// (<c>IsIdentifierAndNotUndefined</c>, <c>GetNamesOfDeclaration</c>, <c>FlattenExportAssignedNamespace</c>,
/// <c>MergeExportDeclarations</c>, <c>InlineExportModifiers</c>, <c>MergeRedundantStatements</c>,
/// <c>AddExportModifier</c>).
/// </para>
/// <para>
/// PORT-NOTE: TS's local <c>tracker</c> object literal (checker.ts:9360-9391) spreads <c>oldcontext.tracker.inner</c>
/// and overrides <c>trackSymbol</c>. C# has no structural tracker type, so the literal needs a class that
/// implements the closed <see cref="ISymbolTracker"/> face and forwards every member to the inner tracker — the
/// nested <see cref="SymbolTableToDeclarationStatementsTracker"/>, declared beside its only user — the placement the
/// port gives such literals, e.g. <c>Checker.InferenceContext</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>context = { ...oldcontext, usedSymbolNames, remappedSymbolNames, remappedSymbolReferences,
/// tracker }</c> (checker.ts:9353-9359) has no C# counterpart, and <see cref="NodeBuilderContext"/> is a landed file
/// this slice may not edit, so the copy lists every slot of the class in the same order the interface declares them.
/// </para>
/// <para>
/// PORT-NOTE: <c>serializePropertySymbolForClass</c>/<c>serializePropertySymbolForInterfaceWorker</c>
/// (checker.ts:9347-9348) are built here in the reference but used only inside agent F's range, which builds them
/// from its own <c>MakeSerializePropertySymbol</c>; declaring them here would be a second, unused copy. TS's
/// frame-local <c>symbolName(symbol)</c> helper is likewise not in this range and is not declared here.
/// </para>
/// <para>
/// PORT-NOTE: the only unresolved references are <c>Utilities.IsStringANonContextualKeyword(string)</c>
/// (utilities.ts:5291), <c>Utilities.CanHaveExportModifier(Node)</c> (:11048),
/// <c>Utilities.HasScopeMarker(IList&lt;IStatement&gt;)</c> (utilitiesPublic.ts:2138),
/// <c>Utilities.NeedsScopeMarker(IStatement)</c> (:2143) and <c>Utilities.IsExternalModuleIndicator(IStatement)</c>
/// (:2148) — all owned by <c>Utilities</c>/M6Utilities and not declared yet — and <c>IsConstantVariable</c>
/// (checker.ts:30389), which is M6-4's. They are the only diagnostics this file contributes (scoped build, see the
/// slice report). <c>Utilities.GetEffectiveModifierFlags(Node)</c> (utilities.ts:3436, landed at
/// <c>Utilities/Utilities.Checker.cs:1296</c>) and <c>ResolveExternalModuleName(Node, Expression?)</c>
/// (checker.ts:4716, landed at <c>Checker/Checker.SymbolSupport.cs:1081</c>) already resolve.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    /// <summary>ported from: src/compiler/checker.ts:9346-9407 — <c>symbolTableToDeclarationStatements</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter <c>context</c> is reassigned (checker.ts:9353) while <c>oldcontext</c> keeps the
    /// caller's context; the port reassigns the parameter the same way, exactly as the landed
    /// <c>WithContext</c>/<c>SymbolToDeclarationsWorker</c> pair does.
    /// </remarks>
    internal List<IStatement> SymbolTableToDeclarationStatements(SymbolTable symbolTable, NodeBuilderContext context)
    {
        // TS: const enclosingDeclaration = context.enclosingDeclaration!;
        var enclosingDeclaration = context.EnclosingDeclaration!;
        var results = new List<IStatement>();
        var visitedSymbols = new HashSet<int>();
        var deferredPrivatesStack = new List<Dictionary<int, ISymbol>>();
        var oldcontext = context;
        context = new NodeBuilderContext
        {
            EnclosingDeclaration = oldcontext.EnclosingDeclaration,
            EnclosingFile = oldcontext.EnclosingFile,
            Flags = oldcontext.Flags,
            InternalFlags = oldcontext.InternalFlags,
            Tracker = null,
            MaxExpansionDepth = oldcontext.MaxExpansionDepth,
            MaxTruncationLength = oldcontext.MaxTruncationLength,
            EncounteredError = oldcontext.EncounteredError,
            ReportedDiagnostic = oldcontext.ReportedDiagnostic,
            TrackedSymbols = oldcontext.TrackedSymbols,
            VisitedTypes = oldcontext.VisitedTypes,
            SymbolDepth = oldcontext.SymbolDepth,
            InferTypeParameters = oldcontext.InferTypeParameters,
            ApproximateLength = oldcontext.ApproximateLength,
            Truncating = oldcontext.Truncating,
            MustCreateTypeParameterSymbolList = oldcontext.MustCreateTypeParameterSymbolList,
            TypeParameterSymbolList = oldcontext.TypeParameterSymbolList,
            MustCreateTypeParametersNamesLookups = oldcontext.MustCreateTypeParametersNamesLookups,
            TypeParameterNames = oldcontext.TypeParameterNames,
            TypeParameterNamesByText = oldcontext.TypeParameterNamesByText,
            TypeParameterNamesByTextNextNameCount = oldcontext.TypeParameterNamesByTextNextNameCount,
            UsedSymbolNames = oldcontext.UsedSymbolNames is null ? new HashSet<string>() : new HashSet<string>(oldcontext.UsedSymbolNames),
            RemappedSymbolNames = [],
            RemappedSymbolReferences = oldcontext.RemappedSymbolReferences is null
                ? new Dictionary<int, ISymbol>()
                : new Dictionary<int, ISymbol>(oldcontext.RemappedSymbolReferences),
            EnclosingSymbolTypes = oldcontext.EnclosingSymbolTypes,
            ReverseMappedStack = oldcontext.ReverseMappedStack,
            Bundled = oldcontext.Bundled,
            Mapper = oldcontext.Mapper,
            Depth = oldcontext.Depth,
            SuppressReportInferenceFallback = oldcontext.SuppressReportInferenceFallback,
            TypeStack = oldcontext.TypeStack,
            Out = oldcontext.Out,
            NoInferenceFallback = oldcontext.NoInferenceFallback,
        };
        var tracker = new SymbolTableToDeclarationStatementsTracker(
            this,
            oldcontext.Tracker?.Inner,
            context,
            oldcontext.EnclosingDeclaration,
            symbol => IncludePrivateSymbol(symbol, context, deferredPrivatesStack));
        context.Tracker = new SymbolTrackerImpl(context, tracker, oldcontext.Tracker?.ModuleResolverHost);
        symbolTable.ForEach((symbol, name) =>
        {
            var baseName = Utilities.UnescapeLeadingUnderscores(name);
            // Called to cache values into `usedSymbolNames` and `remappedSymbolNames`
            _ = GetInternalSymbolName(symbol, baseName, context);
        });
        var addingDeclare = !context.Bundled;
        var exportEquals = symbolTable.GetValueOrDefault(InternalSymbolName.ExportEquals.Value);
        if (exportEquals is not null && symbolTable.Count > 1 && (exportEquals.Flags & (SymbolFlags.Alias | SymbolFlags.Module)) != 0)
        {
            symbolTable = Utilities.CreateSymbolTable();
            // Remove extraneous elements from root symbol table (they'll be mixed back in when the target of the
            // `export=` is looked up)
            symbolTable[InternalSymbolName.ExportEquals.Value] = exportEquals;
        }

        VisitSymbolTable(symbolTable, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        return MergeRedundantStatements(results);

        // ported from: src/compiler/checker.ts:9408-9411 — <c>isIdentifierAndNotUndefined</c>
        bool IsIdentifierAndNotUndefined(Node? node)
            => node is not null && node.Kind == SyntaxKind.Identifier;

        // ported from: src/compiler/checker.ts:9412-9418 — <c>getNamesOfDeclaration</c>
        List<Identifier> GetNamesOfDeclaration(IStatement statement)
        {
            if (NodeTests.IsVariableStatement((Node)statement))
            {
                var declarations = ((VariableStatement)(Node)statement).DeclarationList!.Declarations;
                return Core.MapDefined<VariableDeclaration, Identifier>(declarations, (d, _) =>
                {
                    var name = (Node?)Utilities.GetNameOfDeclaration(d);
                    return IsIdentifierAndNotUndefined(name) ? (Identifier)name! : null;
                });
            }

            var declarationName = (Node?)Utilities.GetNameOfDeclaration((Node)statement);
            return IsIdentifierAndNotUndefined(declarationName) ? [(Identifier)declarationName!] : [];
        }

        // ported from: src/compiler/checker.ts:9419-9468 — <c>flattenExportAssignedNamespace</c>
        /// <remarks>
        /// PORT-NOTE: the reference assigns the *frame* variable <c>results</c> (checker.ts:9458) and then reads it
        /// back at :9463; the port's local function assigns the captured local, which is the same variable.
        /// </remarks>
        List<IStatement> FlattenExportAssignedNamespace(List<IStatement> statements)
        {
            var exportAssignment = Core.Find(statements, (s, _) => NodeTests.IsExportAssignment((Node)s)) as ExportAssignment;
            var nsIndex = Core.FindIndex(statements, (s, _) => NodeTests.IsModuleDeclaration((Node)s));
            var ns = nsIndex != -1 ? (ModuleDeclaration)statements[nsIndex] : null;
            if (ns is not null
                && exportAssignment is not null
                && exportAssignment.IsExportEquals == true
                && NodeTests.IsIdentifier(exportAssignment.Expression!)
                && NodeTests.IsIdentifier(ns.Name!)
                && Utilities.IdText(ns.Name!) == Utilities.IdText(exportAssignment.Expression!)
                && ns.Body is ModuleBlock moduleBody)
            {
                // Pass 0: Correct situations where a module has both an `export = ns` and multiple top-level exports
                // by stripping the export modifiers from the top-level exports and exporting them in the targeted ns,
                // as can occur when a js file has both typedefs and `module.export` assignments
                var excessExports = Core.Filter(statements, s => (Utilities.GetEffectiveModifierFlags((Node)s) & ModifierFlags.Export) != 0)!;
                var name = (Identifier)ns.Name!;
                var body = moduleBody;
                if (Core.Length(excessExports) != 0)
                {
                    var excessSpecifiers = Core.Map(
                        Core.FlatMap<IStatement, Identifier>(excessExports, (e, _) => GetNamesOfDeclaration(e)),
                        (id, _) => _factory.CreateExportSpecifier(/*isTypeOnly*/ false, /*propertyName*/ null, id))!;
                    body = _factory.UpdateModuleBlock(
                        body,
                        _factory.CreateNodeArray<IStatement>(
                            [
                                .. body.Statements!,
                                _factory.CreateExportDeclaration(
                                    /*modifiers*/ null,
                                    /*isTypeOnly*/ false,
                                    _factory.CreateNamedExports(excessSpecifiers),
                                    /*moduleSpecifier*/ null,
                                    /*attributes*/ null),
                            ],
                            /*hasTrailingComma*/ null));
                    ns = _factory.UpdateModuleDeclaration(ns, ns.Modifiers, ns.Name, body);
                    statements = [.. statements.Take(nsIndex), ns, .. statements.Skip(nsIndex + 1)];
                }

                // Pass 1: Flatten `export namespace _exports {} export = _exports;` so long as the `export=` only
                // points at a single namespace declaration
                if (Core.Find(statements, (s, _) => !ReferenceEquals(s, ns) && Utilities.NodeHasName((Node)s, name)) is null)
                {
                    results = [];
                    // If the namespace contains no export assignments or declarations, and no declarations flagged
                    // with `export`, then _everything_ is exported - to respect this as the top level, we need to add
                    // an `export` modifier to everything
                    var mixinExportFlag = !Core.Some(body.Statements, s =>
                        Utilities.HasSyntacticModifier((Node)s, ModifierFlags.Export)
                        || NodeTests.IsExportAssignment((Node)s)
                        || NodeTests.IsExportDeclaration((Node)s));
                    foreach (var statement in body.Statements!)
                    {
                        // Recalculates the ambient (and export, if applicable from above) flag
                        AddResult(context, results, addingDeclare, (Node)statement, mixinExportFlag ? ModifierFlags.Export : ModifierFlags.None);
                    }

                    statements =
                    [
                        .. Core.Filter(statements, s => !ReferenceEquals(s, ns) && !ReferenceEquals(s, exportAssignment))!,
                        .. results,
                    ];
                }
            }

            return statements;
        }

        // ported from: src/compiler/checker.ts:9469-9507 — <c>mergeExportDeclarations</c>
        List<IStatement> MergeExportDeclarations(List<IStatement> statements)
        {
            // Pass 2: Combine all `export {}` declarations
            var exports = Core.Filter(statements, d =>
                NodeTests.IsExportDeclaration((Node)d)
                && ((ExportDeclaration)d).ModuleSpecifier is null
                && ((ExportDeclaration)d).ExportClause is not null
                && NodeTests.IsNamedExports(((ExportDeclaration)d).ExportClause!))!;
            if (exports.Count > 1)
            {
                var nonExports = Core.Filter(statements, d =>
                    !NodeTests.IsExportDeclaration((Node)d)
                    || ((ExportDeclaration)d).ModuleSpecifier is not null
                    || ((ExportDeclaration)d).ExportClause is null)!;
                statements =
                [
                    .. nonExports,
                    _factory.CreateExportDeclaration(
                        /*modifiers*/ null,
                        /*isTypeOnly*/ false,
                        _factory.CreateNamedExports((List<ExportSpecifier>)Core.FlatMap<IStatement, ExportSpecifier>(
                            exports,
                            (e, _) => ((NamedExports)((ExportDeclaration)e).ExportClause!).Elements)),
                        /*moduleSpecifier*/ null,
                        /*attributes*/ null),
                ];
            }

            // Pass 2b: Also combine all `export {} from "..."` declarations as needed
            var reexports = Core.Filter(statements, d =>
                NodeTests.IsExportDeclaration((Node)d)
                && ((ExportDeclaration)d).ModuleSpecifier is not null
                && ((ExportDeclaration)d).ExportClause is not null
                && NodeTests.IsNamedExports(((ExportDeclaration)d).ExportClause!))!;
            if (reexports.Count > 1)
            {
                var groups = Core.Group<IStatement, string, IList<IStatement>>(
                    reexports,
                    decl => ((ExportDeclaration)decl).ModuleSpecifier is StringLiteral specifier
                        ? ">" + specifier.Text!
                        : ">",
                    static group => group);
                if (groups.Count != reexports.Count)
                {
                    foreach (var group in groups)
                    {
                        if (group.Count > 1)
                        {
                            // remove group members from statements and then merge group members and add back to statements
                            statements =
                            [
                                .. Core.Filter(statements, s => !Core.Some(group, g => ReferenceEquals(g, s)))!,
                                _factory.CreateExportDeclaration(
                                    /*modifiers*/ null,
                                    /*isTypeOnly*/ false,
                                    _factory.CreateNamedExports((List<ExportSpecifier>)Core.FlatMap<IStatement, ExportSpecifier>(
                                        group,
                                        (e, _) => ((NamedExports)((ExportDeclaration)e).ExportClause!).Elements)),
                                    ((ExportDeclaration)group[0]).ModuleSpecifier,
                                    /*attributes*/ null),
                            ];
                        }
                    }
                }
            }

            return statements;
        }

        // ported from: src/compiler/checker.ts:9508-9549 — <c>inlineExportModifiers</c>
        List<IStatement> InlineExportModifiers(List<IStatement> statements)
        {
            // Pass 3: Move all `export {}`'s to `export` modifiers where possible
            var index = Core.FindIndex(statements, (d, _) =>
                NodeTests.IsExportDeclaration((Node)d)
                && ((ExportDeclaration)d).ModuleSpecifier is null
                && ((ExportDeclaration)d).Attributes is null
                && ((ExportDeclaration)d).ExportClause is not null
                && NodeTests.IsNamedExports(((ExportDeclaration)d).ExportClause!));
            if (index >= 0)
            {
                var exportDecl = (ExportDeclaration)statements[index];
                var namedExports = (NamedExports)exportDecl.ExportClause!;
                var replacements = Core.MapDefined<ExportSpecifier, ExportSpecifier>(namedExports.Elements, (e, _) =>
                {
                    if (e.PropertyName is null && e.Name!.Kind != SyntaxKind.StringLiteral)
                    {
                        // export {name} - look thru `statements` for `name`, and if all results can take an `export`
                        // modifier, do so and filter it
                        var name = (Identifier)e.Name!;
                        var indices = Core.IndicesOf(statements);
                        var associatedIndices = Core.Filter(indices, i => Utilities.NodeHasName((Node)statements[i], name))!;
                        if (associatedIndices.Count != 0 && Core.Every(associatedIndices, (i, _) => Utilities.CanHaveExportModifier((Node)statements[i])))
                        {
                            foreach (var associatedIndex in associatedIndices)
                            {
                                statements[associatedIndex] = AddExportModifier(statements[associatedIndex]);
                            }

                            return null;
                        }
                    }

                    return e;
                });
                if (replacements.Count == 0)
                {
                    // all clauses removed, remove the export declaration
                    Core.OrderedRemoveItemAt(statements, index);
                }
                else
                {
                    // some items filtered, others not - update the export declaration
                    statements[index] = _factory.UpdateExportDeclaration(
                        exportDecl,
                        exportDecl.Modifiers,
                        exportDecl.IsTypeOnly,
                        _factory.UpdateNamedExports(namedExports, replacements),
                        exportDecl.ModuleSpecifier,
                        exportDecl.Attributes);
                }
            }

            return statements;
        }

        // ported from: src/compiler/checker.ts:9550-9566 — <c>mergeRedundantStatements</c>
        List<IStatement> MergeRedundantStatements(List<IStatement> statements)
        {
            statements = FlattenExportAssignedNamespace(statements);
            statements = MergeExportDeclarations(statements);
            statements = InlineExportModifiers(statements);

            // Not a cleanup, but as a final step: If there is a mix of `export` and non-`export` declarations, but no
            // `export =` or `export {}` add a `export {};` so declaration privacy is respected.
            if (enclosingDeclaration is not null
                && ((NodeTests.IsSourceFile(enclosingDeclaration) && Utilities.IsExternalOrCommonJsModule((SourceFile)enclosingDeclaration))
                    || NodeTests.IsModuleDeclaration(enclosingDeclaration))
                && (!Core.Some(statements, s => Utilities.IsExternalModuleIndicator(s))
                    || (!Utilities.HasScopeMarker(statements) && Core.Some(statements, s => Utilities.NeedsScopeMarker(s)))))
            {
                statements.Add(FactoryUtilities.CreateEmptyExports(_factory));
            }

            return statements;
        }

        // ported from: src/compiler/checker.ts:9567-9571 — <c>addExportModifier</c>
        /// <remarks>PORT-NOTE: TS's <c>Extract&lt;HasModifiers, Statement&gt;</c> is <see cref="IStatement"/> here.</remarks>
        IStatement AddExportModifier(IStatement node)
            => (IStatement)_factory.ReplaceModifiers(
                (Node)node,
                (Utilities.GetEffectiveModifierFlags((Node)node) | ModifierFlags.Export) & ~ModifierFlags.Ambient);
    }

    // ported from: src/compiler/checker.ts:9572-9576 — <c>removeExportModifier</c>
    /// <remarks>
    /// PORT-NOTE: an <c>internal</c> member rather than a local function because agent F's <c>serializeAsNamespaceDeclaration</c>
    /// calls it across the file boundary (checker.ts:10234).
    /// PORT-NOTE: TS's <c>Extract&lt;HasModifiers, Statement&gt;</c> is <see cref="IStatement"/> here.
    /// </remarks>
    internal IStatement RemoveExportModifier(IStatement node)
        => (IStatement)_factory.ReplaceModifiers(
            (Node)node,
            Utilities.GetEffectiveModifierFlags((Node)node) & ~ModifierFlags.Export);

    // ported from: src/compiler/checker.ts:9577-9603 — <c>visitSymbolTable</c>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's <c>context</c>, <c>results</c>, <c>addingDeclare</c>, <c>visitedSymbols</c> and
    /// <c>deferredPrivatesStack</c> are closure variables; they are parameters here for the reason stated on the class
    /// (agent F calls this member at checker.ts:10222). <c>enclosingDeclaration</c> is the frame's saved root
    /// declaration, which TS's <c>serializeSymbol</c> reads (checker.ts:9615).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's second <c>forEach</c> iterates the top <c>Map</c> *while* it is being written (its own comment
    /// at :9594-9596 relies on a JS Map iterator seeing entries added during the walk), and C# forbids enumerating a
    /// dictionary that a nested call writes. The walk therefore keeps its own snapshot of the keys, re-checks it after
    /// every symbol and visits any key that appeared meanwhile — the same order and the same live-visibility.
    /// </para>
    /// </remarks>
    internal void VisitSymbolTable(
        SymbolTable symbolTable,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration,
        bool suppressNewPrivateContext = false,
        bool propertyAsAlias = false)
    {
        if (!suppressNewPrivateContext)
        {
            deferredPrivatesStack.Add([]);
        }

        var i = 0;
        var symbols = symbolTable.Values.ToList();
        foreach (var symbol in symbols)
        {
            i++;
            if (CheckTruncationLengthIfExpanding(context) && i + 2 < symbolTable.Count - 1)
            {
                context.Out!.Truncated = true;
                results.Add((IStatement)CreateTruncationStatement($"... ({symbolTable.Count - i} more ...)", context));
                SerializeSymbol(symbols[^1], /*isPrivate*/ false, propertyAsAlias, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                break;
            }

            SerializeSymbol(symbol, /*isPrivate*/ false, propertyAsAlias, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }

        if (!suppressNewPrivateContext)
        {
            // deferredPrivates will be filled up by visiting the symbol table
            // And will continue to iterate as elements are added while visited `deferredPrivates`
            // (As that's how a map iterator is defined to work)
            var privates = deferredPrivatesStack[^1];
            var keys = privates.Keys.ToList();
            for (var index = 0; index < keys.Count; index++)
            {
                SerializeSymbol(privates[keys[index]], /*isPrivate*/ true, propertyAsAlias, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                if (privates.Count != keys.Count)
                {
                    foreach (var key in privates.Keys)
                    {
                        if (!keys.Contains(key)) keys.Add(key);
                    }
                }
            }

            deferredPrivatesStack.RemoveAt(deferredPrivatesStack.Count - 1);
        }
    }

    // ported from: src/compiler/checker.ts:9604-9632 — <c>serializeSymbol</c>
    /// <remarks>
    /// PORT-NOTE: an <c>internal</c> member because <see cref="VisitSymbolTable"/> is one and a member cannot call a
    /// local function of another member. The frame values it reads from the closure (<c>visitedSymbols</c>,
    /// <c>enclosingDeclaration</c>) are parameters.
    /// </remarks>
    internal void SerializeSymbol(
        ISymbol symbol,
        bool isPrivate,
        bool propertyAsAlias,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        // resolve symbol's type and properties, which should trigger any required merges
        _ = GetPropertiesOfType(GetTypeOfSymbol(symbol));
        // cache visited list based on merged symbol, since we want to use the unmerged top-level symbol, but
        // still skip reserializing it if we encounter the merged product later on
        var visitedSym = GetMergedSymbol(symbol)!;
        if (visitedSymbols.Contains(Utilities.GetSymbolId(visitedSym)))
        {
            return; // Already printed
        }

        visitedSymbols.Add(Utilities.GetSymbolId(visitedSym));
        // Only actually serialize symbols within the correct enclosing declaration, otherwise do nothing with the
        // out-of-context symbol
        var skipMembershipCheck = !isPrivate; // We only call this on exported symbols when we know they're in the correct scope
        if (skipMembershipCheck
            || (Core.Length(symbol.Declarations) != 0
                && Core.Some(symbol.Declarations, d => Utilities.FindAncestor((Node)d, n => ReferenceEquals(n, enclosingDeclaration)) is not null)))
        {
            var scopeCleanup = CloneNodeBuilderContext(context);
            context.Tracker!.PushErrorFallbackNode(Core.Find(symbol.Declarations, (d, _) => ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), context.EnclosingFile)));
            SerializeSymbolWorker(symbol, isPrivate, propertyAsAlias, /*escapedSymbolName*/ null, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
            context.Tracker.PopErrorFallbackNode();
            scopeCleanup();
        }
    }

    // ported from: src/compiler/checker.ts:9633-9844 — <c>serializeSymbolWorker</c>
    /// <summary>
    /// Synthesize declarations for a symbol - might be an Interface, a Class, a Namespace, a Type, a Variable (const,
    /// let, or var), an Alias or a merge of some number of those.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS defaults <c>escapedSymbolName</c> to <c>symbol.escapedName</c>, which C# cannot express as a
    /// parameter default (it would read another parameter), so the port passes <c>null</c> for "not supplied" and
    /// applies the default as the first statement — the same call shape the reference has.
    /// </remarks>
    internal void SerializeSymbolWorker(
        ISymbol symbol,
        bool isPrivate,
        bool propertyAsAlias,
        string? escapedSymbolName,
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        HashSet<int> visitedSymbols,
        List<Dictionary<int, ISymbol>> deferredPrivatesStack,
        Node? enclosingDeclaration)
    {
        var escapedName = escapedSymbolName ?? symbol.EscapedName!;
        var symbolName = Utilities.UnescapeLeadingUnderscores(escapedName);
        var isDefault = escapedName == InternalSymbolName.Default.Value;
        if (isPrivate && (context.Flags & NodeBuilderFlags.AllowAnonymousIdentifier) == 0 && Utilities.IsStringANonContextualKeyword(symbolName) && !isDefault)
        {
            // Oh no. We cannot use this symbol's name as it's name... It's likely some jsdoc had an invalid name like
            // `export` or `default` :(
            context.EncounteredError = true;
            // If we need to emit a private with a keyword name, we're done for, since something else will try to
            // refer to it by that name
            return;
        }

        // An alias symbol should preclude needing to make an alias ourselves
        var needsPostExportDefault = isDefault
            && ((symbol.Flags & SymbolFlags.ExportDoesNotSupportDefaultModifier) != 0
                || ((symbol.Flags & SymbolFlags.Function) != 0 && Core.Length(GetPropertiesOfType(GetTypeOfSymbol(symbol))) != 0))
            && (symbol.Flags & SymbolFlags.Alias) == 0;
        var needsExportDeclaration = !needsPostExportDefault && !isPrivate && Utilities.IsStringANonContextualKeyword(symbolName) && !isDefault;
        // `serializeVariableOrProperty` will handle adding the export declaration if it is run (since
        // `getInternalSymbolName` will create the name mapping), so we need to ensure we unset
        // `needsExportDeclaration` if it is
        if (needsPostExportDefault || needsExportDeclaration)
        {
            isPrivate = true;
        }

        var modifierFlags = (!isPrivate ? ModifierFlags.Export : ModifierFlags.None)
            | (isDefault && !needsPostExportDefault ? ModifierFlags.Default : ModifierFlags.None);
        var isConstMergedWithNS = (symbol.Flags & SymbolFlags.Module) != 0
            && (symbol.Flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.FunctionScopedVariable | SymbolFlags.Property)) != 0
            && escapedName != InternalSymbolName.ExportEquals.Value;
        var isConstMergedWithNSPrintableAsSignatureMerge = isConstMergedWithNS
            && IsTypeRepresentableAsFunctionNamespaceMerge(GetTypeOfSymbol(symbol), symbol, context, enclosingDeclaration);
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0 || isConstMergedWithNSPrintableAsSignatureMerge)
        {
            SerializeAsFunctionNamespaceMerge(
                GetTypeOfSymbol(symbol),
                symbol,
                GetInternalSymbolName(symbol, symbolName, context),
                modifierFlags,
                context,
                results,
                addingDeclare,
                visitedSymbols,
                deferredPrivatesStack,
                enclosingDeclaration);
        }

        if ((symbol.Flags & SymbolFlags.TypeAlias) != 0)
        {
            SerializeTypeAlias(symbol, symbolName, modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }

        // Need to skip over export= symbols below - json source files get a single `Property` flagged
        // symbol of name `export=` which needs to be handled like an alias. It's not great, but it is what it is.
        if ((symbol.Flags & (SymbolFlags.BlockScopedVariable | SymbolFlags.FunctionScopedVariable | SymbolFlags.Property | SymbolFlags.Accessor)) != 0
            && escapedName != InternalSymbolName.ExportEquals.Value
            && (symbol.Flags & SymbolFlags.Prototype) == 0
            && (symbol.Flags & SymbolFlags.Class) == 0
            && (symbol.Flags & SymbolFlags.Method) == 0
            && !isConstMergedWithNSPrintableAsSignatureMerge)
        {
            if (propertyAsAlias)
            {
                var createdExport = SerializeMaybeAliasAssignment(symbol, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                if (createdExport)
                {
                    needsExportDeclaration = false;
                    needsPostExportDefault = false;
                }
            }
            else
            {
                var type = GetTypeOfSymbol(symbol);
                var localName = GetInternalSymbolName(symbol, symbolName, context);
                var typeSymbol = type.Symbol;
                if (typeSymbol is not null
                    && !ReferenceEquals(typeSymbol, symbol)
                    && (typeSymbol.Flags & SymbolFlags.Function) != 0
                    && Core.Some(typeSymbol.Declarations, d => Utilities.IsFunctionExpressionOrArrowFunction((Node)d))
                    && ((typeSymbol.Members?.Count ?? 0) != 0 || (typeSymbol.Exports?.Count ?? 0) != 0))
                {
                    // assignment of a anonymous expando/class-like function, the func/ns/merge branch below won't
                    // trigger, and the assignment form has to reference the unreachable anonymous type so will error.
                    // Instead, serialize the type's symbol, but with the current symbol's name, rather than the
                    // anonymous one.
                    context.RemappedSymbolReferences ??= new Dictionary<int, ISymbol>();
                    // save name remapping as local name for target symbol
                    context.RemappedSymbolReferences[Utilities.GetSymbolId(typeSymbol)] = symbol;
                    SerializeSymbolWorker(typeSymbol, isPrivate, propertyAsAlias, escapedName, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                    context.RemappedSymbolReferences.Remove(Utilities.GetSymbolId(typeSymbol));
                }
                else if ((symbol.Flags & SymbolFlags.Function) == 0 && IsTypeRepresentableAsFunctionNamespaceMerge(type, symbol, context, enclosingDeclaration))
                {
                    // If the type looks like a function declaration + ns could represent it, and it's type is sourced
                    // locally, rewrite it into a function declaration + ns
                    SerializeAsFunctionNamespaceMerge(type, symbol, localName, modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
                }
                else
                {
                    // A Class + Property merge is made for a `module.exports.Member = class {}`, and it doesn't
                    // serialize well as either a class _or_ a property symbol - in fact, _it behaves like an alias!_
                    // `var` is `FunctionScopedVariable`, `const` and `let` are `BlockScopedVariable`, and
                    // `module.exports.thing =` is `Property`
                    NodeFlags? flags;
                    if ((symbol.Flags & SymbolFlags.BlockScopedVariable) != 0)
                    {
                        flags = IsConstantVariable(symbol) ? NodeFlags.Const : NodeFlags.Let;
                    }
                    else if (symbol.Parent is { ValueDeclaration: { } parentValueDeclaration } && NodeTests.IsSourceFile((Node)parentValueDeclaration))
                    {
                        // exports are immutable in es6, which is what we emulate and check; so it's safe to mark all
                        // exports as `const` (there's no difference to consumers, but it allows unique symbol type
                        // declarations)
                        flags = NodeFlags.Const;
                    }
                    else
                    {
                        flags = null;
                    }

                    var name = needsPostExportDefault || (symbol.Flags & SymbolFlags.Property) == 0 ? localName : GetUnusedName(localName, symbol, context);
                    var textRange = (Node?)Core.Find(symbol.Declarations, (d, _) => NodeTests.IsVariableDeclaration((Node)d));
                    if (textRange?.Parent is { } parentDeclaration
                        && NodeTests.IsVariableDeclarationList(parentDeclaration)
                        && ((VariableDeclarationList)parentDeclaration).Declarations?.Count == 1)
                    {
                        textRange = parentDeclaration.Parent;
                    }

                    var propertyAccessRequire = Core.Find(symbol.Declarations, (d, _) => NodeTests.IsPropertyAccessExpression((Node)d)) as PropertyAccessExpression;
                    if (propertyAccessRequire is not null
                        && propertyAccessRequire.Parent is BinaryExpression binary
                        && NodeTests.IsIdentifier(binary.Right!)
                        && typeSymbol?.ValueDeclaration is { } typeValueDeclaration
                        && NodeTests.IsSourceFile((Node)typeValueDeclaration))
                    {
                        var right = (Identifier)binary.Right!;
                        var alias = localName == right.EscapedText ? null : right;
                        // `export { alias };`
                        context.ApproximateLength += 12 + (alias?.EscapedText?.Length ?? 0);
                        AddResult(
                            context,
                            results,
                            addingDeclare,
                            _factory.CreateExportDeclaration(
                                /*modifiers*/ null,
                                /*isTypeOnly*/ false,
                                _factory.CreateNamedExports([_factory.CreateExportSpecifier(/*isTypeOnly*/ false, alias, localName)]),
                                /*moduleSpecifier*/ null,
                                /*attributes*/ null),
                            ModifierFlags.None);
                        _ = context.Tracker!.TrackSymbol(typeSymbol, context.EnclosingDeclaration, SymbolFlags.Value);
                    }
                    else
                    {
                        var statement = SetTextRange(
                            context,
                            _factory.CreateVariableStatement(
                                /*modifiers*/ null,
                                _factory.CreateVariableDeclarationList(
                                    [
                                        _factory.CreateVariableDeclaration(
                                            name,
                                            /*exclamationToken*/ null,
                                            SerializeTypeForDeclaration(context, /*declaration*/ null, type, symbol),
                                            /*initializer*/ null),
                                    ],
                                    flags)),
                            textRange);
                        context.ApproximateLength += 7 + name.Length; // `var name: ;`
                        AddResult(context, results, addingDeclare, statement, name != localName ? modifierFlags & ~ModifierFlags.Export : modifierFlags);
                        if (name != localName && !isPrivate)
                        {
                            // We rename the variable declaration we generate for Property symbols since they may have a
                            // name which conflicts with a local declaration. For example, given input:
                            // ```
                            // function g() {}
                            // module.exports.g = g
                            // ```
                            // In such a situation, we have a local variable named `g`, and a separate exported
                            // variable named `g`. Naively, we would emit
                            // ```
                            // function g() {}
                            // export const g: typeof g;
                            // ```
                            // That's obviously incorrect - the `g` in the type annotation needs to refer to the local
                            // `g`, but the export declaration shadows it. To work around that, we instead write
                            // ```
                            // function g() {}
                            // const g_1: typeof g;
                            // export { g_1 as g };
                            // ```
                            // To create an export named `g` that does _not_ shadow the local `g`
                            context.ApproximateLength += 16 + name.Length + localName.Length; // `export { name as localName };`
                            AddResult(
                                context,
                                results,
                                addingDeclare,
                                _factory.CreateExportDeclaration(
                                    /*modifiers*/ null,
                                    /*isTypeOnly*/ false,
                                    _factory.CreateNamedExports([_factory.CreateExportSpecifier(/*isTypeOnly*/ false, name, localName)]),
                                    /*moduleSpecifier*/ null,
                                    /*attributes*/ null),
                                ModifierFlags.None);
                            needsExportDeclaration = false;
                            needsPostExportDefault = false;
                        }
                    }
                }
            }
        }

        if ((symbol.Flags & SymbolFlags.Enum) != 0)
        {
            SerializeEnum(symbol, symbolName, modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }

        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            if ((symbol.Flags & SymbolFlags.Property) != 0
                && symbol.ValueDeclaration is Node { Parent: BinaryExpression classAssignment }
                && NodeTests.IsClassExpression(classAssignment.Right!))
            {
                // Looks like a `module.exports.Sub = class {}` - if we serialize `symbol` as a class, the result will
                // have no members, since the classiness is actually from the target of the effective alias the symbol
                // is. yes. A BlockScopedVariable|Class|Property _really_ acts like an Alias, and none of a
                // BlockScopedVariable, Class, or Property. This is the travesty of JS binding today.
                SerializeAsAlias(symbol, GetInternalSymbolName(symbol, symbolName, context), modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
            }
            else
            {
                SerializeAsClass(symbol, GetInternalSymbolName(symbol, symbolName, context), modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
            }
        }

        if (((symbol.Flags & (SymbolFlags.ValueModule | SymbolFlags.NamespaceModule)) != 0 && (!isConstMergedWithNS || IsTypeOnlyNamespace(symbol)))
            || isConstMergedWithNSPrintableAsSignatureMerge)
        {
            SerializeModule(symbol, symbolName, modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }

        // The class meaning serialization should handle serializing all interface members
        if ((symbol.Flags & SymbolFlags.Interface) != 0 && (symbol.Flags & SymbolFlags.Class) == 0)
        {
            SerializeInterface(symbol, symbolName, modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }

        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            SerializeAsAlias(symbol, GetInternalSymbolName(symbol, symbolName, context), modifierFlags, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }

        if ((symbol.Flags & SymbolFlags.Property) != 0 && symbol.EscapedName == InternalSymbolName.ExportEquals.Value)
        {
            SerializeMaybeAliasAssignment(symbol, context, results, addingDeclare, visitedSymbols, deferredPrivatesStack, enclosingDeclaration);
        }

        if ((symbol.Flags & SymbolFlags.ExportStar) != 0)
        {
            // synthesize export * from "moduleReference"
            // Straightforward - only one thing to do - make an export declaration
            if (symbol.Declarations is not null)
            {
                foreach (var node in symbol.Declarations)
                {
                    var decl = (ExportDeclaration)node;
                    var resolvedModule = ResolveExternalModuleName((Node)node, decl.ModuleSpecifier!);
                    if (resolvedModule is null) continue;
                    var specifier = GetSpecifierForModuleSymbol(resolvedModule, context);
                    context.ApproximateLength += 17 + specifier.Length; // `export * from "specifier";`
                    AddResult(
                        context,
                        results,
                        addingDeclare,
                        _factory.CreateExportDeclaration(
                            /*modifiers*/ null,
                            decl.IsTypeOnly,
                            /*exportClause*/ null,
                            _factory.CreateStringLiteral(specifier),
                            /*attributes*/ null),
                        ModifierFlags.None);
                }
            }
        }

        if (needsPostExportDefault)
        {
            var internalSymbolName = GetInternalSymbolName(symbol, symbolName, context);
            context.ApproximateLength += 16 + internalSymbolName.Length; // `export default internalName;`
            AddResult(
                context,
                results,
                addingDeclare,
                _factory.CreateExportAssignment(/*modifiers*/ null, /*isExportEquals*/ false, _factory.CreateIdentifier(internalSymbolName)),
                ModifierFlags.None);
        }
        else if (needsExportDeclaration)
        {
            var internalSymbolName = GetInternalSymbolName(symbol, symbolName, context);
            context.ApproximateLength += 22 + symbolName.Length + internalSymbolName.Length; // `export { internalName as symbolName };`
            AddResult(
                context,
                results,
                addingDeclare,
                _factory.CreateExportDeclaration(
                    /*modifiers*/ null,
                    /*isTypeOnly*/ false,
                    _factory.CreateNamedExports([_factory.CreateExportSpecifier(/*isTypeOnly*/ false, internalSymbolName, symbolName)]),
                    /*moduleSpecifier*/ null,
                    /*attributes*/ null),
                ModifierFlags.None);
        }
    }

    // ported from: src/compiler/checker.ts:9845-9860 — <c>includePrivateSymbol</c>
    /// <remarks>
    /// PORT-NOTE: an <c>internal</c> member because agent F's serializers call it (checker.ts:10070, :10413, :10671);
    /// the frame's <c>deferredPrivatesStack</c> and <c>context</c> are parameters.
    /// </remarks>
    internal void IncludePrivateSymbol(ISymbol symbol, NodeBuilderContext context, List<Dictionary<int, ISymbol>> deferredPrivatesStack)
    {
        if (Core.Some(symbol.Declarations, d => Utilities.IsPartOfParameterDeclaration((Node)d))) return;
        Debug.AssertIsDefined(deferredPrivatesStack[^1]);
        // Call to cache unique name for symbol
        _ = GetUnusedName(Utilities.UnescapeLeadingUnderscores(symbol.EscapedName!), symbol, context);
        // Blanket moving (import) aliases into the root private context should work, since imports are not valid within
        // namespaces (so they must have been in the root to begin with if they were real imports) cjs `require` aliases
        // (an upcoming feature) will throw a wrench in this, since those may have been nested, but we'll need to
        // synthesize them in the outer scope anyway, as that's the only place the import they translate to is valid. In
        // such a case, we might need to use a unique name for the moved import; which hopefully the above
        // `getUnusedName` call should produce.
        var isExternalImportAlias = (symbol.Flags & SymbolFlags.Alias) != 0 && !Core.Some(symbol.Declarations, d =>
            Utilities.FindAncestor((Node)d, NodeTests.IsExportDeclaration) is not null
            || NodeTests.IsNamespaceExport((Node)d)
            || (NodeTests.IsImportEqualsDeclaration((Node)d) && !NodeTests.IsExternalModuleReference(((ImportEqualsDeclaration)d).ModuleReference!)));
        deferredPrivatesStack[isExternalImportAlias ? 0 : deferredPrivatesStack.Count - 1][Utilities.GetSymbolId(symbol)] = symbol;
    }

    // ported from: src/compiler/checker.ts:9861-9866 — <c>isExportingScope</c>
    /// <remarks>
    /// PORT-NOTE: an <c>internal</c> member because <see cref="AddResult"/> is one and a member cannot call a local
    /// function of another member; it reads no frame state.
    /// </remarks>
    internal bool IsExportingScope(Node enclosingDeclaration)
        => (NodeTests.IsSourceFile(enclosingDeclaration)
                && (Utilities.IsExternalOrCommonJsModule((SourceFile)enclosingDeclaration) || Utilities.IsJsonSourceFile((SourceFile)enclosingDeclaration)))
            || (Utilities.IsAmbientModule(enclosingDeclaration) && !Utilities.IsGlobalScopeAugmentation((ModuleDeclaration)enclosingDeclaration));

    // ported from: src/compiler/checker.ts:9867-9899 — <c>addResult</c>
    /// <summary>
    /// Prepends a `declare` and/or `export` modifier if the context requires it, and then adds `node` to `results`.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: an <c>internal</c> member because agent F's serializers call it directly (twenty-one sites in
    /// checker.ts:9900-11167); the frame's <c>results</c> and <c>addingDeclare</c> are parameters. TS's
    /// <c>{...node, modifiers}</c> is <c>factory.replaceModifiers</c>, which keeps the node's kind.
    /// </remarks>
    internal void AddResult(
        NodeBuilderContext context,
        List<IStatement> results,
        bool addingDeclare,
        Node node,
        ModifierFlags additionalModifierFlags)
    {
        if (Utilities.CanHaveModifiers(node))
        {
            var oldModifierFlags = Utilities.GetEffectiveModifierFlags(node);
            var newModifierFlags = ModifierFlags.None;
            Node? enclosingDeclaration = context.EnclosingDeclaration;
            if (enclosingDeclaration is not null && Utilities.IsJSDocTypeAlias(enclosingDeclaration))
            {
                enclosingDeclaration = Utilities.GetSourceFileOfNode(enclosingDeclaration);
            }

            if ((additionalModifierFlags & ModifierFlags.Export) != 0
                && enclosingDeclaration is not null
                && (IsExportingScope(enclosingDeclaration) || NodeTests.IsModuleDeclaration(enclosingDeclaration))
                && Utilities.CanHaveExportModifier(node))
            {
                // Classes, namespaces, variables, functions, interfaces, and types should all be `export`ed in a
                // module context if not private
                newModifierFlags |= ModifierFlags.Export;
            }

            if (addingDeclare
                && (newModifierFlags & ModifierFlags.Export) == 0
                && (enclosingDeclaration is null || (enclosingDeclaration.Flags & NodeFlags.Ambient) == 0)
                && (NodeTests.IsEnumDeclaration(node)
                    || NodeTests.IsVariableStatement(node)
                    || NodeTests.IsFunctionDeclaration(node)
                    || NodeTests.IsClassDeclaration(node)
                    || NodeTests.IsModuleDeclaration(node)))
            {
                // Classes, namespaces, variables, enums, and functions all need `declare` modifiers to be valid in a
                // declaration file top-level scope
                newModifierFlags |= ModifierFlags.Ambient;
            }

            if ((additionalModifierFlags & ModifierFlags.Default) != 0
                && (NodeTests.IsClassDeclaration(node) || NodeTests.IsInterfaceDeclaration(node) || NodeTests.IsFunctionDeclaration(node)))
            {
                newModifierFlags |= ModifierFlags.Default;
            }

            if (newModifierFlags != ModifierFlags.None)
            {
                node = _factory.ReplaceModifiers(node, newModifierFlags | oldModifierFlags);
            }

            context.ApproximateLength += ModifiersLength(newModifierFlags | oldModifierFlags);
        }

        results.Add((IStatement)node);
    }

    /// <summary>
    /// The tracker TS builds as an object literal inside <c>symbolTableToDeclarationStatements</c>
    /// (checker.ts:9360-9391): every member of the caller's tracker except <c>trackSymbol</c>, which is overridden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the reference literal is <c>{ ...oldcontext.tracker.inner, trackSymbol }</c>. A spread copies own
    /// enumerable properties, and the port's tracker objects are the closed <see cref="ISymbolTracker"/> face, so the
    /// class forwards each member to the inner tracker instead — a member the inner does not answer is a no-op, the
    /// convention <see cref="SymbolTrackerImpl"/> already records. <c>moduleResolverHost</c> is a data property in the
    /// spread's result, so it is one here too (copied from the inner tracker at construction).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>trackSymbol</c> reads the closure variable <c>context</c> — the new context the member
    /// assigns, and one a nested serializer may temporarily swap for a subcontext. Only
    /// <c>remappedSymbolNames</c> is read, and that map object is shared by the subcontext (the spread copies the
    /// reference), so holding the new context is unobservable.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS types this tracker as <c>SymbolTracker</c> whose <c>trackSymbol</c> takes a non-optional symbol;
    /// the port's slot is nullable like every reference slot, so a null symbol answers <c>false</c> rather than
    /// dereferencing — the same handling <see cref="SymbolTrackerImpl"/> documents.
    /// </para>
    /// </remarks>
    internal sealed class SymbolTableToDeclarationStatementsTracker : ISymbolTracker
    {
        private readonly Checker _checker;
        private readonly ISymbolTracker? _inner;
        private readonly NodeBuilderContext _context;
        private readonly Node? _enclosingDeclaration;
        private readonly Action<ISymbol> _includePrivateSymbol;

        /// <summary>
        /// ported from: src/compiler/checker.ts:9360-9366 — what the literal's <c>trackSymbol</c> closure captures
        /// (the inner tracker, the new context, the caller's enclosing declaration and the frame's
        /// <c>includePrivateSymbol</c>).
        /// </summary>
        public SymbolTableToDeclarationStatementsTracker(
            Checker checker,
            ISymbolTracker? inner,
            NodeBuilderContext context,
            Node? enclosingDeclaration,
            Action<ISymbol> includePrivateSymbol)
        {
            _checker = checker;
            _inner = inner;
            _context = context;
            _enclosingDeclaration = enclosingDeclaration;
            _includePrivateSymbol = includePrivateSymbol;
            ModuleResolverHost = inner?.ModuleResolverHost;
        }

        /// <summary>ported from: src/compiler/checker.ts:9367-9389 — the literal's <c>trackSymbol</c>.</summary>
        public bool TrackSymbol(ISymbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning)
        {
            // If the context has a remapped name for the symbol, it *should* mean it's been made visible
            if (symbol is null) return false;
            if (_context.RemappedSymbolNames?.ContainsKey(Utilities.GetSymbolId(symbol)) == true) return false;
            var accessibleResult = _checker.IsSymbolAccessible(symbol, enclosingDeclaration, meaning, /*shouldComputeAliasesToMakeVisible*/ false);
            if (accessibleResult.Accessibility == SymbolAccessibility.Accessible)
            {
                // Lookup the root symbol of the chain of refs we'll use to access it and serialize it
                var chain = _checker.LookupSymbolChainWorker(symbol, _context, meaning, /*yieldModuleSymbol*/ false);
                if ((symbol.Flags & SymbolFlags.Property) == 0)
                {
                    // Only include referenced privates in the same file. Weird JS aliases may expose privates
                    // from other files - assume JS transforms will make those available via expected means
                    var root = Core.First(chain);
                    var contextFile = Utilities.GetSourceFileOfNode(_enclosingDeclaration);
                    if (Core.Some(root.Declarations, d => ReferenceEquals(Utilities.GetSourceFileOfNode((Node)d), contextFile)))
                    {
                        _includePrivateSymbol(root);
                    }
                }
            }
            else if (_inner is not null)
            {
                return _inner.TrackSymbol(symbol, enclosingDeclaration, meaning);
            }

            return false;
        }

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>moduleResolverHost</c>.</summary>
        public IModuleSpecifierResolutionHost? ModuleResolverHost { get; set; }

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportInaccessibleThisError</c>.</summary>
        public void ReportInaccessibleThisError() => _inner?.ReportInaccessibleThisError();

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportPrivateInBaseOfClassExpression</c>.</summary>
        public void ReportPrivateInBaseOfClassExpression(string? propertyName) => _inner?.ReportPrivateInBaseOfClassExpression(propertyName);

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportInaccessibleUniqueSymbolError</c>.</summary>
        public void ReportInaccessibleUniqueSymbolError() => _inner?.ReportInaccessibleUniqueSymbolError();

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportCyclicStructureError</c>.</summary>
        public void ReportCyclicStructureError() => _inner?.ReportCyclicStructureError();

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportLikelyUnsafeImportRequiredError</c>.</summary>
        public void ReportLikelyUnsafeImportRequiredError(string? specifier, string? symbolName) => _inner?.ReportLikelyUnsafeImportRequiredError(specifier, symbolName);

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportTruncationError</c>.</summary>
        public void ReportTruncationError() => _inner?.ReportTruncationError();

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportNonlocalAugmentation</c>.</summary>
        public void ReportNonlocalAugmentation(SourceFile? containingFile, ISymbol? parentSymbol, ISymbol? augmentingSymbol)
            => _inner?.ReportNonlocalAugmentation(containingFile, parentSymbol, augmentingSymbol);

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportNonSerializableProperty</c>.</summary>
        public void ReportNonSerializableProperty(string? propertyName) => _inner?.ReportNonSerializableProperty(propertyName);

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>reportInferenceFallback</c>.</summary>
        public void ReportInferenceFallback(Node? node) => _inner?.ReportInferenceFallback(node);

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>pushErrorFallbackNode</c>.</summary>
        public void PushErrorFallbackNode(IDeclaration? node) => _inner?.PushErrorFallbackNode(node);

        /// <summary>ported from: src/compiler/checker.ts:9361 — the spread's <c>popErrorFallbackNode</c>.</summary>
        public void PopErrorFallbackNode() => _inner?.PopErrorFallbackNode();
    }
}
