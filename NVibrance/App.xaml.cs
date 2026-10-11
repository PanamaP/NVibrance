
using System.Windows;
using NVibrance.Focus;
using NVibrance.Services;
using NVibrance.UI;

namespace NVibrance;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    private TrayHost? _tray;
    private ForegroundHook? _hook;
    private ForegroundPoller? _poller;
    private VibranceController? _controller;

    private readonly ProgramRegistry _registry = new();
    private readonly VibranceService _vibrance = new();
    private readonly UpdateService _updates = new();

    private Type? _stormExceptionType;
    private DateTime _stormWindowStartUtc;
    private int _stormCount;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--verbose"))
            Log.EnableVerbose();

        Log.Info($"NVibrance starting, args=[{string.Join(" ", e.Args)}]");
        RegisterGlobalExceptionHandlers();
        Theme.Initialize(Resources);

        // the app may have been moved since autostart was enabled
        AutoStartService.SyncIfEnabled();

        _registry.LoadFromDisk();

        try
        {
            _hook = new ForegroundHook();
        }
        catch (InvalidOperationException ex)
        {
            // the 2s poller still provides full functionality, just with higher latency
            Log.Error("Failed to install foreground hook; running in polling-only mode.", ex);
        }

        _controller = new VibranceController(_hook, _registry, _vibrance);
        _poller = new ForegroundPoller(_controller);
        _tray = new TrayHost(_registry, _vibrance, _controller, _updates);

        // a game may already be in the foreground when NVibrance autostarts
        _controller.EvaluateCurrentForeground();
        _updates.Start();

        if (e.Args.Contains("--minimized"))
        {
            return;
        }

        _tray.ShowMainWindow();
    }

    /// <summary>Shows the window when NVibrance is launched again while already running.</summary>
    internal void ShowMainWindow() => _tray?.ShowMainWindow();

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled dispatcher exception.", args.Exception);
            args.Handled = true;

            if (IsExceptionStorm(args.Exception))
            {
                Log.Error("Same exception type repeated too often; shutting down.");
                Shutdown();
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception; process is terminating.", args.ExceptionObject as Exception);
            // possibly off the UI thread, where the debounce timer can't be touched; SaveToDisk logs its own failures
            _registry.SaveToDisk();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>
    /// Loop guard for the keep-alive crash policy: more than five unhandled exceptions
    /// of the same type within 60 seconds means we are stuck in an error storm.
    /// </summary>
    private bool IsExceptionStorm(Exception exception)
    {
        var type = exception.GetType();
        var now = DateTime.UtcNow;

        if (type != _stormExceptionType || now - _stormWindowStartUtc > TimeSpan.FromSeconds(60))
        {
            _stormExceptionType = type;
            _stormWindowStartUtc = now;
            _stormCount = 1;
            return false;
        }

        return ++_stormCount > 5;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("NVibrance exiting.");
        _registry.SaveToDisk();

        _poller?.Dispose();
        _controller?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();
        _updates.Dispose();
        base.OnExit(e);
    }
}
