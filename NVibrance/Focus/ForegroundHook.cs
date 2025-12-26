using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NVibrance.Focus;

public sealed class ForegroundHook : IDisposable
{
    private readonly WinEventDelegate _callback;
    private readonly IntPtr _hook;

    public event Action<IntPtr, Process?>? ForegroundProcessChanged;

    public ForegroundHook()
    {
        _callback = WinEventProc;

        _hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WINEVENT_OUTOFCONTEXT);

        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Failed to set foreground window hook.");
    }

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
            return;

        var handlers = ForegroundProcessChanged;
        if (handlers == null) return;

        GetWindowThreadProcessId(hwnd, out uint pid);
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
        UnhookWinEvent(_hook);
    }

    // -------- Win32 --------

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint lpdwProcessId);
}