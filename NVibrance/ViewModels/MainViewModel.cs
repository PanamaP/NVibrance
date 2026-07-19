using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NVibrance.Focus;
using NVibrance.Nvidia;
using NVibrance.Services;

namespace NVibrance.ViewModels;

/// <summary>
/// ViewModel for the main window.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ProgramRegistry _registry;
    private readonly IVibranceService _vibrance;
    private readonly VibranceController? _controller;

    /// <summary>Suppresses driver writes while reflecting an already-current value into the slider.</summary>
    private bool _refreshingDisplayValue;

    public ObservableCollection<VibranceInfo> Displays { get; } = new();
    public ObservableCollection<ProgramProfile> Profiles { get; } = new();

    public MainViewModel(ProgramRegistry registry, IVibranceService vibrance, VibranceController? controller = null)
    {
        _registry = registry;
        _vibrance = vibrance;
        _controller = controller;

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

            // reflect the current value into the slider without writing it back to the
            // driver — the user's real desktop value (possibly below 50) must survive
            _refreshingDisplayValue = true;
            try
            {
                Vibrance = Math.Max(value.Current, VibranceService.MinVibrance);
            }
            finally
            {
                _refreshingDisplayValue = false;
            }
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
            if (_refreshingDisplayValue) return;

            try
            {
                _vibrance.Set(value);
                _controller?.NotifyManualDesktopChange(value);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to set vibrance to {value}.", ex);
            }
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
        try
        {
            foreach (var d in VibranceReader.ReadAll())
                Displays.Add(d);
        }
        catch (Exception ex)
        {
            // NVAPI unavailable (non-NVIDIA GPU, driver update in progress) → empty list
            Log.Error("Failed to read displays from NVAPI.", ex);
        }

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