using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MSICenterPro.Services;

namespace MSICenterPro;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        bool ecTest = e.Args.Length > 0 && string.Equals(e.Args[0], "--ec-selftest", StringComparison.OrdinalIgnoreCase);
        bool uiTest = e.Args.Length > 0 && string.Equals(e.Args[0], "--ui-selftest", StringComparison.OrdinalIgnoreCase);

        if (ecTest)
        {
            RunEcSelfTest();
            Environment.Exit(0);
            return;
        }

        string uiTestPath = Path.Combine(Path.GetTempPath(), "msicenterpro_ui_selftest.txt");
        if (uiTest)
        {
            try { File.Delete(uiTestPath); } catch { /* best effort */ }
            DispatcherUnhandledException += (_, args) =>
            {
                try { File.WriteAllText(uiTestPath, "FAIL: " + args.Exception, Encoding.UTF8); } catch { /* best effort */ }
                Environment.Exit(2);
            };
        }

        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline),
            new FrameworkPropertyMetadata(120));
        ThemeManager.Initialize();
        base.OnStartup(e);

        if (uiTest)
        {
            // StartupUri already built MainWindow; if XAML/resources were broken we
            // never reach the timer and the unhandled-exception hook writes FAIL.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { File.WriteAllText(uiTestPath, "OK", Encoding.UTF8); } catch { /* best effort */ }
                Environment.Exit(0);
            };
            timer.Start();
        }
    }

    /// <summary>
    /// Headless EC round-trip: read state, apply Game -> Silent -> Balance, toggle
    /// cooler boost on/off, verify read-backs, restore the original state.
    /// Result file: %TEMP%\msicenterpro_ec_selftest.txt
    /// </summary>
    private static void RunEcSelfTest()
    {
        var sb = new StringBuilder();
        var wmi = MsiWmiService.Instance;
        void L(string s) => sb.AppendLine(s);

        try
        {
            L("available: " + wmi.IsAvailable + (wmi.LastError is { } e1 ? " err=" + e1 : ""));
            L("ec-version: " + (wmi.ReadEcVersion() ?? "null"));
            L("temps(cpu/gpu): " + Fmt(wmi.ReadTemperatures()));
            L("fans(cpu/gpu): " + Fmt(wmi.ReadFanRpm()));
            L("shift: " + Hex(wmi.ReadShiftByte()));
            L("fanflag: " + Hex(wmi.ReadFanFlagByte()));

            bool? boost0 = wmi.ReadCoolerBoost();
            L("cooler-boost(before): " + (boost0?.ToString() ?? "null"));
            L("scenario(before): " + (wmi.ReadCurrentScenario()?.ToString() ?? "null"));

            L("-- apply Game --");
            L("write ok: " + wmi.ApplyScenario(ScenarioKind.Game));
            Thread.Sleep(500);
            L("shift: " + Hex(wmi.ReadShiftByte()) + " fanflag: " + Hex(wmi.ReadFanFlagByte())
              + " scenario: " + wmi.ReadCurrentScenario());

            L("-- apply Silent --");
            L("write ok: " + wmi.ApplyScenario(ScenarioKind.Silent));
            Thread.Sleep(500);
            L("shift: " + Hex(wmi.ReadShiftByte()) + " fanflag: " + Hex(wmi.ReadFanFlagByte())
              + " scenario: " + wmi.ReadCurrentScenario());

            L("-- cooler boost on --");
            L("write ok: " + wmi.SetCoolerBoost(true));
            Thread.Sleep(500);
            L("cooler-boost(read): " + (wmi.ReadCoolerBoost()?.ToString() ?? "null"));

            L("-- cooler boost off --");
            L("write ok: " + wmi.SetCoolerBoost(false));
            Thread.Sleep(500);
            L("cooler-boost(read): " + (wmi.ReadCoolerBoost()?.ToString() ?? "null"));

            L("-- restore Balance + original cooler boost --");
            L("write ok: " + wmi.ApplyScenario(ScenarioKind.Balance));
            if (boost0 == true) wmi.SetCoolerBoost(true);
            Thread.Sleep(500);
            L("final shift: " + Hex(wmi.ReadShiftByte()) + " fanflag: " + Hex(wmi.ReadFanFlagByte())
              + " scenario: " + wmi.ReadCurrentScenario());
            L("final cooler-boost: " + (wmi.ReadCoolerBoost()?.ToString() ?? "null"));
            L("final temps: " + Fmt(wmi.ReadTemperatures()) + "  fans: " + Fmt(wmi.ReadFanRpm()));
        }
        catch (Exception ex)
        {
            L("EXCEPTION: " + ex);
        }

        try
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "msicenterpro_ec_selftest.txt"),
                sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // best effort
        }
    }

    private static string Fmt((int Cpu, int Gpu)? v) => v is { } t ? $"{t.Cpu}/{t.Gpu}" : "null";

    private static string Hex(int? v) => v is { } b ? "0x" + b.ToString("X2") : "null";
}
