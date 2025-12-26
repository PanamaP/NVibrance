using System.Diagnostics;
using NVibrance.Services;
using Application = System.Windows.Application;

namespace NVibrance.Focus;

public sealed class VibranceController : IDisposable
{
    private readonly ForegroundHook _hook;
    private readonly ProgramRegistry _registry;
    private readonly VibranceService _vibrance;
    private readonly VibranceState _state = new();

    private string? _activeExePath;

    public VibranceController(ForegroundHook hook, ProgramRegistry registry, VibranceService vibrance)
    {
        _hook = hook;
        _registry = registry;
        _vibrance = vibrance;

        _hook.ForegroundProcessChanged += OnForegroundChanged;
    }

    private void OnForegroundChanged(Process? process)
    {
        string? exePath = null;

        try { exePath = process?.MainModule?.FileName; }
        catch { }

        if (exePath == _activeExePath)
            return;

        _activeExePath = exePath;

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (exePath == null)
            {
                RestoreIfNeeded();
                return;
            }

            var profile = _registry.FindByExePath(exePath);
            if (profile != null) ApplyProfile(profile);
            else RestoreIfNeeded();
        });
    }

    private void ApplyProfile(ProgramProfile profile)
    {
        var current = _vibrance.GetCurrent();
        _state.Capture(current);
        _vibrance.Set(profile.Vibrance);
    }

    private void RestoreIfNeeded()
    {
        var restore = _state.Restore();
        if (restore.HasValue)
            _vibrance.Set(restore.Value);
    }

    public void Dispose()
    {
        Application.Current.Dispatcher.Invoke(RestoreIfNeeded);
        _hook.ForegroundProcessChanged -= OnForegroundChanged;
    }
}