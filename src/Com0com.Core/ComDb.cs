using System.Globalization;
using Microsoft.Win32;
using static Com0com.Core.Native.NativeMethods;

namespace Com0com.Core;

/// <summary>
/// Port of setup/comdb.cpp. Keeps the system COM port database (msports ComDB) in sync with
/// COM&lt;n&gt; names used by com0com ports, recording our own claims under
/// HKLM\...\Services\com0com\COM Name Arbiter\ComDB so that we only release what we claimed.
/// </summary>
internal sealed class ComDb(ISetupUi ui)
{
    private const string LocalKey = C0C.RegKeyServices + @"\" + C0C.Service + @"\COM Name Arbiter";
    private const string LocalName = "ComDB";

    /// <summary>COM&lt;n&gt; → n (1..4096), anything else → 0.</summary>
    public static int NameToNum(string? portName)
    {
        if (portName == null || portName.Length < 4 || !portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || portName[3] == '0')
            return 0;
        if (!int.TryParse(portName.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int num))
            return 0;
        return num is > 0 and <= COMDB_MAX_PORTS_ARBITRATED ? num : 0;
    }

    private static HashSet<int> FromBits(byte[] buf, uint maxPorts)
    {
        var set = new HashSet<int>();
        for (int n = 0; n < maxPorts && n / 8 < buf.Length; n++)
            if (((buf[n / 8] >> (n % 8)) & 1) != 0) set.Add(n);
        return set;
    }

    /// <summary>Zero-based numbers of COM ports logged "in use" in the system ComDB, or null.</summary>
    public HashSet<int>? LoadSystem(bool showErrors = true)
    {
        while (true)
        {
            int err = ComDBOpen(out IntPtr h);
            if (err != ERROR_SUCCESS)
            {
                if (showErrors && ui.ShowError(Buttons.CancelTryContinue, err, "ComDBOpen()") == Choice.TryAgain) continue;
                return null;
            }
            try
            {
                err = ComDBGetCurrentPortUsage(h, null, 0, CDB_REPORT_BITS, out uint maxPorts);
                if (err == ERROR_SUCCESS)
                {
                    var buf = new byte[(maxPorts + 7) / 8];
                    err = ComDBGetCurrentPortUsage(h, buf, (uint)buf.Length, CDB_REPORT_BITS, out maxPorts);
                    if (err == ERROR_SUCCESS) return FromBits(buf, maxPorts);
                }
            }
            finally
            {
                ComDBClose(h);
            }
            if (!showErrors || ui.ShowError(Buttons.CancelTryContinue, err, "ComDBGetCurrentPortUsage()") != Choice.TryAgain)
                return null;
        }
    }

    private bool ClaimRelease(int num, bool claim)
    {
        if (num == 0) return false;
        while (true)
        {
            int err = ComDBOpen(out IntPtr h);
            if (err != ERROR_SUCCESS)
            {
                if (ui.ShowError(Buttons.RetryCancel, err, "ComDBOpen()") == Choice.Retry) continue;
                return false;
            }
            try
            {
                err = claim ? ComDBClaimPort(h, (uint)num, false, IntPtr.Zero) : ComDBReleasePort(h, (uint)num);
            }
            finally
            {
                ComDBClose(h);
            }
            if (err == ERROR_SUCCESS)
            {
                ui.Line(claim ? $"ComDB: COM{num} - logged as \"in use\"" : $"ComDB: COM{num} - released");
                return true;
            }
            if (claim && err == ERROR_SHARING_VIOLATION) return false;
            if (ui.ShowError(Buttons.RetryCancel, err, claim ? $"ComDBClaimPort(COM{num})" : $"ComDBReleasePort(COM{num})") != Choice.Retry)
                return false;
        }
    }

    private static HashSet<int> LoadLocal()
    {
        using var key = Registry.LocalMachine.OpenSubKey(LocalKey);
        return key?.GetValue(LocalName) is byte[] buf ? FromBits(buf, (uint)buf.Length * 8) : [];
    }

    private static void SaveLocal(HashSet<int> set)
    {
        using var key = Registry.LocalMachine.CreateSubKey(LocalKey, true);
        int max = set.Count == 0 ? 0 : set.Where(n => n < COMDB_MAX_PORTS_ARBITRATED).DefaultIfEmpty(-1).Max() + 1;
        if (max == 0)
        {
            key.DeleteValue(LocalName, false);
            return;
        }
        var buf = new byte[(max + 7) / 8];
        foreach (int n in set) if (n < max) buf[n / 8] |= (byte)(1 << (n % 8));
        key.SetValue(LocalName, buf, RegistryValueKind.Binary);
    }

    public bool GetInUse(string portName, out bool inUse)
    {
        inUse = false;
        int num = NameToNum(portName);
        if (num == 0) return true; // not arbitrated by ComDB
        var set = LoadSystem();
        if (set == null) return false;
        inUse = set.Contains(num - 1);
        return true;
    }

    /// <summary>Claims/releases ComDB entries so they match the COM names of the current com0com ports.</summary>
    public void Sync(IEnumerable<string> usedPortNames)
    {
        var names = new HashSet<int>();
        foreach (var n in usedPortNames)
        {
            int num = NameToNum(n);
            if (num > 0) names.Add(num - 1);
        }

        var local = LoadLocal();
        for (int n = 0; n < COMDB_MAX_PORTS_ARBITRATED; n++)
        {
            bool used = names.Contains(n), claimed = local.Contains(n);
            if (!used && claimed)
            {
                if (ClaimRelease(n + 1, false))
                {
                    local.Remove(n);
                    SaveLocal(local);
                }
            }
            else if (used && !claimed)
            {
                if (ClaimRelease(n + 1, true))
                {
                    local.Add(n);
                    SaveLocal(local);
                }
            }
        }
    }

    public IEnumerable<string> QueryNames() =>
        (LoadSystem(showErrors: false) ?? []).Order().Select(n => "COM" + (n + 1).ToString(CultureInfo.InvariantCulture));

    public bool Claim(string portName) => ClaimRelease(NameToNum(portName), true);
    public bool Release(string portName) => ClaimRelease(NameToNum(portName), false);

    public bool IsValidName(string portName)
    {
        if (NameToNum(portName) != 0) return true;
        ui.Show(Buttons.Ok, $"The port name {portName} is not in COM<1-{COMDB_MAX_PORTS_ARBITRATED}> format.\n");
        return false;
    }
}
