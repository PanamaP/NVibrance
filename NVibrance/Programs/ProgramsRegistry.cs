using System.IO;
using System.Text.Json;

namespace NVibrance;

public sealed class ProgramRegistry
{
    private readonly List<ProgramProfile> _profiles = new();

    public IReadOnlyList<ProgramProfile> Profiles => _profiles;

    public void Add(ProgramProfile profile)
    {
        if (_profiles.Any(p => p.Matches(profile.ExecutablePath)))
            return;

        _profiles.Add(profile);
        
        try
        {
            SaveToDisk();
        }
        catch
        {
            // ignore disk errors
        }
    }

    public bool RemoveByExePath(string exePath)
    {
        var p = FindByExePath(exePath);
        if (p is null) return false;
        
        try
        {
            SaveToDisk();
        }
        catch
        {
            // ignore disk errors
        }
        
        return _profiles.Remove(p);
    }

    public ProgramProfile? FindByExePath(string exePath)
        => _profiles.FirstOrDefault(p => p.Matches(exePath));

    // ---- persistence ----

    private static string GetProfilesPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NVibrance");

        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "profiles.json");
    }

    public void LoadFromDisk()
    {
        var path = GetProfilesPath();
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);

            var loaded = JsonSerializer.Deserialize<List<ProgramProfileDto>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            _profiles.Clear();

            foreach (var dto in loaded)
            {
                if (string.IsNullOrWhiteSpace(dto.Name)) continue;
                if (string.IsNullOrWhiteSpace(dto.ExecutablePath)) continue;

                Add(new ProgramProfile(dto.Name, dto.ExecutablePath, dto.Vibrance));
            }
        }
        catch
        {
            // ignore bad/corrupt file; keep current in-memory list
        }
    }

    public void SaveToDisk()
    {
        var path = GetProfilesPath();

        var dto = _profiles
            .Select(p => new ProgramProfileDto(p.Name, p.ExecutablePath, p.Vibrance))
            .ToList();

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }

    private sealed record ProgramProfileDto(string Name, string ExecutablePath, int Vibrance);
}
