using System.Runtime.InteropServices;
using Microsoft.Win32;
using static Com0com.Core.Native.NativeMethods;

namespace Com0com.Core;

public sealed class SetupOptions
{
    /// <summary>Directory with com0com.inf / cncport.inf / comport.inf / com0com.sys / com0com.cat.</summary>
    public string InfDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>Don't update the driver when installing pairs (a later "driver install" is expected).</summary>
    public bool NoUpdate { get; set; }

    /// <summary>Don't touch device friendly names.</summary>
    public bool NoUpdateFriendlyNames { get; set; }

    /// <summary>Trace friendly name activity.</summary>
    public bool ShowFriendlyNames { get; set; }

    /// <summary>Show default values in traced parameter strings.</summary>
    public bool DetailParams { get; set; }
}

/// <summary>One port of a pair as seen by the list operation.</summary>
public sealed class PortInfo
{
    internal PortInfo(PortParameters p, string? friendlyName, bool present, PortState state = PortState.Running)
    {
        State = present ? state : PortState.NotPresent;
        Parameters = p;
        FriendlyName = friendlyName;
        Present = present;
    }

    public string Id => Parameters.PhPortName;
    public PortParameters Parameters { get; }
    public string? FriendlyName { get; }
    public bool Present { get; }

    /// <summary>Device state as reported by the PnP manager.</summary>
    public PortState State { get; }

    /// <summary>Problem code when <see cref="State"/> is <see cref="PortState.Problem"/>.</summary>
    public uint ProblemCode { get; internal init; }

    /// <summary>Name an application should open (RealPortName for COM# ports).</summary>
    public string DisplayName =>
        C0C.IsComClass(Parameters.PortName) && Parameters.RealPortName.Length > 0 ? Parameters.RealPortName : Parameters.PortName;
}

public enum PortState { Running, Disabled, Problem, RestartRequired, NotPresent }

public sealed record PairInfo(int Num, PortInfo A, PortInfo B, string? BusFriendlyName)
{
    public PortInfo this[bool isA] => isA ? A : B;
}

/// <summary>
/// High-level com0com setup operations. Port of setup/setup.cpp; shared by setupc and setupg.
/// Methods return false (after telling the user why) when an operation is cancelled or fails.
/// </summary>
public sealed class Com0comSetup
{
    private static readonly InfField[] RequiredBus =
    [
        new("Version", "ClassGUID", 1, C0C.CncClassGuid),
        new("Version", "Provider", 1, C0C.Provider),
        new("Version", "UninstallInfTag", 1, C0C.CncClassGuid),
        new("Manufacturer", null, 1, C0C.Service),
    ];

    private static readonly InfField[] RequiredComPort =
    [
        new("Version", "ClassGUID", 1, C0C.ComClassGuid),
        new("Version", "Provider", 1, C0C.Provider),
        new("Version", "UninstallInfTag", 1, C0C.CncClassGuid),
        new("Manufacturer", null, 1, C0C.Service),
    ];

    private sealed record InfInstall(string InfName, string? CopyDriversSection, string HardwareId, bool PreinstallClass, InfField[] Required);

    private static readonly InfInstall[] InfInstallList =
    [
        new(C0C.InfName, C0C.CopyDriversSection, C0C.BusDeviceId, true, RequiredBus),
        new(C0C.InfNameCncPort, null, C0C.PortHwIdCncClass, false, RequiredBus),
        new(C0C.InfNameComPort, null, C0C.PortHwIdComClass, false, RequiredComPort),
    ];

    private static readonly (InfField[] Fields, bool AskUser)[] InfUninstallRules =
    [
        ([new("Version", "UninstallInfTag", 1, C0C.CncClassGuid), new("Manufacturer", null, 1, C0C.Service)], false),
        ([new("Version", "ClassGUID", 1, C0C.CncClassGuid), new("Version", "Provider", 1, C0C.Provider),
          new("Version", "CatalogFile", 1, "com0com.cat")], false),
        ([new("Version", "ClassGUID", 1, C0C.CncClassGuid)], true),
        ([new("Version", "ClassGUID", 1, C0C.ComClassGuid), new("Manufacturer", null, 1, C0C.Service)], true),
    ];

    private readonly ISetupUi _ui;
    private readonly Devices _dev;
    private readonly ComDb _comDb;

