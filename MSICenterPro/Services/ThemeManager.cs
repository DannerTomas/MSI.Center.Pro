using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace MSICenterPro.Services;

/// <summary>
/// Theme switching on top of MaterialDesignThemes: light/dark base theme with
/// the MSI red (#E4002B) as primary color, plus a few derived brushes used by
/// the custom ring gauge (Material Design has no determinate circular gauge).
/// </summary>
public static class ThemeManager
{
    /// <summary>MSI brand red.</summary>
    public static readonly Color MsiRed = Color.FromRgb(0xE4, 0x00, 0x2B);

    private static readonly Color SecondarySlate = Color.FromRgb(0x5E, 0x64, 0x77);

    private static bool _applied;

    public static bool IsDark { get; private set; }
    public static event Action<bool>? ThemeChanged;

    public static void Initialize()
    {
        IsDark = DetectSystemDark();
        Apply(IsDark);
        _applied = true;
        ThemeChanged?.Invoke(IsDark);
    }

    public static void SetTheme(bool dark)
    {
        bool changed = IsDark != dark || !_applied;
        IsDark = dark;
        Apply(dark);
        _applied = true;
        if (changed) ThemeChanged?.Invoke(dark);
    }

    public static void Toggle() => SetTheme(!IsDark);

    private static void Apply(bool dark)
    {
        var helper = new PaletteHelper();
        Theme theme = helper.GetTheme();
        theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
        theme.SetPrimaryColor(MsiRed);
        theme.SetSecondaryColor(SecondarySlate);
        helper.SetTheme(theme);

        var res = Application.Current?.Resources;
        if (res is null) return;

        res["App.RingTrack"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x3A, 0x3F, 0x4A) : Color.FromRgb(0xE1, 0xE5, 0xEF));

        var value = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        value.GradientStops.Add(new GradientStop(
            dark ? Color.FromRgb(0xFF, 0x5A, 0x6E) : Color.FromRgb(0xF0, 0x5E, 0x6B), 0));
        value.GradientStops.Add(new GradientStop(MsiRed, 1));
        res["App.RingValue"] = value;

        // primary red is hard to read as thin text on dark surfaces; brighten it there
        res["App.AccentText"] = new SolidColorBrush(
            dark ? Color.FromRgb(0xFF, 0x77, 0x88) : MsiRed);

        // Win10-style caption button states
        res["App.CaptionHover"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x38, 0x38, 0x3A) : Color.FromRgb(0xE7, 0xE7, 0xE7));
        res["App.CaptionPressed"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x46, 0x46, 0x48) : Color.FromRgb(0xD8, 0xD8, 0xD8));

        // visible ripple feedback for buttons whose BorderBrush is transparent
        res["App.Ripple"] = new SolidColorBrush(
            dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00));
    }

    private static bool DetectSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i == 0;
        }
        catch
        {
            // fall through to light theme
        }
        return false;
    }
}
