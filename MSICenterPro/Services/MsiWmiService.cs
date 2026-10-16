using System.Management;

namespace MSICenterPro.Services;

/// <summary>
/// Direct MSI EC access through root\WMI MSI_ACPI (Package_32), replicating the
/// call patterns of MSI Center's API_NB_Base Module (WmiMajorVersion 2 path).
///
/// Decoded register map (MS-17KK / Alpha 17 C7VF):
///   EC 0xD2 (Set_Data 210)  shift mode: 0xC0+offset  (Comfort 0xC0, Green 0xC1,
///                           Eco 0xC2, User 0xC3, Sport 0xC4, Surge 0xC5, Manual 0xC6)
///   EC 0xD4 (Set_Data 212)  fan flag byte: bit7 = custom curve active,
///                           bit4 = silent(quiet) flag; read via Get_AP(1)
///   EC 0x98 (Set_Data 152)  cooler boost: bit7 = on; read via Get_Thermal(3)
/// </summary>
public sealed class MsiWmiService
{
    public static MsiWmiService Instance { get; } = new();

    private const string Scope = @"root\WMI";
    private const string AcpiPath = @"MSI_ACPI.InstanceName='ACPI\PNP0C14\0_0'";

    private readonly object _gate = new();
    private ManagementObject? _acpi;
    private DateTime _bindFailedAt = DateTime.MinValue;

    public string? LastError { get; private set; }

    private MsiWmiService() { }

    public bool IsAvailable
    {
        get { lock (_gate) { return EnsureBound(); } }
    }

