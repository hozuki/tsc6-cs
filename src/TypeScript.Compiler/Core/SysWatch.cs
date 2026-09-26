using System.Globalization;

namespace TypeScript.Compiler;

// ported from: src/compiler/sys.ts:88-560, 890-960, src/compiler/watchUtilities.ts:845-847
/// <summary>
/// The host-independent half of <c>sys.ts</c>: given a host that can only report modification times, watch
/// primitives and timers, it decides when a watched file or directory counts as changed.
/// </summary>
/// <remarks>
/// TS keeps all of this in <c>sys.ts</c> as module-private functions plus a handful of exported values; the port
/// gathers them into one static class, split across the <c>SysWatch.*.cs</c> files by subject.
/// <para>
/// PORT-NOTE: three of TS's shapes have no direct C# counterpart and are adapted here:
/// <list type="number">
/// <item><c>queue[pollIndex] = undefined</c> punches holes into an array that a later loop compacts; the port
/// uses <c>List&lt;T?&gt;</c> with <c>null</c> holes and <c>RemoveRange</c> for TS's <c>queue.length = n</c>.</item>
/// <item>TS gives a watcher an extra property by assigning it (<c>const w = fsWatch(…) as DirectoryWatcher;
/// w.referenceCount = 0</c>); a C# watcher cannot grow a member, so <see cref="ReferenceCountedWatcher"/> wraps
/// it instead.</item>
/// <item><c>setCustomLevels</c> is handed the <c>PollingInterval</c> enum object and assigns to its members, so
/// <c>TSC_WATCH_POLLINGINTERVAL_*</c> changes the enum's <em>values</em>. A C# enum has no mutable members, so
/// the effective durations are kept as <see cref="Levels"/> and read through
/// <see cref="PollingIntervalMilliseconds"/>.</item>
/// </list>
/// </para>
/// </remarks>
internal static partial class SysWatch
{
    // ---------------------------------------------------------------------------------------------
    // Watched files
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sys.ts:88-92 — TS: <c>WatchedFile</c>.</summary>
    public class WatchedFile
    {
        public required string FileName { get; init; }

        public required FileWatcherCallback Callback { get; init; }

        public DateTimeOffset Mtime { get; set; }
    }

    /// <summary>ported from: src/compiler/sys.ts:182-184 — TS: <c>WatchedFileWithIsClosed</c>.</summary>
    public class WatchedFileWithIsClosed : WatchedFile
    {
        public bool IsClosed { get; set; }
    }

