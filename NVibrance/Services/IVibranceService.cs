using NVibrance.Nvidia;

namespace NVibrance.Services;

/// <summary>
/// Reads and writes the digital vibrance level of each NVIDIA display.
/// </summary>
public interface IVibranceService
{
    /// <summary>The Windows primary monitor.</summary>
    DisplayId PrimaryDisplay { get; }

    /// <summary>Returns the active, connected NVIDIA displays.</summary>
    IReadOnlyList<DisplayId> GetDisplays();

    /// <summary>Returns the display's current driver vibrance level, unmodified.</summary>
    int GetCurrent(DisplayId display);

    /// <summary>Returns the display's hardware vibrance range.</summary>
    (int Minimum, int Maximum) GetRange(DisplayId display);

    /// <summary>Sets the display's vibrance level, clamped to its hardware range.</summary>
    void Set(DisplayId display, int value);
}