    public Com0comSetup(ISetupUi ui, SetupOptions? options = null)
    {
        _ui = ui;
        Options = options ?? new SetupOptions();
        _dev = new Devices(ui);
        _comDb = new ComDb(ui);
    }

    public SetupOptions Options { get; }

    /// <summary>Set when some operation needs a reboot to complete.</summary>
    public bool RebootRequired { get; private set; }

    private static readonly DevProperties BusMatch = new(C0C.BusDeviceId);

    private static DevProperties PortMatch(int num, bool isA) => new(C0C.PortDeviceId, PhObjName: C0C.DeviceName(isA, num));

    private void PromptReboot(RebootFlag reboot)
    {
        if (!reboot.Required) return;
        _ui.Line("\nReboot required.");
        RebootRequired = true;
    }

    private string InfPath(string name) => Path.Combine(Options.InfDirectory, name);

    private static PortParameters LoadParams(int num, bool isA)
    {
        var p = new PortParameters(C0C.PortId(isA, num));
        p.Load();
        return p;
    }

    // ------------------------------------------------------------------ checks

    /// <summary>Verifies that the INF files in <see cref="SetupOptions.InfDirectory"/> are the com0com ones.</summary>
    public bool CheckInfFiles()
    {
        foreach (var inf in InfInstallList)
        {
            string path = InfPath(inf.InfName);
            if (!File.Exists(path))
            {
                _ui.Show(Buttons.Ok, $"File {path} not found.\nUse --inf-dir to point to the driver directory.\n");
                return false;
            }
            using var f = new InfFile(_ui, path);
            if (!f.Test(inf.Required))
            {
                _ui.Show(Buttons.Ok, $"File {path} is not a com0com INF file.\n");
                return false;
            }
        }
        return true;
    }

    private bool IsValidPortNum(int num)
    {
        if (num < 0) return false;
        if ((C0C.PrefBusName + num).Length > C0C.PortNameLen)
        {
            var res = _ui.Show(Buttons.OkCancel, $"The port number {num} is too big.\n", warning: true);
            return res == Choice.Ok;
        }
        return true;
    }

    private static string? QueryDosDevice(string name)
    {
        var buf = new char[1024];
        uint n = QueryDosDeviceW(name, buf, (uint)buf.Length);
        if (n == 0) return Marshal.GetLastWin32Error() == ERROR_INSUFFICIENT_BUFFER ? "" : null;
        return new string(buf, 0, (int)n).Split('\0')[0];
    }

