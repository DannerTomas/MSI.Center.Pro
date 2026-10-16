using System.Windows.Threading;
using MSICenterPro.Core;
using MSICenterPro.Models;
using MSICenterPro.Services;

namespace MSICenterPro.ViewModels;

public class DashboardViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SystemMonitorService _monitor = SystemMonitorService.Instance;

    public DashboardViewModel()
    {
        Monitor = _monitor.Data;
        _monitor.Start();

        CleanDiskCommand = new RelayCommand(() => RunMaintenance(_monitor.CleanDisk));
        FreeMemoryCommand = new RelayCommand(() => RunMaintenance(_monitor.FreeMemory));
    }

    public MonitorData Monitor { get; }

    public RelayCommand CleanDiskCommand { get; }
    public RelayCommand FreeMemoryCommand { get; }

    public event Action<string, string>? ToastRequested;

    private void RunMaintenance(Func<(string title, string detail)> action)
    {
        _ = Task.Run(() =>
        {
            (string title, string detail) result;
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                result = ("操作失败", ex.Message);
            }
            _dispatcher.BeginInvoke(() => ToastRequested?.Invoke(result.title, result.detail));
        });
    }
}
