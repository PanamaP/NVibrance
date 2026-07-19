using NvAPIWrapper.Display;

namespace NVibrance.Services;

public sealed class VibranceService : IVibranceService
{
    /// <summary>
    /// UI floor for profile values. Deliberately not applied to reads or writes here:
    /// capture/restore must round-trip the user's true desktop value, even below 50.
    /// </summary>
    public const int MinVibrance = 50;

    private Display? _cachedDisplay;

    public int GetCurrent()
    {
        try
        {
            var display = GetCachedPrimaryDisplay();
            return display.DigitalVibranceControl.CurrentLevel;
        }
        catch (InvalidOperationException)
        {
            // try refresh once and rethrow if still failing
            _cachedDisplay = FindPrimaryDisplay();
            return _cachedDisplay.DigitalVibranceControl.CurrentLevel;
        }
    }

    public void Set(int value)
    {
        try
        {
            var dvc = GetCachedPrimaryDisplay().DigitalVibranceControl;
            dvc.CurrentLevel = ClampToHardware(value, dvc.MinimumLevel, dvc.MaximumLevel);
        }
        catch (InvalidOperationException)
        {
            // refresh and retry once
            _cachedDisplay = FindPrimaryDisplay();
            var dvc = _cachedDisplay.DigitalVibranceControl;
            dvc.CurrentLevel = ClampToHardware(value, dvc.MinimumLevel, dvc.MaximumLevel);
        }
    }

    public static int ClampToHardware(int value, int hwMin, int hwMax)
        => Math.Clamp(value, hwMin, hwMax);

    private Display GetCachedPrimaryDisplay()
    {
        if (_cachedDisplay != null)
            return _cachedDisplay;

        _cachedDisplay = FindPrimaryDisplay();
        return _cachedDisplay;
    }

    private static Display FindPrimaryDisplay()
    {
        foreach (var display in Display.GetDisplays())
        {
            if (display.DisplayDevice.IsActive &&
                display.DisplayDevice.IsConnected)
                return display;
        }

        throw new InvalidOperationException("No active NVIDIA display found.");
    }
}
