using System.Collections;

namespace TypeScript.Compiler;

// ported from: src/compiler/program.ts:328-341 (findConfigFile, resolveTripleslashReference)
// ported from: src/compiler/program.ts:387-499 (createCompilerHost, createGetSourceFile, createWriteFileMeasuringIO, createCompilerHostWorker)
// ported from: src/compiler/program.ts:501-633 (CompilerHostLikeForCache/WithCache, changeCompilerHostLikeToUseCache)
// ported from: src/compiler/program.ts:634-650 (getPreEmitDiagnostics)
// ported from: src/compiler/program.ts:652-852 (the formatDiagnostics family, formatColorAndReset, formatCodeSpan, formatLocation, flattenDiagnosticMessageText)
// ported from: src/compiler/program.ts:854-1020 (the resolution-mode readers, getResolutionModeOverride, emptyResolution)
// ported from: src/compiler/program.ts:1036-1137 (the resolution name/mode getters, the two loaders, loadWithModeAwareCache)
// ported from: src/compiler/program.ts:1141-1161 (getInferredLibraryNameResolveFrom, getLibraryNameFromLibFileName)
// ported from: src/compiler/program.ts:1162-1227 (isReferencedFile, isReferenceFileLocation, getReferencedFileLocation)
// ported from: src/compiler/program.ts:1228-1369 (isProgramUptoDate, getConfigFileParsingDiagnostics, getImpliedNodeFormatForFile[Worker])
// ported from: src/compiler/program.ts:1469-1476 (shouldProgramCreateNewSources)
// ported from: src/compiler/program.ts:1478-1498 (createCreateProgramOptions)
// ported from: src/compiler/program.ts:4843-4880 (shouldTransformImportCallWorker, getEmitModuleFormatOfFileWorker, getImpliedNodeFormatForEmitWorker, getDefaultResolutionModeForFileWorker)
// ported from: src/compiler/program.ts:5056-5201 (emitSkippedWithNoDiagnostics, handleNoEmitOptions, filterSemanticDiagnostics,
//              parseConfigHostFromCompilerHostLike, resolveProjectReferencePath, getResolutionDiagnostic, getModuleNames, getModuleNameStringLiteralAt)
// ported from: src/compiler/utilities.ts:725-727 (optionsHaveChanges), 9014-9019 (importSyntaxAffectsModuleResolution),
//              9007-9011 (getOptionValueByName), 5276-5286 (walkUpParenthesizedExpressions),
//              4953-4955 (isImportCall), 7262-7264 (isJsonEqual), 8236-8254 (maybeBind), 8506-8518 (forEachEntry),
//              7767-7793 (compareDataObjects), 11824-11826 (isBuildInfoFile)
// ported from: src/compiler/commandLineParser.ts:1710 (sourceFileAffectingCompilerOptions)
/// <summary>
/// The module-level free functions of <c>program.ts</c>: the compiler-host factories, the diagnostic formatters, the
/// resolution-mode readers and the resolution loaders.
/// </summary>
/// <remarks>
/// PORT-NOTE: these members are <c>internal static</c> members of <see cref="Program"/> (a non-static class may carry
/// static members), not a separate host class, because the rest of the <c>Program</c> partials call them unqualified
/// and <c>Program.State.cs</c> already calls one of them through the type name.
/// <para>
/// PORT-NOTE: TS's <c>ResolutionMode</c> (<c>ModuleKind.ESNext | ModuleKind.CommonJS | undefined</c>) has no generated
/// C# type; every signature here spells it <see cref="ModuleKind"/> and casts at the generated-interface boundary, the
/// same treatment <c>Program.State.cs</c> records.
/// </para>
/// </remarks>
internal sealed partial class Program
{
    // ---------------------------------------------------------------------------------------------
    // 1. Config-file discovery and triple-slash references (program.ts:328-341)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:328-334 — <c>findConfigFile</c>.</summary>
    internal static string? FindConfigFile(string searchPath, Func<string, bool> fileExists, string configName = "tsconfig.json")
        => Paths.ForEachAncestorDirectory<string>(searchPath, ancestor =>
        {
            var fileName = Paths.CombinePaths(ancestor, configName);
            return fileExists(fileName) ? fileName : null;
        });

    /// <summary>ported from: src/compiler/program.ts:335-341 — <c>resolveTripleslashReference</c>.</summary>
    internal static string ResolveTripleslashReference(string moduleName, string containingFile)
    {
        var basePath = Paths.GetDirectoryPath(containingFile);
        var referencedFileName = Paths.IsRootedDiskPath(moduleName) ? moduleName : Paths.CombinePaths(basePath, moduleName);
        return Paths.NormalizePath(referencedFileName);
    }

    // ---------------------------------------------------------------------------------------------
    // 2. The compiler host (program.ts:387-499)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:387-389 — <c>createCompilerHost</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS defaults <c>system</c> to the <c>sys</c> singleton; §13.5 drops that singleton, so the port's
    /// factory requires the host to be passed (the same deviation <see cref="CreateCompilerHostWorker"/> records).
    /// </remarks>
    internal static CompilerHost CreateCompilerHost(ICompilerOptions options, bool? setParentNodes, ISystem system)
        => CreateCompilerHostWorker(options, setParentNodes, system);

    /// <summary>ported from: src/compiler/program.ts:392-412 — <c>createGetSourceFile</c>.</summary>
    /// <returns>The <c>CompilerHost.getSourceFile</c> instantiation, spelled as the delegate the generated
    /// <see cref="ICompilerHost.GetSourceFile"/> takes.</returns>
    /// <remarks>
    /// PORT-NOTE: TS's <c>ScriptTarget | CreateSourceFileOptions</c> parameter is the generated slot's <c>object?</c>;
    /// the port dispatches to the parser's two overloads (its own PORT-NOTE records why they exist) and fails loudly
    /// on anything else, since TS's union admits no third case.
    /// </remarks>
    internal static Func<string?, object?, Action<string>?, bool?, SourceFile?> CreateGetSourceFile(
        Func<string, string?> readFile,
        bool? setParentNodes)
        => (fileName, languageVersionOrOptions, onError, shouldCreateNewSourceFile) =>
        {
            string? text;
            try
            {
                Performance.Mark("beforeIORead");
                text = readFile(fileName ?? "");
                Performance.Mark("afterIORead");
                Performance.Measure("I/O Read", "beforeIORead", "afterIORead");
            }
            catch (Exception e)
            {
                onError?.Invoke(e.Message);
                text = "";
            }

            if (text is null)
            {
                return null;
            }

            return languageVersionOrOptions switch
            {
                CreateSourceFileOptions createSourceFileOptions => Parser.CreateSourceFile(
                    fileName ?? "",
                    text,
                    createSourceFileOptions,
                    setParentNodes ?? false),
                ScriptTarget scriptTarget => Parser.CreateSourceFile(
                    fileName ?? "",
                    text,
                    scriptTarget,
                    setParentNodes ?? false),
                _ => throw Debug.Fail<Exception>(
                    "`languageVersionOrOptions` must be a `ScriptTarget` or a `CreateSourceFileOptions`"),
            };
        };

    /// <summary>ported from: src/compiler/program.ts:415-446 — <c>createWriteFileMeasuringIO</c>.</summary>
    /// <returns>The <c>CompilerHost.writeFile</c> instantiation.</returns>
    internal static Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?> CreateWriteFileMeasuringIO(
        Action<string, string, bool> actualWriteFile,
        Action<string> createDirectory,
        Func<string, bool> directoryExists)
        => (fileName, data, writeByteOrderMark, onError, sourceFiles, writeFileData, emitData) =>
        {
            try
            {
                Performance.Mark("beforeIOWrite");

                // NOTE: If patchWriteFileEnsuringDirectory has been called,
                // the system.writeFile will do its own directory creation and
                // the ensureDirectoriesExist call will always be redundant.
                Files.WriteFileEnsuringDirectories(
                    fileName,
                    data,
                    writeByteOrderMark,
                    actualWriteFile,
                    createDirectory,
                    directoryExists);

                Performance.Mark("afterIOWrite");
                Performance.Measure("I/O Write", "beforeIOWrite", "afterIOWrite");
            }
            catch (Exception e)
            {
                onError?.Invoke(e.Message);
            }
        };

    /// <summary>ported from: src/compiler/program.ts:448-499 — <c>createCompilerHostWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS defaults <c>system</c> to the <c>sys</c> singleton (<c>system: System = sys</c>); §13.5 drops
    /// that singleton, so <paramref name="system"/> is required here. The object literal the reference returns is the
    /// <see cref="CompilerHost"/> class (§5 rule 6), which holds the closure's state.
    /// </remarks>
    internal static CompilerHost CreateCompilerHostWorker(
        ICompilerOptions options,
        bool? setParentNodes,
        ISystem system,
        LibSourceFileRegistry? libSourceFileRegistry = null)
        => new(options, setParentNodes, system, libSourceFileRegistry);

