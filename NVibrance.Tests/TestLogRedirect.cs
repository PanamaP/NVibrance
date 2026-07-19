using System.IO;
using System.Runtime.CompilerServices;
using NVibrance.Services;

namespace NVibrance.Tests;

/// <summary>
/// Sends log output to a temp file for the whole test run, so tests do not write
/// into the user's real log at %LOCALAPPDATA%\NVibrance\logs. Runs before any test.
/// The fixed filename means repeated runs reuse the same file rather than accumulating.
/// </summary>
internal static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "NVibranceTests_logs");
        Directory.CreateDirectory(dir);
        Log.RedirectTo(Path.Combine(dir, "test.log"));
    }
}
