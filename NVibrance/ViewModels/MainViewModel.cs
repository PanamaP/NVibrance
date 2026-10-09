using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

    /// <summary>
    /// Set while model state is pushed into the slider. A slider coerces an out-of-range
    /// value and writes it back through the binding; that write must not overwrite a profile.
    /// </summary>
    private bool _syncingSlider;

    public ObservableCollection<VibranceInfo> Displays { get; } = new();

    public DesktopEntry Desktop { get; } = new();

    /// <summary>Sidebar rows: the <see cref="Desktop"/> entry first, then every profile.</summary>
    public ObservableCollection<object> Entries { get; } = new();

    public MainViewModel(ProgramRegistry registry, IVibranceService vibrance, VibranceController? controller = null)
    {
        _registry = registry;
        _vibrance = vibrance;
        _controller = controller;

        Entries.CollectionChanged += OnEntriesChanged;
        if (_controller is not null)
            _controller.StatusChanged += (_, _) => OnPropertyChanged(nameof(IsSwitchingPaused));

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
            SyncSlider(() =>
            {
                SliderMin = Math.Max(value.Minimum, VibranceService.MinVibrance);
                SliderMax = value.Maximum;
            });

            ShowDesktopValue(value.Current);
        }
    }

    /// <summary>
    /// Re-reads the desktop vibrance, which may have changed outside NVibrance
    /// (e.g. in the NVIDIA Control Panel) since the window was last shown.
    /// </summary>
    public void RefreshDesktopVibrance()
    {
        if (!HasDisplay) return;

        try
        {
            ShowDesktopValue(_vibrance.GetCurrent());
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the current vibrance: {ex.Message}");
        }
    }

    /// <summary>
    /// Reflects the desktop value into the UI without writing it back to the driver;
    /// the user's real desktop value (possibly below 50) must survive.
    /// </summary>
    private void ShowDesktopValue(int driverValue)
    {
        // while a profile is applied the driver holds the profile's value, not the desktop's
        var desktop = _controller?.State.CapturedValue ?? driverValue;

        _refreshingDisplayValue = true;
        try
        {
            Vibrance = Math.Max(desktop, VibranceService.MinVibrance);
        }
        finally
        {
            _refreshingDisplayValue = false;
        }
    }

    private void SyncSlider(Action update)
    {
        _syncingSlider = true;
        try
        {
            update();
        }
        finally
        {
            _syncingSlider = false;
        }
    }

    /// <summary>The selected sidebar row: <see cref="Desktop"/> or a <see cref="ProgramProfile"/>.</summary>
    public object? SelectedEntry
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;

            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(IsDesktopSelected));
            OnPropertyChanged(nameof(IsProfileSelected));
            SyncSlider(() => OnPropertyChanged(nameof(DetailVibrance)));
        }
    }

    public ProgramProfile? SelectedProfile => SelectedEntry as ProgramProfile;

    public bool IsDesktopSelected => SelectedEntry is DesktopEntry;

    public bool IsProfileSelected => SelectedEntry is ProgramProfile;

    public bool HasProfiles => Entries.Count > 1;

    /// <summary>Automatic profile switching is paused (set from the tray menu).</summary>
    public bool IsSwitchingPaused
    {
        get => _controller?.IsPaused ?? false;
        set
        {
            if (_controller is not null)
                _controller.IsPaused = value;
        }
    }

    public int SliderMin
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(HasDisplay));
        }
    }

    public int SliderMax
    {
        get;
        private set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(HasDisplay));
        }
    }

    /// <summary>False when NVAPI found no active NVIDIA display, so there is nothing to adjust.</summary>
    public bool HasDisplay => SliderMax > SliderMin;

    /// <summary>The desktop vibrance; setting it writes to the driver immediately.</summary>
    public int Vibrance
    {
        get;
        set
        {
            if (!SetField(ref field, value)) return;

            Desktop.Vibrance = value;
            if (IsDesktopSelected)
                SyncSlider(() => OnPropertyChanged(nameof(DetailVibrance)));

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

    /// <summary>The vibrance of whichever sidebar row is selected.</summary>
    public int DetailVibrance
    {
        get => SelectedProfile?.Vibrance ?? Vibrance;
        set
        {
            if (_syncingSlider || value == DetailVibrance) return;

            if (SelectedProfile is { } profile)
            {
                profile.Vibrance = value;
                ProfileEdited?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Vibrance = value;
            }

            OnPropertyChanged();
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
    /// Rebuilds the sidebar from the registry, keeping the selection when it still exists.
    /// </summary>
    public void RefreshProfiles()
    {
        var previous = SelectedProfile?.ExecutablePath;

        Entries.Clear();
        Entries.Add(Desktop);

        foreach (var p in _registry.GetProfiles())
        {
            p.Icon = ExeIconCache.Get(p.ExecutablePath);
            Entries.Add(p);
        }

        SelectedEntry = (previous is null ? null : FindEntry(previous)) ?? (object)Desktop;
    }

    /// <summary>
    /// Adds a new program profile or selects an existing one.
    /// </summary>
    public void AddOrSelectProfile(string name, string exePath, int vibrance)
    {
        if (_registry.FindByExePath(exePath) is null)
        {
            var created = new ProgramProfile(name, exePath, vibrance)
            {
                Icon = ExeIconCache.Get(exePath)
            };

            _registry.Add(created);
            Entries.Add(created);
        }

        SelectedEntry = FindEntry(exePath);
    }

    /// <summary>
    /// Renames the currently selected program profile.
    /// </summary>
    public void RenameSelectedProfile(string newName)
    {
        if (SelectedProfile is null) return;
        if (string.IsNullOrWhiteSpace(newName)) return;

        var trimmed = newName.Trim();
        if (trimmed == SelectedProfile.Name) return;

        SelectedProfile.Name = trimmed;
        ProfileEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Deletes the currently selected program profile and selects its neighbour.
    /// Returns the removed profile so the caller can offer undo via <see cref="RestoreProfile"/>.
    /// </summary>
    public ProgramProfile? DeleteSelectedProfile()
    {
        if (SelectedProfile is not { } removed) return null;

        var index = Entries.IndexOf(removed);
        _registry.RemoveByExePath(removed.ExecutablePath);
        Entries.Remove(removed);

        // the row that slid into its place, else the one above (Desktop at worst)
        SelectedEntry = Entries[Math.Min(index, Entries.Count - 1)];
        return removed;
    }

    /// <summary>
    /// Re-adds a profile removed by <see cref="DeleteSelectedProfile"/> and selects it.
    /// If the same executable was added again in the meantime, that profile wins.
    /// </summary>
    public void RestoreProfile(ProgramProfile profile)
    {
        if (_registry.FindByExePath(profile.ExecutablePath) is null)
        {
            _registry.Add(profile);
            Entries.Add(profile);
        }

        SelectedEntry = FindEntry(profile.ExecutablePath);
    }

    private ProgramProfile? FindEntry(string exePath)
        => Entries.OfType<ProgramProfile>().FirstOrDefault(p => p.Matches(exePath));

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasProfiles));

    /// <summary>
    /// Raised when the user changes a profile's name or vibrance (but not when profiles
    /// are added or removed), so the view can confirm the save that follows.
    /// </summary>
    public event EventHandler? ProfileEdited;

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
