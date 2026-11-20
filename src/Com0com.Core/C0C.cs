namespace Com0com.Core;

/// <summary>Constants shared with the driver (include/com0com.h).</summary>
public static class C0C
{
    public const string BusDeviceId = @"root\com0com";
    public const string PortDeviceId = @"com0com\port";
    public const string PortHwIdCncClass = @"com0com\cncport";
    public const string PortHwIdComClass = @"com0com\comport";
    public const string Service = "com0com";

    public const int PortNameLen = 12;
    public const string PrefBusName = "CNCBUS";
    public const string PrefPortNameA = "CNCA";
    public const string PrefPortNameB = "CNCB";
    public const string PrefDeviceNameA = @"\Device\com0com1";
    public const string PrefDeviceNameB = @"\Device\com0com2";
    public const string PortNameComClass = "COM#";
    public const string RegValPortNum = "PortNum";

    public const string InfName = "com0com.inf";
    public const string InfNameCncPort = "cncport.inf";
    public const string InfNameComPort = "comport.inf";
    public const string CncClassGuid = "{df799e12-3c56-421b-b298-b6d3642bc878}";
    public const string ComClassGuid = "{4d36e978-e325-11ce-bfc1-08002be10318}";
    public const string Provider = "Vyacheslav Frolov";
    public const string CopyDriversSection = "com0com_CopyDrivers";
    public const string RegKeyServices = @"SYSTEM\CurrentControlSet\Services";
    public const string RegKeyEventLog = RegKeyServices + @"\Eventlog\System\" + Service;

    // Pin wiring bits
    public const uint PinRRTS = 0x00000001;
    public const uint PinRDTR = 0x00000002;
    public const uint PinROUT1 = 0x00000004;
    public const uint PinROUT2 = 0x00000008;
    public const uint PinROPEN = 0x00000080;
    public const uint PinLRTS = 0x00000100;
    public const uint PinLDTR = 0x00000200;
    public const uint PinLOUT1 = 0x00000400;
    public const uint PinLOUT2 = 0x00000800;
    public const uint PinLOPEN = 0x00008000;
    public const uint PinON = 0x10000000;
    public const uint PinNegative = 0x80000000;

    public const uint ProbabilityOne = 100000000;

    public static string PortId(bool isA, int num) => (isA ? PrefPortNameA : PrefPortNameB) + num;
    public static string DeviceName(bool isA, int num) => (isA ? PrefDeviceNameA : PrefDeviceNameB) + num;

    public static bool IsComClass(string? portName) =>
        string.Equals(portName, PortNameComClass, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses CNCA&lt;n&gt; / CNCB&lt;n&gt;.</summary>
    public static bool TryParsePortId(string id, out bool isA, out int num)
    {
        isA = false;
        num = -1;
        if (id.StartsWith(PrefPortNameA, StringComparison.OrdinalIgnoreCase)) isA = true;
        else if (!id.StartsWith(PrefPortNameB, StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(id.AsSpan(4), System.Globalization.NumberStyles.None, null, out num);
    }
}
