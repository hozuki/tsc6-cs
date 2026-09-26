namespace TypeScript.Compiler.Tests;

/// <summary>
/// A hand-driven watcher host: modification times are set directly and timers only fire when
/// <see cref="RunPendingTimers"/> is called, so the polling logic can be observed step by step.
/// </summary>
internal sealed class FakePollingWatchFileHost : SysWatch.IPollingWatchFileHost
{
    private readonly Dictionary<string, DateTimeOffset?> _modifiedTimes = new(StringComparer.Ordinal);
    private readonly List<(Action Callback, object Handle)> _timers = [];

    /// <summary>The interval of every scheduled timer, in order.</summary>
    public List<int> TimerIntervals { get; } = [];

    public void SetModifiedTime(string path, DateTimeOffset? time) => _modifiedTimes[path] = time;

    public DateTimeOffset? GetModifiedTime(string fileName) => _modifiedTimes.GetValueOrDefault(fileName);

    public object? SetTimeout(Action callback, int ms)
    {
        var handle = new object();
        _timers.Add((callback, handle));
        TimerIntervals.Add(ms);
        return handle;
    }

    public void ClearTimeout(object? timeoutId) => _timers.RemoveAll(timer => ReferenceEquals(timer.Handle, timeoutId));

    /// <summary>Fires every timer that is currently pending, once each.</summary>
    public void RunPendingTimers()
    {
        var pending = _timers.ToArray();
        _timers.Clear();
        foreach (var timer in pending) timer.Callback();
    }
}

/// <summary>A watcher that only records whether it was closed.</summary>
internal sealed class FakeWatcher : IFileWatcher
{
    public bool Closed { get; private set; }

    public void Close() => Closed = true;
}

/// <summary>A native-watcher stand-in, including the <c>on("error")</c> hook the port uses.</summary>
internal sealed class FakeFsWatchWorkerWatcher : SysWatch.IFsWatchWorkerWatcher
{
    private readonly Dictionary<string, Action> _listeners = new(StringComparer.Ordinal);

    public bool Closed { get; private set; }

    public void On(string eventName, Action listener) => _listeners[eventName] = listener;

    public void Close() => Closed = true;

    /// <summary>Raises a host event, e.g. <c>"error"</c>.</summary>
    public void Raise(string eventName)
    {
        if (_listeners.TryGetValue(eventName, out var listener)) listener();
    }
}
