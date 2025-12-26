using System.Diagnostics;

namespace NVibrance.Focus;

/// <summary>
/// Monitors changes to the foreground window using a Win32 event hook.
/// Raises events when the foreground process changes.
/// </summary>
public sealed class ForegroundHook : IDisposable
{
    /// <summary>
    /// Handle to the Win32 event hook for monitoring foreground window changes.
    /// </summary>
    private readonly IntPtr _hook;
    
    /// <summary>
    /// Keep a reference to the managed delegate so the GC does not collect it while native code may call it.
    /// </summary>
    private readonly NativeMethods.WinEventDelegate _callback;

    /// <summary>
    /// Event triggered when the foreground process changes.
    /// Provides the window handle and associated process (if available).
    /// </summary>
    public event Action<IntPtr, Process?>? ForegroundProcessChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForegroundHook"/> class.
    /// Sets up a Win32 event hook to monitor foreground window changes.
    /// </summary>
    public ForegroundHook()
    {
        _callback = WinEventProc;

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WineventOutofcontext);

        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Failed to set foreground window hook.");
    }

    /// <summary>
    ///   Handles Win32 foreground window change events triggered by the event hook.
    ///   This callback is invoked when the foreground window changes,
    ///   allowing the application to react to focus changes at the OS level.
    /// </summary>
    /// <param name="hWinEventHook">
    ///   Handle to the event hook instance that triggered the callback.
    /// </param>
    /// <param name="eventType">
    ///   The type of event that occurred (should be EVENT_SYSTEM_FOREGROUND).
    /// </param>
    /// <param name="hwnd">
    ///   Handle to the window that became the foreground window.
    ///   May be IntPtr.Zero if no window is associated.
    /// </param>
    /// <param name="idObject">
    ///   Identifies the object associated with the event (usually OBJID_WINDOW).
    /// </param>
    /// <param name="idChild">
    ///   Identifies the child element of the object, if applicable.
    /// </param>
    /// <param name="dwEventThread">
    ///   Identifier of the thread that generated the event.
    /// </param>
    /// <param name="dwmsEventTime">
    ///   Timestamp (in milliseconds) when the event was generated.
    /// </param>
    private void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var handlers = ForegroundProcessChanged;
        if (handlers == null)
        {
            return;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (threadId == 0 || pid == 0)
        {
            handlers(hwnd, null);
            return;
        }
        
        Process? process = null;
        try
        {
            process = Process.GetProcessById((int)pid);
        }
        catch
        {
            process = null;
        }
        handlers(hwnd, process);
    }

    public void Dispose()
    {
        NativeMethods.UnhookWinEvent(_hook);
    }
}