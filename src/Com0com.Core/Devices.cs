using System.Runtime.InteropServices;
using System.Text;
using Com0com.Core.Native;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using static Com0com.Core.Native.NativeMethods;

namespace Com0com.Core;

/// <summary>Identifying properties of a device node (hardware id, location, PDO name).</summary>
public sealed record DevProperties(string? DevId = null, string? Location = null, string? PhObjName = null)
{
    public override string ToString() => $"{Location} {DevId} {PhObjName}";
}

internal enum Step { Continue, Cancel, TryAgain }

/// <summary>Mutable "reboot required" accumulator (BOOL *pRebootRequired in the original).</summary>
internal sealed class RebootFlag
{
    public bool Required;
}

/// <summary>A device inside an enumeration; valid only during the callback.</summary>
internal sealed class Device(IntPtr set, SP_DEVINFO_DATA data, DevProperties props)
{
    private SP_DEVINFO_DATA _data = data;
    public IntPtr Set { get; } = set;
    public DevProperties Props { get; } = props;
    public uint DevInst => _data.DevInst;
    public ref SP_DEVINFO_DATA Data => ref _data;

    public string? GetStringProperty(uint property) => Devices.GetStringProperty(Set, ref _data, property);
    public string[]? GetMultiStringProperty(uint property) => Devices.GetMultiStringProperty(Set, ref _data, property);

    public bool SetStringProperty(uint property, string value)
    {
        byte[] buf = Encoding.Unicode.GetBytes(value + "\0");
        return SetupDiSetDeviceRegistryPropertyW(Set, ref _data, property, buf, (uint)buf.Length);
    }

    /// <summary>Opens the device hardware key (DIREG_DEV), or null.</summary>
    public RegistryKey? OpenDevKey(bool writable)
    {
        IntPtr h = SetupDiOpenDevRegKey(Set, ref _data, DICS_FLAG_GLOBAL, 0, DIREG_DEV, writable ? KEY_READ | KEY_WRITE : KEY_READ);
        return h == INVALID_HANDLE_VALUE ? null : RegistryKey.FromHandle(new SafeRegistryHandle(h, true));
    }

    public RegistryKey? CreateDevKey()
    {
        IntPtr h = SetupDiCreateDevRegKeyW(Set, ref _data, DICS_FLAG_GLOBAL, 0, DIREG_DEV, IntPtr.Zero, null);
        return h == INVALID_HANDLE_VALUE ? null : RegistryKey.FromHandle(new SafeRegistryHandle(h, true));
    }

    /// <summary>Bus number stored in the bus device key (PortNum), or -1.</summary>
    public int PortNum
    {
        get
        {
            using var key = OpenDevKey(false);
            return key?.GetValue(C0C.RegValPortNum) is int n ? n : -1;
        }
    }
}

/// <summary>Port of setup/devutils.cpp.</summary>
internal sealed class Devices(ISetupUi ui)
{
    /// <summary>Only com0com devices are ever touched (first hardware id must be ours).</summary>
    private static bool Filter(string firstHardwareId) =>
        firstHardwareId.Equals(C0C.BusDeviceId, StringComparison.OrdinalIgnoreCase) ||
        firstHardwareId.Equals(C0C.PortDeviceId, StringComparison.OrdinalIgnoreCase);

