using System.IO;
using Microsoft.Win32;

namespace NVibrance.Services;

public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "NVibrance";

    public static bool IsEnabled(string valueName = DefaultValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key == null) return false;
            var val = key.GetValue(valueName) as string;
            return !string.IsNullOrEmpty(val);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read autostart registry value: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Repairs the autostart entry when it points somewhere other than the running
    /// executable — which happens whenever the user moves or replaces the app folder.
    /// Windows fails such a launch silently and the tray menu still reports autostart
    /// as enabled, so without this the user has no way to notice it stopped working.
    /// </summary>
    public static void SyncIfEnabled(string valueName = DefaultValueName)
    {
        try
        {
            string? stored;
            using (var readKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: false))
            {
                stored = readKey?.GetValue(valueName) as string;
            }

            // no entry at all means autostart is off; nothing to repair
            if (string.IsNullOrEmpty(stored))
                return;

            var expected = BuildRunCommand(Environment.ProcessPath);
            if (expected is null || !NeedsRepair(stored, expected, File.Exists))
                return;

            using var writeKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (writeKey is null)
                return;

            writeKey.SetValue(valueName, expected, RegistryValueKind.String);
            Log.Info($"Autostart pointed at {stored}; repaired to {expected}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not verify autostart entry: {ex.Message}");
        }
    }

    /// <summary>Builds the Run-key command line, or null if the path is unusable.</summary>
    internal static string? BuildRunCommand(string? exePath)
        => string.IsNullOrEmpty(exePath) ? null : $"\"{exePath}\" --minimized";

    /// <summary>
    /// True only when the registered executable is gone. A different but still valid
    /// path is left alone: someone running a second copy should not silently steal the
    /// autostart entry from the install they actually use.
    /// </summary>
    internal static bool NeedsRepair(string stored, string expected, Func<string, bool> pathExists)
    {
        if (string.Equals(stored.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            return false;

        var storedPath = ExtractExePath(stored);
        return storedPath is null || !pathExists(storedPath);
    }

    /// <summary>Pulls the executable path back out of a Run-key command line.</summary>
    internal static string? ExtractExePath(string storedCommand)
    {
        var command = storedCommand.Trim();
        if (command.Length == 0)
            return null;

        if (command[0] == '"')
        {
            var closing = command.IndexOf('"', 1);
            return closing > 1 ? command[1..closing] : null;
        }

        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    private static void Enable(string valueName = DefaultValueName)
    {
        try
        {
            // Environment.ProcessPath reports the real executable under single-file
            // publish, which is how NVibrance ships.
            var command = BuildRunCommand(Environment.ProcessPath);
            if (command is null)
            {
                Log.Warn("Could not determine executable path; autostart not enabled.");
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(valueName, command, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not enable autostart: {ex.Message}");
        }
    }

    private static void Disable(string valueName = DefaultValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not disable autostart: {ex.Message}");
        }
    }

    public static void SetEnabled(bool enable, string valueName = DefaultValueName)
    {
        if (enable)
        {
            Enable(valueName);
        }
        else
        {
            Disable(valueName);
        }
    }
}
