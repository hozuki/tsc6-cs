namespace TypeScript.Compiler;

// ported from: src/compiler/sys.ts:381-455, 576-1371, src/compiler/watchUtilities.ts:834-842
/// <summary>
/// The composition layer of the watcher machinery: the recursive-directory watcher, the
/// watch-the-parent-directory watcher, and <see cref="CreateSystemWatchFunctions"/>, which picks between them
/// from <see cref="IWatchOptions"/>.
/// </summary>
internal static partial class SysWatch
{
    // ported from: src/compiler/watchUtilities.ts:834-842
    /// <summary>
    /// The options to fall back to when the native watcher cannot be used: poll the file instead.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS reinterprets the <c>fallbackPolling</c> value as a <c>WatchFileKind</c>
    /// (<c>fallbackPolling as unknown as WatchFileKind</c>). That is only sound because the two enums list their
    /// members in the same order; the port keeps the reinterpretation explicit, and the test below pins the
    /// correspondence.
    /// </remarks>
    public static IWatchOptions GetFallbackOptions(IWatchOptions? options)
    {
        var fallbackPolling = options?.FallbackPolling;
        return new WatchOptions
        {
            WatchFile = fallbackPolling is not null
                ? (WatchFileKind)fallbackPolling.Value
                : WatchFileKind.PriorityPollingInterval,
        };
    }

    /// <summary>ported from: src/compiler/sys.ts:576-585 — TS: <c>RecursiveDirectoryWatcherHost</c>.</summary>
    public sealed class RecursiveDirectoryWatcherHost
    {
        public required HostWatchDirectory WatchDirectory { get; init; }

        public required bool UseCaseSensitiveFileNames { get; init; }

        public required Func<string> GetCurrentDirectory { get; init; }

        public required Func<string, IList<string>> GetAccessibleSortedChildDirectories { get; init; }

        public required FileSystemEntryExists FileSystemEntryExists { get; init; }

        public required Func<string, string> Realpath { get; init; }

        public required Func<Action, int, object?> SetTimeout { get; init; }

        public required Action<object?> ClearTimeout { get; init; }
    }

    /// <summary>ported from: src/compiler/sys.ts:602-604 — TS: <c>ChildDirectoryWatcher</c>.</summary>
    private interface IChildDirectoryWatcher : IFileWatcher
    {
        string DirName { get; }
    }

    /// <summary>ported from: src/compiler/sys.ts:678-694 — TS: the <c>{ dirName, close }</c> literal.</summary>
    private sealed class ChildDirectoryWatcher(string dirName, Action close) : IChildDirectoryWatcher
    {
        public string DirName { get; } = dirName;

        public void Close() => close();
    }

    /// <summary>ported from: src/compiler/sys.ts:606-612 — TS: <c>HostDirectoryWatcher</c>.</summary>
    private sealed class HostDirectoryWatcher
    {
        public required IFileWatcher Watcher { get; init; }

        public List<IChildDirectoryWatcher> ChildWatches { get; set; } = [];

        public int RefCount { get; set; }

        public IChildDirectoryWatcher? TargetWatcher { get; set; }

        public HashSet<string>? Links { get; set; }
    }

    /// <summary>ported from: src/compiler/sys.ts:615 — the callback registration kept per watched directory.
    /// </summary>
    private sealed record DirectoryCallbackEntry(string DirName, DirectoryWatcherCallback Callback);

    /// <summary>ported from: src/compiler/sys.ts:616 — a directory whose child watches are due to be refreshed.
    /// </summary>
    private sealed class PendingChildWatchUpdate
    {
        public required string DirName { get; init; }

        public required IWatchOptions? Options { get; init; }

        public List<string> FileNames { get; } = [];
    }

    /// <summary>ported from: src/compiler/sys.ts:697 — TS: <c>Map&lt;Path, string[] | true&gt;</c>: either the
    /// changed file names for a root directory, or "the whole directory changed".</summary>
    private sealed class InvokeMapValue
    {
        public bool WholeDirectory { get; set; }

        public List<string>? FileNames { get; set; }
    }

    // ported from: src/compiler/sys.ts:592-888
    /// <summary>
    /// Watches a directory recursively on top of a non-recursive primitive: every child directory gets its own
    /// watcher, and the child set is re-enumerated whenever one of them reports a change.
    /// </summary>
    public static HostWatchDirectory CreateDirectoryWatcherSupportingRecursive(RecursiveDirectoryWatcherHost host)
    {
        var watchDirectory = host.WatchDirectory;
        var useCaseSensitiveFileNames = host.UseCaseSensitiveFileNames;
        var getCurrentDirectory = host.GetCurrentDirectory;
        var getAccessibleSortedChildDirectories = host.GetAccessibleSortedChildDirectories;
        var fileSystemEntryExists = host.FileSystemEntryExists;
        var realpath = host.Realpath;
        var setTimeout = host.SetTimeout;
        var clearTimeout = host.ClearTimeout;

        var cache = new Dictionary<string, HostDirectoryWatcher>();
        var callbackCache = Core.CreateMultiMap<string, DirectoryCallbackEntry>();
        var cacheToUpdateChildWatches = new Dictionary<string, PendingChildWatchUpdate>();
        object? timerToUpdateChildWatches = null;

        var filePathComparer = Core.GetStringComparer(!useCaseSensitiveFileNames);
        var toCanonicalFilePath = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames);

        return WatchDirectory;

