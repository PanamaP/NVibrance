using NVibrance.Services;
using Xunit;

namespace NVibrance.Tests;

public class VibranceStateTests
{
    [Fact]
    public void Restore_WithoutCapture_ReturnsNull()
    {
        var state = new VibranceState();

        Assert.Null(state.Restore());
    }

    [Fact]
    public void Capture_ThenRestore_ReturnsCapturedValue()
    {
        var state = new VibranceState();

        state.Capture(42);

        Assert.Equal(42, state.Restore());
    }

    [Fact]
    public void Capture_WhileOverridden_IsIgnored()
    {
        var state = new VibranceState();

        state.Capture(42);
        state.Capture(99);

        Assert.Equal(42, state.Restore());
    }

    [Fact]
    public void Restore_SecondCall_ReturnsNull()
    {
        var state = new VibranceState();

        state.Capture(42);
        state.Restore();

        Assert.Null(state.Restore());
    }

    [Fact]
    public void CaptureRestoreCapture_WorksAgain()
    {
        var state = new VibranceState();

        state.Capture(42);
        state.Restore();
        state.Capture(30);

        Assert.Equal(30, state.Restore());
    }

    [Fact]
    public void Recapture_WhileOverridden_UpdatesRestoredValue()
    {
        var state = new VibranceState();

        state.Capture(42);
        state.Recapture(60);

        Assert.Equal(60, state.Restore());
    }

    [Fact]
    public void Recapture_WithoutOverride_HasNoEffect()
    {
        var state = new VibranceState();

        state.Recapture(60);

        Assert.Null(state.Restore());
    }
}
