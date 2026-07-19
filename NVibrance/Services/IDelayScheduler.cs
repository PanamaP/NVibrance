using System.Windows.Threading;

namespace NVibrance.Services;

/// <summary>
/// Restartable single-action delay used for debouncing. Scheduling replaces any
/// pending action and restarts the delay.
/// </summary>
public interface IDelayScheduler
{
    void Schedule(Action action);

    void Cancel();
}

/// <summary>
/// UI-thread implementation backed by a <see cref="DispatcherTimer"/>.
/// </summary>
public sealed class DispatcherDelayScheduler : IDelayScheduler
{
    private readonly DispatcherTimer _timer;
    private Action? _action;

    public DispatcherDelayScheduler(TimeSpan delay)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _action?.Invoke();
        };
    }

    public void Schedule(Action action)
    {
        _action = action;
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _action = null;
    }
}
