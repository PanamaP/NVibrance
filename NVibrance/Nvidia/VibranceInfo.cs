namespace NVibrance.Nvidia;

public sealed record VibranceInfo(
    uint DisplayId,
    string Name,
    int Current,
    int Minimum,
    int Maximum
);
