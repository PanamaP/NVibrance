namespace NVibrance;

/// <summary>
/// Keeps NVibrance to one running copy per user session. A second launch (e.g. the Start
/// menu shortcut while NVibrance sits in the tray) asks the running copy to show its window
/// and exits, instead of starting a second copy that would fight over the vibrance.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\NVibrance.SingleInstance";
    private const string ShowWindowEventName = @"Local\NVibrance.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showWindow;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
    }

    /// <summary>Returns null when another copy is already running.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
            return new SingleInstance(mutex);

        mutex.Dispose();
        return null;
    }

    /// <summary>Asks the running copy to show its window.</summary>
    public static void SignalRunningInstance()
    {
        try
        {
            using var showWindow = EventWaitHandle.OpenExisting(ShowWindowEventName);
            showWindow.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // the running copy is still starting up or already exiting; nothing to show
        }
    }

    /// <summary>Calls <paramref name="callback"/> (on a thread-pool thread) for each later launch.</summary>
    public void OnShowWindowRequested(Action callback)
        => _registration = ThreadPool.RegisterWaitForSingleObject(
            _showWindow, (_, _) => callback(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showWindow.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
