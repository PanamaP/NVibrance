using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Windows;
using NVibrance.Focus;
using NVibrance.Nvidia;
using NVibrance.Services;
using Application = System.Windows.Application;

namespace NVibrance;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private TrayHost? _tray;
    private ForegroundHook? _hook;
    private VibranceController? _controller;

    private readonly ProgramRegistry _registry = new();
    private readonly VibranceService _vibrance = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        NvApiTest.Initialize();

        _registry.LoadFromDisk();
        
        _tray = new TrayHost(_registry);
        _hook = new ForegroundHook();
        _controller = new VibranceController(_hook, _registry, _vibrance);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _registry.SaveToDisk(); } catch { }
        
        _controller?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}