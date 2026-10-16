using MSICenterPro.Core;

namespace MSICenterPro.Models;

/// <summary>
/// Mirrors UC_User_Scenario.Define_MonitoringData from MSI Center DCv2.
/// All values are real readings; a field is "--" when its source is unavailable.
/// </summary>
public class MonitorData : ObservableObject
{
    private double _cpuUsage;
    private double _gpuUsage;
    private double _memoryUsage;

    private string _cpuUsageText = "--";
    private string _gpuUsageText = "--";
    private string _cpuTemperature = "--";
    private string _gpuTemperature = "--";
    private string _cpuFanSpeed = "--";
    private string _gpuFanSpeed = "--";
    private string _cpuClock = "--";
    private string _gpuClock = "--";
    private string _gpuVram = "--";
    private string _cpuName = "正在检测...";
    private string _gpuName = "正在检测...";
    private string _modelName = "MSI";
    private string _greetingTitle = "您好，尊敬的机主。";
    private string _singleTflops = "--";
    private string _doubleGflops = "--";
    private string _gpuFan1 = "--";
    private string _ecStatus = string.Empty;

    private double _totalMemoryGb;
    private double _memoryUsedGb;
    private double _memoryAvailableGb;

    private string _diskName = "--";
    private double _diskFreeGb;
    private double _diskTotalGb;
    private double _diskUsagePercent;
    private bool _isSsd;

    public double CpuUsage { get => _cpuUsage; set => Set(ref _cpuUsage, value); }
    public double GpuUsage { get => _gpuUsage; set => Set(ref _gpuUsage, value); }
    public double MemoryUsage { get => _memoryUsage; set => Set(ref _memoryUsage, value); }

    /// <summary>"17 %" or "--" — shown inside the CPU ring.</summary>
    public string CpuUsageText { get => _cpuUsageText; set => Set(ref _cpuUsageText, value); }

    /// <summary>"8 %" or "--" — shown inside the GPU ring.</summary>
    public string GpuUsageText { get => _gpuUsageText; set => Set(ref _gpuUsageText, value); }

    public string CpuTemperature { get => _cpuTemperature; set => Set(ref _cpuTemperature, value); }
    public string GpuTemperature { get => _gpuTemperature; set => Set(ref _gpuTemperature, value); }
    public string CpuFanSpeed { get => _cpuFanSpeed; set => Set(ref _cpuFanSpeed, value); }
    public string GpuFanSpeed { get => _gpuFanSpeed; set => Set(ref _gpuFanSpeed, value); }
    public string CpuClock { get => _cpuClock; set => Set(ref _cpuClock, value); }
    public string GpuClock { get => _gpuClock; set => Set(ref _gpuClock, value); }
    public string GpuVram { get => _gpuVram; set => Set(ref _gpuVram, value); }
    public string CpuName { get => _cpuName; set => Set(ref _cpuName, value); }
    public string GpuName { get => _gpuName; set => Set(ref _gpuName, value); }
    public string ModelName { get => _modelName; set => Set(ref _modelName, value); }

    /// <summary>「下午好，尊敬的 Alpha 17 C7VF 机主。」 - rebuilt as the day passes.</summary>
    public string GreetingTitle { get => _greetingTitle; set => Set(ref _greetingTitle, value); }
    public string SingleTflops { get => _singleTflops; set => Set(ref _singleTflops, value); }
    public string DoubleGflops { get => _doubleGflops; set => Set(ref _doubleGflops, value); }
    public string GpuFan1 { get => _gpuFan1; set => Set(ref _gpuFan1, value); }

    /// <summary>Empty when the EC link is healthy, otherwise the real failure reason.</summary>
    public string EcStatus { get => _ecStatus; set => Set(ref _ecStatus, value); }

    public bool EcAvailable => string.IsNullOrEmpty(EcStatus);

    public double TotalMemoryGb { get => _totalMemoryGb; set => Set(ref _totalMemoryGb, value); }
    public double MemoryUsedGb { get => _memoryUsedGb; set => Set(ref _memoryUsedGb, value); }
    public double MemoryAvailableGb { get => _memoryAvailableGb; set => Set(ref _memoryAvailableGb, value); }

    public string DiskName { get => _diskName; set => Set(ref _diskName, value); }
    public double DiskFreeGb { get => _diskFreeGb; set => Set(ref _diskFreeGb, value); }
    public double DiskTotalGb { get => _diskTotalGb; set => Set(ref _diskTotalGb, value); }
    public double DiskUsagePercent { get => _diskUsagePercent; set => Set(ref _diskUsagePercent, value); }
    public bool IsSsd { get => _isSsd; set => Set(ref _isSsd, value); }
}
