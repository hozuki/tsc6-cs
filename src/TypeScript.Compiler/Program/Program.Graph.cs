namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:1526-1735 (the state the file graph mutates — see Program.State.cs)
// ported from: src/compiler/program.ts:3291-3997 (processRootFile … processImportedModules)
// ported from: src/compiler/program.ts:3421-3443, 3533-3543 (getLibFileFromReference, getSourceFileFromReference, getCreateSourceFileOptions)
/// <summary>
/// The file graph: root files, triple-slash and module references, and the source-file lookup every one of them goes
/// through.
/// </summary>
internal sealed partial class Program
{
    /// <summary>ported from: src/compiler/program.ts:3291-3293 — <c>processRootFile</c>.</summary>
    internal void ProcessRootFile(string fileName, bool isDefaultLib, IFileIncludeReason reason)
        => ProcessSourceFile(Paths.NormalizePath(fileName), isDefaultLib, /*packageId*/ null, reason);

    /// <summary>ported from: src/compiler/program.ts:3295-3297 — <c>fileReferenceIsEqualTo</c>.</summary>
    private static bool FileReferenceIsEqualTo(IFileReference a, IFileReference b) => a.FileName == b.FileName;

    /// <summary>ported from: src/compiler/program.ts:3299-3303 — <c>moduleNameIsEqualTo</c>.</summary>
    /// <remarks>PORT-NOTE: TS's parameter is <c>StringLiteralLike | Identifier</c>; the two kinds are tested before the
    /// cast, exactly as the reference's ternary does.</remarks>
    private static bool ModuleNameIsEqualTo(Node a, Node b)
        => a.Kind == SyntaxKind.Identifier
            ? b.Kind == SyntaxKind.Identifier && ((Identifier)a).EscapedText == ((Identifier)b).EscapedText
            : b.Kind == SyntaxKind.StringLiteral && ((StringLiteral)a).Text == ((StringLiteral)b).Text;

    /// <summary>ported from: src/compiler/program.ts:3305-3316 — <c>createSyntheticImport</c>.</summary>
    /// <remarks>PORT-NOTE: TS casts to <c>Mutable&lt;Node&gt;</c> to clear the synthesized flag; the port's nodes are
    /// mutable, so the flag write is direct.</remarks>
    private LiteralExpression CreateSyntheticImport(string text, SourceFile file)
    {
        var externalHelpersModuleReference = NodeFactory.Factory.CreateStringLiteral(text);
        var importDecl = NodeFactory.Factory.CreateImportDeclaration(/*modifiers*/ null, /*importClause*/ null, externalHelpersModuleReference, /*attributes*/ null);
        EmitNodes.AddInternalEmitFlags(importDecl, InternalEmitFlags.NeverApplyImportHelper);
        Utilities.SetParent(externalHelpersModuleReference, importDecl);
        Utilities.SetParent(importDecl, file);
        // explicitly unset the synthesized flag on these declarations so the checker API will answer questions about them
        // (which is required to build the dependency graph for incremental emit)
        externalHelpersModuleReference.Flags &= ~NodeFlags.Synthesized;
        importDecl.Flags &= ~NodeFlags.Synthesized;
        return externalHelpersModuleReference;
    }

