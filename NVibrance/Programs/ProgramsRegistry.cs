using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Timers;
using Timer = System.Timers.Timer;


namespace NVibrance;

public sealed class ProgramRegistry : IDisposable
{
    private readonly List<ProgramProfile> _profiles = new();
    private readonly Dictionary<ProgramProfile, PropertyChangedEventHandler> _handlers = new();
    private readonly Lock _sync = new();
    private readonly Timer _debounceTimer;
    private bool _isLoading;

    private const int DebounceDelayMs = 500;

    public event EventHandler? Saved;

    public ProgramRegistry()
    {
        _debounceTimer = new Timer(DebounceDelayMs) { AutoReset = false };
        _debounceTimer.Elapsed += DebounceTimer_Elapsed;
    }

    public IReadOnlyList<ProgramProfile> GetProfiles()
    {
        lock (_sync)
        {
            return _profiles.ToList();
        }
    }

    public void Add(ProgramProfile profile)
    {
        lock (_sync)
        {
            if (_profiles.Any(p => p.Matches(profile.ExecutablePath)))
                return;

            _profiles.Add(profile);
            AttachProfile(profile);
        }

        ScheduleSave();
    }

    public bool RemoveByExePath(string exePath)
    {
        ProgramProfile? p;
        lock (_sync)
        {
            p = FindByExePathInternal(exePath);
            if (p is null) return false;
            DetachProfile(p);
            _profiles.Remove(p);
        }

        ScheduleSave();
        return true;
    }

    public ProgramProfile? FindByExePath(string exePath)
    {
        lock (_sync)
        {
            return FindByExePathInternal(exePath);
        }
    }

    private ProgramProfile? FindByExePathInternal(string exePath)
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
            _isLoading = true;
            var json = File.ReadAllText(path);

            var loaded = JsonSerializer.Deserialize<List<ProgramProfileDto>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            lock (_sync)
            {
                // detach existing handlers before clearing
                foreach (var existing in _profiles.ToArray())
                    DetachProfile(existing);

                _profiles.Clear();

                foreach (var dto in loaded)
                {
                    if (string.IsNullOrWhiteSpace(dto.Name)) continue;
                    if (string.IsNullOrWhiteSpace(dto.ExecutablePath)) continue;

                    var profile = new ProgramProfile(dto.Name, dto.ExecutablePath, dto.Vibrance);
                    _profiles.Add(profile);
                    AttachProfile(profile);
                }
            }
        }
        catch
        {
            // ignore bad/corrupt file; keep current in-memory list
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void SaveToDisk()
    {
        // Public immediate save (will write current snapshot)
        List<ProgramProfileDto> dto;
        lock (_sync)
        {
            dto = _profiles
                .Select(p => new ProgramProfileDto(p.Name, p.ExecutablePath, p.Vibrance))
                .ToList();
        }

        try
        {
            var path = GetProfilesPath();
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);

            OnSaved();
        }
        catch
        {
            // ignore disk errors
        }
    }

    private void ScheduleSave()
    {
        if (_isLoading) return;

        // restart debounce timer
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DebounceTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            SaveToDisk();
        }
        catch
        {
            // swallow errors from timer thread
        }
    }

    public void Flush()
    {
        // stop timer and persist immediately
        _debounceTimer.Stop();
        SaveToDisk();
    }

    private void OnSaved() => Saved?.Invoke(this, EventArgs.Empty);

    private void AttachProfile(ProgramProfile profile)
    {
        if (_handlers.ContainsKey(profile)) return;

        PropertyChangedEventHandler handler = (s, e) =>
        {
            // Only persist meaningful, persisted properties
            if (string.Equals(e.PropertyName, nameof(ProgramProfile.Vibrance), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(ProgramProfile.Name), StringComparison.Ordinal))
            {
                ScheduleSave();
            }
        };

        profile.PropertyChanged += handler;
        _handlers[profile] = handler;
    }

    private void DetachProfile(ProgramProfile profile)
    {
        if (_handlers.TryGetValue(profile, out var handler))
        {
            profile.PropertyChanged -= handler;
            _handlers.Remove(profile);
        }
    }

    private sealed record ProgramProfileDto(string Name, string ExecutablePath, int Vibrance);

    public void Dispose()
    {
        _debounceTimer.Stop();
        _debounceTimer.Elapsed -= DebounceTimer_Elapsed;
        _debounceTimer.Dispose();
        Flush();
    }
}