    private bool IsValidPortName(string portName)
    {
        if (portName.Length == 0)
        {
            _ui.Show(Buttons.Ok, "The port name is empty.\n");
            return false;
        }
        if (portName.Length > C0C.PortNameLen &&
            _ui.Show(Buttons.OkCancel, $"The length of port name {portName}\nis too big (greater then {C0C.PortNameLen}).\n", warning: true) != Choice.Ok)
        {
            return false;
        }

        Choice res;
        do
        {
            res = Choice.Continue;
            var target = QueryDosDevice(portName);
            if (target != null)
                res = _ui.Show(Buttons.CancelTryContinue, $"The port name {portName} is already used for other device {target}.\n");
        } while (res == Choice.TryAgain);
        if (res != Choice.Continue) return false;

        do
        {
            res = Choice.Continue;
            if (!_comDb.GetInUse(portName, out bool inUse)) res = Choice.Cancel;
            else if (inUse)
                res = _ui.Show(Buttons.CancelTryContinue,
                    $"The port name {portName} is already logged as \"in use\"\nin the COM port database.\n");
        } while (res == Choice.TryAgain);
        return res == Choice.Continue;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Port names of all installed ports, or null if they could not all be read.</summary>
    private List<string>? CurrentPortNames()
    {
        var names = new List<string>();
        try
        {
            if (_dev.EnumDevices(BusMatch, bus =>
                {
                    int n = bus.PortNum;
                    if (n < 0) return true;
                    names.Add(LoadParams(n, true).PortName);
                    names.Add(LoadParams(n, false).PortName);
                    return true;
                }) < 0)
            {
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _ui.Line($"Can't read port names: {e.Message}");
            return null;
        }
        return names;
    }

    /// <summary>
    /// Brings our ComDB claims in line with the installed ports. Skipped (as in the original) when the
    /// ports can't be enumerated: syncing against an incomplete list would release names still in use.
    /// </summary>
    private void ComDbSync()
    {
        if (CurrentPortNames() is { } names) _comDb.Sync(names);
    }

    private static string? ReadDevPortName(Device dev)
    {
        using var key = dev.OpenDevKey(false);
        return key?.GetValue("PortName") as string;
    }

    // ------------------------------------------------------------------ friendly names

    private void UpdateFriendlyNameBus(Device bus, int num)
    {
        string a = LoadParams(num, true).PortName, b = LoadParams(num, false).PortName;
        string old = bus.GetStringProperty(SPDRP_FRIENDLYNAME) ?? "";
        if (Options.ShowFriendlyNames) _ui.Line($"       {C0C.PrefBusName}{num} FriendlyName=\"{old}\"");
        if (Options.NoUpdateFriendlyNames) return;

        string desc = bus.GetStringProperty(SPDRP_DEVICEDESC) ?? "com0com - bus for serial port pair emulator";
        string name = $"{desc} {num} ({a} <-> {b})";
        if (name != old && bus.SetStringProperty(SPDRP_FRIENDLYNAME, name) && Options.ShowFriendlyNames)
            _ui.Line($"update {C0C.PrefBusName}{num} FriendlyName=\"{name}\"");
    }

    private void SetFriendlyNamePort(Device port)
    {
        string? ph = port.Props.PhObjName;
        if (ph == null) return;

        string? phPortName = null;
        foreach (bool isA in new[] { true, false })
        {
            string pref = isA ? C0C.PrefDeviceNameA : C0C.PrefDeviceNameB;
            if (ph.Length > pref.Length && ph.StartsWith(pref, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(ph.AsSpan(pref.Length), out int n) && n >= 0)
            {
                phPortName = C0C.PortId(isA, n);
                break;
            }
        }
        if (phPortName == null) return;

        var prms = new PortParameters(phPortName);
        prms.Load();
        string portName = prms.PortName;

        string old = port.GetStringProperty(SPDRP_FRIENDLYNAME) ?? "";
        if (Options.ShowFriendlyNames) _ui.Line($"       {phPortName} FriendlyName=\"{old}\"");

        if (C0C.IsComClass(portName))
        {
            var real = ReadDevPortName(port);
            if (real == null) return;
            portName = real;
        }

        if (Options.NoUpdateFriendlyNames) return;
        string desc = port.GetStringProperty(SPDRP_DEVICEDESC) ?? "com0com - serial port emulator";
        string name = $"{desc} {phPortName} ({portName})";
        if (name != old && port.SetStringProperty(SPDRP_FRIENDLYNAME, name) && Options.ShowFriendlyNames)
            _ui.Line($"update {phPortName} FriendlyName=\"{name}\"");
    }

    private void UpdateFriendlyNamesPorts(int num)
    {
        foreach (bool isA in new[] { true, false })
            _dev.EnumDevices(PortMatch(num, isA), p => { SetFriendlyNamePort(p); return true; });
    }

    /// <summary>Updates friendly names for one bus (num) or all (-1).</summary>
    private void UpdateFriendlyNamesBus(int num)
    {
        _dev.EnumDevices(BusMatch, bus =>
        {
            int n = bus.PortNum;
            if (num == -1 || num == n)
            {
                UpdateFriendlyNameBus(bus, n);
                UpdateFriendlyNamesPorts(n);
            }
            return true;
        });
    }

    /// <summary>updatefnames / listfnames.</summary>
    public bool UpdateFriendlyNames(bool update)
    {
        bool saveNo = Options.NoUpdateFriendlyNames, saveShow = Options.ShowFriendlyNames;
        try
        {
            if (!update) Options.NoUpdateFriendlyNames = true;
            Options.ShowFriendlyNames = true;
            UpdateFriendlyNamesBus(-1);
        }
        finally
        {
            Options.NoUpdateFriendlyNames = saveNo;
            Options.ShowFriendlyNames = saveShow;
        }
        return true;
    }

    // ------------------------------------------------------------------ list

    /// <summary>All installed pairs (sorted by number). Loads RealPortName for COM# ports.</summary>
    public List<PairInfo> ListPairs()
    {
        var pairs = new List<PairInfo>();
        _dev.EnumDevices(BusMatch, bus =>
        {
            int n = bus.PortNum;
            if (n < 0) return true;
            var ports = new PortInfo[2];
            for (int j = 0; j < 2; j++)
            {
                bool isA = j == 0;
                var p = LoadParams(n, isA);
                string? fname = null;
                bool present = false;
                var state = PortState.Running;
                uint problem = 0;
                _dev.EnumDevices(PortMatch(n, isA), dev =>
                {
                    present = true;
                    if (CM_Get_DevNode_Status(out uint st, out uint pr, dev.DevInst, 0) == CR_SUCCESS)
                    {
                        if ((st & DN_HAS_PROBLEM) != 0)
                        {
                            state = pr == CM_PROB_DISABLED ? PortState.Disabled : PortState.Problem;
                            problem = pr;
                        }
                        else if ((st & DN_NEED_RESTART) != 0)
                        {
                            state = PortState.RestartRequired;
                        }
                    }
                    fname = dev.GetStringProperty(SPDRP_FRIENDLYNAME);
                    if (C0C.IsComClass(p.PortName) && ReadDevPortName(dev) is { } real) p.InitRealPortName(real);
                    return true;
                });
                ports[j] = new PortInfo(p, fname, present, state) { ProblemCode = problem };
            }
            pairs.Add(new PairInfo(n, ports[0], ports[1], bus.GetStringProperty(SPDRP_FRIENDLYNAME)));
            return true;
        });
        pairs.Sort((x, y) => x.Num.CompareTo(y.Num));
        return pairs;
    }

    // ------------------------------------------------------------------ change

    /// <summary>Applies a parameter string to the port CNCA&lt;n&gt;/CNCB&lt;n&gt;.</summary>
    public bool ChangePort(string portId, string parameters)
    {
        if (!C0C.TryParsePortId(portId, out bool isA, out int num))
        {
            _ui.Show(Buttons.Ok, $"Invalid port identifier '{portId}' (expected {C0C.PrefPortNameA}<n> or {C0C.PrefPortNameB}<n>).\n");
            return false;
        }

        var reboot = new RebootFlag();
        bool found = false, changed = false, ok = true;

        _dev.EnumDevices(BusMatch, bus =>
        {
            if (bus.PortNum != num) return true;
            found = true;
            ok = ChangePortOnBus(bus, num, isA, parameters, reboot, ref changed);
            return true;
        });

        if (!found)
        {
            _ui.Show(Buttons.Ok, $"Port pair {num} is not installed.\n");
            return false;
        }
        if (changed) ComDbSync();
        PromptReboot(reboot);
        return ok;
    }

    private bool ChangePortOnBus(Device bus, int num, bool isA, string parameters, RebootFlag reboot, ref bool changed)
    {
        string phPortName = C0C.PortId(isA, num);
        var prms = new PortParameters(phPortName);
        try
        {
            prms.Load();
        }
        catch (Exception e)
        {
            _ui.Show(Buttons.Ok, $"Load parameters of {phPortName}\nERROR: {e.Message}", warning: true);
            return false;
        }

        string portNameOld = prms.PortName;
        bool isComClassOld = C0C.IsComClass(portNameOld);
        var portMatch = PortMatch(num, isA);

        if (isComClassOld)
            _dev.EnumDevices(portMatch, d => { if (ReadDevPortName(d) is { } r) prms.InitRealPortName(r); return true; });

        _ui.Line($"       {phPortName} {prms.Format(Options.DetailParams)}");

        string realPortNameOld = prms.RealPortName;
        if (!prms.TryApply(parameters, out var error))
        {
            _ui.Line(error!);
            return false;
        }

        string portNameNew = prms.PortName;
        if (!portNameNew.Equals(portNameOld, StringComparison.OrdinalIgnoreCase) &&
            (!isComClassOld || realPortNameOld.Length == 0 || !portNameNew.Equals(realPortNameOld, StringComparison.OrdinalIgnoreCase)) &&
            !IsValidPortName(portNameNew))
        {
            return false;
        }

        bool isComClassNew = C0C.IsComClass(portNameNew);
        if (!isComClassNew || !isComClassOld)
        {
            realPortNameOld = "";
            prms.InitRealPortName(); // RealPortName only applies to COM# ports
        }

        if (!prms.Changed) return true;

        string realPortNameNew = prms.RealPortName;
        if (!realPortNameNew.Equals(realPortNameOld, StringComparison.OrdinalIgnoreCase) &&
            (!IsValidPortName(realPortNameNew) || !_comDb.IsValidName(realPortNameNew)))
        {
            return false;
        }

        try
        {
            prms.Save();
        }
        catch (Exception e)
        {
            _ui.Show(Buttons.Ok, $"Save parameters of {phPortName}\nERROR: {e.Message}", warning: true);
            return false;
        }

        changed = true;
        _ui.Line($"change {phPortName} {prms.Format(Options.DetailParams)}");

        bool renamed = !portNameNew.Equals(portNameOld, StringComparison.OrdinalIgnoreCase);
        if (renamed) UpdateFriendlyNameBus(bus, num);

        if (isComClassNew != isComClassOld)
        {
            // The port changes its device class: remove the child device and let the bus re-create it.
            _ui.Line($"Changed port class for {phPortName} (renamed {portNameOld} to {portNameNew})");
            _dev.DisableDevices(portMatch, reboot, null);
            _dev.RemoveDevices(portMatch, reboot);
            Devices.Reenumerate(bus);
            _dev.WaitNoPendingInstallEvents(10);
            UpdateFriendlyNamesPorts(num);
        }
        else
        {
            if (renamed) UpdateFriendlyNamesPorts(num);

            if (!realPortNameNew.Equals(realPortNameOld, StringComparison.OrdinalIgnoreCase))
            {
                _dev.EnumDevices(portMatch, d =>
                {
                    using var key = d.OpenDevKey(true);
                    if (key?.GetValue("PortName") is not string old) return true;
                    if (_comDb.Claim(realPortNameNew))
                    {
                        try
                        {
                            key.SetValue("PortName", realPortNameNew, RegistryValueKind.String);
                            _comDb.Release(old);
                            SetFriendlyNamePort(d);
                        }
                        catch
                        {
                            _comDb.Release(realPortNameNew);
                        }
                    }
                    return true;
                });
            }

            _dev.RestartDevices(portMatch, reboot);
        }
        return true;
    }

    // ------------------------------------------------------------------ install / remove

    private int AllocPortNum(int wanted)
    {
        var busy = new HashSet<int>();
        if (_dev.EnumDevices(BusMatch, bus => { int n = bus.PortNum; if (n >= 0) busy.Add(n); return true; }) < 0)
            return -1;
        if (!busy.Contains(wanted)) return wanted;
        int i = 0;
        while (busy.Contains(i)) i++;
        return i;
    }

    /// <summary>
    /// Installs a new pair. <paramref name="num"/> &lt; 0 picks the first free number.
    /// Returns the pair number, or -1 if not installed.
    /// </summary>
    public int InstallPair(int num, string parametersA, string parametersB)
    {
        int i = InstallPairCore(num, parametersA, parametersB);
        if (i < 0) _ui.Line("\nInstall not completed!");
        return i;
    }

    private int InstallPairCore(int num, string parametersA, string parametersB)
    {
        if (!CheckInfFiles()) return -1;

        int i;
        Choice res;
        do
        {
            res = Choice.Continue;
            i = AllocPortNum(num >= 0 ? num : 0);
            if (i < 0) return -1;
            if (num >= 0 && num != i)
            {
                res = _ui.Show(Buttons.CancelTryContinue,
                    $"The identifiers {C0C.PrefPortNameA}{num} and {C0C.PrefPortNameB}{num} are already used for other ports\n" +
                    $"so ports with identifiers {C0C.PrefPortNameA}{i} and {C0C.PrefPortNameB}{i} will be installed instead.\n",
                    warning: true);
            }
        } while (res == Choice.TryAgain);
        if (res != Choice.Continue && res != Choice.None) return -1;
        if (!IsValidPortNum(i)) return -1;

        var names = new string[2];
        for (int j = 0; j < 2; j++)
        {
            bool isA = j == 0;
            var prms = new PortParameters(C0C.PortId(isA, i));
            try
            {
                prms.Load();
            }
            catch (Exception e)
            {
                _ui.Show(Buttons.Ok, $"Load parameters of {prms.PhPortName}\nERROR: {e.Message}", warning: true);
            }

            if (!prms.TryApply(isA ? parametersA : parametersB, out var error))
            {
                _ui.Line(error!);
                return -1;
            }
            names[j] = prms.PortName;
            if (!IsValidPortName(names[j])) return -1;
            prms.InitRealPortName();
            if (prms.Changed)
            {
                try
                {
                    prms.Save();
                }
                catch (Exception e)
                {
                    _ui.Show(Buttons.Ok, $"Save parameters of {prms.PhPortName}\nERROR: {e.Message}", warning: true);
                }
            }
            _ui.Line($"       {prms.PhPortName} {prms.Format(Options.DetailParams)}");
        }

        if (names[0].Equals(names[1], StringComparison.OrdinalIgnoreCase) && !C0C.IsComClass(names[0]) &&
            _ui.Show(Buttons.OkCancel, $"The same port name {names[0]} is used for both ports.\n", warning: true) != Choice.Ok)
        {
            return -1;
        }

        var reboot = new RebootFlag();
        bool ok = _dev.InstallDevice(InfPath(C0C.InfName), C0C.BusDeviceId, dev =>
        {
            while (true)
            {
                try
                {
                    using var key = dev.CreateDevKey() ?? throw new SetupException(UiExtensions.FormatError(Marshal.GetLastWin32Error()));
                    key.SetValue(C0C.RegValPortNum, i, RegistryValueKind.DWord);
                    return true;
                }
                catch (Exception e)
                {
                    if (_ui.Show(Buttons.CancelTryContinue, $"SetPortNum({i})\nERROR: {e.Message}") != Choice.TryAgain)
                        return false;
                }
            }
        }, !Options.NoUpdate, reboot);
        if (!ok) return -1;

        UpdateFriendlyNamesBus(i);
        PromptReboot(reboot);
        ComDbSync();
        return i;
    }

    /// <summary>Installs/updates the driver for already registered bus devices (after --no-update installs).</summary>
    public bool InstallDriver()
    {
        if (!CheckInfFiles()) return false;
        var reboot = new RebootFlag();
        Choice res;
        do res = _dev.UpdateDriver(InfPath(C0C.InfName), C0C.BusDeviceId, 0, false, reboot);
        while (res == Choice.TryAgain);
        if (res != Choice.Continue) return false;

        ComDbSync();
        UpdateFriendlyNamesBus(-1);
        PromptReboot(reboot);
        return true;
    }

    public bool RemovePair(int num)
    {
        var reboot = new RebootFlag();
        Step res;
        bool found;
        do
        {
            res = Step.Continue;
            found = false;
            var r = Step.Continue;
            _dev.EnumDevices(BusMatch, bus =>
            {
                if (bus.PortNum != num) return true;
                found = true;
                r = _dev.DisableDevice(bus, reboot, null);
                if (r != Step.Continue) return false;
                if (!_dev.RemoveDevice(bus, reboot)) { r = Step.Cancel; return false; }
                return true;
            });
            res = r;
        } while (res == Step.TryAgain);

        if (!found)
        {
            _ui.Show(Buttons.Ok, $"Port pair {num} is not installed.\n");
            return false;
        }

        if (res == Step.Continue)
        {
            // Remove the (now non-present) child port devices too.
            foreach (bool isA in new[] { true, false })
                _dev.RemoveDevices(new DevProperties(C0C.PortDeviceId, Location: C0C.PortId(isA, num)), null);
        }

        ComDbSync();
        PromptReboot(reboot);
        return res == Step.Continue;
    }

    // ------------------------------------------------------------------ enable / disable

    public bool DisableAll()
    {
        var reboot = new RebootFlag();
        bool ok = _dev.DisableDevices(BusMatch, reboot, null);
        PromptReboot(reboot);
        return ok;
    }

    public bool EnableAll()
    {
        var reboot = new RebootFlag();
        bool ok = _dev.EnableDevices(BusMatch, reboot);
        PromptReboot(reboot);
        return ok;
    }

    // ------------------------------------------------------------------ driver maintenance

    public bool Preinstall()
    {
        if (!CheckInfFiles()) return false;
        foreach (var inf in InfInstallList)
        {
            using var f = new InfFile(_ui, InfPath(inf.InfName));
            if (inf.PreinstallClass)
            {
                while (true)
                {
                    if (SetupDiInstallClassW(IntPtr.Zero, f.Path, 0, IntPtr.Zero))
                    {
                        _ui.Line($"Installed Class {f.Version("ClassGUID")}");
                        break;
                    }
                    var r = _ui.ShowLastError(Buttons.CancelTryContinue, "SetupDiInstallClass()");
                    if (r == Choice.TryAgain) continue;
                    if (r == Choice.Continue) break;
                    _ui.Line("\nPreinstall not completed!");
                    return false;
                }
            }
            if (!f.InstallOemInf())
            {
                _ui.Line("\nPreinstall not completed!");
                return false;
            }
        }
        return true;
    }

    private bool Reload(string hardwareId, string? infPath, RebootFlag reboot)
    {
        var disabled = new List<DevProperties>();
        bool ok = _dev.DisableDevices(new DevProperties(hardwareId), reboot, disabled);

        if (ok && infPath != null && !Options.NoUpdate)
        {
            Choice res;
            do res = _dev.UpdateDriver(infPath, hardwareId, INSTALLFLAG_FORCE, false, reboot);
            while (res == Choice.TryAgain);
            ok = res == Choice.Continue;
        }

        foreach (var d in disabled)
            if (d.PhObjName != null) _dev.EnableDevices(d, reboot);

        if (ok) ComDbSync();
        return ok;
    }

    /// <summary>Restarts all bus devices so the driver is reloaded.</summary>
    public bool Reload()
    {
        var reboot = new RebootFlag();
        bool ok = Reload(C0C.BusDeviceId, null, reboot);
        PromptReboot(reboot);
        return ok;
    }

    /// <summary>Force-updates the driver for the bus and both port classes from the INF directory.</summary>
    public bool Update()
    {
        if (!CheckInfFiles()) return false;
        var reboot = new RebootFlag();
        bool ok = true;
        foreach (var inf in InfInstallList)
            if (!Reload(inf.HardwareId, InfPath(inf.InfName), reboot)) ok = false;

        if (!ok)
        {
            _ui.Line("\nUpdate not completed!");
            return false;
        }
        UpdateFriendlyNamesBus(-1);
        PromptReboot(reboot);
        return true;
    }

    /// <summary>Removes all pairs, the service, the device class and the INF files.</summary>
    public bool Uninstall()
    {
        if (!UninstallCore())
        {
            _ui.Line("\nUninstall not completed!");
            return false;
        }
        return true;
    }

    private bool UninstallCore()
    {
        if (!CheckInfFiles()) return false;
        var reboot = new RebootFlag();

        if (!_dev.DisableDevices(new DevProperties(C0C.PortDeviceId), reboot, null)) return false;
        if (!_dev.RemoveDevices(BusMatch, reboot)) return false;
        if (!_dev.RemoveDevices(null, null)) return false; // leftovers (non-present ports etc.)

        ComDbSync();

        if (reboot.Required)
        {
            PromptReboot(reboot);
            _ui.Line("Reboot and run uninstall again to finish.");
            return false;
        }

        if (!DeleteDriverService()) return false;

        DeleteKeyRetry(Registry.LocalMachine, C0C.RegKeyEventLog, $"EventLog {C0C.Service}");
        DeleteKeyRetry(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Class\" + C0C.CncClassGuid, $"Class {C0C.CncClassGuid}");

        foreach (var inf in InfInstallList)
        {
            using var f = new InfFile(_ui, InfPath(inf.InfName));
            if (!f.UninstallOemInf()) return false;
            if (inf.CopyDriversSection != null && !f.UninstallFiles(inf.CopyDriversSection)) return false;
        }

        return InfFile.UninstallAll(_ui, InfUninstallRules, null);
    }

    private void DeleteKeyRetry(RegistryKey root, string path, string what)
    {
        while (true)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null)
                    {
                        _ui.Line($"{what} not installed");
                        return;
                    }
                }
                root.DeleteSubKeyTree(path, false);
                _ui.Line($"Deleted {what}");
                return;
            }
            catch (Exception e)
            {
                var r = _ui.Show(Buttons.CancelTryContinue, $"Delete HKLM\\{path}\nERROR: {e.Message}", warning: true);
                if (r == Choice.TryAgain) continue;
                _ui.Line($"WARNING: Key {path} not deleted");
                return;
            }
        }
    }