    // ---------------------------------------------------------------------------------------------
    // 3. changeCompilerHostLikeToUseCache (program.ts:520-633)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:648-653 — <c>changeCompilerHostLikeToUseCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS mutates the host's own members (<c>host.readFile = …</c>), which C# cannot do to an interface's
    /// methods; the port keeps the same construction on the handwritten <c>…Func</c> slots the two cache interfaces
    /// carry (see <see cref="ICompilerHostLikeForCache"/>), so a caller that wants the cache installs it on those.
    /// <para>
    /// PORT-NOTE: TS's <c>Map&lt;Path, string | false&gt;</c> read-file cache uses <c>false</c> for "read, and the
    /// file is missing"; a <see cref="Dictionary{TKey,TValue}"/> value cannot hold that and <c>undefined</c> apart, so
    /// the port stores <c>null</c> for the absent case and distinguishes it from "not in the cache" with
    /// <c>TryGetValue</c>.
    /// </para>
    /// </remarks>
    internal static ICompilerHostLikeWithCache ChangeCompilerHostLikeToUseCache(
        ICompilerHostLikeForCache host,
        Core.GetCanonicalFileName toPath,
        Func<string?, object?, Action<string>?, bool?, SourceFile?>? getSourceFile = null)
    {
        var originalReadFile = host.ReadFileFunc ?? ((fileName, encoding) => host.ReadFile(fileName, encoding));
        var originalFileExists = host.FileExistsFunc ?? (fileName => host.FileExists(fileName));
        var originalDirectoryExists = host.DirectoryExistsFunc;
        var originalCreateDirectory = host.CreateDirectoryFunc;
        var originalWriteFile = host.WriteFile;
        var readFileCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var fileExistsCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        var directoryExistsCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        // PORT-NOTE: TS keys this cache by `ResolutionMode | undefined`, and a JS Map accepts `undefined` as a key. A
        // C# dictionary rejects a null key for a nullable value type (`notnull`), so the absent case gets the sentinel.
        var sourceFileCache = new Dictionary<object, Dictionary<string, SourceFile>>();

        string? ReadFileWithCache(string fileName)
        {
            var key = toPath(fileName);
            if (readFileCache.TryGetValue(key, out var value))
            {
                return value; // could be .d.ts from output
            }

            return SetReadFileCache(key, fileName);
        }

        string? SetReadFileCache(string key, string fileName)
        {
            var newValue = originalReadFile(fileName, null);
            readFileCache[key] = newValue;
            return newValue;
        }

        host.ReadFileFunc = (fileName, _encoding) =>
        {
            var key = toPath(fileName ?? "");
            if (readFileCache.TryGetValue(key, out var value))
            {
                return value; // could be .d.ts from output
            }

            // Cache json or buildInfo
            if (!Paths.FileExtensionIs(fileName ?? "", Extension.Json.Value) && !Utilities.IsBuildInfoFile(fileName ?? ""))
            {
                return originalReadFile(fileName, null);
            }

            return SetReadFileCache(key, fileName ?? "");
        };

        var getSourceFileWithCache = CreateGetSourceFileWithCache();
        Func<string?, object?, Action<string>?, bool?, SourceFile?>? CreateGetSourceFileWithCache()
        {
            if (getSourceFile is not { } getSourceFileNonNull)
            {
                return null;
            }

            return (fileName, languageVersionOrOptions, onError, shouldCreateNewSourceFile) =>
            {
                var key = toPath(fileName ?? "");
                var impliedNodeFormat = languageVersionOrOptions is CreateSourceFileOptions options
                    ? options.ImpliedNodeFormat
                    : null;
                var cacheKey = impliedNodeFormat ?? NoImpliedNodeFormatKey;
                var forImpliedNodeFormat = sourceFileCache.GetValueOrDefault(cacheKey);
                var value = forImpliedNodeFormat?.GetValueOrDefault(key);
                if (value is not null)
                {
                    return value;
                }

                var sourceFile = getSourceFileNonNull(fileName, languageVersionOrOptions, onError, shouldCreateNewSourceFile);
                if (sourceFile is not null
                    && (Parser.IsDeclarationFileName(fileName ?? "") || Paths.FileExtensionIs(fileName ?? "", Extension.Json.Value)))
                {
                    var map = forImpliedNodeFormat ?? new Dictionary<string, SourceFile>(StringComparer.Ordinal);
                    map[key] = sourceFile;
                    sourceFileCache[cacheKey] = map;
                }

                return sourceFile;
            };
        }

        // fileExists for any kind of extension
        host.FileExistsFunc = fileName =>
        {
            var key = toPath(fileName ?? "");
            if (fileExistsCache.TryGetValue(key, out var value))
            {
                return value;
            }

            var newValue = originalFileExists(fileName);
            fileExistsCache[key] = newValue;
            return newValue;
        };

        if (originalWriteFile is { } originalWriteFileNonNull)
        {
            host.WriteFile = (fileName, data, writeByteOrderMark, onError, sourceFiles, writeFileData, emitData) =>
            {
                var key = toPath(fileName);
                fileExistsCache.Remove(key);

                if (readFileCache.TryGetValue(key, out var value) && value != data)
                {
                    readFileCache.Remove(key);
                    foreach (var map in sourceFileCache.Values)
                    {
                        map.Remove(key);
                    }
                }
                else if (getSourceFileWithCache is not null)
                {
                    foreach (var map in sourceFileCache.Values)
                    {
                        if (map.TryGetValue(key, out var sourceFile) && sourceFile.Text != data)
                        {
                            map.Remove(key);
                        }
                    }
                }

                originalWriteFileNonNull(fileName, data, writeByteOrderMark, onError, sourceFiles, writeFileData, emitData);
            };
        }

        // directoryExists
        if (originalDirectoryExists is { } originalDirectoryExistsNonNull)
        {
            host.DirectoryExistsFunc = directory =>
            {
                var key = toPath(directory ?? "");
                if (directoryExistsCache.TryGetValue(key, out var value))
                {
                    return value;
                }

                var newValue = originalDirectoryExistsNonNull(directory);
                directoryExistsCache[key] = newValue;
                return newValue;
            };

            if (originalCreateDirectory is { } originalCreateDirectoryNonNull)
            {
                host.CreateDirectoryFunc = directory =>
                {
                    var key = toPath(directory ?? "");
                    directoryExistsCache.Remove(key);
                    originalCreateDirectoryNonNull(directory);
                };
            }
        }

        // PORT-NOTE: TS returns the object literal above; the port needs the constructible type, and the three
        // delegates the generated interface types without a null return get their null suppressed here (the
        // reference's own `string | undefined` is what they answer).
        Func<string, object?, Action<string>, bool, SourceFile>? GetSourceFileWithCacheFunc()
        {
            if (getSourceFileWithCache is not { } sourceFileWithCacheNonNull)
            {
                return null;
            }

            return (fileName, languageVersionOrOptions, onError, shouldCreateNewSourceFile) =>
                sourceFileWithCacheNonNull(fileName, languageVersionOrOptions, onError, shouldCreateNewSourceFile)!;
        }

        return new CompilerHostLikeWithCache
        {
            OriginalReadFile = (fileName, encoding) => originalReadFile(fileName, encoding)!,
            OriginalFileExists = fileName => originalFileExists(fileName),
            OriginalDirectoryExists = originalDirectoryExists is { } directoryExistsWorker
                ? directory => directoryExistsWorker(directory)
                : null,
            OriginalCreateDirectory = originalCreateDirectory is { } createDirectoryWorker
                ? directory => createDirectoryWorker(directory)
                : null,
            OriginalWriteFile = originalWriteFile,
            GetSourceFileWithCache = GetSourceFileWithCacheFunc(),
            ReadFileWithCache = fileName => ReadFileWithCache(fileName)!,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // 4. The concrete CompilerHost (program.ts:448-499, the object literal createCompilerHostWorker builds)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:448-499 — the <c>CompilerHost</c> object literal.</summary>
    /// <remarks>
    /// PORT-NOTE: §5 rule 6 turns the reference's object literal into this class; the closures the literal held
    /// (<c>existingDirectories</c>, <c>getCanonicalFileName</c>, <c>newLine</c>, the memoized
    /// <c>getCurrentDirectory</c>, the <c>directoryExists</c>/<c>getDefaultLibLocation</c> helpers) are its fields and
    /// private methods.
    /// <para>
    /// PORT-NOTE: TS's <c>CompilerHost</c> marks roughly twenty members optional and this literal defines only the
    /// eighteen it needs; the generated <see cref="ICompilerHost"/> declares all of them as required, so the port
    /// carries each optional member's "absent" state in a nullable <c>…Func</c> slot declared on the interface's
    /// handwritten part (see below). The interface member is unreachable while the slot is null — a caller that wants
    /// to know whether the member is provided tests the slot, exactly as the reference's
    /// <c>host.resolveModuleNames ? … : …</c> does — and fails loudly rather than inventing a resolution if it is
    /// called anyway (the error TS gets from calling <c>undefined</c>).
    /// </para>
    /// <para>
    /// PORT-NOTE: TS's <c>realpath</c> slot is <c>system.realpath &amp;&amp; …</c>, i.e. <c>undefined</c> on a host
    /// without it; <see cref="ISystem.Realpath"/> is required with an identity default (§13.2), so the port cannot tell
    /// "no realpath" from "identity realpath" and always installs one.
    /// </para>
    /// </remarks>
    /// <summary>One file the program asks a host to parse ahead of the file graph walk.</summary>
    /// <remarks>
    /// PORT-NOTE: nothing in TS — the reference parses each file inside its own <c>getSourceFile</c> call, on the
    /// single thread that drives <c>createProgram</c> (see <see cref="IParallelSourcePrefetchHost"/>).
    /// </remarks>
    internal readonly record struct SourceFileRequest(string FileName, CreateSourceFileOptions Options);

    /// <summary>
    /// NOT ported from TS — an optional host hook the program uses to overlap the per-file read-and-parse that would
    /// otherwise run one file at a time inside <see cref="ICompilerHost.GetSourceFile"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this lives on the host and not in the program: producing a <c>SourceFile</c> from a file name is
    /// <c>getSourceFile</c>'s contract, and only the host knows how it is fulfilled (a host may answer from memory, or
    /// not parse at all), so the program cannot parse files behind its host's back. What the program <em>can</em> do is
    /// say which files it is about to ask for and with which options — which is this hook, and it is optional: a host
    /// that does not implement it keeps the reference's behaviour exactly, one file at a time.
    /// </para>
    /// <para>
    /// A host that does implement it is promising two things: its reads and lookups are safe to call from several
    /// threads, and answering <c>getSourceFile</c> from what it prefetched produces the same file it would have
    /// produced without the prefetch. The order in which the program <em>asks</em> is unchanged, so the file graph, the
    /// file list, the diagnostic order and the emitted output are the same either way; only the wall clock moves.
    /// </para>
    /// </remarks>
    internal interface IParallelSourcePrefetchHost
    {
        /// <summary>Parses the given files ahead of the walk, at most <paramref name="degree"/> at a time.</summary>
        void PrefetchSourceFiles(IReadOnlyList<SourceFileRequest> requests, int degree);
    }

    internal sealed class CompilerHost : ICompilerHost, IParallelSourcePrefetchHost
    {
        private readonly ISystem _system;
        private readonly Core.GetCanonicalFileName _getCanonicalFileName;
        private readonly Func<string> _getCurrentDirectory;
        private readonly string _newLine;
        private readonly Func<string?, object?, Action<string>?, bool?, SourceFile?> _getSourceFile;
        private readonly bool? _setParentNodes;

        /// <summary>The files <see cref="PrefetchSourceFiles"/> already produced, keyed the way the program asks.</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (SourceFile File, CreateSourceFileOptions Options)> _prefetched =
            new(StringComparer.Ordinal);

        /// <summary>The process-wide library-file cache this host serves from, when a caller attached one (see
        /// <see cref="LibSourceFileRegistry"/>) — null in every ordinary run, so the reference's behaviour is what
        /// happens by default.</summary>
        private readonly LibSourceFileRegistry? _libSourceFileRegistry;

        /// <summary>This host's key into the library cache: every compiler option, read once at construction.</summary>
        private readonly string? _libSourceFileSignature;

        /// <summary>ported from: src/compiler/program.ts:451 — <c>existingDirectories</c>.</summary>
        /// <remarks>PORT-NOTE: TS's <c>Map&lt;string, boolean&gt;</c> is only ever used as a set, so the port keeps a set.</remarks>
        private readonly HashSet<string> _existingDirectories = new(StringComparer.Ordinal);

        /// <summary>ported from: src/compiler/program.ts:449-499 — the literal's construction.</summary>
        public CompilerHost(
            ICompilerOptions options,
            bool? setParentNodes,
            ISystem system,
            LibSourceFileRegistry? libSourceFileRegistry = null)
        {
            _system = system;
            _setParentNodes = setParentNodes;
            _libSourceFileRegistry = libSourceFileRegistry;
            // The library cache's key for this host: every option, read once (see LibSourceFileRegistry.SignatureFrom).
            _libSourceFileSignature = libSourceFileRegistry is null ? null : LibSourceFileRegistry.SignatureFrom(options);
            _getCanonicalFileName = Core.CreateGetCanonicalFileName(system.UseCaseSensitiveFileNames);
            _getCurrentDirectory = Core.Memoize(() => _system.GetCurrentDirectory());
            _newLine = Utilities.GetNewLineCharacter(options);
            _getSourceFile = Program.CreateGetSourceFile(fileName => ReadFile(fileName), setParentNodes);
            WriteFile = Program.CreateWriteFileMeasuringIO(
                (path, data, writeByteOrderMark) => system.WriteFile(path, data, writeByteOrderMark),
                path => system.CreateDirectory(path),
                path => DirectoryExists(path));

            // PORT-NOTE: the reference stores a function (`() => system.useCaseSensitiveFileNames`); the generated
            // IModuleResolutionHost slot is `object?` and `Utilities.HostUsesCaseSensitiveFileNames` reads both shapes,
            // so the port keeps TS's function form.
            Func<bool> useCaseSensitiveFileNames = () => _system.UseCaseSensitiveFileNames;
            UseCaseSensitiveFileNames = useCaseSensitiveFileNames;
        }

        // ---- The members the reference's literal defines (program.ts:452-498). ----

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getSourceFile</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: the prefetch arm is the only deviation, and it is deliberately narrow. A prefetched file is
        /// reused only when the options the walk computed are the ones the prefetch parsed with: the implied node
        /// format and the package.json locations a file's parse depends on are read through the module-resolution
        /// caches, which the walk fills as it goes, so a file whose answer moves between the two points is parsed
        /// again here rather than served from the prefetch. (That is not hypothetical: computing every root's options
        /// up front and trusting them diverged on 123 of the conformance corpus's 5,908 files before this comparison
        /// — the emitted JavaScript of files whose `package.json` resolution had moved.)
        /// </remarks>
        public SourceFile? GetSourceFile(string? fileName, object? languageVersionOrOptions, Action<string>? onError, bool? shouldCreateNewSourceFile)
        {
            if (fileName is not null && languageVersionOrOptions is CreateSourceFileOptions options
                && _prefetched.TryRemove(fileName, out var entry) && SameParseOptions(entry.Options, options))
            {
                // What the prefetch produced is what the call below would have produced: the same text and the same
                // options. A read that failed while prefetching is not cached, so the normal path still reports it,
                // and so is a file whose options moved between the two requests (see SameParseOptions).
                return entry.File;
            }

            // The process-wide library cache (see LibSourceFileRegistry): a library file this process already parsed
            // and bound under the same options is handed back as it is, and every other file — including a library
            // file the cache has not seen — goes through the ordinary path below. A miss costs one dictionary lookup.
            if (fileName is not null && _libSourceFileRegistry is not null && _libSourceFileSignature is not null
                && _libSourceFileRegistry.TryGet(fileName, _libSourceFileSignature, out var cachedLibFile))
            {
                return cachedLibFile;
            }

            var file = _getSourceFile(fileName, languageVersionOrOptions, onError, shouldCreateNewSourceFile);
            if (file is not null && fileName is not null && _libSourceFileRegistry is not null && _libSourceFileSignature is not null)
            {
                _libSourceFileRegistry.Add(fileName, _libSourceFileSignature, file);
            }

            return file;
        }

        /// <summary>Whether two per-file parse requests would produce the same <see cref="SourceFile"/>.</summary>
        /// <remarks>
        /// Every field of <see cref="CreateSourceFileOptions"/> is compared. <c>PackageJsonScope</c> and
        /// <c>ImpliedNodeFormat</c> are compared by <see cref="object.Equals(object?, object?)"/> and the lists by
        /// content; anything the comparison cannot prove equal counts as different, because the cost of being wrong
        /// here is a wrong parse and the cost of re-parsing is one file.
        /// </remarks>
        private static bool SameParseOptions(CreateSourceFileOptions a, CreateSourceFileOptions b)
            => a.LanguageVersion == b.LanguageVersion
                && a.JsDocParsingMode == b.JsDocParsingMode
                && Equals(a.ImpliedNodeFormat, b.ImpliedNodeFormat)
                && Equals(a.PackageJsonScope, b.PackageJsonScope)
                // PORT-NOTE: the indicator is *not* compared by identity: `getSetExternalModuleIndicator` builds a new
                // callback on every call (the reference carries the same `TODO: Should this callback be cached?`,
                // utilities.ts:8981), so identity is never equal and comparing it rejected every prefetched file.
                // Within one program the callback is a pure function of that program's options, which both requests
                // read, so presence is the whole of what can differ between them.
                && (a.SetExternalModuleIndicator is null) == (b.SetExternalModuleIndicator is null)
                && SameStrings(a.PackageJsonLocations, b.PackageJsonLocations);

        private static bool SameStrings(List<string>? a, List<string>? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null || a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The optional prefetch hook — see <see cref="IParallelSourcePrefetchHost"/>.</summary>
        public void PrefetchSourceFiles(IReadOnlyList<SourceFileRequest> requests, int degree)
        {
            if (degree <= 1 || requests.Count <= 1)
            {
                return;
            }

            var setParentNodes = _setParentNodes ?? false;
            Parallelism.For(requests.Count, i =>
            {
                var request = requests[i];
                try
                {
                    var text = ReadFile(request.FileName);
                    if (text is null)
                    {
                        return; // not cached: the walk's own read reports the failure
                    }

                    _prefetched[request.FileName] = (
                        Parser.CreateSourceFile(request.FileName, text, request.Options, setParentNodes),
                        request.Options);
                }
                catch (Exception)
                {
                    // Any failure here is the walk's to report; leaving the entry out is the whole recovery.
                }
            });
        }

        /// <summary>ported from: src/compiler/program.ts:453 — <c>getDefaultLibLocation</c>.</summary>
        public string GetDefaultLibLocation()
            => Paths.GetDirectoryPath(Paths.NormalizePath(_system.GetExecutingFilePath()));

        /// <summary>ported from: src/compiler/program.ts:454 — <c>getDefaultLibFileName</c>.</summary>
        public string GetDefaultLibFileName(ICompilerOptions? options)
            => Paths.CombinePaths(GetDefaultLibLocation(), Utilities.GetDefaultLibFileName(options));

        /// <summary>ported from: src/compiler/program.ts:455-459 — <c>writeFile</c>.</summary>
        public Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? WriteFile { get; set; }

        /// <summary>ported from: src/compiler/program.ts:460 — <c>getCurrentDirectory</c>.</summary>
        public string GetCurrentDirectory() => _getCurrentDirectory();

        /// <summary>ported from: src/compiler/program.ts:461 — <c>useCaseSensitiveFileNames</c>.</summary>
        public object? UseCaseSensitiveFileNames { get; set; }

        /// <summary>ported from: src/compiler/program.ts:462 — <c>getCanonicalFileName</c>.</summary>
        public string GetCanonicalFileName(string? fileName) => _getCanonicalFileName(fileName ?? "");

        /// <summary>ported from: src/compiler/program.ts:463 — <c>getNewLine</c>.</summary>
        public string GetNewLine() => _newLine;

        /// <summary>ported from: src/compiler/program.ts:464 — <c>fileExists</c>.</summary>
        public bool FileExists(string? fileName) => _system.FileExists(fileName ?? "");

        /// <summary>ported from: src/compiler/program.ts:465 — <c>readFile</c>.</summary>
        public string? ReadFile(string? fileName) => _system.ReadFile(fileName ?? "");

        /// <summary>ported from: src/compiler/program.ts:466 — <c>trace</c>.</summary>
        public void Trace(string? s) => _system.Write((s ?? "") + _newLine);

        /// <summary>ported from: src/compiler/program.ts:467 — <c>directoryExists</c>.</summary>
        public bool DirectoryExists(string? directoryName) => DirectoryExistsWorker(directoryName ?? "");

        /// <summary>ported from: src/compiler/program.ts:468 — <c>getEnvironmentVariable</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS's <c>system.getEnvironmentVariable ? … : ""</c> arm cannot arise — <see cref="ISystem"/>'s
        /// member is required — and the <c>null</c> it answers is TS's <c>undefined</c>.
        /// </remarks>
        public string? GetEnvironmentVariable(string? name) => _system.GetEnvironmentVariable(name ?? "");

        /// <summary>ported from: src/compiler/program.ts:469 — <c>getDirectories</c>.</summary>
        public List<string> GetDirectories(string? path) => [.. _system.GetDirectories(path ?? "")];

        /// <summary>ported from: src/compiler/program.ts:470 — <c>realpath</c>.</summary>
        public string Realpath(string? path) => _system.Realpath(path ?? "");

        /// <summary>ported from: src/compiler/program.ts:471 — <c>readDirectory</c>.</summary>
        public List<string> ReadDirectory(string? rootDir, List<string>? extensions, List<string>? excludes, List<string>? includes, int? depth)
            => [.. _system.ReadDirectory(rootDir ?? "", extensions, excludes, includes, depth)];

        /// <summary>ported from: src/compiler/program.ts:472 — <c>createDirectory</c>.</summary>
        public void CreateDirectory(string? directory) => _system.CreateDirectory(directory ?? "");

        /// <summary>ported from: src/compiler/program.ts:473 — <c>createHash</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS's <c>maybeBind(system, system.createHash)</c> answers <c>undefined</c> on a system without a
        /// hash; on the CLR the host always has one (§13.2), so a null here is a host defect and fails loudly.
        /// </remarks>
        public string CreateHash(string? data)
            => _system.CreateHash(data ?? "")
                ?? Debug.Fail<string>("the host reports no hash function; TS's `sys.createHash` always exists on the CLR");

        /// <summary>ported from: src/compiler/program.ts:451-452 — the <c>directoryExists</c> closure.</summary>
        private bool DirectoryExistsWorker(string directoryPath)
        {
            if (_existingDirectories.Contains(directoryPath))
            {
                return true;
            }

            if (_system.DirectoryExists(directoryPath))
            {
                _existingDirectories.Add(directoryPath);
                return true;
            }

            return false;
        }

        /// <summary>The message the reference's "member is absent" case produces: TS calls <c>undefined</c>.</summary>
        private static string NotProvidedMessage(string member)
            => $"`{member}` is an optional member of TS's `CompilerHost` that this host does not provide; test the "
                + $"matching `…Func` slot on `ICompilerHost` (the reference's `host.{member} ? … : …`) instead of "
                + "calling this member.";

        // ---- The optional members of the reference's CompilerHost, which this literal does not define. ----

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>getSourceFileByPath</c>.</summary>
        public Func<string?, string?, object?, Action<string>?, bool?, SourceFile?>? GetSourceFileByPathFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>getCancellationToken</c>.</summary>
        public Func<ICancellationToken>? GetCancellationTokenFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>resolveModuleNames</c>.</summary>
        public Func<List<string>?, string?, List<string>?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<IResolvedModule>>? ResolveModuleNamesFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>resolveModuleNameLiterals</c>.</summary>
        public Func<List<LiteralExpression>?, string?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<LiteralExpression>?, List<IResolvedModuleWithFailedLookupLocations>>? ResolveModuleNameLiteralsFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>resolveTypeReferenceDirectives</c>.</summary>
        public Func<object?, string?, IResolvedProjectReference?, ICompilerOptions?, object?, List<IResolvedTypeReferenceDirective>>? ResolveTypeReferenceDirectivesFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>resolveTypeReferenceDirectiveReferences</c>
        /// over <see cref="IFileReference"/> entries.</summary>
        public Func<List<IFileReference>?, string?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<IFileReference>?, List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>? ResolveTypeReferenceDirectiveReferencesFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>resolveTypeReferenceDirectiveReferences</c>
        /// over <c>string</c> entries (TS's generic member instantiated twice; the port names the second
        /// instantiation).</summary>
        public Func<List<string>?, string?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<string>?, List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>? ResolveTypeReferenceDirectiveReferencesStringsFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>resolveLibrary</c>.</summary>
        public Func<string?, string?, ICompilerOptions?, string?, IResolvedModuleWithFailedLookupLocations>? ResolveLibraryFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>hasInvalidatedLibResolutions</c>.</summary>
        public Func<string?, bool>? HasInvalidatedLibResolutionsFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>onReleaseOldSourceFile</c>.</summary>
        public Action<SourceFile?, ICompilerOptions?, bool, SourceFile?>? OnReleaseOldSourceFileFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>onReleaseParsedCommandLine</c>.</summary>
        public Action<string?, IResolvedProjectReference?, ICompilerOptions?>? OnReleaseParsedCommandLineFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>hasInvalidatedResolutions</c>.</summary>
        public Func<string?, bool>? HasInvalidatedResolutionsFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>getParsedCommandLine</c>.</summary>
        public Func<string?, IParsedCommandLine?>? GetParsedCommandLineFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>useSourceOfProjectReferenceRedirect</c>.</summary>
        public Func<bool>? UseSourceOfProjectReferenceRedirectFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>getSymlinkCache</c>.</summary>
        public Func<object?>? GetSymlinkCacheFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>getModuleResolutionCache</c>.</summary>
        public Func<object?>? GetModuleResolutionCacheFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>getBuildInfo</c>.</summary>
        public Func<string?, string?, IBuildInfo?>? GetBuildInfoFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — the optional <c>getGlobalTypingsCacheLocation</c>.</summary>
        public Func<string?, string?>? GetGlobalTypingsCacheLocationFunc { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — <c>hasChangedAutomaticTypeDirectiveNames</c>.</summary>
        public Func<bool>? HasChangedAutomaticTypeDirectiveNames { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — <c>storeSignatureInfo</c>.</summary>
        public bool? StoreSignatureInfo { get; set; }

        /// <summary>ported from: src/compiler/program.ts:452 — <c>jsDocParsingMode</c>.</summary>
        public JSDocParsingMode? JsDocParsingMode { get; set; }

        // ---- The generated methods behind those slots (program.ts:452). ----

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getSourceFileByPath</c>.</summary>
        public SourceFile? GetSourceFileByPath(string? fileName, string? path, object? languageVersionOrOptions, Action<string>? onError, bool? shouldCreateNewSourceFile)
            => GetSourceFileByPathFunc is { } callback
                ? callback(fileName, path, languageVersionOrOptions, onError, shouldCreateNewSourceFile)
                : Debug.Fail<SourceFile?>(NotProvidedMessage("getSourceFileByPath"));

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getCancellationToken</c>.</summary>
        public ICancellationToken GetCancellationToken()
            => GetCancellationTokenFunc is { } callback
                ? callback()
                : Debug.Fail<ICancellationToken>(NotProvidedMessage("getCancellationToken"));

        /// <summary>ported from: src/compiler/program.ts:452 — <c>resolveModuleNames</c>.</summary>
        public List<IResolvedModule> ResolveModuleNames(List<string>? moduleNames, string? containingFile, List<string>? reusedNames, IResolvedProjectReference? redirectedReference, ICompilerOptions? options, SourceFile? containingSourceFile)
            => ResolveModuleNamesFunc is { } callback
                ? callback(moduleNames, containingFile, reusedNames, redirectedReference, options, containingSourceFile)
                : Debug.Fail<List<IResolvedModule>>(NotProvidedMessage("resolveModuleNames"));

        /// <summary>ported from: src/compiler/program.ts:452 — <c>resolveTypeReferenceDirectives</c>.</summary>
        public List<IResolvedTypeReferenceDirective> ResolveTypeReferenceDirectives(object? typeReferenceDirectiveNames, string? containingFile, IResolvedProjectReference? redirectedReference, ICompilerOptions? options, object? containingFileMode)
            => ResolveTypeReferenceDirectivesFunc is { } callback
                ? callback(typeReferenceDirectiveNames, containingFile, redirectedReference, options, containingFileMode)
                : Debug.Fail<List<IResolvedTypeReferenceDirective>>(NotProvidedMessage("resolveTypeReferenceDirectives"));

        /// <summary>ported from: src/compiler/program.ts:452 — <c>resolveModuleNameLiterals</c>.</summary>
        public List<IResolvedModuleWithFailedLookupLocations> ResolveModuleNameLiterals(List<LiteralExpression>? moduleLiterals, string? containingFile, IResolvedProjectReference? redirectedReference, ICompilerOptions? options, SourceFile? containingSourceFile, List<LiteralExpression>? reusedNames)
            => ResolveModuleNameLiteralsFunc is { } callback
                ? callback(moduleLiterals, containingFile, redirectedReference, options, containingSourceFile, reusedNames)
                : Debug.Fail<List<IResolvedModuleWithFailedLookupLocations>>(NotProvidedMessage("resolveModuleNameLiterals"));

        /// <summary>ported from: src/compiler/program.ts:452 — <c>resolveTypeReferenceDirectiveReferences</c>.</summary>
        /// <remarks>
        /// PORT-NOTE: TS's member is generic in the entry type (<c>T extends FileReference | string</c>) and the port
        /// keeps one slot per instantiation; this member dispatches on the runtime list type, which is total because
        /// the reference's constraint admits only those two.
        /// </remarks>
        public List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations> ResolveTypeReferenceDirectiveReferences<T>(List<T>? typeDirectiveReferences, string? containingFile, IResolvedProjectReference? redirectedReference, ICompilerOptions? options, SourceFile? containingSourceFile, List<T>? reusedNames)
        {
            if (typeDirectiveReferences is List<IFileReference> fileReferences)
            {
                return ResolveTypeReferenceDirectiveReferencesFunc is { } callback
                    ? callback(fileReferences, containingFile, redirectedReference, options, containingSourceFile, reusedNames as List<IFileReference>)
                    : Debug.Fail<List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>(NotProvidedMessage("resolveTypeReferenceDirectiveReferences"));
            }

            if (typeDirectiveReferences is List<string> names)
            {
                return ResolveTypeReferenceDirectiveReferencesStringsFunc is { } callback
                    ? callback(names, containingFile, redirectedReference, options, containingSourceFile, reusedNames as List<string>)
                    : Debug.Fail<List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>(NotProvidedMessage("resolveTypeReferenceDirectiveReferences"));
            }

            return Debug.Fail<List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>("`typeDirectiveReferences` must hold `FileReference`s or strings");
        }

        /// <summary>ported from: src/compiler/program.ts:452 — <c>resolveLibrary</c>.</summary>
        public IResolvedModuleWithFailedLookupLocations ResolveLibrary(string? libraryName, string? resolveFrom, ICompilerOptions? options, string? libFileName)
            => ResolveLibraryFunc is { } callback
                ? callback(libraryName, resolveFrom, options, libFileName)
                : Debug.Fail<IResolvedModuleWithFailedLookupLocations>(NotProvidedMessage("resolveLibrary"));

        /// <summary>ported from: src/compiler/program.ts:452 — <c>hasInvalidatedLibResolutions</c>.</summary>
        public bool HasInvalidatedLibResolutions(string? libFileName)
            => HasInvalidatedLibResolutionsFunc is { } callback ? callback(libFileName) : Core.ReturnFalse();

        /// <summary>ported from: src/compiler/program.ts:452 — <c>onReleaseOldSourceFile</c>.</summary>
        public void OnReleaseOldSourceFile(SourceFile? oldSourceFile, ICompilerOptions? oldOptions, bool hasSourceFileByPath, SourceFile? newSourceFileByResolvedPath)
            => OnReleaseOldSourceFileFunc?.Invoke(oldSourceFile, oldOptions, hasSourceFileByPath, newSourceFileByResolvedPath);

        /// <summary>ported from: src/compiler/program.ts:452 — <c>onReleaseParsedCommandLine</c>.</summary>
        public void OnReleaseParsedCommandLine(string? configFileName, IResolvedProjectReference? oldResolvedRef, ICompilerOptions? optionOptions)
            => OnReleaseParsedCommandLineFunc?.Invoke(configFileName, oldResolvedRef, optionOptions);

        /// <summary>ported from: src/compiler/program.ts:452 — <c>hasInvalidatedResolutions</c>.</summary>
        public bool HasInvalidatedResolutions(string? filePath)
            => HasInvalidatedResolutionsFunc is { } callback ? callback(filePath) : Core.ReturnFalse();

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getParsedCommandLine</c>.</summary>
        public IParsedCommandLine? GetParsedCommandLine(string? fileName)
            => GetParsedCommandLineFunc is { } callback ? callback(fileName) : null;

        /// <summary>ported from: src/compiler/program.ts:452 — <c>useSourceOfProjectReferenceRedirect</c>.</summary>
        public bool UseSourceOfProjectReferenceRedirect()
            => UseSourceOfProjectReferenceRedirectFunc is { } callback && callback();

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getSymlinkCache</c>.</summary>
        public object? GetSymlinkCache()
            => GetSymlinkCacheFunc is { } callback ? callback() : null;

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getModuleResolutionCache</c>.</summary>
        public object? GetModuleResolutionCache()
            => GetModuleResolutionCacheFunc is { } callback ? callback() : null;

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getBuildInfo</c>.</summary>
        public IBuildInfo? GetBuildInfo(string? fileName, string? configFilePath)
            => GetBuildInfoFunc is { } callback ? callback(fileName, configFilePath) : null;

        /// <summary>ported from: src/compiler/program.ts:452 — <c>getGlobalTypingsCacheLocation</c>.</summary>
        public string? GetGlobalTypingsCacheLocation()
            => GetGlobalTypingsCacheLocationFunc is { } callback ? callback(null) : null;
    }

    // ---------------------------------------------------------------------------------------------
    // 5. getPreEmitDiagnostics (program.ts:634-650)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:636-650 — <c>getPreEmitDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's overload set also takes a <c>BuilderProgram</c> (M8); the port declares the
    /// <see cref="IProgram"/> form, which is the one <c>program.ts</c> itself defines.
    /// <para>
    /// PORT-NOTE: the reference concatenates <c>Diagnostic[]</c>s; the generated program members answer
    /// <c>List&lt;IDiagnostic&gt;</c>/<c>List&lt;IDiagnosticWithLocation&gt;</c>, so the port narrows them with
    /// <see cref="AsDiagnostics"/> — every diagnostic the port builds <em>is</em> a <see cref="Diagnostic"/>
    /// (<c>Core/Diagnostic.cs</c>), the only implementation of the interface.
    /// </para>
    /// </remarks>
    internal static List<Diagnostic> GetPreEmitDiagnostics(IProgram program, SourceFile? sourceFile = null, ICancellationToken? cancellationToken = null)
    {
        IList<Diagnostic>? diagnostics = null;
        diagnostics = Core.AddRange(diagnostics, AsDiagnostics(program.GetConfigFileParsingDiagnostics()));
        diagnostics = Core.AddRange(diagnostics, AsDiagnostics(program.GetOptionsDiagnostics(cancellationToken)));
        diagnostics = Core.AddRange(diagnostics, AsDiagnostics(program.GetSyntacticDiagnostics(sourceFile, cancellationToken)));
        diagnostics = Core.AddRange(diagnostics, AsDiagnostics(program.GetGlobalDiagnostics(cancellationToken)));
        diagnostics = Core.AddRange(diagnostics, AsDiagnostics(program.GetSemanticDiagnostics(sourceFile, cancellationToken)));

        if (Utilities.GetEmitDeclarations(GetCompilerOptionsOf(program)))
        {
            diagnostics = Core.AddRange(diagnostics, AsDiagnostics(program.GetDeclarationDiagnostics(sourceFile, cancellationToken)));
        }

        return Utilities.SortAndDeduplicateDiagnostics(diagnostics ?? []);
    }

    /// <summary>The compiler options of an <see cref="IProgram"/>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>IProgram.GetCompilerOptions()</c> is ambiguous in the port: the generated interface inherits it
    /// from both <c>ISourceFileMayBeEmittedHost</c> and <c>IScriptReferenceHost</c> (TS inherits it once, through
    /// <c>TypeCheckerHost</c>), so a call on an <see cref="IProgram"/>-typed value must name the declaring interface.
    /// </remarks>
    private static ICompilerOptions GetCompilerOptionsOf(IProgram program)
        => ((ISourceFileMayBeEmittedHost)program).GetCompilerOptions();

    /// <summary>The concrete form of a program member's diagnostic list, used by <see cref="GetPreEmitDiagnostics"/>.</summary>
    private static List<Diagnostic>? AsDiagnostics(IEnumerable<IDiagnostic>? diagnostics)
        => diagnostics is null ? null : diagnostics.Cast<Diagnostic>().ToList();

    // ---------------------------------------------------------------------------------------------
    // 6. The diagnostic formatters (program.ts:652-852)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:658-666 — <c>formatDiagnostics</c>.</summary>
    internal static string FormatDiagnostics(IList<Diagnostic> diagnostics, IFormatDiagnosticsHost host)
    {
        var output = "";

        foreach (var diagnostic in diagnostics)
        {
            output += FormatDiagnostic(diagnostic, host);
        }

        return output;
    }

    /// <summary>ported from: src/compiler/program.ts:668-678 — <c>formatDiagnostic</c>.</summary>
    internal static string FormatDiagnostic(Diagnostic diagnostic, IFormatDiagnosticsHost host)
    {
        var errorMessage = $"{Utilities.DiagnosticCategoryName(diagnostic)} TS{diagnostic.Code}: {FlattenDiagnosticMessageText(diagnostic.MessageText, host.GetNewLine())}{host.GetNewLine()}";

        if (diagnostic.File is not null)
        {
            var lineAndCharacter = GetLineAndCharacterOfPosition(diagnostic.File, Debug.CheckDefined(diagnostic.Start));
            var (line, character) = (lineAndCharacter.Line, lineAndCharacter.Character);
            var fileName = diagnostic.File.FileName ?? "";
            var relativeFileName = Paths.ConvertToRelativePath(fileName, host.GetCurrentDirectory(), fileName => host.GetCanonicalFileName(fileName));
            return $"{relativeFileName}({line + 1},{character + 1}): " + errorMessage;
        }

        return errorMessage;
    }

    /// <summary>The stand-in for <c>undefined</c> as a key of <c>changeCompilerHostLikeToUseCache</c>'s source-file
    /// cache (a JS <c>Map</c> accepts it, a C# dictionary does not).</summary>
    private static readonly object NoImpliedNodeFormatKey = new();

    /// <summary>ported from: src/compiler/program.ts:687 — <c>gutterStyleSequence</c>.</summary>
    private const string GutterStyleSequence = "\u001b[7m";

    /// <summary>ported from: src/compiler/program.ts:688 — <c>gutterSeparator</c>.</summary>
    private const string GutterSeparator = " ";

    /// <summary>ported from: src/compiler/program.ts:689 — <c>resetEscapeSequence</c>.</summary>
    private const string ResetEscapeSequence = "\u001b[0m";

    /// <summary>ported from: src/compiler/program.ts:690 — <c>ellipsis</c>.</summary>
    private const string Ellipsis = "...";

    /// <summary>ported from: src/compiler/program.ts:691 — <c>halfIndent</c>.</summary>
    private const string HalfIndent = "  ";

    /// <summary>ported from: src/compiler/program.ts:692 — <c>indent</c>.</summary>
    private const string Indent = "    ";

    /// <summary>ported from: src/compiler/program.ts:693-703 — <c>getCategoryFormat</c>.</summary>
    private static ForegroundColorEscapeSequences GetCategoryFormat(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.Error => ForegroundColorEscapeSequences.Red,
        DiagnosticCategory.Warning => ForegroundColorEscapeSequences.Yellow,
        DiagnosticCategory.Suggestion => Debug.Fail<ForegroundColorEscapeSequences>("Should never get an Info diagnostic on the command line."),
        DiagnosticCategory.Message => ForegroundColorEscapeSequences.Blue,
        _ => Debug.Fail<ForegroundColorEscapeSequences>($"unknown DiagnosticCategory {(int)category}"),
    };

    /// <summary>ported from: src/compiler/program.ts:711-713 — <c>formatColorAndReset</c>.</summary>
    internal static string FormatColorAndReset(string text, string formatStyle)
        => formatStyle + text + ResetEscapeSequence;

    /// <summary>ported from: src/compiler/program.ts:715-767 — <c>formatCodeSpan</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's two masking steps are regexes over the line (<c>/\S/g → " "</c> for the part before the
    /// squiggle, <c>/./g → "~"</c> for the squiggled part). The second matches any character except a line terminator
    /// and <c>lineContent</c> is one line, so the port writes a run of tildes of the same length; the first preserves
    /// whitespace and masks the rest, which the port does per code unit with <see cref="char.IsWhiteSpace(char)"/>.
    /// </remarks>
    private static string FormatCodeSpan(
        SourceFile file,
        int start,
        int length,
        string indent,
        ForegroundColorEscapeSequences squiggleColor,
        IFormatDiagnosticsHost host)
    {
        var first = GetLineAndCharacterOfPosition(file, start);
        var (firstLine, firstLineChar) = (first.Line, first.Character);
        var last = GetLineAndCharacterOfPosition(file, start + length);
        var (lastLine, lastLineChar) = (last.Line, last.Character);
        var lastLineInFile = GetLineAndCharacterOfPosition(file, (file.Text ?? "").Length).Line;

        var hasMoreThanFiveLines = (lastLine - firstLine) >= 4;
        var gutterWidth = (lastLine + 1 + "").Length;
        if (hasMoreThanFiveLines)
        {
            gutterWidth = Math.Max(Ellipsis.Length, gutterWidth);
        }

        var context = "";
        for (var i = firstLine; i <= lastLine; i++)
        {
            context += host.GetNewLine();

            // If the error spans over 5 lines, we'll only show the first 2 and last 2 lines,
            // so we'll skip ahead to the second-to-last line.
            if (hasMoreThanFiveLines && firstLine + 1 < i && i < lastLine - 1)
            {
                context += indent + FormatColorAndReset(Ellipsis.PadLeft(gutterWidth), GutterStyleSequence) + GutterSeparator + host.GetNewLine();
                i = lastLine - 1;
            }

            var lineStart = GetPositionOfLineAndCharacter(file, i, 0);
            var lineEnd = i < lastLineInFile ? GetPositionOfLineAndCharacter(file, i + 1, 0) : (file.Text ?? "").Length;
            var lineContent = (file.Text ?? "")[lineStart..lineEnd];
            lineContent = lineContent.TrimEnd(); // trim from end
            lineContent = lineContent.Replace('\t', ' '); // convert tabs to single spaces

            // Output the gutter and the actual contents of the line.
            context += indent + FormatColorAndReset((i + 1 + "").PadLeft(gutterWidth), GutterStyleSequence) + GutterSeparator;
            context += lineContent + host.GetNewLine();

            // Output the gutter and the error span for the line using tildes.
            context += indent + FormatColorAndReset("".PadLeft(gutterWidth), GutterStyleSequence) + GutterSeparator;
            context += squiggleColor.Value;
            if (i == firstLine)
            {
                // If we're on the last line, then limit it to the last character of the last line.
                // Otherwise, we'll just squiggle the rest of the line, giving 'slice' no end position.
                int? lastCharForLine = i == lastLine ? lastLineChar : null;

                context += MaskNonWhitespace(Slice(lineContent, 0, firstLineChar));
                context += Squiggle(Slice(lineContent, firstLineChar, lastCharForLine));
            }
            else if (i == lastLine)
            {
                context += Squiggle(Slice(lineContent, 0, lastLineChar));
            }
            else
            {
                // Squiggle the entire line.
                context += Squiggle(lineContent);
            }

            context += ResetEscapeSequence;
        }

        return context;
    }

    /// <summary>TS: <c>str.replace(/\S/g, " ")</c> — mask everything that is not whitespace, keeping the width.</summary>
    private static string MaskNonWhitespace(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsWhiteSpace(source[i]) ? source[i] : ' ';
            }
        });
    }

    /// <summary>TS: <c>str.replace(/./g, "~")</c> — every code unit of a single line becomes a tilde.</summary>
    private static string Squiggle(string text) => text.Length == 0 ? text : new string('~', text.Length);

    /// <summary>TS: <c>str.slice(start, end)</c> — clamp the bounds, never throw (the port's slices index a
    /// <c>string</c>, which throws where JS clamps).</summary>
    private static string Slice(string text, int start, int? end = null)
    {
        var length = text.Length;
        var from = start < 0 ? Math.Max(length + start, 0) : Math.Min(start, length);
        var to = end is null ? length : end.Value < 0 ? Math.Max(length + end.Value, 0) : Math.Min(end.Value, length);
        return to <= from ? "" : text[from..to];
    }

    /// <summary>ported from: src/compiler/program.ts:770-783 — <c>formatLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's optional <c>color</c> parameter defaults to <c>formatColorAndReset</c>; a C# default parameter
    /// cannot name a method group whose type is a delegate, so it is nullable and defaulted to null here.
    /// </remarks>
    internal static string FormatLocation(
        SourceFile file,
        int start,
        IFormatDiagnosticsHost host,
        Func<string, string, string>? color = null)
    {
        color ??= FormatColorAndReset;
        var location = GetLineAndCharacterOfPosition(file, start);
        var (firstLine, firstLineChar) = (location.Line, location.Character);
        var relativeFileName = host is not null
            ? Paths.ConvertToRelativePath(file.FileName ?? "", host.GetCurrentDirectory(), fileName => host.GetCanonicalFileName(fileName))
            : file.FileName ?? "";

        var output = "";
        output += color(relativeFileName, ForegroundColorEscapeSequences.Cyan.Value);
        output += ":";
        output += color($"{firstLine + 1}", ForegroundColorEscapeSequences.Yellow.Value);
        output += ":";
        output += color($"{firstLineChar + 1}", ForegroundColorEscapeSequences.Yellow.Value);
        return output;
    }

    /// <summary>ported from: src/compiler/program.ts:785-812 — <c>formatDiagnosticsWithColorAndContext</c>.</summary>
    internal static string FormatDiagnosticsWithColorAndContext(IList<Diagnostic> diagnostics, IFormatDiagnosticsHost host)
    {
        var output = "";
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.File is not null)
            {
                output += FormatLocation(diagnostic.File, Debug.CheckDefined(diagnostic.Start), host);
                output += " - ";
            }

            output += FormatColorAndReset(Utilities.DiagnosticCategoryName(diagnostic), GetCategoryFormat(diagnostic.Category).Value);
            output += FormatColorAndReset($" TS{diagnostic.Code}: ", ForegroundColorEscapeSequences.Grey.Value);
            output += FlattenDiagnosticMessageText(diagnostic.MessageText, host.GetNewLine());

            if (diagnostic.File is not null && diagnostic.Code != Diagnostics.File_appears_to_be_binary.Code)
            {
                output += host.GetNewLine();
                output += FormatCodeSpan(
                    diagnostic.File,
                    Debug.CheckDefined(diagnostic.Start),
                    Debug.CheckDefined(diagnostic.Length),
                    "",
                    GetCategoryFormat(diagnostic.Category),
                    host);
            }

            if (diagnostic.RelatedInformation is not null)
            {
                output += host.GetNewLine();
                foreach (var related in diagnostic.RelatedInformation)
                {
                    if (related.File is not null)
                    {
                        output += host.GetNewLine();
                        output += HalfIndent + FormatLocation(related.File, Debug.CheckDefined(related.Start), host);
                        output += FormatCodeSpan(
                            related.File,
                            Debug.CheckDefined(related.Start),
                            Debug.CheckDefined(related.Length),
                            Indent,
                            ForegroundColorEscapeSequences.Cyan,
                            host);
                    }

                    output += host.GetNewLine();
                    output += Indent + FlattenDiagnosticMessageText(related.MessageText, host.GetNewLine());
                }
            }

            output += host.GetNewLine();
        }

        return output;
    }

