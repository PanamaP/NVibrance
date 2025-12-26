using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NVibrance.Nvidia;
using NVibrance.Services;

namespace NVibrance.ViewModels;

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

    private VibranceInfo? _selectedDisplay;
    public VibranceInfo? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (!SetField(ref _selectedDisplay, value)) return;
            if (value is null) return;

            SliderMin = value.Minimum;
            SliderMax = value.Maximum;
            Vibrance = value.Current;
        }
    }

    private ProgramProfile? _selectedProfile;
    public ProgramProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetField(ref _selectedProfile, value)) return;
            if (value is null) return;

            ProfileVibrance = value.Vibrance;
        }
    }

    private int _sliderMin;
    public int SliderMin { get => _sliderMin; private set => SetField(ref _sliderMin, value); }

    private int _sliderMax;
    public int SliderMax { get => _sliderMax; private set => SetField(ref _sliderMax, value); }

    private int _vibranceValue;
    public int Vibrance
    {
        get => _vibranceValue;
        set
        {
            if (!SetField(ref _vibranceValue, value)) return;
            _vibrance.Set(value);
        }
    }

    private int _profileVibrance;
    public int ProfileVibrance
    {
        get => _profileVibrance;
        set
        {
            if (!SetField(ref _profileVibrance, value)) return;
            if (SelectedProfile is null) return;

            SelectedProfile.Vibrance = value;
        }
    }

    public void RefreshDisplays()
    {
        Displays.Clear();
        foreach (var d in NvVibranceReader.ReadAll())
            Displays.Add(d);

        SelectedDisplay = Displays.FirstOrDefault();
    }

    public void RefreshProfiles()
    {
        Profiles.Clear();

        foreach (var p in _registry.Profiles)
        {
            p.Icon = ExeIconLoader.TryLoad(p.ExecutablePath);
            Profiles.Add(p);
        }

        SelectedProfile = Profiles.FirstOrDefault();
    }

    public void AddOrSelectProfile(string name, string exePath, int vibrance)
    {
        var existing = _registry.FindByExePath(exePath);
        if (existing is null)
        {
            var created = new ProgramProfile(name, exePath, vibrance)
            {
                Icon = ExeIconLoader.TryLoad(exePath)
            };

            _registry.Add(created);
            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Matches(exePath));
            return;
        }

        if (existing.Icon is null)
            existing.Icon = ExeIconLoader.TryLoad(existing.ExecutablePath);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Matches(exePath));
    }

    public void RenameSelectedProfile(string newName)
    {
        if (SelectedProfile is null) return;
        if (string.IsNullOrWhiteSpace(newName)) return;

        SelectedProfile.Name = newName.Trim();
    }

    public void DeleteSelectedProfile()
    {
        if (SelectedProfile is null) return;

        var exePath = SelectedProfile.ExecutablePath;
        _registry.RemoveByExePath(exePath);

        RefreshProfiles();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}