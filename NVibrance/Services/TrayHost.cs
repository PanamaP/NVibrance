using System.ComponentModel;
using System.Windows;
using NVibrance.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace NVibrance.Services;

public class TrayHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ProgramRegistry _registry;
    private readonly IVibranceService _vibrance;
    private readonly Focus.VibranceController? _controller;
    private readonly ToolStripMenuItem _autostartMenuItem;
    private readonly CancelEventHandler _contextMenuOpeningHandler;

    public TrayHost(ProgramRegistry registry, IVibranceService vibrance, Focus.VibranceController? controller = null)
    {
        _registry = registry;
        _vibrance = vibrance;
        _controller = controller;

        _autostartMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true
        };
        _autostartMenuItem.Click += AutostartMenuItem_Click;
        
        _contextMenuOpeningHandler = (_, _) => UpdateAutostartChecked();
        
        _notifyIcon = new NotifyIcon
        {
            Text = "NVibrance",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        _notifyIcon.MouseClick += OnMouseClick;
        if (_notifyIcon.ContextMenuStrip != null)
            _notifyIcon.ContextMenuStrip.Opening += _contextMenuOpeningHandler;
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
        catch (Exception ex)
        {
            Log.Warn($"Failed to load tray icon, using system default: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostartMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());

        UpdateAutostartChecked();
        
        return menu;
    }
    
    private void UpdateAutostartChecked()
    {
        try
        {
            _autostartMenuItem.Checked = AutoStartService.IsEnabled();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read autostart state: {ex.Message}");
            _autostartMenuItem.Checked = false;
        }
    }
    
    private void AutostartMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            var shouldEnable = _autostartMenuItem.Checked;
            // prefer published exe path; AutoStartService will resolve the best candidate
            AutoStartService.SetEnabled(shouldEnable);
            // reflect actual state (in case of failure)
            UpdateAutostartChecked();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update autostart setting.", ex);
            // revert and notify user minimally
            UpdateAutostartChecked();
            try { MessageBox.Show("Failed to update autostart setting.", "NVibrance", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch (Exception mbEx) { Log.Debug($"MessageBox failed: {mbEx.Message}"); }
        }
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
            app.MainWindow = new MainWindow(_registry, _vibrance, _controller);

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
        try
        {
            if (_notifyIcon?.ContextMenuStrip != null)
                _notifyIcon.ContextMenuStrip.Opening -= _contextMenuOpeningHandler;

            _autostartMenuItem.Click -= AutostartMenuItem_Click;
            _notifyIcon?.MouseClick -= OnMouseClick;
            _notifyIcon?.Visible = false;
            _notifyIcon?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Error disposing tray icon: {ex.Message}");
        }
        GC.SuppressFinalize(this);
    }
}