using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using NVibrance.ViewModels;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace NVibrance.UI;

public partial class MainWindow
{
    private readonly ProgramRegistry _registry;
    private readonly DispatcherTimer _saveTimer;
    
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow(ProgramRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        
        InitializeComponent();
        DataContext = new MainViewModel(registry);

        Closing += OnClosingHide;
        RefreshProcesses();
        
        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavedIndicator.Visibility = Visibility.Collapsed;
        };

        _registry.Saved += Registry_Saved;
    }
    
    // Apply Windows 11 rounded corners via DWM after window is initialized
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
    }
    
    private void Registry_Saved(object? sender, EventArgs e)
    {
        // ensure UI thread
        Dispatcher.Invoke(() =>
        {
            SavedIndicator.Visibility = Visibility.Visible;
            _saveTimer.Stop();
            _saveTimer.Start();
        });
    }

    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnCaptionControl(e.OriginalSource as DependencyObject))
            return;

        DragMove();
    }

    private static bool IsClickOnCaptionControl(DependencyObject? origin)
    {
        while (origin != null)
        {
            if (origin is System.Windows.Controls.Button) return true;
            origin = VisualTreeHelper.GetParent(origin);
        }
        return false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void OnClosingHide(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private  void RefreshProcesses()
    {
        ProcessList.ItemsSource = RunningProcessScanner.GetUserProcesses();
    }

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e) => RefreshProcesses();

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is not RunningProgram program)
            return;

        Vm.AddOrSelectProfile(program.Name, program.ExePath, vibrance: 80);
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe",
            Title = "Select game executable"
        };

        if (dlg.ShowDialog(this) != true)
            return;

        var exePath = dlg.FileName!;
        var name = System.IO.Path.GetFileNameWithoutExtension(exePath);
        Vm.AddOrSelectProfile(name, exePath, vibrance: 80);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedProfile is null)
            return;

        var name = Vm.SelectedProfile.Name;

        var result = MessageBox.Show(
            this,
            $"Delete profile {name}?",
            "NVibrance",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        Vm.DeleteSelectedProfile();
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedProfile is null)
            return;

        var current = Vm.SelectedProfile.Name;

        var input = Interaction.InputBox(
            "Enter a new name:",
            "Rename profile",
            current);

        if (string.IsNullOrWhiteSpace(input))
            return;

        Vm.RenameSelectedProfile(input);
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _registry.Saved -= Registry_Saved;
        base.OnClosed(e);
    }
}