    /// <summary>ported from: src/compiler/program.ts:814-844 — <c>flattenDiagnosticMessageText</c>.</summary>
    internal static string FlattenDiagnosticMessageText(object? diag, string newLine, int indent = 0)
    {
        if (diag is string text)
        {
            return text;
        }

        if (diag is null)
        {
            return "";
        }

        if (diag is not IDiagnosticMessageChain chain)
        {
            return Debug.Fail<string>($"a diagnostic message is a string or a message chain, not {diag.GetType().Name}");
        }

        var result = "";
        if (indent != 0)
        {
            result += newLine;

            for (var i = 0; i < indent; i++)
            {
                result += "  ";
            }
        }

        result += chain.MessageText;
        indent++;
        if (chain.Next is not null)
        {
            foreach (var kid in chain.Next)
            {
                result += FlattenDiagnosticMessageText(kid, newLine, indent);
            }
        }

        return result;
    }

    /// <summary>ported from: src/compiler/scanner.ts:547-550 — <c>getLineAndCharacterOfPosition</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: not in the task's member list. <c>ScannerUtilities.GetLineAndCharacterOfPosition</c> takes
    /// <c>ISourceFileLike</c>, which no type implements yet, so the port keeps the <c>SourceFile</c> narrowing here —
    /// the same two helpers (and the same reason) as <c>Utilities.Binder.Diagnostics.cs</c>.
    /// </remarks>
    private static LineAndCharacter GetLineAndCharacterOfPosition(SourceFile sourceFile, int position)
        => ScannerUtilities.ComputeLineAndCharacterOfPosition(GetLineStarts(sourceFile), position);

