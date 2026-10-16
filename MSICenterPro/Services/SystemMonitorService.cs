using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MSICenterPro.Models;

namespace MSICenterPro.Services;

/// <summary>
/// Samples live system metrics. Every value is a real reading:
///   CPU usage / clock  — Win32 + performance counters
///   GPU usage          — "GPU Engine \ engtype_3D" performance counters
///   GPU clock / VRAM   — nvidia-smi
///   temps / fan speeds — MSI EC via <see cref="MsiWmiService"/>
///   RAM / disk         — Win32
/// A metric whose source is unavailable is reported as "--" (never simulated).
/// </summary>
public sealed class SystemMonitorService
{
    public static SystemMonitorService Instance { get; } = new();

    public MonitorData Data { get; } = new();

    private DispatcherTimer? _timer;
    private bool _started;

    private readonly MsiWmiService _wmi = MsiWmiService.Instance;
    private int _wmiBusy;
    private int _nvSmiBusy;

    private long _prevIdle, _prevKernel, _prevUser;
    private bool _cpuPrimed;

    private PerformanceCounter? _cpuPerf;
    private bool _cpuPerfFailed;
    private double _cpuBaseMhz;

    private readonly Dictionary<string, PerformanceCounter> _gpu3d = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gpuGate = new();
    private int _gpuSampleBusy;
    private DateTime _gpuCountersBuiltAt = DateTime.MinValue;
    private string? _gpuCounterError;

    private DateTime _lastNvSmi = DateTime.MinValue;
    private DateTime _lastDiskInfo = DateTime.MinValue;

    private SystemMonitorService() { }