    /// <summary>ported from: src/compiler/program.ts:3318-3419 — <c>collectExternalModuleReferences</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's three locals are <c>undefined</c> until the first append (and the three <c>file.*</c> slots
    /// receive <c>emptyArray</c> when nothing was collected); the port's start as <c>null</c> and are replaced by an
    /// empty list at the end. The nested <c>collectModuleReferences</c> is a local function so it can close over them.
    /// </remarks>
    internal void CollectExternalModuleReferences(SourceFile file)
    {
        if (file.Imports is not null)
        {
            return;
        }

        var isJavaScriptFile = Utilities.IsSourceFileJS(file);
        var isExternalModuleFile = Parser.IsExternalModule(file);

        // file.imports may not be undefined if there exists dynamic import
        List<LiteralExpression>? imports = null;
        // PORT-NOTE: TS's local is `(StringLiteral | Identifier)[]`, the element type the generated
        // `SourceFile.moduleAugmentations` slot spells `List<MemberExpression>` (the two kinds' common face).
        List<MemberExpression>? moduleAugmentations = null;
        List<string>? ambientModules = null;

        // If we are importing helpers, we need to add a synthetic reference to resolve the
        // helpers library. (A JavaScript file without `externalModuleIndicator` set might be
        // a CommonJS module; `commonJsModuleIndicator` doesn't get set until the binder has
        // run. We synthesize a helpers import for it just in case; it will never be used if
        // the binder doesn't find and set a `commonJsModuleIndicator`.)
        if (isJavaScriptFile || !file.IsDeclarationFile && (Utilities.GetIsolatedModules(_options) || Parser.IsExternalModule(file)))
        {
            if (Js.IsTruthy(_options.ImportHelpers))
            {
                // synthesize 'import "tslib"' declaration
                imports = [CreateSyntheticImport(Utilities.ExternalHelpersModuleNameText, file)];
            }

            var jsxImport = Utilities.GetJsxRuntimeImport(Utilities.GetJsxImplicitImportBase(_options, file), _options);
            if (jsxImport is not null)
            {
                // synthesize `import "base/jsx-runtime"` declaration
                (imports ??= []).Add(CreateSyntheticImport(jsxImport, file));
            }
        }

        foreach (var node in file.Statements ?? [])
        {
            // The generated `statements` slot is `NodeArray<IStatement>` while the local function takes the class
            // `Node` (TS's `Statement[]` satisfies `Node` directly); the reference's own loop passes the statement.
            CollectModuleReferences((Node)node, /*inAmbientModule*/ false);
        }

        if ((file.Flags & NodeFlags.PossiblyContainsDynamicImport) != 0 || isJavaScriptFile)
        {
            Utilities.ForEachDynamicImportOrRequireCall(
                file,
                /*includeTypeSpaceImports*/ true,
                /*requireStringLiteralLikeArgument*/ true,
                (node, moduleSpecifier) =>
                {
                    Utilities.SetParentRecursive(node, /*incremental*/ false); // we need parent data on imports before the program is fully bound, so we ensure it's set here
                    (imports ??= []).Add((LiteralExpression)moduleSpecifier);
                });
        }

        file.Imports = imports ?? [];
        file.ModuleAugmentations = moduleAugmentations ?? [];
        file.AmbientModuleNames = ambientModules ?? [];
        return;

        void CollectModuleReferences(Node node, bool inAmbientModule)
        {
            if (Utilities.IsAnyImportOrReExport(node))
            {
                var moduleNameExpr = Utilities.GetExternalModuleName(node);
                // TypeScript 1.0 spec (April 2014): 12.1.6
                // An ExternalImportDeclaration in an AmbientExternalModuleDeclaration may reference other external modules
                // only through top - level external module names. Relative external module names are not permitted.
                if (moduleNameExpr is StringLiteral stringLiteral
                    && !string.IsNullOrEmpty(stringLiteral.Text)
                    && (!inAmbientModule || !Utilities.IsExternalModuleNameRelative(stringLiteral.Text)))
                {
                    Utilities.SetParentRecursive(node, /*incremental*/ false); // we need parent data on imports before the program is fully bound, so we ensure it's set here
                    (imports ??= []).Add(stringLiteral);
                    if (_usesUriStyleNodeCoreModules != true && _currentNodeModulesDepth == 0 && !file.IsDeclarationFile)
                    {
                        if (stringLiteral.Text!.StartsWith("node:", StringComparison.Ordinal) && !Utilities.ExclusivelyPrefixedNodeCoreModules.Contains(stringLiteral.Text))
                        {
                            // Presence of `node:` prefix takes precedence over unprefixed node core modules
                            _usesUriStyleNodeCoreModules = true;
                        }
                        else if (_usesUriStyleNodeCoreModules is null && Utilities.UnprefixedNodeCoreModules.Contains(stringLiteral.Text))
                        {
                            // Avoid `unprefixedNodeCoreModules.has` for every import
                            _usesUriStyleNodeCoreModules = false;
                        }
                    }
                }
            }
            else if (NodeTests.IsModuleDeclaration(node))
            {
                var moduleDeclaration = (ModuleDeclaration)node;
                if (Utilities.IsAmbientModule(moduleDeclaration)
                    && (inAmbientModule || Utilities.HasSyntacticModifier(moduleDeclaration, ModifierFlags.Ambient) || file.IsDeclarationFile))
                {
                    Utilities.SetParent(moduleDeclaration.Name, moduleDeclaration);
                    // The generated `Name` slot is nullable and `getTextOfIdentifierOrLiteral`'s union-returning face is
                    // `string?`; the reference's `moduleDeclaration.name` and its typed overload are both total.
                    var nameText = Utilities.GetTextOfIdentifierOrLiteral(moduleDeclaration.Name!)!;
                    // Ambient module declarations can be interpreted as augmentations for some existing external modules.
                    // This will happen in two cases:
                    // - if current file is external module then module augmentation is a ambient module declaration defined in the top level scope
                    // - if current file is not external module then module augmentation is an ambient module declaration with non-relative module name
                    //   immediately nested in top level ambient module declaration .
                    if (isExternalModuleFile || inAmbientModule && !Utilities.IsExternalModuleNameRelative(nameText))
                    {
                        (moduleAugmentations ??= []).Add(moduleDeclaration.Name!);
                    }
                    else if (!inAmbientModule)
                    {
                        if (file.IsDeclarationFile)
                        {
                            // for global .d.ts files record name of ambient module
                            (ambientModules ??= []).Add(nameText);
                        }
                        // An AmbientExternalModuleDeclaration declares an external module.
                        // This type of declaration is permitted only in the global module.
                        // The StringLiteral must specify a top - level external module name.
                        // Relative external module names are not permitted

                        // NOTE: body of ambient module is always a module block, if it exists
                        if (moduleDeclaration.Body is ModuleBlock body)
                        {
                            foreach (var statement in body.Statements ?? [])
                            {
                                CollectModuleReferences((Node)statement, /*inAmbientModule*/ true);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/program.ts:2770-2772 — <c>getSourceFile</c>.</summary>
    internal SourceFile? GetSourceFile(string fileName) => GetSourceFileByPath(ToPath(fileName));

    /// <summary>ported from: src/compiler/program.ts:2774-2776 — <c>getSourceFileByPath</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>filesByName.get(path) || undefined</c> folds the <c>false</c> and <c>undefined</c> arms
    /// into "no file"; <see cref="FileByNameEntry.ToSourceFile"/> is that projection.</remarks>
    internal SourceFile? GetSourceFileByPath(string path)
        => _filesByName.TryGetValue(path, out var entry) ? entry.ToSourceFile() : null;

    /// <summary>ported from: src/compiler/program.ts:2684-2686 — <c>getTypeChecker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS calls <c>createTypeChecker(host)</c> from <c>checker.ts</c>; the port's checker factory is M6-2's
    /// <c>Checker.CreateTypeChecker</c> and is referenced here, lazily, exactly as the reference does — no member of
    /// these files calls it eagerly, so a program can be built before the checker lands.
    /// </remarks>
    internal ITypeChecker GetTypeChecker() => _typeChecker ??= Checker.CreateTypeChecker(this);

    /// <summary>ported from: src/compiler/program.ts:2684-2686 — <c>program.getSourceFiles()</c>.</summary>
    internal List<SourceFile> GetSourceFiles() => _files;

    /// <summary>ported from: src/compiler/program.ts:3421-3426 — <c>getLibFileFromReference</c>.</summary>
    internal SourceFile? GetLibFileFromReference(IFileReference @ref)
    {
        var libFileName = Utilities.GetLibFileNameFromLibReference(@ref);
        var actualFileName = libFileName is not null && _resolvedLibReferences is not null && _resolvedLibReferences.TryGetValue(libFileName, out var resolution)
            ? resolution.Actual
            : null;
        return actualFileName is not null ? GetSourceFile(actualFileName) : null;
    }

    /// <summary>ported from: src/compiler/program.ts:3428-3431 — <c>getSourceFileFromReference</c>.</summary>
    internal SourceFile? GetSourceFileFromReference(SourceFile referencingFile, IFileReference @ref)
        => GetSourceFileFromReferenceWorker(ResolveTripleslashReference(@ref.FileName ?? "", referencingFile.FileName ?? ""), GetSourceFile);

    /// <summary>ported from: src/compiler/program.ts:3432-3484 — <c>getSourceFileFromReferenceWorker</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>fail</c> takes <c>(diagnostic, ...argument: string[])</c>; the port passes the same
    /// pair as a message and an array, because a C# <c>params</c> array cannot be captured but an array can.</remarks>
    private SourceFile? GetSourceFileFromReferenceWorker(
        string fileName,
        Func<string, SourceFile?> getSourceFile,
        Action<DiagnosticMessage, string[]>? fail = null,
        IFileIncludeReason? reason = null)
    {
        if (Paths.HasExtension(fileName))
        {
            var canonicalFileName = _host.GetCanonicalFileName(fileName);
            if (!Js.IsTruthy(_options.AllowNonTsExtensions)
                && !_supportedExtensionsWithJsonIfResolveJsonModule.SelectMany(static x => x)
                    .Any(extension => Paths.FileExtensionIs(canonicalFileName, extension)))
            {
                if (fail is not null)
                {
                    if (Utilities.HasJsFileExtension(canonicalFileName))
                    {
                        fail(Diagnostics.File_0_is_a_JavaScript_file_Did_you_mean_to_enable_the_allowJs_option, [fileName]);
                    }
                    else
                    {
                        fail(Diagnostics.File_0_has_an_unsupported_extension_The_only_supported_extensions_are_1, [fileName, "'" + string.Join("', '", _supportedExtensions.SelectMany(static x => x)) + "'"]);
                    }
                }

                return null;
            }

            var sourceFile = getSourceFile(fileName);
            if (fail is not null)
            {
                if (sourceFile is null)
                {
                    var redirect = GetRedirectFromSourceFile(fileName);
                    if (redirect?.OutputDts is not null)
                    {
                        fail(Diagnostics.Output_file_0_has_not_been_built_from_source_file_1, [redirect.OutputDts, fileName]);
                    }
                    else
                    {
                        fail(Diagnostics.File_0_not_found, [fileName]);
                    }
                }
                else if (Utilities.IsReferencedFile(reason) && canonicalFileName == _host.GetCanonicalFileName(GetSourceFileByPath(reason!.File ?? "")!.FileName ?? ""))
                {
                    fail(Diagnostics.A_file_cannot_have_a_reference_to_itself, []);
                }
            }

            return sourceFile;
        }
        else
        {
            var sourceFileNoExtension = Js.IsTruthy(_options.AllowNonTsExtensions) ? getSourceFile(fileName) : null;
            if (sourceFileNoExtension is not null)
            {
                return sourceFileNoExtension;
            }

            if (fail is not null && Js.IsTruthy(_options.AllowNonTsExtensions))
            {
                fail(Diagnostics.File_0_not_found, [fileName]);
                return null;
            }

            // Only try adding extensions from the first supported group (which should be .ts/.tsx/.d.ts)
            SourceFile? sourceFileWithAddedExtension = null;
            foreach (var extension in _supportedExtensions[0])
            {
                sourceFileWithAddedExtension = getSourceFile(fileName + extension);
                if (sourceFileWithAddedExtension is not null)
                {
                    break;
                }
            }

            if (fail is not null && sourceFileWithAddedExtension is null)
            {
                fail(Diagnostics.Could_not_resolve_the_path_0_with_the_extensions_Colon_1, [fileName, "'" + string.Join("', '", _supportedExtensions.SelectMany(static x => x)) + "'"]);
            }

            return sourceFileWithAddedExtension;
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3486-3494 — <c>processSourceFile</c>.</summary>
    private void ProcessSourceFile(string fileName, bool isDefaultLib, IPackageId? packageId, IFileIncludeReason reason)
        => GetSourceFileFromReferenceWorker(
            fileName,
            name => FindSourceFile(name, isDefaultLib, reason, packageId),
            (diagnostic, args) => AddFilePreprocessingFileExplainingDiagnostic(/*file*/ null, reason, diagnostic, args),
            reason);

    /// <summary>ported from: src/compiler/program.ts:3495-3497 — <c>processProjectReferenceFile</c>.</summary>
    internal void ProcessProjectReferenceFile(string fileName, ProjectReferenceFile reason)
        => ProcessSourceFile(fileName, /*isDefaultLib*/ false, /*packageId*/ null, reason);

    /// <summary>ported from: src/compiler/program.ts:3499-3507 — <c>reportFileNamesDifferOnlyInCasingError</c>.</summary>
    private void ReportFileNamesDifferOnlyInCasingError(string fileName, SourceFile existingFile, IFileIncludeReason reason)
    {
        var hasExistingReasonToReportErrorOn = !Utilities.IsReferencedFile(reason)
            && (_programDiagnostics.GetFileReasons().Get(existingFile.Path ?? "") ?? []).Any(Utilities.IsReferencedFile);
        if (hasExistingReasonToReportErrorOn)
        {
            AddFilePreprocessingFileExplainingDiagnostic(existingFile, reason, Diagnostics.Already_included_file_name_0_differs_from_file_name_1_only_in_casing, [existingFile.FileName ?? "", fileName]);
        }
        else
        {
            AddFilePreprocessingFileExplainingDiagnostic(existingFile, reason, Diagnostics.File_name_0_differs_from_already_included_file_name_1_only_in_casing, [fileName, existingFile.FileName ?? ""]);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3509-3520 — <c>createRedirectedSourceFile</c>.</summary>
    /// <remarks>PORT-NOTE: TS calls <c>parseNodeFactory.createRedirectedSourceFile({ redirectTarget, unredirected })</c>
    /// and gets a <c>SourceFile</c> back; the port's factory member has the same shape (see <c>Parser/NodeFactory.cs</c>)
    /// and the result is the same <c>SourceFile</c> class.</remarks>
    private SourceFile CreateRedirectedSourceFile(
        SourceFile redirectTarget,
        SourceFile unredirected,
        string fileName,
        string path,
        string resolvedPath,
        string originalFileName,
        CreateSourceFileOptions sourceFileOptions)
    {
        var redirect = NodeFactory.Factory.CreateRedirectedSourceFile(new RedirectInfo { RedirectTarget = redirectTarget, Unredirected = unredirected });
        redirect.FileName = fileName;
        redirect.Path = path;
        redirect.ResolvedPath = resolvedPath;
        redirect.OriginalFileName = originalFileName;
        redirect.PackageJsonLocations = sourceFileOptions.PackageJsonLocations is { Count: > 0 } ? sourceFileOptions.PackageJsonLocations : null;
        redirect.PackageJsonScope = sourceFileOptions.PackageJsonScope;
        _sourceFilesFoundSearchingNodeModules[path] = _currentNodeModulesDepth > 0;
        return (SourceFile)redirect;
    }

    /// <summary>ported from: src/compiler/program.ts:3522-3531 — <c>findSourceFile</c>.</summary>
    internal SourceFile? FindSourceFile(string fileName, bool isDefaultLib, IFileIncludeReason reason, IPackageId? packageId)
    {
        Tracing.Current?.Push(Tracing.Phase.Program, "findSourceFile", TracingArgs.Of(
            ("fileName", fileName),
            ("isDefaultLib", isDefaultLib ? true : (bool?)null),
            ("fileIncludeKind", reason.Kind.ToString())));
        var result = FindSourceFileWorker(fileName, isDefaultLib, reason, packageId);
        Tracing.Current?.Pop();
        return result;
    }

    /// <summary>ported from: src/compiler/program.ts:3533-3543 — <c>getCreateSourceFileOptions</c>.</summary>
    /// <remarks>PORT-NOTE: TS spreads the worker's object arm into the options object; the port has the class
    /// <see cref="ImpliedNodeFormatAndPackageJsonScope"/> for that arm (declared by the M6ProgHost slice).
    /// <para>
    /// PORT-NOTE: TS assigns <c>jsDocParsingMode: host.jsDocParsingMode</c>, whose optional slot can hold
    /// <c>undefined</c>, to an optional field. The port's <see cref="CreateSourceFileOptions.JsDocParsingMode"/> is
    /// total with <c>JSDocParsingMode.ParseAll</c> as its default (its own note records why), so the reference's
    /// <c>undefined</c> is spelled <c>?? JSDocParsingMode.ParseAll</c> — the same value the reference's parser
    /// resolves it to.
    /// </para>
    /// </remarks>
    private CreateSourceFileOptions GetCreateSourceFileOptions(string fileName, ModuleResolutionCache? moduleResolutionCache)
    {
        // It's a _little odd_ that we can't set `impliedNodeFormat` until the program step - but it's the first and only time we have a resolution cache
        // and a freshly made source file node on hand at the same time, and we need both to set the field. Persisting the resolution cache all the way
        // to the check and emit steps would be bad - so we much prefer detecting and storing the format information on the source file node upfront.
        var result = GetImpliedNodeFormatForFileWorker(
            Paths.GetNormalizedAbsolutePath(fileName, _currentDirectory),
            // PORT-NOTE: TS has one `PackageJsonInfoCache` type; the port has the generated `IPackageJsonInfoCache` face
            // (what the resolution cache hands out, `ModuleResolution.Model.cs`) and the concrete class the worker's
            // parameter names, so the boundary casts — the same treatment the port's other generated-vs-concrete pairs get.
            (PackageJsonInfoCache?)moduleResolutionCache?.GetPackageJsonInfoCache(),
            _host,
            _options);
        var languageVersion = Utilities.GetEmitScriptTarget(_options);
        var setExternalModuleIndicator = Utilities.GetSetExternalModuleIndicator(_options);
        return result is ImpliedNodeFormatAndPackageJsonScope withFormat
            ? new CreateSourceFileOptions
            {
                LanguageVersion = languageVersion,
                ImpliedNodeFormat = withFormat.ImpliedNodeFormat,
                PackageJsonLocations = withFormat.PackageJsonLocations,
                PackageJsonScope = withFormat.PackageJsonScope,
                SetExternalModuleIndicator = setExternalModuleIndicator,
                JsDocParsingMode = _host.JsDocParsingMode ?? JSDocParsingMode.ParseAll,
            }
            : new CreateSourceFileOptions
            {
                LanguageVersion = languageVersion,
                ImpliedNodeFormat = result as ModuleKind?,
                SetExternalModuleIndicator = setExternalModuleIndicator,
                JsDocParsingMode = _host.JsDocParsingMode ?? JSDocParsingMode.ParseAll,
            };
    }

    /// <summary>ported from: src/compiler/program.ts:3545-3706 — <c>findSourceFileWorker</c>.</summary>
    /// <remarks>PORT-NOTE: TS's <c>filesByName.get(path)</c> answers <c>SourceFile | false | undefined</c>; the port's
    /// <see cref="FileByNameEntry"/> keeps the three states (see <c>Program.Model.cs</c>), and its <c>File</c> member is
    /// the file or null for both empty arms — the reference's following <c>file &amp;&amp; …</c> tests are therefore the
    /// same <c>is not null</c> tests here.</remarks>
    private SourceFile? FindSourceFileWorker(string fileName, bool isDefaultLib, IFileIncludeReason reason, IPackageId? packageId)
    {
        var path = ToPath(fileName);
        if (_useSourceOfProjectReferenceRedirect)
        {
            var source = GetRedirectFromOutput(path);
            // If preserveSymlinks is true, module resolution wont jump the symlink
            // but the resolved real path may be the .d.ts from project reference
            // Note:: Currently we try the real path only if the
            // file is from node_modules to avoid having to run real path on all file paths
            if (source is null
                // PORT-NOTE: TS's guard is `host.realpath && options.preserveSymlinks && …`; the generated host
                // interface declares `realpath` required, so that half compiles away (the treatment
                // `Program/Program.ProjectReferences.cs`'s `originalRealpath` note records).
                && Js.IsTruthy(_options.PreserveSymlinks)
                && Parser.IsDeclarationFileName(fileName)
                && fileName.Contains(ModuleNameResolver.NodeModulesPathPart))
            {
                var realPath = ToPath(_host.Realpath(fileName));
                if (realPath != path)
                {
                    source = GetRedirectFromOutput(realPath);
                }
            }

            if (source?.Source is not null)
            {
                var file = FindSourceFile(source.Source, isDefaultLib, reason, packageId);
                if (file is not null)
                {
                    AddFileToFilesByName(file, path, fileName, /*redirectedPath*/ null);
                }

                return file;
            }
        }

        var originalFileName = fileName;
        if (_filesByName.TryGetValue(path, out var existing))
        {
            var file = existing.File;
            var addedReason = AddFileIncludeReason(file, reason, /*checkExisting*/ true);
            // try to check if we've already seen this file but with a different casing in path
            // NOTE: this only makes sense for case-insensitive file systems, and only on files which are not redirected
            if (file is not null && addedReason && _options.ForceConsistentCasingInFileNames != false)
            {
                var checkedName = file.FileName ?? "";
                var isRedirect = ToPath(checkedName) != ToPath(fileName);
                if (isRedirect)
                {
                    fileName = GetRedirectFromSourceFile(fileName)?.OutputDts ?? fileName;
                }

                // Check if it differs only in drive letters its ok to ignore that error:
                var checkedAbsolutePath = Paths.GetNormalizedAbsolutePathWithoutRoot(checkedName, _currentDirectory);
                var inputAbsolutePath = Paths.GetNormalizedAbsolutePathWithoutRoot(fileName, _currentDirectory);
                if (checkedAbsolutePath != inputAbsolutePath)
                {
                    ReportFileNamesDifferOnlyInCasingError(fileName, file, reason);
                }
            }

            // If the file was previously found via a node_modules search, but is now being processed as a root file,
            // then everything it sucks in may also be marked incorrectly, and needs to be checked again.
            if (file is not null && _sourceFilesFoundSearchingNodeModules.GetValueOrDefault(file.Path ?? "") && _currentNodeModulesDepth == 0)
            {
                _sourceFilesFoundSearchingNodeModules[file.Path ?? ""] = false;
                if (!Js.IsTruthy(_options.NoResolve))
                {
                    ProcessReferencedFiles(file, isDefaultLib);
                    ProcessTypeReferenceDirectives(file);
                }

                if (!Js.IsTruthy(_options.NoLib))
                {
                    ProcessLibReferenceDirectives(file);
                }

                _modulesWithElidedImports[file.Path ?? ""] = false;
                ProcessImportedModules(file);
            }
            // See if we need to reprocess the imports due to prior skipped imports
            else if (file is not null && _modulesWithElidedImports.GetValueOrDefault(file.Path ?? ""))
            {
                if (_currentNodeModulesDepth < _maxNodeModuleJsDepth)
                {
                    _modulesWithElidedImports[file.Path ?? ""] = false;
                    ProcessImportedModules(file);
                }
            }

            return file;
        }

        string? redirectedPath = null;
        if (!_useSourceOfProjectReferenceRedirect)
        {
            var redirectProject = GetRedirectFromSourceFile(fileName);
            if (redirectProject?.OutputDts is not null)
            {
                if (redirectProject.ResolvedRef?.CommandLine?.Options?.OutFile is not null)
                {
                    // Shouldnt create many to 1 mapping file in --out scenario
                    return null;
                }

                fileName = redirectProject.OutputDts;
                // Once we start redirecting to a file, we can potentially come back to it
                // via a back-reference from another file in the .d.ts folder. If that happens we'll
                // end up trying to add it to the program *again* because we were tracking it via its
                // original (un-redirected) name. So we have to map both the original path and the redirected path
                // to the source file we're about to find/create
                redirectedPath = ToPath(redirectProject.OutputDts);
            }
        }

        // We haven't looked for this file, do so now and cache result
        var sourceFileOptions = GetCreateSourceFileOptions(fileName, _moduleResolutionCache);
        var fileFromHost = _host.GetSourceFile(
            fileName,
            sourceFileOptions,
            file => AddFilePreprocessingFileExplainingDiagnostic(/*file*/ null, reason, Diagnostics.Cannot_read_file_0_Colon_1, [fileName, file]),
            _shouldCreateNewSourceFile);

        if (packageId is not null)
        {
            var packageIdKey = Utilities.PackageIdToString(packageId);
            if (_packageIdToSourceFile.TryGetValue(packageIdKey, out var fileFromPackageId))
            {
                // Some other SourceFile already exists with this package name and version.
                // Instead of creating a duplicate, just redirect to the existing one.
                var dupFile = CreateRedirectedSourceFile(fileFromPackageId, fileFromHost!, fileName, path, ToPath(fileName), originalFileName, sourceFileOptions);
                _redirectTargetsMap.Add(fileFromPackageId.Path ?? "", fileName);
                AddFileToFilesByName(dupFile, path, fileName, redirectedPath);
                AddFileIncludeReason(dupFile, reason, /*checkExisting*/ false);
                _sourceFileToPackageName[path] = Utilities.PackageIdToPackageName(packageId);
                _processingOtherFiles!.Add(dupFile);
                return dupFile;
            }

            if (fileFromHost is not null)
            {
                // This is the first source file to have this packageId.
                _packageIdToSourceFile[packageIdKey] = fileFromHost;
                _sourceFileToPackageName[path] = Utilities.PackageIdToPackageName(packageId);
            }
        }

        AddFileToFilesByName(fileFromHost, path, fileName, redirectedPath);

        if (fileFromHost is not null)
        {
            _sourceFilesFoundSearchingNodeModules[path] = _currentNodeModulesDepth > 0;
            fileFromHost.FileName = fileName; // Ensure that source file has same name as what we were looking for
            fileFromHost.Path = path;
            fileFromHost.ResolvedPath = ToPath(fileName);
            fileFromHost.OriginalFileName = originalFileName;
            fileFromHost.PackageJsonLocations = sourceFileOptions.PackageJsonLocations is { Count: > 0 } ? sourceFileOptions.PackageJsonLocations : null;
            fileFromHost.PackageJsonScope = sourceFileOptions.PackageJsonScope;
            AddFileIncludeReason(fileFromHost, reason, /*checkExisting*/ false);

            if (Utilities.HostUsesCaseSensitiveFileNames(_host))
            {
                var pathLowerCase = Core.ToFileNameLowerCase(path);
                // for case-sensitive file systems check if we've already seen some file with similar filename ignoring case
                if (_filesByNameIgnoreCase!.TryGetValue(pathLowerCase, out var existingFile))
                {
                    ReportFileNamesDifferOnlyInCasingError(fileName, existingFile, reason);
                }
                else
                {
                    _filesByNameIgnoreCase[pathLowerCase] = fileFromHost;
                }
            }

            if (!Js.IsTruthy(_options.NoResolve))
            {
                ProcessReferencedFiles(fileFromHost, isDefaultLib);
                ProcessTypeReferenceDirectives(fileFromHost);
            }

            if (!Js.IsTruthy(_options.NoLib))
            {
                ProcessLibReferenceDirectives(fileFromHost);
            }

            // always process imported modules to record module name resolutions
            ProcessImportedModules(fileFromHost);

            if (isDefaultLib)
            {
                _processingDefaultLibFiles!.Add(fileFromHost);
            }
            else
            {
                _processingOtherFiles!.Add(fileFromHost);
            }

            (_filesWithReferencesProcessed ??= []).Add(fileFromHost.Path ?? "");
        }

        return fileFromHost;
    }

    /// <summary>ported from: src/compiler/program.ts:3708-3714 — <c>addFileIncludeReason</c>.</summary>
    private bool AddFileIncludeReason(SourceFile? file, IFileIncludeReason reason, bool checkExisting)
    {
        if (file is not null
            && (!checkExisting
                || !Utilities.IsReferencedFile(reason)
                || !(_filesWithReferencesProcessed?.Contains(reason.File ?? "") ?? false)))
        {
            _programDiagnostics.GetFileReasons().Add(file.Path ?? "", reason);
            return true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/program.ts:3716-3723 — <c>addFileToFilesByName</c>.</summary>
    private void AddFileToFilesByName(SourceFile? file, string path, string fileName, string? redirectedPath)
    {
        if (redirectedPath is not null)
        {
            UpdateFilesByNameMap(fileName, redirectedPath, file);
            UpdateFilesByNameMap(fileName, path, file ?? FileByNameEntry.Missing);
        }
        else
        {
            UpdateFilesByNameMap(fileName, path, file);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3725-3732 — <c>updateFilesByNameMap</c>.</summary>
    private void UpdateFilesByNameMap(string fileName, string path, FileByNameEntry? entry)
    {
        _filesByName[path] = entry ?? FileByNameEntry.Unset;
        if (entry is not null && entry.State == FileByNameState.File)
        {
            _missingFileNames.Remove(path);
        }
        else
        {
            _missingFileNames[path] = fileName;
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3760-3769 — <c>processReferencedFiles</c>.</summary>
    private void ProcessReferencedFiles(SourceFile file, bool isDefaultLib)
    {
        var referencedFiles = file.ReferencedFiles ?? [];
        for (var index = 0; index < referencedFiles.Count; index++)
        {
            var @ref = referencedFiles[index];
            ProcessSourceFile(
                ResolveTripleslashReference(@ref.FileName ?? "", file.FileName ?? ""),
                isDefaultLib,
                /*packageId*/ null,
                new ReferencedFile { Kind = FileIncludeKind.ReferenceFile, File = file.Path, Index = index });
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3771-3788 — <c>processTypeReferenceDirectives</c>.</summary>
    private void ProcessTypeReferenceDirectives(SourceFile file)
    {
        var typeDirectives = file.TypeReferenceDirectives ?? [];
        if (typeDirectives.Count == 0)
        {
            return;
        }

        var resolutions = _resolvedTypeReferenceDirectiveNamesProcessing is not null
                && _resolvedTypeReferenceDirectiveNamesProcessing.TryGetValue(file.Path ?? "", out var processing)
            ? processing
            : ResolveTypeReferenceDirectiveNamesReusingOldState(typeDirectives, file);
        var resolutionsInFile = ModuleNameResolver.CreateModeAwareCache<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>();
        (_resolvedTypeReferenceDirectiveNames ??= [])[file.Path ?? ""] = resolutionsInFile;
        for (var index = 0; index < typeDirectives.Count; index++)
        {
            var @ref = typeDirectives[index];
            var resolvedTypeReferenceDirective = resolutions[index];
            // store resolved type directive on the file
            var fileName = @ref.FileName ?? "";
            var mode = GetModeForTypeReferenceDirectiveInFile(@ref, file);
            resolutionsInFile.Set(fileName, mode, resolvedTypeReferenceDirective);
            ProcessTypeReferenceDirective(fileName, mode, resolvedTypeReferenceDirective, new ReferencedFile { Kind = FileIncludeKind.TypeReferenceDirective, File = file.Path, Index = index });
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3790-3792 — <c>getCompilerOptionsForFile</c>.</summary>
    internal ICompilerOptions GetCompilerOptionsForFile(SourceFile file)
        => GetRedirectReferenceForResolution(file)?.CommandLine?.Options ?? _options;

    /// <summary>ported from: src/compiler/program.ts:3794-3803 — <c>processTypeReferenceDirective</c>.</summary>
    private void ProcessTypeReferenceDirective(
        string typeReferenceDirective,
        ModuleKind? mode,
        IResolvedTypeReferenceDirectiveWithFailedLookupLocations resolution,
        IFileIncludeReason reason)
    {
        Tracing.Current?.Push(Tracing.Phase.Program, "processTypeReferenceDirective", TracingArgs.Of(
            ("directive", typeReferenceDirective),
            ("hasResolved", resolution.ResolvedTypeReferenceDirective is not null),
            ("refKind", reason.Kind.ToString()),
            ("refPath", Utilities.IsReferencedFile(reason) ? reason.File : null)));
        ProcessTypeReferenceDirectiveWorker(typeReferenceDirective, mode, resolution, reason);
        Tracing.Current?.Pop();
    }

    /// <summary>ported from: src/compiler/program.ts:3805-3824 — <c>processTypeReferenceDirectiveWorker</c>.</summary>
    private void ProcessTypeReferenceDirectiveWorker(
        string typeReferenceDirective,
        ModuleKind? mode,
        IResolvedTypeReferenceDirectiveWithFailedLookupLocations resolution,
        IFileIncludeReason reason)
    {
        AddResolutionDiagnostics(resolution);
        var resolvedTypeReferenceDirective = resolution.ResolvedTypeReferenceDirective;
        if (resolvedTypeReferenceDirective is not null)
        {
            if (resolvedTypeReferenceDirective.IsExternalLibraryImport == true)
            {
                _currentNodeModulesDepth++;
            }

            // resolved from the primary path
            ProcessSourceFile(resolvedTypeReferenceDirective.ResolvedFileName ?? "", /*isDefaultLib*/ false, resolvedTypeReferenceDirective.PackageId, reason);

            if (resolvedTypeReferenceDirective.IsExternalLibraryImport == true)
            {
                _currentNodeModulesDepth--;
            }
        }
        else
        {
            AddFilePreprocessingFileExplainingDiagnostic(/*file*/ null, reason, Diagnostics.Cannot_find_type_definition_file_for_0, [typeReferenceDirective]);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3826-3833 — <c>pathForLibFile</c>.</summary>
    private string PathForLibFile(string libFileName)
    {
        if (_resolvedLibReferences is not null && _resolvedLibReferences.TryGetValue(libFileName, out var existing))
        {
            return existing.Actual ?? "";
        }

        var result = PathForLibFileWorker(libFileName);
        (_resolvedLibReferences ??= [])[libFileName] = result;
        _libFiles.Add(ToPath(result.Actual ?? ""));
        return result.Actual ?? "";
    }

    /// <summary>ported from: src/compiler/program.ts:3835-3890 — <c>pathForLibFileWorker</c>.</summary>
    /// <remarks>PORT-NOTE: <c>LibResolution</c> is the generated generic interface <c>ILibResolution&lt;T&gt;</c>; the
    /// port instantiates it with <c>object?</c> at this boundary because the slot holds either a
    /// <c>ResolvedModuleWithFailedLookupLocations</c> or the old program's value.</remarks>
    private ILibResolution<object?> PathForLibFileWorker(string libFileName)
    {
        if (_resolvedLibProcessing is not null && _resolvedLibProcessing.TryGetValue(libFileName, out var existing))
        {
            return existing;
        }

        if (!Js.IsTruthy(_options.LibReplacement))
        {
            ILibResolution<object?> result = new LibResolution<object?>
            {
                Resolution = new ResolvedModuleWithFailedLookupLocations { ResolvedModule = null },
                Actual = Paths.CombinePaths(_defaultLibraryPath, libFileName),
            };
            (_resolvedLibProcessing ??= [])[libFileName] = result;
            return result;
        }

        if (_structureIsReused != StructureIsReused.Not && _oldProgram is not null && !_hasInvalidatedLibResolutions(libFileName))
        {
            var oldResolution = _oldProgram._resolvedLibReferences is not null && _oldProgram._resolvedLibReferences.TryGetValue(libFileName, out var value) ? value : null;
            if (oldResolution is not null)
            {
                if (oldResolution.Resolution is IResolvedModuleWithFailedLookupLocations resolution && ModuleNameResolver.IsTraceEnabled(_options, _host))
                {
                    var libraryName = GetLibraryNameFromLibFileName(libFileName);
                    var resolveFrom = GetInferredLibraryNameResolveFrom(_options, _currentDirectory, libFileName);
                    ModuleNameResolver.Trace(
                        _host,
                        resolution.ResolvedModule is not null
                            ? resolution.ResolvedModule.PackageId is not null
                                ? Diagnostics.Reusing_resolution_of_module_0_from_1_of_old_program_it_was_successfully_resolved_to_2_with_Package_ID_3
                                : Diagnostics.Reusing_resolution_of_module_0_from_1_of_old_program_it_was_successfully_resolved_to_2
                            : Diagnostics.Reusing_resolution_of_module_0_from_1_of_old_program_it_was_not_resolved,
                        libraryName,
                        Paths.GetNormalizedAbsolutePath(resolveFrom, _currentDirectory),
                        resolution.ResolvedModule?.ResolvedFileName,
                        resolution.ResolvedModule?.PackageId is not null ? Utilities.PackageIdToString(resolution.ResolvedModule.PackageId) : null);
                }

                (_resolvedLibProcessing ??= [])[libFileName] = oldResolution;
                return oldResolution;
            }
        }

        var libraryNameWorker = GetLibraryNameFromLibFileName(libFileName);
        var resolveFromWorker = GetInferredLibraryNameResolveFrom(_options, _currentDirectory, libFileName);
        Tracing.Current?.Push(Tracing.Phase.Program, "resolveLibrary", TracingArgs.Of(("resolveFrom", resolveFromWorker)));
        Performance.Mark("beforeResolveLibrary");
        var resolutionWorker = _actualResolveLibrary(libraryNameWorker, resolveFromWorker, _options, libFileName);
        Performance.Mark("afterResolveLibrary");
        Performance.Measure("ResolveLibrary", "beforeResolveLibrary", "afterResolveLibrary");
        Tracing.Current?.Pop();
        ILibResolution<object?> resultWorker = new LibResolution<object?>
        {
            Resolution = resolutionWorker,
            Actual = resolutionWorker.ResolvedModule is not null
                ? resolutionWorker.ResolvedModule.ResolvedFileName
                : Paths.CombinePaths(_defaultLibraryPath, libFileName),
        };
        (_resolvedLibProcessing ??= [])[libFileName] = resultWorker;
        return resultWorker;
    }

    /// <summary>ported from: src/compiler/program.ts:3892-3905 — <c>processLibReferenceDirectives</c>.</summary>
    private void ProcessLibReferenceDirectives(SourceFile file)
    {
        var libReferenceDirectives = file.LibReferenceDirectives ?? [];
        for (var index = 0; index < libReferenceDirectives.Count; index++)
        {
            var libReference = libReferenceDirectives[index];
            var libFileName = Utilities.GetLibFileNameFromLibReference(libReference);
            if (libFileName is not null)
            {
                ProcessRootFile(PathForLibFile(libFileName), /*isDefaultLib*/ true, new ReferencedFile { Kind = FileIncludeKind.LibReferenceDirective, File = file.Path, Index = index });
            }
            else
            {
                _programDiagnostics.AddFileProcessingDiagnostic(new FilePreprocessingLibReferenceDiagnostic
                {
                    Kind = FilePreprocessingDiagnosticsKind.FilePreprocessingLibReferenceDiagnostic,
                    Reason = new ReferencedFile { Kind = FileIncludeKind.LibReferenceDirective, File = file.Path, Index = index },
                });
            }
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3907-3909 — <c>getCanonicalFileName</c>.</summary>
    private string GetCanonicalFileName(string fileName) => _host.GetCanonicalFileName(fileName);

    /// <summary>ported from: src/compiler/program.ts:3911-3976 — <c>processImportedModules</c>.</summary>
    private void ProcessImportedModules(SourceFile file)
    {
        CollectExternalModuleReferences(file);
        var imports = file.Imports ?? [];
        var moduleAugmentations = file.ModuleAugmentations ?? [];
        if (imports.Count != 0 || moduleAugmentations.Count != 0)
        {
            // Because global augmentation doesn't have string literal name, we can check for global augmentation as such.
            var moduleNames = GetModuleNames(file);
            var resolutions = _resolvedModulesProcessing is not null && _resolvedModulesProcessing.TryGetValue(file.Path ?? "", out var processing)
                ? processing
                : ResolveModuleNamesReusingOldState(moduleNames, file);
            Debug.Assert(resolutions.Count == moduleNames.Count);
            var optionsForFile = GetCompilerOptionsForFile(file);
            var resolutionsInFile = ModuleNameResolver.CreateModeAwareCache<IResolvedModuleWithFailedLookupLocations>();
            (_resolvedModules ??= [])[file.Path ?? ""] = resolutionsInFile;
            for (var index = 0; index < moduleNames.Count; index++)
            {
                var resolution = resolutions[index].ResolvedModule;
                var moduleName = moduleNames[index].Text ?? "";
                var mode = GetModeForUsageLocationWorker(file, moduleNames[index], optionsForFile);
                resolutionsInFile.Set(moduleName, mode, resolutions[index]);
                AddResolutionDiagnosticsFromResolutionOrCache(file, moduleName, resolutions[index], mode);

                if (resolution is null)
                {
                    continue;
                }

                var isFromNodeModulesSearch = resolution.IsExternalLibraryImport == true;
                // If this is js file source of project reference, dont treat it as js file but as d.ts
                var isJsFile = !Utilities.ResolutionExtensionIsTSOrJson(resolution.Extension ?? "") && GetRedirectFromSourceFile(resolution.ResolvedFileName ?? "") is null;
                var isJsFileFromNodeModules = isFromNodeModulesSearch && isJsFile && (resolution.OriginalPath is null || ModuleNameResolver.PathContainsNodeModules(resolution.ResolvedFileName ?? ""));
                var resolvedFileName = resolution.ResolvedFileName;

                if (isFromNodeModulesSearch)
                {
                    _currentNodeModulesDepth++;
                }

                // add file to program only if:
                // - resolution was successful
                // - noResolve is falsy
                // - module name comes from the list of imports
                // - it's not a top level JavaScript module that exceeded the search max
                var elideImport = isJsFileFromNodeModules && _currentNodeModulesDepth > _maxNodeModuleJsDepth;
                // Don't add the file if it has a bad extension (e.g. 'tsx' if we don't have '--allowJs')
                // This may still end up being an untyped module -- the file won't be included but imports will be allowed.
                var shouldAddFile = resolvedFileName is not null
                    && GetResolutionDiagnostic(optionsForFile, resolution, file.IsDeclarationFile) is null
                    && !Js.IsTruthy(optionsForFile.NoResolve)
                    && index < imports.Count
                    && !elideImport
                    && !(isJsFile && !Utilities.GetAllowJsCompilerOption(optionsForFile))
                    && (Utilities.IsInJSFile(imports[index]) || (imports[index].Flags & NodeFlags.JSDoc) == 0);

                if (elideImport)
                {
                    _modulesWithElidedImports[file.Path ?? ""] = true;
                }
                else if (shouldAddFile)
                {
                    FindSourceFile(
                        resolvedFileName!,
                        /*isDefaultLib*/ false,
                        new ReferencedFile { Kind = FileIncludeKind.Import, File = file.Path, Index = index },
                        resolution.PackageId);
                }

                if (isFromNodeModulesSearch)
                {
                    _currentNodeModulesDepth--;
                }
            }
        }
    }

    /// <summary>ported from: src/compiler/program.ts:3978-3997 — <c>checkSourceFilesBelongToPath</c>.</summary>
    internal bool CheckSourceFilesBelongToPath(List<SourceFile> sourceFiles, string rootDirectory)
    {
        var allFilesBelongToPath = true;
        var absoluteRootDirectoryPath = _host.GetCanonicalFileName(Paths.GetNormalizedAbsolutePath(rootDirectory, _currentDirectory));
        foreach (var sourceFile in sourceFiles)
        {
            if (!sourceFile.IsDeclarationFile)
            {
                var absoluteSourceFilePath = _host.GetCanonicalFileName(Paths.GetNormalizedAbsolutePath(sourceFile.FileName ?? "", _currentDirectory));
                if (!absoluteSourceFilePath.StartsWith(absoluteRootDirectoryPath, StringComparison.Ordinal))
                {
                    _programDiagnostics.AddLazyConfigDiagnostic(
                        sourceFile,
                        Diagnostics.File_0_is_not_under_rootDir_1_rootDir_is_expected_to_contain_all_source_files,
                        sourceFile.FileName ?? "",
                        rootDirectory);
                    allFilesBelongToPath = false;
                }
            }
        }

        return allFilesBelongToPath;
    }

    /// <summary>ported from: src/compiler/program.ts:2143-2158 — <c>getCommonSourceDirectory</c>.</summary>
    private string GetCommonSourceDirectory()
    {
        var commonSourceDirectory = _programDiagnostics.GetCommonSourceDirectory();
        if (commonSourceDirectory is not null)
        {
            return commonSourceDirectory;
        }

        var emittedFiles = _files.Where(file => Utilities.SourceFileMayBeEmitted(file, this)).ToList();
        commonSourceDirectory = Program.GetCommonSourceDirectoryFromOptions(
            _options,
            () => emittedFiles.Where(file => !file.IsDeclarationFile).Select(file => file.FileName).OfType<string>().ToList(),
            _currentDirectory,
            _getCanonicalFileName,
            commonSourceDirectory => CheckSourceFilesBelongToPath(emittedFiles, commonSourceDirectory));
        _programDiagnostics.SetCommonSourceDirectory(commonSourceDirectory);
        return commonSourceDirectory;
    }

    /// <summary>ported from: src/compiler/program.ts:2160-2172 — <c>getClassifiableNames</c>.</summary>
    private HashSet<string> GetClassifiableNames()
    {
        if (_classifiableNames is null)
        {
            // Initialize a checker so that all our files are bound.
            GetTypeChecker();
            _classifiableNames = [];

            foreach (var sourceFile in _files)
            {
                if (sourceFile.ClassifiableNames is not null)
                {
                    foreach (var value in sourceFile.ClassifiableNames)
                    {
                        _classifiableNames.Add(value);
                    }
                }
            }
        }

        return _classifiableNames;
    }
}
