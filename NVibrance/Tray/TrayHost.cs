using System.Windows;
using Application = System.Windows.Application;

namespace NVibrance;

public class TrayHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ProgramRegistry _registry;

    public TrayHost(ProgramRegistry registry)
    {
        _registry = registry;

        _notifyIcon = new NotifyIcon
        {
            Text = "NVibrance",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        _notifyIcon.MouseClick += OnMouseClick;
    }

    private Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/ui/Assets/logo.ico", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
                return new Icon(streamInfo.Stream);
        }
        catch { /* ignore */ }
        
        return SystemIcons.Application;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());

        return menu;
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        var app = Application.Current;

        if (app.MainWindow == null)
            app.MainWindow = new MainWindow(_registry);

        if (!app.MainWindow.IsVisible)
            app.MainWindow.Show();

        if (app.MainWindow.WindowState == WindowState.Minimized)
            app.MainWindow.WindowState = WindowState.Normal;

        app.MainWindow.Activate();
        app.MainWindow.Topmost = true;
        app.MainWindow.Topmost = false;
    }

    public void Dispose()
    {
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        GC.SuppressFinalize(this);
    }
}