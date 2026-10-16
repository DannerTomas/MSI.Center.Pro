using MSICenterPro.Core;
using MSICenterPro.Services;

namespace MSICenterPro.ViewModels;

public class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        Settings = AppSettings.Instance;
        IsDarkMode = ThemeManager.IsDark;

        ResetCommand = new RelayCommand(() =>
        {
            Settings.ResetToDefaults();
            ToastRequested?.Invoke("MSI Center 设置", "已恢复为默认设置");
        });

        ThemeManager.ThemeChanged += _ => OnPropertyChanged(nameof(IsDarkMode));
    }

    public AppSettings Settings { get; }

    public bool IsDarkMode
    {
        get => ThemeManager.IsDark;
        set
        {
            if (ThemeManager.IsDark == value) return;
            ThemeManager.SetTheme(value);
            OnPropertyChanged();
        }
    }

    public RelayCommand ResetCommand { get; }

    public event Action<string, string>? ToastRequested;
}
