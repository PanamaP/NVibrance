using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NVibrance.ViewModels;

/// <summary>
/// The sidebar row for the desktop vibrance: the value restored whenever the focused
/// application has no profile. Listed alongside profiles so both edit the same way.
/// </summary>
public sealed class DesktopEntry : INotifyPropertyChanged
{
    public string Name => "Desktop";

    public int Vibrance
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