        IFileWatcher WatchDirectory(string dirName, DirectoryWatcherCallback callback, bool recursive, IWatchOptions? options)
            => recursive
                ? CreateDirectoryWatcher(dirName, options, callback)
                : watchDirectory(dirName, callback, recursive, options);

        /// <summary>Creates (or reuses) the watcher for <paramref name="dirName"/>.</summary>
        IChildDirectoryWatcher CreateDirectoryWatcher(
            string dirName,
            IWatchOptions? options,
            DirectoryWatcherCallback? callback = null,
            string? link = null)
        {
            var dirPath = toCanonicalFilePath(dirName);
            if (cache.TryGetValue(dirPath, out var directoryWatcher))
            {
                directoryWatcher.RefCount++;
            }
            else
            {
                directoryWatcher = new HostDirectoryWatcher
                {
                    Watcher = watchDirectory(
                        dirName,
                        fileName =>
                        {
                            if (IsIgnoredPath(fileName, options)) return;

                            if (options?.SynchronousWatchDirectory == true)
                            {
                                // Call the actual callback
                                if (cache.GetValueOrDefault(dirPath)?.TargetWatcher is null) InvokeCallbacks(dirName, dirPath, fileName, invokeMap: null, fileNames: null);

                                // Iterate through existing children and update the watches if needed
                                UpdateChildWatches(dirName, dirPath, options);
                            }
                            else
                            {
                                NonSyncUpdateChildWatches(dirName, dirPath, fileName, options);
                            }
                        },
                        /*recursive*/ false,
                        options),
                    RefCount = 1,
                };
                cache[dirPath] = directoryWatcher;
                UpdateChildWatches(dirName, dirPath, options);
            }

            if (link is not null) (directoryWatcher.Links ??= []).Add(link);

            var callbackToAdd = callback is not null ? new DirectoryCallbackEntry(dirName, callback) : null;
            if (callbackToAdd is not null) callbackCache.Add(dirPath, callbackToAdd);

            return new ChildDirectoryWatcher(dirName, () =>
            {
                var watcher = Debug.CheckDefined(cache.GetValueOrDefault(dirPath));
                if (callbackToAdd is not null) callbackCache.Remove(dirPath, callbackToAdd);
                if (link is not null) watcher.Links?.Remove(link);
                watcher.RefCount--;

                if (watcher.RefCount != 0) return;

                cache.Remove(dirPath);
                watcher.Links = null;
                CloseFileWatcher(watcher.Watcher);
                CloseTargetWatcher(watcher);
                foreach (var childWatcher in watcher.ChildWatches) CloseFileWatcher(childWatcher);
            });
        }

        /// <summary>
        /// Calls the registered callbacks, either for one changed file or by recording which roots a batch of
        /// changes affects.
        /// </summary>
        /// <remarks>
        /// TS declares this as two overloads (one taking a file name, one a map); C# local functions cannot be
        /// overloaded, so the two shapes are folded into one signature.
        /// </remarks>
        void InvokeCallbacks(
            string dirName,
            string dirPath,
            string? fileName,
            Dictionary<string, InvokeMapValue>? invokeMap,
            List<string>? fileNames)
        {
            // Call the actual callback
            foreach (var rootDirName in callbackCache.Keys)
            {
                if (invokeMap is not null && invokeMap.TryGetValue(rootDirName, out var recorded) && recorded.WholeDirectory) continue;
                var callbacks = callbackCache.Get(rootDirName);
                if (callbacks is null) continue;

                if (rootDirName == dirPath ||
                    (Core.StartsWith(dirPath, rootDirName) &&
                        dirPath.Length > rootDirName.Length &&
                        dirPath[rootDirName.Length] == Paths.DirectorySeparator[0]))
                {
                    if (invokeMap is not null)
                    {
                        if (fileNames is not null)
                        {
                            if (invokeMap.TryGetValue(rootDirName, out var existing)) existing.FileNames!.AddRange(fileNames);
                            else invokeMap[rootDirName] = new InvokeMapValue { FileNames = [.. fileNames] };
                        }
                        else
                        {
                            invokeMap[rootDirName] = new InvokeMapValue { WholeDirectory = true };
                        }
                    }
                    else
                    {
                        foreach (var entry in callbacks) entry.Callback(fileName!);
                    }
                }
            }

            var links = cache.GetValueOrDefault(dirPath)?.Links;
            if (links is not null)
            {
                foreach (var link in links.ToArray())
                {
                    string ToPathInLink(string path) =>
                        Paths.CombinePaths(link, Paths.GetRelativePathFromDirectory(dirName, path, toCanonicalFilePath));

                    if (invokeMap is not null)
                    {
                        InvokeCallbacks(link, toCanonicalFilePath(link), null, invokeMap, fileNames?.Select(ToPathInLink).ToList());
                    }
                    else
                    {
                        InvokeCallbacks(link, toCanonicalFilePath(link), ToPathInLink(fileName!), invokeMap: null, fileNames: null);
                    }
                }
            }
        }

        void NonSyncUpdateChildWatches(string dirName, string dirPath, string fileName, IWatchOptions? options)
        {
            // Iterate through existing children and update the watches if needed
            var parentWatcher = cache.GetValueOrDefault(dirPath);
            if (parentWatcher is not null && fileSystemEntryExists(dirName, FileSystemEntryKind.Directory))
            {
                // Schedule the update and postpone invoke for callbacks
                ScheduleUpdateChildWatches(dirName, dirPath, fileName, options);
                return;
            }

            // Call the actual callbacks and remove child watches
            InvokeCallbacks(dirName, dirPath, fileName, invokeMap: null, fileNames: null);
            CloseTargetWatcher(parentWatcher);
            RemoveChildWatches(parentWatcher);
        }

