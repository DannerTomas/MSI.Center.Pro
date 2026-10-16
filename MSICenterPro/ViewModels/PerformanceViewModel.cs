using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using MSICenterPro.Core;
using MSICenterPro.Services;

namespace MSICenterPro.ViewModels;

public class PerformanceViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly MsiWmiService _wmi = MsiWmiService.Instance;
    private ScenarioItem _selected;
    private bool _firstLoad = true;
    private bool _coolerBoost;
    private bool _coolerBoostSyncing;

    public PerformanceViewModel()
    {
        Scenarios = new[]
        {
            new ScenarioItem { Key = "Game", Title = "狂暴模式", Subtitle = "释放全部性能", Icon = PackIconKind.GamepadVariant },
            new ScenarioItem { Key = "Balance", Title = "均衡模式", Subtitle = "性能与功耗平衡", Icon = PackIconKind.ScaleBalance },
            new ScenarioItem { Key = "Silent", Title = "静音模式", Subtitle = "更低噪音与功耗", Icon = PackIconKind.Leaf }
        };

        _selected = Scenarios[1];
        _selected.IsSelected = true;

        SelectScenarioCommand = new RelayCommand(p =>
        {
            if (p is ScenarioItem item) Select(item);
        });

        // reflect the real EC state on startup
        _ = Task.Run(() =>
        {
            var kind = _wmi.ReadCurrentScenario();
            bool? boost = _wmi.ReadCoolerBoost();
            _dispatcher.BeginInvoke(() =>
            {
                if (kind is { } k) ApplySelected(k);
                if (boost is { } b) SetCoolerBoostSilently(b);
            });
        });
    }

    public ScenarioItem[] Scenarios { get; }

    public ScenarioItem Selected
    {
        get => _selected;
        private set => Set(ref _selected, value);
    }

    public RelayCommand SelectScenarioCommand { get; }

    // ---------- cooler boost ----------

    /// <summary>Cooler Boost switch, driven by the real EC state (EC 0x98 bit7).</summary>
    public bool CoolerBoost
    {
        get => _coolerBoost;
        set
        {
            if (!Set(ref _coolerBoost, value)) return;
            if (_coolerBoostSyncing) return;
            ApplyCoolerBoost(value);
        }
    }

    private void SetCoolerBoostSilently(bool value)
    {
        _coolerBoostSyncing = true;
        CoolerBoost = value;
        _coolerBoostSyncing = false;
    }

    private void ApplyCoolerBoost(bool target)
    {
        _ = Task.Run(() =>
        {
            bool writeOk = _wmi.SetCoolerBoost(target);
            string err = _wmi.LastError ?? "未知错误";
            bool? actual = writeOk ? _wmi.ReadCoolerBoost() : null;

            _dispatcher.BeginInvoke(() =>
            {
                if (writeOk && actual is { } a)
                {
                    SetCoolerBoostSilently(a);
                    ToastRequested?.Invoke("酷寒散热",
                        a == target
                            ? (target ? "已开启，风扇将全速运转" : "已关闭，风扇恢复自动策略")
                            : $"EC 回报状态为{(a ? "开启" : "关闭")}，与目标不一致");
                }
                else if (writeOk)
                {
                    SetCoolerBoostSilently(target);
                    ToastRequested?.Invoke("酷寒散热",
                        (target ? "已开启" : "已关闭") + $"（EC 状态回读失败：{err}）");
                }
                else
                {
                    SetCoolerBoostSilently(!target);
                    ToastRequested?.Invoke("酷寒散热切换失败", $"无法写入 EC：{err}");
                }
            });
        });
    }

    // ---------- scenario selection ----------

    private void Select(ScenarioItem item)
    {
        if (ReferenceEquals(item, Selected) && !_firstLoad) return;
        _firstLoad = false;

        var previous = Selected;
        previous.IsSelected = false;
        item.IsSelected = true;
        Selected = item;

        ScenarioKind? kind = item.Key switch
        {
            "Game" => ScenarioKind.Game,
            "Balance" => ScenarioKind.Balance,
            "Silent" => ScenarioKind.Silent,
            _ => null
        };
        if (kind is not { } k) return;

        _ = Task.Run(() =>
        {
            bool ok = _wmi.ApplyScenario(k);
            if (ok) return; // no toast on success - the card state is the feedback

            string err = _wmi.LastError ?? "未知错误";
            _dispatcher.BeginInvoke(() =>
            {
                // revert the optimistic switch and report the real reason
                if (ReferenceEquals(Selected, item))
                {
                    item.IsSelected = false;
                    previous.IsSelected = true;
                    Selected = previous;
                }
                ToastRequested?.Invoke("性能模式切换失败", $"无法写入 EC：{err}");
            });
        });
    }

    /// <summary>Selects the card matching the real EC state without triggering a write.</summary>
    private void ApplySelected(ScenarioKind kind)
    {
        ScenarioItem item = kind switch
        {
            ScenarioKind.Game => Scenarios[0],
            ScenarioKind.Silent => Scenarios[2],
            _ => Scenarios[1]
        };
        if (ReferenceEquals(item, Selected)) return;
        _firstLoad = false;
        Selected.IsSelected = false;
        item.IsSelected = true;
        Selected = item;
    }

    public event Action<string, string>? ToastRequested;
}
