using NVibrance.Services;
using Velopack;

namespace NVibrance;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else: when Velopack's installer, updater or uninstaller
        // launches the app with a hook argument, the hook runs here and the process exits.
        VelopackApp.Build()
            // a downloaded update is installed only when the user chooses Restart to update;
            // Velopack's default would install it silently at the next launch (e.g. login)
            .SetAutoApplyOnStartup(false)
            // start with Windows by default; the tray menu turns it off
            .OnAfterInstallFastCallback(_ => AutoStartService.SetEnabled(true))
            .OnBeforeUninstallFastCallback(_ => AutoStartService.DisableIfRegisteredFor(Environment.ProcessPath))
            .Run();

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalRunningInstance();
            return;
        }

        var app = new App();
        app.InitializeComponent();
        instance.OnShowWindowRequested(() => app.Dispatcher.BeginInvoke(app.ShowMainWindow));
        app.Run();
    }
}
