using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            
            // Freeze bitmap to reduce memory/GDI overhead and make it shareable across threads.
            if (value is BitmapSource { CanFreeze: true, IsFrozen: false } bs)
                bs.Freeze();
            
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

    /// <summary>
    /// Last-resort match by bare process name (no directory, no extension), used only
    /// when the foreground process's full path cannot be read (protected processes).
    /// </summary>
    public bool MatchesProcessName(string processName)
        => string.Equals(
            Path.GetFileNameWithoutExtension(ExecutablePath),
            processName,
            StringComparison.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}