    /// <summary>ported from: src/compiler/scanner.ts:475-500 — <c>getPositionOfLineAndCharacter</c>.</summary>
    /// <remarks>PORT-NOTE: the same <c>SourceFile</c> narrowing as <see cref="GetLineAndCharacterOfPosition"/>.</remarks>
    private static int GetPositionOfLineAndCharacter(SourceFile sourceFile, int line, int character)
        => ScannerUtilities.ComputePositionOfLineAndCharacter(GetLineStarts(sourceFile), line, character, sourceFile.Text);
    /// <summary>ported from: src/compiler/scanner.ts:160-172 — <c>getLineStarts</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>ScannerUtilities.GetLineStarts</c> takes an <c>ISourceFileLike</c>, which no ported type
    /// implements, so this <c>SourceFile</c> narrowing (and the memo on <see cref="SourceFile.LineMap"/>) lives here —
    /// the same helper, and the same reason, as <c>Utilities.Binder.Diagnostics.cs</c>.
    /// </remarks>
    private static IList<int> GetLineStarts(SourceFile sourceFile)
        => sourceFile.LineMap ?? (sourceFile.LineMap = ScannerUtilities.ComputeLineStarts(sourceFile.Text ?? ""));

    // ---------------------------------------------------------------------------------------------
    // 7. The resolution-mode readers (program.ts:854-1020)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:857-859 — <c>getModeForFileReference</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>FileReference | string</c> and the reference discriminates it with
    /// <c>isString</c>; the port takes <c>object?</c> and tests for <c>string</c>, which is the same discrimination.
    /// TS's <c>||</c> is JS truthiness of a number, so a <c>ModuleKind.None</c> (0) falls back to the containing
    /// file's mode — hence the explicit comparison rather than <c>??</c>.
    /// </remarks>
    internal static ModuleKind? GetModeForFileReference(object? reference, ModuleKind? containingFileMode)
    {
        var mode = reference is string ? containingFileMode : (ModuleKind?)((IFileReference)reference!).ResolutionMode;
        return mode is { } value && value != ModuleKind.None ? mode : containingFileMode;
    }

    /// <summary>ported from: src/compiler/program.ts:872-877 — <c>getModeForResolutionAtIndex</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS has a second overload over <c>SourceFileImportsList</c>; the port declares the
    /// <see cref="SourceFile"/> form, which is what the program's own members pass (the generated
    /// <c>ISourceFileImportsList</c> has no implementation).
    /// </remarks>
    internal static ModuleKind? GetModeForResolutionAtIndex(SourceFile file, int index, ICompilerOptions? compilerOptions)
        // we ensure all elements of file.imports and file.moduleAugmentations have the relevant parent pointers set
        // during program setup, so it's safe to use them even pre-bind
        => GetModeForUsageLocationWorker(file, GetModuleNameStringLiteralAt(file, index), compilerOptions);

    /// <summary>ported from: src/compiler/program.ts:880-889 — <c>isExclusivelyTypeOnlyImportOrExport</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is the union <c>ImportDeclaration | ExportDeclaration | JSDocImportTag</c>; the port
    /// takes a <see cref="Node"/> and tests the three shapes, which is the same discrimination — and, as in the
    /// reference, a node that is none of them answers <c>false</c> (TS reads <c>decl.importClause?.isTypeOnly</c>,
    /// which is <c>undefined</c> there).
    /// </remarks>
    internal static bool IsExclusivelyTypeOnlyImportOrExport(Node decl)
    {
        if (decl is ExportDeclaration exportDeclaration)
        {
            return exportDeclaration.IsTypeOnly;
        }

        if (decl is ImportDeclaration importDeclaration)
        {
            return importDeclaration.ImportClause?.IsTypeOnly == true;
        }

        if (decl is JSDocImportTag jsDocImportTag)
        {
            return jsDocImportTag.ImportClause?.IsTypeOnly == true;
        }

        return false;
    }

    /// <summary>ported from: src/compiler/program.ts:909-911 — <c>getModeForUsageLocation</c>.</summary>
    internal static ModuleKind? GetModeForUsageLocation(SourceFile? file, LiteralExpression? usage, ICompilerOptions? options)
        => GetModeForUsageLocationWorker(file, usage, options);

    /// <summary>ported from: src/compiler/program.ts:913-955 — <c>getModeForUsageLocationWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's file parameter is the structural
    /// <c>Pick&lt;SourceFile, "fileName" | "impliedNodeFormat" | "packageJsonScope"&gt;</c>; the port takes the
    /// <see cref="SourceFile"/> those slots would come from.
    /// </remarks>
    internal static ModuleKind? GetModeForUsageLocationWorker(SourceFile? file, LiteralExpression? usage, ICompilerOptions? options)
    {
        if (usage?.Parent is { } parent
            && (NodeTests.IsImportDeclaration(parent) || NodeTests.IsExportDeclaration(parent) || NodeTests.IsJSDocImportTag(parent)))
        {
            var isTypeOnly = IsExclusivelyTypeOnlyImportOrExport(parent);
            if (isTypeOnly)
            {
                var importAttributes = parent switch
                {
                    ImportDeclaration importDeclaration => importDeclaration.Attributes,
                    ExportDeclaration exportDeclaration => exportDeclaration.Attributes,
                    JSDocImportTag jsDocImportTag => jsDocImportTag.Attributes,
                    _ => null,
                };
                var over = GetResolutionModeOverride(importAttributes);
                if (over is not null)
                {
                    return over;
                }
            }
        }

        if (usage?.Parent?.Parent is { } grandParent && NodeTests.IsImportTypeNode(grandParent))
        {
            var over = GetResolutionModeOverride(((ImportTypeNode)grandParent).Attributes);
            if (over is not null)
            {
                return over;
            }
        }

        if (options is not null && Utilities.ImportSyntaxAffectsModuleResolution(options))
        {
            return GetEmitSyntaxForUsageLocationWorker(file, usage, options);
        }

        return null;
    }

