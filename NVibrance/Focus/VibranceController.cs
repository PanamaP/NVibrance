using NVibrance.Nvidia;
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
    private readonly Func<IntPtr, DisplayId> _resolveDisplay;
    private readonly Func<(IntPtr Hwnd, uint Pid)> _getForeground;
    public readonly VibranceState State = new();

    private IntPtr _activeWindowHandle = IntPtr.Zero;
    private string? _activeExePath;
    private DisplayId? _activeDisplay;
    private ProgramProfile? _lastTarget;
    private (ProgramProfile Profile, DisplayId Display)? _pendingTarget;
    private (DisplayId Display, int Value)? _preview;
    private bool _isPaused;

    /// <summary>The profile currently applied, or null when the desktop value is in effect.</summary>
    public ProgramProfile? ActiveProfile { get; private set; }

    /// <summary>Raised when <see cref="ActiveProfile"/> or <see cref="IsPaused"/> may have changed.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Pausing restores the desktop value and stops switching until resumed; resuming
    /// immediately re-evaluates the focused window. Not persisted across restarts.
    /// </summary>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            Log.Info(value ? "Automatic switching paused." : "Automatic switching resumed.");

            ForgetEvaluation();
            if (value)
            {
                _debounce.Cancel();
                _pendingTarget = null;
                Commit();
            }
            else
            {
                EvaluateCurrentForeground();
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public VibranceController(
        ForegroundHook? hook,
        ProgramRegistry registry,
        IVibranceService vibrance,
        IDelayScheduler? debounce = null,
        Func<uint, string?>? resolveExePath = null,
        Func<uint, string?>? resolveProcessName = null,
        Func<(IntPtr Hwnd, uint Pid)>? getForeground = null,
        Func<IntPtr, DisplayId>? resolveDisplay = null)
    {
        _hook = hook;
        _registry = registry;
        _vibrance = vibrance;
        _debounce = debounce ?? new DispatcherDelayScheduler(DebounceDelay);
        _resolveExePath = resolveExePath ?? ProcessPathResolver.TryGetExecutablePath;
        _resolveProcessName = resolveProcessName ?? ProcessPathResolver.TryGetProcessName;
        _getForeground = getForeground ?? GetForegroundFromWin32;
        _resolveDisplay = resolveDisplay ?? MonitorLocator.ForWindow;

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
        if (_isPaused)
            return;

        var exePath = _resolveExePath(pid);
        var display = _resolveDisplay(hwnd);
        // a windowed game dragged to another monitor is a new placement to apply on
        var sameWindow = hwnd == _activeWindowHandle && display == _activeDisplay;

        // Dedupe: only a successful resolution is authoritative. A null path is never
        // cached, so a window whose process couldn't be read yet (e.g. transient failure
        // at game spawn) is re-resolved on the next event or poll tick.
        if (sameWindow && exePath is not null && exePath == _activeExePath)
            return;

        _activeWindowHandle = hwnd;
        _activeExePath = exePath;
        _activeDisplay = display;

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
            $"Foreground hwnd=0x{hwnd:X} pid={pid} path={exePath ?? "<unresolved>"} on {display} " +
            $"-> {(target is null ? "no matching profile" : $"profile '{target.Name}'")}");

        // Same window reaching the same conclusion as last time → nothing new to
        // schedule; avoids NVAPI reads on every poll tick for name-matched or
        // unresolvable windows. (A still-pending debounce commits this same target.)
        if (sameWindow && ReferenceEquals(target, _lastTarget))
            return;

        // Debounce: restart the quiet period on every new target so rapid alt-tab churn
        // (game → overlay → game) produces a single NVAPI write.
        _pendingTarget = target is null ? null : (target, display);
        _lastTarget = target;
        _debounce.Schedule(Commit);
    }

    /// <summary>
    /// The user manually changed the desktop vibrance. If a profile override is active,
    /// update the value restore will return so their change survives leaving the game.
    /// </summary>
    public void NotifyManualDesktopChange(int value) => State.Recapture(value);

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        // Matching data changed: forget cached conclusions and re-check the current window.
        ForgetEvaluation();
        EvaluateCurrentForeground();
    }

    private void ForgetEvaluation()
    {
        _activeWindowHandle = IntPtr.Zero;
        _activeExePath = null;
        _activeDisplay = null;
        _lastTarget = null;
    }

    /// <summary>
    /// Shows a profile value being edited, on the display of the foreground window
    /// (NVibrance's own while the slider is dragged), until <see cref="EndPreview"/>.
    /// Applies immediately and even while paused, so the edit is visible as it happens.
    /// </summary>
    public void Preview(int value)
    {
        var (hwnd, _) = _getForeground();
        var display = hwnd == IntPtr.Zero ? _vibrance.PrimaryDisplay : _resolveDisplay(hwnd);
        _preview = (display, value);
        Commit();
    }

    /// <summary>Ends a preview, returning to the foreground window's profile or the desktop value.</summary>
    public void EndPreview()
    {
        if (_preview is null) return;

        _preview = null;
        Commit();
    }

    private void Commit()
    {
        try
        {
            ApplyDesired();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to apply or restore vibrance.", ex);
        }

        // also after a failure, and when a profile's value changed: listeners re-read
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDesired()
    {
        if (_preview is (var previewDisplay, var previewValue))
            Apply(previewDisplay, previewValue, "preview");
        else if (_pendingTarget is (var profile, var display))
            Apply(display, profile.Vibrance, $"profile '{profile.Name}'");
        else
            RestoreIfNeeded();

        ActiveProfile = _preview is null ? _pendingTarget?.Profile : null;
    }

    private void Apply(DisplayId display, int value, string what)
    {
        if (State.Current is { } other && other.Display != display)
            RestoreIfNeeded();

        var current = _vibrance.GetCurrent(display);
        if (current == value)
            return;

        State.Capture(display, current);
        _vibrance.Set(display, value);
        Log.Info($"Applied {what} on {display} (vibrance {value}, was {current}).");
    }

    private void RestoreIfNeeded()
    {
        if (State.Restore() is { } restore)
        {
            _vibrance.Set(restore.Display, restore.DesktopValue);
            Log.Info($"Restored desktop vibrance {restore.DesktopValue} on {restore.Display}.");
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
