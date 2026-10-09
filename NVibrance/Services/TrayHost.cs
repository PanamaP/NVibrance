using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NVibrance.UI;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using Separator = System.Windows.Controls.Separator;

namespace NVibrance.Services;

public class TrayHost : IDisposable
{
    private const int TooltipMaxLength = 127; // NotifyIcon.Text limit
    private static readonly TimeSpan TooltipRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly NotifyIcon _notifyIcon;
    private readonly ProgramRegistry _registry;
    private readonly IVibranceService _vibrance;
    private readonly Focus.VibranceController? _controller;
    private readonly UpdateService? _updates;

    private readonly ContextMenu _menu;
    private readonly MenuItem _updateMenuItem;
    private readonly TextBlock _statusTitle = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _statusDetail = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
    private readonly MenuItem _pauseMenuItem;
    private readonly MenuItem _autostartMenuItem;

    private DateTime _tooltipRefreshedAtUtc;
    private bool _exiting;
    private bool _syncingMenu;

    public TrayHost(ProgramRegistry registry, IVibranceService vibrance, Focus.VibranceController? controller = null,
        UpdateService? updates = null)
    {
        _registry = registry;
        _vibrance = vibrance;
        _controller = controller;
        _updates = updates;

        _statusDetail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        _pauseMenuItem = CreateToggleItem("Pause automatic switching", SetPaused);
        _pauseMenuItem.Visibility = controller is null ? Visibility.Collapsed : Visibility.Visible;

        _autostartMenuItem = CreateToggleItem("Start with Windows", SetAutostart);

        // hidden until an update has been downloaded
        _updateMenuItem = CreateItem("Restart to update", (_, _) => RestartToUpdate());
        _updateMenuItem.Visibility = Visibility.Collapsed;

        _menu = BuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Text = "NVibrance",
            Icon = LoadIcon(),
            Visible = true,
        };

        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.MouseMove += OnMouseMove;
        _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;

        if (_controller is not null)
            _controller.StatusChanged += OnStatusChanged;

        if (_updates is not null)
            _updates.UpdateReady += OnUpdateReady;

        RefreshStatus();
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

    // ---------------------------------------------------------------- menu

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuStyle");

        var status = new MenuItem
        {
            Header = new StackPanel { Children = { _statusTitle, _statusDetail } },
        };
        status.SetResourceReference(FrameworkElement.StyleProperty, "TrayStatusItemStyle");

        // the default action, which a left click also performs, is bold as in other tray menus
        var open = CreateItem("Open NVibrance", (_, _) => ShowMainWindow());
        open.FontWeight = FontWeights.SemiBold;

        var exit = CreateItem("Exit", (_, _) =>
        {
            _exiting = true;
            Application.Current.Shutdown();
        });

        menu.Items.Add(status);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(_updateMenuItem);
        menu.Items.Add(open);
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(_autostartMenuItem);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(exit);
        menu.Items.Add(CreateVersionFooter());

        menu.Opened += (_, _) =>
        {
            RefreshStatus();
            UpdateAutostartChecked();
        };

