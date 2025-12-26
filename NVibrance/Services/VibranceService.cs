using NvAPIWrapper.Display;
using NVibrance.Nvidia;

namespace NVibrance.Services;

public sealed class VibranceService
{
    public int GetCurrent()
    {
        var display = GetPrimaryDisplay();
        return display.DigitalVibranceControl.CurrentLevel;
    }

    public void Set(int value)
    {
        var display = GetPrimaryDisplay();
        var dvc = display.DigitalVibranceControl;

        int clamped = Math.Clamp(
            value,
            dvc.MinimumLevel,
            dvc.MaximumLevel);

        dvc.CurrentLevel = clamped;
    }

    private static Display GetPrimaryDisplay()
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