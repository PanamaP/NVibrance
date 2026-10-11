namespace NVibrance.Nvidia;

/// <summary>
/// A monitor, identified by its GDI device name (e.g. <c>\\.\DISPLAY2</c>). NVAPI and
/// Win32 report the same name in different casing, so it is upper-cased on construction.
/// </summary>
public readonly record struct DisplayId
{
    public DisplayId(string deviceName) => DeviceName = deviceName.ToUpperInvariant();

    public string DeviceName { get; }

    public override string ToString() => DeviceName;
}
