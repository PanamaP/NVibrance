namespace NVibrance.Services;

/// <summary>
/// Reads and writes the digital vibrance level of the primary display.
/// </summary>
public interface IVibranceService
{
    /// <summary>Returns the current driver vibrance level, unmodified.</summary>
    int GetCurrent();

    /// <summary>Sets the vibrance level, clamped to the hardware range.</summary>
    void Set(int value);
}