    public void Start()
    {
        if (_started) return;
        _started = true;

        LoadStaticInfo();
        _cpuPrimed = false;
        _lastNvSmi = DateTime.MinValue;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => SampleDynamic();
        _timer.Start();

        // warm up the performance counters off the UI thread while the window shows
        _ = Task.Run(() => { SampleCpuClock(); SampleGpuUsage(); });

        // defer the first sample until the UI is idle so the window paints first
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle, new Action(SampleDynamic));
    }

    // ---------- static hardware info ----------

    private void LoadStaticInfo()
    {
        Data.CpuName = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Trim()
                       ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
        Data.GpuName = DetectGpuName();
        Data.ModelName = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName")?.Trim() ?? "MSI";
        UpdateGreeting();
        ApplyTflops(Data.GpuName);
        LoadDiskInfo();

        try
        {
            object? mhz = Registry.LocalMachine
                .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?.GetValue("~MHz");
            if (mhz is int i && i > 0) _cpuBaseMhz = i;
        }
        catch
        {
            _cpuBaseMhz = 0;
        }
    }

    private void UpdateGreeting()
    {
        string greeting = DateTime.Now.Hour switch
        {
            >= 5 and < 11 => "早上好",
            >= 11 and < 13 => "中午好",
            >= 13 and < 18 => "下午好",
            _ => "晚上好"
        };
        Data.GreetingTitle = $"{greeting}，尊敬的 {Data.ModelName} 机主。";
    }

    private static string? ReadRegistryString(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string DetectGpuName()
    {
        const string displayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        string? first = null;
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(displayClass);
            if (root is null) return "Unknown GPU";
            foreach (var sub in root.GetSubKeyNames())
            {
                if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;
                using var key = root.OpenSubKey(sub);
                if (key?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                if (desc.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)) continue;
                desc = desc.Trim();
                if (desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                {
                    if (desc.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) && first is null)
                        first = desc; // integrated APU graphics as fallback
                    else
                        return desc;
                }
                first ??= desc;
            }
        }
        catch
        {
            // ignore, fallback below
        }
        return first ?? "Unknown GPU";
    }

    private void ApplyTflops(string gpuName)
    {
        (string single, string @double)? spec = gpuName switch
        {
            _ when gpuName.Contains("4090", StringComparison.OrdinalIgnoreCase) => ("38.90", "608"),
            _ when gpuName.Contains("4080", StringComparison.OrdinalIgnoreCase) => ("32.80", "512"),
            _ when gpuName.Contains("4070", StringComparison.OrdinalIgnoreCase) => ("20.30", "317"),
            _ when gpuName.Contains("4060", StringComparison.OrdinalIgnoreCase) => ("15.11", "236"),
            _ when gpuName.Contains("3090", StringComparison.OrdinalIgnoreCase) => ("35.58", "556"),
            _ when gpuName.Contains("3080", StringComparison.OrdinalIgnoreCase) => ("29.77", "465"),
            _ when gpuName.Contains("3070", StringComparison.OrdinalIgnoreCase) => ("20.31", "317"),
            _ when gpuName.Contains("3060", StringComparison.OrdinalIgnoreCase) => ("12.74", "199"),
            _ when gpuName.Contains("2080", StringComparison.OrdinalIgnoreCase) => ("13.45", "420"),
            _ when gpuName.Contains("2070", StringComparison.OrdinalIgnoreCase) => ("7.49", "234"),
            _ => null
        };
        Data.SingleTflops = spec?.single ?? "--";
        Data.DoubleGflops = spec?.@double ?? "--";
    }

    private void LoadDiskInfo()
    {
        try
        {
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(systemRoot);
            if (!drive.IsReady) return;
            Data.DiskName = drive.Name.TrimEnd('\\') + " " + (drive.DriveFormat ?? "");
            Data.DiskTotalGb = drive.TotalSize / 1000.0 / 1000.0 / 1000.0;
            Data.DiskFreeGb = drive.TotalFreeSpace / 1000.0 / 1000.0 / 1000.0;
            Data.DiskUsagePercent = 100.0 * (1.0 - (double)drive.TotalFreeSpace / drive.TotalSize);
        }
        catch
        {
            // keep defaults
        }
    }

    // ---------- dynamic sampling (UI timer, 1 s) ----------

    private void SampleDynamic()
    {
        UpdateGreeting();
        SampleCpuUsage();
        SampleCpuClock();
        SampleGpuUsage();
        SampleMemory();

        if (DateTime.Now - _lastDiskInfo > TimeSpan.FromSeconds(5))
        {
            _lastDiskInfo = DateTime.Now;
            LoadDiskInfo();
        }

        KickWmi();
        if (DateTime.Now - _lastNvSmi >= TimeSpan.FromSeconds(2))
        {
            _lastNvSmi = DateTime.Now;
            KickNvSmi();
        }
    }

    private void SampleCpuUsage()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return;
        if (!_cpuPrimed)
        {
            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
            _cpuPrimed = true;
            return;
        }
        long idleD = idle - _prevIdle;
        long kernelD = kernel - _prevKernel;
        long userD = user - _prevUser;
        _prevIdle = idle; _prevKernel = kernel; _prevUser = user;

        long total = kernelD + userD;
        if (total <= 0) return;
        double usage = Math.Clamp((1.0 - (double)idleD / total) * 100.0, 0, 100);
        Data.CpuUsage = Math.Round(usage);
        Data.CpuUsageText = $"{Data.CpuUsage:0} %";
    }

    private int _cpuClockBusy;

    private void SampleCpuClock()
    {
        if (_cpuPerfFailed) return;
        if (Interlocked.CompareExchange(ref _cpuClockBusy, 1, 0) != 0) return;

        // the first PerformanceCounter construction loads the whole perf-counter
        // subsystem (~1-4 s) - never do that on the UI thread
        _ = Task.Run(() =>
        {
            string? text = null;
            bool failed = false;
            try
            {
                _cpuPerf ??= new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                float perf = _cpuPerf.NextValue();
                if (perf > 0 && _cpuBaseMhz > 0)
                {
                    double ghz = perf / 100.0 * _cpuBaseMhz / 1000.0;
                    if (ghz > 0.1) text = ghz.ToString("0.00") + " GHz";
                }
            }
            catch
            {
                failed = true;
                try { _cpuPerf?.Dispose(); } catch { /* ignore */ }
                _cpuPerf = null;
            }

            void Apply()
            {
                if (text != null)
                {
                    Data.CpuClock = text;
                }
                else if (failed)
                {
                    _cpuPerfFailed = true;
                    Data.CpuClock = "--";
                }
                Interlocked.Exchange(ref _cpuClockBusy, 0);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(Apply);
            else
                Apply();
        });
    }

    private void SampleGpuUsage()
    {
        // enumerating the "GPU Engine" category can take a second or two on
        // first use - keep it off the UI thread entirely
        if (Interlocked.CompareExchange(ref _gpuSampleBusy, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            double usage = 0;
            bool ok;
            string? error = null;
            try
            {
                ok = ComputeGpuUsage(out usage);
            }
            catch (Exception ex)
            {
                ok = false;
                error = ex.Message;
            }

            void Apply()
            {
                if (ok)
                {
                    Data.GpuUsage = Math.Round(usage);
                    Data.GpuUsageText = $"{Data.GpuUsage:0} %";
                }
                else
                {
                    if (error != null) _gpuCounterError = error;
                    Data.GpuUsage = 0;
                    Data.GpuUsageText = "--";
                }
                Interlocked.Exchange(ref _gpuSampleBusy, 0);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(Apply);
            else
                Apply();
        });
    }

    private bool ComputeGpuUsage(out double usage)
    {
        usage = 0;
        lock (_gpuGate)
        {
            if (DateTime.Now - _gpuCountersBuiltAt > TimeSpan.FromSeconds(30))
                RebuildGpuCounters();

            if (_gpu3d.Count == 0)
            {
                _gpuCounterError = "未找到 3D 引擎性能计数器";
                return false;
            }

            double sum = 0;
            foreach (var pc in _gpu3d.Values)
                sum += pc.NextValue();
            usage = Math.Clamp(sum, 0, 100);
            return true;
        }
    }

    private void RebuildGpuCounters()
    {
        var category = new PerformanceCounterCategory("GPU Engine");
        var instances = category.GetInstanceNames()
            .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // drop counters for instances that no longer exist
        foreach (var gone in _gpu3d.Keys.Where(k => !instances.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray())
        {
            try { _gpu3d[gone].Dispose(); } catch { /* ignore */ }
            _gpu3d.Remove(gone);
        }

        // add newly appeared instances
        foreach (var name in instances)
        {
            if (_gpu3d.ContainsKey(name)) continue;
            try
            {
                var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true);
                pc.NextValue(); // prime
                _gpu3d[name] = pc;
            }
            catch
            {
                // instance disappeared between enumeration and creation; skip
            }
        }

        _gpuCountersBuiltAt = DateTime.Now;
        _gpuCounterError = _gpu3d.Count == 0 ? "未找到 3D 引擎性能计数器" : null;
    }

    private void SampleMemory()
    {
        if (!TryGetMemory(out double totalGb, out double usedGb, out double load)) return;
        Data.TotalMemoryGb = Math.Round(totalGb);
        Data.MemoryUsedGb = Math.Round(usedGb, 1);
        Data.MemoryAvailableGb = Math.Round(totalGb - usedGb, 1);
        Data.MemoryUsage = Math.Round(load);
    }

    // ---------- EC sensors via WMI (background) ----------

    private void KickWmi()
    {
        if (Interlocked.CompareExchange(ref _wmiBusy, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try { SampleWmiSensors(); }
            finally { Interlocked.Exchange(ref _wmiBusy, 0); }
        });
    }

    private void SampleWmiSensors()
    {
        var temps = _wmi.ReadTemperatures();
        var fans = _wmi.ReadFanRpm();
        string? err = _wmi.LastError;

        void Apply()
        {
            if (temps is { } t)
            {
                Data.CpuTemperature = t.Cpu + " °C";
                Data.GpuTemperature = t.Gpu + " °C";
            }
            else
            {
                Data.CpuTemperature = "--";
                Data.GpuTemperature = "--";
            }

            if (fans is { } f)
            {
                Data.CpuFanSpeed = f.Cpu + " RPM";
                Data.GpuFanSpeed = f.Gpu + " RPM";
                Data.GpuFan1 = f.Gpu + " RPM";
            }
            else
            {
                Data.CpuFanSpeed = "--";
                Data.GpuFanSpeed = "--";
                Data.GpuFan1 = "--";
            }

            Data.EcStatus = temps is null && fans is null
                ? "EC 读取失败：" + (err ?? "未知原因")
                : string.Empty;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(Apply);
        else
            Apply();
    }

    // ---------- nvidia-smi (background, 2 s) ----------

    private void KickNvSmi()
    {
        if (Interlocked.CompareExchange(ref _nvSmiBusy, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try { SampleNvSmi(); }
            finally { Interlocked.Exchange(ref _nvSmiBusy, 0); }
        });
    }

    private void SampleNvSmi()
    {
        string? clock = null, vram = null, error = null;
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=clocks.current.graphics,clocks.current.memory --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                error = "无法启动 nvidia-smi";
            }
            else if (!p.WaitForExit(4000))
            {
                try { p.Kill(); } catch { /* ignore */ }
                error = "nvidia-smi 超时";
            }
            else if (p.ExitCode != 0)
            {
                error = "nvidia-smi 退出码 " + p.ExitCode;
            }
            else
            {
                var parts = p.StandardOutput.ReadToEnd().Trim()
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
                {
                    clock = parts[0] + " MHz";
                    vram = parts[1] + " MHz";
                }
                else
                {
                    error = "无法解析 nvidia-smi 输出";
                }
            }
        }
        catch (Exception ex)
        {
            error = "nvidia-smi 调用失败：" + ex.Message;
        }

        void Apply()
        {
            Data.GpuClock = clock ?? "--";
            Data.GpuVram = vram ?? "--";
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(Apply);
        else
            Apply();
    }

    // ---------- maintenance actions (real) ----------

    /// <summary>Deletes user/Windows temp files, reporting actual freed space or the real error.</summary>
    public (string title, string detail) CleanDisk()
    {
        var roots = new List<string> { Path.GetTempPath() };
        try
        {
            string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (Directory.Exists(winTemp)) roots.Add(winTemp);
        }
        catch
        {
            // Windows folder unknown; user temp only
        }

        long freed = 0;
        int deleted = 0, locked = 0;
        var errors = new List<string>();

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
                foreach (var f in files)
                {
                    try
                    {
                        long len = 0;
                        try { len = new FileInfo(f).Length; } catch { /* length unknown */ }
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        freed += len;
                        deleted++;
                    }
                    catch
                    {
                        locked++;
                    }
                }

                foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                             .OrderByDescending(s => s.Length))
                {
                    try { Directory.Delete(d, false); } catch { /* not empty or in use */ }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{root}：{ex.Message}");
            }
        }

        double mb = freed / 1024.0 / 1024.0;
        if (deleted == 0 && errors.Count > 0)
            return ("磁盘清理失败", string.Join("；", errors));
        if (deleted == 0)
            return ("磁盘清理完成", "未发现可清理的临时文件");
        return ("磁盘清理完成",
            $"已删除 {deleted} 个临时文件，释放 {mb:0.0} MB" + (locked > 0 ? $"（{locked} 个被占用而跳过）" : ""));
    }

    /// <summary>Trims working sets of all accessible processes, reporting the real delta.</summary>
    public (string title, string detail) FreeMemory()
    {
        long before = AvailableBytes();
        int trimmed = 0, denied = 0, failed = 0;

        foreach (var p in Process.GetProcesses())
        {
            nint handle = nint.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetQuota, false, p.Id);
                if (handle == nint.Zero)
                {
                    denied++;
                    continue;
                }
                if (EmptyWorkingSet(handle)) trimmed++;
                else failed++;
            }
            catch
            {
                failed++;
            }
            finally
            {
                if (handle != nint.Zero) CloseHandle(handle);
                p.Dispose();
            }
        }

        Thread.Sleep(400);
        long after = AvailableBytes();
        double mb = (after - before) / 1024.0 / 1024.0;

        if (trimmed == 0)
            return ("内存释放失败",
                $"无法访问任何进程进行整理（拒绝 {denied} 个，失败 {failed} 个），请以管理员身份运行");
        if (mb < 1)
            return ("内存释放完成", $"已整理 {trimmed} 个进程，可用内存无明显变化");
        return ("内存释放完成", $"已整理 {trimmed} 个进程，可用内存增加 {mb:0.0} MB");
    }

    private static long AvailableBytes()
    {
        try
        {
            var status = new MEMORYSTATUSEX();
            return GlobalMemoryStatusEx(status) ? (long)status.ullAvailPhys : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetMemory(out double totalGb, out double usedGb, out double loadPercent)
    {
        totalGb = 0; usedGb = 0; loadPercent = 0;
        try
        {
            var status = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(status)) return false;
            totalGb = status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
            double availGb = status.ullAvailPhys / 1024.0 / 1024.0 / 1024.0;
            usedGb = totalGb - availGb;
            loadPercent = status.dwMemoryLoad;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------- native ----------

    private const int ProcessSetQuota = 0x0100;
    private const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(nint hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
