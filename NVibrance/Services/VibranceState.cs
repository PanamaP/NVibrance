namespace NVibrance.Services;

/// <summary>
/// Manages the state of vibrance overrides.
/// </summary>
public sealed class VibranceState
{
    /// <summary>
    /// Indicates whether the vibrance has been overridden.
    /// </summary>
    private bool IsOverridden { get; set; }
    
    /// <summary>
    /// The previously captured vibrance value.
    /// </summary>
    private int? PreviousValue { get; set; }

    /// <summary>
    /// Capture the current vibrance value if not already captured.
    /// </summary>
    public void Capture(int current)
    {
        if (IsOverridden)
            return;

        PreviousValue = current;
        IsOverridden = true;
    }

    /// <summary>
    /// Restore the previously captured vibrance value if overridden.
    /// </summary>
    public int? Restore()
    {
        if (!IsOverridden)
            return null;

        IsOverridden = false;
        return PreviousValue;
    }
}