    /// <summary>ported from: src/compiler/sys.ts:241-243 — TS: <c>WatchedFileWithUnchangedPolls</c>.</summary>
    public sealed class WatchedFileWithUnchangedPolls : WatchedFileWithIsClosed
    {
        public int UnchangedPolls { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Watchers as objects
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// An <see cref="IFileWatcher"/> built from a close action — TS's ubiquitous <c>{ close: () =&gt; … }</c>
    /// object literal.
    /// </summary>
    public sealed class ActionFileWatcher(Action close) : IFileWatcher
    {
        public void Close() => close();
    }

    /// <summary>
    /// Wraps a watcher so it can be reference counted.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS assigns <c>referenceCount</c> onto the object <c>fsWatch</c> returned (<c>as
    /// DirectoryWatcher</c>); a C# watcher cannot grow a member, so it is wrapped instead.
    /// </remarks>
    public sealed class ReferenceCountedWatcher(IFileWatcher inner) : IDirectoryWatcher
    {
        public int ReferenceCount { get; set; }

        public void Close() => inner.Close();
    }

    // ported from: src/compiler/watchUtilities.ts:845-847
    /// <summary>Calls <see cref="IFileWatcher.Close"/> on a watcher.</summary>
    public static void CloseFileWatcher(IFileWatcher watcher) => watcher.Close();

    /// <summary>
    /// A concrete <see cref="IWatchOptions"/>.
    /// </summary>
    /// <remarks>
    /// TS builds watch options as object literals all over <c>sys.ts</c>; C# needs a constructible type, since
    /// the generated <see cref="IWatchOptions"/> is an interface.
    /// </remarks>
    public sealed class WatchOptions : IWatchOptions
    {
        public WatchFileKind? WatchFile { get; set; }

        public WatchDirectoryKind? WatchDirectory { get; set; }

        public PollingWatchKind? FallbackPolling { get; set; }

        public bool? SynchronousWatchDirectory { get; set; }

        public List<string>? ExcludeDirectories { get; set; }

        public List<string>? ExcludeFiles { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Watch primitives the host supplies
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sys.ts:891 — TS: the literal union <c>"rename" | "change"</c>.</summary>
    public enum FsWatchEventName
    {
        Rename,
        Change,
    }

    /// <summary>ported from: src/compiler/sys.ts:891 — TS: <c>FsWatchCallback</c>.</summary>
    public delegate void FsWatchCallback(FsWatchEventName eventName, string? relativeFileName, DateTimeOffset? modifiedTime = null);

    /// <summary>ported from: src/compiler/sys.ts:893 — TS: <c>FsWatch</c>.</summary>
    public delegate IFileWatcher FsWatch(
        string fileOrDirectory,
        FileSystemEntryKind entryKind,
        FsWatchCallback callback,
        bool recursive,
        PollingInterval fallbackPollingInterval,
        IWatchOptions? fallbackOptions);

    /// <summary>ported from: src/compiler/sys.ts:895-897 — TS: <c>FsWatchWorkerWatcher</c>.</summary>
    public interface IFsWatchWorkerWatcher : IFileWatcher
    {
        void On(string eventName, Action listener);
    }

    /// <summary>ported from: src/compiler/sys.ts:899 — TS: <c>FsWatchWorker</c>.</summary>
    public delegate IFsWatchWorkerWatcher FsWatchWorker(string fileOrDirectory, bool recursive, FsWatchCallback callback);

    /// <summary>ported from: src/compiler/sys.ts:962 — TS: <c>FileSystemEntryExists</c>.</summary>
    public delegate bool FileSystemEntryExists(string fileOrDirectory, FileSystemEntryKind entryKind);

    /// <summary>ported from: src/compiler/sys.ts:244-247 — the <c>{ getModifiedTime, setTimeout }</c> host the
    /// polling watchers are built on, plus the timer the recursive directory watcher needs.</summary>
    public interface IPollingWatchFileHost : IModifiedTimeHost
    {
        /// <returns>An opaque handle, as in TS's <c>System.setTimeout</c>.</returns>
        object? SetTimeout(Action callback, int ms);

        void ClearTimeout(object? timeoutId);
    }

    // ---------------------------------------------------------------------------------------------
    // Polling intervals
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sys.ts:114-118 — TS: <c>Levels</c>.</summary>
    public sealed class Levels
    {
        public int Low { get; set; }

        public int Medium { get; set; }

        public int High { get; set; }

        /// <summary>
        /// Indexes the levels by TS's property names, which is how the environment overrides address them.
        /// </summary>
        public int this[string level]
        {
            get => level switch
            {
                "Low" => Low,
                "Medium" => Medium,
                _ => High,
            };
            set
            {
                switch (level)
                {
                    case "Low": Low = value; break;
                    case "Medium": Medium = value; break;
                    default: High = value; break;
                }
            }
        }
    }

    // ported from: src/compiler/sys.ts:120-126
    private static Dictionary<PollingInterval, int> CreatePollingIntervalBasedLevels(Levels levels) => new()
    {
        [PollingInterval.Low] = levels.Low,
        [PollingInterval.Medium] = levels.Medium,
        [PollingInterval.High] = levels.High,
    };

    // ported from: src/compiler/sys.ts:128-131
    private static readonly Levels s_defaultChunkLevels = new() { Low = 32, Medium = 64, High = 256 };

    private static Dictionary<PollingInterval, int> s_pollingChunkSize = CreatePollingIntervalBasedLevels(s_defaultChunkLevels);

    private static Dictionary<PollingInterval, int> s_unchangedPollThresholds = CreatePollingIntervalBasedLevels(s_defaultChunkLevels);

    /// <summary>ported from: src/compiler/sys.ts:131 — TS exports this as <c>unchangedPollThresholds</c>.</summary>
    public static IReadOnlyDictionary<PollingInterval, int> UnchangedPollThresholds => s_unchangedPollThresholds;

    /// <summary>
    /// The effective durations of the polling intervals.
    /// </summary>
    /// <remarks>
    /// PORT-NOTE: TS reads the duration straight off the enum (<c>setTimeout(cb, PollingInterval.High)</c>) and
    /// lets <c>setCustomLevels</c> rewrite those enum members from <c>TSC_WATCH_POLLINGINTERVAL_*</c>. C# enums
    /// are immutable, so the overridable durations live here and every use site asks
    /// <see cref="PollingIntervalMilliseconds"/>.
    /// </remarks>
    private static Levels s_pollingIntervalMilliseconds = new()
    {
        Low = (int)PollingInterval.Low,
        Medium = (int)PollingInterval.Medium,
        High = (int)PollingInterval.High,
    };

    public static int PollingIntervalMilliseconds(PollingInterval interval) => interval switch
    {
        PollingInterval.Low => s_pollingIntervalMilliseconds.Low,
        PollingInterval.Medium => s_pollingIntervalMilliseconds.Medium,
        _ => s_pollingIntervalMilliseconds.High,
    };

    private static readonly string[] s_levelNames = ["Low", "Medium", "High"];

    // ported from: src/compiler/sys.ts:133-180
    /// <summary>
    /// Applies the <c>TSC_WATCH_POLLINGINTERVAL_*</c>, <c>TSC_WATCH_POLLINGCHUNKSIZE_*</c> and
    /// <c>TSC_WATCH_UNCHANGEDPOLLTHRESHOLDS_*</c> environment overrides.
    /// </summary>
    public static void SetCustomPollingValues(ISystem system)
    {
        var pollingIntervalChanged = SetCustomLevels("TSC_WATCH_POLLINGINTERVAL", s_pollingIntervalMilliseconds);
        s_pollingChunkSize = GetCustomPollingBasedLevels("TSC_WATCH_POLLINGCHUNKSIZE", s_defaultChunkLevels) ?? s_pollingChunkSize;
        s_unchangedPollThresholds = GetCustomPollingBasedLevels("TSC_WATCH_UNCHANGEDPOLLTHRESHOLDS", s_defaultChunkLevels) ?? s_unchangedPollThresholds;

        return;

        Levels? GetCustomLevels(string baseVariable)
        {
            Levels? customLevels = null;
            foreach (var level in s_levelNames)
            {
                var customLevel = system.GetEnvironmentVariable($"{baseVariable}_{level.ToUpperInvariant()}");
                if (Js.IsTruthy(customLevel))
                {
                    (customLevels ??= new Levels())[level] = Number(customLevel!);
                }
            }

            return customLevels;
        }

        bool SetCustomLevels(string baseVariable, Levels levels)
        {
            var customLevels = GetCustomLevels(baseVariable);
            if (customLevels is null) return false;

            foreach (var level in s_levelNames)
            {
                levels[level] = customLevels[level] != 0 ? customLevels[level] : levels[level];
            }

            return true;
        }

        Dictionary<PollingInterval, int>? GetCustomPollingBasedLevels(string baseVariable, Levels defaultLevels)
        {
            var customLevels = GetCustomLevels(baseVariable);
            if (!pollingIntervalChanged && customLevels is null) return null;

            return CreatePollingIntervalBasedLevels(customLevels is null ? defaultLevels : MergeLevels(defaultLevels, customLevels));
        }
    }

    /// <summary>
    /// Overlays the levels that were actually set on top of the defaults.
    /// </summary>
    /// <remarks>
    /// TS spreads a partial object over the defaults (<c>{ ...defaultLevels, ...customLevels }</c>), so an unset
    /// level keeps its default. The port's <see cref="Levels"/> always carries all three, with <c>0</c> standing
    /// for "not set" — which is also how JS behaves, since <c>customLevels[level] || levels[level]</c> falls back
    /// for a <c>NaN</c>.
    /// </remarks>
    private static Levels MergeLevels(Levels defaultLevels, Levels customLevels) => new()
    {
        Low = customLevels.Low != 0 ? customLevels.Low : defaultLevels.Low,
        Medium = customLevels.Medium != 0 ? customLevels.Medium : defaultLevels.Medium,
        High = customLevels.High != 0 ? customLevels.High : defaultLevels.High,
    };

    /// <summary>TS's <c>Number(text)</c>, degrading to <c>0</c> (which reads as "not set") when unparsable —
    /// JS would produce <c>NaN</c>, which is falsy and degrades the same way.</summary>
    private static int Number(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    // ---------------------------------------------------------------------------------------------
    // Polling a queue of watched files
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/sys.ts:185-239
    /// <summary>
    /// Polls up to <paramref name="chunkSize"/> live entries of <paramref name="queue"/> starting at
    /// <paramref name="pollIndex"/>, dropping closed watchers and compacting the holes it leaves behind.
    /// </summary>
    /// <returns>The next poll index.</returns>
    public static int PollWatchedFileQueue<T>(
        IModifiedTimeHost host,
        List<T?> queue,
        int pollIndex,
        int chunkSize,
        Action<T, int, bool>? callbackOnWatchFileStat = null)
        where T : WatchedFileWithIsClosed
    {
        var definedValueCopyToIndex = pollIndex;
        // Max visit would be all elements of the queue
        for (var canVisit = queue.Count; chunkSize != 0 && canVisit != 0; NextPollIndex(), canVisit--)
        {
            var watchedFile = queue[pollIndex];
            if (watchedFile is null)
            {
                continue;
            }

            if (watchedFile.IsClosed)
            {
                queue[pollIndex] = null;
                continue;
            }

            // Only files polled count towards chunkSize
            chunkSize--;
            var fileChanged = OnWatchedFileStat(watchedFile, SysCore.GetModifiedTime(host, watchedFile.FileName));
            if (watchedFile.IsClosed)
            {
                // Closed watcher as part of callback
                queue[pollIndex] = null;
                continue;
            }

            callbackOnWatchFileStat?.Invoke(watchedFile, pollIndex, fileChanged);
            // Defragment the queue while we are at it
            if (queue[pollIndex] is not null)
            {
                // Copy this file to the non hole location
                if (definedValueCopyToIndex < pollIndex)
                {
                    queue[definedValueCopyToIndex] = watchedFile;
                    queue[pollIndex] = null;
                }

                definedValueCopyToIndex++;
            }
        }

        // Return next poll index
        return pollIndex;

        void NextPollIndex()
        {
            pollIndex++;
            if (pollIndex == queue.Count)
            {
                if (definedValueCopyToIndex < pollIndex)
                {
                    // There are holes from definedValueCopyToIndex to end of queue, change queue size
                    queue.RemoveRange(definedValueCopyToIndex, queue.Count - definedValueCopyToIndex);
                }

                pollIndex = 0;
                definedValueCopyToIndex = 0;
            }
        }
    }

    // ported from: src/compiler/sys.ts:536-550
    /// <summary>Returns true if the file status changed.</summary>
    private static bool OnWatchedFileStat(WatchedFile watchedFile, DateTimeOffset modifiedTime)
    {
        var oldTime = watchedFile.Mtime.ToUnixTimeMilliseconds();
        var newTime = modifiedTime.ToUnixTimeMilliseconds();
        if (oldTime != newTime)
        {
            watchedFile.Mtime = modifiedTime;
            // Pass modified times so tsc --build can use it
            watchedFile.Callback(watchedFile.FileName, SysCore.GetFileWatcherEventKind(oldTime, newTime), modifiedTime);
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Dynamic priority polling
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sys.ts:248-252 — TS: <c>PollingIntervalQueue</c>, an array carrying
    /// three extra properties.</summary>
    private sealed class PollingIntervalQueue(PollingInterval pollingInterval)
    {
        public PollingInterval PollingInterval { get; } = pollingInterval;

        public int PollIndex { get; set; }

        public bool PollScheduled { get; set; }

        public List<WatchedFileWithUnchangedPolls?> Items { get; } = [];
    }

    // ported from: src/compiler/sys.ts:244-379
    /// <summary>
    /// Watches files by polling each one at an interval that follows the file: files that keep changing are
    /// polled more often, files that stop changing less often.
    /// </summary>
    public static HostWatchFile CreateDynamicPriorityPollingWatchFile(IPollingWatchFileHost host)
    {
        var watchedFiles = new List<WatchedFileWithUnchangedPolls>();
        var changedFilesInLastPoll = new List<WatchedFileWithUnchangedPolls?>();
        var lowPollingIntervalQueue = new PollingIntervalQueue(PollingInterval.Low);
        var mediumPollingIntervalQueue = new PollingIntervalQueue(PollingInterval.Medium);
        var highPollingIntervalQueue = new PollingIntervalQueue(PollingInterval.High);
        return WatchFile;

        IFileWatcher WatchFile(string fileName, FileWatcherCallback callback, PollingInterval defaultPollingInterval, IWatchOptions? options)
        {
            var file = new WatchedFileWithUnchangedPolls
            {
                FileName = fileName,
                Callback = callback,
                UnchangedPolls = 0,
                Mtime = SysCore.GetModifiedTime(host, fileName),
            };
            watchedFiles.Add(file);

            AddToPollingIntervalQueue(file, defaultPollingInterval);
            return new ActionFileWatcher(() =>
            {
                file.IsClosed = true;
                // Remove from watchedFiles
                Core.UnorderedRemoveItem(watchedFiles, file);
                // Do not update polling interval queue since that will happen as part of polling
            });
        }

        void PollPollingIntervalQueue(PollingIntervalQueue queue)
        {
            queue.PollIndex = PollQueue(queue.Items, queue.PollingInterval, queue.PollIndex, s_pollingChunkSize[queue.PollingInterval]);
            // Set the next polling index and timeout
            if (queue.Items.Count != 0)
            {
                ScheduleNextPoll(queue.PollingInterval);
            }
            else
            {
                Debug.Assert(queue.PollIndex == 0);
                queue.PollScheduled = false;
            }
        }

        void PollLowPollingIntervalQueue(PollingIntervalQueue queue)
        {
            // Always poll complete list of changedFilesInLastPoll
            PollQueue(changedFilesInLastPoll, PollingInterval.Low, /*pollIndex*/ 0, changedFilesInLastPoll.Count);

            // Finally do the actual polling of the queue
            PollPollingIntervalQueue(queue);
            // Schedule poll if there are files in changedFilesInLastPoll but no files in the actual queue
            // as PollPollingIntervalQueue wont schedule for next poll
            if (!queue.PollScheduled && changedFilesInLastPoll.Count != 0)
            {
                ScheduleNextPoll(PollingInterval.Low);
            }
        }

        int PollQueue(List<WatchedFileWithUnchangedPolls?> queue, PollingInterval pollingInterval, int pollIndex, int chunkSize)
        {
            return PollWatchedFileQueue(host, queue, pollIndex, chunkSize, OnWatchFileStat);

            void OnWatchFileStat(WatchedFileWithUnchangedPolls watchedFile, int pollIndex, bool fileChanged)
            {
                if (fileChanged)
                {
                    watchedFile.UnchangedPolls = 0;
                    // Changed files go to changedFilesInLastPoll queue
                    if (!ReferenceEquals(queue, changedFilesInLastPoll))
                    {
                        queue[pollIndex] = null;
                        AddChangedFileToLowPollingIntervalQueue(watchedFile);
                    }
                }
                else if (watchedFile.UnchangedPolls != s_unchangedPollThresholds[pollingInterval])
                {
                    watchedFile.UnchangedPolls++;
                }
                else if (ReferenceEquals(queue, changedFilesInLastPoll))
                {
                    // Restart unchangedPollCount for unchanged file and move to low polling interval queue
                    watchedFile.UnchangedPolls = 1;
                    queue[pollIndex] = null;
                    AddToPollingIntervalQueue(watchedFile, PollingInterval.Low);
                }
                else if (pollingInterval != PollingInterval.High)
                {
                    watchedFile.UnchangedPolls++;
                    queue[pollIndex] = null;
                    AddToPollingIntervalQueue(
                        watchedFile,
                        pollingInterval == PollingInterval.Low ? PollingInterval.Medium : PollingInterval.High);
                }
            }
        }

        PollingIntervalQueue PollingIntervalQueueFor(PollingInterval pollingInterval) => pollingInterval switch
        {
            PollingInterval.Low => lowPollingIntervalQueue,
            PollingInterval.Medium => mediumPollingIntervalQueue,
            _ => highPollingIntervalQueue,
        };

        void AddToPollingIntervalQueue(WatchedFileWithUnchangedPolls file, PollingInterval pollingInterval)
        {
            PollingIntervalQueueFor(pollingInterval).Items.Add(file);
            ScheduleNextPollIfNotAlreadyScheduled(pollingInterval);
        }

        void AddChangedFileToLowPollingIntervalQueue(WatchedFileWithUnchangedPolls file)
        {
            changedFilesInLastPoll.Add(file);
            ScheduleNextPollIfNotAlreadyScheduled(PollingInterval.Low);
        }

        void ScheduleNextPollIfNotAlreadyScheduled(PollingInterval pollingInterval)
        {
            if (!PollingIntervalQueueFor(pollingInterval).PollScheduled)
            {
                ScheduleNextPoll(pollingInterval);
            }
        }

        void ScheduleNextPoll(PollingInterval pollingInterval)
        {
            var queue = PollingIntervalQueueFor(pollingInterval);
            queue.PollScheduled = true;
            host.SetTimeout(
                pollingInterval == PollingInterval.Low
                    ? () => PollLowPollingIntervalQueue(queue)
                    : () => PollPollingIntervalQueue(queue),
                PollingIntervalMilliseconds(pollingInterval));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Fixed chunk size polling
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/sys.ts:457-492
    /// <summary>Polls every watched file once per interval, in chunks, regardless of how often each changes.
    /// </summary>
    public static HostWatchFile CreateFixedChunkSizePollingWatchFile(IPollingWatchFileHost host)
    {
        var watchedFiles = new List<WatchedFileWithIsClosed?>();
        var pollIndex = 0;
        object? pollScheduled = null;
        return WatchFile;

        IFileWatcher WatchFile(string fileName, FileWatcherCallback callback, PollingInterval pollingInterval, IWatchOptions? options)
        {
            var file = new WatchedFileWithIsClosed
            {
                FileName = fileName,
                Callback = callback,
                Mtime = SysCore.GetModifiedTime(host, fileName),
            };
            watchedFiles.Add(file);
            ScheduleNextPoll();
            return new ActionFileWatcher(() =>
            {
                file.IsClosed = true;
                Core.UnorderedRemoveItem(watchedFiles, file);
            });
        }

        void PollQueue()
        {
            pollScheduled = null;
            pollIndex = PollWatchedFileQueue(host, watchedFiles, pollIndex, s_pollingChunkSize[PollingInterval.Low]);
            ScheduleNextPoll();
        }

        void ScheduleNextPoll()
        {
            if (watchedFiles.Count == 0 || pollScheduled is not null) return;
            pollScheduled = host.SetTimeout(PollQueue, PollingIntervalMilliseconds(PollingInterval.High));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // One watcher per canonical name
    // ---------------------------------------------------------------------------------------------

    /// <summary>ported from: src/compiler/sys.ts:494-497 — TS: <c>SingleFileWatcher&lt;T&gt;</c>.</summary>
    public sealed class SingleFileWatcher<T>
        where T : Delegate
    {
        public SingleFileWatcher(T callback, Func<T, IFileWatcher> createWatcher)
        {
            Callbacks.Add(callback);
            Watcher = createWatcher(CreateForwarder(this));
        }

        public IFileWatcher Watcher { get; }

        public List<T> Callbacks { get; } = [];

        /// <summary>
        /// The callback handed to the underlying watcher: it re-reads the callback list per event, so a later
        /// registration takes effect without re-creating the watcher.
        /// </summary>
        private static T CreateForwarder(SingleFileWatcher<T> state)
        {
            if (typeof(T) == typeof(FileWatcherCallback))
            {
                return (T)(object)new FileWatcherCallback((fileName, eventKind, modifiedTime) =>
                {
                    foreach (var callback in state.Callbacks.ToArray())
                    {
                        ((FileWatcherCallback)(Delegate)callback)(fileName, eventKind, modifiedTime);
                    }
                });
            }

            if (typeof(T) == typeof(FsWatchCallback))
            {
                return (T)(object)new FsWatchCallback((eventName, relativeFileName, modifiedTime) =>
                {
                    foreach (var callback in state.Callbacks.ToArray())
                    {
                        ((FsWatchCallback)(Delegate)callback)(eventName, relativeFileName, modifiedTime);
                    }
                });
            }

            return Debug.Fail<T>($"unexpected watcher callback type {typeof(T).Name}");
        }
    }

    // ported from: src/compiler/sys.ts:498-534
    /// <summary>
    /// Reuses one host watcher for every callback registered under the same canonical name, and closes it once
    /// the last of them goes away.
    /// </summary>
    public static IFileWatcher CreateSingleWatcherPerName<T>(
        Dictionary<string, SingleFileWatcher<T>> cache,
        bool useCaseSensitiveFileNames,
        string name,
        T callback,
        Func<T, IFileWatcher> createWatcher)
        where T : Delegate
    {
        var toCanonicalFileName = Core.CreateGetCanonicalFileName(useCaseSensitiveFileNames);
        var path = toCanonicalFileName(name);
        if (cache.TryGetValue(path, out var existing))
        {
            existing.Callbacks.Add(callback);
        }
        else
        {
            cache[path] = new SingleFileWatcher<T>(callback, createWatcher);
        }

        return new ActionFileWatcher(() =>
        {
            var watcher = cache.GetValueOrDefault(path);
            // Watcher is not expected to be undefined, but if it is normally its because
            // exception was thrown somewhere else and watch state is not what it should be
            if (watcher is null) return;
            if (!Core.OrderedRemoveItem(watcher.Callbacks, callback) || watcher.Callbacks.Count != 0) return;
            cache.Remove(path);
            CloseFileWatcher(watcher.Watcher);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Callback adapters
    // ---------------------------------------------------------------------------------------------

    // ported from: src/compiler/sys.ts:906-908
    private static FileWatcherCallback CreateFileWatcherCallback(FsWatchCallback callback)
        => (fileName, eventKind, modifiedTime) => callback(
            eventKind == FileWatcherEventKind.Changed ? FsWatchEventName.Change : FsWatchEventName.Rename,
            "",
            modifiedTime);

    // ported from: src/compiler/sys.ts:910-926
    private static FsWatchCallback CreateFsWatchCallbackForFileWatcherCallback(
        string fileName,
        FileWatcherCallback callback,
        Func<string, DateTimeOffset?> getModifiedTime)
        => (eventName, relativeFileName, modifiedTime) =>
        {
            if (eventName == FsWatchEventName.Rename)
            {
                // Check time stamps rather than file system entry checks
                modifiedTime ??= getModifiedTime(fileName) ?? SysCore.MissingFileModifiedTime;
                callback(
                    fileName,
                    modifiedTime != SysCore.MissingFileModifiedTime ? FileWatcherEventKind.Created : FileWatcherEventKind.Deleted,
                    modifiedTime);
            }
            else
            {
                // Change
                callback(fileName, FileWatcherEventKind.Changed, modifiedTime);
            }
        };

    // ported from: src/compiler/sys.ts:928-938
    private static bool IsIgnoredByWatchOptions(
        string pathToCheck,
        IWatchOptions? options,
        bool useCaseSensitiveFileNames,
        Func<string> getCurrentDirectory)
        => (options?.ExcludeDirectories is not null || options?.ExcludeFiles is not null) && (
            Wildcard.MatchesExclude(pathToCheck, options.ExcludeFiles, useCaseSensitiveFileNames, getCurrentDirectory()) ||
            Wildcard.MatchesExclude(pathToCheck, options.ExcludeDirectories, useCaseSensitiveFileNames, getCurrentDirectory()));

    // ported from: src/compiler/sys.ts:940-959
    private static FsWatchCallback CreateFsWatchCallbackForDirectoryWatcherCallback(
        string directoryName,
        DirectoryWatcherCallback callback,
        IWatchOptions? options,
        bool useCaseSensitiveFileNames,
        Func<string> getCurrentDirectory)
        => (eventName, relativeFileName, modifiedTime) =>
        {
            // In watchDirectory we only care about adding and removing files (when event name is
            // "rename"); changes made within files are handled by corresponding fileWatchers (when
            // event name is "change")
            if (eventName == FsWatchEventName.Rename)
            {
                // When deleting a file, the passed baseFileName is null
                var fileName = !Js.IsTruthy(relativeFileName)
                    ? directoryName
                    : Paths.NormalizePath(Paths.CombinePaths(directoryName, relativeFileName));
                if (!Js.IsTruthy(relativeFileName) ||
                    !IsIgnoredByWatchOptions(fileName, options, useCaseSensitiveFileNames, getCurrentDirectory))
                {
                    callback(fileName);
                }
            }
        };
}
