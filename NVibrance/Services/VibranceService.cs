using NvAPIWrapper.Display;

namespace NVibrance.Services;

public sealed class VibranceService
{
    public const int MinVibrance = 50;
    
    private Display? _cachedDisplay;
    
    public int GetCurrent()
    {
        try
        {
            var display = GetCachedPrimaryDisplay();
            var current = display.DigitalVibranceControl.CurrentLevel;
            return Math.Max(MinVibrance, current);
        }
        catch (InvalidOperationException)
        {
            // try refresh once and rethrow if still failing
            _cachedDisplay = FindPrimaryDisplay();
            var current = _cachedDisplay.DigitalVibranceControl.CurrentLevel;
            return Math.Max(current, MinVibrance);
        }
    }

    public void Set(int value)
    {
        try
        {
            var display = GetCachedPrimaryDisplay();
            var dvc = display.DigitalVibranceControl;

            int effectiveMin = Math.Max(dvc.MinimumLevel, MinVibrance);
            int clamped = Math.Clamp(
                value,
                effectiveMin,
                dvc.MaximumLevel);

            dvc.CurrentLevel = clamped;
        }
        catch (InvalidOperationException)
        {
            // refresh and retry once
            _cachedDisplay = FindPrimaryDisplay();
            var dvc = _cachedDisplay.DigitalVibranceControl;

            int effectiveMin = Math.Max(dvc.MinimumLevel, MinVibrance);
            int clamped = Math.Clamp(
                value,
                effectiveMin,
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
}