    private bool DeleteDriverService()
    {
        while (true)
        {
            IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                if (_ui.ShowLastError(Buttons.CancelTryContinue, "OpenSCManager()") == Choice.TryAgain) continue;
                return false;
            }
            try
            {
                IntPtr srv = OpenServiceW(scm, C0C.Service, DELETE | SERVICE_QUERY_STATUS);
                if (srv == IntPtr.Zero)
                {
                    if (Marshal.GetLastWin32Error() == ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        _ui.Line($"Service {C0C.Service} not installed");
                        return true;
                    }
                    if (_ui.ShowLastError(Buttons.CancelTryContinue, $"OpenService({C0C.Service})") == Choice.TryAgain) continue;
                    return false;
                }
                try
                {
                    if (!QueryServiceStatus(srv, out var st))
                    {
                        if (_ui.ShowLastError(Buttons.CancelTryContinue, $"QueryServiceStatus({C0C.Service})") == Choice.TryAgain) continue;
                        return false;
                    }
                    if (st.dwCurrentState != SERVICE_STOPPED)
                    {
                        var r = _ui.Show(Buttons.CancelTryContinue, $"Service {C0C.Service} is not stopped (state {st.dwCurrentState}).\n");
                        if (r == Choice.TryAgain) continue;
                        _ui.Line($"WARNING: Service {C0C.Service} not deleted");
                        return r == Choice.Continue;
                    }

                    // In silent mode the service is deleted without asking (as in the original).
                    var ask = _ui.Show(Buttons.YesNo,
                        $"The deleting {C0C.Service} service will remove your manual settings.\nWould you like to delete service?\n");
                    if (ask is Choice.Yes or Choice.None)
                    {
                        if (DeleteService(srv))
                        {
                            _ui.Line($"Deleted Service {C0C.Service}");
                            return true;
                        }
                        if (_ui.ShowLastError(Buttons.CancelTryContinue, $"DeleteService({C0C.Service})") == Choice.TryAgain) continue;
                        return false;
                    }
                    _ui.Line($"WARNING: Service {C0C.Service} not deleted");
                    return true;
                }
                finally
                {
                    CloseServiceHandle(srv);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }
    }

    /// <summary>Uninstalls stale com0com INF files from %windir%\INF (keeps the current ones).</summary>
    public bool InfClean()
    {
        if (!CheckInfFiles()) return false;
        var exclude = new List<string>();
        foreach (var inf in InfInstallList)
        {
            using var f = new InfFile(_ui, InfPath(inf.InfName));
            if (f.OemPath is { } p) exclude.Add(p);
        }
        if (!InfFile.UninstallAll(_ui, InfUninstallRules, exclude))
        {
            _ui.Line("\nCleaning not completed!");
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ names / waiting

    /// <summary>Names already in use (ComDB + DOS devices) that match a '*'/'?' pattern.</summary>
    public List<string> BusyNames(string pattern = "*")
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pattern = pattern.ToUpperInvariant();

        IEnumerable<string> dos = [];
        var buf = new char[64 * 1024];
        for (int tries = 0; tries < 6; tries++, buf = new char[buf.Length * 2])
        {
            uint n = QueryDosDeviceW(null, buf, (uint)buf.Length);
            if (n != 0)
            {
                dos = new string(buf, 0, (int)n).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                break;
            }
            if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER) break;
        }

        foreach (var raw in _comDb.QueryNames().Concat(dos))
        {
            if (raw.IndexOfAny(":#&$?!{}()[]/\\ \t\r\n".ToCharArray()) >= 0) continue;
            string name = raw.ToUpperInvariant();
            if (MatchPattern(pattern, name) && seen.Add(name)) result.Add(name);
        }
        result.Sort(NaturalCompare);
        return result;
    }

    /// <summary>Orders COM2 before COM10.</summary>
    public static int NaturalCompare(string? x, string? y)
    {
        x ??= ""; y ??= "";
        int px = x.Length, py = y.Length;
        while (px > 0 && char.IsAsciiDigit(x[px - 1])) px--;
        while (py > 0 && char.IsAsciiDigit(y[py - 1])) py--;
        int c = string.Compare(x[..px], y[..py], StringComparison.OrdinalIgnoreCase);
        if (c != 0 || px == x.Length || py == y.Length) return c != 0 ? c : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        return long.Parse(x[px..]).CompareTo(long.Parse(y[py..]));
    }

    public static bool MatchPattern(string pattern, string s)
    {
        int p = 0, i = 0, starP = -1, starI = 0;
        while (i < s.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == s[i])) { p++; i++; }
            else if (p < pattern.Length && pattern[p] == '*') { starP = p++; starI = i; }
            else if (starP >= 0) { p = starP + 1; i = ++starI; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>Waits for PnP to finish pending installs. seconds &lt; 0 = forever.</summary>
    public bool WaitForInstallCompletion(int seconds, bool askToContinue) =>
        _dev.WaitNoPendingInstallEvents(seconds, askToContinue);
}
