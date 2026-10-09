using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using NVibrance.Focus;
using NVibrance.Services;
using NVibrance.ViewModels;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using TextBox = System.Windows.Controls.TextBox;

namespace NVibrance.UI;

public partial class MainWindow
{
    private const int NewProfileVibrance = 80;

    private readonly ProgramRegistry _registry;
    private readonly DispatcherTimer _savedTimer;
    private readonly DispatcherTimer _undoTimer;

    private ProgramProfile? _lastDeleted;
    private bool _confirmNextSave;
    private bool _micaEnabled;
    private DateTime _popupClosedAtUtc;
    private bool _scanningProcesses;

    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow(ProgramRegistry registry, IVibranceService vibrance, VibranceController? controller = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        InitializeComponent();
        DataContext = new MainViewModel(registry, vibrance, controller);

        Closing += OnClosingHide;
        Activated += (_, _) => RefreshOnActivate();
        StateChanged += (_, _) => UpdateMaximizedState();

        _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _savedTimer.Tick += (_, _) =>
        {
            _savedTimer.Stop();
            Fade(SavedIndicator, 0, 300);
        };

        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _undoTimer.Tick += (_, _) =>
        {
            _undoTimer.Stop();
            _lastDeleted = null;
            HideUndoToast();
        };

        Vm.ProfileEdited += (_, _) => _confirmNextSave = true;
        _registry.Saved += Registry_Saved;

        // colour tokens are app-wide (see Theme); only the Mica background is the window's
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    /// <summary>Raised when the window is closed to the tray rather than exiting.</summary>
    public event EventHandler? HiddenToTray;

    // ---------------------------------------------------------------- window chrome

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var preference = (int)NativeMethods.DwmWindowCornerPreference.DwmwcpRound;
        var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref preference, sizeof(int));

        if (hr != 0)
        {
            Debug.WriteLine($"DwmSetWindowAttribute failed: 0x{hr:X8}");
        }

