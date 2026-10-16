using MSICenterPro.Core;

namespace MSICenterPro.ViewModels;

public class MainViewModel : ObservableObject
{
    private int _pageIndex;

    public MainViewModel()
    {
        Dashboard = new DashboardViewModel();
        Performance = new PerformanceViewModel();
        Settings = new SettingsViewModel();

        Dashboard.ToastRequested += (t, d) => ShowToast(t, d);
        Performance.ToastRequested += (t, d) => ShowToast(t, d);
        Settings.ToastRequested += (t, d) => ShowToast(t, d);
    }

    public DashboardViewModel Dashboard { get; }
    public PerformanceViewModel Performance { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>0=看板 1=性能 2=设置</summary>
    public int PageIndex
    {
        get => _pageIndex;
        set => Set(ref _pageIndex, value);
    }

    // ---------- Snackbar ----------

    private string _toastTitle = string.Empty;
    private string _toastDetail = string.Empty;

    public string ToastTitle { get => _toastTitle; private set => Set(ref _toastTitle, value); }
    public string ToastDetail { get => _toastDetail; private set => Set(ref _toastDetail, value); }

    public event Action? ToastRequested;

    public void ShowToast(string title, string detail)
    {
        ToastTitle = title;
        ToastDetail = detail;
        ToastRequested?.Invoke();
    }
}
