using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NVibrance.Nvidia;
using NVibrance.Services;

namespace NVibrance.ViewModels;

/// <summary>
/// ViewModel for the main window.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ProgramRegistry _registry;
    private readonly VibranceService _vibrance = new();

    public ObservableCollection<VibranceInfo> Displays { get; } = new();
    public ObservableCollection<ProgramProfile> Profiles { get; } = new();

    public MainViewModel(ProgramRegistry registry)
    {
        _registry = registry;

        RefreshDisplays();
        RefreshProfiles();
    }

    public VibranceInfo? SelectedDisplay
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;
            if (value is null) return;

            // ensure UI minimum honors the service minimum
            SliderMin = Math.Max(value.Minimum, VibranceService.MinVibrance);
            SliderMax = value.Maximum;

            // ensure displayed current value respects the service minimum
            Vibrance = Math.Max(value.Current, VibranceService.MinVibrance);
        }
    }

    public ProgramProfile? SelectedProfile
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;
            if (value is null) return;

            ProfileVibrance = value.Vibrance;
        }
    }

    public int SliderMin
    {
        get;
        private set => SetField(ref field, value);
    }

    public int SliderMax
    {
        get;
        private set => SetField(ref field, value);
    }

    public int Vibrance
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;
            _vibrance.Set(value);
        }
    }

    public int ProfileVibrance
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;
            if (SelectedProfile is null) return;

            SelectedProfile.Vibrance = value;
        }
    }

    /// <summary>
    /// Refreshes the list of available displays.
    /// </summary>
    public void RefreshDisplays()
    {
        Displays.Clear();
        foreach (var d in VibranceReader.ReadAll())
            Displays.Add(d);

        SelectedDisplay = Displays.FirstOrDefault();
    }

    /// <summary>
    /// Refreshes the list of available program profiles.
    /// </summary>
    public void RefreshProfiles()
    {
        Profiles.Clear();

        foreach (var p in _registry.GetProfiles())
        {
            p.Icon = ExeIconCache.Get(p.ExecutablePath);
            Profiles.Add(p);
        }

        SelectedProfile = Profiles.FirstOrDefault();
    }

    /// <summary>
    /// Adds a new program profile or selects an existing one.
    /// </summary>
    public void AddOrSelectProfile(string name, string exePath, int vibrance)
    {
        var existing = _registry.FindByExePath(exePath);
        if (existing is null)
        {
            var created = new ProgramProfile(name, exePath, vibrance)
            {
                Icon = ExeIconCache.Get(exePath)
            };

            _registry.Add(created);
            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Matches(exePath));
            return;
        }

        if (existing.Icon is null)
        {
            existing.Icon = ExeIconCache.Get(existing.ExecutablePath);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Matches(exePath));
    }

    /// <summary>
    /// Renames the currently selected program profile.
    /// </summary>
    public void RenameSelectedProfile(string newName)
    {
        if (SelectedProfile is null) return;
        if (string.IsNullOrWhiteSpace(newName)) return;

        SelectedProfile.Name = newName.Trim();
    }

    /// <summary>
    /// Deletes the currently selected program profile.
    /// </summary>
    public void DeleteSelectedProfile()
    {
        if (SelectedProfile is null) return;

        var exePath = SelectedProfile.ExecutablePath;
        _registry.RemoveByExePath(exePath);

        RefreshProfiles();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Sets a field and raises PropertyChanged if the value changed.
    /// </summary>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}