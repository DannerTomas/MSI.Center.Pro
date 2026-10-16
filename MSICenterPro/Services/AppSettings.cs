using MSICenterPro.Core;

namespace MSICenterPro.Services;

/// <summary>
/// State container for the "基础设置" (Basic Settings) page,
/// mirroring MSI Center's notebook general settings.
/// </summary>
public sealed class AppSettings : ObservableObject
{
    public static AppSettings Instance { get; } = new();

    private bool _winKey = true;
    private bool _winFnSwap;
    private bool _overDrive;
    private bool _crosshair;
    private bool _hdr;
    private bool _powerSaving;
    private bool _usbPowerShare;
    private int _graphicsModeIndex;
    private int _sleepModeIndex;
    private int _lidActionIndex;

    /// <summary>Win 键开关</summary>
    public bool WinKey { get => _winKey; set => Set(ref _winKey, value); }

    /// <summary>Win/Fn 按键对调</summary>
    public bool WinFnSwap { get => _winFnSwap; set => Set(ref _winFnSwap, value); }

    /// <summary>提高显示器响应速度 (OD)</summary>
    public bool OverDrive { get => _overDrive; set => Set(ref _overDrive, value); }

    /// <summary>屏幕准星</summary>
    public bool Crosshair { get => _crosshair; set => Set(ref _crosshair, value); }

    /// <summary>HDR</summary>
    public bool Hdr { get => _hdr; set => Set(ref _hdr, value); }

    /// <summary>屏幕省电模式</summary>
    public bool PowerSaving { get => _powerSaving; set => Set(ref _powerSaving, value); }

    /// <summary>USB 电源共享</summary>
    public bool UsbPowerShare { get => _usbPowerShare; set => Set(ref _usbPowerShare, value); }

    /// <summary>显卡模式切换 0=独显直连 1=混合输出</summary>
    public int GraphicsModeIndex { get => _graphicsModeIndex; set => Set(ref _graphicsModeIndex, value); }

    /// <summary>休眠/睡眠切换 0=睡眠 1=休眠</summary>
    public int SleepModeIndex { get => _sleepModeIndex; set => Set(ref _sleepModeIndex, value); }

    /// <summary>合盖行为 0=预设 1=睡眠 2=休眠 3=关机 4=不采取任何操作</summary>
    public int LidActionIndex { get => _lidActionIndex; set => Set(ref _lidActionIndex, value); }

    public void ResetToDefaults()
    {
        WinKey = true;
        WinFnSwap = false;
        OverDrive = false;
        Crosshair = false;
        Hdr = false;
        PowerSaving = false;
        UsbPowerShare = false;
        GraphicsModeIndex = 0;
        SleepModeIndex = 0;
        LidActionIndex = 0;
    }
}