    internal static byte[]? GetPropertyBytes(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, null, 0, out uint size);
        if (size == 0) return null;
        var buf = new byte[size];
        return SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buf, size, out _) ? buf : null;
    }

    internal static string? GetStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        var buf = GetPropertyBytes(set, ref data, property);
        return buf == null ? null : Encoding.Unicode.GetString(buf).Split('\0')[0];
    }

    internal static string[]? GetMultiStringProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
    {
        var buf = GetPropertyBytes(set, ref data, property);
        return buf == null ? null : Encoding.Unicode.GetString(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Enumerates com0com devices matching <paramref name="match"/> (null fields match anything).
    /// Returns the last callback result (Continue if all processed).
    /// </summary>
    private Step Enum(DevProperties? match, bool presentOnly, Func<Device, Step> routine)
    {
        IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | (presentOnly ? DIGCF_PRESENT : 0));
        if (set == INVALID_HANDLE_VALUE)
        {
            ui.ShowLastError(Buttons.Ok, "SetupDiGetClassDevs()");
            return Step.Cancel;
        }

        try
        {
            var res = Step.Continue;
            var data = SP_DEVINFO_DATA.Create();
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var hwIds = GetMultiStringProperty(set, ref data, SPDRP_HARDWAREID) ?? ["UNKNOWN HARDWAREID"];
                if (hwIds.Length == 0 || !Filter(hwIds[0])) continue;

                string? devId = hwIds[0];
                if (match?.DevId != null)
                {
                    devId = hwIds.FirstOrDefault(h => h.Equals(match.DevId, StringComparison.OrdinalIgnoreCase));
                    if (devId == null) continue;
                }

                string? location = GetStringProperty(set, ref data, SPDRP_LOCATION_INFORMATION);
                if (match?.Location != null && !string.Equals(location, match.Location, StringComparison.OrdinalIgnoreCase))
                    continue;

                string? phName = GetStringProperty(set, ref data, SPDRP_PHYSICAL_DEVICE_OBJECT_NAME);
                if (match?.PhObjName != null && !string.Equals(phName, match.PhObjName, StringComparison.OrdinalIgnoreCase))
                    continue;

                res = routine(new Device(set, data, new DevProperties(devId, location, phName)));
                if (res != Step.Continue) break;
            }
            return res;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Calls <paramref name="callback"/> for each present matching device. Returns count or -1 if aborted.</summary>
    public int EnumDevices(DevProperties? match, Func<Device, bool> callback)
    {
        int count = 0;
        var res = Enum(match, true, dev =>
        {
            if (!callback(dev)) return Step.Cancel;
            count++;
            return Step.Continue;
        });
        return res == Step.Continue ? count : -1;
    }

    private static bool NeedsRestart(Device dev) =>
        CM_Get_DevNode_Status(out uint status, out _, dev.DevInst, 0) == CR_SUCCESS && (status & DN_NEED_RESTART) != 0;

    private bool UpdateRebootRequired(Device dev, RebootFlag? reboot)
    {
        if (reboot == null || reboot.Required) return true;
        if (NeedsRestart(dev))
        {
            reboot.Required = true;
            return true;
        }
        var p = new SP_DEVINSTALL_PARAMS { cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS>(), DriverPath = "" };
        if (!SetupDiGetDeviceInstallParamsW(dev.Set, ref dev.Data, ref p))
        {
            ui.ShowLastError(Buttons.Ok, "SetupDiGetDeviceInstallParams()");
            return false;
        }
        reboot.Required = (p.Flags & (DI_NEEDREBOOT | DI_NEEDRESTART)) != 0;
        return true;
    }

    private bool ChangeState(Device dev, uint stateChange)
    {
        var p = new SP_PROPCHANGE_PARAMS
        {
            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
            {
                cbSize = (uint)Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                InstallFunction = DIF_PROPERTYCHANGE,
            },
            StateChange = stateChange,
            Scope = DICS_FLAG_CONFIGSPECIFIC,
            HwProfile = 0,
        };
        if (!SetupDiSetClassInstallParamsW(dev.Set, ref dev.Data, ref p, (uint)Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
        {
            ui.ShowLastError(Buttons.Ok, "SetupDiSetClassInstallParams()");
            return false;
        }
        if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, dev.Set, ref dev.Data))
        {
            ui.ShowLastError(Buttons.Ok, "SetupDiCallClassInstaller(DIF_PROPERTYCHANGE)");
            return false;
        }
        return true;
    }

    private static (uint Status, uint Problem)? Status(Device dev) =>
        CM_Get_DevNode_Status(out uint s, out uint p, dev.DevInst, 0) == CR_SUCCESS ? (s, p) : null;

    private static bool IsDisabled(Device dev) =>
        Status(dev) is { } st && (st.Status & DN_HAS_PROBLEM) != 0 && st.Problem == CM_PROB_DISABLED;

    private static bool IsEnabled(Device dev) =>
        Status(dev) is { } st && (st.Status & (DN_HAS_PROBLEM | DN_NEED_RESTART)) == 0;

    /// <summary>Disables one device. Returns Continue on success, otherwise Cancel/TryAgain.</summary>
    public Step DisableDevice(Device dev, RebootFlag? reboot, List<DevProperties>? disabled)
    {
        if (IsDisabled(dev)) return Step.Continue;

        var local = new RebootFlag();
        if (!UpdateRebootRequired(dev, local)) return Step.Cancel;
        if (!ChangeState(dev, DICS_DISABLE)) return Step.Cancel;
        ui.Line($"Disabled {dev.Props}");

        if (!local.Required)
        {
            if (!UpdateRebootRequired(dev, local)) return Step.Cancel;
            if (local.Required)
            {
                ui.Line($"Can't stop device {dev.Props}");
                var res = ui.Show(Buttons.CancelTryContinue,
                    $"Can't stop device {dev.Props}.\n" +
                    "Close application that use this device and Try Again.\n" +
                    "Or Continue and then reboot system.\n");
                if (res == Choice.None) res = Choice.Continue;
                if (res != Choice.Continue)
                {
                    if (!ChangeState(dev, DICS_ENABLE)) return Step.Cancel;
                    ui.Line($"Enabled {dev.Props}");
                    return res == Choice.TryAgain ? Step.TryAgain : Step.Cancel;
                }
                if (reboot != null) reboot.Required = true;
            }
        }
        disabled?.Add(dev.Props);
        return Step.Continue;
    }

    public bool DisableDevices(DevProperties match, RebootFlag? reboot, List<DevProperties>? disabled)
    {
        Step res;
        do res = Enum(match, true, d => DisableDevice(d, reboot, disabled));
        while (res == Step.TryAgain);
        if (res != Step.Continue) return false;
        if (!WaitNoPendingInstallEvents(10)) Thread.Sleep(1000);
        return true;
    }

    public bool EnableDevices(DevProperties match, RebootFlag? reboot)
    {
        var res = Enum(match, true, dev =>
        {
            if (IsEnabled(dev)) return Step.Continue;
            if (ChangeState(dev, DICS_ENABLE)) ui.Line($"Enabled {dev.Props}");
            UpdateRebootRequired(dev, reboot);
            return Step.Continue;
        });
        return res == Step.Continue;
    }

    public bool RestartDevices(DevProperties match, RebootFlag? reboot)
    {
        Step res;
        do
        {
            res = Enum(match, true, dev =>
            {
                if (!ChangeState(dev, DICS_PROPCHANGE)) return Step.Cancel;
                var local = new RebootFlag();
                if (!UpdateRebootRequired(dev, local)) return Step.Cancel;
                if (!local.Required)
                {
                    ui.Line($"Restarted {dev.Props}");
                    return Step.Continue;
                }
                ui.Line($"Can't restart device {dev.Props}");
                if (reboot != null && !reboot.Required)
                {
                    var r = ui.Show(Buttons.CancelTryContinue,
                        $"Can't restart device {dev.Props}.\n" +
                        "Close application that use this device and Try Again.\n" +
                        "Or Continue and then reboot system.\n");
                    if (r == Choice.None) r = Choice.Continue;
                    if (r != Choice.Continue)
                    {
                        if (!ChangeState(dev, DICS_ENABLE)) return Step.Cancel;
                        return r == Choice.TryAgain ? Step.TryAgain : Step.Cancel;
                    }
                    reboot.Required = true;
                }
                return Step.Continue;
            });
        } while (res == Step.TryAgain);
        return res == Step.Continue;
    }

    public bool RemoveDevice(Device dev, RebootFlag? reboot)
    {
        if (!SetupDiCallClassInstaller(DIF_REMOVE, dev.Set, ref dev.Data))
        {
            ui.ShowLastError(Buttons.Ok, $"SetupDiCallClassInstaller(DIF_REMOVE, {dev.Props.DevId}, {dev.Props.PhObjName})");
            return false;
        }
        ui.Line($"Removed {dev.Props}");
        return UpdateRebootRequired(dev, reboot);
    }

    /// <summary>Removes all matching devices, including non-present ones.</summary>
    public bool RemoveDevices(DevProperties? match, RebootFlag? reboot)
    {
        int count = 0;
        var res = Enum(match, false, dev =>
        {
            if (!RemoveDevice(dev, reboot)) return Step.Cancel;
            count++;
            return Step.Continue;
        });
        if (res != Step.Continue) return false;
        if (count == 0) ui.Line("No devices found");
        return true;
    }

    public static bool Reenumerate(Device dev) => CM_Reenumerate_DevNode(dev.DevInst, 0) == CR_SUCCESS;

    /// <summary>Wrapper for UpdateDriverForPlugAndPlayDevices with retries on sharing violation.</summary>
    public Choice UpdateDriver(string infPath, string hardwareId, uint flags, bool mandatory, RebootFlag? reboot)
    {
        int updateErr = ERROR_SUCCESS;
        for (int i = 0; i < 10; i++)
        {
            if (UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hardwareId, infPath, flags, out bool rb))
            {
                if (rb && reboot != null) reboot.Required = true;
                updateErr = ERROR_SUCCESS;
            }
            else
            {
                updateErr = Marshal.GetLastWin32Error();
                if (updateErr == ERROR_SHARING_VIOLATION)
                {
                    ui.Write(".");
                    Thread.Sleep(1000);
                    continue;
                }
                if (!mandatory &&
                    (updateErr == ERROR_NO_SUCH_DEVINST ||
                     (updateErr == ERROR_NO_MORE_ITEMS && (flags & INSTALLFLAG_FORCE) == 0)))
                {
                    updateErr = ERROR_SUCCESS;
                }
            }
            if (i > 0) ui.Line();
            break;
        }

        if (updateErr == ERROR_SUCCESS) return Choice.Continue;

        if (updateErr == ERROR_FILE_NOT_FOUND)
        {
            const string runOnce = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
            using var key = Registry.LocalMachine.OpenSubKey(runOnce);
            if (key == null)
            {
                var res = ui.Show(Buttons.CancelTryContinue,
                    "Can't update driver. Possible it's because your Windows registry is corrupted and\n" +
                    $"there is not the following key:\n\nHKEY_LOCAL_MACHINE\\{runOnce}\n\nContinue to add the key to the registry.\n");
                if (res == Choice.Continue)
                {
                    Registry.LocalMachine.CreateSubKey(runOnce)?.Dispose();
                    return Choice.TryAgain;
                }
                return res == Choice.None ? Choice.Cancel : res;
            }
        }

        if (updateErr == ERROR_IN_WOW64)
            ui.Line("This program must be run as a 64-bit process on 64-bit Windows.");

        return ui.ShowError(Buttons.CancelTryContinue, updateErr,
            $"UpdateDriverForPlugAndPlayDevices(\"{hardwareId}\", \"{infPath}\", 0x{flags:X})");
    }

    /// <summary>Creates a root-enumerated device node and (optionally) installs its driver.</summary>
    public bool InstallDevice(string infPath, string devId, Func<Device, bool>? onRegistered, bool updateDriver, RebootFlag? reboot)
    {
        Step res;
        do res = TryInstallDevice(infPath, devId, onRegistered, updateDriver, reboot);
        while (res == Step.TryAgain);
        return res == Step.Continue;
    }

    private Step TryInstallDevice(string infPath, string devId, Func<Device, bool>? onRegistered, bool updateDriver, RebootFlag? reboot)
    {
        var className = new StringBuilder(64);
        if (!SetupDiGetINFClassW(infPath, out Guid classGuid, className, (uint)className.Capacity, out _))
        {
            ui.ShowLastError(Buttons.Ok, $"SetupDiGetINFClass({infPath})");
            return Step.Cancel;
        }

        IntPtr set = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (set == INVALID_HANDLE_VALUE)
        {
            ui.ShowLastError(Buttons.Ok, "SetupDiCreateDeviceInfoList()");
            return Step.Cancel;
        }

        try
        {
            if (!devId.StartsWith(@"root\", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only root-enumerated devices can be created", nameof(devId));

            var data = SP_DEVINFO_DATA.Create();
            if (!SetupDiCreateDeviceInfoW(set, devId[5..], ref classGuid, null, IntPtr.Zero, DICD_GENERATE_ID, ref data))
            {
                ui.ShowLastError(Buttons.Ok, "SetupDiCreateDeviceInfo()");
                return Step.Cancel;
            }

            byte[] hwId = Encoding.Unicode.GetBytes(devId + "\0\0");
            if (!SetupDiSetDeviceRegistryPropertyW(set, ref data, SPDRP_HARDWAREID, hwId, (uint)hwId.Length))
            {
                ui.ShowLastError(Buttons.Ok, "SetupDiSetDeviceRegistryProperty()");
                return Step.Cancel;
            }

            if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, set, ref data))
            {
                ui.ShowLastError(Buttons.Ok, "SetupDiCallClassInstaller(DIF_REGISTERDEVICE)");
                return Step.Cancel;
            }

            var res = Step.Continue;
            var dev = new Device(set, data, new DevProperties(devId));
            if (onRegistered != null && !onRegistered(dev))
                res = Step.Cancel;

            if (res == Step.Continue && updateDriver)
            {
                var r = UpdateDriver(infPath, devId, 0, true, reboot);
                res = r == Choice.Continue ? Step.Continue : r == Choice.TryAgain ? Step.TryAgain : Step.Cancel;
            }

            if (res != Step.Continue && !SetupDiCallClassInstaller(DIF_REMOVE, set, ref dev.Data))
                ui.ShowLastError(Buttons.Ok, "SetupDiCallClassInstaller(DIF_REMOVE)", warning: true);

            return res;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>
    /// Waits until PnP has no pending installation activities.
    /// <paramref name="seconds"/> &lt; 0 waits forever. If <paramref name="askToContinue"/>, asks the user on timeout.
    /// </summary>
    public bool WaitNoPendingInstallEvents(int seconds, bool askToContinue = false)
    {
        bool inTrace = false;
        var start = Environment.TickCount64;
        for (int count = 0; ;)
        {
            uint res = CMP_WaitNoPendingInstallEvents(0);
            if (res == WAIT_OBJECT_0)
            {
                if (++count < 5)
                {
                    Thread.Sleep(100);
                    continue;
                }
                if (inTrace) ui.Line(" OK");
                return true;
            }
            count = 0;
            if (res != WAIT_TIMEOUT)
            {
                ui.ShowLastError(Buttons.Ok, "CMP_WaitNoPendingInstallEvents()", warning: true);
                if (inTrace) ui.Line(" FAIL");
                return false;
            }

            if (seconds >= 0 && Environment.TickCount64 - start >= seconds * 1000L)
            {
                if (inTrace)
                {
                    ui.Line(" timeout");
                    inTrace = false;
                }
                if (askToContinue)
                {
                    if (ui.Show(Buttons.YesNo, "The device installation activities are still pending.\nContinue to wait?\n") == Choice.Yes)
                        start = Environment.TickCount64;
                    else
                        askToContinue = false;
                    continue;
                }
                return false;
            }

            if (!inTrace)
            {
                ui.Write(seconds >= 0
                    ? $"Waiting for no pending device installation activities ({seconds} secs) "
                    : "Waiting for no pending device installation activities (perpetually) ");
                inTrace = true;
            }
            Thread.Sleep(1000);
            ui.Write(".");
        }
    }
}
