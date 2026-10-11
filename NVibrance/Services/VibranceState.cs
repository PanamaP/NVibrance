using NVibrance.Nvidia;

namespace NVibrance.Services;

/// <summary>A profile applied on <see cref="Display"/>, which restore returns to <see cref="DesktopValue"/>.</summary>
public sealed record ActiveOverride(DisplayId Display, int DesktopValue);

/// <summary>
/// Manages the state of vibrance overrides. Only the foreground window carries a profile,
/// so at most one display is overridden at a time.
/// </summary>
public sealed class VibranceState
{
    /// <summary>The active override; null when every display holds its desktop value.</summary>
    public ActiveOverride? Current { get; private set; }

    /// <summary>
    /// Capture the display's desktop value, unless it is already overridden. An override on
    /// another display must be restored first, or its desktop value would be lost.
    /// </summary>
    public void Capture(DisplayId display, int current)
    {
        if (Current is not null)
        {
            if (Current.Display == display)
                return;

            throw new InvalidOperationException(
                $"Cannot capture {display} while {Current.Display} is still overridden.");
        }

        Current = new ActiveOverride(display, current);
    }

    /// <summary>
    /// Replace the captured value while an override is active. Used when the user
    /// manually changes the desktop vibrance mid-override, so restore keeps their
    /// change instead of silently undoing it.
    /// </summary>
    public void Recapture(int current)
    {
        if (Current is not null)
            Current = Current with { DesktopValue = current };
    }

    /// <summary>
    /// End the override, returning the display and value to restore, or null if none was active.
    /// </summary>
    public ActiveOverride? Restore()
    {
        var restore = Current;
        Current = null;
        return restore;
    }

    /// <summary>
    /// The desktop value restore will return the display to; null when it is not overridden
    /// (the driver then holds the desktop value).
    /// </summary>
    public int? CapturedValueFor(DisplayId display)
        => Current?.Display == display ? Current.DesktopValue : null;
}
