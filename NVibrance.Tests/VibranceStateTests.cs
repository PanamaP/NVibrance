using NVibrance.Nvidia;
using NVibrance.Services;
using Xunit;

namespace NVibrance.Tests;

public class VibranceStateTests
{
    private static readonly DisplayId Primary = new(@"\.\DISPLAY1");
    private static readonly DisplayId Secondary = new(@"\.\DISPLAY2");

    [Fact]
    public void Restore_WithoutCapture_ReturnsNull()
    {
        var state = new VibranceState();

        Assert.Null(state.Restore());
    }

    [Fact]
    public void Capture_ThenRestore_ReturnsDisplayAndCapturedValue()
    {
        var state = new VibranceState();

        state.Capture(Secondary, 42);

        Assert.Equal(new ActiveOverride(Secondary, 42), state.Restore());
    }

    [Fact]
    public void Capture_WhileOverridden_IsIgnored()
    {
        var state = new VibranceState();

        state.Capture(Primary, 42);
        state.Capture(Primary, 99);

        Assert.Equal(new ActiveOverride(Primary, 42), state.Restore());
    }

    [Fact]
    public void Capture_WhileAnotherDisplayIsOverridden_Throws()
    {
        var state = new VibranceState();
        state.Capture(Primary, 42);

        Assert.Throws<InvalidOperationException>(() => state.Capture(Secondary, 99));
        Assert.Equal(new ActiveOverride(Primary, 42), state.Current);
    }

    [Fact]
    public void Restore_SecondCall_ReturnsNull()
    {
        var state = new VibranceState();

        state.Capture(Primary, 42);
        state.Restore();

        Assert.Null(state.Restore());
    }

    [Fact]
    public void CaptureRestoreCapture_WorksAgain()
    {
        var state = new VibranceState();

        state.Capture(Primary, 42);
        state.Restore();
        state.Capture(Secondary, 30);

        Assert.Equal(new ActiveOverride(Secondary, 30), state.Restore());
    }

    [Fact]
    public void Recapture_WhileOverridden_UpdatesRestoredValue()
    {
        var state = new VibranceState();

        state.Capture(Secondary, 42);
        state.Recapture(60);

        Assert.Equal(new ActiveOverride(Secondary, 60), state.Restore());
    }

    [Fact]
    public void Recapture_WithoutOverride_HasNoEffect()
    {
        var state = new VibranceState();

        state.Recapture(60);

        Assert.Null(state.Restore());
    }

    [Fact]
    public void CapturedValueFor_IsTheDesktopValueOnlyForTheOverriddenDisplay()
    {
        var state = new VibranceState();
        Assert.Null(state.CapturedValueFor(Primary));

        state.Capture(Secondary, 55);
        Assert.Equal(55, state.CapturedValueFor(Secondary));
        Assert.Null(state.CapturedValueFor(Primary));

        state.Restore();
        Assert.Null(state.CapturedValueFor(Secondary));
    }
}
