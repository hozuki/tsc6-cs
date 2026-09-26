namespace TypeScript.Compiler;

// ported from: src/compiler/checker.ts:49547-49575 (checkSourceFile, unusedIsError, getPotentiallyUnusedIdentifiers)
// ported from: src/compiler/checker.ts:49578-49688 (checkSourceFileWorker, checkSourceFileNodesWorker)
// ported from: src/compiler/checker.ts:49689-49765 (getDiagnostics … getGlobalDiagnostics)
// ported from: src/compiler/checker.ts:49767-49873 (getSymbolsInScope)
// ported from: src/compiler/checker.ts:49874-49986 (isTypeDeclarationName … isThisPropertyAndThisTyped)
// ported from: src/compiler/checker.ts:49988-50188 (getSymbolOfNameOrPropertyAccessExpression … resolveJSDocMemberName)
// ported from: src/compiler/checker.ts:50190-50380 (getSymbolAtLocation … getExportSpecifierLocalTargetSymbol)
// ported from: src/compiler/checker.ts:50382-50545 (getTypeOfNode … getClassElementPropertyKeyType)
// ported from: src/compiler/checker.ts:50547-50590 (getAugmentedPropertiesOfType … tryGetTarget)
// ported from: src/compiler/checker.ts:50594-50666 (isArgumentsLocalBinding … getReferencedImportDeclaration)
// ported from: src/compiler/checker.ts:50668-50801 (isSymbolOfDestructuredElementOfCatchBinding … isAliasResolvedToValue)
// ported from: src/compiler/checker.ts:50803-50853 (isConstEnumOrConstEnumOnlyModule, isReferencedAliasDeclaration, isImplementationOfOverload)
/// <summary>
/// How the checker walks a file: <c>checkSourceFile</c> and its two workers, the diagnostic collections the walk
/// leaves behind, and the semantic questions a service asks about one node (<c>getSymbolsInScope</c>,
/// <c>getSymbolAtLocation</c>, <c>getTypeOfNode</c>) with the alias/collision predicates the emitter asks.
/// </summary>
/// <remarks>
/// <para>
/// PORT-NOTE: this file calls a large number of members of the sibling slices and of the two blocks this slice does
/// not own — <c>checkSourceElement</c>, <c>checkGrammarSourceFile</c> and the deferred-node walk (M6-5's
/// <c>Checker.Check.cs</c>, the grammar and statements bands), <c>checkDeferredNodes</c>,
/// <c>registerForUnusedIdentifiersCheck</c>, <c>checkUnusedIdentifiers</c>,
/// <c>checkPotentialUncheckedRenamedBindingElementsInTypes</c> and the four collision checks (the statements band),
/// <c>checkExternalModuleExports</c> (the modules band), and the checking members
/// <c>checkExpression</c>/<c>checkExpressionCached</c>/<c>checkPropertyAccessExpression</c>/<c>checkQualifiedName</c>
/// (M6-5). Each is called unqualified because its reference declaration site is inside <c>createTypeChecker</c>, and
/// each is listed with its owner in the slice report.
/// </para>
/// <para>
/// PORT-NOTE: <c>PotentiallyUnusedIdentifier</c> (checker.ts:44436) is a union of node kinds with no C# counterpart;
/// it erases to <see cref="Node"/> here (agreed with the statements band, which owns the alias — see
/// <see cref="GetPotentiallyUnusedIdentifiers"/>).
/// </para>
/// <para>
/// PORT-NOTE: TS's <c>EntityName</c>/<c>SignatureDeclaration</c>/<c>AssignmentPattern</c> unions likewise have no C#
/// type (§7.5): a parameter the reference types with one of them is a <see cref="Node"/> here (or the interface the
/// generated model states — <c>MemberExpression</c> for the assignment pattern), with the narrowing casts the
/// reference implies.
/// </para>
/// <para>
/// PORT-NOTE: the <c>!</c> annotations in this file mark the three members whose guard tests a member a
/// sibling slice has not landed yet (<c>Utilities.TryGetClassImplementingOrExtendingExpressionWithTypeArguments</c>,
/// <c>Utilities.IsInternalModuleImportEqualsDeclaration</c>, <c>Utilities.IsAliasSymbolDeclaration</c>): an unresolved
/// call makes the nullable analyzer give up on the narrowing, so <c>classType</c>/<c>node</c> read as "maybe null"
/// after a guard that proves otherwise. Each one is what the reference's non-nullable parameter states, and each
/// silences a phantom CS8602/CS8604 rather than a real one.
/// </para>
/// <para>
/// PORT-NOTE: <c>ITypeCheckerHost</c> inherits <c>IsSourceOfProjectReferenceRedirect</c> from both of its bases
/// (<c>IModuleSpecifierResolutionHost</c> and <c>ISourceFileMayBeEmittedHost</c>), so
/// <c>_host.IsSourceOfProjectReferenceRedirect</c> does not bind; the two <c>skipTypeChecking</c> calls here
/// disambiguate with the cast to <c>ISourceFileMayBeEmittedHost</c>, which is the base the reference's
/// <c>HostWithIsSourceOfProjectReferenceRedirect</c> (utilities.ts:10392-10395) corresponds to.
/// </para>
/// <para>
/// PORT-NOTE: the six state slots this band reads and reassigns — <c>_allPotentiallyUnusedIdentifiers</c>
/// (:2319) and the five <c>_potential*Collisions</c>/<c>_potentialUnusedRenamedBindingElementsInTypes</c> lists
/// (:2367-2371) — are <c>createTypeChecker</c>'s state (the M6-5 assembly's <c>Checker/Checker.Initialize.cs</c>);
/// this file only names them.
/// </para>
/// </remarks>
internal sealed partial class Checker
{
    // ---------------------------------------------------------------------------------------------
    // Walking a file (checker.ts:49547-49688)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:49547-49558 — <c>checkSourceFile</c>.</summary>
    internal void CheckSourceFile(SourceFile node, List<Node>? nodesToCheck)
    {
        Tracing.Current?.Push(
            Tracing.Phase.Check,
            nodesToCheck is not null ? "checkSourceFileNodes" : "checkSourceFile",
            TracingArgs.Of(("path", node.Path)),
            separateBeginAndEnd: true);
        var beforeMark = nodesToCheck is not null ? "beforeCheckNodes" : "beforeCheck";
        var afterMark = nodesToCheck is not null ? "afterCheckNodes" : "afterCheck";
        Performance.Mark(beforeMark);
        if (nodesToCheck is not null)
        {
            CheckSourceFileNodesWorker(node, nodesToCheck);
        }
        else
        {
            CheckSourceFileWorker(node);
        }

        Performance.Mark(afterMark);
        Performance.Measure("Check", beforeMark, afterMark);
        Tracing.Current?.Pop();
        _reportedUnreachableNodes = null;
    }

