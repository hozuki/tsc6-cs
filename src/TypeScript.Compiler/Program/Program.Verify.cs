namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:4075-4429 (verifyCompilerOptions and its verifyEmitFilePath worker)
// ported from: src/compiler/program.ts:4431-4440 (getIgnoreDeprecationsVersion)
// ported from: src/compiler/program.ts:4442-4476 (checkDeprecations)
// ported from: src/compiler/program.ts:4478-4563 (verifyDeprecatedCompilerOptions)
// ported from: src/compiler/program.ts:4565-4575 (verifyDeprecatedProjectReference)
// ported from: src/compiler/program.ts:4577-4585 (addFilePreprocessingFileExplainingDiagnostic)
// ported from: src/compiler/program.ts:4587-4615 (verifyProjectReferences)
// ported from: src/compiler/program.ts:4617-4750 (the createDiagnosticForOption family)
// ported from: src/compiler/program.ts:4752-4808 (blockEmittingOfFile, isEmittedFile, isSameFile, getSymlinkCache)
// ported from: src/compiler/program.ts:4810-4840 (the option-dependent wrappers and getModeForTypeReferenceDirectiveInFile)
// ported from: src/compiler/utilities.ts:9435-9446 (hasZeroOrOneAsteriskCharacter)
// ported from: src/compiler/commandLineParser.ts:132-141 (the JSX option tables this slice is the only consumer of)
/// <summary>
/// <c>verifyCompilerOptions</c> and everything it reaches: the deprecation checks, the option-syntax diagnostic family
/// (<c>createDiagnosticForOption*</c>, which put a diagnostic on the offending key of <c>tsconfig.json</c> when the
/// program was built from one), the project-reference verification, <c>isEmittedFile</c>/<c>isSameFile</c>, the symlink
/// cache and the option-dependent wrappers the checker and the resolver call for a module reference's resolution mode.
/// </summary>
/// <remarks>
/// TS's functions are nested in <c>createProgram</c> and read its closure: <c>options</c>, <c>files</c>,
/// <c>filesByName</c>, <c>projectReferences</c>, <c>resolvedProjectReferences</c>, <c>programDiagnostics</c>,
/// <c>hasEmitBlockingDiagnostics</c>, <c>symbols</c>, <c>automaticTypeDirectiveResolutions</c> and the memoized
/// <c>_compilerOptionsObjectLiteralSyntax</c>/<c>_compilerOptionsPropertySyntax</c> pair. §15.13's rule turns them into
/// members of this class reading the state fields <c>Program/Program.State.cs</c> declares — those fields are the
/// contract, and no member here re-declares one.
/// <para>
/// PORT-NOTE: the two nested functions TS calls <c>createDiagnostic</c> (4479-4496 inside
/// <c>verifyDeprecatedCompilerOptions</c> and 4566-4568 inside <c>verifyDeprecatedProjectReference</c>) become one
/// member (<see cref="CreateDiagnostic"/>, which <c>checkDeprecations</c> receives as a delegate) and one inline
/// lambda: C# has a single member namespace, and a nested function is a named lambda anyway.
/// </para>
/// <para>
/// PORT-NOTE: the generated <see cref="ICompilerOptions"/> types <c>paths</c> as
/// <c>Dictionary&lt;string, List&lt;string&gt;&gt;</c> and the option enums as their enums, where TS's options object is
/// an unchecked JSON shape. Everywhere that makes an arm of a check unreachable from a program's options the arm is
/// dropped with the reason recorded at the site (the tsconfig reader is the only producer of that object and owns
/// those diagnostics), and every <c>false</c>-vs-<c>undefined</c> distinction TS makes by truthiness is kept with
/// <c>Js.IsTruthy</c>.
/// </para>
/// <para>
/// PORT-NOTE: <c>Utilities.HasZeroOrOneAsteriskCharacter</c> is declared at the end of this file because
/// <c>program.ts</c> is its only caller in the reference (<c>utilities.ts:9435-9446</c> is the definition).
/// </para>
/// </remarks>
internal sealed partial class Program
{
    /// <summary>ported from: src/compiler/program.ts:4075-4429 — <c>verifyCompilerOptions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the <c>options.paths</c> walk (4139-4171) tests <c>isArray(options.paths[key])</c> and
    /// <c>typeof subst === "string"</c> because a hand-written <c>tsconfig.json</c> can put any JSON value in the
    /// <c>paths</c> map. The generated <see cref="ICompilerOptions.Paths"/> already types the value as
    /// <c>List&lt;string&gt;</c> and the substitution as <c>string</c>, so both type tests are decided by the model:
    /// <c>isArray(x)</c> is the value being present (a <c>null</c> value is TS's <c>undefined</c> arm — the
    /// "should be an array" diagnostic), and the "expected string, got ..." arm cannot be reached. The tsconfig reader
    /// (<c>convertCompilerOptionsFromJson</c>, M8) reports those shapes before an options object exists, which is why
    /// the arm is not restated here.
    /// <para>
    /// PORT-NOTE: <c>ModuleKind[moduleKind]</c> and <c>ModuleResolutionKind[moduleResolution]</c> (4382-4388) are the
    /// enums' reverse mappings, which TS uses both as an "is this a known value" test and as the name passed to the
    /// diagnostic; the port spells those <see cref="Enum.IsDefined{TEnum}(TEnum)"/> and <see cref="Enum.GetName{TEnum}(TEnum)"/>.
    /// </para>
    /// </remarks>
    private void VerifyCompilerOptions()
    {
        if (Js.IsTruthy(_options.StrictPropertyInitialization) && !Utilities.GetStrictOptionValue(_options, "strictNullChecks"))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1, "strictPropertyInitialization", "strictNullChecks");
        }
        if (Js.IsTruthy(_options.ExactOptionalPropertyTypes) && !Utilities.GetStrictOptionValue(_options, "strictNullChecks"))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1, "exactOptionalPropertyTypes", "strictNullChecks");
        }

        if (Js.IsTruthy(_options.IsolatedModules) || Js.IsTruthy(_options.VerbatimModuleSyntax))
        {
            if (Js.IsTruthy(_options.OutFile))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_with_option_1, "outFile", Js.IsTruthy(_options.VerbatimModuleSyntax) ? "verbatimModuleSyntax" : "isolatedModules");
            }
        }

        if (Js.IsTruthy(_options.IsolatedDeclarations))
        {
            if (Utilities.GetAllowJsCompilerOption(_options))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_with_option_1, "allowJs", "isolatedDeclarations");
            }
            if (!Utilities.GetEmitDeclarations(_options))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "isolatedDeclarations", "declaration", "composite");
            }
        }

        if (Js.IsTruthy(_options.InlineSourceMap))
        {
            if (Js.IsTruthy(_options.SourceMap))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_with_option_1, "sourceMap", "inlineSourceMap");
            }
            if (Js.IsTruthy(_options.MapRoot))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_with_option_1, "mapRoot", "inlineSourceMap");
            }
        }

        if (Js.IsTruthy(_options.Composite))
        {
            if (_options.Declaration == false)
            {
                CreateDiagnosticForOptionName(Diagnostics.Composite_projects_may_not_disable_declaration_emit, "declaration");
            }
            if (_options.Incremental == false)
            {
                CreateDiagnosticForOptionName(Diagnostics.Composite_projects_may_not_disable_incremental_compilation, "declaration");
            }
        }

        var outputFile = _options.OutFile;
        if (!Js.IsTruthy(_options.TsBuildInfoFile) && Js.IsTruthy(_options.Incremental) && !Js.IsTruthy(outputFile) && !Js.IsTruthy(_options.ConfigFilePath))
        {
            _programDiagnostics.AddConfigDiagnostic(Utilities.CreateCompilerDiagnostic(Diagnostics.Option_incremental_can_only_be_specified_using_tsconfig_emitting_to_single_file_or_when_option_tsBuildInfoFile_is_specified));
        }

        VerifyDeprecatedCompilerOptions();
        VerifyProjectReferences();

        // List of collected files is complete; validate exhautiveness if this is a project with a file list
        if (Js.IsTruthy(_options.Composite))
        {
            var rootPaths = new HashSet<string>(_rootNames.Select(ToPath));
            foreach (var file in _files)
            {
                // Ignore file that is not emitted
                if (Utilities.SourceFileMayBeEmitted(file, this) && !rootPaths.Contains(file.Path ?? ""))
                {
                    _programDiagnostics.AddLazyConfigDiagnostic(
                        file,
                        Diagnostics.File_0_is_not_listed_within_the_file_list_of_project_1_Projects_must_list_all_files_or_use_an_include_pattern,
                        file.FileName,
                        _options.ConfigFilePath ?? "");
                }
            }
        }

        if (_options.Paths is not null)
        {
            // PORT-NOTE: TS walks the map with `for (const key in options.paths)` and skips keys `hasProperty` says are
            // not own; enumerating the port's Dictionary yields exactly its own keys, so the guard is not ported.
            foreach (var key in _options.Paths.Keys)
            {
                if (!Utilities.HasZeroOrOneAsteriskCharacter(key))
                {
                    CreateDiagnosticForOptionPaths(/*onKey*/ true, key, Diagnostics.Pattern_0_can_have_at_most_one_Asterisk_character, key);
                }
                var substitutions = _options.Paths[key];
                if (substitutions is not null)
                {
                    var len = substitutions.Count;
                    if (len == 0)
                    {
                        CreateDiagnosticForOptionPaths(/*onKey*/ false, key, Diagnostics.Substitutions_for_pattern_0_shouldn_t_be_an_empty_array, key);
                    }
                    for (var i = 0; i < len; i++)
                    {
                        var subst = substitutions[i];
                        if (!Utilities.HasZeroOrOneAsteriskCharacter(subst))
                        {
                            CreateDiagnosticForOptionPathKeyValue(key, i, Diagnostics.Substitution_0_in_pattern_1_can_have_at_most_one_Asterisk_character, subst, key);
                        }
                        if (!Js.IsTruthy(_options.BaseUrl) && !Paths.PathIsRelative(subst) && !Paths.PathIsAbsolute(subst))
                        {
                            CreateDiagnosticForOptionPathKeyValue(key, i, Diagnostics.Non_relative_paths_are_not_allowed_when_baseUrl_is_not_set_Did_you_forget_a_leading_Slash);
                        }
                    }
                }
                else
                {
                    CreateDiagnosticForOptionPaths(/*onKey*/ false, key, Diagnostics.Substitutions_for_pattern_0_should_be_an_array, key);
                }
            }
        }

        if (!Js.IsTruthy(_options.SourceMap) && !Js.IsTruthy(_options.InlineSourceMap))
        {
            if (Js.IsTruthy(_options.InlineSources))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_can_only_be_used_when_either_option_inlineSourceMap_or_option_sourceMap_is_provided, "inlineSources");
            }
            if (Js.IsTruthy(_options.SourceRoot))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_can_only_be_used_when_either_option_inlineSourceMap_or_option_sourceMap_is_provided, "sourceRoot");
            }
        }

        if (Js.IsTruthy(_options.MapRoot) && !(Js.IsTruthy(_options.SourceMap) || Js.IsTruthy(_options.DeclarationMap)))
        {
            // Error to specify --mapRoot without --sourcemap
            CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "mapRoot", "sourceMap", "declarationMap");
        }

        if (Js.IsTruthy(_options.DeclarationDir))
        {
            if (!Utilities.GetEmitDeclarations(_options))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "declarationDir", "declaration", "composite");
            }
            if (Js.IsTruthy(outputFile))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_with_option_1, "declarationDir", "outFile");
            }
        }

        if (Js.IsTruthy(_options.DeclarationMap) && !Utilities.GetEmitDeclarations(_options))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "declarationMap", "declaration", "composite");
        }

        if (_options.Lib is not null && Js.IsTruthy(_options.NoLib))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_with_option_1, "lib", "noLib");
        }

        var languageVersion = Utilities.GetEmitScriptTarget(_options);

        var firstNonAmbientExternalModuleSourceFile = Core.Find(_files, (f, _) => Parser.IsExternalModule(f) && !f.IsDeclarationFile);
        if (Js.IsTruthy(_options.IsolatedModules) || Js.IsTruthy(_options.VerbatimModuleSyntax))
        {
            if (_options.Module == ModuleKind.None && languageVersion < ScriptTarget.ES2015 && Js.IsTruthy(_options.IsolatedModules))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_isolatedModules_can_only_be_used_when_either_option_module_is_provided_or_option_target_is_ES2015_or_higher, "isolatedModules", "target");
            }

            if (_options.PreserveConstEnums == false)
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_preserveConstEnums_cannot_be_disabled_when_0_is_enabled, Js.IsTruthy(_options.VerbatimModuleSyntax) ? "verbatimModuleSyntax" : "isolatedModules", "preserveConstEnums");
            }
        }
        else if (firstNonAmbientExternalModuleSourceFile is not null && languageVersion < ScriptTarget.ES2015 && _options.Module == ModuleKind.None)
        {
            // We cannot use createDiagnosticFromNode because nodes do not have parents yet
            var span = Utilities.GetErrorSpanForNode(firstNonAmbientExternalModuleSourceFile, ExternalModuleIndicatorNode(firstNonAmbientExternalModuleSourceFile));
            _programDiagnostics.AddConfigDiagnostic(Utilities.CreateFileDiagnostic(firstNonAmbientExternalModuleSourceFile, span.Start, span.Length, Diagnostics.Cannot_use_imports_exports_or_module_augmentations_when_module_is_none));
        }

        // Cannot specify module gen that isn't amd or system with --out
        if (Js.IsTruthy(outputFile) && !Js.IsTruthy(_options.EmitDeclarationOnly))
        {
            if (Js.IsTruthy(_options.Module) && !(_options.Module == ModuleKind.AMD || _options.Module == ModuleKind.System))
            {
                CreateDiagnosticForOptionName(Diagnostics.Only_amd_and_system_modules_are_supported_alongside_0, "outFile", "module");
            }
            else if (_options.Module is null && firstNonAmbientExternalModuleSourceFile is not null)
            {
                var span = Utilities.GetErrorSpanForNode(firstNonAmbientExternalModuleSourceFile, ExternalModuleIndicatorNode(firstNonAmbientExternalModuleSourceFile));
                _programDiagnostics.AddConfigDiagnostic(Utilities.CreateFileDiagnostic(firstNonAmbientExternalModuleSourceFile, span.Start, span.Length, Diagnostics.Cannot_compile_modules_using_option_0_unless_the_module_flag_is_amd_or_system, "outFile"));
            }
        }

        if (Utilities.GetResolveJsonModule(_options))
        {
            if (Utilities.GetEmitModuleResolutionKind(_options) == ModuleResolutionKind.Classic)
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_resolveJsonModule_cannot_be_specified_when_moduleResolution_is_set_to_classic, "resolveJsonModule");
            }
            else if (!Utilities.HasJsonModuleEmitEnabled(_options))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_resolveJsonModule_cannot_be_specified_when_module_is_set_to_none_system_or_umd, "resolveJsonModule", "module");
            }
        }

        // there has to be common source directory if user specified --outdir || --rootDir || --sourceRoot
        // if user specified --mapRoot, there needs to be common source directory if there would be multiple files being emitted
        if (
            Js.IsTruthy(_options.OutDir) || // there is --outDir specified
            Js.IsTruthy(_options.RootDir) || // there is --rootDir specified
            Js.IsTruthy(_options.SourceRoot) || // there is --sourceRoot specified
            Js.IsTruthy(_options.MapRoot) || // there is --mapRoot specified
            (Utilities.GetEmitDeclarations(_options) && Js.IsTruthy(_options.DeclarationDir)) // there is --declarationDir specified
        )
        {
            // Precalculate and cache the common source directory
            var dir = GetCommonSourceDirectory();

            // If we failed to find a good common directory, but outDir is specified and at least one of our files is on a windows drive/URL/other resource, add a failure
            if (Js.IsTruthy(_options.OutDir) && dir == "" && _files.Any(file => Paths.GetRootLength(file.FileName ?? "") > 1))
            {
                CreateDiagnosticForOptionName(Diagnostics.Cannot_find_the_common_subdirectory_path_for_the_input_files, "outDir");
            }
        }

        // PORT-NOTE: TS reads `options.configFilePath` three times in this block; the port reads it once into a local,
        // because the compiler narrows a local (not a property) after `Js.IsTruthy`, and the two path helpers below
        // take a non-null string.
        var configFilePath = _options.ConfigFilePath;
        if (
            !Js.IsTruthy(_options.NoEmit) &&
            !Js.IsTruthy(_options.Composite) &&
            !Js.IsTruthy(_options.RootDir) &&
            Js.IsTruthy(configFilePath) &&
            (Js.IsTruthy(_options.OutDir) || // there is --outDir specified
                (Utilities.GetEmitDeclarations(_options) && Js.IsTruthy(_options.DeclarationDir)) || // there is --declarationDir specified
                Js.IsTruthy(outputFile))
        )
        {
            // Check if rootDir inferred changed and issue diagnostic
            var dir = GetCommonSourceDirectory();
            // PORT-NOTE: TS's `mapDefined(files, f => … ? f.fileName : undefined)`; the LINQ form is the port's spelling
            // for "map and drop the missing entries" and is what Program.State.cs's GetCommonSourceDirectory already
            // uses for the same list.
            var emittedFiles = _files
                .Where(file => !file.IsDeclarationFile && Utilities.SourceFileMayBeEmitted(file, this))
                .Select(file => file.FileName)
                .OfType<string>()
                .ToList();
            var dir59 = GetComputedCommonSourceDirectory(emittedFiles, _currentDirectory, _getCanonicalFileName);
            if (dir59 != "" && _getCanonicalFileName(dir) != _getCanonicalFileName(dir59))
            {
                // change in layout
                CreateDiagnosticForOption(
                    /*onKey*/ true,
                    Js.IsTruthy(outputFile) ? "outFile" : Js.IsTruthy(_options.OutDir) ? "outDir" : "declarationDir",
                    !Js.IsTruthy(outputFile) && Js.IsTruthy(_options.OutDir) ? "declarationDir" : null,
                    Utilities.ChainDiagnosticMessages(
                        Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Visit_https_Colon_Slash_Slashaka_ms_Slashts6_for_migration_information),
                        Diagnostics.The_common_source_directory_of_0_is_1_The_rootDir_setting_must_be_explicitly_set_to_this_or_another_path_to_adjust_your_output_s_file_layout,
                        Paths.GetBaseFileName(configFilePath),
                        Paths.GetRelativePathFromFile(configFilePath, dir59, _getCanonicalFileName)));
            }
        }

        if (Js.IsTruthy(_options.CheckJs) && !Utilities.GetAllowJsCompilerOption(_options))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1, "checkJs", "allowJs");
        }

        if (Js.IsTruthy(_options.EmitDeclarationOnly))
        {
            if (!Utilities.GetEmitDeclarations(_options))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1_or_option_2, "emitDeclarationOnly", "declaration", "composite");
            }
        }

        if (
            Js.IsTruthy(_options.EmitDecoratorMetadata) &&
            !Js.IsTruthy(_options.ExperimentalDecorators)
        ) {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1, "emitDecoratorMetadata", "experimentalDecorators");
        }

        if (Js.IsTruthy(_options.JsxFactory))
        {
            if (Js.IsTruthy(_options.ReactNamespace))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_with_option_1, "reactNamespace", "jsxFactory");
            }
            if (_options.Jsx == JsxEmit.ReactJSX || _options.Jsx == JsxEmit.ReactJSXDev)
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_when_option_jsx_is_1, "jsxFactory", Utilities.InverseJsxOptionMap.GetValueOrDefault(_options.Jsx.Value));
            }
            if (Parser.ParseIsolatedEntityName(_options.JsxFactory, languageVersion) is null)
            {
                CreateOptionValueDiagnostic("jsxFactory", Diagnostics.Invalid_value_for_jsxFactory_0_is_not_a_valid_identifier_or_qualified_name, _options.JsxFactory);
            }
        }
        else if (Js.IsTruthy(_options.ReactNamespace) && !ScannerUtilities.IsIdentifierText(_options.ReactNamespace, languageVersion))
        {
            CreateOptionValueDiagnostic("reactNamespace", Diagnostics.Invalid_value_for_reactNamespace_0_is_not_a_valid_identifier, _options.ReactNamespace);
        }

        if (Js.IsTruthy(_options.JsxFragmentFactory))
        {
            if (!Js.IsTruthy(_options.JsxFactory))
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_without_specifying_option_1, "jsxFragmentFactory", "jsxFactory");
            }
            if (_options.Jsx == JsxEmit.ReactJSX || _options.Jsx == JsxEmit.ReactJSXDev)
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_when_option_jsx_is_1, "jsxFragmentFactory", Utilities.InverseJsxOptionMap.GetValueOrDefault(_options.Jsx.Value));
            }
            if (Parser.ParseIsolatedEntityName(_options.JsxFragmentFactory, languageVersion) is null)
            {
                CreateOptionValueDiagnostic("jsxFragmentFactory", Diagnostics.Invalid_value_for_jsxFragmentFactory_0_is_not_a_valid_identifier_or_qualified_name, _options.JsxFragmentFactory);
            }
        }

        if (Js.IsTruthy(_options.ReactNamespace))
        {
            if (_options.Jsx == JsxEmit.ReactJSX || _options.Jsx == JsxEmit.ReactJSXDev)
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_when_option_jsx_is_1, "reactNamespace", Utilities.InverseJsxOptionMap.GetValueOrDefault(_options.Jsx.Value));
            }
        }

        if (Js.IsTruthy(_options.JsxImportSource))
        {
            if (_options.Jsx == JsxEmit.React)
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_0_cannot_be_specified_when_option_jsx_is_1, "jsxImportSource", Utilities.InverseJsxOptionMap.GetValueOrDefault(_options.Jsx.Value));
            }
        }

        var moduleKind = Utilities.GetEmitModuleKind(_options);
        if (Js.IsTruthy(_options.VerbatimModuleSyntax))
        {
            if (moduleKind == ModuleKind.AMD || moduleKind == ModuleKind.UMD || moduleKind == ModuleKind.System)
            {
                CreateDiagnosticForOptionName(Diagnostics.Option_verbatimModuleSyntax_cannot_be_used_when_module_is_set_to_UMD_AMD_or_System, "verbatimModuleSyntax");
            }
        }

        if (Js.IsTruthy(_options.AllowImportingTsExtensions) && !(Js.IsTruthy(_options.NoEmit) || Js.IsTruthy(_options.EmitDeclarationOnly) || Js.IsTruthy(_options.RewriteRelativeImportExtensions)))
        {
            CreateOptionValueDiagnostic("allowImportingTsExtensions", Diagnostics.Option_allowImportingTsExtensions_can_only_be_used_when_one_of_noEmit_emitDeclarationOnly_or_rewriteRelativeImportExtensions_is_set);
        }

        var moduleResolution = Utilities.GetEmitModuleResolutionKind(_options);
        if (Js.IsTruthy(_options.ResolvePackageJsonExports) && !Utilities.ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_can_only_be_used_when_moduleResolution_is_set_to_node16_nodenext_or_bundler, "resolvePackageJsonExports");
        }
        if (Js.IsTruthy(_options.ResolvePackageJsonImports) && !Utilities.ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_can_only_be_used_when_moduleResolution_is_set_to_node16_nodenext_or_bundler, "resolvePackageJsonImports");
        }
        if (_options.CustomConditions is not null && !Utilities.ModuleResolutionSupportsPackageJsonExportsAndImports(moduleResolution))
        {
            CreateDiagnosticForOptionName(Diagnostics.Option_0_can_only_be_used_when_moduleResolution_is_set_to_node16_nodenext_or_bundler, "customConditions");
        }

        if (moduleResolution == ModuleResolutionKind.Bundler && !Utilities.EmitModuleKindIsNonNodeEsm(moduleKind) && moduleKind != ModuleKind.Preserve && moduleKind != ModuleKind.CommonJS)
        {
            CreateOptionValueDiagnostic("moduleResolution", Diagnostics.Option_0_can_only_be_used_when_module_is_set_to_preserve_commonjs_or_es2015_or_later, "bundler");
        }

        if (
            Enum.IsDefined(moduleKind) &&
            ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext &&
            !(ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext)
        ) {
            var moduleKindName = Enum.GetName(moduleKind)!;
            var moduleResolutionName = Enum.IsDefined(typeof(ModuleResolutionKind), moduleKindName) ? moduleKindName : "Node16";
            CreateOptionValueDiagnostic("moduleResolution", Diagnostics.Option_moduleResolution_must_be_set_to_0_or_left_unspecified_when_option_module_is_set_to_1, moduleResolutionName, moduleKindName);
        }
        else if (
            Enum.IsDefined(moduleResolution) &&
            ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext &&
            !(ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext)
        ) {
            var moduleResolutionName = Enum.GetName(moduleResolution)!;
            CreateOptionValueDiagnostic("module", Diagnostics.Option_module_must_be_set_to_0_when_option_moduleResolution_is_set_to_1, moduleResolutionName, moduleResolutionName);
        }

        // If the emit is enabled make sure that every output file is unique and not overwriting any of the input files
        // PORT-NOTE: `forEachEmittedFile` (emitter.ts:446-474) and `getTsBuildInfoEmitOutputFilePath` (emitter.ts:481-497)
        // are M7's. The port declares the emitter.ts members the program needs as static members of `Program` — the
        // arrangement `Program.GetComputedCommonSourceDirectory` and `Program.GetCommonSourceDirectoryOfConfig` already
        // record — so both are called unqualified here. A class named `Emit` would not be addressable from inside
        // `Program` in any case: the M7-boundary method `Program.Emit` (program.ts:2688) hides the type name (CS0119).
        if (!Js.IsTruthy(_options.NoEmit) && !Js.IsTruthy(_options.SuppressOutputPathCheck))
        {
            var emitHost = GetEmitHost();
            var emitFilesSeen = new HashSet<string>();
            ForEachEmittedFile(emitHost, (emitFileNames, _) =>
            {
                if (!Js.IsTruthy(_options.EmitDeclarationOnly))
                {
                    VerifyEmitFilePath(emitFileNames.JsFilePath, emitFilesSeen);
                }
                VerifyEmitFilePath(emitFileNames.DeclarationFilePath, emitFilesSeen);
            });
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4404-4428 — <c>verifyEmitFilePath</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the local's type is <see cref="IDiagnosticMessageChain"/> rather than
    /// <see cref="DiagnosticMessageChain"/> because <c>Utilities.ChainDiagnosticMessages</c> is declared with the
    /// generated interface as its result (see that member's own note); TS's slot is the concrete
    /// <c>DiagnosticMessageChain | undefined</c>, and every use below only reads the interface's slots.
    /// </remarks>
    private void VerifyEmitFilePath(string? emitFileName, HashSet<string> emitFilesSeen)
    {
        if (Js.IsTruthy(emitFileName))
        {
            var emitFilePath = ToPath(emitFileName);
            // Report error if the output overwrites input file
            if (_filesByName.ContainsKey(emitFilePath))
            {
                IDiagnosticMessageChain? chain = null;
                if (_options.ConfigFilePath is null)
                {
                    // The program is from either an inferred project or an external project
                    chain = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Adding_a_tsconfig_json_file_will_help_organize_projects_that_contain_both_TypeScript_and_JavaScript_files_Learn_more_at_https_Colon_Slash_Slashaka_ms_Slashtsconfig);
                }
                chain = Utilities.ChainDiagnosticMessages(chain, Diagnostics.Cannot_write_file_0_because_it_would_overwrite_input_file, emitFileName);
                BlockEmittingOfFile(emitFileName, Utilities.CreateCompilerDiagnosticFromMessageChain(chain));
            }

            var emitFileKey = !Utilities.HostUsesCaseSensitiveFileNames(_host) ? Core.ToFileNameLowerCase(emitFilePath) : emitFilePath;
            // Report error if multiple files write into same file
            if (emitFilesSeen.Contains(emitFileKey))
            {
                // Already seen the same emit file - report error
                BlockEmittingOfFile(emitFileName, Utilities.CreateCompilerDiagnostic(Diagnostics.Cannot_write_file_0_because_it_would_be_overwritten_by_multiple_input_files, emitFileName));
            }
            else
            {
                emitFilesSeen.Add(emitFileKey);
            }
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4431-4440 — <c>getIgnoreDeprecationsVersion</c>.</summary>
    private Version GetIgnoreDeprecationsVersion()
    {
        var ignoreDeprecations = _options.IgnoreDeprecations;
        if (Js.IsTruthy(ignoreDeprecations))
        {
            if (ignoreDeprecations is "5.0" or "6.0")
            {
                return new Version(ignoreDeprecations);
            }
            _reportInvalidIgnoreDeprecations();
        }
        return Version.Zero;
    }

    /// <summary>ported from: src/compiler/program.ts:4445 — the <c>createDeprecatedDiagnostic</c> parameter of
    /// <c>checkDeprecations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types that parameter as a function whose last three parameters are optional and its callers use
    /// every prefix of the list (<c>createDeprecatedDiagnostic("target")</c>, <c>("target", "ES3")</c>, …);
    /// <see cref="Action{T1, T2, T3, T4}"/> cannot express an optional parameter, so the port declares the delegate —
    /// C# allows default values on a delegate's parameter list and applies them to a call through the delegate.
    /// </remarks>
    private delegate void CreateDeprecatedDiagnostic(string name, string? value = null, string? useInstead = null, DiagnosticMessage? related = null);

    /// <summary>ported from: src/compiler/program.ts:4442-4476 — <c>checkDeprecations</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <paramref name="createDiagnostic"/> and <paramref name="fn"/> are the port's spelling of TS's two
    /// function-typed parameters; <c>...args</c> (a rest parameter) cannot be part of a C# delegate's signature, so the
    /// arguments are passed as the <c>object?[]</c> the callee's own <c>params</c> parameter builds.
    /// </remarks>
    private void CheckDeprecations(
        string deprecatedIn,
        string removedIn,
        Action<string, string?, string?, DiagnosticMessage?, DiagnosticMessage, object?[]> createDiagnostic,
        Action<CreateDeprecatedDiagnostic> fn)
    {
        var deprecatedInVersion = new Version(deprecatedIn);
        var removedInVersion = new Version(removedIn);
        var typescriptVersion = new Version(Js.IsTruthy(_typeScriptVersion) ? _typeScriptVersion : VersionInfo.VersionMajorMinor);
        var ignoreDeprecationsVersion = GetIgnoreDeprecationsVersion();

        var mustBeRemoved = removedInVersion.CompareTo(typescriptVersion) != Comparison.GreaterThan;
        var canBeSilenced = !mustBeRemoved && ignoreDeprecationsVersion.CompareTo(deprecatedInVersion) == Comparison.LessThan;

        if (mustBeRemoved || canBeSilenced)
        {
            fn((name, value, useInstead, related) =>
            {
                if (mustBeRemoved)
                {
                    if (value is null)
                    {
                        createDiagnostic(name, value, useInstead, related, Diagnostics.Option_0_has_been_removed_Please_remove_it_from_your_configuration, [name]);
                    }
                    else
                    {
                        createDiagnostic(name, value, useInstead, related, Diagnostics.Option_0_1_has_been_removed_Please_remove_it_from_your_configuration, [name, value]);
                    }
                }
                else
                {
                    if (value is null)
                    {
                        createDiagnostic(name, value, useInstead, related, Diagnostics.Option_0_is_deprecated_and_will_stop_functioning_in_TypeScript_1_Specify_compilerOption_ignoreDeprecations_Colon_2_to_silence_this_error, [name, removedIn, deprecatedIn]);
                    }
                    else
                    {
                        createDiagnostic(name, value, useInstead, related, Diagnostics.Option_0_1_is_deprecated_and_will_stop_functioning_in_TypeScript_2_Specify_compilerOption_ignoreDeprecations_Colon_3_to_silence_this_error, [name, value, removedIn, deprecatedIn]);
                    }
                }
            });
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4478-4563 — <c>verifyDeprecatedCompilerOptions</c>.</summary>
    private void VerifyDeprecatedCompilerOptions()
    {
        CheckDeprecations("5.0", "5.5", CreateDiagnostic, createDeprecatedDiagnostic =>
        {
            if (_options.Target == ScriptTarget.ES3)
            {
                createDeprecatedDiagnostic("target", "ES3");
            }
            if (Js.IsTruthy(_options.NoImplicitUseStrict))
            {
                createDeprecatedDiagnostic("noImplicitUseStrict");
            }
            if (Js.IsTruthy(_options.KeyofStringsOnly))
            {
                createDeprecatedDiagnostic("keyofStringsOnly");
            }
            if (Js.IsTruthy(_options.SuppressExcessPropertyErrors))
            {
                createDeprecatedDiagnostic("suppressExcessPropertyErrors");
            }
            if (Js.IsTruthy(_options.SuppressImplicitAnyIndexErrors))
            {
                createDeprecatedDiagnostic("suppressImplicitAnyIndexErrors");
            }
            if (Js.IsTruthy(_options.NoStrictGenericChecks))
            {
                createDeprecatedDiagnostic("noStrictGenericChecks");
            }
            if (Js.IsTruthy(_options.Charset))
            {
                createDeprecatedDiagnostic("charset");
            }
            if (Js.IsTruthy(_options.Out))
            {
                createDeprecatedDiagnostic("out");
            }
            if (Js.IsTruthy(_options.ImportsNotUsedAsValues))
            {
                createDeprecatedDiagnostic("importsNotUsedAsValues", /*value*/ null, "verbatimModuleSyntax");
            }
            if (Js.IsTruthy(_options.PreserveValueImports))
            {
                createDeprecatedDiagnostic("preserveValueImports", /*value*/ null, "verbatimModuleSyntax");
            }
        });

        CheckDeprecations("6.0", "7.0", CreateDiagnostic, createDeprecatedDiagnostic =>
        {
            if (_options.AlwaysStrict == false)
            {
                createDeprecatedDiagnostic("alwaysStrict", "false", /*useInstead*/ null, /*related*/ null);
            }
            if (_options.Target == ScriptTarget.ES5)
            {
                createDeprecatedDiagnostic("target", "ES5");
            }
            if (_options.ModuleResolution == ModuleResolutionKind.Node10)
            {
                createDeprecatedDiagnostic("moduleResolution", "node10", /*useInstead*/ null, Diagnostics.Visit_https_Colon_Slash_Slashaka_ms_Slashts6_for_migration_information);
            }
            if (_options.ModuleResolution == ModuleResolutionKind.Classic)
            {
                createDeprecatedDiagnostic("moduleResolution", "classic", /*useInstead*/ null, /*related*/ null);
            }
            if (_options.BaseUrl is not null)
            {
                createDeprecatedDiagnostic("baseUrl", /*value*/ null, /*useInstead*/ null, Diagnostics.Visit_https_Colon_Slash_Slashaka_ms_Slashts6_for_migration_information);
            }
            if (_options.EsModuleInterop == false)
            {
                createDeprecatedDiagnostic("esModuleInterop", "false", /*useInstead*/ null, /*related*/ null);
            }
            if (_options.AllowSyntheticDefaultImports == false)
            {
                createDeprecatedDiagnostic("allowSyntheticDefaultImports", "false", /*useInstead*/ null, /*related*/ null);
            }
            if (Js.IsTruthy(_options.OutFile))
            {
                createDeprecatedDiagnostic("outFile");
            }
            if (_options.Module is ModuleKind.None or ModuleKind.AMD or ModuleKind.UMD or ModuleKind.System)
            {
                createDeprecatedDiagnostic("module", Enum.GetName(_options.Module.Value), /*useInstead*/ null, /*related*/ null);
            }
            if (_options.DownlevelIteration is not null)
            {
                createDeprecatedDiagnostic("downlevelIteration");
            }
        });
    }

    /// <summary>ported from: src/compiler/program.ts:4479-4496 — <c>createDiagnostic</c> (the nested function of
    /// <c>verifyDeprecatedCompilerOptions</c>).</summary>
    /// <remarks>
    /// PORT-NOTE: TS's last parameter is a rest parameter, and a rest parameter cannot be part of the
    /// <c>checkDeprecations</c> delegate; the port declares it as the <c>object?[]</c> that call site's <c>params</c>
    /// parameter supplies. The `'messageText' in message` test TS's <c>createDiagnosticForOption</c> uses becomes the
    /// chain overload of that member (see its note).
    /// </remarks>
    private void CreateDiagnostic(
        string name,
        string? value,
        string? useInstead,
        DiagnosticMessage? related,
        DiagnosticMessage message,
        params object?[] args)
    {
        if (Js.IsTruthy(useInstead))
        {
            var details = Utilities.ChainDiagnosticMessages(/*details*/ null, Diagnostics.Use_0_instead, useInstead);
            if (related is not null)
            {
                details = Utilities.ChainDiagnosticMessages(details, related);
            }
            var chain = Utilities.ChainDiagnosticMessages(details, message, args);
            CreateDiagnosticForOption(/*onKey*/ !Js.IsTruthy(value), name, /*option2*/ null, chain);
        }
        else
        {
            IDiagnosticMessageChain? details = null;
            if (related is not null)
            {
                details = Utilities.ChainDiagnosticMessages(/*details*/ null, related);
            }
            var chain = Utilities.ChainDiagnosticMessages(details, message, args);
            CreateDiagnosticForOption(/*onKey*/ !Js.IsTruthy(value), name, /*option2*/ null, chain);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4565-4575 — <c>verifyDeprecatedProjectReference</c>.</summary>
    private void VerifyDeprecatedProjectReference(IProjectReference @ref, JsonSourceFile? parentFile, int index)
    {
        // TS's nested createDiagnostic (4566-4568) ignores its first four parameters and reports on the reference's
        // entry in the config file; the port inlines it as the lambda below, which is the same closure.
        CheckDeprecations("5.0", "5.5",
            (_, _, _, _, message, args) => CreateDiagnosticForReference(parentFile, index, message, args),
            createDeprecatedDiagnostic =>
            {
                if (Js.IsTruthy(@ref.Prepend))
                {
                    createDeprecatedDiagnostic("prepend");
                }
            });
    }

    /// <summary>ported from: src/compiler/program.ts:4577-4585 — <c>addFilePreprocessingFileExplainingDiagnostic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS builds the <c>FilePreprocessingFileExplainingDiagnostic</c> document inline; the port hands the
    /// four values to the builder the port's <c>ProgramDiagnostics</c> declares beside that arm
    /// (<c>AddFileExplainingDiagnostic</c> — its own note records that this reference function is the builder it ports),
    /// so the shape of the document stays with the union it belongs to.
    /// </remarks>
    private void AddFilePreprocessingFileExplainingDiagnostic(SourceFile? file, IFileIncludeReason fileProcessingReason, DiagnosticMessage diagnostic, object?[] args)
        => _programDiagnostics.AddFileExplainingDiagnostic(file, fileProcessingReason, diagnostic, args);

    /// <summary>ported from: src/compiler/program.ts:4587-4615 — <c>verifyProjectReferences</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getTsBuildInfoEmitOutputFilePath</c> (emitter.ts:481) is M7's and is called unqualified for
    /// the reason <see cref="VerifyCompilerOptions"/>'s emit-path block records.
    /// <para>
    /// PORT-NOTE: the reference evaluates <c>buildInfoPath</c> eagerly (program.ts:4588), before
    /// <c>forEachProjectReference</c>, but reads it only inside the callback — and the callback cannot run without
    /// project references (utilities.ts:12268 iterates <c>resolvedProjectReferences</c>, which program.ts:1749-1751
    /// only fills when there are references). The port asks the same question the loop asks and only then calls the
    /// M7 member, so a program over a root file and no references (the M6 path, e.g. <c>ProgramTests</c>) never
    /// reaches the emitter; with references present the call and its value are the reference's.
    /// </para>
    /// </remarks>
    private void VerifyProjectReferences()
    {
        var hasProjectReferences = _projectReferences is { Count: > 0 };
        var buildInfoPath = hasProjectReferences && !Js.IsTruthy(_options.SuppressOutputPathCheck) ? GetTsBuildInfoEmitOutputFilePath(_options) : null;
        Utilities.ForEachProjectReference(
            _projectReferences,
            _resolvedProjectReferences,
            (resolvedRef, parent, index) =>
            {
                var @ref = (parent is not null ? parent.CommandLine?.ProjectReferences : _projectReferences)![index];
                var parentFile = parent?.SourceFile as JsonSourceFile;
                VerifyDeprecatedProjectReference(@ref, parentFile, index);
                if (resolvedRef is null)
                {
                    CreateDiagnosticForReference(parentFile, index, Diagnostics.File_0_not_found, @ref.Path);
                    return;
                }
                var options = resolvedRef.CommandLine!.Options!;
                if (!Js.IsTruthy(options.Composite) || Js.IsTruthy(options.NoEmit))
                {
                    // ok to not have composite if the current program is container only
                    var inputs = parent is not null ? parent.CommandLine?.FileNames : _rootNames;
                    if (inputs is not null && inputs.Count != 0)
                    {
                        if (!Js.IsTruthy(options.Composite)) CreateDiagnosticForReference(parentFile, index, Diagnostics.Referenced_project_0_must_have_setting_composite_Colon_true, @ref.Path);
                        if (Js.IsTruthy(options.NoEmit)) CreateDiagnosticForReference(parentFile, index, Diagnostics.Referenced_project_0_may_not_disable_emit, @ref.Path);
                    }
                }
                if (parent is null && buildInfoPath is not null && buildInfoPath == GetTsBuildInfoEmitOutputFilePath(options))
                {
                    CreateDiagnosticForReference(parentFile, index, Diagnostics.Cannot_write_file_0_because_it_will_overwrite_tsbuildinfo_file_generated_by_referenced_project_1, buildInfoPath, @ref.Path);
                    _hasEmitBlockingDiagnostics[ToPath(buildInfoPath)] = true;
                }
            });
    }

    /// <summary>ported from: src/compiler/program.ts:4617-4633 — <c>createDiagnosticForOptionPathKeyValue</c>.</summary>
    private void CreateDiagnosticForOptionPathKeyValue(string key, int valueIndex, DiagnosticMessage message, params object?[] args)
    {
        var needCompilerDiagnostic = true;
        ForEachOptionPathsSyntax<object?>(pathProp =>
        {
            if (NodeTests.IsObjectLiteralExpression(pathProp.Initializer!))
            {
                var pathsObjectLiteral = (ObjectLiteralExpression)pathProp.Initializer!;
                Utilities.ForEachPropertyAssignment<object?>(pathsObjectLiteral, key, keyProps =>
                {
                    var initializer = keyProps.Initializer;
                    if (NodeTests.IsArrayLiteralExpression(initializer!) && ((ArrayLiteralExpression)initializer!).Elements!.Count > valueIndex)
                    {
                        _programDiagnostics.AddConfigDiagnostic(Utilities.CreateDiagnosticForNodeInSourceFile(ConfigFileSource!, ((ArrayLiteralExpression)initializer!).Elements![valueIndex], message, args));
                        needCompilerDiagnostic = false;
                    }
                    return null;
                });
            }
            return null;
        });
        if (needCompilerDiagnostic)
        {
            CreateCompilerOptionsDiagnostic(message, args);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4635-4655 — <c>createDiagnosticForOptionPaths</c>.</summary>
    private void CreateDiagnosticForOptionPaths(bool onKey, string key, DiagnosticMessage message, params object?[] args)
    {
        var needCompilerDiagnostic = true;
        ForEachOptionPathsSyntax<object?>(pathProp =>
        {
            if (
                NodeTests.IsObjectLiteralExpression(pathProp.Initializer!) &&
                CreateOptionDiagnosticInObjectLiteralSyntax(
                    (ObjectLiteralExpression)pathProp.Initializer!,
                    onKey,
                    key,
                    /*key2*/ null,
                    message,
                    args)
            )
            {
                needCompilerDiagnostic = false;
            }
            return null;
        });
        if (needCompilerDiagnostic)
        {
            CreateCompilerOptionsDiagnostic(message, args);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4657-4659 — <c>forEachOptionPathsSyntax</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's callback answers <c>T | undefined</c> and a truthy answer stops the walk; both callers here
    /// (4619-4632 and 4637-4654) return nothing, so the port keeps the generic member and the callers answer
    /// <c>null</c> — the value TS's void callbacks produce, which never stops the walk.
    /// </remarks>
    private T? ForEachOptionPathsSyntax<T>(Func<PropertyAssignment, T?> callback)
        => Utilities.ForEachOptionsSyntaxByName(GetCompilerOptionsObjectLiteralSyntax(), "paths", callback);

    /// <summary>ported from: src/compiler/program.ts:4661-4664 — <c>createDiagnosticForOptionName</c>.</summary>
    private void CreateDiagnosticForOptionName(DiagnosticMessage message, string option1, string? option2 = null, string? option3 = null)
    {
        // TODO(jakebailey): this code makes assumptions about the format of the diagnostic messages.
        CreateDiagnosticForOption(/*onKey*/ true, option1, option2, message, option1, option2, option3);
    }

    /// <summary>ported from: src/compiler/program.ts:4666-4668 — <c>createOptionValueDiagnostic</c>.</summary>
    internal void CreateOptionValueDiagnostic(string option1, DiagnosticMessage message, params object?[] args)
        => CreateDiagnosticForOption(/*onKey*/ false, option1, /*option2*/ null, message, args);

    /// <summary>ported from: src/compiler/program.ts:4670-4678 — <c>createDiagnosticForReference</c>.</summary>
    private void CreateDiagnosticForReference(JsonSourceFile? sourceFile, int index, DiagnosticMessage message, params object?[] args)
    {
        var referencesSyntax = Utilities.ForEachTsConfigPropArray(_options.ConfigFile, "references", property => NodeTests.IsArrayLiteralExpression(property.Initializer!) ? (ArrayLiteralExpression)property.Initializer! : null);
        if (referencesSyntax is not null && referencesSyntax.Elements!.Count > index)
        {
            _programDiagnostics.AddConfigDiagnostic(Utilities.CreateDiagnosticForNodeInSourceFile(ConfigFileSource!, referencesSyntax.Elements![index], message, args));
        }
        else
        {
            _programDiagnostics.AddConfigDiagnostic(Utilities.CreateCompilerDiagnostic(message, args));
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4680-4690 — <c>createDiagnosticForOption</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS declares two overloads whose second differs only in the message type
    /// (<c>DiagnosticMessageChain</c> vs <c>DiagnosticMessage</c> plus a rest parameter) and dispatches inside with
    /// <c>'messageText' in message</c>; the port keeps the two overloads and lets the argument's type select the arm,
    /// so the <c>in</c> test becomes the parameter type of the shared body.
    /// </remarks>
    private void CreateDiagnosticForOption(bool onKey, string option1, string? option2, IDiagnosticMessageChain message)
        => CreateDiagnosticForOptionWorker(onKey, option1, option2, message, []);

    /// <summary>ported from: src/compiler/program.ts:4681 — <c>createDiagnosticForOption</c> (the message arm).</summary>
    private void CreateDiagnosticForOption(bool onKey, string option1, string? option2, DiagnosticMessage message, params object?[] args)
        => CreateDiagnosticForOptionWorker(onKey, option1, option2, message, args);

    /// <summary>ported from: src/compiler/program.ts:4682-4690 — the shared body of the two overloads.</summary>
    private void CreateDiagnosticForOptionWorker(bool onKey, string option1, string? option2, object message, object?[] args)
    {
        var compilerOptionsObjectLiteralSyntax = GetCompilerOptionsObjectLiteralSyntax();
        var needCompilerDiagnostic = compilerOptionsObjectLiteralSyntax is null ||
            !CreateOptionDiagnosticInObjectLiteralSyntaxWorker(compilerOptionsObjectLiteralSyntax, onKey, option1, option2, message, args);

        if (needCompilerDiagnostic)
        {
            CreateCompilerOptionsDiagnosticWorker(message, args);
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4692-4713 — <c>createCompilerOptionsDiagnostic</c>.</summary>
    private void CreateCompilerOptionsDiagnostic(IDiagnosticMessageChain message)
        => CreateCompilerOptionsDiagnosticWorker(message, []);

    /// <summary>ported from: src/compiler/program.ts:4693 — <c>createCompilerOptionsDiagnostic</c> (the message arm).</summary>
    private void CreateCompilerOptionsDiagnostic(DiagnosticMessage message, params object?[] args)
        => CreateCompilerOptionsDiagnosticWorker(message, args);

    /// <summary>ported from: src/compiler/program.ts:4695-4713 — the shared body of the two overloads.</summary>
    private void CreateCompilerOptionsDiagnosticWorker(object message, object?[] args)
    {
        var compilerOptionsProperty = GetCompilerOptionsPropertySyntax();
        if (compilerOptionsProperty is not null)
        {
            // eslint-disable-next-line local/no-in-operator
            if (message is IDiagnosticMessageChain chain)
            {
                _programDiagnostics.AddConfigDiagnostic(Utilities.CreateDiagnosticForNodeFromMessageChain(ConfigFileSource!, compilerOptionsProperty.Name!, chain));
            }
            else
            {
                _programDiagnostics.AddConfigDiagnostic(Utilities.CreateDiagnosticForNodeInSourceFile(ConfigFileSource!, compilerOptionsProperty.Name!, (DiagnosticMessage)message, args));
            }
        }
        // eslint-disable-next-line local/no-in-operator
        else if (message is IDiagnosticMessageChain chain)
        {
            _programDiagnostics.AddConfigDiagnostic(Utilities.CreateCompilerDiagnosticFromMessageChain(chain));
        }
        else
        {
            _programDiagnostics.AddConfigDiagnostic(Utilities.CreateCompilerDiagnostic((DiagnosticMessage)message, args));
        }
    }

    /// <summary>ported from: src/compiler/program.ts:4715-4721 — <c>getCompilerOptionsObjectLiteralSyntax</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's slot is <c>ObjectLiteralExpression | false | undefined</c> where <c>false</c> is the memoized
    /// "there is none"; the port keeps the two states apart with
    /// <see cref="_compilerOptionsObjectLiteralSyntaxComputed"/> (the pattern <c>Program/Program.State.cs</c> records)
    /// and answers TS's <c>|| undefined</c> by returning <c>null</c> for the <c>false</c> state. The member is what
    /// <c>createProgram</c> hands to <c>ProgramDiagnostics.CreateProgramDiagnostics</c>, so it stays a parameterless
    /// instance method (a <c>Func&lt;ObjectLiteralExpression?&gt;</c> method group).
    /// </remarks>
    internal ObjectLiteralExpression? GetCompilerOptionsObjectLiteralSyntax()
    {
        if (!_compilerOptionsObjectLiteralSyntaxComputed)
        {
            var compilerOptionsProperty = GetCompilerOptionsPropertySyntax();
            _compilerOptionsObjectLiteralSyntax = compilerOptionsProperty is not null
                ? Core.TryCast<ObjectLiteralExpression, Expression>(compilerOptionsProperty.Initializer, NodeTests.IsObjectLiteralExpression)
                : null;
            _compilerOptionsObjectLiteralSyntaxComputed = true;
        }
        return _compilerOptionsObjectLiteralSyntax;
    }

    /// <summary>ported from: src/compiler/program.ts:4723-4732 — <c>getCompilerOptionsPropertySyntax</c>.</summary>
    /// <remarks>PORT-NOTE: the memoized slot is the same <c>X | false | undefined</c> shape as
    /// <see cref="GetCompilerOptionsObjectLiteralSyntax"/>'s; see its note.</remarks>
    private PropertyAssignment? GetCompilerOptionsPropertySyntax()
    {
        if (!_compilerOptionsPropertySyntaxComputed)
        {
            _compilerOptionsPropertySyntax = Utilities.ForEachPropertyAssignment(
                Utilities.GetTsConfigObjectLiteralExpression(_options.ConfigFile),
                "compilerOptions",
                Core.Identity);
            _compilerOptionsPropertySyntaxComputed = true;
        }
        return _compilerOptionsPropertySyntax;
    }

    /// <summary>ported from: src/compiler/program.ts:4734-4750 — <c>createOptionDiagnosticInObjectLiteralSyntax</c>.</summary>
    private bool CreateOptionDiagnosticInObjectLiteralSyntax(ObjectLiteralExpression objectLiteral, bool onKey, string key1, string? key2, IDiagnosticMessageChain message)
        => CreateOptionDiagnosticInObjectLiteralSyntaxWorker(objectLiteral, onKey, key1, key2, message, []);

    /// <summary>ported from: src/compiler/program.ts:4735 — <c>createOptionDiagnosticInObjectLiteralSyntax</c> (the
    /// message arm).</summary>
    private bool CreateOptionDiagnosticInObjectLiteralSyntax(ObjectLiteralExpression objectLiteral, bool onKey, string key1, string? key2, DiagnosticMessage message, params object?[] args)
        => CreateOptionDiagnosticInObjectLiteralSyntaxWorker(objectLiteral, onKey, key1, key2, message, args);

    /// <summary>ported from: src/compiler/program.ts:4737-4750 — the shared body of the two overloads.</summary>
    /// <remarks>
    /// PORT-NOTE: TS dispatches on <c>'messageText' in message</c>; the port's two overloads select the arm by the
    /// argument's type, as <see cref="CreateDiagnosticForOption"/>'s note records. TS's callback answers nothing, so the
    /// <c>forEachPropertyAssignment</c> walk never stops early and the callback's result is <c>undefined</c> — the
    /// <c>null</c> returned here.
    /// </remarks>
    private bool CreateOptionDiagnosticInObjectLiteralSyntaxWorker(ObjectLiteralExpression objectLiteral, bool onKey, string key1, string? key2, object message, object?[] args)
    {
        var needsCompilerDiagnostic = false;
        Utilities.ForEachPropertyAssignment<object?>(objectLiteral, key1, prop =>
        {
            if (message is IDiagnosticMessageChain chain)
            {
                _programDiagnostics.AddConfigDiagnostic(Utilities.CreateDiagnosticForNodeFromMessageChain(ConfigFileSource!, onKey ? prop.Name! : prop.Initializer!, chain));
            }
            else
            {
                _programDiagnostics.AddConfigDiagnostic(Utilities.CreateDiagnosticForNodeInSourceFile(ConfigFileSource!, onKey ? prop.Name! : prop.Initializer!, (DiagnosticMessage)message, args));
            }
            needsCompilerDiagnostic = true;
            return null;
        }, key2);
        return needsCompilerDiagnostic;
    }

    /// <summary>ported from: the <c>options.configFile!</c> reads of program.ts:4673-4749 — <c>configFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated <see cref="ICompilerOptions.ConfigFile"/> is an <see cref="ITsConfigSourceFile"/>, while
    /// the tsconfig-syntax and diagnostic helpers take a <see cref="SourceFile"/> — TS's <c>TsConfigSourceFile</c>
    /// extends <c>JsonSourceFile</c> extends <c>SourceFile</c>, and the generator flattens that chain into one
    /// interface. The port's tsconfig file class (M8) is a <see cref="SourceFile"/>, so this single cast is the bridge,
    /// named once instead of at every use; the uses keep TS's <c>!</c> because these helpers are only reached when a
    /// config file exists (see <see cref="GetCompilerOptionsObjectLiteralSyntax"/>).
    /// </remarks>
    private SourceFile? ConfigFileSource => _options.ConfigFile as SourceFile;

    /// <summary>ported from: src/compiler/program.ts:4752-4755 — <c>blockEmittingOfFile</c>.</summary>
    private void BlockEmittingOfFile(string emitFileName, Diagnostic diag)
    {
        _hasEmitBlockingDiagnostics[ToPath(emitFileName)] = true;
        _programDiagnostics.AddConfigDiagnostic(diag);
    }

    /// <summary>ported from: src/compiler/program.ts:4757-4791 — <c>isEmittedFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS asks the host <c>host.useCaseSensitiveFileNames()</c>; the generated
    /// <see cref="IModuleResolutionHost"/> types that slot <c>object?</c> — a boolean or a function, because
    /// <c>sys.ts</c>'s own host fills it with the boolean — so the port calls
    /// <see cref="Utilities.HostUsesCaseSensitiveFileNames"/> (the normalizer <c>Utilities/Utilities.ModuleResolution.cs</c>
    /// declares for exactly that union). <see cref="VerifyEmitFilePath"/> makes the same call for its emit-file key.
    /// </remarks>
    internal bool IsEmittedFile(string file)
    {
        if (Js.IsTruthy(_options.NoEmit))
        {
            return false;
        }

        // If this is source file, its not emitted file
        var filePath = ToPath(file);
        if (GetSourceFileByPath(filePath) is not null)
        {
            return false;
        }

        // If options have --outFile or --out just check that
        var @out = _options.OutFile;
        if (Js.IsTruthy(@out))
        {
            return IsSameFile(filePath, @out) || IsSameFile(filePath, Utilities.RemoveFileExtension(@out) + Extension.Dts.Value);
        }

        // If declarationDir is specified, return if its a file in that directory
        if (Js.IsTruthy(_options.DeclarationDir) && Paths.ContainsPath(_options.DeclarationDir, filePath, _currentDirectory, !Utilities.HostUsesCaseSensitiveFileNames(_host)))
        {
            return true;
        }

        // If --outDir, check if file is in that directory
        if (Js.IsTruthy(_options.OutDir))
        {
            return Paths.ContainsPath(_options.OutDir, filePath, _currentDirectory, !Utilities.HostUsesCaseSensitiveFileNames(_host));
        }

        if (Paths.FileExtensionIsOneOf(filePath, Utilities.SupportedJSExtensionsFlat) || Parser.IsDeclarationFileName(filePath))
        {
            // Otherwise just check if sourceFile with the name exists
            var filePathWithoutExtension = Utilities.RemoveFileExtension(filePath);
            return GetSourceFileByPath(filePathWithoutExtension + Extension.Ts.Value) is not null
                || GetSourceFileByPath(filePathWithoutExtension + Extension.Tsx.Value) is not null;
        }
        return false;
    }

    /// <summary>ported from: src/compiler/program.ts:4793-4795 — <c>isSameFile</c>.</summary>
    private bool IsSameFile(string file1, string file2)
        => Paths.ComparePaths(file1, file2, _currentDirectory, !Utilities.HostUsesCaseSensitiveFileNames(_host)) == Comparison.EqualTo;

    /// <summary>ported from: src/compiler/program.ts:4797-4808 — <c>getSymlinkCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS tests <c>if (host.getSymlinkCache)</c> — whether the host implements that optional member. The
    /// generated <see cref="ICompilerHost"/> declares it, as it declares every optional host member, unconditionally, so
    /// a port host signals "no cache from me" by answering <c>null</c> and the test is on the answer; a host that does
    /// implement it is used exactly as the reference's is.
    /// <para>
    /// PORT-NOTE: TS guards the resolution walk with <c>files &amp;&amp;</c>; the state field is a non-null
    /// <see cref="List{T}"/> (<c>Program/Program.State.cs</c> initializes it to the empty list and nothing assigns
    /// <c>null</c>), so that guard is always true and is not ported.
    /// </para>
    /// <para>
    /// PORT-NOTE: the two <c>forEach…</c> members are passed as lambdas rather than as method groups: they declare the
    /// optional <c>file</c> parameter that TS's assignability forgives, and C# method-group conversion does not.
    /// </para>
    /// </remarks>
    internal SymlinkCache GetSymlinkCache()
    {
        var hostSymlinkCache = _host.GetSymlinkCache();
        if (hostSymlinkCache is not null)
        {
            return (SymlinkCache)hostSymlinkCache;
        }

        _symlinks ??= Utilities.CreateSymlinkCache(_currentDirectory, _getCanonicalFileName);
        // PORT-NOTE: the state slot is `object?` (Program/Program.State.cs records why: the concrete `SymlinkCache` is
        // declared outside the generated model), so this one cast is the boundary; the local is what the two members
        // below read. `Program.IProgram.cs` casts the same slot for `IModuleSpecifierResolutionHost`.
        var symlinks = (SymlinkCache)_symlinks;
        if (!symlinks.HasProcessedResolutions())
        {
            symlinks.SetSymlinksFromResolutions(
                callback => ForEachResolvedModule(callback),
                callback => ForEachResolvedTypeReferenceDirective(callback),
                _automaticTypeDirectiveResolutions);
        }
        return symlinks;
    }

    /// <summary>ported from: src/compiler/program.ts:4810-4812 — <c>getModeForUsageLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS types <c>usage</c> as <c>StringLiteralLike</c> (a string literal or a no-substitution template); the
    /// port's model has no union for it, so the parameter is the <see cref="LiteralExpression"/> base every string-ish
    /// literal shares — the widening <c>Utilities.IsStringLiteralLike</c>'s callers already record — and the worker keeps
    /// the reference's reading.
    /// <para>
    /// PORT-NOTE: the workers these wrappers call (<c>getModeForUsageLocationWorker</c> and its siblings,
    /// program.ts:936/958/4843-4890) are module-level functions of program.ts that another slice of this milestone
    /// declares as members of this class; they are called unqualified because that is how
    /// <c>Program/Program.Graph.cs</c> already calls <c>getModeForUsageLocationWorker</c>.
    /// </para>
    /// </remarks>
    internal ModuleKind? GetModeForUsageLocation(SourceFile file, LiteralExpression usage)
        => GetModeForUsageLocationWorker(file, usage, GetCompilerOptionsForFile(file));

    /// <summary>ported from: src/compiler/program.ts:4814-4816 — <c>getEmitSyntaxForUsageLocation</c>.</summary>
    internal ModuleKind? GetEmitSyntaxForUsageLocation(SourceFile file, LiteralExpression usage)
        => GetEmitSyntaxForUsageLocationWorker(file, usage, GetCompilerOptionsForFile(file));

    /// <summary>ported from: src/compiler/program.ts:4818-4820 — <c>getModeForResolutionAtIndex</c>.</summary>
    /// <remarks>PORT-NOTE: <c>getModuleNameStringLiteralAt</c> (program.ts:5190) is another module-level function of the
    /// file, declared by the same slice as the workers above and called unqualified for the same reason.</remarks>
    internal ModuleKind? GetModeForResolutionAtIndex(SourceFile file, int index)
        => GetModeForUsageLocation(file, GetModuleNameStringLiteralAt(file, index));

    /// <summary>ported from: src/compiler/program.ts:4822-4824 — <c>getDefaultResolutionModeForFile</c>.</summary>
    internal ModuleKind? GetDefaultResolutionModeForFile(SourceFile sourceFile)
        => GetDefaultResolutionModeForFileWorker(sourceFile, GetCompilerOptionsForFile(sourceFile));

    /// <summary>ported from: src/compiler/program.ts:4826-4828 — <c>getImpliedNodeFormatForEmit</c>.</summary>
    internal ModuleKind? GetImpliedNodeFormatForEmit(SourceFile sourceFile)
        => GetImpliedNodeFormatForEmitWorker(sourceFile, GetCompilerOptionsForFile(sourceFile));

    /// <summary>ported from: src/compiler/program.ts:4830-4832 — <c>getEmitModuleFormatOfFile</c>.</summary>
    internal ModuleKind GetEmitModuleFormatOfFile(SourceFile sourceFile)
        => GetEmitModuleFormatOfFileWorker(sourceFile, GetCompilerOptionsForFile(sourceFile));

    /// <summary>ported from: src/compiler/program.ts:4834-4836 — <c>shouldTransformImportCall</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated <see cref="IProgram"/> declares this member on itself (as does <c>IEmitHost</c>), while
    /// the other option-dependent wrappers are declared by <c>ITypeCheckerHost</c>/<c>IModuleSpecifierResolutionHost</c>
    /// and are reached through the explicit adapters in <c>Program/Program.IProgram.cs</c>. A <c>public</c> member
    /// satisfies every declaration whose parameter types match modulo their nullability annotations (which C# ignores for
    /// implementation), so this one is public while the TS-named members those adapters forward to stay
    /// <c>internal</c>; <see cref="GetModeForTypeReferenceDirectiveInFile"/>, which no generated interface carries, stays
    /// <c>internal</c> as well.
    /// </remarks>
    public bool ShouldTransformImportCall(SourceFile sourceFile)
        => ShouldTransformImportCallWorker(sourceFile, GetCompilerOptionsForFile(sourceFile));

    /// <summary>ported from: src/compiler/program.ts:4838-4840 — <c>getModeForTypeReferenceDirectiveInFile</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS writes <c>ref.resolutionMode || getDefaultResolutionModeForFile(sourceFile)</c>. The generated
    /// <see cref="IFileReference.ResolutionMode"/> is <c>object?</c> (the generator has no union for
    /// <c>ResolutionMode</c>), so the port casts it to <see cref="ModuleKind"/> at this one boundary — the cast every
    /// new signature's note records — and <c>||</c> is <c>??</c> because both members of that union are non-zero enum
    /// values, so JS truthiness and "is specified" agree.
    /// </remarks>
    internal ModuleKind? GetModeForTypeReferenceDirectiveInFile(IFileReference @ref, SourceFile sourceFile)
        => (ModuleKind?)@ref.ResolutionMode ?? GetDefaultResolutionModeForFile(sourceFile);

    /// <summary>ported from: the <c>typeof ….externalModuleIndicator === "boolean" ? … : ….externalModuleIndicator!</c>
    /// ternaries of program.ts:4217 and 4225.</summary>
    /// <remarks>
    /// PORT-NOTE: TS passes the file itself when the indicator is a boolean and the indicator node otherwise, because the
    /// binder sets the slot to <c>true</c> for a file whose module-ness came from something it did not keep a node for.
    /// The port's <see cref="SourceFile.ExternalModuleIndicator"/> is <c>object?</c> holding <c>true</c> or a
    /// <see cref="Node"/>, so the test is <c>is not true</c> — the idiom <c>Binder/Binder.Modules.cs</c> already uses for
    /// this slot, where the same slot's <c>object?</c> type is recorded.
    /// </remarks>
    private static Node ExternalModuleIndicatorNode(SourceFile file)
        => file.ExternalModuleIndicator is not true
            ? (Node)file.ExternalModuleIndicator!
            : file;
}

// ported from: src/compiler/utilities.ts:9435-9446 (hasZeroOrOneAsteriskCharacter)
/// <summary>
/// The two tables this slice needs that no other slice of M6 declares: <c>hasZeroOrOneAsteriskCharacter</c>, whose only
/// callers in the reference are <c>verifyCompilerOptions</c>'s two <c>paths</c> checks (program.ts:4145 and 4157), and
/// the JSX option table <c>inverseJsxOptionMap</c> (commandLineParser.ts:141), whose only consumers are the four JSX
/// checks (program.ts:4313, 4328, 4337, 4343).
/// </summary>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:9435-9446 — <c>hasZeroOrOneAsteriskCharacter</c>.</summary>
    public static bool HasZeroOrOneAsteriskCharacter(string str)
    {
        var seenAsterisk = false;
        for (var i = 0; i < str.Length; i++)
        {
            if ((int)str[i] == CharacterCodes.Asterisk)
            {
                if (!seenAsterisk)
                {
                    seenAsterisk = true;
                }
                else
                {
                    // have already seen asterisk
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>ported from: src/compiler/commandLineParser.ts:141 — <c>inverseJsxOptionMap</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the map TS derives is <c>new Map(mapIterator(jsxOptionMap.entries(), ([key, value]) =&gt; ["" + value,
    /// key]))</c> — keyed by the <em>stringified numeric value</em> of <c>JsxEmit</c> (commandLineParser.ts:132-138 holds
    /// the five entries). The port keys it by the enum itself, which is the same lookup with the conversion done once,
    /// here, instead of at each of the four call sites (program.ts:4313, 4328, 4337, 4343 — the map's only consumers).
    /// <para>
    /// PORT-NOTE: declared by this slice because the table it derives from is <c>commandLineParser.ts</c>'s (M8's) and
    /// this slice is its only consumer in the reference; M8 must call this member rather than declare a second copy —
    /// the same arrangement <c>Program.GetCommonSourceDirectoryFromOptions</c> records for the common source directory.
    /// </para>
    /// </remarks>
    public static readonly Dictionary<JsxEmit, string> InverseJsxOptionMap = new()
    {
        [JsxEmit.Preserve] = "preserve",
        [JsxEmit.ReactNative] = "react-native",
        [JsxEmit.ReactJSX] = "react-jsx",
        [JsxEmit.ReactJSXDev] = "react-jsxdev",
        [JsxEmit.React] = "react",
    };
}