using System.IO;

namespace NVibrance.Services;

/// <summary>
/// Minimal static file logger writing to %LOCALAPPDATA%\NVibrance\logs\nvibrance.log
/// with size-based rotation (1 MB, one archive file kept).
/// Never throws: on IO failure it disables itself and retries at most every five minutes.
/// </summary>
public static class Log
{
    private const long MaxFileSizeBytes = 1024 * 1024;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    private static readonly Lock Sync = new();
    private static string? _logPath;
    private static bool _disabled;
    private static DateTime _lastFailureUtc;

    public static void Info(string message) => WriteEntry("INFO ", message, null);

    public static void Warn(string message) => WriteEntry("WARN ", message, null);

    public static void Error(string message, Exception? exception = null) => WriteEntry("ERROR", message, exception);

    /// <summary>
    /// Debug-build-only diagnostic output; never written to the log file.
    /// </summary>
    public static void Debug(string message)
        => System.Diagnostics.Debug.WriteLine($"NVibrance: {message}");

    private static void WriteEntry(string level, string message, Exception? exception)
    {
        System.Diagnostics.Debug.WriteLine(
            exception is null
                ? $"NVibrance [{level.TrimEnd()}] {message}"
                : $"NVibrance [{level.TrimEnd()}] {message}{Environment.NewLine}{exception}");

        lock (Sync)
        {
            if (_disabled && DateTime.UtcNow - _lastFailureUtc < RetryInterval)
                return;

            try
            {
                var path = _logPath ??= BuildLogPath();
                RotateIfNeeded(path);

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (exception is not null)
                    line += Environment.NewLine + exception;

                File.AppendAllText(path, line + Environment.NewLine);
                _disabled = false;
            }
            catch
            {
                // The logger must never throw; back off and retry later.
                _disabled = true;
                _lastFailureUtc = DateTime.UtcNow;
            }
        }
    }

    private static string BuildLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NVibrance",
            "logs");

        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "nvibrance.log");
    }

    private static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxFileSizeBytes)
            return;

        var archive = Path.Combine(info.DirectoryName!, "nvibrance.1.log");
        File.Move(path, archive, overwrite: true);
    }
}