        TryEnableMica(hwnd);
    }

    /// <summary>
    /// Puts the Windows 11 Mica material behind the window, so the sidebar sits on the
    /// desktop-tinted surface and the detail pane reads as a layer above it.
    /// Falls back to the solid background on older builds and in high contrast.
    /// </summary>
    private void TryEnableMica(IntPtr hwnd)
    {
        if (Environment.OSVersion.Version.Build < 22621 || SystemParameters.HighContrast)
            return;

        var dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var backdrop = NativeMethods.DwmsbtMainWindow;
        var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        if (hr != 0)
        {
            Log.Debug($"Mica backdrop unavailable: 0x{hr:X8}");
            return;
        }

        // the backdrop only shows through a client area that is transparent and part of the frame
        WindowChrome.GetWindowChrome(this).GlassFrameThickness = new Thickness(-1);
        var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;

        _micaEnabled = true;
        Background = Brushes.Transparent;
        RootBorder.BorderThickness = new Thickness(0); // DWM draws the border
    }

    private void UpdateMaximizedState()
    {
        var maximized = WindowState == WindowState.Maximized;

        // a maximized WindowChrome window overhangs the screen by its resize border
        RootBorder.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void OnClosingHide(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The window is hidden rather than closed, so values read when it was created go stale:
    /// re-read the desktop vibrance, and retry NVAPI if no display was found last time.
    /// </summary>
    private void RefreshOnActivate()
    {
        if (Vm.HasDisplay)
            Vm.RefreshDesktopVibrance();
        else
            Vm.RefreshDisplays();
    }

    // ---------------------------------------------------------------- theme

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.BeginInvoke(UpdateBackground);
    }

    /// <summary>High contrast needs the solid system background even when Mica is on.</summary>
    private void UpdateBackground()
    {
        if (SystemParameters.HighContrast)
            SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        else if (_micaEnabled)
            Background = Brushes.Transparent;
    }

    // ---------------------------------------------------------------- motion

    /// <summary>
    /// Windows' "Animation effects" setting. When it is off, movement (slides, scaling)
    /// is skipped; short opacity fades remain because they carry no motion.
    /// </summary>
    private static bool MotionEnabled => SystemParameters.ClientAreaAnimation;

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Animates from the current on-screen value (or <paramref name="from"/> when entering),
    /// so a transition can be reversed mid-flight without a jump.
    /// </summary>
    private static void Animate(IAnimatable target, DependencyProperty property, double to, int milliseconds,
        EventHandler? completed = null, double? from = null)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            From = from,
            EasingFunction = EaseOut
        };
        if (completed is not null)
            animation.Completed += completed;

        target.BeginAnimation(property, animation);
    }

    private static void Fade(UIElement element, double to, int milliseconds, EventHandler? completed = null)
        => Animate(element, OpacityProperty, to, milliseconds, completed);

    // ---------------------------------------------------------------- saved feedback

    private void Registry_Saved(object? sender, EventArgs e)
    {
        // raised on the registry's timer thread
        Dispatcher.BeginInvoke(() =>
        {
            if (!_confirmNextSave)
                return;

            _confirmNextSave = false;
            Fade(SavedIndicator, 1, 150);
            _savedTimer.Stop();
            _savedTimer.Start();
        });
    }

    // ---------------------------------------------------------------- sidebar

    private void ResumeSwitching_Click(object sender, RoutedEventArgs e) => Vm.IsSwitchingPaused = false;

    private void EntriesList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Vm.IsProfileSelected)
        {
            DeleteSelectedProfile();
            e.Handled = true;
        }
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(EntriesList, (DependencyObject)e.OriginalSource) is ListBoxItem)
            BeginRename();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && !NameBox.IsKeyboardFocused)
        {
            BeginRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control
                 && _lastDeleted is not null && Keyboard.FocusedElement is not TextBox)
        {
            UndoDelete();
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- add program

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        // a click on the button while the picker is open first closes it (StaysOpen=False);
        // don't immediately reopen it
        if (DateTime.UtcNow - _popupClosedAtUtc < TimeSpan.FromMilliseconds(200))
            return;

        AddPopup.IsOpen = true;
    }

    private async void AddPopup_Opened(object? sender, EventArgs e)
    {
        ProcessSearch.Text = string.Empty;

        Animate(AddPopupContent, OpacityProperty, 1, 120, from: 0);

        if (MotionEnabled)
        {
            Animate(AddPopupScale, ScaleTransform.ScaleXProperty, 1, 160, from: 0.96);
            Animate(AddPopupScale, ScaleTransform.ScaleYProperty, 1, 160, from: 0.96);
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(ProcessSearch));

        await RefreshProcessesAsync();
    }

    private void AddPopup_Closed(object? sender, EventArgs e) => _popupClosedAtUtc = DateTime.UtcNow;

    /// <summary>
    /// Scans running apps off the UI thread (resolving every process's path is slow),
    /// so the picker opens and accepts typing immediately.
    /// </summary>
    private async Task RefreshProcessesAsync()
    {
        _scanningProcesses = true;
        ProcessList.ItemsSource = null;
        OnProcessFilterChanged();

        IReadOnlyList<RunningProgram> scanned;
        try
        {
            scanned = await Task.Run(() => RunningProcessScanner.GetUserProcesses(loadIcons: false));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to list running applications.", ex);
            scanned = [];
        }
        finally
        {
            _scanningProcesses = false;
        }

        // apps that already have a profile are left out (the sidebar has them), as is NVibrance;
        // icons load here because WPF images belong to the thread that creates them
        var programs = scanned
            .Where(p => _registry.FindByExePath(p.ExePath) is null)
            .Where(p => !string.Equals(p.ExePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            .Select(p => p with { Icon = ExeIconCache.Get(p.ExePath) })
            .ToList();

        var view = CollectionViewSource.GetDefaultView(programs);
        view.Filter = MatchesSearch;
        ProcessList.ItemsSource = view;
        OnProcessFilterChanged();
    }

    private bool MatchesSearch(object item)
    {
        var query = ProcessSearch.Text.Trim();
        return item is RunningProgram program
               && (query.Length == 0 || program.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void ProcessSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        (ProcessList.ItemsSource as ICollectionView)?.Refresh();
        OnProcessFilterChanged();
    }

    private void OnProcessFilterChanged()
    {
        // keep the top match selected so Enter adds it straight from the search box
        ProcessList.SelectedIndex = ProcessList.Items.Count > 0 ? 0 : -1;

        EmptyProcessesText.Text = _scanningProcesses
            ? "Looking for running apps…"
            : "No matching apps with a window are running. Browse for the .exe instead.";
        EmptyProcessesText.Visibility = ProcessList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProcessSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when ProcessList.Items.Count > 0:
                ProcessList.SelectedIndex = 0;
                ProcessList.UpdateLayout();
                (ProcessList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                AddProgram(ProcessList.SelectedItem as RunningProgram);
                e.Handled = true;
                break;
            case Key.Escape:
                AddPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void ProcessList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddProgram(ProcessList.SelectedItem as RunningProgram);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AddPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void ProcessItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => AddProgram((sender as ListBoxItem)?.DataContext as RunningProgram);

    private void AddProgram(RunningProgram? program)
    {
        if (program is null)
            return;

        AddPopup.IsOpen = false;
        AddProfile(program.Name, program.ExePath);
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        AddPopup.IsOpen = false;

        var dlg = new OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe",
            Title = "Select game executable"
        };

        if (dlg.ShowDialog(this) != true)
            return;

        var exePath = dlg.FileName!;
        AddProfile(System.IO.Path.GetFileNameWithoutExtension(exePath), exePath);
    }

    private void AddProfile(string name, string exePath)
    {
        Vm.AddOrSelectProfile(name, exePath, NewProfileVibrance);
        EntriesList.ScrollIntoView(Vm.SelectedEntry);
    }

    // ---------------------------------------------------------------- rename

    private void Rename_Click(object sender, RoutedEventArgs e) => BeginRename();

    private void BeginRename()
    {
        if (!Vm.IsProfileSelected)
            return;

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitName();
            FocusSelectedEntry();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            NameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            FocusSelectedEntry();
            e.Handled = true;
        }
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitName();

    private void CommitName()
    {
        Vm.RenameSelectedProfile(NameBox.Text);

        // a blank name is rejected; show the real name again either way
        NameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void FocusSelectedEntry()
    {
        if (EntriesList.ItemContainerGenerator.ContainerFromItem(Vm.SelectedEntry) is ListBoxItem item)
            item.Focus();
        else
            EntriesList.Focus();
    }

    // ---------------------------------------------------------------- delete & undo

    private void DeleteProfile_Click(object sender, RoutedEventArgs e) => DeleteSelectedProfile();

    /// <summary>Deletes right away and offers undo, rather than asking for confirmation first.</summary>
    private void DeleteSelectedProfile()
    {
        var removed = Vm.DeleteSelectedProfile();
        if (removed is null)
            return;

        _lastDeleted = removed;
        UndoText.Text = $"Deleted {removed.Name}";
        ShowUndoToast();

        _undoTimer.Stop();
        _undoTimer.Start();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoDelete();

    private void UndoDelete()
    {
        if (_lastDeleted is null)
            return;

        Vm.RestoreProfile(_lastDeleted);
        EntriesList.ScrollIntoView(Vm.SelectedEntry);

        _lastDeleted = null;
        _undoTimer.Stop();
        HideUndoToast();
    }

    // hovering the toast holds it open, so it can't vanish under the pointer
    private void UndoToast_MouseEnter(object sender, MouseEventArgs e) => _undoTimer.Stop();

    private void UndoToast_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_lastDeleted is not null)
            _undoTimer.Start();
    }

    private void ShowUndoToast()
    {
        // already showing (a second delete): keep it in place instead of re-entering
        var entering = UndoToast.Visibility != Visibility.Visible;

        UndoToast.Visibility = Visibility.Visible;
        Fade(UndoToast, 1, 180);

        if (MotionEnabled)
            Animate(UndoToastOffset, TranslateTransform.YProperty, 0, 220, from: entering ? 8 : null);
    }

    private void HideUndoToast()
    {
        Fade(UndoToast, 0, 150, (_, _) =>
        {
            // a new delete may have brought it back while this fade ran
            if (_lastDeleted is null)
                UndoToast.Visibility = Visibility.Collapsed;
        });

        if (MotionEnabled)
            Animate(UndoToastOffset, TranslateTransform.YProperty, 8, 150);
    }

    protected override void OnClosed(EventArgs e)
    {
        _registry.Saved -= Registry_Saved;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        base.OnClosed(e);
    }
}
