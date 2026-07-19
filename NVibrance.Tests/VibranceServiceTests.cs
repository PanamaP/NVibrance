using NVibrance.Services;
using Xunit;

namespace NVibrance.Tests;

public class VibranceServiceTests
{
    [Theory]
    [InlineData(80, 0, 100, 80)]  // in range → unchanged
    [InlineData(-5, 0, 100, 0)]   // below hardware min → clamped up
    [InlineData(150, 0, 100, 100)] // above hardware max → clamped down
    [InlineData(30, 0, 100, 30)]  // below the UI floor of 50 is allowed at hardware level
    public void ClampToHardware_ClampsToHardwareRangeOnly(int value, int hwMin, int hwMax, int expected)
    {
        Assert.Equal(expected, VibranceService.ClampToHardware(value, hwMin, hwMax));
    }
}