    private bool EnsureBound()
    {
        if (_acpi != null) return true;
        if (DateTime.Now - _bindFailedAt < TimeSpan.FromSeconds(10)) return false;
        try
        {
            _acpi = new ManagementObject(Scope, AcpiPath, null);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _acpi = null;
            _bindFailedAt = DateTime.Now;
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>Invokes an MSI_ACPI method. Returns raw response bytes (byte 0 = status flag).</summary>
    private byte[]? Invoke(string method, int index, params byte[] data)
    {
        lock (_gate)
        {
            if (!EnsureBound()) return null;
            try
            {
                using var inP = _acpi!.GetMethodParameters(method);
                if (inP == null) { LastError = method + ": no in-parameters"; return null; }

                byte[] arr = new byte[32];
                arr[0] = (byte)index;
                if (data.Length > 0)
                    Array.Copy(data, 0, arr, 1, Math.Min(data.Length, 31));

                using var pkgClass = new ManagementClass(Scope, "Package_32", null);
                using var pkg = pkgClass.CreateInstance();
                pkg["Bytes"] = arr;
                inP["Data"] = pkg;

                using var outP = _acpi.InvokeMethod(method, inP, null);
                if (outP?["Data"] is not ManagementBaseObject mbo) return null;
                return mbo["Bytes"] as byte[];
            }
            catch (Exception ex)
            {
                LastError = method + ": " + ex.Message;
                _acpi = null;
                return null;
            }
        }
    }

    private byte[]? InvokeNoArg(string method)
    {
        lock (_gate)
        {
            if (!EnsureBound()) return null;
            try
            {
                using var outP = _acpi!.InvokeMethod(method, null, null);
                if (outP?["Data"] is not ManagementBaseObject mbo) return null;
                return mbo["Bytes"] as byte[];
            }
            catch (Exception ex)
            {
                LastError = method + ": " + ex.Message;
                _acpi = null;
                return null;
            }
        }
    }

    private static bool Ok(byte[]? r, int minLength) =>
        r != null && r.Length >= minLength && r[0] == 1;

    // ---------- reads ----------

    /// <summary>Current CPU/GPU temperatures. Get_Temperature(0) -> [flag, cpu, gpu].</summary>
    public (int Cpu, int Gpu)? ReadTemperatures()
    {
        var r = Invoke("Get_Temperature", 0);
        if (!Ok(r, 3)) return null;
        return (r![1], r[2]);
    }

    /// <summary>Current CPU/GPU fan speeds. Get_Fan(0) -> [flag, cpuH, cpuL, gpuH, gpuL].</summary>
    public (int Cpu, int Gpu)? ReadFanRpm()
    {
        var r = Invoke("Get_Fan", 0);
        if (!Ok(r, 5)) return null;
        return (Rpm(r![1], r[2]), Rpm(r[3], r[4]));
    }

    private static int Rpm(byte high, byte low)
    {
        int v = (high << 8) | low;
        if (v == 0) return 0;
        return (int)(60000000.0 / (v * 2 * 62.5));
    }

    /// <summary>Raw shift byte in EC 0xD2. Get_AP(0) -> [flag, b0, b1, shift, b3, ...].</summary>
    public int? ReadShiftByte()
    {
        var r = Invoke("Get_AP", 0);
        if (!Ok(r, 4)) return null;
        return r![3];
    }

    /// <summary>EC 0xD4 mirror byte. Get_AP(1) -> [flag, fanFlag, ...].</summary>
    public int? ReadFanFlagByte()
    {
        var r = Invoke("Get_AP", 1);
        if (!Ok(r, 2)) return null;
        return r![1];
    }

    /// <summary>Cooler boost state, bit7 of Get_Thermal(3).</summary>
    public bool? ReadCoolerBoost()
    {
        var r = Invoke("Get_Thermal", 3);
        if (!Ok(r, 2)) return null;
        return (r![1] & 0x80) != 0;
    }

    public string? ReadEcVersion()
    {
        var r = InvokeNoArg("Get_EC");
        if (r == null) return null;
        var chars = r.Skip(1).Where(b => b >= 0x20 && b < 0x7F).Select(b => (char)b).ToArray();
        string s = new string(chars).Trim();
        return s.Length >= 3 ? s : null;
    }

    /// <summary>Maps current EC state to the closest scenario card.</summary>
    public ScenarioKind? ReadCurrentScenario()
    {
        int? shift = ReadShiftByte();
        if (shift == null) return null;
        bool silent = (ReadFanFlagByte() ?? 0) is int f && (f & 0x10) != 0;

        return (shift.Value & 0x07) switch
        {
            4 => ScenarioKind.Game,       // Sport (Game / AI)
            1 => silent ? ScenarioKind.Silent : ScenarioKind.Balance, // Green
            2 => ScenarioKind.Silent,     // Eco (closest card)
            0 => ScenarioKind.Balance,    // Comfort
            3 => ScenarioKind.Balance,    // User
            _ => ScenarioKind.Balance
        };
    }

    // ---------- writes ----------

    private bool SetData(int index, byte value)
    {
        var r = Invoke("Set_Data", index, value);
        return Ok(r, 1);
    }

    public static byte ShiftByteFor(ShiftKind kind) => kind switch
    {
        ShiftKind.Sport => 0xC4,
        ShiftKind.Comfort => 0xC0,
        ShiftKind.Green => 0xC1,
        ShiftKind.Eco => 0xC2,
        ShiftKind.User => 0xC3,
        ShiftKind.PowerSurge => 0xC5,
        ShiftKind.Manual => 0xC6,
        _ => 0xC1
    };

    /// <summary>
    /// Applies a scenario the way MSI Center's setScenario() does on this model
    /// (all *_Scenario\Fan registry values are 0 -> auto fan):
    ///   1. silent flag (0xD4 bit4) set for Silent, cleared otherwise
    ///   2. shift register 0xD2
    ///   3. auto fan (0xD4 bit7 cleared, written back)
    /// </summary>
    public bool ApplyScenario(ScenarioKind kind)
    {
        byte shift = kind switch
        {
            ScenarioKind.Game => ShiftByteFor(ShiftKind.Sport),   // 0xC4
            ScenarioKind.Balance => ShiftByteFor(ShiftKind.Green), // 0xC1
            ScenarioKind.Silent => ShiftByteFor(ShiftKind.Green),  // 0xC1 + quiet flag
            _ => ShiftByteFor(ShiftKind.Green)
        };

        int? fanFlag = ReadFanFlagByte();
        byte fan = (byte)(fanFlag ?? 0x0D);
        fan = kind == ScenarioKind.Silent ? (byte)(fan | 0x10) : (byte)(fan & ~0x10);
        if (!SetData(212, fan)) return false;

        if (!SetData(210, shift)) return false;

        int? fanNow = ReadFanFlagByte();
        byte auto = (byte)((fanNow ?? fan) & 0x7F);
        if (!SetData(212, auto)) return false;

        return true;
    }

    public bool SetCoolerBoost(bool on)
    {
        var r = Invoke("Get_Thermal", 3);
        if (!Ok(r, 2)) return false;
        byte b = r![1];
        b = on ? (byte)(b | 0x80) : (byte)(b & 0x7F);
        return SetData(152, b);
    }
}
