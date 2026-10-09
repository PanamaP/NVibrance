using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;

namespace NVibrance.UI;

/// <summary>
/// Keeps the colour tokens from Theme.xaml in step with high contrast: while a high
/// contrast theme is on, every token is replaced by the matching system colour.
/// App-wide, so the tray menu follows it even when the window was never opened.
/// </summary>
public static class Theme
{
    private static readonly Dictionary<object, object> Defaults = new();
    private static ResourceDictionary? _resources;

    public static void Initialize(ResourceDictionary resources)
    {
        _resources = resources;
        foreach (var key in HighContrastTokens().Keys)
            Defaults[key] = resources[key];

        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Apply();
    }

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Application.Current.Dispatcher.BeginInvoke(Apply);
    }

    private static void Apply()
    {
        if (_resources is null)
            return;

        var tokens = SystemParameters.HighContrast ? HighContrastTokens() : Defaults;
        foreach (var (key, value) in tokens)
            _resources[key] = value;
    }

    private static Dictionary<object, object> HighContrastTokens() => new()
    {
        ["WindowBackgroundBrush"] = SystemColors.WindowBrush,
        ["LayerBrush"] = SystemColors.WindowBrush,
        ["LayerBorderBrush"] = SystemColors.WindowTextBrush,
        ["CardBrush"] = SystemColors.WindowBrush,
        ["CardBorderBrush"] = SystemColors.WindowTextBrush,
        ["TextBrush"] = SystemColors.WindowTextBrush,
        ["MutedBrush"] = SystemColors.WindowTextBrush,
        ["SelectedTextBrush"] = SystemColors.HighlightTextBrush,
        ["HoverFillBrush"] = Brushes.Transparent,
        ["PressedFillBrush"] = Brushes.Transparent,
        ["SelectedFillBrush"] = SystemColors.HighlightBrush,
        ["BadgeBrush"] = Brushes.Transparent,
        ["ControlFillBrush"] = SystemColors.ControlBrush,
        ["ControlHoverBrush"] = SystemColors.ControlBrush,
        ["ControlPressedBrush"] = SystemColors.ControlBrush,
        ["ControlBorderBrush"] = SystemColors.ControlTextBrush,
        ["AccentBrush"] = SystemColors.HighlightBrush,
        ["AccentHoverBrush"] = SystemColors.HighlightBrush,
        ["AccentPressedBrush"] = SystemColors.HighlightBrush,
        ["OnAccentBrush"] = SystemColors.HighlightTextBrush,
        ["TrackBrush"] = SystemColors.GrayTextBrush,
        ["ThumbBrush"] = SystemColors.WindowBrush,
        ["ThumbBorderBrush"] = SystemColors.WindowTextBrush,
        ["SuccessBrush"] = SystemColors.WindowTextBrush,
        ["DangerBrush"] = SystemColors.ControlTextBrush,
        ["CloseHoverBrush"] = SystemColors.HighlightBrush,
        ["CloseHoverTextBrush"] = SystemColors.HighlightTextBrush,
        ["PopupBrush"] = SystemColors.WindowBrush,
        ["PopupBorderBrush"] = SystemColors.WindowTextBrush,
        ["InputBrush"] = SystemColors.WindowBrush,
        ["FocusRingBrush"] = SystemColors.WindowTextBrush,
        ["ScrollThumbBrush"] = SystemColors.WindowTextBrush,
    };
}
