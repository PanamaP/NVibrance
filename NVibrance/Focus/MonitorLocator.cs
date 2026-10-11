using System.Runtime.InteropServices;
using NVibrance.Nvidia;

namespace NVibrance.Focus;

/// <summary>
/// Maps windows to the monitor they are shown on, by GDI device name.
/// </summary>
public static class MonitorLocator
{
    /// <summary>The monitor showing most of the window, or the nearest one when it is off-screen.</summary>
    public static DisplayId ForWindow(IntPtr hwnd)
        => TryGetDeviceName(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest))
           ?? Primary(); // the window was destroyed between the focus event and this lookup

    /// <summary>The Windows primary monitor, which always contains the origin.</summary>
    public static DisplayId Primary()
        => TryGetDeviceName(NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary))
           ?? throw new InvalidOperationException("Could not read the primary monitor.");

    private static DisplayId? TryGetDeviceName(IntPtr monitor)
    {
        var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? new DisplayId(info.DeviceName) : null;
    }
}
