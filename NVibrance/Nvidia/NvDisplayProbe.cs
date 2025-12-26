using NvAPIWrapper.Display;

namespace NVibrance.Nvidia;

public static class NvDisplayProbe
{
    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var result = new List<DisplayInfo>();

        foreach (var display in Display.GetDisplays())
        {
            result.Add(new DisplayInfo(
                id: display.DisplayDevice.DisplayId,
                isConnected: display.DisplayDevice.IsConnected,
                isActive: display.DisplayDevice.IsActive,
                name: display.DisplayDevice.ToString()
            ));
        }

        return result;
    }
}

public sealed record DisplayInfo(
    uint id,
    bool isConnected,
    bool isActive,
    string name
);