namespace NVibrance.Services;

public sealed class VibranceState
{
    public bool IsOverridden { get; private set; }
    public int? PreviousValue { get; private set; }

    public void Capture(int current)
    {
        if (IsOverridden)
            return;

        PreviousValue = current;
        IsOverridden = true;
    }

    public int? Restore()
    {
        if (!IsOverridden)
            return null;

        IsOverridden = false;
        return PreviousValue;
    }

    public void Reset()
    {
        IsOverridden = false;
        PreviousValue = null;
    }
}