        void ScheduleUpdateChildWatches(string dirName, string dirPath, string fileName, IWatchOptions? options)
        {
            if (cacheToUpdateChildWatches.TryGetValue(dirPath, out var existing))
            {
                existing.FileNames.Add(fileName);
            }
            else
            {
                var pending = new PendingChildWatchUpdate { DirName = dirName, Options = options };
                pending.FileNames.Add(fileName);
                cacheToUpdateChildWatches[dirPath] = pending;
            }

            if (timerToUpdateChildWatches is not null)
            {
                clearTimeout(timerToUpdateChildWatches);
                timerToUpdateChildWatches = null;
            }

            timerToUpdateChildWatches = setTimeout(OnTimerToUpdateChildWatches, 1000);
        }

        void OnTimerToUpdateChildWatches()
        {
            timerToUpdateChildWatches = null;
            SysCore.SysLog($"sysLog:: onTimerToUpdateChildWatches:: {cacheToUpdateChildWatches.Count}");
            var start = PerformanceCore.Timestamp();
            var invokeMap = new Dictionary<string, InvokeMapValue>();

            while (timerToUpdateChildWatches is null && cacheToUpdateChildWatches.Count != 0)
            {
                var (dirPath, pending) = cacheToUpdateChildWatches.First();
                cacheToUpdateChildWatches.Remove(dirPath);
                // Because the child refresh is fresh, we would need to invalidate whole root directory being
                // watched to ensure that all the changes are reflected at this time
                var hasChanges = UpdateChildWatches(pending.DirName, dirPath, pending.Options);
                if (cache.GetValueOrDefault(dirPath)?.TargetWatcher is null)
                {
                    InvokeCallbacks(pending.DirName, dirPath, null, invokeMap, hasChanges ? null : pending.FileNames);
                }
            }

            SysCore.SysLog($"sysLog:: invokingWatchers:: Elapsed:: {PerformanceCore.Timestamp() - start}ms:: {cacheToUpdateChildWatches.Count}");
            foreach (var rootDirName in callbackCache.Keys)
            {
                if (!invokeMap.TryGetValue(rootDirName, out var existing)) continue;
                var callbacks = callbackCache.Get(rootDirName);
                if (callbacks is null) continue;

                foreach (var entry in callbacks)
                {
                    if (existing.FileNames is { } fileNames)
                    {
                        foreach (var fileName in fileNames) entry.Callback(fileName);
                    }
                    else
                    {
                        entry.Callback(entry.DirName);
                    }
                }
            }

            var elapsed = PerformanceCore.Timestamp() - start;
            SysCore.SysLog($"sysLog:: Elapsed:: {elapsed}ms:: onTimerToUpdateChildWatches:: {cacheToUpdateChildWatches.Count} {timerToUpdateChildWatches}");
        }

        void RemoveChildWatches(HostDirectoryWatcher? parentWatcher)
        {
            if (parentWatcher is null) return;
            var existingChildWatches = parentWatcher.ChildWatches;
            parentWatcher.ChildWatches = [];
            foreach (var childWatcher in existingChildWatches)
            {
                childWatcher.Close();
                RemoveChildWatches(cache.GetValueOrDefault(toCanonicalFilePath(childWatcher.DirName)));
            }
        }

        static void CloseTargetWatcher(HostDirectoryWatcher? watcher)
        {
            if (watcher?.TargetWatcher is { } targetWatcher)
            {
                targetWatcher.Close();
                watcher.TargetWatcher = null;
            }
        }

        bool UpdateChildWatches(string parentDir, string parentDirPath, IWatchOptions? options)
        {
            // Iterate through existing children and update the watches if needed
            var parentWatcher = cache.GetValueOrDefault(parentDirPath);
            if (parentWatcher is null) return false;
            var target = Paths.NormalizePath(realpath(parentDir));
            bool hasChanges;
            List<IChildDirectoryWatcher>? newChildWatches = null;
            if (filePathComparer(target, parentDir) == Comparison.EqualTo)
            {
                hasChanges = Core.EnumerateInsertsAndDeletes<string, IChildDirectoryWatcher>(
                    fileSystemEntryExists(parentDir, FileSystemEntryKind.Directory)
                        ? Core.MapDefined<string, string>(getAccessibleSortedChildDirectories(parentDir), (child, _) =>
                        {
                            var childFullName = Paths.GetNormalizedAbsolutePath(child, parentDir);
                            // Filter our the symbolic link directories since those arent included in recursive
                            // watch which is same behaviour when recursive: true is passed to fs.watch
                            return !IsIgnoredPath(childFullName, options) &&
                                filePathComparer(childFullName, Paths.NormalizePath(realpath(childFullName))) == Comparison.EqualTo
                                    ? childFullName
                                    : null;
                        })
                        : [],
                    parentWatcher.ChildWatches,
                    (child, childWatcher) => filePathComparer(child, childWatcher.DirName),
                    CreateAndAddChildDirectoryWatcher,
                    CloseFileWatcher,
                    // TS passes a one-parameter function here and lets the second argument go unused.
                    (childWatcher, _) => AddChildDirectoryWatcher(childWatcher));
            }
            else if (parentWatcher.TargetWatcher is not null &&
                filePathComparer(target, parentWatcher.TargetWatcher.DirName) == Comparison.EqualTo)
            {
                hasChanges = false;
                Debug.Assert(parentWatcher.ChildWatches.Count == 0);
            }
            else
            {
                CloseTargetWatcher(parentWatcher);
                parentWatcher.TargetWatcher = CreateDirectoryWatcher(target, options, /*callback*/ null, parentDir);
                foreach (var childWatcher in parentWatcher.ChildWatches) CloseFileWatcher(childWatcher);
                hasChanges = true;
            }

            parentWatcher.ChildWatches = newChildWatches ?? [];
            return hasChanges;

            /// <summary>Create new childDirectoryWatcher and add it to the new ChildDirectoryWatcher list.</summary>
            void CreateAndAddChildDirectoryWatcher(string childName)
            {
                var result = CreateDirectoryWatcher(childName, options);
                AddChildDirectoryWatcher(result);
            }

            /// <summary>Add child directory watcher to the new ChildDirectoryWatcher list.</summary>
            void AddChildDirectoryWatcher(IChildDirectoryWatcher childWatcher)
            {
                (newChildWatches ??= []).Add(childWatcher);
            }
        }

