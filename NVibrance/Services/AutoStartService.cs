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
        catch { return false; }
    }

    private static void Enable(string valueName = DefaultValueName)
    {
        try
        {
            var exePath = Application.ExecutablePath;
            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }
            
            var quoted = $"\"{Application.ExecutablePath}\" --minimized";
            
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(valueName, quoted, RegistryValueKind.String);
        }
        catch { /* swallow errors */ }
    }

    private static void Disable(string valueName = DefaultValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch { /* swallow errors */ }
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