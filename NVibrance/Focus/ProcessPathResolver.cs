using System.Diagnostics;
using NVibrance.Services;

namespace NVibrance.Focus;

/// <summary>
/// Resolves the full executable path of a process by pid.
/// Uses OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) + QueryFullProcessImageName,
/// which succeeds for anti-cheat-protected (e.g. EAC), elevated, and cross-bitness
/// processes where Process.MainModule throws "Access is denied".
/// </summary>
public static class ProcessPathResolver
{
    /// <summary>Pids whose resolution failure was already logged, to avoid poll-driven log spam.</summary>
    private static readonly HashSet<uint> LoggedFailedPids = new();
    private static readonly Lock Sync = new();

    public static string? TryGetExecutablePath(uint pid)
    {
        if (pid == 0)
            return null;

        var path = TryQueryFullProcessImageName(pid) ?? TryGetMainModulePath(pid);
        if (path is null)
            LogFailureOnce(pid);

        return path;
    }

    /// <summary>
    /// Last-resort identity when the full path cannot be resolved: the process name
    /// (no directory, no extension), readable without any process access rights.
    /// </summary>
    public static string? TryGetProcessName(uint pid)
    {
        if (pid == 0)
            return null;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not get process name for pid {pid}: {ex.Message}");
            return null;
        }
    }

    private static string? TryQueryFullProcessImageName(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var path = QueryImageName(handle, 1024);
            // ERROR_INSUFFICIENT_BUFFER for long (\\?\-style) paths: retry once, larger
            return path ?? QueryImageName(handle, 32768);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string? QueryImageName(IntPtr handle, uint bufferSize)
    {
        var buffer = new char[bufferSize];
        var size = bufferSize;

        // dwFlags = 0 → Win32 drive-letter path, the same form OpenFileDialog and
        // Process.MainModule produce, so profile matching is unaffected.
        if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) || size == 0)
            return null;

        return new string(buffer, 0, (int)size);
    }

    private static string? TryGetMainModulePath(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static void LogFailureOnce(uint pid)
    {
        lock (Sync)
        {
            if (!LoggedFailedPids.Add(pid))
                return;

            // pids recycle; cap the set so a long-running session cannot grow it unbounded
            if (LoggedFailedPids.Count > 1024)
                LoggedFailedPids.Clear();
        }

        Log.Debug($"Could not resolve executable path for pid {pid}.");
    }
}
