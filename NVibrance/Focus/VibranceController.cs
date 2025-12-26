using System.Diagnostics;
using NVibrance.Services;
using Application = System.Windows.Application;

namespace NVibrance.Focus;

public sealed class VibranceController : IDisposable
{
    private readonly ForegroundHook _hook;
    private readonly ProgramRegistry _registry;
    private readonly VibranceService _vibrance;
    public readonly VibranceState State = new();

    private string? _activeExePath;
    private IntPtr _activeWindowHandle = IntPtr.Zero;

    public VibranceController(ForegroundHook hook, ProgramRegistry registry, VibranceService vibrance)
    {
        _hook = hook;
        _registry = registry;
        _vibrance = vibrance;

        _hook.ForegroundProcessChanged += OnForegroundChanged;
    }

    private void OnForegroundChanged(IntPtr hwnd, Process? process)
    {
        string? exePath = null;

        try { exePath = process?.MainModule?.FileName; }
        catch { }

        if (exePath == _activeExePath && hwnd == _activeWindowHandle)
            return;

        _activeExePath = exePath;
        _activeWindowHandle = hwnd;

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exePath == null)
            {
                RestoreIfNeeded();
                return;
            }

            var profile = _registry.FindByExePath(exePath);
            if (profile != null) ApplyProfile(profile);
            else RestoreIfNeeded();
        }));
    }

    private void ApplyProfile(ProgramProfile profile)
    {
        var current = _vibrance.GetCurrent();
        if (current == profile.Vibrance)
            return;
        
        State.Capture(current);
        _vibrance.Set(profile.Vibrance);
    }

    private void RestoreIfNeeded()
    {
        var restore = State.Restore();
        if (restore.HasValue)
            _vibrance.Set(restore.Value);
    }

    public void Dispose()
    {
        _hook.ForegroundProcessChanged -= OnForegroundChanged;
        Application.Current.Dispatcher.BeginInvoke(new Action(RestoreIfNeeded));
    }
}