    /// <summary>ported from: src/compiler/program.ts:957-989 — <c>getEmitSyntaxForUsageLocationWorker</c>.</summary>
    /// <remarks>PORT-NOTE: the same <c>Pick&lt;SourceFile, …&gt;</c> narrowing as
    /// <see cref="GetModeForUsageLocationWorker"/>.</remarks>
    internal static ModuleKind? GetEmitSyntaxForUsageLocationWorker(SourceFile? file, LiteralExpression? usage, ICompilerOptions? options)
    {
        if (options is null)
        {
            // This should always be provided, but we try to fail somewhat
            // gracefully to allow projects like ts-node time to update.
            return null;
        }

        var exprParentParent = usage?.Parent is { } usageParentForWalk
            ? Utilities.WalkUpParenthesizedExpressions(usageParentForWalk).Parent
            : null;
        if ((exprParentParent is not null && NodeTests.IsImportEqualsDeclaration(exprParentParent))
            || (usage?.Parent is { } usageParent && Utilities.IsRequireCall(usageParent, /*requireStringLiteralLikeArgument*/ false)))
        {
            return ModuleKind.CommonJS;
        }

        if (usage?.Parent is { } usageParentForImportCall
            && Utilities.IsImportCall(Utilities.WalkUpParenthesizedExpressions(usageParentForImportCall)))
        {
            return ShouldTransformImportCallWorker(file, options) ? ModuleKind.CommonJS : ModuleKind.ESNext;
        }

        // If we're in --module preserve on an input file, we know that an import
        // is an import. But if this is a declaration file, we'd prefer to use the
        // impliedNodeFormat. Since we want things to be consistent between the two,
        // we need to issue errors when the user writes ESM syntax in a definitely-CJS
        // file, until/unless declaration emit can indicate a true ESM import. On the
        // other hand, writing CJS syntax in a definitely-ESM file is fine, since declaration
        // emit preserves the CJS syntax.
        var fileEmitMode = GetEmitModuleFormatOfFileWorker(file, options);
        return fileEmitMode == ModuleKind.CommonJS ? ModuleKind.CommonJS :
            Utilities.EmitModuleKindIsNonNodeEsm(fileEmitMode) || fileEmitMode == ModuleKind.Preserve ? ModuleKind.ESNext :
            null;
    }

    /// <summary>ported from: src/compiler/program.ts:992-1014 — <c>getResolutionModeOverride</c>.</summary>
    internal static ModuleKind? GetResolutionModeOverride(
        ImportAttributes? node,
        Action<Node, DiagnosticMessage>? grammarErrorOnNode = null)
    {
        if (node is null)
        {
            return null;
        }

        if (Core.Length(node.Elements) != 1)
        {
            grammarErrorOnNode?.Invoke(
                node,
                node.Token == SyntaxKind.WithKeyword
                    ? Diagnostics.Type_import_attributes_should_have_exactly_one_key_resolution_mode_with_value_import_or_require
                    : Diagnostics.Type_import_assertions_should_have_exactly_one_key_resolution_mode_with_value_import_or_require);
            return null;
        }

        var elem = node.Elements![0];
        if (elem.Name is not { } elemName || !Utilities.IsStringLiteralLike(elemName))
        {
            return null;
        }

        if (((LiteralExpression)elemName).Text != "resolution-mode")
        {
            grammarErrorOnNode?.Invoke(
                elemName,
                node.Token == SyntaxKind.WithKeyword
                    ? Diagnostics.resolution_mode_is_the_only_valid_key_for_type_import_attributes
                    : Diagnostics.resolution_mode_is_the_only_valid_key_for_type_import_assertions);
            return null;
        }

        if (elem.Value is not { } elemValue || !Utilities.IsStringLiteralLike(elemValue))
        {
            return null;
        }

        var valueText = ((LiteralExpression)elemValue).Text;
        if (valueText != "import" && valueText != "require")
        {
            grammarErrorOnNode?.Invoke(elemValue, Diagnostics.resolution_mode_should_be_either_require_or_import);
            return null;
        }

        return valueText == "import" ? ModuleKind.ESNext : ModuleKind.CommonJS;
    }

    /// <summary>ported from: src/compiler/program.ts:1015-1020 — <c>emptyResolution</c>.</summary>
    internal static readonly EmptyResolution EmptyResolution = new();

    // ---------------------------------------------------------------------------------------------
    // 8. The resolution loaders (program.ts:1036-1137)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1038-1040 — <c>getModuleResolutionName</c>.</summary>
    internal static string GetModuleResolutionName(LiteralExpression literal) => literal.Text ?? "";

    /// <summary>ported from: src/compiler/program.ts:1043-1046 — <c>moduleResolutionNameAndModeGetter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's literal is an instance of the generated <see cref="IResolutionNameAndModeGetter{TEntry,TFile}"/>
    /// (types.ts), whose <c>getMode</c> slot the generator types <c>object?</c> because <c>ResolutionMode</c> has no
    /// generated type; the getter below boxes the <see cref="ModuleKind"/>.
    /// </remarks>
    internal static readonly IResolutionNameAndModeGetter<LiteralExpression, SourceFile> ModuleResolutionNameAndModeGetter = new ModuleResolutionNameAndModeGetterWorker();

    /// <summary>ported from: src/compiler/program.ts:1071-1073 — <c>getTypeReferenceResolutionName</c>.</summary>
    internal static string GetTypeReferenceResolutionName<TEntry>(TEntry entry)
        => entry is IFileReference fileReference
            ? fileReference.FileName ?? ""
            : entry as string ?? Debug.Fail<string>($"a type-reference entry is a `FileReference` or a string, not {entry?.GetType().Name}");

    /// <summary>ported from: src/compiler/program.ts:1075-1079 — <c>typeReferenceResolutionNameAndModeGetter</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's const closes over one <c>T</c> at a time because the getter object it builds is generic only in
    /// the type of its call; the port declares the worker generic and hands out its per-instantiation singleton.
    /// </remarks>
    internal static IResolutionNameAndModeGetter<TEntry, SourceFile?> TypeReferenceResolutionNameAndModeGetter<TEntry>()
        => TypeReferenceResolutionNameAndModeGetterWorker<TEntry>.Instance;

    /// <summary>ported from: src/compiler/program.ts:1082-1096 — <c>createModuleResolutionLoader</c>.</summary>
    internal static IResolutionLoader<LiteralExpression, IResolvedModuleWithFailedLookupLocations, SourceFile> CreateModuleResolutionLoader(
        string containingFile,
        IResolvedProjectReference? redirectedReference,
        ICompilerOptions options,
        IModuleResolutionHost host,
        ModuleResolutionCache? cache)
        => new ModuleResolutionLoader(containingFile, redirectedReference, options, host, cache);

    /// <summary>ported from: src/compiler/program.ts:1099-1120 — <c>createTypeReferenceResolutionLoader</c>.</summary>
    internal static IResolutionLoader<TEntry, IResolvedTypeReferenceDirectiveWithFailedLookupLocations, SourceFile?> CreateTypeReferenceResolutionLoader<TEntry>(
        string containingFile,
        IResolvedProjectReference? redirectedReference,
        ICompilerOptions options,
        IModuleResolutionHost host,
        TypeReferenceDirectiveResolutionCache? cache)
        => new TypeReferenceResolutionLoader<TEntry>(containingFile, redirectedReference, options, host, cache);

    /// <summary>ported from: src/compiler/program.ts:1123-1137 — <c>loadWithModeAwareCache</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the generated <see cref="IResolutionLoader{TEntry,TResolution,TFile}"/> spells <c>nameAndMode</c> as
    /// an <c>object?</c> slot and <c>resolve</c>'s mode as <c>object?</c> (again because <c>ResolutionMode</c> has no
    /// generated type), so the port narrows the slot to the generated getter interface and boxes the mode — one cast
    /// per loader instead of an object-typed slot at every use.
    /// </remarks>
    internal static List<TResolution> LoadWithModeAwareCache<TEntry, TFile, TResolutionCache, TResolution>(
        IList<TEntry> entries,
        string containingFile,
        IResolvedProjectReference? redirectedReference,
        ICompilerOptions options,
        TFile containingSourceFile,
        IModuleResolutionHost host,
        TResolutionCache? resolutionCache,
        Func<string, IResolvedProjectReference?, ICompilerOptions, IModuleResolutionHost, TResolutionCache?, IResolutionLoader<TEntry, TResolution, TFile>> createLoader)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var resolutions = new List<TResolution>();
        var cache = new Dictionary<string, TResolution>(StringComparer.Ordinal);
        var loader = createLoader(containingFile, redirectedReference, options, host, resolutionCache);
        var nameAndMode = (IResolutionNameAndModeGetter<TEntry, TFile>)loader.NameAndMode!;
        foreach (var entry in entries)
        {
            var name = nameAndMode.GetName(entry);
            var mode = (ModuleKind?)nameAndMode.GetMode(entry, containingSourceFile, redirectedReference?.CommandLine?.Options ?? options);
            var key = ModuleNameResolver.CreateModeAwareCacheKey(name, mode);
            if (!cache.TryGetValue(key, out var result))
            {
                result = loader.Resolve(name, mode);
                cache[key] = result;
            }

            resolutions.Add(result!);
        }

