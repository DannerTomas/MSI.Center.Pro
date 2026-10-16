using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using MSICenterPro.Core;
using MSICenterPro.Services;
using MSICenterPro.ViewModels;

namespace MSICenterPro;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private int _currentPage = -1;

    public MainWindow()
    {
        // parse XAML first so the window shows up fast; the view-model (and the
        // sensor pipeline) initialises afterwards
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        Snackbar.MessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(3));

        _vm.ToastRequested += PlayToast;
        ThemeManager.ThemeChanged += OnThemeChanged;

        Loaded += (_, _) => NavList.SelectedIndex = 0;
        StateChanged += (_, _) =>
        {
            MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            // WindowChrome maximized windows get the same 8px frame inflation as
            // native ones; the margin keeps the client exactly inside the work area
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
        };
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    // ---------- navigation ----------

    private bool _navSwitchPending;

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            // Windows-style: the page switches when the button is released
            _navSwitchPending = true;
            return;
        }
        SwitchPage(NavList.SelectedIndex);
    }

    private void NavList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_navSwitchPending)
        {
            _navSwitchPending = false;
            SwitchPage(NavList.SelectedIndex);
        }
    }

    private void SwitchPage(int index)
    {
        if (index < 0 || index == _currentPage || index >= PageHost.Children.Count) return;
        _currentPage = index;

        for (int i = 0; i < PageHost.Children.Count; i++)
            ((UIElement)PageHost.Children[i]).Visibility =
                i == index ? Visibility.Visible : Visibility.Collapsed;

        EntranceAnim.PlayPageIn((FrameworkElement)PageHost.Children[index]);
        _vm.PageIndex = index;
    }

    // ---------- window buttons ----------

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaxRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- theme transition ----------

    private void OnThemeChanged(bool isDark)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.62,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)), ease));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)), ease));
        RootGrid.BeginAnimation(OpacityProperty, pulse);
    }

    // ---------- snackbar ----------

    private void PlayToast()
    {
        try
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = _vm.ToastTitle,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(_vm.ToastDetail))
            {
                content.Children.Add(new TextBlock
                {
                    Text = _vm.ToastDetail,
                    FontSize = 12,
                    Opacity = 0.85,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520
                });
            }
            Snackbar.MessageQueue?.Enqueue(content, "好", () => { });
        }
        catch (Exception ex)
        {
            // a toast must never take the app down; record instead
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msicenterpro_toast_error.txt"),
                    DateTime.Now.ToString("O") + " " + ex + Environment.NewLine);
            }
            catch
            {
                // best effort
            }
        }
    }
}
