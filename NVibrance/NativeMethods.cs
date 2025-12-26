using System.Runtime.InteropServices;

namespace NVibrance;


public static class NativeMethods
{
    /// <summary>
    ///   Delegate for handling Windows event hooks.
    ///   This delegate is used to define the signature of the callback function
    ///   that processes events triggered by the SetWinEventHook function.
    /// </summary>
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    public const uint EventSystemForeground = 0x0003;
    public const uint WineventOutofcontext = 0x0000;
    
    // -------- DWM interop for rounded corners (Windows 11) --------
    public const int DwmwaWindowCornerPreference = 33;

    public enum DwmWindowCornerPreference
    {
        DwmwcpRound = 2,
    }

    /// <summary>
    /// Sets a specified attribute for a window using the Desktop Window Manager (DWM) API.
    /// This function allows customization of window appearance and behavior on Windows OS.
    /// </summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
    
    /// <summary>
    ///   Sets an event hook to monitor specific system events, such as foreground window changes.
    ///   This function allows applications to receive notifications when certain events occur
    ///   in the Windows operating system.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    /// <summary>
    ///   Removes a previously set event hook, stopping the application from receiving
    ///   notifications for the specified events.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    /// <summary>
    ///   Retrieves the identifier of the thread and process that created the specified window.
    ///   This function is useful for associating windows with their owning processes.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint lpdwProcessId);}