using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Timers;
using NVibrance.Services;
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

    /// <summary>
    /// Raised when the set of profiles or a profile's vibrance changes,
    /// so the controller can re-evaluate the current foreground window.
    /// </summary>
    public event EventHandler? ProfilesChanged;

    private readonly string? _profilesPathOverride;

    /// <param name="profilesPath">
    /// Overrides the profiles.json location (used by tests); defaults to
    /// %APPDATA%\NVibrance\profiles.json.
    /// </param>
    public ProgramRegistry(string? profilesPath = null)
    {
        _profilesPathOverride = profilesPath;
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
        OnProfilesChanged();
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
        OnProfilesChanged();
        return true;
    }

    public ProgramProfile? FindByExePath(string exePath)
    {
        lock (_sync)
        {
            return FindByExePathInternal(exePath);
        }
    }

    /// <summary>
    /// Finds a profile by bare process name. Used only when the full executable path
    /// of the foreground process cannot be read (anti-cheat-protected processes).
    /// </summary>
    public ProgramProfile? FindByProcessName(string processName)
    {
        List<ProgramProfile> matches;
        lock (_sync)
        {
            matches = _profiles.Where(p => p.MatchesProcessName(processName)).ToList();
        }

        if (matches.Count > 1)
            Log.Warn($"Multiple profiles match process name '{processName}'; using '{matches[0].Name}'.");

        return matches.FirstOrDefault();
    }

    private ProgramProfile? FindByExePathInternal(string exePath)
        => _profiles.FirstOrDefault(p => p.Matches(exePath));

    // ---- persistence ----

    private string GetProfilesPath()
    {
        if (_profilesPathOverride is not null)
            return _profilesPathOverride;

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
        catch (JsonException ex)
        {
            Log.Error($"profiles.json is corrupt; keeping in-memory profiles. Path: {path}", ex);
            BackupCorruptFile(path);
        }
        catch (Exception ex)
        {
            // IOException (file locked, e.g. by cloud sync), UnauthorizedAccessException, …
            Log.Warn($"Could not read profiles from {path}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static void BackupCorruptFile(string path)
    {
        try
        {
            File.Copy(path, path + ".bad", overwrite: true);
            Log.Info($"Backed up corrupt profiles file to {path}.bad");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not back up corrupt profiles file: {ex.Message}");
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
            File.Move(tmp, path, overwrite: true);

            OnSaved();
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to save profiles: {ex.GetType().Name}: {ex.Message}");
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
        catch (Exception ex)
        {
            // an escaped exception on this timer thread would kill the process
            Log.Error("Unexpected error saving profiles from debounce timer.", ex);
        }
    }

    public void Flush()
    {
        // stop timer and persist immediately
        _debounceTimer.Stop();
        SaveToDisk();
    }

    private void OnSaved() => Saved?.Invoke(this, EventArgs.Empty);

    private void OnProfilesChanged()
    {
        if (_isLoading) return;
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

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

            // A changed vibrance value affects what should currently be applied
            if (string.Equals(e.PropertyName, nameof(ProgramProfile.Vibrance), StringComparison.Ordinal))
                OnProfilesChanged();
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