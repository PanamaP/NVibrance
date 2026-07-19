using NVibrance.Services;

namespace NVibrance.Focus;

/// <summary>
/// Decides when to apply a profile's vibrance or restore the desktop value, based on
/// which window is in the foreground.
/// Threading invariant: every entry point (hook callback, poller tick, startup and
/// profile-change re-evaluation) runs on the UI thread, so state needs no locking.
/// </summary>
public sealed class VibranceController : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly ForegroundHook? _hook;
    private readonly ProgramRegistry _registry;
    private readonly IVibranceService _vibrance;
    private readonly IDelayScheduler _debounce;
    private readonly Func<uint, string?> _resolveExePath;
    private readonly Func<uint, string?> _resolveProcessName;
    private readonly Func<(IntPtr Hwnd, uint Pid)> _getForeground;
    public readonly VibranceState State = new();

    private IntPtr _activeWindowHandle = IntPtr.Zero;
    private string? _activeExePath;
    private ProgramProfile? _lastTarget;
    private ProgramProfile? _pendingTarget;

    public VibranceController(
        ForegroundHook? hook,
        ProgramRegistry registry,
        IVibranceService vibrance,
        IDelayScheduler? debounce = null,
        Func<uint, string?>? resolveExePath = null,
        Func<uint, string?>? resolveProcessName = null,
        Func<(IntPtr Hwnd, uint Pid)>? getForeground = null)
    {
        _hook = hook;
        _registry = registry;
        _vibrance = vibrance;
        _debounce = debounce ?? new DispatcherDelayScheduler(DebounceDelay);
        _resolveExePath = resolveExePath ?? ProcessPathResolver.TryGetExecutablePath;
        _resolveProcessName = resolveProcessName ?? ProcessPathResolver.TryGetProcessName;
        _getForeground = getForeground ?? GetForegroundFromWin32;

        if (_hook is not null)
            _hook.ForegroundProcessChanged += Evaluate;

        _registry.ProfilesChanged += OnProfilesChanged;
    }

    /// <summary>
    /// Evaluates whatever window is currently in the foreground. Used at startup,
    /// by the polling fallback, and after profile changes.
    /// </summary>
    public void EvaluateCurrentForeground()
    {
        var (hwnd, pid) = _getForeground();
        if (hwnd == IntPtr.Zero)
            return; // secure desktop / UAC / lock screen: keep current state, don't restore

        Evaluate(hwnd, pid);
    }

    private static (IntPtr Hwnd, uint Pid) GetForegroundFromWin32()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return (IntPtr.Zero, 0u);

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return (hwnd, pid);
    }

    public void Evaluate(IntPtr hwnd, uint pid)
    {
        var exePath = _resolveExePath(pid);
        var sameWindow = hwnd == _activeWindowHandle;

        // Dedupe: only a successful resolution is authoritative. A null path is never
        // cached, so a window whose process couldn't be read yet (e.g. transient failure
        // at game spawn) is re-resolved on the next event or poll tick.
        if (sameWindow && exePath is not null && exePath == _activeExePath)
            return;

        _activeWindowHandle = hwnd;
        _activeExePath = exePath;

        ProgramProfile? target = null;
        if (exePath is not null)
        {
            target = _registry.FindByExePath(exePath);
        }
        else
        {
            // Anti-cheat-protected games (e.g. Apex Legends under EAC) can deny even the
            // limited path query; the process name needs no access rights at all.
            var name = _resolveProcessName(pid);
            if (name is not null)
            {
                target = _registry.FindByProcessName(name);
                if (target is not null)
                    Log.Info($"Matched profile '{target.Name}' by process name '{name}' (path unavailable).");
            }
        }

        Log.Debug(
            $"Foreground hwnd=0x{hwnd:X} pid={pid} path={exePath ?? "<unresolved>"} " +
            $"-> {(target is null ? "no matching profile" : $"profile '{target.Name}'")}");

        // Same window reaching the same conclusion as last time → nothing new to
        // schedule; avoids NVAPI reads on every poll tick for name-matched or
        // unresolvable windows. (A still-pending debounce commits this same target.)
        if (sameWindow && ReferenceEquals(target, _lastTarget))
            return;

        // Debounce: restart the quiet period on every new target so rapid alt-tab churn
        // (game → overlay → game) produces a single NVAPI write.
        _pendingTarget = target;
        _lastTarget = target;
        _debounce.Schedule(CommitPending);
    }

    /// <summary>
    /// The user manually changed the desktop vibrance. If a profile override is active,
    /// update the value restore will return so their change survives leaving the game.
    /// </summary>
    public void NotifyManualDesktopChange(int value) => State.Recapture(value);

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        // Matching data changed: forget cached conclusions and re-check the current window.
        _activeWindowHandle = IntPtr.Zero;
        _activeExePath = null;
        _lastTarget = null;
        EvaluateCurrentForeground();
    }

    private void CommitPending()
    {
        try
        {
            if (_pendingTarget is not null)
                ApplyProfile(_pendingTarget);
            else
                RestoreIfNeeded();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to apply or restore vibrance.", ex);
        }
    }

    private void ApplyProfile(ProgramProfile profile)
    {
        var current = _vibrance.GetCurrent();
        if (current == profile.Vibrance)
            return;

        State.Capture(current);
        _vibrance.Set(profile.Vibrance);
        Log.Info($"Applied profile '{profile.Name}' (vibrance {profile.Vibrance}, was {current}).");
    }

    private void RestoreIfNeeded()
    {
        var restore = State.Restore();
        if (restore.HasValue)
        {
            _vibrance.Set(restore.Value);
            Log.Info($"Restored desktop vibrance {restore.Value}.");
        }
    }

    public void Dispose()
    {
        if (_hook is not null)
            _hook.ForegroundProcessChanged -= Evaluate;

        _registry.ProfilesChanged -= OnProfilesChanged;
        _debounce.Cancel();

        try
        {
            RestoreIfNeeded();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to restore vibrance on shutdown.", ex);
        }
    }
}