        return resolutions;
    }

    /// <summary>ported from: src/compiler/program.ts:1043-1046 — the getter <c>moduleResolutionNameAndModeGetter</c> holds.</summary>
    private sealed class ModuleResolutionNameAndModeGetterWorker : IResolutionNameAndModeGetter<LiteralExpression, SourceFile>
    {
        public string GetName(LiteralExpression? entry) => GetModuleResolutionName(Debug.CheckDefined(entry));

        public object? GetMode(LiteralExpression? entry, SourceFile? file, ICompilerOptions? compilerOptions)
            => GetModeForUsageLocation(file, entry, compilerOptions);
    }

    /// <summary>ported from: src/compiler/program.ts:1075-1079 — the getter
    /// <c>typeReferenceResolutionNameAndModeGetter</c> holds.</summary>
    private sealed class TypeReferenceResolutionNameAndModeGetterWorker<TEntry> : IResolutionNameAndModeGetter<TEntry, SourceFile?>
    {
        public static readonly TypeReferenceResolutionNameAndModeGetterWorker<TEntry> Instance = new();

        public string GetName(TEntry? entry)
            => entry is null
                ? Debug.Fail<string>("a type-reference entry is a `FileReference` or a string, not null")
                : GetTypeReferenceResolutionName(entry);

        public object? GetMode(TEntry? entry, SourceFile? file, ICompilerOptions? compilerOptions)
            => GetModeForFileReference(entry, file is not null ? GetDefaultResolutionModeForFileWorker(file, compilerOptions) : null);
    }

    /// <summary>ported from: src/compiler/program.ts:1082-1096 — the loader
    /// <c>createModuleResolutionLoader</c> builds.</summary>
    private sealed class ModuleResolutionLoader : IResolutionLoader<LiteralExpression, IResolvedModuleWithFailedLookupLocations, SourceFile>
    {
        private readonly string _containingFile;
        private readonly IResolvedProjectReference? _redirectedReference;
        private readonly ICompilerOptions _options;
        private readonly IModuleResolutionHost _host;
        private readonly ModuleResolutionCache? _cache;

        public ModuleResolutionLoader(
            string containingFile,
            IResolvedProjectReference? redirectedReference,
            ICompilerOptions options,
            IModuleResolutionHost host,
            ModuleResolutionCache? cache)
        {
            _containingFile = containingFile;
            _redirectedReference = redirectedReference;
            _options = options;
            _host = host;
            _cache = cache;
        }

        public object? NameAndMode { get; set; } = ModuleResolutionNameAndModeGetter;

        public IResolvedModuleWithFailedLookupLocations Resolve(string? name, object? mode)
            => ModuleNameResolver.ResolveModuleName(name ?? "", _containingFile, _options, _host, _cache, _redirectedReference, (ModuleKind?)mode);
    }

    /// <summary>ported from: src/compiler/program.ts:1099-1120 — the loader
    /// <c>createTypeReferenceResolutionLoader</c> builds.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference passes the entry straight to <c>resolveTypeReferenceDirective</c>, whose own parameter
    /// is the <c>FileReference | string</c> union; the port's <c>ResolveTypeReferenceDirective</c> takes the file name
    /// (<c>ModuleNameResolver.cs</c>), so the loader resolves the entry's name here — the same value the reference's
    /// <c>isString(typeReferenceDirectiveName) ? … : typeReferenceDirectiveName.fileName</c> would compute.
    /// </remarks>
    private sealed class TypeReferenceResolutionLoader<TEntry> : IResolutionLoader<TEntry, IResolvedTypeReferenceDirectiveWithFailedLookupLocations, SourceFile?>
    {
        private readonly string _containingFile;
        private readonly IResolvedProjectReference? _redirectedReference;
        private readonly ICompilerOptions _options;
        private readonly IModuleResolutionHost _host;
        private readonly TypeReferenceDirectiveResolutionCache? _cache;

        public TypeReferenceResolutionLoader(
            string containingFile,
            IResolvedProjectReference? redirectedReference,
            ICompilerOptions options,
            IModuleResolutionHost host,
            TypeReferenceDirectiveResolutionCache? cache)
        {
            _containingFile = containingFile;
            _redirectedReference = redirectedReference;
            _options = options;
            _host = host;
            _cache = cache;
        }

        public object? NameAndMode { get; set; } = TypeReferenceResolutionNameAndModeGetter<TEntry>();

        public IResolvedTypeReferenceDirectiveWithFailedLookupLocations Resolve(string? name, object? mode)
            => ModuleNameResolver.ResolveTypeReferenceDirective(name ?? "", _containingFile, _options, _host, _redirectedReference, _cache, (ModuleKind?)mode);
    }

    // ---------------------------------------------------------------------------------------------
    // 9. Inferred library names (program.ts:1138-1161)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE: `inferredTypesContainingFile` (program.ts:1138) is declared where the module-resolution slice put it,
    // as `Utilities.InferredTypesContainingFile` (Utilities.ModuleResolution.cs); it is not redeclared here.

    /// <summary>ported from: src/compiler/program.ts:1141-1146 — <c>getInferredLibraryNameResolveFrom</c>.</summary>
    internal static string GetInferredLibraryNameResolveFrom(ICompilerOptions options, string currentDirectory, string libFileName)
    {
        var containingDirectory = Js.IsTruthy(options.ConfigFilePath) ? Paths.GetDirectoryPath(options.ConfigFilePath) : currentDirectory;
        return Paths.CombinePaths(containingDirectory, $"__lib_node_modules_lookup_{libFileName}__.ts");
    }

    /// <summary>ported from: src/compiler/program.ts:1147-1161 — <c>getLibraryNameFromLibFileName</c>.</summary>
    internal static string GetLibraryNameFromLibFileName(string libFileName)
    {
        // Support resolving to lib.dom.d.ts -> @typescript/lib-dom, and
        //                      lib.dom.iterable.d.ts -> @typescript/lib-dom/iterable
        //                      lib.es2015.symbol.wellknown.d.ts -> @typescript/lib-es2015/symbol-wellknown
        var components = libFileName.Split(".");
        var path = components[1];
        var i = 2;
        while (i < components.Length && Js.IsTruthy(components[i]) && components[i] != "d")
        {
            path += (i == 2 ? "/" : "-") + components[i];
            i++;
        }

        return "@typescript/lib-" + path;
    }

    // ---------------------------------------------------------------------------------------------
    // 10. The referenced-file locations (program.ts:1162-1227)
    // ---------------------------------------------------------------------------------------------

    // PORT-NOTE: `isReferencedFile` (program.ts:1162-1169) is declared by the programDiagnostics slice as
    // `Utilities.IsReferencedFile` (Program.ProgramDiagnostics.cs); it is not redeclared here.

    /// <summary>ported from: src/compiler/program.ts:1211-1213 — <c>isReferenceFileLocation</c>.</summary>
    /// <remarks>PORT-NOTE: TS's predicate is <c>pos !== undefined</c> over the two location shapes; the port's
    /// generated <see cref="IReferenceFileLocation"/> is the arm that carries the range, so the test narrows through
    /// that interface — the same treatment <c>Program.ProgramDiagnostics.cs</c> gives it.</remarks>
    internal static bool IsReferenceFileLocation(IReferencedFileLocation location)
        => location is IReferenceFileLocation;

    /// <summary>ported from: src/compiler/program.ts:1216-1227 — <c>getReferencedFileLocation</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference's result is the union
    /// <c>ReferenceFileLocation | SyntheticReferenceFileLocation</c>; §5 rule 3 needs one type for it, and the port
    /// uses the marker <see cref="IReferencedFileLocation"/> both classes implement — the same construction
    /// <c>IFileIncludeReason</c> got for the reasons' union.
    /// </remarks>
    internal static IReferencedFileLocation GetReferencedFileLocation(IProgram program, IReferencedFile @ref)
    {
        var file = Debug.CheckDefined(program.GetSourceFileByPath(@ref.File));
        var kind = (FileIncludeKind)@ref.Kind!;
        var index = @ref.Index;
        var pos = 0;
        var end = 0;
        IPackageId? packageId = null;
        switch (kind)
        {
            case FileIncludeKind.Import:
            {
                var importLiteral = GetModuleNameStringLiteralAt(file, index);
                packageId = program.GetResolvedModuleFromModuleSpecifier(importLiteral, file)?.ResolvedModule?.PackageId;
                if (importLiteral.Pos == -1)
                {
                    return new SyntheticReferenceFileLocation { File = file, PackageId = packageId, Text = importLiteral.Text ?? "" };
                }

                pos = ScannerUtilities.SkipTrivia(file.Text ?? "", importLiteral.Pos);
                end = importLiteral.End;
                break;
            }

            case FileIncludeKind.ReferenceFile:
            {
                var reference = file.ReferencedFiles![index];
                pos = reference.Pos;
                end = reference.End;
                break;
            }

            case FileIncludeKind.TypeReferenceDirective:
            {
                var typeReference = file.TypeReferenceDirectives![index];
                pos = typeReference.Pos;
                end = typeReference.End;
                packageId = program.GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(typeReference, file)
                    ?.ResolvedTypeReferenceDirective?.PackageId;
                break;
            }

            case FileIncludeKind.LibReferenceDirective:
            {
                var libReference = file.LibReferenceDirectives![index];
                pos = libReference.Pos;
                end = libReference.End;
                break;
            }

            default:
                return Debug.Fail<IReferencedFileLocation>($"`getReferencedFileLocation` has no location for FileIncludeKind {(int)kind}");
        }

        return new ReferenceFileLocation { File = file, Pos = pos, End = end, PackageId = packageId };
    }

    // ---------------------------------------------------------------------------------------------
    // 11. Program reuse and implied formats (program.ts:1228-1498, 4843-4880)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:1234-1322 — <c>isProgramUptoDate</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>HasInvalidatedResolutions</c>/<c>HasInvalidatedLibResolutions</c>/
    /// <c>HasChangedAutomaticTypeDirectiveNames</c> are named delegate types of this file; the port declares the
    /// <c>Func</c> shapes the generated host interface carries, so the caller can pass its host's slots directly.
    /// </remarks>
    internal static bool IsProgramUptoDate(
        Program? program,
        IList<string> rootFileNames,
        ICompilerOptions newOptions,
        Func<string, string, string?> getSourceVersion,
        Func<string, bool> fileExists,
        Func<string, bool> hasInvalidatedResolutions,
        Func<string, bool> hasInvalidatedLibResolutions,
        Func<bool>? hasChangedAutomaticTypeDirectiveNames,
        Func<string, IParsedCommandLine?> getParsedCommandLine,
        IList<IProjectReference>? projectReferences)
    {
        // If we haven't created a program yet or have changed automatic type directives, then it is not up-to-date
        if (program is null || (hasChangedAutomaticTypeDirectiveNames is not null && hasChangedAutomaticTypeDirectiveNames()))
        {
            return false;
        }

        // If root file names don't match
        if (!Core.ArrayIsEqualTo(program.GetRootFileNames(), rootFileNames))
        {
            return false;
        }

        List<IResolvedProjectReference?>? seenResolvedRefs = null;

        // If project references don't match
        if (!Core.ArrayIsEqualTo(program.GetProjectReferences(), projectReferences, ProjectReferenceUptoDate))
        {
            return false;
        }

        // If any file is not up-to-date, then the whole program is not up-to-date
        if (program.GetSourceFiles().Any(SourceFileNotUptoDate))
        {
            return false;
        }

        // If any of the missing file paths are now created
        var missingPaths = program.GetMissingFilePaths();
        if (missingPaths is not null && Utilities.ForEachEntry(missingPaths, (fileName, _) => fileExists(fileName)) is true)
        {
            return false;
        }

        var currentOptions = program.GetCompilerOptions();

        // If the compilation settings do no match, then the program is not up-to-date
        if (!Utilities.CompareDataObjects(currentOptions, newOptions))
        {
            return false;
        }

        // If library resolution is invalidated, then the program is not up-to-date
        if (program._resolvedLibReferences is not null
            && Utilities.ForEachEntry(program._resolvedLibReferences, (_, libFileName) => hasInvalidatedLibResolutions(libFileName)) is true)
        {
            return false;
        }

        // If everything matches but the text of config file is changed,
        // error locations can change for program options, so update the program
        if (currentOptions.ConfigFile is SourceFile currentConfigFile && newOptions.ConfigFile is SourceFile newConfigFile)
        {
            return currentConfigFile.Text == newConfigFile.Text;
        }

        return true;

        bool SourceFileNotUptoDate(SourceFile sourceFile)
            => !SourceFileVersionUptoDate(sourceFile) || hasInvalidatedResolutions(sourceFile.Path ?? "");

        bool SourceFileVersionUptoDate(SourceFile sourceFile)
            => sourceFile.Version == getSourceVersion(sourceFile.ResolvedPath ?? "", sourceFile.FileName ?? "");

        bool ProjectReferenceUptoDate(IProjectReference oldRef, IProjectReference newRef, int index)
            => Utilities.ProjectReferenceIsEqualTo(oldRef, newRef)
                && ResolvedProjectReferenceUptoDate(program.GetResolvedProjectReferences()![index], oldRef);

        bool ResolvedProjectReferenceUptoDate(IResolvedProjectReference? oldResolvedRef, IProjectReference oldRef)
        {
            if (oldResolvedRef is not null)
            {
                // Assume true
                if (Core.Contains(seenResolvedRefs, oldResolvedRef))
                {
                    return true;
                }

                var refPath = ResolveProjectReferencePath(oldRef);
                var newParsedCommandLine = getParsedCommandLine(refPath);

                // Check if config file exists
                if (newParsedCommandLine is null)
                {
                    return false;
                }

                // If change in source file
                if (!ReferenceEquals(oldResolvedRef.CommandLine?.Options?.ConfigFile, newParsedCommandLine.Options?.ConfigFile))
                {
                    return false;
                }

                // check file names
                if (!Core.ArrayIsEqualTo(oldResolvedRef.CommandLine?.FileNames, newParsedCommandLine.FileNames))
                {
                    return false;
                }

                // Add to seen before checking the referenced paths of this config file
                (seenResolvedRefs ??= []).Add(oldResolvedRef);

                // If child project references are upto date, this project reference is uptodate
                // PORT-NOTE: TS's `!forEach(refs, (child, index) => …)` stops at the first failing child; the port's
                // `Core.Some` predicate takes only the element, so the walk keeps the index itself.
                for (var index = 0; index < (oldResolvedRef.References?.Count ?? 0); index++)
                {
                    if (!ResolvedProjectReferenceUptoDate(
                        oldResolvedRef.References![index],
                        oldResolvedRef.CommandLine!.ProjectReferences![index]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // In old program, not able to resolve project reference path,
            // so if config file doesnt exist, it is uptodate.
            var unresolvedRefPath = ResolveProjectReferencePath(oldRef);
            return getParsedCommandLine(unresolvedRefPath) is null;
        }
    }

    /// <summary>ported from: src/compiler/program.ts:1323-1338 — <c>getConfigFileParsingDiagnostics</c>.</summary>
    internal static List<IDiagnostic> GetConfigFileParsingDiagnostics(IParsedCommandLine configFileParseResult)
        => configFileParseResult.Options?.ConfigFile is SourceFile configFile
            ? [.. configFile.ParseDiagnostics ?? [], .. configFileParseResult.Errors ?? []]
            : configFileParseResult.Errors ?? [];

    /// <summary>ported from: src/compiler/program.ts:1468-1476 — <c>shouldProgramCreateNewSourceFiles</c>.</summary>
    internal static bool ShouldProgramCreateNewSourceFiles(Program? program, ICompilerOptions newOptions)
    {
        if (program is null)
        {
            return false;
        }

        // If any compiler options change, we can't reuse old source file even if version match
        // The change in options like these could result in change in syntax tree or `sourceFile.bindDiagnostics`.
        return Utilities.OptionsHaveChanges(
            program.GetCompilerOptions(),
            newOptions,
            Utilities.SourceFileAffectingCompilerOptions,
            Utilities.GetCompilerOptionValueByName);
    }

    /// <summary>ported from: src/compiler/program.ts:1339-1343 — <c>getImpliedNodeFormatForFile</c>.</summary>
    internal static ModuleKind? GetImpliedNodeFormatForFile(
        string fileName,
        PackageJsonInfoCache? packageJsonInfoCache,
        IModuleResolutionHost host,
        ICompilerOptions options)
    {
        var result = GetImpliedNodeFormatForFileWorker(fileName, packageJsonInfoCache, host, options);
        return result is ImpliedNodeFormatAndPackageJsonScope nodeFormat
            ? nodeFormat.ImpliedNodeFormat
            : (ModuleKind?)result;
    }

    /// <summary>ported from: src/compiler/program.ts:1345-1368 — <c>getImpliedNodeFormatForFileWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's answer is <c>ResolutionMode | Partial&lt;CreateSourceFileOptions&gt;</c>, which the port spells
    /// <c>object?</c> (a boxed <see cref="ModuleKind"/> or the class <see cref="ImpliedNodeFormatAndPackageJsonScope"/>),
    /// because <c>ResolutionMode</c> has no generated type — <c>Program.State.cs</c> records the same treatment for the
    /// signatures that admit it. The second arm's fields are named as <c>CreateSourceFileOptions</c> names them
    /// (<c>impliedNodeFormat</c>, <c>packageJsonLocations</c>, <c>packageJsonScope</c>), so the class is what
    /// <c>GetCreateSourceFileOptions</c> pattern-matches.
    /// </remarks>
    internal static object? GetImpliedNodeFormatForFileWorker(
        string fileName,
        PackageJsonInfoCache? packageJsonInfoCache,
        IModuleResolutionHost host,
        ICompilerOptions options)
    {
        var moduleResolution = Utilities.GetEmitModuleResolutionKind(options);
        var shouldLookupFromPackageJson = ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext
            || ModuleNameResolver.PathContainsNodeModules(fileName);
        return Paths.FileExtensionIsOneOf(fileName, [Extension.Dmts.Value, Extension.Mts.Value, Extension.Mjs.Value]) ? ModuleKind.ESNext :
            Paths.FileExtensionIsOneOf(fileName, [Extension.Dcts.Value, Extension.Cts.Value, Extension.Cjs.Value]) ? ModuleKind.CommonJS :
            shouldLookupFromPackageJson && Paths.FileExtensionIsOneOf(fileName, [Extension.Dts.Value, Extension.Ts.Value, Extension.Tsx.Value, Extension.Js.Value, Extension.Jsx.Value]) ? LookupFromPackageJson() :
            null; // other extensions, like `json` or `tsbuildinfo`, are set as `undefined` here but they should never be fed through the transformer pipeline

        object LookupFromPackageJson()
        {
            var state = ModuleNameResolver.GetTemporaryModuleResolutionState(packageJsonInfoCache, host, options);
            var packageJsonLocations = new List<string>();
            state.FailedLookupLocations = packageJsonLocations;
            state.AffectingLocations = packageJsonLocations;
            var packageJsonScope = ModuleNameResolver.GetPackageScopeForPath(Paths.GetDirectoryPath(fileName), state);
            var impliedNodeFormat = packageJsonScope?.Contents.PackageJsonContent.Get("type") as string == "module"
                ? ModuleKind.ESNext
                : ModuleKind.CommonJS;
            return new ImpliedNodeFormatAndPackageJsonScope
            {
                ImpliedNodeFormat = impliedNodeFormat,
                PackageJsonLocations = packageJsonLocations,
                PackageJsonScope = packageJsonScope,
            };
        }
    }

    /// <summary>ported from: src/compiler/program.ts:1478-1487 — <c>createCreateProgramOptions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: §5 rule 6 turns the reference's object literal into <see cref="CreateProgramOptions"/>, and the
    /// function answers the generated <see cref="ICreateProgramOptions"/> — the type
    /// <c>Program.CreateProgram(ICreateProgramOptions)</c> takes.
    /// </remarks>
    internal static ICreateProgramOptions CreateCreateProgramOptions(
        List<string> rootNames,
        ICompilerOptions options,
        ICompilerHost? host = null,
        Program? oldProgram = null,
        List<IDiagnostic>? configFileParsingDiagnostics = null,
        string? typeScriptVersion = null)
        => new CreateProgramOptions
        {
            RootNames = rootNames,
            Options = options,
            Host = host,
            OldProgram = oldProgram,
            ConfigFileParsingDiagnostics = configFileParsingDiagnostics,
            TypeScriptVersion = typeScriptVersion,
        };

    /// <summary>ported from: src/compiler/program.ts:4843-4850 — <c>shouldTransformImportCallWorker</c>.</summary>
    internal static bool ShouldTransformImportCallWorker(SourceFile? sourceFile, ICompilerOptions? options)
    {
        var moduleKind = Utilities.GetEmitModuleKind(options);
        if (ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext || moduleKind == ModuleKind.Preserve)
        {
            return false;
        }

        return GetEmitModuleFormatOfFileWorker(sourceFile, options) < ModuleKind.ES2015;
    }

    /// <summary>ported from: src/compiler/program.ts:4852-4855 — <c>getEmitModuleFormatOfFileWorker</c>.</summary>
    internal static ModuleKind GetEmitModuleFormatOfFileWorker(SourceFile? sourceFile, ICompilerOptions? options)
        => GetImpliedNodeFormatForEmitWorker(sourceFile, options) ?? Utilities.GetEmitModuleKind(options);

    /// <summary>ported from: src/compiler/program.ts:4857-4876 — <c>getImpliedNodeFormatForEmitWorker</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's <c>Pick&lt;SourceFile, "fileName" | "impliedNodeFormat" | "packageJsonScope"&gt;</c> is the
    /// port's <see cref="SourceFile"/>; the two object-typed slots need the casts below.
    /// </remarks>
    internal static ModuleKind? GetImpliedNodeFormatForEmitWorker(SourceFile? sourceFile, ICompilerOptions? options)
    {
        var moduleKind = Utilities.GetEmitModuleKind(options);
        if (ModuleKind.Node16 <= moduleKind && moduleKind <= ModuleKind.NodeNext)
        {
            return (ModuleKind?)sourceFile?.ImpliedNodeFormat;
        }

        var packageJsonType = ((PackageJsonInfo?)sourceFile?.PackageJsonScope)?.Contents.PackageJsonContent.Get("type") as string;
        var impliedNodeFormat = (ModuleKind?)sourceFile?.ImpliedNodeFormat;
        var fileName = sourceFile?.FileName ?? "";
        if (
            impliedNodeFormat == ModuleKind.CommonJS
            && (packageJsonType == "commonjs"
                || Paths.FileExtensionIsOneOf(fileName, [Extension.Cjs.Value, Extension.Cts.Value]))
        )
        {
            return ModuleKind.CommonJS;
        }

        if (
            impliedNodeFormat == ModuleKind.ESNext
            && (packageJsonType == "module"
                || Paths.FileExtensionIsOneOf(fileName, [Extension.Mjs.Value, Extension.Mts.Value]))
        )
        {
            return ModuleKind.ESNext;
        }

        return null;
    }

    /// <summary>ported from: src/compiler/program.ts:4878-4880 — <c>getDefaultResolutionModeForFileWorker</c>.</summary>
    internal static ModuleKind? GetDefaultResolutionModeForFileWorker(SourceFile? sourceFile, ICompilerOptions? options)
        => Utilities.ImportSyntaxAffectsModuleResolution(options)
            ? GetImpliedNodeFormatForEmitWorker(sourceFile, options)
            : null;

    // ---------------------------------------------------------------------------------------------
    // 12. The no-emit path and the config host (program.ts:5056-5201)
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/program.ts:5056-5058 — <c>emitSkippedWithNoDiagnostics</c>.</summary>
    internal static readonly EmitResult EmitSkippedWithNoDiagnostics = new()
    {
        Diagnostics = [],
        SourceMaps = null,
        EmittedFiles = null,
        EmitSkipped = true,
    };

    /// <summary>ported from: src/compiler/program.ts:5060-5097 — <c>handleNoEmitOptions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's generic <c>Program | T</c> (with <c>T extends BuilderProgram</c>, M8) folds to the port's
    /// <see cref="IProgram"/>, whose <c>emitBuildInfo</c> is <c>Program/Program.Emit.cs</c>'s.
    /// </remarks>
    internal static IEmitResult? HandleNoEmitOptions(
        IProgram program,
        SourceFile? sourceFile,
        Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? writeFile,
        ICancellationToken? cancellationToken)
    {
        var options = GetCompilerOptionsOf(program);
        if (Js.IsTruthy(options.NoEmit))
        {
            return sourceFile is not null
                ? EmitSkippedWithNoDiagnostics
                : program.EmitBuildInfo(writeFile, cancellationToken);
        }

        // If the noEmitOnError flag is set, then check if we have any errors so far.  If so,
        // immediately bail out.  Note that we pass 'undefined' for 'sourceFile' so that we
        // get any preEmit diagnostics, not just the ones
        if (!Js.IsTruthy(options.NoEmitOnError))
        {
            return null;
        }

        // PORT-NOTE: TS spreads four diagnostic arrays into one; the generated members answer different list types
        // (and `List<T>` is invariant), so the port appends them, which `IEnumerable<T>`'s covariance allows.
        List<IDiagnostic> diagnostics = [.. program.GetOptionsDiagnostics(cancellationToken)];
        diagnostics.AddRange(program.GetSyntacticDiagnostics(sourceFile, cancellationToken));
        diagnostics.AddRange(program.GetGlobalDiagnostics(cancellationToken));
        diagnostics.AddRange(program.GetSemanticDiagnostics(sourceFile, cancellationToken));

        if (diagnostics.Count == 0 && Utilities.GetEmitDeclarations(GetCompilerOptionsOf(program)))
        {
            diagnostics = [.. program.GetDeclarationDiagnostics(/*sourceFile*/ null, cancellationToken)];
        }

        if (diagnostics.Count == 0)
        {
            return null;
        }

        List<string>? emittedFiles = null;
        if (sourceFile is null)
        {
            var emitResult = program.EmitBuildInfo(writeFile, cancellationToken);
            if (emitResult.Diagnostics is not null)
            {
                diagnostics = [.. diagnostics, .. emitResult.Diagnostics];
            }

            emittedFiles = emitResult.EmittedFiles;
        }

        return new EmitResult
        {
            Diagnostics = [.. diagnostics],
            SourceMaps = null,
            EmittedFiles = emittedFiles,
            EmitSkipped = true,
        };
    }

    /// <summary>ported from: src/compiler/program.ts:5098-5102 — <c>filterSemanticDiagnostics</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS indexes the options object by the diagnostic's <c>skippedOn</c> name
    /// (<c>option[d.skippedOn]</c>); the port reads the same slot through the name reader
    /// (<see cref="Utilities.GetSourceFileAffectingCompilerOptionValue"/> plus
    /// <c>Utilities.GetCompilerOptionValue</c>) because a C# interface has no indexer.
    /// </remarks>
    internal static List<IDiagnostic> FilterSemanticDiagnostics(IList<IDiagnostic> diagnostics, ICompilerOptions option)
        => diagnostics
            .Where(d => d.SkippedOn is not string skippedOn
                || !Js.IsTruthy(Utilities.GetCompilerOptionValueByName(option, skippedOn)))
            .ToList();

    /// <summary>ported from: src/compiler/program.ts:5103-5127 — <c>parseConfigHostFromCompilerHostLike</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's parameter is <c>(CompilerHost | ProgramHost&lt;T&gt;) &amp;
    /// { onUnRecoverableConfigFileDiagnostic?: DiagnosticReporter }</c> and its <c>directoryStructureHost</c> defaults
    /// to the host; TS's structural <c>DirectoryStructureHost</c> has no ported name, so the port takes the
    /// <see cref="ICompilerHost"/> that satisfies it (and is what the default is).
    /// <para>
    /// PORT-NOTE: the reference reads <c>onUnRecoverableConfigFileDiagnostic</c> off the host, which the generated
    /// <see cref="ICompilerHost"/> does not carry, so the returned host exposes it for the caller to set (its default is
    /// the reference's <c>returnUndefined</c>).
    /// </para>
    /// </remarks>
    internal static ParseConfigFileHost ParseConfigHostFromCompilerHostLike(ICompilerHost host, ICompilerHost? directoryStructureHost = null)
        => new(host, directoryStructureHost ?? host);

    /// <summary>ported from: src/compiler/program.ts:5139-5177 — <c>getResolutionDiagnostic</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's second parameter is the <c>ResolvedModuleFull</c> interface and its third is the structural
    /// <c>{ isDeclarationFile }</c>; the port takes the generated <see cref="IResolvedModuleFull"/> and the boolean.
    /// The reference switches on the <c>Extension</c> enum, whose ported <see cref="IResolvedModuleFull.Extension"/> is
    /// the extension string, so the port compares against <see cref="Extension"/>'s values.
    /// </remarks>
    internal static DiagnosticMessage? GetResolutionDiagnostic(
        ICompilerOptions options,
        IResolvedModuleFull resolvedModule,
        bool isDeclarationFile)
    {
        var extension = resolvedModule.Extension;
        if (extension == Extension.Ts.Value
            || extension == Extension.Dts.Value
            || extension == Extension.Mts.Value
            || extension == Extension.Dmts.Value
            || extension == Extension.Cts.Value
            || extension == Extension.Dcts.Value)
        {
            // These are always allowed.
            return null;
        }

        if (extension == Extension.Tsx.Value)
        {
            return NeedJsx();
        }

        if (extension == Extension.Jsx.Value)
        {
            return NeedJsx() ?? NeedAllowJs();
        }

        if (extension == Extension.Js.Value || extension == Extension.Mjs.Value || extension == Extension.Cjs.Value)
        {
            return NeedAllowJs();
        }

        if (extension == Extension.Json.Value)
        {
            return NeedResolveJsonModule();
        }

        return NeedAllowArbitraryExtensions();

        DiagnosticMessage? NeedJsx()
            => Js.IsTruthy(options.Jsx) ? null : Diagnostics.Module_0_was_resolved_to_1_but_jsx_is_not_set;

        DiagnosticMessage? NeedAllowJs()
            => Utilities.GetAllowJsCompilerOption(options) || !Utilities.GetStrictOptionValue(options, "noImplicitAny")
                ? null
                : Diagnostics.Could_not_find_a_declaration_file_for_module_0_1_implicitly_has_an_any_type;

        DiagnosticMessage? NeedResolveJsonModule()
            => Utilities.GetResolveJsonModule(options)
                ? null
                : Diagnostics.Module_0_was_resolved_to_1_but_resolveJsonModule_is_not_used;

        // But don't report the allowArbitraryExtensions error from declaration files (no reason to report it, since the
        // import doesn't have a runtime component)
        DiagnosticMessage? NeedAllowArbitraryExtensions()
            => isDeclarationFile || Js.IsTruthy(options.AllowArbitraryExtensions)
                ? null
                : Diagnostics.Module_0_was_resolved_to_1_but_allowArbitraryExtensions_is_not_set;
    }

    /// <summary>ported from: src/compiler/program.ts:5178-5187 — <c>getModuleNames</c>.</summary>
    internal static List<LiteralExpression> GetModuleNames(SourceFile file)
    {
        var res = new List<LiteralExpression>(file.Imports ?? []);
        foreach (var aug in file.ModuleAugmentations ?? [])
        {
            if (aug.Kind == SyntaxKind.StringLiteral)
            {
                res.Add((StringLiteral)aug);
            }

            // Do nothing if it's an Identifier; we don't need to do module resolution for `declare global`.
        }

        return res;
    }

    /// <summary>ported from: src/compiler/program.ts:5190-5201 — <c>getModuleNameStringLiteralAt</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's first parameter is the structural <c>SourceFileImportsList</c>; the generated
    /// <c>ISourceFileImportsList</c> has no implementation (<c>SourceFile</c> is not declared to satisfy it), and its
    /// two slots have exactly the <see cref="SourceFile"/> types, so the port takes the <see cref="SourceFile"/>.
    /// </remarks>
    internal static LiteralExpression GetModuleNameStringLiteralAt(SourceFile file, int index)
    {
        var imports = file.Imports ?? [];
        if (index < imports.Count)
        {
            return imports[index];
        }

        var augIndex = imports.Count;
        foreach (var aug in file.ModuleAugmentations ?? [])
        {
            if (aug.Kind == SyntaxKind.StringLiteral)
            {
                if (index == augIndex)
                {
                    return (StringLiteral)aug;
                }

                augIndex++;
            }

            // Do nothing if it's an Identifier; we don't need to do module resolution for `declare global`.
        }

        return Debug.Fail<LiteralExpression>("should never ask for module name at index higher than possible module name");
    }

}

// ---------------------------------------------------------------------------------------------
// The constructible shapes program.ts's free functions hand around (§5 rule 6)
// ---------------------------------------------------------------------------------------------

/// <summary>The reference's <c>ReferenceFileLocation | SyntheticReferenceFileLocation</c> union.</summary>
/// <remarks>
/// PORT-NOTE: TS's inline union has no name; §5 rule 3 needs one for the result of
/// <see cref="Program.GetReferencedFileLocation"/>, so the port declares a marker interface the two location classes
/// implement — the same construction <c>IFileIncludeReason</c> got for the reasons' union
/// (<c>Program/Program.ProgramDiagnostics.cs</c>).
/// </remarks>
internal interface IReferencedFileLocation;

/// <summary>ported from: src/compiler/program.ts:1185-1190 — <c>ReferenceFileLocation</c>.</summary>
internal sealed class ReferenceFileLocation : IReferenceFileLocation, IReferencedFileLocation
{
    public SourceFile? File { get; set; }

    public int Pos { get; set; }

    public int End { get; set; }

    public IPackageId? PackageId { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:1192-1197 — <c>SyntheticReferenceFileLocation</c>.</summary>
internal sealed class SyntheticReferenceFileLocation : ISyntheticReferenceFileLocation, IReferencedFileLocation
{
    public SourceFile? File { get; set; }

    public IPackageId? PackageId { get; set; }

    public string? Text { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:1015-1020 — <c>emptyResolution</c>'s shape.</summary>
/// <remarks>
/// PORT-NOTE: TS's const is an object literal whose one slot is read through two different interfaces
/// (<c>ResolvedModuleWithFailedLookupLocations</c> and <c>ResolvedTypeReferenceDirectiveWithFailedLookupLocations</c>);
/// the port declares one class implementing both, so the const can be shared exactly as the reference shares it.
/// </remarks>
internal sealed class EmptyResolution : IResolvedModuleWithFailedLookupLocations, IResolvedTypeReferenceDirectiveWithFailedLookupLocations
{
    public IResolvedModuleFull? ResolvedModule => null;

    public IResolvedTypeReferenceDirective? ResolvedTypeReferenceDirective => null;

    public List<string>? FailedLookupLocations { get; set; }

    public List<string>? AffectingLocations { get; set; }

    public List<IDiagnostic>? ResolutionDiagnostics { get; set; }

    public string? AlternateResult { get; set; }
}

/// <summary>ported from: src/compiler/types.ts:7179-7184 — <c>EmitResult</c>.</summary>
/// <remarks>
/// PORT-NOTE: §5 rule 6 — TS builds every <c>EmitResult</c> as an object literal (<c>program.ts:5056</c>, 5087,
/// <c>emitter.ts</c>), so the port needs the constructible class. It lives here because this file creates the two
/// literals <c>program.ts</c> owns; M7's emitter constructs the same class rather than declaring a second one.
/// </remarks>
internal sealed class EmitResult : IEmitResult
{
    public bool EmitSkipped { get; set; }

    public List<IDiagnostic>? Diagnostics { get; set; }

    public List<string>? EmittedFiles { get; set; }

    public List<ISourceMapEmitResult>? SourceMaps { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:1478-1487 — the <c>CreateProgramOptions</c> literal.</summary>
/// <remarks>PORT-NOTE: §5 rule 6 — the reference's <c>createCreateProgramOptions</c> returns an object literal.</remarks>
internal sealed class CreateProgramOptions : ICreateProgramOptions
{
    public List<string>? RootNames { get; set; }

    public ICompilerOptions? Options { get; set; }

    public List<IProjectReference>? ProjectReferences { get; set; }

    public ICompilerHost? Host { get; set; }

    public IProgram? OldProgram { get; set; }

    public List<IDiagnostic>? ConfigFileParsingDiagnostics { get; set; }

    public string? TypeScriptVersion { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:1362 — the <c>lookupFromPackageJson</c> literal.</summary>
/// <remarks>
/// PORT-NOTE: TS's <c>getImpliedNodeFormatForFileWorker</c> answers either a <c>ResolutionMode</c> or a
/// <c>Partial&lt;CreateSourceFileOptions&gt;</c>; the port's second arm is this class, whose slots are named exactly as
/// <c>CreateSourceFileOptions</c> names them so the same object serves as the options (M6's <c>GetCreateSourceFileOptions</c>
/// pattern-matches it).
/// </remarks>
internal sealed class ImpliedNodeFormatAndPackageJsonScope
{
    /// <summary>ported from: src/compiler/program.ts:1364 — <c>impliedNodeFormat</c>.</summary>
    public ModuleKind? ImpliedNodeFormat { get; set; }

    /// <summary>ported from: src/compiler/program.ts:1365 — <c>packageJsonLocations</c>.</summary>
    public List<string>? PackageJsonLocations { get; set; }

    /// <summary>ported from: src/compiler/program.ts:1366 — <c>packageJsonScope</c>.</summary>
    public PackageJsonInfo? PackageJsonScope { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:5103-5127 — the <c>ParseConfigFileHost</c> literal.</summary>
/// <remarks>
/// PORT-NOTE: the reference's <c>ParseConfigFileHost</c> is <c>{ ...ParseConfigHost, onUnRecoverableConfigFileDiagnostic,
/// trace? }</c>; §5 rule 6 makes it this class, which forwards to the host it was built from.
/// </remarks>
internal sealed class ParseConfigFileHost : IParseConfigHost
{
    private readonly ICompilerHost _host;
    private readonly ICompilerHost _directoryStructureHost;

    public ParseConfigFileHost(ICompilerHost host, ICompilerHost directoryStructureHost)
    {
        _host = host;
        _directoryStructureHost = directoryStructureHost;

        // PORT-NOTE: TS's `host.useCaseSensitiveFileNames()` is a bool; the generated ModuleResolutionHost slot is
        // `object?`, and `Utilities.HostUsesCaseSensitiveFileNames` is the port's reader of either shape.
        UseCaseSensitiveFileNames = Utilities.HostUsesCaseSensitiveFileNames(host);
    }

    /// <summary>ported from: src/compiler/program.ts:5109 — <c>fileExists</c>.</summary>
    public bool FileExists(string? fileName) => _directoryStructureHost.FileExists(fileName);

    /// <summary>ported from: src/compiler/program.ts:5113 — <c>readDirectory</c>.</summary>
    public List<string> ReadDirectory(string? rootDir, List<string>? extensions, List<string>? excludes, List<string>? includes, int? depth)
    {
        Debug.AssertIsDefined(_directoryStructureHost, "'CompilerHost.readDirectory' must be implemented to correctly process 'projectReferences'");
        return _directoryStructureHost.ReadDirectory(rootDir, extensions, excludes, includes, depth);
    }

    /// <summary>ported from: src/compiler/program.ts:5115 — <c>readFile</c>.</summary>
    public string? ReadFile(string? fileName) => _directoryStructureHost.ReadFile(fileName);

    /// <summary>ported from: src/compiler/program.ts:5116 — <c>directoryExists</c>.</summary>
    public bool DirectoryExists(string? directory) => _directoryStructureHost.DirectoryExists(directory);

    /// <summary>ported from: src/compiler/program.ts:5117 — <c>getDirectories</c>.</summary>
    public List<string> GetDirectories(string? path) => _directoryStructureHost.GetDirectories(path);

    /// <summary>ported from: src/compiler/program.ts:5118 — <c>realpath</c>.</summary>
    public string Realpath(string? path) => _directoryStructureHost.Realpath(path);

    /// <summary>ported from: src/compiler/program.ts:5119 — <c>useCaseSensitiveFileNames</c>.</summary>
    public object? UseCaseSensitiveFileNames { get; set; }

    /// <summary>ported from: src/compiler/program.ts:5120 — <c>getCurrentDirectory</c>.</summary>
    public string GetCurrentDirectory() => _host.GetCurrentDirectory();

    /// <summary>ported from: src/compiler/program.ts:5121 — <c>onUnRecoverableConfigFileDiagnostic</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's default is <c>returnUndefined</c>, i.e. a reporter that reports nothing.</remarks>
    public Action<IDiagnostic> OnUnRecoverableConfigFileDiagnostic { get; set; } = static _ => { };

    /// <summary>ported from: src/compiler/program.ts:5122 — <c>trace</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's slot is optional and absent when the host has no trace; the generated
    /// <see cref="IParseConfigHost"/> declares it required (the same "the generator cannot express `?`" gap the
    /// <c>ICompilerHost</c> extension records), so the port forwards to the host's, which the reference's
    /// <c>createCompilerHostWorker</c> always provides.
    /// </remarks>
    public void Trace(string? s) => _host.Trace(s);

    /// <summary>ported from: src/compiler/program.ts:5123 — <c>getGlobalTypingsCacheLocation</c>.</summary>
    public string? GetGlobalTypingsCacheLocation() => _directoryStructureHost.GetGlobalTypingsCacheLocation();
}

/// <summary>
/// The optional members of the generated <see cref="ICompilerHost"/>.
/// </summary>
/// <remarks>
/// PORT-NOTE: TS's <c>CompilerHost</c> marks these members <c>?</c> and <c>program.ts</c> branches on their absence
/// (<c>host.resolveModuleNames ? … : …</c>, <c>host.hasInvalidatedResolutions || returnFalse</c>, …). The generated
/// interface cannot say "absent" — every method it declares is required — so the port keeps the reference's field in a
/// nullable delegate slot and the class's method forwards to it (failing loudly if it is called while null). This is
/// the mechanism <c>docs/architecture.md</c> §11.1 D9 calls for: <c>CompilerHost</c> is replicated as the reference
/// declares it, presence included.
/// </remarks>
internal partial interface ICompilerHost
{
    /// <summary>TS: the optional <c>getSourceFileByPath</c>.</summary>
    Func<string?, string?, object?, Action<string>?, bool?, SourceFile?>? GetSourceFileByPathFunc { get; set; }

    /// <summary>TS: the optional <c>getCancellationToken</c>.</summary>
    Func<ICancellationToken>? GetCancellationTokenFunc { get; set; }

    /// <summary>TS: the optional <c>resolveModuleNames</c>.</summary>
    Func<List<string>?, string?, List<string>?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<IResolvedModule>>? ResolveModuleNamesFunc { get; set; }

    /// <summary>TS: the optional <c>resolveTypeReferenceDirectives</c>.</summary>
    Func<object?, string?, IResolvedProjectReference?, ICompilerOptions?, object?, List<IResolvedTypeReferenceDirective>>? ResolveTypeReferenceDirectivesFunc { get; set; }

    /// <summary>TS: the optional <c>resolveModuleNameLiterals</c>.</summary>
    Func<List<LiteralExpression>?, string?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<LiteralExpression>?, List<IResolvedModuleWithFailedLookupLocations>>? ResolveModuleNameLiteralsFunc { get; set; }

    /// <summary>TS: the optional <c>resolveTypeReferenceDirectiveReferences</c>, over <see cref="IFileReference"/>
    /// entries.</summary>
    Func<List<IFileReference>?, string?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<IFileReference>?, List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>? ResolveTypeReferenceDirectiveReferencesFunc { get; set; }

    /// <summary>TS: the same optional member over <c>string</c> entries — TS's generic member instantiated twice; the
    /// port names the second instantiation.</summary>
    Func<List<string>?, string?, IResolvedProjectReference?, ICompilerOptions?, SourceFile?, List<string>?, List<IResolvedTypeReferenceDirectiveWithFailedLookupLocations>>? ResolveTypeReferenceDirectiveReferencesStringsFunc { get; set; }

    /// <summary>TS: the optional <c>resolveLibrary</c>.</summary>
    Func<string?, string?, ICompilerOptions?, string?, IResolvedModuleWithFailedLookupLocations>? ResolveLibraryFunc { get; set; }

    /// <summary>TS: the optional <c>hasInvalidatedLibResolutions</c>.</summary>
    Func<string?, bool>? HasInvalidatedLibResolutionsFunc { get; set; }

    /// <summary>TS: the optional <c>onReleaseOldSourceFile</c>.</summary>
    Action<SourceFile?, ICompilerOptions?, bool, SourceFile?>? OnReleaseOldSourceFileFunc { get; set; }

    /// <summary>TS: the optional <c>onReleaseParsedCommandLine</c>.</summary>
    Action<string?, IResolvedProjectReference?, ICompilerOptions?>? OnReleaseParsedCommandLineFunc { get; set; }

    /// <summary>TS: the optional <c>hasInvalidatedResolutions</c>.</summary>
    Func<string?, bool>? HasInvalidatedResolutionsFunc { get; set; }

    /// <summary>TS: the optional <c>getParsedCommandLine</c>.</summary>
    Func<string?, IParsedCommandLine?>? GetParsedCommandLineFunc { get; set; }

    /// <summary>TS: the optional <c>useSourceOfProjectReferenceRedirect</c>.</summary>
    Func<bool>? UseSourceOfProjectReferenceRedirectFunc { get; set; }

    /// <summary>TS: the optional <c>getSymlinkCache</c>.</summary>
    Func<object?>? GetSymlinkCacheFunc { get; set; }

    /// <summary>TS: the optional <c>getModuleResolutionCache</c>.</summary>
    Func<object?>? GetModuleResolutionCacheFunc { get; set; }

    /// <summary>TS: the optional <c>getBuildInfo</c>.</summary>
    Func<string?, string?, IBuildInfo?>? GetBuildInfoFunc { get; set; }

    /// <summary>TS: the optional <c>getGlobalTypingsCacheLocation</c>.</summary>
    Func<string?, string?>? GetGlobalTypingsCacheLocationFunc { get; set; }
}

/// <summary>
/// The same "the generator cannot express <c>?</c>" gap for <see cref="ICompilerHostLikeForCache"/>, whose four
/// members TS reassigns (<c>host.readFile = …</c> in <c>changeCompilerHostLikeToUseCache</c>).
/// </summary>
internal partial interface ICompilerHostLikeForCache
{
    /// <summary>TS: <c>readFile</c> as the cache wrapper replaces it.</summary>
    Func<string?, string?, string?>? ReadFileFunc { get; set; }

    /// <summary>TS: <c>fileExists</c> as the cache wrapper replaces it.</summary>
    Func<string?, bool>? FileExistsFunc { get; set; }

    /// <summary>TS: the optional <c>directoryExists</c>.</summary>
    Func<string?, bool>? DirectoryExistsFunc { get; set; }

    /// <summary>TS: the optional <c>createDirectory</c>.</summary>
    Action<string?>? CreateDirectoryFunc { get; set; }
}

/// <summary>ported from: src/compiler/program.ts:626-633 — the <c>CompilerHostLikeWithCache</c> literal.</summary>
/// <remarks>PORT-NOTE: §5 rule 6 — <c>changeCompilerHostLikeToUseCache</c> returns an object literal.</remarks>
internal sealed class CompilerHostLikeWithCache : ICompilerHostLikeWithCache
{
    public Func<string, string, string>? OriginalReadFile { get; set; }

    public Func<string, bool>? OriginalFileExists { get; set; }

    public Func<string, bool>? OriginalDirectoryExists { get; set; }

    public Action<string>? OriginalCreateDirectory { get; set; }

    public Action<string, string, bool, Action<string>, List<SourceFile>, IWriteFileCallbackData, object?>? OriginalWriteFile { get; set; }

    public Func<string, object?, Action<string>, bool, SourceFile>? GetSourceFileWithCache { get; set; }

    public Func<string, string>? ReadFileWithCache { get; set; }
}

// ---------------------------------------------------------------------------------------------
// The core.ts/utilities.ts members program.ts's own free functions need
// ---------------------------------------------------------------------------------------------

/// <summary>
/// The <c>core.ts</c>/<c>utilities.ts</c> members the members above call.
/// </summary>
/// <remarks>
/// PORT-NOTE: program.ts is the only caller of these, so they are declared here rather than in a Layer-0 file; a slice
/// that needs one elsewhere should move it (and this comment) rather than declare a second copy. The option tables are
/// name lists, not <c>CommandLineOption</c> metadata — the treatment
/// <c>Program/Utilities.ModuleResolution.cs:209-248</c> records, because the metadata table is M8's.
/// </remarks>
internal static partial class Utilities
{
    /// <summary>ported from: src/compiler/utilities.ts:10450-10453 — <c>isJsonEqual</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS's body is <c>a === b || equalOwnProperties(a, b, isJsonEqual)</c> once both sides are objects. The
    /// port's values are the option slots and the shapes the option tables carry, so it compares scalars by equality and
    /// the two container shapes deeply; a value of any other shape (a <see cref="SourceFile"/> in the config-file slot)
    /// is compared by identity, where the reference would walk its own keys — see
    /// <see cref="CompareDataObjects"/>.
    /// </remarks>
    public static bool IsJsonEqual(object? a, object? b)
    {
        if (Equals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a is IDictionary leftMap && b is IDictionary rightMap)
        {
            return EqualOwnProperties(leftMap, rightMap);
        }

        if (a is not string && a is IEnumerable leftSequence && b is IEnumerable rightSequence)
        {
            return SequenceEqual(leftSequence, rightSequence);
        }

        return false;
    }

    /// <summary>TS: <c>equalOwnProperties</c> — the same keys, with equal values, in either order.</summary>
    private static bool EqualOwnProperties(IDictionary left, IDictionary right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (DictionaryEntry entry in left)
        {
            if (!right.Contains(entry.Key) || !IsJsonEqual(entry.Value, right[entry.Key]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>TS: the element-wise comparison of two arrays (their indices are the own keys it compares).</summary>
    private static bool SequenceEqual(IEnumerable left, IEnumerable right)
    {
        var leftEnumerator = left.GetEnumerator();
        var rightEnumerator = right.GetEnumerator();
        while (true)
        {
            var hasLeft = leftEnumerator.MoveNext();
            if (hasLeft != rightEnumerator.MoveNext())
            {
                return false;
            }

            if (!hasLeft)
            {
                return true;
            }

            if (!IsJsonEqual(leftEnumerator.Current, rightEnumerator.Current))
            {
                return false;
            }
        }
    }

    /// <summary>ported from: src/compiler/utilities.ts:8147-8165 — <c>compareDataObjects</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: TS walks both objects' own enumerable keys (<c>Object.keys</c>/<c>for…in</c>) and compares each pair,
    /// recursing into the values whose <c>typeof</c> is <c>"object"</c>. A C# object has no own-key view, so the port
    /// walks the slots of the <see cref="ICompilerOptions"/> interface — which is what both sides are, and which bounds
    /// the walk (the reference's recursion can leave the options object through the config file's AST). Values are
    /// compared with <see cref="IsJsonEqual"/>, which agrees with the reference on every scalar option and compares the
    /// container-shaped ones deeply; a slot holding an object of another shape is compared by identity, so a
    /// structurally identical re-parse of a config file reads as a change here (a rebuild) where the reference would
    /// answer "same" — the reference's own next check compares the config file's text, and a changed text fails there.
    /// </remarks>
    public static bool CompareDataObjects(ICompilerOptions dst, ICompilerOptions src)
    {
        var properties = typeof(ICompilerOptions).GetProperties();
        foreach (var property in properties)
        {
            if (!IsJsonEqual(property.GetValue(dst), property.GetValue(src)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ported from: src/compiler/utilities.ts:11824-11826 — <c>isBuildInfoFile</c>.</summary>
    public static bool IsBuildInfoFile(string fileName)
        => Paths.FileExtensionIs(fileName, Extension.TsBuildInfo.Value);

    /// <summary>ported from: src/compiler/utilities.ts:9014-9019 — <c>importSyntaxAffectsModuleResolution</c>.</summary>
    public static bool ImportSyntaxAffectsModuleResolution(ICompilerOptions? options)
    {
        var moduleResolution = GetEmitModuleResolutionKind(options);
        return ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext
            || GetResolvePackageJsonExports(options)
            || GetResolvePackageJsonImports(options);
    }

    /// <summary>ported from: src/compiler/commandLineParser.ts:1710 — <c>sourceFileAffectingCompilerOptions</c>.</summary>
    /// <remarks>
    /// PORT-NOTE: the reference filters the full <c>optionDeclarations</c> table (M8) down to the entries marked
    /// <c>affectsSourceFile</c> or <c>affectsBindDiagnostics</c>; only the filtered names are transcribed here, the
    /// treatment <c>Program/Utilities.ModuleResolution.cs:209-248</c> gives the module-resolution subset (the eleven
    /// names were read off the reference's table with a throwaway script, like the lib and diagnostic tables).
    /// </remarks>
    public static readonly List<string> SourceFileAffectingCompilerOptions =
    [
        "target",
        "module",
        "jsx",
        "importHelpers",
        "alwaysStrict",
        "noFallthroughCasesInSwitch",
        "moduleResolution",
        "jsxImportSource",
        "allowUnusedLabels",
        "allowUnreachableCode",
        "moduleDetection",
    ];

    /// <summary>ported from: src/compiler/utilities.ts:9399-9403 — <c>getCompilerOptionValue</c>, for the names
    /// <see cref="SourceFileAffectingCompilerOptions"/> and the diagnostics' <c>skippedOn</c> use.</summary>
    /// <remarks>
    /// PORT-NOTE: <c>getCompilerOptionValue</c> reads <c>options[option.name]</c> (with the <c>strictFlag</c>/
    /// <c>allowJsFlag</c> special cases); the port's existing reader
    /// (<c>Program/Utilities.ModuleResolution.cs:224</c>) answers for the module-resolution names only, so this reader
    /// covers the rest of the names program.ts's own members look up and delegates the remainder to that one. Both
    /// collapse into one when M8's table lands.
    /// </remarks>
    public static object? GetCompilerOptionValueByName(ICompilerOptions options, string optionName) => optionName switch
    {
        "importHelpers" => options.ImportHelpers,
        "alwaysStrict" => options.AlwaysStrict,
        "noFallthroughCasesInSwitch" => options.NoFallthroughCasesInSwitch,
        "jsxImportSource" => options.JsxImportSource,
        "allowUnusedLabels" => options.AllowUnusedLabels,
        "allowUnreachableCode" => options.AllowUnreachableCode,
        "noEmit" => options.NoEmit,
        _ => GetCompilerOptionValue(options, optionName),
    };
}
