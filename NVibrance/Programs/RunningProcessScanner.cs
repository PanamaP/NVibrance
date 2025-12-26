using System.Diagnostics;
using System.Windows.Media;

namespace NVibrance;

public static class RunningProcessScanner
{
    public static IReadOnlyList<RunningProgram> GetUserProcesses()
    {
        var result = new List<RunningProgram>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                    continue;

                if (process.MainModule?.FileName is not string exePath)
                    continue;

                if (exePath.StartsWith(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new RunningProgram(
                    Name: process.ProcessName,
                    ExePath: exePath,
                    Icon: ExeIconLoader.TryLoad(exePath)));
            }
            catch
            {
                // Access denied → ignore
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