    /// <summary>ported from: src/compiler/checker.ts:49559-49572 — <c>unusedIsError</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>default: return Debug.assertNever(kind)</c> returns the never-typed call, which throws; the
    /// port's <see cref="Debug.AssertNever(object?, string)"/> returns void and a reader follows it (the shape
    /// <c>Checker/Checker.Inference.cs:2008</c> established).
    /// </remarks>
    internal bool UnusedIsError(UnusedKind kind, bool isAmbient)
    {
        if (isAmbient)
        {
            return false;
        }

        switch (kind)
        {
            case UnusedKind.Local:
                return Js.IsTruthy(_compilerOptions.NoUnusedLocals);
            case UnusedKind.Parameter:
                return Js.IsTruthy(_compilerOptions.NoUnusedParameters);
            default:
                Debug.AssertNever(kind);
                return false;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49573-49575 — <c>getPotentiallyUnusedIdentifiers</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the result is the union alias <c>PotentiallyUnusedIdentifier</c> (checker.ts:44436) with the union
    /// erased to <see cref="Node"/>; TS's <c>|| emptyArray</c> is a fresh empty list here, which is what every caller
    /// reads it as (none mutates it — the statements band accumulates into
    /// <c>_allPotentiallyUnusedIdentifiers</c> instead).
    /// </remarks>
    internal List<Node> GetPotentiallyUnusedIdentifiers(SourceFile sourceFile)
        => _allPotentiallyUnusedIdentifiers.GetValueOrDefault(sourceFile.Path ?? "") ?? [];

    /// <summary>Fully type check a source file and collect the relevant diagnostics.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:49578-49652 — <c>checkSourceFileWorker</c>.</remarks>
    internal void CheckSourceFileWorker(SourceFile node)
    {
        var links = GetNodeLinks(node);
        if ((links.Flags & NodeCheckFlags.TypeChecked) != 0) return;

        if (Utilities.SkipTypeChecking(node, _compilerOptions, _host.IsSourceFileDefaultLibrary, ((ISourceFileMayBeEmittedHost)_host).IsSourceOfProjectReferenceRedirect))
        {
            return;
        }

        // Grammar checking
        CheckGrammarSourceFile(node);

        Core.Clear(_potentialThisCollisions);
        Core.Clear(_potentialNewTargetCollisions);
        Core.Clear(_potentialWeakMapSetCollisions);
        Core.Clear(_potentialReflectCollisions);
        Core.Clear(_potentialUnusedRenamedBindingElementsInTypes);

        if ((links.Flags & NodeCheckFlags.PartiallyTypeChecked) != 0)
        {
            _potentialThisCollisions = links.PotentialThisCollisions!;
            _potentialNewTargetCollisions = links.PotentialNewTargetCollisions!;
            _potentialWeakMapSetCollisions = links.PotentialWeakMapSetCollisions!;
            _potentialReflectCollisions = links.PotentialReflectCollisions!;
            _potentialUnusedRenamedBindingElementsInTypes = links.PotentialUnusedRenamedBindingElementsInTypes!;
        }

        foreach (var statement in node.Statements!)
        {
            CheckSourceElement((Node)statement);
        }

        CheckSourceElement(node.EndOfFileToken!);

        CheckDeferredNodes(node);

        if (Utilities.IsExternalOrCommonJsModule(node))
        {
            RegisterForUnusedIdentifiersCheck(node);
        }

        AddLazyDiagnostic(() =>
        {
            // This relies on the results of other lazy diagnostics, so must be computed after them
            if (!node.IsDeclarationFile && (Js.IsTruthy(_compilerOptions.NoUnusedLocals) || Js.IsTruthy(_compilerOptions.NoUnusedParameters)))
            {
                CheckUnusedIdentifiers(GetPotentiallyUnusedIdentifiers(node), (Node containingNode, UnusedKind kind, Diagnostic diag) =>
                {
                    if (!Utilities.ContainsParseError(containingNode) && UnusedIsError(kind, (containingNode.Flags & NodeFlags.Ambient) != 0))
                    {
                        _diagnostics.Add(diag);
                    }
                });
            }
            if (!node.IsDeclarationFile)
            {
                CheckPotentialUncheckedRenamedBindingElementsInTypes();
            }
        });

        if (Utilities.IsExternalOrCommonJsModule(node))
        {
            CheckExternalModuleExports(node);
        }

        if (_potentialThisCollisions.Count > 0)
        {
            foreach (var collision in _potentialThisCollisions)
            {
                CheckIfThisIsCapturedInEnclosingScope(collision);
            }

            Core.Clear(_potentialThisCollisions);
        }

        if (_potentialNewTargetCollisions.Count > 0)
        {
            foreach (var collision in _potentialNewTargetCollisions)
            {
                CheckIfNewTargetIsCapturedInEnclosingScope(collision);
            }

            Core.Clear(_potentialNewTargetCollisions);
        }

        if (_potentialWeakMapSetCollisions.Count > 0)
        {
            foreach (var collision in _potentialWeakMapSetCollisions)
            {
                CheckWeakMapSetCollision(collision);
            }

            Core.Clear(_potentialWeakMapSetCollisions);
        }

        if (_potentialReflectCollisions.Count > 0)
        {
            foreach (var collision in _potentialReflectCollisions)
            {
                CheckReflectCollision(collision);
            }

            Core.Clear(_potentialReflectCollisions);
        }

        links.Flags |= NodeCheckFlags.TypeChecked;
    }

    /// <summary>ported from: src/compiler/checker.ts:49653-49688 — <c>checkSourceFileNodesWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS pushes <c>...potentialThisCollisions</c> into the node's own arrays with
    /// <c>(links.x || (links.x = [])).push(...x)</c>; the port states the same "create if absent, then append" as
    /// <c>(links.X ??= []).AddRange(_x)</c>.
    /// </remarks>
    internal void CheckSourceFileNodesWorker(SourceFile file, List<Node> nodes)
    {
        var links = GetNodeLinks(file);
        if ((links.Flags & NodeCheckFlags.TypeChecked) != 0) return;

        if (Utilities.SkipTypeChecking(file, _compilerOptions, _host.IsSourceFileDefaultLibrary, ((ISourceFileMayBeEmittedHost)_host).IsSourceOfProjectReferenceRedirect))
        {
            return;
        }

        // Grammar checking
        CheckGrammarSourceFile(file);

        Core.Clear(_potentialThisCollisions);
        Core.Clear(_potentialNewTargetCollisions);
        Core.Clear(_potentialWeakMapSetCollisions);
        Core.Clear(_potentialReflectCollisions);
        Core.Clear(_potentialUnusedRenamedBindingElementsInTypes);

        foreach (var node in nodes)
        {
            CheckSourceElement(node);
        }

        CheckDeferredNodes(file);

        (links.PotentialThisCollisions ??= []).AddRange(_potentialThisCollisions);
        (links.PotentialNewTargetCollisions ??= []).AddRange(_potentialNewTargetCollisions);
        (links.PotentialWeakMapSetCollisions ??= []).AddRange(_potentialWeakMapSetCollisions);
        (links.PotentialReflectCollisions ??= []).AddRange(_potentialReflectCollisions);
        (links.PotentialUnusedRenamedBindingElementsInTypes ??= []).AddRange(_potentialUnusedRenamedBindingElementsInTypes);

        links.Flags |= NodeCheckFlags.PartiallyTypeChecked;
        foreach (var node in nodes)
        {
            var nodeLinks = GetNodeLinks(node);
            nodeLinks.Flags |= NodeCheckFlags.PartiallyTypeChecked;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Collecting the diagnostics of a file (checker.ts:49689-49765)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:49689-49701 — <c>getDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated <c>ITypeChecker.GetDiagnostics</c> (types.ts) states all three parameters as
    /// non-optional nullable slots, and the landed callers (<c>Checker/Checker.Diagnostics.cs:51</c> passes two
    /// arguments, the program aggregation passes three with a possibly-null list); the port keeps the reference's
    /// optional third parameter as a C# default so both call shapes bind.
    /// </remarks>
    internal List<Diagnostic> GetDiagnostics(SourceFile? sourceFile, ICancellationToken? cancellationToken, List<Node>? nodesToCheck = null)
    {
        try
        {
            // Record the cancellation token so it can be checked later on during checkSourceElement.
            // Do this in a finally block so we can ensure that it gets reset back to nothing after
            // this call is done.
            _cancellationToken = cancellationToken;
            return GetDiagnosticsWorker(sourceFile, nodesToCheck);
        }
        finally
        {
            _cancellationToken = null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:49702-49708 — <c>ensurePendingDiagnosticWorkComplete</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS walks the live array — a callback that defers more work appends to it and the loop reaches the
    /// new entries — and then rebinds the slot to a fresh array. The port's slot is a readonly
    /// <see cref="List{T}"/> (Checker.cs:137), so the same live iteration is by index and the "rebind" is
    /// <c>Clear()</c>; a <c>foreach</c> here would throw when a callback appends.
    /// </remarks>
    internal void EnsurePendingDiagnosticWorkComplete()
    {
        // Invoke any existing lazy diagnostics to add them, clear the backlog of diagnostics
        for (var i = 0; i < _deferredDiagnosticsCallbacks.Count; i++)
        {
            _deferredDiagnosticsCallbacks[i]();
        }

        _deferredDiagnosticsCallbacks.Clear();
    }

    /// <summary>ported from: src/compiler/checker.ts:49710-49721 — <c>checkSourceFileWithEagerDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS swaps <c>addLazyDiagnostic</c> for <c>cb =&gt; cb()</c> so every deferral made during the check
    /// runs at its deferral point, then restores the queueing closure. The port performs the identical swap on
    /// <c>_addLazyDiagnostic</c> (Checker.cs:179), so the callbacks run where the reference runs them: in the middle of
    /// the check, before the members checked after the deferral point. That ordering is observable — it decides when
    /// the types those callbacks create get their ids (checker.ts:5517-5523) and where their diagnostics land in the
    /// file's list — so draining them afterwards, as this member once did, reordered both.
    /// </remarks>
    internal void CheckSourceFileWithEagerDiagnostics(SourceFile sourceFile, List<Node>? nodesToCheck = null)
    {
        EnsurePendingDiagnosticWorkComplete();
        // then setup diagnostics for immediate invocation (as we are about to collect them, and
        // this avoids the overhead of longer-lived callbacks we don't need to allocate)
        // This also serves to make the shift to possibly lazy diagnostics transparent to serial command-line scenarios
        // (as in those cases, all the diagnostics will still be computed as the appropriate place in the tree,
        // thus much more likely retaining the same union ordering as before we had lazy diagnostics)
        var oldAddLazyDiagnostics = _addLazyDiagnostic;
        _addLazyDiagnostic = arg => arg();
        CheckSourceFile(sourceFile, nodesToCheck);
        _addLazyDiagnostic = oldAddLazyDiagnostics;
    }

    /// <summary>ported from: src/compiler/checker.ts:49723-49759 — <c>getDiagnosticsWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference compares <c>previousGlobalDiagnostics</c> with <c>currentGlobalDiagnostics</c> by
    /// reference to detect that new global diagnostics were appended; <see cref="DiagnosticCollection.GetGlobalDiagnostics"/>
    /// deliberately hands back its live list for exactly this comparison (its PORT-NOTE says so), so the port's
    /// <c>ReferenceEquals</c> is the same test.
    /// </remarks>
    internal List<Diagnostic> GetDiagnosticsWorker(SourceFile? sourceFile, List<Node>? nodesToCheck)
    {
        if (sourceFile is not null)
        {
            EnsurePendingDiagnosticWorkComplete();
            // Some global diagnostics are deferred until they are needed and
            // may not be reported in the first call to getGlobalDiagnostics.
            // We should catch these changes and report them.
            var previousGlobalDiagnostics = _diagnostics.GetGlobalDiagnostics();
            var previousGlobalDiagnosticsSize = previousGlobalDiagnostics.Count;

            CheckSourceFileWithEagerDiagnostics(sourceFile, nodesToCheck);
            var semanticDiagnostics = _diagnostics.GetDiagnostics(sourceFile.FileName);
            if (nodesToCheck is not null)
            {
                // No need to get global diagnostics.
                return semanticDiagnostics;
            }

            var currentGlobalDiagnostics = _diagnostics.GetGlobalDiagnostics();
            if (!ReferenceEquals(currentGlobalDiagnostics, previousGlobalDiagnostics))
            {
                // If the arrays are not the same reference, new diagnostics were added.
                var deferredGlobalDiagnostics = Core.RelativeComplement(previousGlobalDiagnostics, currentGlobalDiagnostics, Utilities.CompareDiagnostics);
                return [.. Core.Concatenate(deferredGlobalDiagnostics, semanticDiagnostics)!];
            }
            else if (previousGlobalDiagnosticsSize == 0 && currentGlobalDiagnostics.Count > 0)
            {
                // If the arrays are the same reference, but the length has changed, a single
                // new diagnostic was added as DiagnosticCollection attempts to reuse the
                // same array.
                return [.. Core.Concatenate(currentGlobalDiagnostics, semanticDiagnostics)!];
            }

            return semanticDiagnostics;
        }

        // Global diagnostics are always added when a file is not provided to
        // getDiagnostics
        foreach (var file in _host.GetSourceFiles())
        {
            CheckSourceFileWithEagerDiagnostics(file);
        }

        return _diagnostics.GetDiagnostics();
    }

    /// <summary>ported from: src/compiler/checker.ts:49760-49763 — <c>getGlobalDiagnostics</c>.</summary>
    internal List<Diagnostic> GetGlobalDiagnostics()
    {
        EnsurePendingDiagnosticWorkComplete();
        return _diagnostics.GetGlobalDiagnostics();
    }

    // ---------------------------------------------------------------------------------------------
    // Language service support: the symbols visible at a location (checker.ts:49767-49873)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:49767-49873 — <c>getSymbolsInScope</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the generated <c>ITypeChecker.GetSymbolsInScope</c> states <c>location</c> as nullable although the
    /// reference's parameter is not; the port keeps the interface's face and asserts it at the first read, the shape
    /// every landed member of that interface uses.
    /// </para>
    /// <para>
    /// PORT-NOTE: TS calls <c>populateSymbols()</c> and declares the four helpers <em>below</em> it (function
    /// hoisting makes the order immaterial); C# would treat declarations after the <c>return</c> as unreachable, so
    /// the four are stated first and the call follows them. <c>location</c> stays the parameter TS mutates, and
    /// <c>copySymbols</c>/<c>copyLocallyVisibleExportSymbols</c> are the reference's <c>if (meaning)</c> guard as
    /// <c>meaning != SymbolFlags.None</c>.
    /// </para>
    /// </remarks>
    internal List<ISymbol> GetSymbolsInScope(Node? location, SymbolFlags meaning)
    {
        if ((location!.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return [];
        }

        var symbols = Utilities.CreateSymbolTable();
        var isStaticSymbol = false;

        /**
         * Copy the given symbol into symbol tables if the symbol has the given meaning
         * and it doesn't already existed in the symbol table
         * @param key a key for storing in symbol table; if undefined, use symbol.name
         * @param symbol the symbol to be added into symbol table
         * @param meaning meaning of symbol to filter by before adding to symbol table
         */
        void CopySymbol(ISymbol symbol, SymbolFlags copyMeaning)
        {
            if ((Utilities.GetCombinedLocalAndExportSymbolFlags(symbol) & copyMeaning) != 0)
            {
                var id = symbol.EscapedName!;
                // We will copy all symbol regardless of its reserved name because
                // symbolsToArray will check whether the key is a reserved name and
                // it will not copy symbol with reserved name to the array
                if (!symbols.ContainsKey(id))
                {
                    symbols[id] = symbol;
                }
            }
        }

        void CopySymbols(SymbolTable source, SymbolFlags copyMeaning)
        {
            if (copyMeaning != SymbolFlags.None)
            {
                source.ForEach((ISymbol symbol, string _) => CopySymbol(symbol, copyMeaning));
            }
        }

        void CopyLocallyVisibleExportSymbols(SymbolTable source, SymbolFlags copyMeaning)
        {
            if (copyMeaning != SymbolFlags.None)
            {
                source.ForEach((ISymbol symbol, string _) =>
                {
                    // Similar condition as in `resolveNameHelper`
                    if (Utilities.GetDeclarationOfKind(symbol, SyntaxKind.ExportSpecifier) is null
                        && Utilities.GetDeclarationOfKind(symbol, SyntaxKind.NamespaceExport) is null
                        && symbol.EscapedName != InternalSymbolName.Default.Value)
                    {
                        CopySymbol(symbol, copyMeaning);
                    }
                });
            }
        }

        void PopulateSymbols()
        {
            while (location is not null)
            {
                if (Utilities.CanHaveLocals(location)
                    && location is ILocalsContainer localsContainer
                    && localsContainer.Locals is not null
                    && !Utilities.IsGlobalSourceFile(location))
                {
                    CopySymbols(localsContainer.Locals, meaning);
                }

                switch (location.Kind)
                {
                    case SyntaxKind.SourceFile:
                        if (!Parser.IsExternalModule((SourceFile)location)) break;
                        goto case SyntaxKind.ModuleDeclaration;
                    case SyntaxKind.ModuleDeclaration:
                        CopyLocallyVisibleExportSymbols(
                            GetSymbolOfDeclaration((IDeclaration)location).Exports!,
                            meaning & SymbolFlags.ModuleMember);
                        break;
                    case SyntaxKind.EnumDeclaration:
                        CopySymbols(GetSymbolOfDeclaration((IDeclaration)location).Exports!, meaning & SymbolFlags.EnumMember);
                        break;
                    case SyntaxKind.ClassExpression:
                        var className = ((ClassExpression)location).Name;
                        if (className is not null)
                        {
                            CopySymbol(((ClassExpression)location).Symbol!, meaning);
                        }

                        // this fall-through is necessary because we would like to handle
                        // type parameter inside class expression similar to how we handle it in classDeclaration and interface Declaration.
                        goto case SyntaxKind.ClassDeclaration;
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                        // If we didn't come from static member of class or interface,
                        // add the type parameters into the symbol table
                        // (type parameters of classDeclaration/classExpression and interface are in member property of the symbol.
                        // Note: that the memberFlags come from previous iteration.
                        if (!isStaticSymbol)
                        {
                            CopySymbols(
                                GetMembersOfSymbol(GetSymbolOfDeclaration((IDeclaration)location)),
                                meaning & SymbolFlags.Type);
                        }

                        break;
                    case SyntaxKind.FunctionExpression:
                        var funcName = ((FunctionExpression)location).Name;
                        if (funcName is not null)
                        {
                            CopySymbol(((FunctionExpression)location).Symbol!, meaning);
                        }

                        break;
                }

                if (Utilities.IntroducesArgumentsExoticObject(location))
                {
                    CopySymbol(_argumentsSymbol!, meaning);
                }

                isStaticSymbol = Utilities.IsStatic(location);
                location = location.Parent;
            }

            CopySymbols(_globals, meaning);
        }

        PopulateSymbols();

        symbols.Remove(InternalSymbolName.This.Value); // Not a symbol, a keyword
        return SymbolsToArray(symbols);
    }

    /// <summary>ported from: src/compiler/checker.ts:49874-49879 — <c>isTypeDeclarationName</c>.</summary>
    internal bool IsTypeDeclarationName(Node name)
        => name.Kind == SyntaxKind.Identifier
            && Utilities.IsTypeDeclaration(name.Parent!)
            && ReferenceEquals(Utilities.GetNameOfDeclaration(name.Parent), name);

    /// <summary>True if the given identifier is part of a type reference.</summary>
    /// <remarks>ported from: src/compiler/checker.ts:49881-49887 — <c>isTypeReferenceIdentifier</c>.</remarks>
    internal bool IsTypeReferenceIdentifier(Node node)
    {
        while (node.Parent!.Kind == SyntaxKind.QualifiedName)
        {
            node = node.Parent;
        }

        return node.Parent!.Kind == SyntaxKind.TypeReference;
    }

    /// <summary>ported from: src/compiler/checker.ts:49889-49895 — <c>isInNameOfExpressionWithTypeArguments</c>.</summary>
    internal bool IsInNameOfExpressionWithTypeArguments(Node node)
    {
        while (node.Parent!.Kind == SyntaxKind.PropertyAccessExpression)
        {
            node = node.Parent;
        }

        return node.Parent!.Kind == SyntaxKind.ExpressionWithTypeArguments;
    }

    /// <summary>ported from: src/compiler/checker.ts:49897-49906 — <c>forEachEnclosingClass</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>if (result = callback(containingClass)) break;</c> is a truthiness test on <c>T</c>, not a
    /// null test — for <c>T = boolean</c> a <c>false</c> callback keeps walking. The port keeps that as
    /// <c>Js.IsTruthy((object?)result)</c>: the <c>(object?)</c> cast is what makes the boxing overload run instead of
    /// <c>Js.IsTruthy&lt;T&gt;</c> ("is not null"), which would stop on the first <c>false</c>.
    /// </remarks>
    internal T? ForEachEnclosingClass<T>(Node node, Func<IClassLikeDeclarationBase, T?> callback)
    {
        T? result = default;
        var containingClass = Utilities.GetContainingClass(node);
        while (containingClass is not null)
        {
            if (Js.IsTruthy((object?)(result = callback((IClassLikeDeclarationBase)containingClass)))) break;
            containingClass = Utilities.GetContainingClass(containingClass);
        }

        return result;
    }

    /// <summary>ported from: src/compiler/checker.ts:49908-49919 — <c>isNodeUsedDuringClassInitialization</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's callback answers <c>true | "quit" | false</c>; the port's quit-capable
    /// <see cref="Utilities.FindAncestor(Node?, Func{Node, bool?})"/> spells <c>"quit"</c> as <c>null</c> (its
    /// PORT-NOTE says so), which is the overload this lambda binds to because it returns <c>null</c>.
    /// </remarks>
    internal bool IsNodeUsedDuringClassInitialization(Node node)
        => Utilities.FindAncestor(node, (Node element) =>
        {
            if (NodeTests.IsConstructorDeclaration(element) && Utilities.NodeIsPresent(((ConstructorDeclaration)element).Body)
                || NodeTests.IsPropertyDeclaration(element))
            {
                return true;
            }
            else if (Utilities.IsClassLike(element) || Utilities.IsFunctionLikeDeclaration(element))
            {
                return null;
            }

            return false;
        }) is not null;

    /// <summary>ported from: src/compiler/checker.ts:49921-49923 — <c>isNodeWithinClass</c>.</summary>
    internal bool IsNodeWithinClass(Node node, IClassLikeDeclarationBase classDeclaration)
        => Js.IsTruthy(ForEachEnclosingClass(node, n => ReferenceEquals(n, classDeclaration)));

    /// <summary>ported from: src/compiler/checker.ts:49925-49939 — <c>getLeftSideOfImportEqualsOrExportAssignment</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's result is the union <c>ImportEqualsDeclaration | ExportAssignment | undefined</c>; the two are
    /// unrelated classes here, so the port answers the <see cref="Node"/> the union erases to. The reference's only
    /// caller (<c>isInRightSideOfImportOrExportAssignment</c>, :49941) tests the result for undefined and nothing
    /// else reads it.
    /// </remarks>
    internal Node? GetLeftSideOfImportEqualsOrExportAssignment(Node nodeOnRightSide)
    {
        while (nodeOnRightSide.Parent!.Kind == SyntaxKind.QualifiedName)
        {
            nodeOnRightSide = nodeOnRightSide.Parent;
        }

        if (nodeOnRightSide.Parent!.Kind == SyntaxKind.ImportEqualsDeclaration)
        {
            return ReferenceEquals(((ImportEqualsDeclaration)nodeOnRightSide.Parent).ModuleReference, nodeOnRightSide)
                ? nodeOnRightSide.Parent
                : null;
        }

        if (nodeOnRightSide.Parent!.Kind == SyntaxKind.ExportAssignment)
        {
            return ReferenceEquals(((ExportAssignment)nodeOnRightSide.Parent).Expression, nodeOnRightSide)
                ? nodeOnRightSide.Parent
                : null;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:49941-49943 — <c>isInRightSideOfImportOrExportAssignment</c>.</summary>
    internal bool IsInRightSideOfImportOrExportAssignment(Node node)
        => GetLeftSideOfImportEqualsOrExportAssignment(node) is not null;

    /// <summary>ported from: src/compiler/checker.ts:49945-49960 — <c>getSpecialPropertyAssignmentSymbolFromEntityName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the <c>EntityName | PropertyAccessExpression</c> union, which has no C# type
    /// (§7.5), so it is a <see cref="Node"/>; the port's <c>goto case</c> is the reference's <c>Property</c> →
    /// <c>ThisProperty</c> fall-through, which C# does not allow implicitly.
    /// </remarks>
    internal ISymbol? GetSpecialPropertyAssignmentSymbolFromEntityName(Node entityName)
    {
        var specialPropertyAssignmentKind = Utilities.GetAssignmentDeclarationKind(entityName.Parent!.Parent!);
        switch (specialPropertyAssignmentKind)
        {
            case AssignmentDeclarationKind.ExportsProperty:
            case AssignmentDeclarationKind.PrototypeProperty:
                return GetSymbolOfNode(entityName.Parent!);
            case AssignmentDeclarationKind.Property:
                if (NodeTests.IsPropertyAccessExpression(entityName.Parent!)
                    && ReferenceEquals(Utilities.GetLeftmostAccessExpression((Expression)entityName.Parent!), entityName))
                {
                    return null;
                }

                goto case AssignmentDeclarationKind.ThisProperty;
            case AssignmentDeclarationKind.ThisProperty:
            case AssignmentDeclarationKind.ModuleExports:
                return GetSymbolOfDeclaration((IDeclaration)entityName.Parent!.Parent!);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:49962-49972 — <c>isImportTypeQualifierPart</c>.</summary>
    internal ImportTypeNode? IsImportTypeQualifierPart(Node node)
    {
        var parent = node.Parent!;
        while (NodeTests.IsQualifiedName(parent))
        {
            node = parent;
            parent = parent.Parent!;
        }

        if (parent.Kind == SyntaxKind.ImportType && ReferenceEquals(((ImportTypeNode)parent).Qualifier, node))
        {
            return (ImportTypeNode)parent;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:49974-49986 — <c>isThisPropertyAndThisTyped</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference has no final <c>return</c>, so the result is <c>undefined</c> on every path that is not
    /// the object-literal one; the port's <c>bool?</c> states that <c>null</c>. The caller negates it
    /// (<c>!isThisPropertyAndThisTyped(...)</c>), which the port writes <c>!= true</c> for the same reason.
    /// </remarks>
    internal bool? IsThisPropertyAndThisTyped(PropertyAccessExpression node)
    {
        if (node.Expression!.Kind == SyntaxKind.ThisKeyword)
        {
            var container = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
            if (Utilities.IsFunctionLike(container))
            {
                var containingLiteral = GetContainingObjectLiteral(container);
                if (containingLiteral is not null)
                {
                    var contextualType = GetApparentTypeOfContextualType(containingLiteral, /*contextFlags*/ null);
                    var type = GetThisTypeOfObjectLiteralFromContextualType(containingLiteral, contextualType);
                    return type is not null && !IsTypeAny(type);
                }
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:49988-50131 — <c>getSymbolOfNameOrPropertyAccessExpression</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: TS's parameter is the
    /// <c>EntityName | PrivateIdentifier | PropertyAccessExpression | JSDocMemberName</c> union, which has no C# type
    /// (§7.5), so it is a <see cref="Node"/> and every branch narrows with the kind test or a cast, as the reference
    /// does.
    /// </para>
    /// <para>
    /// PORT-NOTE: <c>isDeclarationNameOrImportPropertyName</c> (checker.ts:54213) and
    /// <c>getSymbolOfPartOfRightHandSideOfImportEquals</c> (:4480) are declared by other slices and are called
    /// unqualified (both are <c>checker.ts</c> names); the reference's reads of the
    /// <c>ImportSpecifier | ExportSpecifier</c> union's <c>propertyName</c> are the kind-checked read that union
    /// implies, because the two classes share no interface that carries it.
    /// </para>
    /// </remarks>
    internal ISymbol? GetSymbolOfNameOrPropertyAccessExpression(Node name)
    {
        if (IsDeclarationNameOrImportPropertyName(name))
        {
            return GetSymbolOfNode(name.Parent!);
        }

        if (Utilities.IsInJSFile(name)
            && name.Parent!.Kind == SyntaxKind.PropertyAccessExpression
            && ReferenceEquals(name.Parent, ((BinaryExpression)name.Parent.Parent!).Left))
        {
            // Check if this is a special property assignment
            if (!NodeTests.IsPrivateIdentifier(name)
                && !NodeTests.IsJSDocMemberName(name)
                && IsThisPropertyAndThisTyped((PropertyAccessExpression)name.Parent) != true)
            {
                var specialPropertyAssignmentSymbol = GetSpecialPropertyAssignmentSymbolFromEntityName(name);
                if (specialPropertyAssignmentSymbol is not null)
                {
                    return specialPropertyAssignmentSymbol;
                }
            }
        }

        if (name.Parent!.Kind == SyntaxKind.ExportAssignment && Utilities.IsEntityNameExpression(name))
        {
            // Even an entity name expression that doesn't resolve as an entityname may still typecheck as a property access expression
            var success = ResolveEntityName(
                name,
                /*all meanings*/ SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias,
                /*ignoreErrors*/ true);
            if (success is not null && !ReferenceEquals(success, _unknownSymbol))
            {
                return success;
            }
        }
        else if (Utilities.IsEntityName(name) && IsInRightSideOfImportOrExportAssignment(name))
        {
            // Since we already checked for ExportAssignment, this really could only be an Import
            var importEqualsDeclaration = Utilities.GetAncestor(name, SyntaxKind.ImportEqualsDeclaration);
            Debug.Assert(importEqualsDeclaration is not null);
            return GetSymbolOfPartOfRightHandSideOfImportEquals(name, /*dontResolveAlias*/ true);
        }

        if (Utilities.IsEntityName(name))
        {
            var possibleImportNode = IsImportTypeQualifierPart(name);
            if (possibleImportNode is not null)
            {
                GetTypeFromTypeNode(possibleImportNode);
                var sym = GetNodeLinks(name).ResolvedSymbol;
                return ReferenceEquals(sym, _unknownSymbol) ? null : sym;
            }
        }

        while (Utilities.IsRightSideOfQualifiedNameOrPropertyAccessOrJSDocMemberName(name))
        {
            name = name.Parent!;
        }

        if (IsInNameOfExpressionWithTypeArguments(name))
        {
            var meaning = SymbolFlags.None;
            if (name.Parent!.Kind == SyntaxKind.ExpressionWithTypeArguments)
            {
                // An 'ExpressionWithTypeArguments' may appear in type space (interface Foo extends Bar<T>),
                // value space (return foo<T>), or both(class Foo extends Bar<T>); ensure the meaning matches.
                meaning = Utilities.IsPartOfTypeNode(name) ? SymbolFlags.Type : SymbolFlags.Value;

                // In a class 'extends' clause we are also looking for a value.
                if (Utilities.IsExpressionWithTypeArgumentsInClassExtendsClause(name.Parent))
                {
                    meaning |= SymbolFlags.Value;
                }
            }
            else
            {
                meaning = SymbolFlags.Namespace;
            }

            meaning |= SymbolFlags.Alias;
            var entityNameSymbol = Utilities.IsEntityNameExpression(name)
                ? ResolveEntityName(name, meaning, /*ignoreErrors*/ true)
                : null;
            if (entityNameSymbol is not null)
            {
                return entityNameSymbol;
            }
        }

        if (name.Parent!.Kind == SyntaxKind.JSDocParameterTag)
        {
            return Utilities.GetParameterSymbolFromJSDoc((JSDocParameterTag)name.Parent);
        }

        if (name.Parent!.Kind == SyntaxKind.TypeParameter && name.Parent.Parent!.Kind == SyntaxKind.JSDocTemplateTag)
        {
            Debug.Assert(!Utilities.IsInJSFile(name)); // Otherwise `isDeclarationName` would have been true.
            var typeParameter = Utilities.GetTypeParameterFromJsDoc((TypeParameterDeclaration)name.Parent);
            return typeParameter?.Symbol;
        }

        if (Utilities.IsExpressionNode(name))
        {
            if (Utilities.NodeIsMissing(name))
            {
                // Missing entity name.
                return null;
            }

            var isJSDoc = Utilities.FindAncestor(name, (Node n) =>
                Utilities.IsJSDocLinkLike(n) || NodeTests.IsJSDocNameReference(n) || NodeTests.IsJSDocMemberName(n));
            var meaning = isJSDoc is not null ? SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Value : SymbolFlags.Value;
            if (name.Kind == SyntaxKind.Identifier)
            {
                if (Utilities.IsJSXTagName(name) && IsJsxIntrinsicTagName(name))
                {
                    var symbol = GetIntrinsicTagSymbol(name.Parent!);
                    return ReferenceEquals(symbol, _unknownSymbol) ? null : symbol;
                }

                var result = ResolveEntityName(
                    name,
                    meaning,
                    /*ignoreErrors*/ true,
                    /*dontResolveAlias*/ true,
                    (Node?)Utilities.GetHostSignatureFromJSDoc(name));
                if (result is null && isJSDoc is not null)
                {
                    var container = Utilities.FindAncestor(name, (Node n) => Utilities.IsClassLike(n) || NodeTests.IsInterfaceDeclaration(n));
                    if (container is not null)
                    {
                        return ResolveJSDocMemberName(name, /*ignoreErrors*/ true, GetSymbolOfDeclaration((IDeclaration)container));
                    }
                }

                if (result is not null && isJSDoc is not null)
                {
                    var container = Utilities.GetJSDocHost(name);
                    if (container is not null && NodeTests.IsEnumMember(container) && ReferenceEquals(container, result.ValueDeclaration))
                    {
                        return ResolveEntityName(
                            name,
                            meaning,
                            /*ignoreErrors*/ true,
                            /*dontResolveAlias*/ true,
                            Utilities.GetSourceFileOfNode(container)) ?? result;
                    }
                }

                return result;
            }
            else if (NodeTests.IsPrivateIdentifier(name))
            {
                return GetSymbolForPrivateIdentifierExpression((PrivateIdentifier)name);
            }
            else if (name.Kind == SyntaxKind.PropertyAccessExpression || name.Kind == SyntaxKind.QualifiedName)
            {
                var links = GetNodeLinks(name);
                if (links.ResolvedSymbol is not null)
                {
                    return links.ResolvedSymbol;
                }

                if (name.Kind == SyntaxKind.PropertyAccessExpression)
                {
                    CheckPropertyAccessExpression((PropertyAccessExpression)name, CheckMode.Normal);
                    if (links.ResolvedSymbol is null)
                    {
                        links.ResolvedSymbol = GetApplicableIndexSymbol(
                            CheckExpressionCached(((PropertyAccessExpression)name).Expression!),
                            GetLiteralTypeFromPropertyName(((PropertyAccessExpression)name).Name!));
                    }
                }
                else
                {
                    CheckQualifiedName((QualifiedName)name, CheckMode.Normal);
                }

                if (links.ResolvedSymbol is null && isJSDoc is not null && NodeTests.IsQualifiedName(name))
                {
                    return ResolveJSDocMemberName(name);
                }

                return links.ResolvedSymbol;
            }
            else if (NodeTests.IsJSDocMemberName(name))
            {
                return ResolveJSDocMemberName(name);
            }
        }
        else if (Utilities.IsEntityName(name) && IsTypeReferenceIdentifier(name))
        {
            var meaning = name.Parent!.Kind == SyntaxKind.TypeReference ? SymbolFlags.Type : SymbolFlags.Namespace;
            var symbol = ResolveEntityName(name, meaning, /*ignoreErrors*/ true, /*dontResolveAlias*/ true);
            return symbol is not null && !ReferenceEquals(symbol, _unknownSymbol)
                ? symbol
                : GetUnresolvedSymbolForEntityName(name);
        }

        if (name.Parent!.Kind == SyntaxKind.TypePredicate)
        {
            return ResolveEntityName(name, /*meaning*/ SymbolFlags.FunctionScopedVariable, /*ignoreErrors*/ true);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:50133-50167 — <c>getApplicableIndexSymbol</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>(type as ObjectType).members</c>/<c>resolveStructuredTypeMembers(type as ObjectType)</c> are
    /// the port's <see cref="UnionOrIntersectionType"/> face — the class <see cref="ObjectType"/> derives from, which is
    /// the type <see cref="ResolveStructuredTypeMembers"/> takes — and <c>infos === getIndexInfosOfType(type)</c> is a
    /// reference test on the unresolved list, which the port states with <see cref="ReferenceEquals"/>.
    /// </remarks>
    internal ISymbol? GetApplicableIndexSymbol(IType type, IType keyType)
    {
        var infos = GetApplicableIndexInfos(type, keyType);
        if (infos.Count > 0 && ((UnionOrIntersectionType)type).Members is not null)
        {
            var symbol = GetIndexSymbolFromSymbolTable(ResolveStructuredTypeMembers((UnionOrIntersectionType)type).Members!);
            if (ReferenceEquals(infos, GetIndexInfosOfType(type)))
            {
                return symbol;
            }
            else if (symbol is not null)
            {
                var symbolLinks = GetSymbolLinks(symbol);
                var declarationList = Core.MapDefined<IIndexInfo, IDeclaration>(infos, (IIndexInfo i, int _) => i.Declaration);
                var nodeListId = string.Join(",", Core.Map(declarationList, (IDeclaration d, int _) => Utilities.GetNodeId((Node)d))!);
                symbolLinks.FilteredIndexSymbolCache ??= new Dictionary<string, ISymbol>(StringComparer.Ordinal);
                if (symbolLinks.FilteredIndexSymbolCache.TryGetValue(nodeListId, out var cached))
                {
                    return cached;
                }

                var copy = CreateSymbol(SymbolFlags.Signature, InternalSymbolName.Index.Value);
                copy.Declarations = Core.MapDefined<IIndexInfo, IDeclaration>(infos, (IIndexInfo i, int _) => i.Declaration);
                copy.Parent = type.AliasSymbol ?? type.Symbol ?? GetSymbolAtLocation(((Node)copy.Declarations![0]).Parent!);
                symbolLinks.FilteredIndexSymbolCache[nodeListId] = copy;
                return copy;
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively resolve entity names and jsdoc instance references:
    /// 1. K#m as K.prototype.m for a class (or other value) K
    /// 2. K.m as K.prototype.m
    /// 3. I.m as I.m for a type I, or any other I.m that fails to resolve in (1) or (2)
    ///
    /// For unqualified names, a container K may be provided as a second argument.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:50169-50188 — <c>resolveJSDocMemberName</c>.</remarks>
    internal ISymbol? ResolveJSDocMemberName(Node name, bool? ignoreErrors = null, ISymbol? container = null)
    {
        if (Utilities.IsEntityName(name))
        {
            // resolve static values first
            var meaning = SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Value;
            var symbol = ResolveEntityName(name, meaning, ignoreErrors, /*dontResolveAlias*/ true, (Node?)Utilities.GetHostSignatureFromJSDoc(name));
            if (symbol is null && NodeTests.IsIdentifier(name) && container is not null)
            {
                symbol = GetMergedSymbol(GetSymbol(GetExportsOfSymbol(container), ((Identifier)name).EscapedText!, meaning));
            }

            if (symbol is not null)
            {
                return symbol;
            }
        }

        // Reference (checker.ts:50181-50182): `name.left` / `name.right.escapedText` are read straight off the
        // `EntityName | JSDocMemberName` union. Both non-identifier arms of that union — `QualifiedName` and
        // `JSDocMemberName` — carry the two slots but declare them on no shared interface, so the port reads them per
        // kind (as the parser's `escapedTextsEqual` does for the same union). The old code's concrete
        // `JSDocMemberName` cast threw for the `QualifiedName` arm, which checker.ts:50114 does reach.
        var left = NodeTests.IsIdentifier(name)
            ? container
            : ResolveJSDocMemberName(JSDocNameLeft(name), ignoreErrors, container);
        var right = NodeTests.IsIdentifier(name)
            ? ((Identifier)name).EscapedText
            : JSDocNameRightEscapedText(name);
        if (left is not null)
        {
            var proto = (left.Flags & SymbolFlags.Value) != 0
                ? GetPropertyOfType(GetTypeOfSymbol(left), "prototype")
                : null;
            var t = proto is not null ? GetTypeOfSymbol(proto) : GetDeclaredTypeOfSymbol(left);
            return GetPropertyOfType(t, right);
        }

        return null;
    }

    /// <summary>TS: <c>name.left</c> for the two non-identifier arms of the <c>EntityName | JSDocMemberName</c> union
    /// <see cref="ResolveJSDocMemberName"/> walks.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>name.left</c> is one read over the union; the port's <c>QualifiedName</c> and
    /// <c>JSDocMemberName</c> declare the slot on no shared interface, so the port switches on the kind (the parser's
    /// <c>escapedTextsEqual</c>/<c>NameLeft</c> does the same for this union). The discard arm is unreachable — the
    /// caller has already ruled out the identifier form and the union has no third member — and answers the node itself,
    /// as the parser's <c>NameLeft</c> does.
    /// </remarks>
    private static Node JSDocNameLeft(Node name) => name switch
    {
        QualifiedName qualifiedName => qualifiedName.Left!,
        JSDocMemberName memberName => memberName.Left!,
        _ => name,
    };

    /// <summary>TS: <c>name.right.escapedText</c> for the two non-identifier arms of the same union.</summary>
    private static string? JSDocNameRightEscapedText(Node name) => name switch
    {
        QualifiedName qualifiedName => qualifiedName.Right?.EscapedText,
        JSDocMemberName memberName => memberName.Right?.EscapedText,
        _ => null,
    };

    /// <summary>ported from: src/compiler/checker.ts:50190-50349 — <c>getSymbolAtLocation</c>.</summary>
    /// <remarks>
    /// <para>
    /// PORT-NOTE: the generated <c>ITypeChecker.GetSymbolAtLocation</c> states one nullable parameter and TS declares
    /// an optional <c>ignoreErrors</c> second, so the port keeps both (defaulted) and never returns the input.
    /// </para>
    /// <para>
    /// PORT-NOTE: the reference's <c>node.parent.kind === ImportDeclaration || ExportDeclaration</c> branch reads
    /// <c>moduleSpecifier</c> off the union through a cast; the port tests each kind and reads the slot off its own
    /// class (the two classes share no interface carrying it). <c>isDeclarationNameOrImportPropertyName</c>
    /// (checker.ts:54213) belongs to another slice and is called unqualified.
    /// </para>
    /// </remarks>
    internal ISymbol? GetSymbolAtLocation(Node? node, bool? ignoreErrors = null)
    {
        if (node is null) return null;

        if (NodeTests.IsSourceFile(node))
        {
            return Parser.IsExternalModule((SourceFile)node) ? GetMergedSymbol(((SourceFile)node).Symbol) : null;
        }

        var parent = node.Parent!;
        var grandParent = parent.Parent!;

        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return null;
        }

        if (IsDeclarationNameOrImportPropertyName(node))
        {
            // This is a declaration, call getSymbolOfNode
            var parentSymbol = GetSymbolOfDeclaration((IDeclaration)parent);
            if (Utilities.IsImportOrExportSpecifier(parent))
            {
                var propertyName = parent.Kind == SyntaxKind.ImportSpecifier
                    ? (Node?)((ImportSpecifier)parent).PropertyName
                    : ((ExportSpecifier)parent).PropertyName;
                if (ReferenceEquals(propertyName, node))
                {
                    return GetImmediateAliasedSymbol(parentSymbol);
                }
            }

            return parentSymbol;
        }
        else if (Utilities.IsLiteralComputedPropertyDeclarationName(node))
        {
            return GetSymbolOfDeclaration((IDeclaration)parent.Parent!);
        }

        if (node.Kind == SyntaxKind.Identifier)
        {
            if (IsInRightSideOfImportOrExportAssignment(node))
            {
                return GetSymbolOfNameOrPropertyAccessExpression(node);
            }
            else if (parent.Kind == SyntaxKind.BindingElement
                && grandParent.Kind == SyntaxKind.ObjectBindingPattern
                && ReferenceEquals(((BindingElement)parent).PropertyName, node))
            {
                var typeOfPattern = GetTypeOfNode(grandParent);
                var propertyDeclaration = GetPropertyOfType(typeOfPattern, ((Identifier)node).EscapedText);

                if (propertyDeclaration is not null)
                {
                    return propertyDeclaration;
                }
            }
            else if (NodeTests.IsMetaProperty(parent) && ReferenceEquals(((MetaProperty)parent).Name, node))
            {
                if (((MetaProperty)parent).KeywordToken == SyntaxKind.NewKeyword && Utilities.IdText(node) == "target")
                {
                    // `target` in `new.target`
                    return CheckNewTargetMetaProperty((MetaProperty)parent).Symbol;
                }

                // The `meta` in `import.meta` could be given `getTypeOfNode(parent).symbol` (the `ImportMeta` interface symbol), but
                // we have a fake expression type made for other reasons already, whose transient `meta`
                // member should more exactly be the kind of (declarationless) symbol we want.
                // (See #44364 and #45031 for relevant implementation PRs)
                if (((MetaProperty)parent).KeywordToken == SyntaxKind.ImportKeyword && Utilities.IdText(node) == "meta")
                {
                    return GetGlobalImportMetaExpressionType().Members!["meta"];
                }

                // no other meta properties are valid syntax, thus no others should have symbols
                return null;
            }
        }

        switch (node.Kind)
        {
            case SyntaxKind.Identifier:
            case SyntaxKind.PrivateIdentifier:
            case SyntaxKind.PropertyAccessExpression:
            case SyntaxKind.QualifiedName:
                if (!Utilities.IsThisInTypeQuery(node))
                {
                    return GetSymbolOfNameOrPropertyAccessExpression(node);
                }

                goto case SyntaxKind.ThisKeyword;

            case SyntaxKind.ThisKeyword:
                var container = Utilities.GetThisContainer(node, /*includeArrowFunctions*/ false, /*includeClassComputedPropertyName*/ false);
                if (Utilities.IsFunctionLike(container))
                {
                    var sig = GetSignatureFromDeclaration(container);
                    if (sig.ThisParameter is not null)
                    {
                        return sig.ThisParameter;
                    }
                }

                if (Utilities.IsInExpressionContext(node))
                {
                    return CheckExpression((Expression)node).Symbol;
                }

                goto case SyntaxKind.ThisType;

            case SyntaxKind.ThisType:
                return GetTypeFromThisTypeNode(node).Symbol;

            case SyntaxKind.SuperKeyword:
                return CheckExpression((Expression)node).Symbol;

            case SyntaxKind.ConstructorKeyword:
                // constructor keyword for an overload, should take us to the definition if it exist
                var constructorDeclaration = node.Parent;
                if (constructorDeclaration is not null && constructorDeclaration.Kind == SyntaxKind.Constructor)
                {
                    return ((IDeclaration)constructorDeclaration.Parent!).Symbol;
                }

                return null;

            case SyntaxKind.StringLiteral:
            case SyntaxKind.NoSubstitutionTemplateLiteral:
                // 1). import x = require("./mo/*gotToDefinitionHere*/d")
                // 2). External module name in an import declaration
                // 3). Dynamic import call or require in javascript
                // 4). type A = import("./f/*gotToDefinitionHere*/oo")
                if ((Utilities.IsExternalModuleImportEqualsDeclaration(node.Parent!.Parent!)
                        && ReferenceEquals(Utilities.GetExternalModuleImportEqualsDeclarationExpression(node.Parent!.Parent!), node))
                    || (node.Parent!.Kind == SyntaxKind.ImportDeclaration && ReferenceEquals(((ImportDeclaration)parent).ModuleSpecifier, node))
                    || (node.Parent!.Kind == SyntaxKind.ExportDeclaration && ReferenceEquals(((ExportDeclaration)parent).ModuleSpecifier, node))
                    || (Utilities.IsInJSFile(node) && NodeTests.IsJSDocImportTag(node.Parent!) && ReferenceEquals(((JSDocImportTag)parent).ModuleSpecifier, node))
                    || (Utilities.IsInJSFile(node) && Utilities.IsRequireCall(node.Parent!, /*requireStringLiteralLikeArgument*/ false) || Utilities.IsImportCall(node.Parent!))
                    || (NodeTests.IsLiteralTypeNode(node.Parent!)
                        && Utilities.IsLiteralImportTypeNode(node.Parent!.Parent!)
                        && ReferenceEquals(((ImportTypeNode)parent.Parent!).Argument, node.Parent)))
                {
                    return ResolveExternalModuleName(node, (LiteralExpression)node, ignoreErrors);
                }

                if (NodeTests.IsCallExpression(parent)
                    && Utilities.IsBindableObjectDefinePropertyCall((CallExpression)parent)
                    && ReferenceEquals(Core.ElementAt(((CallExpression)parent).Arguments, 1), node))
                {
                    return GetSymbolOfDeclaration((IDeclaration)parent);
                }

                goto case SyntaxKind.NumericLiteral;

            case SyntaxKind.NumericLiteral:
                // index access
                var objectType = NodeTests.IsElementAccessExpression(parent)
                    ? ReferenceEquals(((ElementAccessExpression)parent).ArgumentExpression, node)
                        ? GetTypeOfExpression(((ElementAccessExpression)parent).Expression!)
                        : null
                    : NodeTests.IsLiteralTypeNode(parent) && NodeTests.IsIndexedAccessTypeNode(grandParent)
                        ? GetTypeFromTypeNode(((IndexedAccessTypeNode)grandParent).ObjectType!)
                        : null;
                return objectType is not null
                    ? GetPropertyOfType(objectType, Utilities.EscapeLeadingUnderscores(((LiteralExpression)node).Text!))
                    : null;

            case SyntaxKind.DefaultKeyword:
            case SyntaxKind.FunctionKeyword:
            case SyntaxKind.EqualsGreaterThanToken:
            case SyntaxKind.ClassKeyword:
                return GetSymbolOfNode(node.Parent!);
            case SyntaxKind.ImportType:
                return Utilities.IsLiteralImportTypeNode(node)
                    ? GetSymbolAtLocation(((LiteralTypeNode)((ImportTypeNode)node).Argument!).Literal, ignoreErrors)
                    : null;

            case SyntaxKind.ExportKeyword:
                return NodeTests.IsExportAssignment(node.Parent!)
                    ? Debug.CheckDefined(((ExportAssignment)parent).Symbol)
                    : null;

            case SyntaxKind.ImportKeyword:
                if (NodeTests.IsMetaProperty(node.Parent!) && ((MetaProperty)parent).Name!.EscapedText == "defer")
                {
                    return null;
                }

                goto case SyntaxKind.NewKeyword;
            case SyntaxKind.NewKeyword:
                return NodeTests.IsMetaProperty(node.Parent!)
                    ? CheckMetaPropertyKeyword((MetaProperty)parent).Symbol
                    : null;
            case SyntaxKind.InstanceOfKeyword:
                if (NodeTests.IsBinaryExpression(node.Parent!))
                {
                    var type = GetTypeOfExpression(((BinaryExpression)parent).Right!);
                    var hasInstanceMethodType = GetSymbolHasInstanceMethodOfObjectType(type);
                    return hasInstanceMethodType?.Symbol ?? type.Symbol;
                }

                return null;
            case SyntaxKind.MetaProperty:
                return CheckExpression((Expression)node).Symbol;
            case SyntaxKind.JsxNamespacedName:
                if (Utilities.IsJSXTagName(node) && IsJsxIntrinsicTagName(node))
                {
                    var symbol = GetIntrinsicTagSymbol(node.Parent!);
                    return ReferenceEquals(symbol, _unknownSymbol) ? null : symbol;
                }

                goto default;

            default:
                return null;
        }
    }

    /// <summary>ported from: src/compiler/checker.ts:50351-50359 — <c>getIndexInfosAtLocation</c>.</summary>
    internal List<IIndexInfo>? GetIndexInfosAtLocation(Node? node)
    {
        if (node is not null
            && NodeTests.IsIdentifier(node)
            && NodeTests.IsPropertyAccessExpression(node.Parent!)
            && ReferenceEquals(((PropertyAccessExpression)node.Parent!).Name, node))
        {
            var keyType = GetLiteralTypeFromPropertyName(node);
            var objectType = GetTypeOfExpression(((PropertyAccessExpression)node.Parent!).Expression!);
            var objectTypes = (objectType.Flags & TypeFlags.Union) != 0
                ? ((IUnionOrIntersectionType)objectType).Types!
                : [objectType];
            return [.. Core.FlatMap<IType, IIndexInfo>(
                objectTypes,
                (IType t, int _) => (object?)Core.Filter(GetIndexInfosOfType(t), (IIndexInfo info) => IsApplicableIndexType(keyType, info.KeyType!)))];
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:50361-50367 — <c>getShorthandAssignmentValueSymbol</c>.</summary>
    internal ISymbol? GetShorthandAssignmentValueSymbol(Node? location)
    {
        if (location is not null && location.Kind == SyntaxKind.ShorthandPropertyAssignment)
        {
            return ResolveEntityName(
                ((ShorthandPropertyAssignment)location).Name!,
                SymbolFlags.Value | SymbolFlags.Alias,
                /*ignoreErrors*/ true);
        }

        return null;
    }

    /// <summary>Returns the target of an export specifier without following aliases.</summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:50369-50380 — <c>getExportSpecifierLocalTargetSymbol</c>.
    /// </remarks>
    internal ISymbol? GetExportSpecifierLocalTargetSymbol(Node? location)
    {
        if (location is not null && NodeTests.IsExportSpecifier(location))
        {
            var node = (ExportSpecifier)location;
            Node? name = node.PropertyName ?? node.Name;
            if (node.Parent!.Parent is ExportDeclaration exportDeclaration)
            {
                if (exportDeclaration.ModuleSpecifier is not null)
                {
                    return GetExternalModuleMember(exportDeclaration, node);
                }

                // Skip for invalid syntax like this: export { "x" }
                return name!.Kind == SyntaxKind.StringLiteral
                    ? null
                    : ResolveEntityName(name, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, /*ignoreErrors*/ true);
            }

            return null;
        }
        else
        {
            return location is not null
                ? ResolveEntityName(location, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, /*ignoreErrors*/ true)
                : null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The type of a node, and the properties a type is queried for (checker.ts:50382-50590)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:50382-50466 — <c>getTypeOfNode</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>tryGetClassImplementingOrExtendingExpressionWithTypeArguments</c> (utilities.ts:7455) and its
    /// record (<c>ClassImplementingOrExtendingExpressionWithTypeArguments</c>, utilities.ts:7450) are not declared
    /// yet; the port calls the name the reference does and reads the record's two members as
    /// <c>Class</c>/<c>IsImplements</c>, the shape every other ported record in the model uses
    /// (<c>Model/NodeInterfaces.cs:2924</c>). The owning slice is named in the slice report.
    /// <para>
    /// PORT-NOTE: TS's <c>node as TypeNode</c> is the port's <see cref="ITypeNode"/> cast (the reference's
    /// <c>isPartOfTypeNode</c> guard is what justifies it), and <c>|| errorType</c> on the two
    /// <c>getTypeForVariableLikeDeclaration</c> results is the port's <c>?? _errorType</c>, because every arm the
    /// reference's <c>||</c> can fall through is the <c>undefined</c> this port's nullable return states.
    /// </para>
    /// </remarks>
    internal IType GetTypeOfNode(Node node)
    {
        if (NodeTests.IsSourceFile(node) && !Parser.IsExternalModule((SourceFile)node))
        {
            return _errorType;
        }

        if ((node.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return _errorType;
        }

        var classDecl = Utilities.TryGetClassImplementingOrExtendingExpressionWithTypeArguments(node);
        var classType = classDecl is not null
            ? GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(classDecl.Class))
            : null;
        if (Utilities.IsPartOfTypeNode(node))
        {
            // PORT-NOTE: the reference's `getTypeFromTypeNode(node as TypeNode)` here is the structural cast its own
            // worker's comment records (checker.ts:20530-20532): `isPartOfTypeNode` (utilities.ts:2788) admits
            // identifiers, qualified names, property accesses, `null` and `this`. The port's marker interface
            // `ITypeNode` is not on those classes, so the arm enters through the worker's node-typed entry point.
            var typeFromTypeNode = GetTypeFromTypeNodeForPartOfTypeNode(node);
            return classType is not null ? GetTypeWithThisArgument(typeFromTypeNode, classType.ThisType) : typeFromTypeNode;
        }

        if (Utilities.IsExpressionNode(node))
        {
            return GetRegularTypeOfExpression((Expression)node);
        }

        if (classType is not null && classDecl is not null && !classDecl.IsImplements)
        {
            // A SyntaxKind.ExpressionWithTypeArguments is considered a type node, except when it occurs in the
            // extends clause of a class. We handle that case here.
            var baseType = Core.FirstOrUndefined(GetBaseTypes(classType!));
            return baseType is not null ? GetTypeWithThisArgument(baseType, classType!.ThisType) : _errorType;
        }

        if (Utilities.IsTypeDeclaration(node))
        {
            // In this case, we call getSymbolOfNode instead of getSymbolAtLocation because it is a declaration
            var symbol = GetSymbolOfDeclaration((IDeclaration)node);
            return GetDeclaredTypeOfSymbol(symbol);
        }

        if (IsTypeDeclarationName(node))
        {
            var symbol = GetSymbolAtLocation(node);
            return symbol is not null ? GetDeclaredTypeOfSymbol(symbol) : _errorType;
        }

        if (NodeTests.IsBindingElement(node))
        {
            return GetTypeForVariableLikeDeclaration(node, /*includeOptionality*/ true, CheckMode.Normal) ?? _errorType;
        }

        if (Utilities.IsDeclaration(node))
        {
            // In this case, we call getSymbolOfNode instead of getSymbolAtLocation because it is a declaration
            var symbol = GetSymbolOfDeclaration((IDeclaration)node);
            return symbol is not null ? GetTypeOfSymbol(symbol) : _errorType;
        }

        if (IsDeclarationNameOrImportPropertyName(node))
        {
            var symbol = GetSymbolAtLocation(node);
            if (symbol is not null)
            {
                return GetTypeOfSymbol(symbol);
            }

            return _errorType;
        }

        if (Utilities.IsBindingPattern(node))
        {
            return GetTypeForVariableLikeDeclaration(node.Parent!, /*includeOptionality*/ true, CheckMode.Normal) ?? _errorType;
        }

        if (IsInRightSideOfImportOrExportAssignment(node))
        {
            var symbol = GetSymbolAtLocation(node);
            if (symbol is not null)
            {
                var declaredType = GetDeclaredTypeOfSymbol(symbol);
                return !IsErrorType(declaredType) ? declaredType : GetTypeOfSymbol(symbol);
            }
        }

        if (NodeTests.IsMetaProperty(node.Parent!) && ((MetaProperty)node.Parent!).KeywordToken == node.Kind)
        {
            return CheckMetaPropertyKeyword((MetaProperty)node.Parent!);
        }

        if (NodeTests.IsImportAttributes(node))
        {
            return GetGlobalImportAttributesType(/*reportErrors*/ false);
        }

        return _errorType;
    }

    /// <summary>
    /// Gets the type of object literal or array literal of destructuring assignment.
    /// <code>
    /// { a } from
    ///     for ( { a } of elems) {
    ///     }
    /// [ a ] from
    ///     [a] = [ some array ...]
    /// </code>
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:50468-50503 — <c>getTypeOfAssignmentPattern</c>.</remarks>
    internal IType GetTypeOfAssignmentPattern(MemberExpression? expr)
    {
        Debug.Assert(
            expr is not null && (expr.Kind == SyntaxKind.ObjectLiteralExpression || expr.Kind == SyntaxKind.ArrayLiteralExpression));
        var node = expr!;
        // If this is from "for of"
        //     for ( { a } of elems) {
        //     }
        if (node.Parent!.Kind == SyntaxKind.ForOfStatement)
        {
            var iteratedType = CheckRightHandSideOfForOf((ForOfStatement)node.Parent);
            return CheckDestructuringAssignment(node, iteratedType ?? _errorType);
        }

        // If this is from "for" initializer
        //     for ({a } = elems[0];.....) { }
        if (node.Parent!.Kind == SyntaxKind.BinaryExpression)
        {
            var iteratedType = GetTypeOfExpression(((BinaryExpression)node.Parent).Right!);
            return CheckDestructuringAssignment(node, iteratedType ?? _errorType);
        }

        // If this is from nested object binding pattern
        //     for ({ skills: { primary, secondary } } = multiRobot, i = 0; i < 1; i++) {
        if (node.Parent!.Kind == SyntaxKind.PropertyAssignment)
        {
            var parentObjectLiteral = (ObjectLiteralExpression)node.Parent.Parent!;
            var typeOfParentObjectLiteral = GetTypeOfAssignmentPattern(parentObjectLiteral) ?? _errorType;
            var propertyIndex = Utilities.IndexOfNode(parentObjectLiteral.Properties!, node.Parent);
            return CheckObjectLiteralDestructuringPropertyAssignment(parentObjectLiteral, typeOfParentObjectLiteral, propertyIndex)!;
        }

        // Array literal assignment - array destructuring pattern
        var arrayLiteral = (ArrayLiteralExpression)node.Parent!;
        //    [{ property1: p1, property2 }] = elems;
        var typeOfArrayLiteral = GetTypeOfAssignmentPattern(arrayLiteral) ?? _errorType;
        var elementType = CheckIteratedTypeOrElementType(IterationUse.Destructuring, typeOfArrayLiteral, _undefinedType, node.Parent) ?? _errorType;
        // PORT-NOTE: the reference's checkArrayLiteralDestructuringElementAssignment answers `Type` (non-null); the
        // port's member is declared `IType?`, so the invariant is asserted at this call.
        return CheckArrayLiteralDestructuringElementAssignment(
            arrayLiteral,
            typeOfArrayLiteral,
            arrayLiteral.Elements!.IndexOf(node),
            elementType)!;
    }

    /// <summary>
    /// Gets the property symbol corresponding to the property in destructuring assignment
    /// <code>
    /// 'property1' from
    ///     for ( { property1: a } of elems) {
    ///     }
    /// 'property1' at location 'a' from:
    ///     [a] = [ property1, property2 ]
    /// </code>
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:50505-50509 — <c>getPropertySymbolOfDestructuringAssignment</c>.</remarks>
    internal ISymbol? GetPropertySymbolOfDestructuringAssignment(Identifier? location)
    {
        if (location is null) return null;

        // Get the type of the object or array literal and then look for property of given name in the type
        var typeOfObjectLiteral = GetTypeOfAssignmentPattern((MemberExpression)location.Parent!.Parent!);
        return typeOfObjectLiteral is not null ? GetPropertyOfType(typeOfObjectLiteral, location.EscapedText) : null;
    }

    /// <summary>ported from: src/compiler/checker.ts:50511-50520 — <c>getRegularTypeOfExpression</c>.</summary>
    internal IType GetRegularTypeOfExpression(Expression expr)
    {
        if (Utilities.IsRightSideOfQualifiedNameOrPropertyAccess(expr))
        {
            expr = (Expression)expr.Parent!;
        }

        return GetRegularTypeOfLiteralType(GetTypeOfExpression(expr));
    }

    /// <summary>
    /// Gets either the static or instance type of a class element, based on
    /// whether the element is declared as "static".
    /// </summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:50522-50527 — <c>getParentTypeOfClassElement</c>.
    /// PORT-NOTE: TS's parameter is the <c>ClassElement</c> union, which has no C# type (§7.5), so it is a
    /// <see cref="Node"/> and the caller casts.
    /// </remarks>
    internal IType GetParentTypeOfClassElement(Node node)
    {
        var classSymbol = GetSymbolOfNode(node.Parent!)!;
        return Utilities.IsStatic(node)
            ? GetTypeOfSymbol(classSymbol)
            : GetDeclaredTypeOfSymbol(classSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:50529-50545 — <c>getClassElementPropertyKeyType</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: as <see cref="GetParentTypeOfClassElement"/> the parameter is the erased <c>ClassElement</c> union,
    /// and its <c>name</c> is read through the <see cref="INamedDeclaration"/> face the class elements share.
    /// TS's <c>default: return Debug.fail(…)</c> is the port's void <see cref="Debug.Fail(string?)"/> followed by the
    /// throw the reader expects (<c>Checker/Checker.Support.cs:192</c>).
    /// </remarks>
    internal IType GetClassElementPropertyKeyType(Node element)
    {
        var name = ((INamedDeclaration)element).Name!;
        switch (name.Kind)
        {
            case SyntaxKind.Identifier:
                return GetStringLiteralType(Utilities.IdText(name));
            case SyntaxKind.NumericLiteral:
            case SyntaxKind.StringLiteral:
                return GetStringLiteralType(((LiteralExpression)name).Text!);
            case SyntaxKind.ComputedPropertyName:
                var nameType = CheckComputedPropertyName((ComputedPropertyName)name);
                return IsTypeAssignableToKind(nameType, TypeFlags.ESSymbolLike) ? nameType : _stringType;
            default:
                Debug.Fail("Unsupported property name.");
                throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>
    /// Return the list of properties of the given type, augmented with properties from Function
    /// if the type has call or construct signatures.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:50547-50561 — <c>getAugmentedPropertiesOfType</c>.</remarks>
    internal List<ISymbol> GetAugmentedPropertiesOfType(IType? type)
    {
        var apparentType = GetApparentType(type);
        var propsByName = Utilities.CreateSymbolTable(GetPropertiesOfType(apparentType));
        var functionType = GetSignaturesOfType(apparentType, SignatureKind.Call).Count != 0 ? _globalCallableFunctionType :
            GetSignaturesOfType(apparentType, SignatureKind.Construct).Count != 0 ? _globalNewableFunctionType :
            null;
        if (functionType is not null)
        {
            foreach (var p in GetPropertiesOfType(functionType))
            {
                if (!propsByName.ContainsKey(p.EscapedName!))
                {
                    propsByName[p.EscapedName!] = p;
                }
            }
        }

        return GetNamedMembers(propsByName, /*container*/ null);
    }

    /// <summary>ported from: src/compiler/checker.ts:50563-50565 — <c>typeHasCallOrConstructSignatures</c>.</summary>
    internal bool TypeHasCallOrConstructSignatures(IType? type)
        => GetSignaturesOfType(type!, SignatureKind.Call).Count != 0 || GetSignaturesOfType(type!, SignatureKind.Construct).Count != 0;

    /// <summary>ported from: src/compiler/checker.ts:50567-50569 — <c>getRootSymbols</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>flatMap(roots, getRootSymbols)</c> is the port's <c>Core.FlatMap</c>, which answers an
    /// <see cref="IList{T}"/> that is copied into the list this member returns.
    /// </remarks>
    internal List<ISymbol> GetRootSymbols(ISymbol? symbol)
    {
        var roots = GetImmediateRootSymbols(symbol!);
        return roots is not null
            ? [.. Core.FlatMap<ISymbol, ISymbol>(roots, (ISymbol s, int _) => (object?)GetRootSymbols(s))!]
            : [symbol!];
    }

    /// <summary>ported from: src/compiler/checker.ts:50571-50581 — <c>getImmediateRootSymbols</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS destructures a transient symbol's links (<c>leftSpread</c>/<c>rightSpread</c>/
    /// <c>syntheticOrigin</c>) off the symbol; the port reads the same three slots through
    /// <see cref="GetSymbolLinks"/>, where <c>Model/SymbolLinks.cs</c> keeps them. The three-way conditional is
    /// stated as three guarded returns so each result's collection-expression target is the method's return type.
    /// </remarks>
    internal IList<ISymbol>? GetImmediateRootSymbols(ISymbol symbol)
    {
        if ((GetCheckFlags(symbol) & CheckFlags.Synthetic) != 0)
        {
            return Core.MapDefined<IType, ISymbol>(
                GetSymbolLinks(symbol).ContainingType!.Types!,
                (IType type, int _) => GetPropertyOfType(type, symbol.EscapedName));
        }
        else if ((symbol.Flags & SymbolFlags.Transient) != 0)
        {
            var links = GetSymbolLinks(symbol);
            if (links.LeftSpread is not null) return [links.LeftSpread, links.RightSpread!];
            if (links.SyntheticOrigin is not null) return [links.SyntheticOrigin];
            return Core.SingleElementArray(TryGetTarget(symbol));
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:50583-50590 — <c>tryGetTarget</c>.</summary>
    internal ISymbol? TryGetTarget(ISymbol symbol)
    {
        ISymbol? target = null;
        var current = symbol;
        while (true)
        {
            var next = GetSymbolLinks(current).Target;
            if (next is null) break;

            target = next;
            current = next;
        }

        return target;
    }

    // ---------------------------------------------------------------------------------------------
    // Emitter support (checker.ts:50594-50853)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/checker.ts:50594-50605 — <c>isArgumentsLocalBinding</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getParseTreeNode(nodeIn, isIdentifier)</c> answers a narrowed node in TS; the port's generic
    /// overload names the type argument (<c>Utilities.GetParseTreeNode&lt;Identifier&gt;</c>). The reference's
    /// <c>parent.name === node</c> reads the <see cref="INamedDeclaration"/> face both parent kinds declare.
    /// </remarks>
    internal bool IsArgumentsLocalBinding(Identifier? nodeIn)
    {
        // Note: does not handle isShorthandPropertyAssignment (and probably a few more)
        if (nodeIn is null || Utilities.IsGeneratedIdentifier(nodeIn)) return false;
        var node = Utilities.GetParseTreeNode<Identifier>(nodeIn, NodeTests.IsIdentifier);
        if (node is null) return false;
        var parent = node.Parent;
        if (parent is null) return false;
        var isPropertyName = (NodeTests.IsPropertyAccessExpression(parent)
            || NodeTests.IsPropertyAssignment(parent))
            && ReferenceEquals(((INamedDeclaration)parent).Name, node);
        return !isPropertyName && ReferenceEquals(GetReferencedValueSymbol(node), _argumentsSymbol);
    }

    /// <summary>ported from: src/compiler/checker.ts:50607-50609 — <c>isNameOfModuleOrEnumDeclaration</c>.</summary>
    internal bool IsNameOfModuleOrEnumDeclaration(Identifier node)
        => Utilities.IsModuleOrEnumDeclaration(node.Parent!) && ReferenceEquals(((INamedDeclaration)node.Parent!).Name, node);

    /// <summary>
    /// When resolved as an expression identifier, if the given node references an exported entity, return the declaration
    /// node of the exported entity's container. Otherwise, return undefined.
    /// </summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:50613-50647 — <c>getReferencedExportContainer</c>.
    /// PORT-NOTE: TS's result is the <c>SourceFile | ModuleDeclaration | EnumDeclaration | undefined</c> union, which
    /// erases to the <see cref="Node"/> the port answers (the interface's face is <c>Node?</c>); the reference's
    /// <c>findAncestor</c> type guard is the port's <c>bool</c> predicate.
    /// </remarks>
    internal Node? GetReferencedExportContainer(Identifier? nodeIn, bool? prefixLocals)
    {
        var node = Utilities.GetParseTreeNode<Identifier>(nodeIn, NodeTests.IsIdentifier);
        if (node is not null)
        {
            // When resolving the export container for the name of a module or enum
            // declaration, we need to start resolution at the declaration's container.
            // Otherwise, we could incorrectly resolve the export container as the
            // declaration if it contains an exported member with the same name.
            var symbol = GetReferencedValueSymbol(node, /*startInDeclarationContainer*/ IsNameOfModuleOrEnumDeclaration(node));
            if (symbol is not null)
            {
                if ((symbol.Flags & SymbolFlags.ExportValue) != 0)
                {
                    // If we reference an exported entity within the same module declaration, then whether
                    // we prefix depends on the kind of entity. SymbolFlags.ExportHasLocal encompasses all the
                    // kinds that we do NOT prefix.
                    var exportSymbol = GetMergedSymbol(symbol.ExportSymbol!)!;
                    if (prefixLocals != true
                        && (exportSymbol.Flags & SymbolFlags.ExportHasLocal) != 0
                        && (exportSymbol.Flags & SymbolFlags.Variable) == 0)
                    {
                        return null;
                    }

                    symbol = exportSymbol;
                }

                var parentSymbol = GetParentOfSymbol(symbol);
                if (parentSymbol is not null)
                {
                    if ((parentSymbol.Flags & SymbolFlags.ValueModule) != 0 && parentSymbol.ValueDeclaration?.Kind == SyntaxKind.SourceFile)
                    {
                        var symbolFile = (SourceFile)parentSymbol.ValueDeclaration;
                        var referenceFile = Utilities.GetSourceFileOfNode(node);
                        // If `node` accesses an export and that export isn't in the same file, then symbol is a namespace export, so return undefined.
                        var symbolIsUmdExport = !ReferenceEquals(symbolFile, referenceFile);
                        return symbolIsUmdExport ? null : symbolFile;
                    }

                    return Utilities.FindAncestor(node.Parent!, (Node n) =>
                        Utilities.IsModuleOrEnumDeclaration(n) && ReferenceEquals(GetSymbolOfDeclaration((IDeclaration)n), parentSymbol));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// When resolved as an expression identifier, if the given node references an import, return the declaration of
    /// that import. Otherwise, return undefined.
    /// </summary>
    /// <remarks>
    /// ported from: src/compiler/checker.ts:50649-50666 — <c>getReferencedImportDeclaration</c>.
    /// PORT-NOTE: <c>getIdentifierGeneratedImportReference</c> is a <c>factory/emitNode.ts</c> member
    /// (emitNode.ts:379) and so is an <c>EmitNodes</c> member here (<c>Parser/EmitNodes.cs:411</c>, called qualified —
    /// the earlier note that it was a <c>Utilities</c> member was wrong), while <c>getReferencedValueOrAliasSymbol</c>
    /// (checker.ts:51251) stays unqualified — the reference's declaration site is what decides.
    /// </remarks>
    internal IDeclaration? GetReferencedImportDeclaration(Identifier? nodeIn)
    {
        if (nodeIn is null) return null;

        var specifier = EmitNodes.GetIdentifierGeneratedImportReference(nodeIn);
        if (specifier is not null)
        {
            return specifier;
        }

        var node = Utilities.GetParseTreeNode<Identifier>(nodeIn, NodeTests.IsIdentifier);
        if (node is not null)
        {
            var symbol = GetReferencedValueOrAliasSymbol(node);

            // We should only get the declaration of an alias if there isn't a local value
            // declaration for the symbol
            if (IsNonLocalAlias(symbol, /*excludes*/ SymbolFlags.Value)
                && GetTypeOnlyAliasDeclaration(symbol!, SymbolFlags.Value) is null)
            {
                return GetDeclarationOfAliasSymbol(symbol!);
            }
        }

        return null;
    }

    /// <summary>ported from: src/compiler/checker.ts:50668-50672 — <c>isSymbolOfDestructuredElementOfCatchBinding</c>.</summary>
    internal bool IsSymbolOfDestructuredElementOfCatchBinding(ISymbol symbol)
        => symbol.ValueDeclaration is not null
            && NodeTests.IsBindingElement((Node)symbol.ValueDeclaration)
            && Utilities.WalkUpBindingElementsAndPatterns((BindingElement)symbol.ValueDeclaration).Parent!.Kind == SyntaxKind.CatchClause;

    /// <summary>ported from: src/compiler/checker.ts:50674-50717 — <c>isSymbolOfDeclarationWithCollidingName</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>links?.referenced</c>-style tri-state reads on <c>isDeclarationWithCollidingName</c>
    /// (<c>=== undefined</c> means "not computed"), which the port keeps as <c>bool?</c> and reads with
    /// <c>is null</c>/<c>.Value</c>. <c>hasNodeCheckFlag</c> (checker.ts:50925) and <c>resolveName</c> are checker
    /// members, so both are unqualified; <c>isStatementWithLocals</c> (utilities.ts:1002) is not.
    /// </remarks>
    internal bool IsSymbolOfDeclarationWithCollidingName(ISymbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.BlockScoped) != 0
            && symbol.ValueDeclaration is not null
            && !NodeTests.IsSourceFile((Node)symbol.ValueDeclaration))
        {
            var links = GetSymbolLinks(symbol);
            if (links.IsDeclarationWithCollidingName is null)
            {
                var container = Utilities.GetEnclosingBlockScopeContainer((Node)symbol.ValueDeclaration);
                if (Utilities.IsStatementWithLocals(container) || IsSymbolOfDestructuredElementOfCatchBinding(symbol))
                {
                    if (ResolveName(
                            container.Parent!,
                            symbol.EscapedName!,
                            SymbolFlags.Value,
                            /*nameNotFoundMessage*/ null,
                            /*isUse*/ false,
                            /*excludeGlobals*/ false) is not null)
                    {
                        // redeclaration - always should be renamed
                        links.IsDeclarationWithCollidingName = true;
                    }
                    else if (HasNodeCheckFlag((Node)symbol.ValueDeclaration, NodeCheckFlags.CapturedBlockScopedBinding))
                    {
                        // binding is captured in the function
                        // should be renamed if:
                        // - binding is not top level - top level bindings never collide with anything
                        // AND
                        //   - binding is not declared in loop, should be renamed to avoid name reuse across siblings
                        //     let a, b
                        //     { let x = 1; a = () => x; }
                        //     { let x = 100; b = () => x; }
                        //     console.log(a()); // should print '1'
                        //     console.log(b()); // should print '100'
                        //     OR
                        //   - binding is declared inside loop but not in inside initializer of iteration statement or directly inside loop body
                        //     * variables from initializer are passed to rewritten loop body as parameters so they are not captured directly
                        //     * variables that are declared immediately in loop body will become top level variable after loop is rewritten and thus
                        //       they will not collide with anything
                        var isDeclaredInLoop = HasNodeCheckFlag((Node)symbol.ValueDeclaration, NodeCheckFlags.BlockScopedBindingInLoop);
                        var inLoopInitializer = Utilities.IsIterationStatement(container, /*lookInLabeledStatements*/ false);
                        var inLoopBodyBlock = container.Kind == SyntaxKind.Block
                            && Utilities.IsIterationStatement(container.Parent!, /*lookInLabeledStatements*/ false);

                        links.IsDeclarationWithCollidingName = !Utilities.IsBlockScopedContainerTopLevel(container)
                            && (!isDeclaredInLoop || (!inLoopInitializer && !inLoopBodyBlock));
                    }
                    else
                    {
                        links.IsDeclarationWithCollidingName = false;
                    }
                }
            }

            return links.IsDeclarationWithCollidingName!.Value;
        }

        return false;
    }

    /// <summary>
    /// When resolved as an expression identifier, if the given node references a nested block scoped entity with
    /// a name that either hides an existing name or might hide it when compiled downlevel,
    /// return the declaration of that entity. Otherwise, return undefined.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:50719-50733 — <c>getReferencedDeclarationWithCollidingName</c>.</remarks>
    internal IDeclaration? GetReferencedDeclarationWithCollidingName(Identifier? nodeIn)
    {
        if (nodeIn is not null && !Utilities.IsGeneratedIdentifier(nodeIn))
        {
            var node = Utilities.GetParseTreeNode<Identifier>(nodeIn, NodeTests.IsIdentifier);
            if (node is not null)
            {
                var symbol = GetReferencedValueSymbol(node);
                if (symbol is not null && IsSymbolOfDeclarationWithCollidingName(symbol))
                {
                    return symbol.ValueDeclaration;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Return true if the given node is a declaration of a nested block scoped entity with a name that either hides an
    /// existing name or might hide a name when compiled downlevel.
    /// </summary>
    /// <remarks>ported from: src/compiler/checker.ts:50735-50745 — <c>isDeclarationWithCollidingName</c>.</remarks>
    internal bool IsDeclarationWithCollidingName(IDeclaration? nodeIn)
    {
        if (nodeIn is not null)
        {
            var node = Utilities.GetParseTreeNode((Node)nodeIn, Utilities.IsDeclaration) as IDeclaration;
            if (node is not null)
            {
                var symbol = GetSymbolOfDeclaration(node);
                if (symbol is not null)
                {
                    return IsSymbolOfDeclarationWithCollidingName(symbol);
                }
            }
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:50747-50770 — <c>isValueAliasDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>isAliasSymbolDeclaration</c>/<c>getSymbolOfDeclaration</c> are checker members, and the
    /// reference's <c>some(exportClause.elements, isValueAliasDeclaration)</c> is a <c>Core.Some</c> whose predicate
    /// names the element type (<c>ExportSpecifier</c>), which is what the union member the reference reads holds.
    /// </remarks>
    internal bool IsValueAliasDeclaration(Node? node)
    {
        Debug.Assert(_canCollectSymbolAliasAccessabilityData);
        if (node is null) return false;
        switch (node.Kind)
        {
            case SyntaxKind.ImportEqualsDeclaration:
                return IsAliasResolvedToValue(GetSymbolOfDeclaration((IDeclaration)node));
            case SyntaxKind.ImportClause:
            case SyntaxKind.NamespaceImport:
            case SyntaxKind.ImportSpecifier:
            case SyntaxKind.ExportSpecifier:
                var symbol = GetSymbolOfDeclaration((IDeclaration)node);
                return IsAliasResolvedToValue(symbol, /*excludeTypeOnlyValues*/ true);
            case SyntaxKind.ExportDeclaration:
                var exportClause = ((ExportDeclaration)node).ExportClause;
                return exportClause is not null && (
                    NodeTests.IsNamespaceExport(exportClause) ||
                    Core.Some(((NamedExports)exportClause).Elements, (ExportSpecifier element) => IsValueAliasDeclaration(element))
                );
            case SyntaxKind.ExportAssignment:
                return ((ExportAssignment)node).Expression is not null
                    && ((ExportAssignment)node).Expression!.Kind == SyntaxKind.Identifier
                    ? IsAliasResolvedToValue(GetSymbolOfDeclaration((IDeclaration)node), /*excludeTypeOnlyValues*/ true)
                    : true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:50772-50781 — <c>isTopLevelValueImportEqualsWithEntityName</c>.</summary>
    internal bool IsTopLevelValueImportEqualsWithEntityName(ImportEqualsDeclaration? nodeIn)
    {
        var node = Utilities.GetParseTreeNode<ImportEqualsDeclaration>(nodeIn, NodeTests.IsImportEqualsDeclaration);
        if (node is null || node.Parent!.Kind != SyntaxKind.SourceFile || !Utilities.IsInternalModuleImportEqualsDeclaration(node))
        {
            // parent is not source file or it is not reference to internal module
            return false;
        }

        var importEquals = node!;
        var isValue = IsAliasResolvedToValue(GetSymbolOfDeclaration(importEquals));
        return isValue && importEquals.ModuleReference is not null && !Utilities.NodeIsMissing(importEquals.ModuleReference);
    }

    /// <summary>ported from: src/compiler/checker.ts:50783-50801 — <c>isAliasResolvedToValue</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>void resolveExternalModuleSymbol(fileSymbol)</c> — a call kept for its side effect — is the
    /// port's discard assignment; <c>shouldPreserveConstEnums</c> (utilities.ts) is a <c>Utilities</c> member.
    /// </remarks>
    internal bool IsAliasResolvedToValue(ISymbol? symbol, bool? excludeTypeOnlyValues = null)
    {
        if (symbol is null)
        {
            return false;
        }

        var container = Utilities.GetSourceFileOfNode((Node?)symbol.ValueDeclaration);
        var fileSymbol = container is not null ? GetSymbolOfDeclaration(container) : null;
        // Ensures cjs export assignment is setup, since this symbol may point at, and merge with, the file itself.
        // If we don't, the merge may not have yet occured, and the flags check below will be missing flags that
        // are added as a result of the merge.
        _ = ResolveExternalModuleSymbol(fileSymbol);
        var target = GetExportSymbolOfValueSymbolIfExported(ResolveAlias(symbol));
        if (ReferenceEquals(target, _unknownSymbol))
        {
            return excludeTypeOnlyValues != true || GetTypeOnlyAliasDeclaration(symbol) is null;
        }

        // const enums and modules that contain only const enums are not considered values from the emit perspective
        // unless 'preserveConstEnums' option is set to true
        return (GetSymbolFlags(symbol, excludeTypeOnlyValues, /*excludeLocalMeanings*/ true) & SymbolFlags.Value) != 0
            && (Utilities.ShouldPreserveConstEnums(_compilerOptions) || !IsConstEnumOrConstEnumOnlyModule(target!));
    }

    /// <summary>ported from: src/compiler/checker.ts:50803-50805 — <c>isConstEnumOrConstEnumOnlyModule</c>.</summary>
    internal bool IsConstEnumOrConstEnumOnlyModule(ISymbol s)
        => IsConstEnumSymbol(s) || s.ConstEnumOnlyModule == true;

    /// <summary>ported from: src/compiler/checker.ts:50807-50830 — <c>isReferencedAliasDeclaration</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's <c>forEachChild(node, node =&gt; isReferencedAliasDeclaration(node, checkChildren))</c>
    /// is the port's <c>ForEachChildTable.ForEachChild</c> (the generic overload, whose <c>bool?</c> result the
    /// reference's <c>!!</c> turns into a <c>== true</c>), and its <c>links?.referenced</c> is the tri-state
    /// <c>SymbolLinks.Referenced</c>.
    /// </remarks>
    internal bool IsReferencedAliasDeclaration(Node? node, bool? checkChildren)
    {
        Debug.Assert(_canCollectSymbolAliasAccessabilityData);
        if (node is not null && IsAliasSymbolDeclaration(node))
        {
            var symbol = GetSymbolOfDeclaration((IDeclaration)node!);
            var links = GetSymbolLinks(symbol);
            if (links.Referenced == true)
            {
                return true;
            }

            var target = links.AliasTarget;
            if (target is not null
                && (Utilities.GetEffectiveModifierFlags(node!) & ModifierFlags.Export) != 0
                && (GetSymbolFlags(target) & SymbolFlags.Value) != 0
                && (Utilities.ShouldPreserveConstEnums(_compilerOptions) || !IsConstEnumOrConstEnumOnlyModule(target)))
            {
                // An `export import ... =` of a value symbol is always considered referenced
                return true;
            }
        }

        if (checkChildren == true)
        {
            return ForEachChildTable.ForEachChild(node!, (Node n) => IsReferencedAliasDeclaration(n, checkChildren)) == true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/checker.ts:50832-50853 — <c>isImplementationOfOverload</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>node as FunctionLikeDeclaration</c> is the port's
    /// <see cref="IFunctionLikeDeclarationBase"/> cast for the <c>body</c> read (the erased
    /// <c>SignatureDeclaration</c> union has no C# type, §7.5); <c>isGetAccessor</c>/<c>isSetAccessor</c> are
    /// <c>utilitiesPublic.ts</c> members and so are <c>Utilities</c> members.
    /// </remarks>
    internal bool? IsImplementationOfOverload(Node? node)
    {
        if (node is not null && Utilities.NodeIsPresent(((IFunctionLikeDeclarationBase)node).Body))
        {
            if (Utilities.IsGetAccessor(node) || Utilities.IsSetAccessor(node)) return false; // Get or set accessors can never be overload implementations, but can have up to 2 signatures
            var symbol = GetSymbolOfDeclaration((IDeclaration)node);
            var signaturesOfSymbol = GetSignaturesOfSymbol(symbol);
            // If this function body corresponds to function with multiple signature, it is implementation of overload
            // e.g.: function foo(a: string): string;
            //       function foo(a: number): number;
            //       function foo(a: any) { // This is implementation of the overloads
            //           return a;
            //       }
            return signaturesOfSymbol.Count > 1
                // If there is single signature for the symbol, it is overload if that signature isn't coming from the node
                // e.g.: function foo(a: string): string;
                //       function foo(a: any) { // This is implementation of the overloads
                //           return a;
                //       }
                || (signaturesOfSymbol.Count == 1 && !ReferenceEquals(signaturesOfSymbol[0].Declaration, node));
        }

        return false;
    }
}
