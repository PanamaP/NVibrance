using NvAPIWrapper.Display;
using NVibrance.Nvidia;

namespace NVibrance.Services;

public sealed class VibranceService
{
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
            var display = GetCachedPrimaryDisplay();
            var dvc = display.DigitalVibranceControl;

            int clamped = Math.Clamp(
                value,
                dvc.MinimumLevel,
                dvc.MaximumLevel);

            dvc.CurrentLevel = clamped;
        }
        catch (InvalidOperationException)
        {
            // refresh and retry once
            _cachedDisplay = FindPrimaryDisplay();
            var dvc = _cachedDisplay.DigitalVibranceControl;

            int clamped = Math.Clamp(
                value,
                dvc.MinimumLevel,
                dvc.MaximumLevel);

            dvc.CurrentLevel = clamped;
        }
    }
    
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
    
    public void RefreshPrimaryDisplay() => _cachedDisplay = FindPrimaryDisplay();
}