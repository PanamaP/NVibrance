using NvAPIWrapper.Display;

namespace NVibrance.Nvidia;

/// <summary>
/// Read-only access to NVIDIA Digital Vibrance.
/// No writes, no side effects.
/// </summary>
public static class NvVibranceReader
{
    public static IReadOnlyList<VibranceInfo> ReadAll()
    {
        var result = new List<VibranceInfo>();
        
        foreach (var display in Display.GetDisplays())
        {
            if (!display.DisplayDevice.IsConnected || !display.DisplayDevice.IsActive)
                continue;

            var dvc = display.DigitalVibranceControl;
            
            result.Add(new VibranceInfo(
                DisplayId: display.DisplayDevice.DisplayId,
                Name: display.Name,
                Current: dvc.CurrentLevel,
                Minimum: dvc.MinimumLevel,
                Maximum: dvc.MaximumLevel
            ));
        }

        return result;
    }
}
