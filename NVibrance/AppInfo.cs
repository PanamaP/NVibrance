using System.Reflection;

namespace NVibrance;

public static class AppInfo
{
    /// <summary>
    /// The version shown to users, e.g. "1.4.0". Release builds take it from the git tag
    /// (see release.yml); the informational version's "+commit" suffix is dropped.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return informational?.Split('+')[0]
               ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
               ?? "unknown";
    }
}
