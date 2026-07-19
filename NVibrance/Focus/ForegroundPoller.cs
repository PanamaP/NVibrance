using System.Windows.Threading;

namespace NVibrance.Focus;

/// <summary>
/// Low-frequency polling fallback for foreground detection. Exclusive-fullscreen games
/// do not always fire EVENT_SYSTEM_FOREGROUND; polling bounds the worst-case staleness
/// at one interval. Redundant ticks are deduplicated inside the controller, so the
/// event hook and the poller can safely coexist.
/// </summary>
public sealed class ForegroundPoller : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;

    public ForegroundPoller(VibranceController controller)
    {
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => controller.EvaluateCurrentForeground();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();
}
