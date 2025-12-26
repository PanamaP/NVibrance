using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic;
using NVibrance.ViewModels;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace NVibrance;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow(ProgramRegistry registry)
    {
        InitializeComponent();
        DataContext = new MainViewModel(registry);

        Closing += OnClosingHide;
        RefreshProcesses();
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

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void OnClosingHide(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void RefreshProcesses()
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
}