        bool IsIgnoredPath(string path, IWatchOptions? options)
            => Core.Some(SysCore.IgnoredPaths, searchPath => IsInPath(path, searchPath)) ||
                IsIgnoredByWatchOptions(path, options, useCaseSensitiveFileNames, getCurrentDirectory);

        bool IsInPath(string path, string searchPath)
        {
            if (path.Contains(searchPath, StringComparison.Ordinal)) return true;
            if (useCaseSensitiveFileNames) return false;
            return toCanonicalFilePath(path).Contains(searchPath, StringComparison.Ordinal);
        }
    }

    // ported from: src/compiler/sys.ts:381-455
    /// <summary>
    /// Watches a file by watching the directory that contains it, so that editors which save by renaming (and so
    /// briefly remove the file) are still observed.
    /// </summary>
    public static HostWatchFile CreateUseFsEventsOnParentDirectoryWatchFile(
        FsWatch fsWatch,
        bool useCaseSensitiveFileNames,
        Func<string, DateTimeOffset?> getModifiedTime,
        bool fsWatchWithTimestamp)
    {
        // One file can have multiple watchers
        var fileWatcherCallbacks = Core.CreateMultiMap<string, FileWatcherCallback>();
        var fileTimestamps = fsWatchWithTimestamp ? new Dictionary<string, DateTimeOffset>() : null;
        var dirWatchers = new Dictionary<string, ReferenceCountedWatcher>();
        var toCanonicalName = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames);
        return NonPollingWatchFile;

        IFileWatcher NonPollingWatchFile(
            string fileName,
            FileWatcherCallback callback,
            PollingInterval pollingInterval,
            IWatchOptions? fallbackOptions)
        {
            var filePath = toCanonicalName(fileName);
            if (fileWatcherCallbacks.Add(filePath, callback).Count == 1 && fileTimestamps is not null)
            {
                fileTimestamps[filePath] = getModifiedTime(fileName) ?? SysCore.MissingFileModifiedTime;
            }

            var fileNameDirectory = DirectoryOrCurrent(Paths.GetDirectoryPath(fileName));
            var dirPath = DirectoryOrCurrent(Paths.GetDirectoryPath(filePath));
            if (!dirWatchers.TryGetValue(dirPath, out var watcher))
            {
                watcher = CreateDirectoryWatcher(fileNameDirectory, dirPath, fallbackOptions);
            }

            watcher.ReferenceCount++;
            return new ActionFileWatcher(() =>
            {
                if (watcher.ReferenceCount == 1)
                {
                    watcher.Close();
                    dirWatchers.Remove(dirPath);
                }
                else
                {
                    watcher.ReferenceCount--;
                }

                fileWatcherCallbacks.Remove(filePath, callback);
            });
        }

        ReferenceCountedWatcher CreateDirectoryWatcher(string dirName, string dirPath, IWatchOptions? fallbackOptions)
        {
            var watcher = fsWatch(
                dirName,
                FileSystemEntryKind.Directory,
                (eventName, relativeFileName, _) =>
                {
                    // When files are deleted from disk, the triggered "rename" event would have a
                    // relativeFileName of "undefined"
                    if (relativeFileName is null) return;
                    var fileName = Paths.GetNormalizedAbsolutePath(relativeFileName, dirName);
                    var filePath = toCanonicalName(fileName);
                    // Some applications save a working file via rename operations
                    var callbacks = fileWatcherCallbacks.Get(filePath);
                    if (callbacks is not null)
                    {
                        DateTimeOffset? currentModifiedTime = null;
                        var eventKind = FileWatcherEventKind.Changed;
                        if (fileTimestamps is not null)
                        {
                            var existingTime = fileTimestamps[filePath];
                            if (eventName == FsWatchEventName.Change)
                            {
                                currentModifiedTime = getModifiedTime(fileName) ?? SysCore.MissingFileModifiedTime;
                                if (currentModifiedTime.Value.ToUnixTimeMilliseconds() == existingTime.ToUnixTimeMilliseconds()) return;
                            }

                            currentModifiedTime ??= getModifiedTime(fileName) ?? SysCore.MissingFileModifiedTime;
                            fileTimestamps[filePath] = currentModifiedTime.Value;
                            if (existingTime == SysCore.MissingFileModifiedTime) eventKind = FileWatcherEventKind.Created;
                            else if (currentModifiedTime == SysCore.MissingFileModifiedTime) eventKind = FileWatcherEventKind.Deleted;
                        }

                        foreach (var fileCallback in callbacks)
                        {
                            fileCallback(fileName, eventKind, currentModifiedTime);
                        }
                    }
                },
                /*recursive*/ false,
                PollingInterval.Medium,
                fallbackOptions);
            var counted = new ReferenceCountedWatcher(watcher);
            dirWatchers[dirPath] = counted;
            return counted;
        }

        /// <summary>TS writes <c>getDirectoryPath(x) || "."</c>.</summary>
        static string DirectoryOrCurrent(string directory) => Js.IsTruthy(directory) ? directory : ".";
    }

    /// <summary>ported from: src/compiler/sys.ts:965-987 — the host parameters of
    /// <see cref="CreateSystemWatchFunctions"/>.</summary>
    /// <remarks>
    /// TS passes an object literal; the port uses a class with <c>required</c> members, so a caller (or a test)
    /// can build one without declaring a host type.
    /// </remarks>
    public sealed class SystemWatchFunctionsHost
    {
        /// <summary>The polling watcher primitive (TS: <c>fsWatchFileWorker</c>).</summary>
        public required HostWatchFile PollingWatchFileWorker { get; init; }

        /// <summary>The modification time and timer host the polling watchers and child-watch scheduler use.
        /// </summary>
        public required IPollingWatchFileHost PollingWatchFileHost { get; init; }

        /// <summary>The native watcher, when the host has one (TS: <c>fsWatchWorker</c>).</summary>
        public required FsWatchWorker FsWatchWorker { get; init; }

        public required FileSystemEntryExists FileSystemEntryExists { get; init; }

        public required bool UseCaseSensitiveFileNames { get; init; }

        public required Func<string> GetCurrentDirectory { get; init; }

        public required bool FsSupportsRecursiveFsWatch { get; init; }

        public required Func<string, IList<string>> GetAccessibleSortedChildDirectories { get; init; }

        public required Func<string, string> Realpath { get; init; }

        // For backward compatibility environment variables
        public required string? TscWatchFile { get; init; }

        public bool UseNonPollingWatchers { get; init; }

        public required string? TscWatchDirectory { get; init; }

        public required bool InodeWatching { get; init; }

        public required bool FsWatchWithTimestamp { get; init; }

        public required Action<string> SysLog { get; init; }
    }

    /// <summary>ported from: src/compiler/sys.ts:1008 — TS: the <c>{ watchFile, watchDirectory }</c> return.
    /// </summary>
    public sealed class SystemWatchFunctions
    {
        public required HostWatchFile WatchFile { get; init; }

        public required HostWatchDirectory WatchDirectory { get; init; }
    }

    // ported from: src/compiler/sys.ts:990-1371
    /// <summary>
    /// Builds the compiler's <c>watchFile</c>/<c>watchDirectory</c> implementations from a host's primitives,
    /// choosing polling, native watching or a mix according to the watch options.
    /// </summary>
    public static SystemWatchFunctions CreateSystemWatchFunctions(SystemWatchFunctionsHost host)
    {
        var pollingWatches = new Dictionary<string, SingleFileWatcher<FileWatcherCallback>>();
        var fsWatches = new Dictionary<string, SingleFileWatcher<FsWatchCallback>>();
        var fsWatchesRecursive = new Dictionary<string, SingleFileWatcher<FsWatchCallback>>();
        HostWatchFile? dynamicPollingWatchFile = null;
        HostWatchFile? fixedChunkSizePollingWatchFile = null;
        HostWatchFile? nonPollingWatchFile = null;
        HostWatchDirectory? hostRecursiveDirectoryWatcher = null;
        var hitSystemWatcherLimit = false;
        return new SystemWatchFunctions { WatchFile = WatchFile, WatchDirectory = WatchDirectory };

        IFileWatcher WatchFile(string fileName, FileWatcherCallback callback, PollingInterval pollingInterval, IWatchOptions? options)
        {
            options = UpdateOptionsForWatchFile(options, host.UseNonPollingWatchers);
            var watchFileKind = Debug.CheckDefined(options.WatchFile);
            return watchFileKind switch
            {
                WatchFileKind.FixedPollingInterval
                    => PollingWatchFile(fileName, callback, PollingInterval.Low, /*options*/ null),
                WatchFileKind.PriorityPollingInterval
                    => PollingWatchFile(fileName, callback, pollingInterval, /*options*/ null),
                WatchFileKind.DynamicPriorityPolling
                    => EnsureDynamicPollingWatchFile()(fileName, callback, pollingInterval, /*options*/ null),
                WatchFileKind.FixedChunkSizePolling
                    => EnsureFixedChunkSizePollingWatchFile()(fileName, callback, /* pollingInterval */ PollingInterval.Low, /*options*/ null),
                WatchFileKind.UseFsEvents
                    => FsWatchFile(
                        fileName,
                        FileSystemEntryKind.File,
                        CreateFsWatchCallbackForFileWatcherCallback(fileName, callback, host.PollingWatchFileHost.GetModifiedTime),
                        /*recursive*/ false,
                        pollingInterval,
                        GetFallbackOptions(options)),
                WatchFileKind.UseFsEventsOnParentDirectory
                    => (nonPollingWatchFile ??= CreateUseFsEventsOnParentDirectoryWatchFile(
                            FsWatchFile,
                            host.UseCaseSensitiveFileNames,
                            host.PollingWatchFileHost.GetModifiedTime,
                            host.FsWatchWithTimestamp))(fileName, callback, pollingInterval, GetFallbackOptions(options)),
                _ => Debug.Fail<IFileWatcher>($"unexpected WatchFileKind {watchFileKind}"),
            };
        }

        HostWatchFile EnsureDynamicPollingWatchFile()
            => dynamicPollingWatchFile ??= CreateDynamicPriorityPollingWatchFile(host.PollingWatchFileHost);

        HostWatchFile EnsureFixedChunkSizePollingWatchFile()
            => fixedChunkSizePollingWatchFile ??= CreateFixedChunkSizePollingWatchFile(host.PollingWatchFileHost);

        IWatchOptions UpdateOptionsForWatchFile(IWatchOptions? options, bool useNonPollingWatchers)
        {
            if (options?.WatchFile is not null) return options;
            return host.TscWatchFile switch
            {
                // Use polling interval based on priority when create watch using host.watchFile
                "PriorityPollingInterval" => new WatchOptions { WatchFile = WatchFileKind.PriorityPollingInterval },
                // Use polling interval but change the interval depending on file changes and their default
                // polling interval
                "DynamicPriorityPolling" => new WatchOptions { WatchFile = WatchFileKind.DynamicPriorityPolling },
                // Use notifications from FS to watch with falling back to fs.watchFile
                "UseFsEvents" => GenerateWatchFileOptions(WatchFileKind.UseFsEvents, PollingWatchKind.PriorityInterval, options),
                // Use notifications from FS to watch with falling back to dynamic watch file
                "UseFsEventsWithFallbackDynamicPolling" => GenerateWatchFileOptions(WatchFileKind.UseFsEvents, PollingWatchKind.DynamicPriority, options),
                // "UseFsEventsOnParentDirectory" sets useNonPollingWatchers and then falls through to the
                // default below, so it is handled by the `_` arm together with "no override at all".
                _ => useNonPollingWatchers || host.TscWatchFile == "UseFsEventsOnParentDirectory"
                    // Use notifications from the parent directory, falling back to fs.watchFile
                    ? GenerateWatchFileOptions(WatchFileKind.UseFsEventsOnParentDirectory, PollingWatchKind.PriorityInterval, options)
                    // Default to using fs events
                    : new WatchOptions { WatchFile = WatchFileKind.UseFsEvents },
            };
        }

        IWatchOptions GenerateWatchFileOptions(WatchFileKind watchFile, PollingWatchKind fallbackPolling, IWatchOptions? options)
        {
            var defaultFallbackPolling = options?.FallbackPolling;
            return new WatchOptions
            {
                WatchFile = watchFile,
                FallbackPolling = defaultFallbackPolling ?? fallbackPolling,
            };
        }

        IFileWatcher WatchDirectory(string directoryName, DirectoryWatcherCallback callback, bool recursive, IWatchOptions? options)
        {
            if (host.FsSupportsRecursiveFsWatch)
            {
                return FsWatchFile(
                    directoryName,
                    FileSystemEntryKind.Directory,
                    CreateFsWatchCallbackForDirectoryWatcherCallback(
                        directoryName,
                        callback,
                        options,
                        host.UseCaseSensitiveFileNames,
                        host.GetCurrentDirectory),
                    recursive,
                    PollingInterval.Medium,
                    GetFallbackOptions(options));
            }

            hostRecursiveDirectoryWatcher ??= CreateDirectoryWatcherSupportingRecursive(new RecursiveDirectoryWatcherHost
            {
                UseCaseSensitiveFileNames = host.UseCaseSensitiveFileNames,
                GetCurrentDirectory = host.GetCurrentDirectory,
                FileSystemEntryExists = host.FileSystemEntryExists,
                GetAccessibleSortedChildDirectories = host.GetAccessibleSortedChildDirectories,
                WatchDirectory = NonRecursiveWatchDirectory,
                Realpath = host.Realpath,
                SetTimeout = host.PollingWatchFileHost.SetTimeout,
                ClearTimeout = host.PollingWatchFileHost.ClearTimeout,
            });
            return hostRecursiveDirectoryWatcher(directoryName, callback, recursive, options);
        }

        IFileWatcher NonRecursiveWatchDirectory(string directoryName, DirectoryWatcherCallback callback, bool recursive, IWatchOptions? options)
        {
            Debug.Assert(!recursive);
            var watchDirectoryOptions = UpdateOptionsForWatchDirectory(options);
            var watchDirectoryKind = Debug.CheckDefined(watchDirectoryOptions.WatchDirectory);
            return watchDirectoryKind switch
            {
                WatchDirectoryKind.FixedPollingInterval
                    => PollingWatchFile(directoryName, (_, _, _) => callback(directoryName), PollingInterval.Medium, /*options*/ null),
                WatchDirectoryKind.DynamicPriorityPolling
                    => EnsureDynamicPollingWatchFile()(directoryName, (_, _, _) => callback(directoryName), PollingInterval.Medium, /*options*/ null),
                WatchDirectoryKind.FixedChunkSizePolling
                    => EnsureFixedChunkSizePollingWatchFile()(directoryName, (_, _, _) => callback(directoryName), /* pollingInterval */ PollingInterval.Low, /*options*/ null),
                WatchDirectoryKind.UseFsEvents
                    => FsWatchFile(
                        directoryName,
                        FileSystemEntryKind.Directory,
                        CreateFsWatchCallbackForDirectoryWatcherCallback(
                            directoryName,
                            callback,
                            options,
                            host.UseCaseSensitiveFileNames,
                            host.GetCurrentDirectory),
                        recursive,
                        PollingInterval.Medium,
                        GetFallbackOptions(watchDirectoryOptions)),
                _ => Debug.Fail<IFileWatcher>($"unexpected WatchDirectoryKind {watchDirectoryKind}"),
            };
        }

        IWatchOptions UpdateOptionsForWatchDirectory(IWatchOptions? options)
        {
            if (options?.WatchDirectory is not null) return options;
            return host.TscWatchDirectory switch
            {
                // Use polling interval based on priority when create watch using host.watchFile
                "RecursiveDirectoryUsingFsWatchFile" => new WatchOptions { WatchDirectory = WatchDirectoryKind.FixedPollingInterval },
                // Use polling interval but change the interval depending on file changes and their default
                // polling interval
                "RecursiveDirectoryUsingDynamicPriorityPolling" => new WatchOptions { WatchDirectory = WatchDirectoryKind.DynamicPriorityPolling },
                _ => new WatchOptions
                {
                    WatchDirectory = WatchDirectoryKind.UseFsEvents,
                    FallbackPolling = options?.FallbackPolling,
                },
            };
        }

        IFileWatcher PollingWatchFile(string fileName, FileWatcherCallback callback, PollingInterval pollingInterval, IWatchOptions? options)
            => CreateSingleWatcherPerName(
                pollingWatches,
                host.UseCaseSensitiveFileNames,
                fileName,
                callback,
                cb => host.PollingWatchFileWorker(fileName, cb, pollingInterval, options));

        IFileWatcher FsWatchFile(
            string fileOrDirectory,
            FileSystemEntryKind entryKind,
            FsWatchCallback callback,
            bool recursive,
            PollingInterval fallbackPollingInterval,
            IWatchOptions? fallbackOptions)
            => CreateSingleWatcherPerName(
                recursive ? fsWatchesRecursive : fsWatches,
                host.UseCaseSensitiveFileNames,
                fileOrDirectory,
                callback,
                cb => FsWatchHandlingExistenceOnHost(fileOrDirectory, entryKind, cb, recursive, fallbackPollingInterval, fallbackOptions));

        IFileWatcher FsWatchHandlingExistenceOnHost(
            string fileOrDirectory,
            FileSystemEntryKind entryKind,
            FsWatchCallback callback,
            bool recursive,
            PollingInterval fallbackPollingInterval,
            IWatchOptions? fallbackOptions)
        {
            string? lastDirectoryPartWithDirectorySeparator = null;
            string? lastDirectoryPart = null;
            if (host.InodeWatching)
            {
                // TS: substring(lastIndexOf(sep)) — a negative index means "from the start", as in JS.
                var separatorIndex = fileOrDirectory.LastIndexOf(Paths.DirectorySeparator[0]);
                lastDirectoryPartWithDirectorySeparator = separatorIndex < 0 ? fileOrDirectory : fileOrDirectory[separatorIndex..];
                lastDirectoryPart = lastDirectoryPartWithDirectorySeparator[Paths.DirectorySeparator.Length..];
            }

            /// Watcher for the file system entry depending on whether it is missing or present.
            IFileWatcher? watcher = null;
            watcher = !host.FileSystemEntryExists(fileOrDirectory, entryKind)
                ? WatchMissingFileSystemEntry()
                : WatchPresentFileSystemEntry();
            return new ActionFileWatcher(() =>
            {
                // Close the watcher (either existing file system entry watcher or missing file system entry
                // watcher)
                if (watcher is not null)
                {
                    watcher.Close();
                    watcher = null;
                }
            });

            void UpdateWatcher(Func<IFileWatcher> createWatcher)
            {
                // If watcher is not closed, update it
                if (watcher is not null)
                {
                    host.SysLog($"sysLog:: {fileOrDirectory}:: Changing watcher to {(createWatcher == WatchPresentFileSystemEntry ? "Present" : "Missing")}FileSystemEntryWatcher");
                    watcher.Close();
                    watcher = createWatcher();
                }
            }

            /// <summary>
            /// Watches the file or directory that is currently present, switching to the missing-entry watcher
            /// when it is deleted.
            /// </summary>
            IFileWatcher WatchPresentFileSystemEntry()
            {
                if (hitSystemWatcherLimit)
                {
                    host.SysLog($"sysLog:: {fileOrDirectory}:: Defaulting to watchFile");
                    return WatchPresentFileSystemEntryWithFsWatchFile();
                }

                try
                {
                    // TS picks between the two watcher primitives inline; the port names the choice because a
                    // conditional expression has no target type to convert the method groups to. The inode
                    // callback also needs wrapping: TS lets a function with fewer parameters stand in for a
                    // callback with more.
                    FsWatchWorker worker = entryKind == FileSystemEntryKind.Directory || !host.FsWatchWithTimestamp
                        ? host.FsWatchWorker
                        : FsWatchWorkerHandlingTimestamp;
                    FsWatchCallback watchCallback = host.InodeWatching
                        ? (eventName, relativeFileName, _) => CallbackChangingToMissingFileSystemEntry(eventName, relativeFileName)
                        : callback;
                    var presentWatcher = worker(fileOrDirectory, recursive, watchCallback);

                    // Watch the missing file or directory or error
                    presentWatcher.On("error", () =>
                    {
                        callback(FsWatchEventName.Rename, "");
                        UpdateWatcher(WatchMissingFileSystemEntry);
                    });
                    return presentWatcher;
                }
                catch (Exception e)
                {
                    // Catch the exception and use polling instead
                    // Eg. on linux the number of watches are limited and one could easily exhaust watches and
                    // the exception ENOSPC is thrown when creating watcher at that point, so instead of throwing
                    // error, use fs.watchFile
                    hitSystemWatcherLimit = hitSystemWatcherLimit || SysCore.IsSystemWatcherLimitExceeded(e);
                    host.SysLog($"sysLog:: {fileOrDirectory}:: Changing to watchFile");
                    return WatchPresentFileSystemEntryWithFsWatchFile();
                }
            }

            void CallbackChangingToMissingFileSystemEntry(FsWatchEventName eventName, string? relativeName)
            {
                // In some scenarios, file save operation fires event with fileName.ext~ instead of fileName.ext
                // To ensure we see the file going missing and coming back up (file delete and then recreated)
                // and watches being updated correctly we are calling back with fileName.ext as well as
                // fileName.ext~. The worst is we have fired event that was not needed but we wont miss any
                // changes especially in cases where file goes missing and watches wrong inode.
                string? originalRelativeName = null;
                if (relativeName is not null && Core.EndsWith(relativeName, "~"))
                {
                    originalRelativeName = relativeName;
                    relativeName = relativeName[..^1];
                }

                // because relativeName is not guaranteed to be correct we need to check on each rename with few
                // combinations. Eg on ubuntu while watching app/node_modules the relativeName is "node_modules"
                // which is neither relative nor full path
                if (eventName == FsWatchEventName.Rename &&
                    (!Js.IsTruthy(relativeName) ||
                        relativeName == lastDirectoryPart ||
                        Core.EndsWith(relativeName, lastDirectoryPartWithDirectorySeparator!)))
                {
                    var modifiedTime = host.PollingWatchFileHost.GetModifiedTime(fileOrDirectory) ?? SysCore.MissingFileModifiedTime;
                    if (originalRelativeName is not null) callback(eventName, originalRelativeName, modifiedTime);
                    callback(eventName, relativeName, modifiedTime);
                    if (host.InodeWatching)
                    {
                        // If this was rename event, inode has changed means we need to update watcher
                        UpdateWatcher(modifiedTime == SysCore.MissingFileModifiedTime
                            ? WatchMissingFileSystemEntry
                            : WatchPresentFileSystemEntry);
                    }
                    else if (modifiedTime == SysCore.MissingFileModifiedTime)
                    {
                        UpdateWatcher(WatchMissingFileSystemEntry);
                    }
                }
                else
                {
                    if (originalRelativeName is not null) callback(eventName, originalRelativeName);
                    callback(eventName, relativeName);
                }
            }

            /// <summary>
            /// Watches the file or directory using fs.watchFile, since fs.watch threw (eg. on linux the number of
            /// watches is limited and one could easily exhaust watches, at which point ENOSPC is thrown).
            /// </summary>
            IFileWatcher WatchPresentFileSystemEntryWithFsWatchFile()
                => WatchFile(fileOrDirectory, CreateFileWatcherCallback(callback), fallbackPollingInterval, fallbackOptions);

            /// <summary>
            /// Watches the file or directory that is currently missing, switching to the present-entry watcher
            /// when it is created.
            /// </summary>
            IFileWatcher WatchMissingFileSystemEntry()
                => WatchFile(
                    fileOrDirectory,
                    (fileName, eventKind, modifiedTime) =>
                    {
                        if (eventKind == FileWatcherEventKind.Created)
                        {
                            modifiedTime ??= host.PollingWatchFileHost.GetModifiedTime(fileOrDirectory) ?? SysCore.MissingFileModifiedTime;
                            if (modifiedTime != SysCore.MissingFileModifiedTime)
                            {
                                callback(FsWatchEventName.Rename, "", modifiedTime);
                                // Call the callback for current file or directory
                                // For now it could be callback for the inner directory creation, but just return
                                // current directory, better than current no-op
                                UpdateWatcher(WatchPresentFileSystemEntry);
                            }
                        }
                    },
                    fallbackPollingInterval,
                    fallbackOptions);
        }

        IFsWatchWorkerWatcher FsWatchWorkerHandlingTimestamp(string fileOrDirectory, bool recursive, FsWatchCallback callback)
        {
            var modifiedTime = host.PollingWatchFileHost.GetModifiedTime(fileOrDirectory) ?? SysCore.MissingFileModifiedTime;
            return host.FsWatchWorker(fileOrDirectory, recursive, (eventName, relativeFileName, currentModifiedTime) =>
            {
                if (eventName == FsWatchEventName.Change)
                {
                    currentModifiedTime ??= host.PollingWatchFileHost.GetModifiedTime(fileOrDirectory) ?? SysCore.MissingFileModifiedTime;
                    if (currentModifiedTime.Value.ToUnixTimeMilliseconds() == modifiedTime.ToUnixTimeMilliseconds()) return;
                }

                modifiedTime = currentModifiedTime ?? host.PollingWatchFileHost.GetModifiedTime(fileOrDirectory) ?? SysCore.MissingFileModifiedTime;
                callback(eventName, relativeFileName, modifiedTime);
            });
        }
    }
}