        return menu;
    }

    private static MenuItem CreateItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = header };
        item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItemStyle");
        item.Click += click;
        return item;
    }

    /// <summary>
    /// Reacts to the checked state rather than Click: screen readers toggle the item through
    /// UI Automation, which flips IsChecked without raising Click.
    /// </summary>
    private MenuItem CreateToggleItem(string header, Action<bool> toggled)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItemStyle");
        item.Checked += (_, _) => OnToggled(true);
        item.Unchecked += (_, _) => OnToggled(false);
        return item;

        void OnToggled(bool isChecked)
        {
            if (!_syncingMenu)
                toggled(isChecked);
        }
    }

    /// <summary>Sets a toggle to reflect state without treating it as the user's choice.</summary>
    private void SyncChecked(MenuItem item, bool isChecked)
    {
        _syncingMenu = true;
        try
        {
            item.IsChecked = isChecked;
        }
        finally
        {
            _syncingMenu = false;
        }
    }

    /// <summary>A quiet last line, so "which version am I on?" has an answer next to Exit.</summary>
    private static MenuItem CreateVersionFooter()
    {
        var text = new TextBlock { Text = $"NVibrance {AppInfo.Version}", FontSize = 12 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var item = new MenuItem { Header = text };
        item.SetResourceReference(FrameworkElement.StyleProperty, "TrayStatusItemStyle");
        return item;
    }

    private static Separator CreateSeparator()
    {
        var separator = new Separator();
        separator.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuSeparatorStyle");
        return separator;
    }

    private void ShowMenu()
    {
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;

        // without owning the foreground, the menu would stay open after clicking elsewhere
        if (PresentationSource.FromVisual(_menu) is HwndSource source)
            NativeMethods.SetForegroundWindow(source.Handle);
    }

    private void SetPaused(bool paused)
    {
        if (_controller is not null)
            _controller.IsPaused = paused;
    }

    // ---------------------------------------------------------------- status

    private void OnStatusChanged(object? sender, EventArgs e) => RefreshStatus();

    /// <summary>
    /// Shows what is applied right now, in the menu's first line and the icon's tooltip,
    /// so a working switch can be confirmed without opening the window.
    /// </summary>
    private void RefreshStatus()
    {
        var (title, detail) = DescribeStatus();

        _statusTitle.Text = title;
        _statusDetail.Text = detail;
        SyncChecked(_pauseMenuItem, _controller?.IsPaused ?? false);

        var tooltip = $"NVibrance {AppInfo.Version}\n{title} · {detail}";
        _notifyIcon.Text = tooltip.Length > TooltipMaxLength ? tooltip[..(TooltipMaxLength - 1)] + "…" : tooltip;
        _tooltipRefreshedAtUtc = DateTime.UtcNow;
    }

    private (string Title, string Detail) DescribeStatus()
    {
        if (_controller is { IsPaused: true })
            return ("Paused", $"Desktop vibrance {ReadCurrentVibrance()} everywhere");

        if (_controller?.ActiveProfile is { } profile)
            return (profile.Name, $"Profile active · vibrance {profile.Vibrance}");

        return ("Desktop", $"No profile active · vibrance {ReadCurrentVibrance()}");
    }

    private string ReadCurrentVibrance()
    {
        try
        {
            return _vibrance.GetCurrent().ToString();
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read vibrance for the tray status: {ex.Message}");
            return "unavailable";
        }
    }

    // the tooltip has no "about to show" event; hovering the icon is the closest signal
    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (DateTime.UtcNow - _tooltipRefreshedAtUtc > TooltipRefreshInterval)
            RefreshStatus();
    }

    // ---------------------------------------------------------------- updates

    private void OnUpdateReady(object? sender, EventArgs e)
    {
        var version = _updates?.ReadyUpdate?.Version.ToString();
        _updateMenuItem.Header = $"Restart to update to {version}";
        _updateMenuItem.Visibility = Visibility.Visible;

        if (version is not null)
            NotifyUpdateOnce(version);
    }

    /// <summary>
    /// Updates install only when the user chooses to, and NVibrance usually sits hidden in the
    /// tray, so say so once per version; clicking the notification opens the window, which
    /// offers Restart and the release notes. Windows' Do Not Disturb holds it during games.
    /// </summary>
    private void NotifyUpdateOnce(string version)
    {
        var marker = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NVibrance",
            "update-notified");

        try
        {
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == version)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, version);
        }
        catch (Exception ex)
        {
            // without the marker the notification would repeat every launch; skip it instead
            Log.Warn($"Could not record the update notification: {ex.Message}");
            return;
        }

        _notifyIcon.ShowBalloonTip(
            8000,
            $"NVibrance {version} is ready",
            "Click to see what's new and restart to update when it suits you.",
            ToolTipIcon.None);
    }

    /// <summary>
    /// Starts Velopack's updater, then exits normally (saving profiles and restoring the
    /// desktop vibrance); the updater applies the update and starts the new version.
    /// </summary>
    private void RestartToUpdate()
    {
        var windowOpen = Application.Current.MainWindow is { IsVisible: true };
        if (_updates?.PrepareRestartToUpdate(startMinimized: !windowOpen) != true)
            return;

        _exiting = true;
        Application.Current.Shutdown();
    }

    // ---------------------------------------------------------------- autostart

    private void UpdateAutostartChecked()
    {
        bool enabled;
        try
        {
            enabled = AutoStartService.IsEnabled();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read autostart state: {ex.Message}");
            enabled = false;
        }

        SyncChecked(_autostartMenuItem, enabled);
    }

    private void SetAutostart(bool shouldEnable)
    {
        try
        {
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

    // ---------------------------------------------------------------- window

    // both notifications (update ready, still running) lead to the window
    private void OnBalloonTipClicked(object? sender, EventArgs e) => ShowMainWindow();

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowMainWindow();
        else if (e.Button == MouseButtons.Right)
            ShowMenu();
    }

    public void ShowMainWindow()
    {
        var app = Application.Current;

        if (app.MainWindow is not MainWindow window)
        {
            window = new MainWindow(_registry, _vibrance, _controller, _updates);
            window.HiddenToTray += (_, _) => ShowStillRunningHintOnce();
            window.RestartToUpdateRequested += (_, _) => RestartToUpdate();
            app.MainWindow = window;
        }

        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }

    /// <summary>
    /// The first time the window is closed, says that NVibrance kept running, so the app
    /// doesn't seem to have quit (or crashed). Shown once per user.
    /// </summary>
    private void ShowStillRunningHintOnce()
    {
        if (_exiting)
            return;

        var marker = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NVibrance",
            "tray-hint-shown");

        try
        {
            if (File.Exists(marker))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, string.Empty);
        }
        catch (Exception ex)
        {
            // without the marker the hint would repeat on every close; skip it instead
            Log.Warn($"Could not record the tray hint: {ex.Message}");
            return;
        }

        var text = _controller is { IsPaused: true }
            ? "Switching is paused. Right-click the tray icon to resume or exit."
            : "Profiles keep switching in the background. Right-click the tray icon to pause or exit.";

        _notifyIcon.ShowBalloonTip(5000, "NVibrance is still running", text, ToolTipIcon.None);
    }

    public void Dispose()
    {
        try
        {
            if (_controller is not null)
                _controller.StatusChanged -= OnStatusChanged;
            if (_updates is not null)
                _updates.UpdateReady -= OnUpdateReady;

            _menu.IsOpen = false;
            _notifyIcon.MouseClick -= OnMouseClick;
            _notifyIcon.MouseMove -= OnMouseMove;
            _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Error disposing tray icon: {ex.Message}");
        }
        GC.SuppressFinalize(this);
    }
}
