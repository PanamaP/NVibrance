using NvAPIWrapper.Display;
using NVibrance.Focus;
using NVibrance.Nvidia;

namespace NVibrance.Services;

public sealed class VibranceService : IVibranceService
{
    /// <summary>
    /// UI floor for profile values. Deliberately not applied to reads or writes here:
    /// capture/restore must round-trip the user's true desktop value, even below 50.
    /// </summary>
    public const int MinVibrance = 50;

    public DisplayId PrimaryDisplay => MonitorLocator.Primary();

    public IReadOnlyList<DisplayId> GetDisplays()
        => Display.GetDisplays().Select(d => new DisplayId(d.Name)).ToList();

    public int GetCurrent(DisplayId display)
        => Find(display).DigitalVibranceControl.CurrentLevel;

    public (int Minimum, int Maximum) GetRange(DisplayId display)
    {
        var dvc = Find(display).DigitalVibranceControl;
        return (dvc.MinimumLevel, dvc.MaximumLevel);
    }

    public void Set(DisplayId display, int value)
    {
        var dvc = Find(display).DigitalVibranceControl;
        dvc.CurrentLevel = ClampToHardware(value, dvc.MinimumLevel, dvc.MaximumLevel);
    }

    public static int ClampToHardware(int value, int hwMin, int hwMax)
        => Math.Clamp(value, hwMin, hwMax);

    // Looked up on every call: a cached handle goes stale when a monitor is hot-plugged.
    // NVAPI enumerates only active displays; the DisplayDevice.IsActive check costs ~60 ms.
    private static Display Find(DisplayId display)
        => Display.GetDisplays().FirstOrDefault(d => new DisplayId(d.Name) == display)
           ?? throw new InvalidOperationException($"{display} is not an active NVIDIA display.");
}
