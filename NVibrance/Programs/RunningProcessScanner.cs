using System.Diagnostics;
using System.Windows.Media;
using NVibrance.Focus;

namespace NVibrance;

public static class RunningProcessScanner
{
    /// <param name="loadIcons">
    /// False leaves <see cref="RunningProgram.Icon"/> null, for scans run off the UI thread;
    /// the caller then loads icons where it creates its UI.
    /// </param>
    public static IReadOnlyList<RunningProgram> GetUserProcesses(bool loadIcons = true)
    {
        var result = new List<RunningProgram>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;

                // Resolver works for anti-cheat-protected games, which MainModule does not
                if (ProcessPathResolver.TryGetExecutablePath((uint)process.Id) is not string exePath)
                    continue;

                if (exePath.StartsWith(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new RunningProgram(
                    Name: process.ProcessName,
                    ExePath: exePath,
                    Icon: loadIcons ? ExeIconLoader.TryLoad(exePath) : null));
            }
            catch (Exception ex)
            {
                // Access denied for protected processes → skip
                Services.Log.Debug($"Skipping process {process.ProcessName} (pid {process.Id}): {ex.Message}");
            }
        }

        return result
            .GroupBy(p => p.ExePath)
            .Select(g => g.First())
            .OrderBy(p => p.Name)
            .ToList();
    }
}

public sealed record RunningProgram(
    string Name,
    string ExePath,
    ImageSource? Icon);