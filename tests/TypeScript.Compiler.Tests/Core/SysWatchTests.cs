using System.Globalization;

namespace TypeScript.Compiler.Tests;

/// <summary>
/// sys.ts 的 watch 机制：轮询队列、动态/固定优先级轮询、按名字复用 watcher、选项解析。
/// </summary>
/// <remarks>
/// 这些测试直接驱动 <c>SysWatch</c>：修改时间由 <see cref="FakePollingWatchFileHost"/> 控制，计时器只在测试
/// 调用 <see cref="FakePollingWatchFileHost.RunPendingTimers"/> 时触发，所以每一步轮询都是显式的、可断言的。
/// <para>
/// 本类里有测试会改 <c>SysWatch</c> 的全局轮询参数（它们来自环境变量），并在同一个测试里设回默认值；
/// xUnit 保证同一测试类内的用例串行执行，因此不会波及其它用例。
/// </para>
/// </remarks>
public class SysWatchTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeMilliseconds(1_000);
    private static readonly DateTimeOffset T1 = DateTimeOffset.FromUnixTimeMilliseconds(2_000);

    private static SysWatch.WatchedFileWithIsClosed WatchedFile(string fileName, DateTimeOffset mtime, List<string> seen)
        => new()
        {
            FileName = fileName,
            Callback = (name, kind, _) => seen.Add($"{name}:{kind}"),
            Mtime = mtime,
        };

    // ---------------------------------------------------------------------------------------------
    // Polling a queue
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PollWatchedFileQueue_ReportsOnlyChangedFiles()
    {
        var host = new FakePollingWatchFileHost();
        var seen = new List<string>();
        var queue = new List<SysWatch.WatchedFileWithIsClosed?>
        {
            WatchedFile("/a.ts", T0, seen),
            WatchedFile("/b.ts", T0, seen),
        };
        host.SetModifiedTime("/a.ts", T0);
        host.SetModifiedTime("/b.ts", T1);

        var nextIndex = SysWatch.PollWatchedFileQueue(host, queue, 0, chunkSize: 2);

        Assert.Equal(["/b.ts:Changed"], seen);
        // Both entries were visited, so the index wrapped back to the start.
        Assert.Equal(0, nextIndex);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void PollWatchedFileQueue_RemovesClosedWatchersAndCompactsTheQueue()
    {
        var host = new FakePollingWatchFileHost();
        var seen = new List<string>();
        var closed = WatchedFile("/a.ts", T0, seen);
        closed.IsClosed = true;
        var queue = new List<SysWatch.WatchedFileWithIsClosed?>
        {
            closed,
            WatchedFile("/b.ts", T0, seen),
            WatchedFile("/c.ts", T0, seen),
        };
        host.SetModifiedTime("/a.ts", T0);
        host.SetModifiedTime("/b.ts", T0);
        host.SetModifiedTime("/c.ts", T0);

        var nextIndex = SysWatch.PollWatchedFileQueue(host, queue, 0, chunkSize: 3);

        Assert.Empty(seen);
        Assert.Equal(["/b.ts", "/c.ts"], queue.Select(file => file!.FileName));
        Assert.Equal(0, nextIndex);
    }

    [Fact]
    public void PollWatchedFileQueue_VisitsAtMostChunkSizeFiles()
    {
        var host = new FakePollingWatchFileHost();
        var seen = new List<string>();
        var queue = new List<SysWatch.WatchedFileWithIsClosed?>();
        foreach (var name in new[] { "/a.ts", "/b.ts", "/c.ts" })
        {
            queue.Add(WatchedFile(name, T0, seen));
            host.SetModifiedTime(name, T1);
        }

        var polled = 0;
        var nextIndex = SysWatch.PollWatchedFileQueue(host, queue, 0, chunkSize: 1, (_, _, _) => polled++);

        Assert.Equal(1, polled);
        Assert.Single(seen);
        // The next poll resumes at the file after the one just visited.
        Assert.Equal(1, nextIndex);
    }

    // ---------------------------------------------------------------------------------------------
    // Polling watchers
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DynamicPriorityPolling_ReportsAChangeOnceAndStopsAfterClose()
    {
        var host = new FakePollingWatchFileHost();
        host.SetModifiedTime("/a.ts", T0);
        var watchFile = SysWatch.CreateDynamicPriorityPollingWatchFile(host);
        var seen = new List<(string FileName, FileWatcherEventKind Kind)>();
        var watcher = watchFile("/a.ts", (name, kind, _) => seen.Add((name, kind)), PollingInterval.High, null);

        // Registering schedules the first poll at the requested interval.
        Assert.Equal([(int)PollingInterval.High], host.TimerIntervals);

        host.SetModifiedTime("/a.ts", T1);
        host.RunPendingTimers();

        Assert.Equal([("/a.ts", FileWatcherEventKind.Changed)], seen);

        watcher.Close();
        host.SetModifiedTime("/a.ts", T1.AddSeconds(1));
        host.RunPendingTimers();

        Assert.Single(seen);
    }

    [Fact]
    public void DynamicPriorityPolling_ReportsCreationAndDeletionFromTheTimeStamps()
    {
        var host = new FakePollingWatchFileHost();
        var watchFile = SysWatch.CreateDynamicPriorityPollingWatchFile(host);
        var seen = new List<FileWatcherEventKind>();
        watchFile("/a.ts", (_, kind, _) => seen.Add(kind), PollingInterval.High, null);

        // No modification time at all reads as "missing" (epoch 0), so appearing is a creation…
        host.SetModifiedTime("/a.ts", T1);
        host.RunPendingTimers();
        Assert.Equal([FileWatcherEventKind.Created], seen);

        // …and disappearing, a deletion.
        host.SetModifiedTime("/a.ts", null);
        host.RunPendingTimers();
        Assert.Equal([FileWatcherEventKind.Created, FileWatcherEventKind.Deleted], seen);
    }

    [Fact]
    public void FixedChunkSizePolling_VisitsEveryWatchedFile()
    {
        var host = new FakePollingWatchFileHost();
        host.SetModifiedTime("/a.ts", T0);
        host.SetModifiedTime("/b.ts", T0);
        var watchFile = SysWatch.CreateFixedChunkSizePollingWatchFile(host);
        var seen = new List<string>();
        var first = watchFile("/a.ts", (name, _, _) => seen.Add(name), PollingInterval.Low, null);
        var second = watchFile("/b.ts", (name, _, _) => seen.Add(name), PollingInterval.Low, null);
        host.SetModifiedTime("/a.ts", T1);
        host.SetModifiedTime("/b.ts", T1);

        host.RunPendingTimers();

        Assert.Equal(["/a.ts", "/b.ts"], seen);
        first.Close();
        second.Close();
    }

    // ---------------------------------------------------------------------------------------------
    // One watcher per name
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SingleWatcherPerName_CreatesOneWatcherAndDispatchesToEveryCallback()
    {
        var cache = new Dictionary<string, SysWatch.SingleFileWatcher<FileWatcherCallback>>(StringComparer.Ordinal);
        var created = new List<IFileWatcher>();
        var seen = new List<string>();

        IFileWatcher CreateWatcher(FileWatcherCallback callback)
        {
            var watcher = new SharedWatcher(callback);
            created.Add(watcher);
            return watcher;
        }

        var first = SysWatch.CreateSingleWatcherPerName(cache, true, "/a.ts", (name, _, _) => seen.Add($"first:{name}"), CreateWatcher);
        var second = SysWatch.CreateSingleWatcherPerName(cache, true, "/a.ts", (name, _, _) => seen.Add($"second:{name}"), CreateWatcher);

        Assert.Single(created);

        var shared = (SharedWatcher)created[0];
        shared.Raise("/a.ts", FileWatcherEventKind.Changed);
        Assert.Equal(["first:/a.ts", "second:/a.ts"], seen);

        // Closing one registration keeps the watcher alive for the other.
        first.Close();
        shared.Raise("/a.ts", FileWatcherEventKind.Changed);
        Assert.Equal(["first:/a.ts", "second:/a.ts", "second:/a.ts"], seen);
        Assert.False(shared.Closed);

        // Closing the last one closes the underlying watcher and drops the cache entry.
        second.Close();
        Assert.True(shared.Closed);
        Assert.Empty(cache);
    }

    /// <summary>Keeps the forwarded callback so a test can raise events through it.</summary>
    private sealed class SharedWatcher(FileWatcherCallback callback) : IFileWatcher
    {
        public bool Closed { get; private set; }

        public void Close() => Closed = true;

        public void Raise(string fileName, FileWatcherEventKind kind) => callback(fileName, kind);
    }

    // ---------------------------------------------------------------------------------------------
    // Option handling
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void GetFallbackOptions_ReinterpretsPollingKindsAsWatchFileKinds()
    {
        // TS casts fallbackPolling to WatchFileKind; that is only sound because the members line up pairwise,
        // so the correspondence is pinned here.
        Assert.Equal(
            WatchFileKind.FixedPollingInterval,
            SysWatch.GetFallbackOptions(new SysWatch.WatchOptions { FallbackPolling = PollingWatchKind.FixedInterval }).WatchFile);
        Assert.Equal(
            WatchFileKind.PriorityPollingInterval,
            SysWatch.GetFallbackOptions(new SysWatch.WatchOptions { FallbackPolling = PollingWatchKind.PriorityInterval }).WatchFile);
        Assert.Equal(
            WatchFileKind.DynamicPriorityPolling,
            SysWatch.GetFallbackOptions(new SysWatch.WatchOptions { FallbackPolling = PollingWatchKind.DynamicPriority }).WatchFile);
        Assert.Equal(
            WatchFileKind.FixedChunkSizePolling,
            SysWatch.GetFallbackOptions(new SysWatch.WatchOptions { FallbackPolling = PollingWatchKind.FixedChunkSize }).WatchFile);

        Assert.Equal(WatchFileKind.PriorityPollingInterval, SysWatch.GetFallbackOptions(null).WatchFile);
    }

    [Fact]
    public void SetCustomPollingValues_AppliesAndClearsTheEnvironmentOverrides()
    {
        var system = new FakeSystem();
        system.EnvironmentVariables["TSC_WATCH_UNCHANGEDPOLLTHRESHOLDS_LOW"] = "7";
        system.EnvironmentVariables["TSC_WATCH_POLLINGINTERVAL_LOW"] = "1000";

        try
        {
            SysWatch.SetCustomPollingValues(system);

            Assert.Equal(7, SysWatch.UnchangedPollThresholds[PollingInterval.Low]);
            Assert.Equal(1000, SysWatch.PollingIntervalMilliseconds(PollingInterval.Low));
        }
        finally
        {
            // TS has no "unset" path either; setting the overrides back to their defaults restores them.
            system.EnvironmentVariables["TSC_WATCH_UNCHANGEDPOLLTHRESHOLDS_LOW"] = "32";
            system.EnvironmentVariables["TSC_WATCH_POLLINGINTERVAL_LOW"] = ((int)PollingInterval.Low).ToString(CultureInfo.InvariantCulture);
            SysWatch.SetCustomPollingValues(system);
        }

        Assert.Equal(32, SysWatch.UnchangedPollThresholds[PollingInterval.Low]);
        Assert.Equal((int)PollingInterval.Low, SysWatch.PollingIntervalMilliseconds(PollingInterval.Low));
    }

    // ---------------------------------------------------------------------------------------------
    // createSystemWatchFunctions
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SystemWatchFunctions_PollsWhenTheEnvironmentAsksForPriorityPolling()
    {
        var polled = new List<string>();
        var host = NewHost(
            pollingWatchFileWorker: (fileName, _, _, _) =>
            {
                polled.Add(fileName);
                return new FakeWatcher();
            },
            tscWatchFile: "PriorityPollingInterval");

        SysWatch.CreateSystemWatchFunctions(host).WatchFile("/a.ts", (_, _, _) => { }, PollingInterval.High, null);

        Assert.Equal(["/a.ts"], polled);
    }

    [Fact]
    public void SystemWatchFunctions_UsesNativeWatchingByDefault()
    {
        var watched = new List<string>();
        var host = NewHost(fsWatchWorker: (fileOrDirectory, _, _) =>
        {
            watched.Add(fileOrDirectory);
            return new FakeFsWatchWorkerWatcher();
        });

        SysWatch.CreateSystemWatchFunctions(host).WatchFile("/a.ts", (_, _, _) => { }, PollingInterval.High, null);

        Assert.Equal(["/a.ts"], watched);
    }

    [Fact]
    public void SystemWatchFunctions_FallsBackToPollingWhenNativeWatchingFails()
    {
        var polled = new List<string>();
        var attempts = 0;
        var host = NewHost(
            pollingWatchFileWorker: (fileName, _, _, _) =>
            {
                polled.Add(fileName);
                return new FakeWatcher();
            },
            fsWatchWorker: (_, _, _) =>
            {
                attempts++;
                // What Linux reports once the inotify watch limit is reached.
                throw new IOException("ENOSPC: System limit for number of file watchers reached");
            },
            tscWatchFile: "UseFsEvents");

        SysWatch.CreateSystemWatchFunctions(host).WatchFile("/a.ts", (_, _, _) => { }, PollingInterval.High, null);

        Assert.Equal(1, attempts);
        Assert.Equal(["/a.ts"], polled);
    }

    [Fact]
    public void SystemWatchFunctions_WatchesPresentEntriesNativelyAndPollsMissingOnes()
    {
        var watched = new List<string>();
        var present = NewHost(
            fsWatchWorker: (fileOrDirectory, _, _) =>
            {
                watched.Add(fileOrDirectory);
                return new FakeFsWatchWorkerWatcher();
            },
            fileSystemEntryExists: (_, _) => true);

        SysWatch.CreateSystemWatchFunctions(present).WatchFile("/a.ts", (_, _, _) => { }, PollingInterval.High, null);

        Assert.Equal(["/a.ts"], watched);

        // A file that is not there yet cannot be watched natively, so the port polls for it to appear.
        var polled = new List<string>();
        var missing = NewHost(
            pollingWatchFileWorker: (fileName, _, _, _) =>
            {
                polled.Add(fileName);
                return new FakeWatcher();
            },
            fileSystemEntryExists: (_, _) => false);

        SysWatch.CreateSystemWatchFunctions(missing).WatchFile("/b.ts", (_, _, _) => { }, PollingInterval.High, null);

        Assert.Equal(["/b.ts"], polled);
    }

    // ---------------------------------------------------------------------------------------------
    // Watch-the-parent-directory
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void UseFsEventsOnParentDirectory_RoutesDirectoryEventsToTheWatchedFile()
    {
        var seen = new List<(string FileName, FileWatcherEventKind Kind)>();
        SysWatch.FsWatchCallback? directoryCallback = null;
        var fsWatch = (SysWatch.FsWatch)((_, _, callback, _, _, _) =>
        {
            directoryCallback = callback;
            return new FakeWatcher();
        });
        var watchFile = SysWatch.CreateUseFsEventsOnParentDirectoryWatchFile(
            fsWatch,
            useCaseSensitiveFileNames: true,
            getModifiedTime: _ => T1,
            fsWatchWithTimestamp: false);

        watchFile("/project/src/a.ts", (name, kind, _) => seen.Add((name, kind)), PollingInterval.High, null);
        Assert.NotNull(directoryCallback);

        // A change reported for a sibling file must not reach this watcher.
        directoryCallback(SysWatch.FsWatchEventName.Change, "b.ts", null);
        Assert.Empty(seen);

        directoryCallback(SysWatch.FsWatchEventName.Change, "a.ts", null);
        Assert.Equal([("/project/src/a.ts", FileWatcherEventKind.Changed)], seen);
    }

    // ---------------------------------------------------------------------------------------------
    // Recursive directory watching
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RecursiveDirectoryWatcher_WatchesEveryChildDirectory()
    {
        var children = new Dictionary<string, IList<string>>(StringComparer.Ordinal)
        {
            ["/project"] = ["/project/src"],
            ["/project/src"] = [],
        };
        var watchedDirectories = new List<string>();
        var host = new SysWatch.RecursiveDirectoryWatcherHost
        {
            WatchDirectory = (dirName, _, _, _) =>
            {
                watchedDirectories.Add(dirName);
                return new FakeWatcher();
            },
            UseCaseSensitiveFileNames = true,
            GetCurrentDirectory = () => "/project",
            GetAccessibleSortedChildDirectories = path => children.GetValueOrDefault(path) ?? [],
            FileSystemEntryExists = (path, _) => children.ContainsKey(path),
            Realpath = path => path,
            SetTimeout = (_, _) => new object(),
            ClearTimeout = _ => { },
        };

        var watchDirectory = SysWatch.CreateDirectoryWatcherSupportingRecursive(host);
        watchDirectory("/project", _ => { }, recursive: true, null);

        Assert.Equal(["/project", "/project/src"], watchedDirectories);
    }

    private static SysWatch.SystemWatchFunctionsHost NewHost(
        HostWatchFile? pollingWatchFileWorker = null,
        SysWatch.FsWatchWorker? fsWatchWorker = null,
        string? tscWatchFile = null,
        string? tscWatchDirectory = null,
        bool useNonPollingWatchers = false,
        SysWatch.FileSystemEntryExists? fileSystemEntryExists = null)
        => new()
        {
            PollingWatchFileWorker = pollingWatchFileWorker ?? ((_, _, _, _) => new FakeWatcher()),
            PollingWatchFileHost = new FakePollingWatchFileHost(),
            FsWatchWorker = fsWatchWorker ?? ((_, _, _) => new FakeFsWatchWorkerWatcher()),
            FileSystemEntryExists = fileSystemEntryExists ?? ((_, kind) => kind == FileSystemEntryKind.File),
            UseCaseSensitiveFileNames = true,
            GetCurrentDirectory = () => "/project",
            FsSupportsRecursiveFsWatch = true,
            GetAccessibleSortedChildDirectories = _ => [],
            Realpath = path => path,
            TscWatchFile = tscWatchFile,
            UseNonPollingWatchers = useNonPollingWatchers,
            TscWatchDirectory = tscWatchDirectory,
            InodeWatching = false,
            FsWatchWithTimestamp = false,
            SysLog = _ => { },
        };
}
