using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace NVibrance;

public sealed class ProgramProfile : INotifyPropertyChanged
{
    private string _name;
    private int _vibrance;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public string ExecutablePath { get; }

    public int Vibrance
    {
        get => _vibrance;
        set
        {
            if (_vibrance == value) return;
            _vibrance = value;
            OnPropertyChanged();
        }
    }

    // Not persisted; computed from ExecutablePath
    public ImageSource? Icon
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            OnPropertyChanged();
        }
    }

    public ProgramProfile(string name, string executablePath, int vibrance)
    {
        _name = name;
        ExecutablePath = executablePath;
        _vibrance = vibrance;
    }

    public bool Matches(string exePath)
        => string.Equals(ExecutablePath, exePath, StringComparison.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}