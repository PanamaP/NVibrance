namespace NVibrance.Services;

public sealed class VibranceRange
{
    public int Min { get; init; }
    public int Max { get; init; }
    public int Current { get; set; }
}